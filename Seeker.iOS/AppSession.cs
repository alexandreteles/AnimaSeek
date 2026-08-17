using System.Collections.Concurrent;
using AnimaSeek.iOS.Services;
using Common;
using Common.Messages;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Seeker.Transfers;
using Soulseek;

namespace AnimaSeek.iOS;

/// <summary>
/// Owns the Soulseek client for the iOS process and exposes small async operations to the UIKit controllers.
/// </summary>
internal sealed class AppSession : IDisposable
{
    private const int MaxRecentTerminalTransfers = 100;
    private readonly IKeyValueStore keyValueStore;
    private readonly IosFileSystemService fileSystem;
    private readonly IosNotifier notifier;
    private readonly IMainThreadRunner mainThreadRunner;
    private readonly Action checkpointTransfers;
    private readonly Func<bool> tryCheckpointTransfers;
    private readonly PrivateMessageSessionState privateMessages;
    private readonly PortableAppDataStateService appDataStateService;
    private readonly IosInterruptedDownloadStore interruptedDownloads;
    private readonly SemaphoreSlim loginGate = new(1, 1);
    private readonly Lock authenticationSync = new();
    private readonly Lock downloadStartSync = new();
    private readonly ConcurrentDictionary<(TransferDirection Direction, int Token), Transfer> transfers = new();
    private readonly ConcurrentDictionary<TransferFileKey, byte> pendingUserDownloads = new();
    private readonly ConcurrentDictionary<(TransferDirection Direction, int Token), byte> userInitiatedTransferTokens = new();
    private readonly ConcurrentDictionary<(TransferDirection Direction, int Token), NotificationTransferState>
        notifiedTransferStates = new();
    private readonly ConcurrentDictionary<(string Username, string FolderName), byte> notifiedCompletedFolders = new();
    private readonly ConcurrentQueue<Transfer> recentTerminalTransfers = new();
    private CancellationTokenSource? activeAuthenticationCancellation;
    private CancellationTokenSource? foregroundReconnectCancellation;
    private long authenticationGeneration;
    private int nextLocalMessageId;
    private int intentionalDisconnectDepth;
    private bool automaticReconnectBlocked;
    private bool foregroundReconnectAllowed;
    private volatile bool disposed;

    /// <summary>Creates a process session and subscribes to client lifecycle, transfer, and messaging events.</summary>
    /// <param name="client">The real or mock Soulseek client selected by the composition root.</param>
    /// <param name="keyValueStore">The store used to restore credentials and login intent.</param>
    /// <param name="fileSystem">The sandbox file service used for direct downloads.</param>
    /// <param name="notifier">The local-notification bridge.</param>
    /// <param name="mainThreadRunner">The service used to deliver observable changes to UIKit.</param>
    /// <param name="privateMessages">The restored, account-scoped private-message state.</param>
    /// <param name="appDataStateService">Persists private-message roots and read positions.</param>
    /// <param name="interruptedDownloads">Persists crash-resume descriptors before foreground download I/O.</param>
    /// <param name="checkpointTransfers">Persists portable transfer state after file-lifecycle transitions.</param>
    /// <param name="tryCheckpointTransfers">Persists critical terminal state and reports whether it became durable.</param>
    /// <param name="toaster">The user-facing transient-message service.</param>
    /// <param name="logger">The diagnostics backend.</param>
    public AppSession(
        ISoulseekClient client,
        IKeyValueStore keyValueStore,
        IosFileSystemService fileSystem,
        IosNotifier notifier,
        IMainThreadRunner mainThreadRunner,
        PrivateMessageSessionState privateMessages,
        PortableAppDataStateService appDataStateService,
        IosInterruptedDownloadStore interruptedDownloads,
        Action checkpointTransfers,
        Func<bool> tryCheckpointTransfers,
        IToaster toaster,
        ILoggerBackend logger)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
        this.privateMessages = privateMessages ?? throw new ArgumentNullException(nameof(privateMessages));
        this.appDataStateService = appDataStateService ?? throw new ArgumentNullException(nameof(appDataStateService));
        this.interruptedDownloads = interruptedDownloads ?? throw new ArgumentNullException(nameof(interruptedDownloads));
        this.checkpointTransfers = checkpointTransfers ?? throw new ArgumentNullException(nameof(checkpointTransfers));
        this.tryCheckpointTransfers = tryCheckpointTransfers ?? throw new ArgumentNullException(nameof(tryCheckpointTransfers));
        Toaster = toaster ?? throw new ArgumentNullException(nameof(toaster));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

        RestoreLoginIntent();
        privateMessages.SwitchAccount(PreferencesState.CurrentlyLoggedIn ? PreferencesState.Username : null);
        privateMessages.Changed += OnPrivateMessageStateChanged;
        Client.StateChanged += OnClientStateChanged;
        Client.TransferStateChanged += OnTransferChanged;
        Client.TransferProgressUpdated += OnTransferChanged;
        Client.PrivateMessageReceived += OnPrivateMessageReceived;
        notifier.DirectReplySubmitted += OnDirectReplySubmitted;
    }

    /// <summary>Gets the selected real or mock Soulseek client.</summary>
    public ISoulseekClient Client { get; }

    /// <summary>Gets the transient-message service used by session adapters and screens.</summary>
    public IToaster Toaster { get; }

    /// <summary>Gets the shared diagnostics backend.</summary>
    public ILoggerBackend Logger { get; }

    /// <summary>Gets whether the client currently has both a server connection and an authenticated session.</summary>
    public bool IsConnected =>
        Client.State.HasFlag(SoulseekClientStates.Connected) && Client.State.HasFlag(SoulseekClientStates.LoggedIn);

    /// <summary>Gets whether credentials were persisted after a successful login.</summary>
    public bool HasStoredCredentials =>
        !string.IsNullOrWhiteSpace(PreferencesState.Username) && PreferencesState.Password is not null;

    /// <summary>Gets the connected or most recently authenticated username without exposing stored credentials.</summary>
    public string? Username =>
        IsConnected && !string.IsNullOrWhiteSpace(Client.Username)
            ? Client.Username
            : PreferencesState.Username;

    /// <summary>Gets a presentation-safe summary of the current authentication and connection state.</summary>
    public SessionConnectionState ConnectionState =>
        IsConnected
            ? SessionConnectionState.Connected
            : IsAuthenticationInProgress
                ? SessionConnectionState.Connecting
                : HasStoredCredentials
                    ? SessionConnectionState.Disconnected
                    : SessionConnectionState.SignedOut;

    /// <summary>Gets whether lifecycle policy permits an automatic credentials-based foreground reconnect.</summary>
    internal bool CanAutomaticallyReconnect
    {
        get
        {
            lock (authenticationSync)
            {
                return !disposed &&
                       !automaticReconnectBlocked &&
                       foregroundReconnectAllowed &&
                       HasStoredCredentials;
            }
        }
    }

    /// <summary>Gets whether an explicit, automatic, or bounded-background authentication currently owns the gate.</summary>
    internal bool IsAuthenticationInProgress => loginGate.CurrentCount == 0;

    /// <summary>Gets a detached snapshot of the most recent state observed for each transfer.</summary>
    public IReadOnlyList<Transfer> Transfers => transfers.Values
        .Concat(recentTerminalTransfers)
        .OrderByDescending(transfer => transfer.StartTime ?? DateTime.MinValue)
        .ThenBy(transfer => transfer.Filename, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Gets an unsorted detached snapshot of live transfers plus the bounded recent-terminal window.</summary>
    internal IReadOnlyList<Transfer> GetTransferReconciliationSnapshot() =>
        [.. transfers.Values, .. recentTerminalTransfers];

    /// <summary>Gets whether any observed transfer is requested, queued, initializing, or actively transferring.</summary>
    internal bool HasActiveOrQueuedTransfers => transfers.Values.Any(transfer =>
        !transfer.State.HasFlag(TransferStates.Completed) &&
        (transfer.State &
        (
            TransferStates.Requested |
            TransferStates.Queued |
            TransferStates.Initializing |
            TransferStates.InProgress)) != 0);

    /// <summary>Gets the durable account-scoped private-message state used by future UIKit controllers.</summary>
    public PrivateMessageSessionState PrivateMessages => privateMessages;

    /// <summary>Gets a detached chronological snapshot of the active account's persisted private messages.</summary>
    public IReadOnlyList<ConversationMessage> Messages
    {
        get
        {
            string account = privateMessages.CurrentAccount;
            if (account.Length == 0 ||
                !privateMessages.ExportMessageRoots().TryGetValue(account, out var conversations))
            {
                return [];
            }

            return conversations.Values
                .SelectMany(static messages => messages)
                .OrderBy(static message => message.UtcDateTime)
                .ThenBy(static message => message.Id)
                .Select(static message => new ConversationMessage(
                    new DateTimeOffset(DateTime.SpecifyKind(message.UtcDateTime, DateTimeKind.Utc)),
                    message.Username,
                    message.MessageText,
                    message.FromMe))
                .ToArray();
        }
    }

    /// <summary>Raised on the main thread when authentication or client connection state changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised synchronously when an authenticated session is lost without an intentional disconnect.</summary>
    internal event EventHandler? UnexpectedlyDisconnected;

    /// <summary>Raised synchronously when automatic reconnect work must be forgotten.</summary>
    internal event EventHandler? AutomaticReconnectSuppressed;

    /// <summary>Raised synchronously after an authentication owner releases the serialization gate.</summary>
    internal event EventHandler? AuthenticationAttemptCompleted;

    /// <summary>Raised on the main thread when a transfer state or byte count changes.</summary>
    public event EventHandler? TransfersChanged;

    /// <summary>
    /// Raised on the main thread only when an explicit foreground action starts a transfer with a known token.
    /// </summary>
    internal event EventHandler<UserInitiatedTransferEventArgs>? UserInitiatedTransferRequested;

    /// <summary>Raised on the main thread when the explicitly initiated transfer call leaves the session.</summary>
    internal event EventHandler<UserInitiatedTransferEventArgs>? UserInitiatedTransferFinished;

    /// <summary>Raised on the main thread when a private message is received or sent.</summary>
    public event EventHandler? MessagesChanged;

    /// <summary>Authenticates and persists the credentials only after the server accepts them.</summary>
    /// <param name="username">The Soulseek username.</param>
    /// <param name="password">The Soulseek password.</param>
    /// <param name="cancellationToken">Cancels the login attempt.</param>
    public Task LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        lock (authenticationSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            automaticReconnectBlocked = false;
        }

        // A user-driven login supersedes a sleeping or active automatic reconnect attempt.
        AutomaticReconnectSuppressed?.Invoke(this, EventArgs.Empty);
        return LoginCoreAsync(username, password, automaticReconnect: false, cancellationToken);
    }

    private async Task LoginCoreAsync(
        string username,
        string password,
        bool automaticReconnect,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        ObjectDisposedException.ThrowIf(disposed, this);

        long requestedGeneration;
        lock (authenticationSync)
        {
            requestedGeneration = authenticationGeneration;
        }

        await loginGate.WaitAsync(cancellationToken);
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            lock (authenticationSync)
            {
                if (disposed || requestedGeneration != authenticationGeneration)
                {
                    throw new OperationCanceledException("The authentication request was superseded.", cancellationToken);
                }

                activeAuthenticationCancellation = attemptCancellation;
            }

            if (IsConnected && string.Equals(Client.Username, username, StringComparison.Ordinal))
            {
                privateMessages.SwitchAccount(username);
                return;
            }

            if (!Client.State.HasFlag(SoulseekClientStates.Disconnected))
            {
                DisconnectIntentionally(automaticReconnect
                    ? "Preparing an automatic reconnect."
                    : "Starting a new login.");
            }

            await Client.ConnectAsync(username, password, attemptCancellation.Token);
            lock (authenticationSync)
            {
                if (disposed ||
                    requestedGeneration != authenticationGeneration ||
                    attemptCancellation.IsCancellationRequested)
                {
                    if (!Client.State.HasFlag(SoulseekClientStates.Disconnected))
                    {
                        DisconnectIntentionally("Authentication was superseded.");
                    }

                    throw new OperationCanceledException("The authentication request was superseded.", attemptCancellation.Token);
                }

                PreferencesState.SetCredentials(username, password);
                PreferencesState.CurrentlyLoggedIn = true;
                keyValueStore.PutString(KeyConsts.M_Username, username);
                keyValueStore.PutString(KeyConsts.M_Password, password);
                keyValueStore.PutBoolean(KeyConsts.M_CurrentlyLoggedIn, true);
                keyValueStore.Flush();
            }

            privateMessages.SwitchAccount(username);
            RaiseOnMain(StateChanged);
        }
        finally
        {
            lock (authenticationSync)
            {
                if (ReferenceEquals(activeAuthenticationCancellation, attemptCancellation))
                {
                    activeAuthenticationCancellation = null;
                }
            }

            loginGate.Release();
            AuthenticationAttemptCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reconnects with the credentials restored from the preference store.</summary>
    /// <param name="cancellationToken">Cancels the reconnect attempt.</param>
    /// <exception cref="InvalidOperationException">No successful-login credentials are available.</exception>
    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!HasStoredCredentials)
        {
            throw new InvalidOperationException("No stored Soulseek credentials are available.");
        }

        AutomaticReconnectSuppressed?.Invoke(this, EventArgs.Empty);
        return ReconnectCoreAsync(cancellationToken);
    }

    /// <summary>Runs one policy-owned reconnect attempt without cancelling its enclosing retry loop.</summary>
    internal Task ReconnectAutomaticallyAsync(CancellationToken cancellationToken) =>
        ReconnectCoreAsync(cancellationToken);

    private async Task ReconnectCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasStoredCredentials)
        {
            throw new InvalidOperationException("No stored Soulseek credentials are available.");
        }

        CancellationTokenSource reconnectCancellation;
        CancellationTokenSource? previousReconnectCancellation;
        lock (authenticationSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (automaticReconnectBlocked)
            {
                throw new InvalidOperationException(
                    "Automatic reconnect is disabled until the user explicitly signs in again.");
            }

            if (!foregroundReconnectAllowed)
            {
                throw new OperationCanceledException("Foreground reconnection is no longer allowed.", cancellationToken);
            }

            reconnectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            previousReconnectCancellation = foregroundReconnectCancellation;
            foregroundReconnectCancellation = reconnectCancellation;
        }

        CancelIgnoringDisposed(previousReconnectCancellation);

        try
        {
            await LoginCoreAsync(
                PreferencesState.Username!,
                PreferencesState.Password!,
                automaticReconnect: true,
                reconnectCancellation.Token);
        }
        finally
        {
            lock (authenticationSync)
            {
                if (ReferenceEquals(foregroundReconnectCancellation, reconnectCancellation))
                {
                    foregroundReconnectCancellation = null;
                }
            }

            reconnectCancellation.Dispose();
        }
    }

    /// <summary>Allows the active scene to start a credentials-based foreground reconnect.</summary>
    internal void AllowForegroundReconnect()
    {
        lock (authenticationSync)
        {
            if (!disposed)
            {
                foregroundReconnectAllowed = true;
            }
        }
    }

    /// <summary>Cancels an in-flight automatic reconnect and rejects one that races with scene deactivation.</summary>
    internal void CancelPendingForegroundReconnect()
    {
        CancellationTokenSource? cancellationSource;
        lock (authenticationSync)
        {
            foregroundReconnectAllowed = false;
            cancellationSource = foregroundReconnectCancellation;
        }

        CancelIgnoringDisposed(cancellationSource);
    }

    /// <summary>
    /// Runs a bounded background operation with stored credentials, without enabling foreground auto-reconnect.
    /// </summary>
    /// <param name="operation">The connected operation to execute.</param>
    /// <param name="cancellationToken">Cancels connection setup or the operation.</param>
    /// <returns>The result returned by <paramref name="operation"/>.</returns>
    /// <remarks>
    /// Authentication, explicit login, and other background connection windows are serialized. The connection is
    /// closed afterward only when this method created it; an existing foreground session is left connected. Logout
    /// invalidates the authentication generation and cancels both connection setup and the supplied operation.
    /// </remarks>
    internal async Task<bool> RunBackgroundConnectedAsync(
        Func<CancellationToken, Task<bool>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        string username;
        string password;
        long requestedGeneration;
        lock (authenticationSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool hasCredentials =
                !string.IsNullOrWhiteSpace(PreferencesState.Username) && PreferencesState.Password is not null;
            if (!BackgroundConnectionEligibilityPolicy.CanConnect(
                    disposed,
                    automaticReconnectBlocked,
                    hasCredentials,
                    authenticationGenerationMatches: true))
            {
                return false;
            }

            username = PreferencesState.Username!;
            password = PreferencesState.Password!;
            requestedGeneration = authenticationGeneration;
        }

        await loginGate.WaitAsync(cancellationToken);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool connectedForOperation = false;
        try
        {
            lock (authenticationSync)
            {
                bool hasCurrentCredentials =
                    !string.IsNullOrWhiteSpace(PreferencesState.Username) && PreferencesState.Password is not null;
                if (!BackgroundConnectionEligibilityPolicy.CanConnect(
                        disposed,
                        automaticReconnectBlocked,
                        hasCurrentCredentials,
                        requestedGeneration == authenticationGeneration))
                {
                    throw new OperationCanceledException(
                        "The background connection was superseded or automatic relogin was blocked.",
                        cancellationToken);
                }

                activeAuthenticationCancellation = operationCancellation;
            }

            if (!IsConnected)
            {
                if (!Client.State.HasFlag(SoulseekClientStates.Disconnected))
                {
                    DisconnectIntentionally("Preparing a bounded background connection.");
                }

                await Client.ConnectAsync(username, password, operationCancellation.Token);
                lock (authenticationSync)
                {
                    if (disposed ||
                        requestedGeneration != authenticationGeneration ||
                        operationCancellation.IsCancellationRequested)
                    {
                        if (!Client.State.HasFlag(SoulseekClientStates.Disconnected))
                        {
                            DisconnectIntentionally("The background connection was superseded.");
                        }

                        throw new OperationCanceledException(
                            "The background connection was superseded.",
                            operationCancellation.Token);
                    }
                }

                connectedForOperation = true;
            }

            return await operation(operationCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            if (connectedForOperation && !Client.State.HasFlag(SoulseekClientStates.Disconnected))
            {
                DisconnectIntentionally("The bounded background operation completed.");
            }

            lock (authenticationSync)
            {
                if (ReferenceEquals(activeAuthenticationCancellation, operationCancellation))
                {
                    activeAuthenticationCancellation = null;
                }
            }

            loginGate.Release();
            AuthenticationAttemptCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Disconnects, clears login intent and credentials, and keeps local files and settings intact.</summary>
    public void Logout()
    {
        CancellationTokenSource? authenticationCancellation;
        CancellationTokenSource? reconnectCancellation;
        lock (authenticationSync)
        {
            authenticationGeneration++;
            foregroundReconnectAllowed = false;
            authenticationCancellation = activeAuthenticationCancellation;
            reconnectCancellation = foregroundReconnectCancellation;
            PreferencesState.ClearCredentials();
            keyValueStore.PutString(KeyConsts.M_Username, null);
            keyValueStore.PutString(KeyConsts.M_Password, null);
            keyValueStore.PutBoolean(KeyConsts.M_CurrentlyLoggedIn, false);
            keyValueStore.Flush();
        }

        CancelIgnoringDisposed(reconnectCancellation);
        CancelIgnoringDisposed(authenticationCancellation);
        AutomaticReconnectSuppressed?.Invoke(this, EventArgs.Empty);
        DisconnectIntentionally("User signed out.");
        privateMessages.SwitchAccount(null);
        RaiseOnMain(StateChanged);
    }

    /// <summary>Changes the authenticated account password and persists it only after server acknowledgement.</summary>
    /// <param name="newPassword">The non-empty replacement password. Leading and trailing characters are preserved.</param>
    /// <param name="cancellationToken">Cancels the server request before it completes.</param>
    /// <exception cref="ArgumentException"><paramref name="newPassword"/> is empty or exceeds 256 characters.</exception>
    /// <exception cref="InvalidOperationException">No authenticated Soulseek session is available.</exception>
    public async Task ChangePasswordAsync(
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length > 256)
        {
            throw new ArgumentException("A password between 1 and 256 characters is required.", nameof(newPassword));
        }

        EnsureConnected();
        string account = Client.Username;
        await Client.ChangePasswordAsync(newPassword, cancellationToken).ConfigureAwait(false);
        lock (authenticationSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!IsConnected || !string.Equals(Client.Username, account, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The authenticated account changed before the password was saved.");
            }

            PreferencesState.Password = newPassword;
            keyValueStore.PutString(KeyConsts.M_Password, newPassword);
            keyValueStore.Flush();
        }

        RaiseOnMain(StateChanged);
    }

    /// <summary>Gets the server-reported remaining privilege time for the authenticated account.</summary>
    /// <param name="cancellationToken">Cancels the server request.</param>
    /// <returns>The non-negative number of privilege seconds remaining.</returns>
    /// <exception cref="InvalidOperationException">No authenticated Soulseek session is available.</exception>
    public async Task<int> GetPrivilegesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        int seconds = await Client.GetPrivilegesAsync(cancellationToken).ConfigureAwait(false);
        return Math.Max(0, seconds);
    }

    /// <summary>Transfers a positive number of privilege days to another Soulseek account.</summary>
    /// <param name="username">The exact receiving account name.</param>
    /// <param name="days">The positive number of whole days to transfer.</param>
    /// <param name="cancellationToken">Cancels the server request.</param>
    /// <exception cref="ArgumentException"><paramref name="username"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is outside the supported range.</exception>
    /// <exception cref="InvalidOperationException">No authenticated Soulseek session is available.</exception>
    public async Task GrantPrivilegesAsync(
        string username,
        int days,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, 36_500);
        EnsureConnected();
        await Client.GrantUserPrivilegesAsync(username.Trim(), days, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies listener availability and port to the active client without exposing protocol options to UI code.</summary>
    /// <param name="enabled">Whether incoming peer connections should be accepted.</param>
    /// <param name="port">The listener port between 1024 and 65535.</param>
    /// <param name="cancellationToken">Cancels the live reconfiguration.</param>
    /// <returns><see langword="true"/> when the client reports that a reconnect is required.</returns>
    public Task<bool> ReconfigureListenerAsync(
        bool enabled,
        int port,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1_024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);
        return Client.ReconfigureOptionsAsync(
            new SoulseekClientOptionsPatch(enableListener: enabled, listenPort: port),
            cancellationToken);
    }

    /// <summary>Runs a network-wide search through the selected Soulseek client.</summary>
    /// <param name="query">The user-entered search expression.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The peer response snapshot returned when the search completes.</returns>
    public async Task<IReadOnlyCollection<SearchResponse>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
        => await SearchAsync(
            query,
            SearchScope.Network,
            options: null,
            responseReceived: null,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs a scoped search and publishes each accepted peer response while the terminal snapshot is still forming.
    /// </summary>
    /// <param name="query">The user-entered search expression.</param>
    /// <param name="scope">The network, wishlist, room, or user scope.</param>
    /// <param name="options">Optional protocol filtering and result limits.</param>
    /// <param name="responseReceived">
    /// An optional callback invoked on the protocol callback thread for every accepted response. Presentation stores
    /// must generation-check and marshal this callback before updating UIKit.
    /// </param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>A detached snapshot containing every progressively published response.</returns>
    public async Task<IReadOnlyCollection<SearchResponse>> SearchAsync(
        string query,
        SearchScope scope,
        SearchOptions? options,
        Action<SearchResponse>? responseReceived,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(scope);
        EnsureConnected();

        var responses = new ConcurrentQueue<SearchResponse>();
        await Client.SearchAsync(
            SearchQuery.FromText(query),
            response =>
            {
                responses.Enqueue(response);
                responseReceived?.Invoke(response);
            },
            scope: scope,
            options: options,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return responses.ToArray();
    }

    /// <summary>Requests notification authorization in response to an explicit feature or settings action.</summary>
    /// <returns><see langword="true"/> when iOS granted alert, sound, and badge authorization.</returns>
    public Task<bool> RequestNotificationAuthorizationAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return notifier.RequestAuthorizationAsync();
    }

    /// <summary>Requests a user's shared directory tree.</summary>
    /// <param name="username">The remote Soulseek username.</param>
    /// <param name="cancellationToken">Cancels the browse request.</param>
    /// <returns>The remote directory snapshot.</returns>
    public Task<BrowseResponse> BrowseAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        EnsureConnected();
        return Client.BrowseAsync(username, cancellationToken: cancellationToken);
    }

    /// <summary>Starts a download into the Files-visible Documents folder.</summary>
    /// <param name="response">The search response that owns the remote file.</param>
    /// <param name="file">The remote file selected by the user.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>The terminal transfer snapshot.</returns>
    public async Task<Transfer> DownloadAsync(
        SearchResponse response,
        Soulseek.File file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(file);
        EnsureConnected();
        TransferFileKey fileKey = new(TransferDirection.Download, response.Username, file.Filename);
        var resumeDescriptor = new SuspendedDownload(response.Username, file.Filename, file.Size);
        DownloadInfo downloadInfo;
        lock (downloadStartSync)
        {
            TransferItemManager manager = TransferItems.TransferItemManagerDL ??
                throw new InvalidOperationException("The download transfer manager is not initialized.");
            if (manager.GetTransferItemWithIndexFromAll(file.Filename, response.Username, out _) is not null)
            {
                throw new InvalidOperationException("This download already exists in the portable transfer list.");
            }

            downloadInfo = DownloadService.Instance.AddTransfer(
                response.Username,
                file.Filename,
                file.Size,
                response.QueueLength,
                depth: 1,
                queuePaused: false,
                wasLatin1Decoded: false,
                wasFolderLatin1Decoded: false,
                isSingle: true,
                out bool transferExists);
            if (transferExists || downloadInfo?.TransferItemReference is null)
            {
                throw new InvalidOperationException("The download could not be added to the portable transfer list.");
            }

            // A fresh download into a previously completed folder must allow that folder to notify again.
            notifiedCompletedFolders.TryRemove(
                (response.Username, downloadInfo.TransferItemReference.FolderName),
                out _);

            // Persist the resume descriptor first: restoration can recreate a missing manager entry, while the
            // inverse ordering would leave a crash window with no automatic-resume identity at all. Both durable
            // writes still complete before network or filesystem work begins.
            interruptedDownloads.Remember(resumeDescriptor);
            TransferItemManager.MarkTransfersDirty();
            checkpointTransfers();
        }

        pendingUserDownloads[fileKey] = 0;
        Task? transferTask = null;
        bool continuationInvoked = false;
        try
        {
            transferTask = DownloadService.Instance.DownloadFileAsync(
                response.Username,
                file.Filename,
                file.Size,
                downloadInfo.CancellationTokenSource,
                out _,
                downloadInfo,
                depth: 1,
                forceFileBacked: true);
            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
                downloadInfo.CancellationTokenSource.Cancel);
            await transferTask.ConfigureAwait(false);
            continuationInvoked = true;
            DownloadService.Instance.DownloadContinuationActionUI(new DownloadAddedEventArgs(downloadInfo))(transferTask);
            return transfers.Values
                .Concat(recentTerminalTransfers)
                .Where(transfer =>
                    transfer.Direction == TransferDirection.Download &&
                    string.Equals(transfer.Username, response.Username, StringComparison.Ordinal) &&
                    string.Equals(transfer.Filename, file.Filename, StringComparison.Ordinal))
                .OrderByDescending(transfer => transfer.EndTime ?? transfer.StartTime ?? DateTime.MinValue)
                .FirstOrDefault() ?? throw new InvalidOperationException("The completed download snapshot was not observed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An explicit caller cancellation is terminal. System suspension cancels the private transfer CTS
            // instead and deliberately retains this descriptor for the next foreground activation.
            interruptedDownloads.Forget(resumeDescriptor.Username, resumeDescriptor.Filename);
            throw;
        }
        finally
        {
            pendingUserDownloads.TryRemove(fileKey, out _);
            if (!continuationInvoked && transferTask is not null)
            {
                DownloadService.Instance.DownloadContinuationActionUI(new DownloadAddedEventArgs(downloadInfo))(transferTask);
            }

            TransferItemManager.MarkTransfersDirty();
            checkpointTransfers();
        }
    }

    /// <summary>
    /// Accepts one search-result download without making the caller wait for the transfer's terminal state.
    /// </summary>
    /// <param name="response">The peer response that owns the selected file.</param>
    /// <param name="file">The selected remote file.</param>
    /// <param name="queuePaused">Whether to add the file without starting network work.</param>
    /// <param name="cancellationToken">Cancels acceptance only; accepted network work is session-owned.</param>
    /// <returns>The portable identity once the request has entered the transfer manager.</returns>
    public async Task<DownloadAcceptance> QueueDownloadAsync(
        SearchResponse response,
        Soulseek.File file,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var identity = new TransferIdentity(
            TransferDirection.Download,
            response.Username,
            file.Filename);
        if (queuePaused)
        {
            await QueueDownloadsAsync(
                response.Username,
                [new FullFileInfo
                {
                    FullFileName = file.Filename,
                    Size = file.Size,
                    Depth = 1,
                }],
                queuePaused: true,
                cancellationToken).ConfigureAwait(false);
            return new DownloadAcceptance(identity, QueuedPaused: true);
        }

        // DownloadAsync performs its validated manager insertion and durable descriptor write before its first await.
        // Inspect an already-completed task so synchronous validation failures are never reported as accepted.
        Task<Transfer> terminalTask = DownloadAsync(response, file, CancellationToken.None);
        if (terminalTask.IsCompleted)
        {
            await terminalTask.ConfigureAwait(false);
        }
        else
        {
            _ = ObserveAcceptedDownloadAsync(terminalTask, identity);
        }

        return new DownloadAcceptance(identity, QueuedPaused: false);
    }

    /// <summary>
    /// Adds a validated browse selection to the portable download manager and starts it in deterministic file order.
    /// </summary>
    /// <param name="username">The remote user who owns every selected file.</param>
    /// <param name="files">A non-empty, detached set of exact remote paths and sizes.</param>
    /// <param name="queuePaused">Whether to retain the selection without starting network work.</param>
    /// <param name="cancellationToken">Cancels acceptance only; accepted work is session-owned.</param>
    /// <returns>One portable acceptance identity per distinct remote file.</returns>
    public async Task<IReadOnlyList<DownloadAcceptance>> QueueDownloadsAsync(
        string username,
        IReadOnlyCollection<FullFileInfo> files,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(files);
        cancellationToken.ThrowIfCancellationRequested();
        FullFileInfo[] acceptedFiles = files
            .Where(static file => file is not null)
            .GroupBy(static file => file.FullFileName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        if (acceptedFiles.Length == 0 || acceptedFiles.Any(static file => string.IsNullOrWhiteSpace(file.FullFileName)))
        {
            throw new ArgumentException("At least one file with an exact remote path is required.", nameof(files));
        }

        if (!queuePaused)
        {
            EnsureConnected();
        }

        if (string.Equals(username, Username, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Files cannot be downloaded from the signed-in account.");
        }

        var acceptances = acceptedFiles
            .Select(file => new DownloadAcceptance(
                new TransferIdentity(TransferDirection.Download, username, file.FullFileName),
                queuePaused))
            .ToArray();
        Task enqueueTask;
        lock (downloadStartSync)
        {
            TransferItemManager manager = TransferItems.TransferItemManagerDL ??
                throw new InvalidOperationException("The download transfer manager is not initialized.");
            if (acceptedFiles.Any(file =>
                    manager.GetTransferItemWithIndexFromAll(file.FullFileName, username, out _) is not null))
            {
                throw new InvalidOperationException("One or more selected downloads already exist.");
            }

            if (!queuePaused)
            {
                foreach (FullFileInfo file in acceptedFiles)
                {
                    interruptedDownloads.Remember(new SuspendedDownload(username, file.FullFileName, file.Size));
                    pendingUserDownloads[new TransferFileKey(
                        TransferDirection.Download,
                        username,
                        file.FullFileName)] = 0;
                }
            }

            // EnqueueFiles inserts every portable row synchronously before it yields while waiting for the first
            // protocol queue transition. This lock therefore covers duplicate validation and the complete insertion.
            enqueueTask = DownloadService.Instance.EnqueueFiles(acceptedFiles, queuePaused, username);
            TransferItemManager.MarkTransfersDirty();
            checkpointTransfers();
        }

        if (enqueueTask.IsCompleted)
        {
            await enqueueTask.ConfigureAwait(false);
        }
        else
        {
            _ = ObserveAcceptedBatchAsync(enqueueTask, acceptances);
        }

        RaiseOnMain(TransfersChanged);
        return acceptances;
    }

    /// <summary>
    /// Gets detached transfer rows, including paused and restored entries that have no current protocol token.
    /// </summary>
    public IReadOnlyList<TransferSnapshot> TransferSnapshots
    {
        get
        {
            var snapshots = new Dictionary<TransferIdentity, TransferSnapshot>();
            CapturePortableTransfers(TransferItems.TransferItemManagerDL, TransferDirection.Download, snapshots);
            CapturePortableTransfers(TransferItems.TransferItemManagerUploads, TransferDirection.Upload, snapshots);

            Transfer[] latestProtocolTransfers = Transfers
                .GroupBy(static transfer => new TransferIdentity(
                    transfer.Direction,
                    transfer.Username,
                    transfer.Filename))
                .Select(static group => group
                    .OrderByDescending(transfer => transfer.EndTime ?? transfer.StartTime ?? DateTime.MinValue)
                    .First())
                .ToArray();
            foreach (Transfer transfer in latestProtocolTransfers)
            {
                var identity = new TransferIdentity(
                    transfer.Direction,
                    transfer.Username,
                    transfer.Filename);
                if (snapshots.TryGetValue(identity, out TransferSnapshot? portable))
                {
                    bool finalized = portable.CanOpen;
                    snapshots[identity] = portable with
                    {
                        State = finalized ? portable.State : transfer.State,
                        Size = portable.Size >= 0 ? portable.Size : transfer.Size,
                        BytesTransferred = Math.Max(portable.BytesTransferred, transfer.BytesTransferred),
                        AverageSpeed = transfer.AverageSpeed,
                        RemainingTime = transfer.RemainingTime,
                        IsProcessing = portable.IsProcessing || !transfer.State.HasFlag(TransferStates.Completed),
                    };
                }
                else
                {
                    snapshots[identity] = new TransferSnapshot(
                        identity,
                        SimpleHelpers.GetFileNameFromFile(transfer.Filename),
                        global::Common.Helpers.GetFolderNameFromFile(transfer.Filename),
                        transfer.State,
                        transfer.Size,
                        transfer.BytesTransferred,
                        transfer.AverageSpeed,
                        transfer.RemainingTime,
                        FinalUri: null,
                        IsProcessing: !transfer.State.HasFlag(TransferStates.Completed));
                }
            }

            return snapshots.Values
                .OrderByDescending(static snapshot => snapshot.IsProcessing)
                .ThenBy(static snapshot => snapshot.Identity.Direction)
                .ThenBy(static snapshot => snapshot.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Executes a transfer action against its portable identity without exposing mutable managers to UIKit.</summary>
    /// <param name="identity">The stable direction, peer, and remote path identity.</param>
    /// <param name="command">Retry, pause, or clear.</param>
    /// <param name="cancellationToken">Cancels before the mutation begins.</param>
    /// <returns><see langword="true"/> when a matching row accepted the action.</returns>
    public Task<bool> ExecuteTransferCommandAsync(
        TransferIdentity identity,
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(identity.Username) || string.IsNullOrWhiteSpace(identity.RemoteFilename))
        {
            throw new ArgumentException("A transfer identity requires a peer and exact remote path.", nameof(identity));
        }

        if (command == TransferCommand.Retry)
        {
            EnsureConnected();
        }

        bool accepted;
        lock (downloadStartSync)
        {
            TransferItemManager? manager = identity.Direction == TransferDirection.Download
                ? TransferItems.TransferItemManagerDL
                : TransferItems.TransferItemManagerUploads;
            TransferItem? item = manager?.GetTransferItemWithIndexFromAll(
                identity.RemoteFilename,
                identity.Username,
                out _);
            if (item is null)
            {
                return Task.FromResult(false);
            }

            accepted = command switch
            {
                TransferCommand.Retry => RetryTransfer(item, identity),
                TransferCommand.Pause => PauseTransfer(item),
                TransferCommand.Clear => ClearTransfer(item, identity, manager!),
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown transfer command."),
            };
            if (accepted)
            {
                TransferItemManager.MarkTransfersDirty();
                checkpointTransfers();
            }
        }

        if (accepted)
        {
            RaiseOnMain(TransfersChanged);
        }

        return Task.FromResult(accepted);
    }

    private bool RetryTransfer(TransferItem item, TransferIdentity identity)
    {
        if (identity.Direction != TransferDirection.Download || item.State.HasFlag(TransferStates.Succeeded))
        {
            return false;
        }

        interruptedDownloads.Remember(new SuspendedDownload(item.Username, item.FullFilename, item.Size));
        pendingUserDownloads[new TransferFileKey(
            TransferDirection.Download,
            item.Username,
            item.FullFilename)] = 0;
        _ = DownloadService.Instance.RetryDownloadItem(item, forceFileBacked: true);
        item.ClearStateForRetry();
        item.State = TransferStates.Requested;
        item.InProcessing = true;
        return true;
    }

    private static bool PauseTransfer(TransferItem item)
    {
        if (!item.InProcessing &&
            (item.State.HasFlag(TransferStates.Completed) || item.State.HasFlag(TransferStates.Cancelled)))
        {
            return false;
        }

        TransferState.CancelAndRemoveToken(item);
        if (item.CancellationTokenSource is { IsCancellationRequested: false } source)
        {
            source.Cancel();
        }

        item.State = TransferStates.Cancelled;
        item.RemainingTime = null;
        item.InProcessing = false;
        return true;
    }

    private bool ClearTransfer(
        TransferItem item,
        TransferIdentity identity,
        TransferItemManager manager)
    {
        if (identity.Direction == TransferDirection.Download)
        {
            // Forget explicit clears before touching the manager so lifecycle restoration cannot resurrect them.
            interruptedDownloads.Forget(identity.Username, identity.RemoteFilename);
        }

        bool wasProcessing = item.InProcessing;
        if (wasProcessing)
        {
            item.CancelAndClearFlag = identity.Direction == TransferDirection.Download;
            TransferState.CancelAndRemoveToken(item);
            if (item.CancellationTokenSource is { IsCancellationRequested: false } source)
            {
                source.Cancel();
            }

            manager.Remove(item);
        }
        else if (identity.Direction == TransferDirection.Download)
        {
            TransferItems.TransferItemManagerWrapped.RemoveAndCleanUp(item);
        }
        else
        {
            manager.Remove(item);
        }

        pendingUserDownloads.TryRemove(new TransferFileKey(
            identity.Direction,
            identity.Username,
            identity.RemoteFilename), out _);
        return true;
    }

    private void CapturePortableTransfers(
        TransferItemManager? manager,
        TransferDirection direction,
        IDictionary<TransferIdentity, TransferSnapshot> destination)
    {
        if (manager?.AllTransferItems is not { } items)
        {
            return;
        }

        TransferItem[] detached;
        lock (items)
        {
            detached = items.ToArray();
        }

        foreach (TransferItem item in detached)
        {
            var identity = new TransferIdentity(direction, item.Username, item.FullFilename);
            destination[identity] = new TransferSnapshot(
                identity,
                item.Filename,
                item.FolderName,
                item.State,
                item.Size,
                item.GetBytesTransferred(),
                item.AvgSpeed,
                item.RemainingTime,
                direction == TransferDirection.Download ? GetVerifiedFinalUri(item.FinalUri) : null,
                item.InProcessing);
        }
    }

    private string? GetVerifiedFinalUri(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            string path = Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && uri.IsFile
                ? uri.LocalPath
                : candidate;
            _ = fileSystem.GetDocumentsRelativePath(path);
            return System.IO.File.Exists(path) ? new Uri(path).AbsoluteUri : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or UriFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task ObserveAcceptedDownloadAsync(Task<Transfer> terminalTask, TransferIdentity identity)
    {
        try
        {
            await terminalTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A later user pause or lifecycle suspension owns this outcome.
        }
        catch (Exception exception)
        {
            Logger.FirebaseError($"Accepted download {identity.StableId} ended with an error.", exception);
        }
    }

    private async Task ObserveAcceptedBatchAsync(
        Task enqueueTask,
        IReadOnlyCollection<DownloadAcceptance> acceptances)
    {
        try
        {
            await enqueueTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Accepted file transfers retain their own lifecycle-owned cancellation sources.
        }
        catch (Exception exception)
        {
            string batchIdentity = string.Join(',', acceptances.Select(static acceptance => acceptance.Identity.StableId));
            Logger.FirebaseError($"Accepted download batch {batchIdentity} failed while entering the peer queue.", exception);
        }
        finally
        {
            TransferItemManager.MarkTransfersDirty();
            checkpointTransfers();
            RaiseOnMain(TransfersChanged);
        }
    }

    /// <summary>Restarts a crash- or system-interrupted download at the actual length of its sandbox partial file.</summary>
    /// <param name="download">The durable versioned descriptor captured before transfer I/O or during suspension.</param>
    /// <returns>Whether work is active, terminal, or could not be started.</returns>
    internal InterruptedDownloadResumeResult ResumeInterruptedDownload(SuspendedDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (!IsConnected)
        {
            return InterruptedDownloadResumeResult.NotStarted;
        }

        TransferItem item;
        lock (downloadStartSync)
        {
            // Checked under the lock so a serialized terminal commit cannot complete between the check and the
            // manager mutation below.
            if (Client.IsTransferInDownloads(download.Username, download.Filename))
            {
                return InterruptedDownloadResumeResult.Active;
            }

            TransferItemManager manager = TransferItems.TransferItemManagerDL ??
                throw new InvalidOperationException("The download transfer manager is not initialized.");
            item = manager.GetTransferItemWithIndexFromAll(download.Filename, download.Username, out _) ??
                manager.AddIfNotExistAndReturnTransfer(
                    CreateInterruptedTransferItem(download),
                    out _);

            if (download.PartialResetRequired)
            {
                ResolveInterruptedDownloadPartialReset(item, download.Size, markerAlreadyPersisted: true);
                download = download with { PartialResetRequired = false };
            }

            if (item.State.HasFlag(TransferStates.Succeeded) && !string.IsNullOrWhiteSpace(item.FinalUri))
            {
                CommitSucceededDownload(item, applyTerminalState: static () => { });
                return InterruptedDownloadResumeResult.Terminal;
            }

            long expectedSize = InterruptedDownloadResumePolicy.ResolveExpectedSize(
                download.Size,
                item.Size,
                download.SizeIsAuthoritative);
            if (expectedSize != download.Size)
            {
                // A prior TransferSizeMismatch retry may have corrected the manager after the original descriptor was
                // written. Upgrade the descriptor before touching the partial or starting any new network work.
                download = download with { Size = expectedSize, SizeIsAuthoritative = true };
                interruptedDownloads.Remember(download);
            }

            if (expectedSize >= 0 && item.Size != expectedSize)
            {
                item.Size = expectedSize;
            }

            string incompletePath;
            string incompleteParentPath;
            long partialLength;
            if (download.DocumentsRelativePath is { Length: > 0 } documentsRelativePath)
            {
                fileSystem.MigrateDocumentsPartialToIncomplete(
                    download.Username,
                    download.Filename,
                    documentsRelativePath,
                    expectedSize,
                    out incompletePath,
                    out incompleteParentPath,
                    out partialLength);
            }
            else
            {
                fileSystem.GetOrCreateIncompleteLocation(
                    download.Username,
                    download.Filename,
                    depth: 1,
                    out incompletePath,
                    out incompleteParentPath,
                    out partialLength);
            }

            item.IncompleteUri = incompletePath;
            item.IncompleteParentUri = incompleteParentPath;
            item.State = TransferStates.Cancelled;
            item.InProcessing = false;
            TransferItemManager.MarkTransfersDirty();

            if (InterruptedDownloadResumePolicy.RequiresOversizedPartialDiscard(partialLength, expectedSize))
            {
                // Bytes retained beyond a downward size correction are untrustworthy and are discarded through
                // the crash-ordered reset flow, never truncated.
                ResolveInterruptedDownloadPartialReset(item, expectedSize, markerAlreadyPersisted: false);
                partialLength = 0;
            }

            if (InterruptedDownloadResumePolicy.IsComplete(
                    partialLength,
                    download.Size,
                    item.Size,
                    download.SizeIsAuthoritative))
            {
                string finalPath = fileSystem.SaveToFile(
                    download.Filename,
                    download.Username,
                    default,
                    incompletePath,
                    incompleteParentPath,
                    memoryMode: false,
                    depth: 1,
                    noSubFolder: item.TransferItemExtra.HasFlag(Seeker.Transfers.TransferItemExtras.NoSubfolder),
                    out string finalUri);
                fileSystem.OnFileFinalized(finalPath);
                CommitSucceededDownload(
                    item,
                    () => ApplyFinalizedState(item, finalUri, partialLength));
                return InterruptedDownloadResumeResult.Terminal;
            }
        }

        checkpointTransfers();
        notifiedCompletedFolders.TryRemove((item.Username, item.FolderName), out _);
        _ = DownloadService.Instance.RetryDownloadItem(item, forceFileBacked: true);
        return InterruptedDownloadResumeResult.Active;
    }

    /// <summary>
    /// Reconciles prepared final-file moves before any interrupted descriptor is allowed to start network work.
    /// </summary>
    internal void ReconcileInterruptedDownloadFinalizations()
    {
        SuspendedDownload[] durableDescriptors = interruptedDownloads
            .ReadEncoded()
            .Select(value => IosInterruptedDownloadStore.TryDecode(
                value,
                out SuspendedDownload descriptor,
                out _)
                ? descriptor
                : null)
            .Where(descriptor => descriptor is not null)
            .Cast<SuspendedDownload>()
            .ToArray();
        foreach (PendingDownloadFinalization entry in fileSystem.FinalizationJournal.Read())
        {
            try
            {
                lock (downloadStartSync)
                {
                    // A concurrent SaveToFile must never be observed between its journal prepare and its move.
                    fileSystem.RunFinalizationExclusive(() =>
                        ReconcilePreparedFinalization(entry, durableDescriptors));
                }
            }
            catch (Exception exception)
            {
                Logger.FirebaseError(
                    $"Reconciling finalized download '{entry.Filename}' failed; its journal was retained.",
                    exception);
            }
        }
    }

    private void ReconcilePreparedFinalization(
        PendingDownloadFinalization entry,
        SuspendedDownload[] durableDescriptors)
    {
        if (!fileSystem.FinalizationJournal.Contains(entry.Username, entry.Filename))
        {
            // A serialized terminal commit resolved this entry after the reconcile snapshot was read.
            return;
        }

        if (Client.IsTransferInDownloads(entry.Username, entry.Filename))
        {
            // A live transfer owns this identity; its own terminal commit reconciles the prepared move.
            return;
        }

        TransferItemManager manager = TransferItems.TransferItemManagerDL ??
            throw new InvalidOperationException("The download transfer manager is not initialized.");
        var journalDescriptor = new SuspendedDownload(entry.Username, entry.Filename, entry.ActualLength);
        TransferItem item = manager.GetTransferItemWithIndexFromAll(entry.Filename, entry.Username, out _) ??
            manager.AddIfNotExistAndReturnTransfer(CreateInterruptedTransferItem(journalDescriptor), out _);
        bool managerSucceeded =
            item.State.HasFlag(TransferStates.Succeeded) && !string.IsNullOrWhiteSpace(item.FinalUri);
        DownloadFinalizationFileSnapshot snapshot = fileSystem.InspectFinalization(entry);
        SuspendedDownload? durableDescriptor = durableDescriptors
            .Where(descriptor =>
                string.Equals(descriptor.Username, entry.Username, StringComparison.Ordinal) &&
                string.Equals(descriptor.Filename, entry.Filename, StringComparison.Ordinal))
            .OrderByDescending(descriptor => descriptor.SizeIsAuthoritative)
            .FirstOrDefault();
        long expectedSize = InterruptedDownloadResumePolicy.ResolveFinalizationExpectedSize(
            entry.ActualLength,
            item.Size,
            durableDescriptor);
        bool preparedTargetSizeMismatched =
            expectedSize != entry.ActualLength &&
            !snapshot.SourceExists &&
            snapshot.TargetExists &&
            snapshot.TargetLength == entry.ActualLength;
        var descriptor = new SuspendedDownload(
            entry.Username,
            entry.Filename,
            expectedSize,
            preparedTargetSizeMismatched ? entry.DocumentsRelativePath : null,
            SizeIsAuthoritative: true);
        DownloadFinalizationRecoveryAction action = expectedSize == entry.ActualLength
            && durableDescriptor?.PartialResetRequired != true
            ? IosDownloadFinalizationRecoveryPolicy.Classify(
                managerSucceeded,
                snapshot.SourceExists,
                snapshot.TargetExists,
                snapshot.TargetLength,
                entry.ActualLength)
            : DownloadFinalizationRecoveryAction.RetryFromDescriptor;

        if (action is DownloadFinalizationRecoveryAction.RetryFromDescriptor)
        {
            ResetPreparedFinalizationForRetry(item, descriptor, entry);
            return;
        }

        _ = CommitSucceededDownload(
            item,
            () => ApplyFinalizedState(item, snapshot.FinalUri, entry.ActualLength));
    }

    /// <summary>Commits terminal download cleanup while retaining a prepared move until its metadata is durable.</summary>
    /// <param name="args">The terminal disposition and portable transfer item.</param>
    /// <returns>
    /// The explicit durable outcome. A nominal failure may return <see cref="DurableDownloadTerminalOutcome.Succeeded"/>
    /// when the finalization journal proves that the exact move committed before the later failure was reported.
    /// </returns>
    internal DurableDownloadTerminalOutcome CommitTerminalDownload(DownloadTerminalProcessedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        TransferItem item = args.TransferItem;
        // Serialized against reconciliation and resume: an activation-time reconcile pass must never interleave
        // with this live terminal commit and resurrect a descriptor it has already forgotten.
        lock (downloadStartSync)
        {
            PendingDownloadFinalization? prepared = fileSystem.FinalizationJournal
                .Read()
                .FirstOrDefault(entry =>
                    string.Equals(entry.Username, item.Username, StringComparison.Ordinal) &&
                    string.Equals(entry.Filename, item.FullFilename, StringComparison.Ordinal));
            if (prepared is not null)
            {
                DownloadFinalizationFileSnapshot snapshot = fileSystem.InspectFinalization(prepared);
                long expectedSize = InterruptedDownloadResumePolicy.ResolveExpectedSize(
                    prepared.ActualLength,
                    item.Size,
                    descriptorSizeIsAuthoritative: false);
                bool preparedTargetSizeMismatched =
                    expectedSize != prepared.ActualLength &&
                    !snapshot.SourceExists &&
                    snapshot.TargetExists &&
                    snapshot.TargetLength == prepared.ActualLength;
                var retryDescriptor = new SuspendedDownload(
                    item.Username,
                    item.FullFilename,
                    expectedSize,
                    preparedTargetSizeMismatched ? prepared.DocumentsRelativePath : null,
                    SizeIsAuthoritative: true);
                DownloadFinalizationRecoveryAction action = expectedSize == prepared.ActualLength
                    ? IosDownloadFinalizationRecoveryPolicy.Classify(
                        managerSucceeded:
                            item.State.HasFlag(TransferStates.Succeeded) && !string.IsNullOrWhiteSpace(item.FinalUri),
                        snapshot.SourceExists,
                        snapshot.TargetExists,
                        snapshot.TargetLength,
                        prepared.ActualLength)
                    : DownloadFinalizationRecoveryAction.RetryFromDescriptor;
                if (action is not DownloadFinalizationRecoveryAction.RetryFromDescriptor)
                {
                    // The exact move is stronger evidence than a later continuation disposition. In particular, a failure
                    // reported after the rename must commit the already-finalized bytes instead of redownloading them.
                    bool committed = CommitSucceededDownload(
                        item,
                        () => ApplyFinalizedState(item, snapshot.FinalUri, prepared.ActualLength));
                    return committed
                        ? DurableDownloadTerminalOutcome.Succeeded
                        : DurableDownloadTerminalOutcome.NotDurable;
                }

                if (args.Disposition is DownloadTerminalDisposition.Succeeded)
                {
                    Logger.Firebase(
                        $"Download '{item.FullFilename}' reported success without an exact finalized target; " +
                        "it was returned to crash-safe retry state.");
                    ResetPreparedFinalizationForRetry(item, retryDescriptor, prepared);
                    return DurableDownloadTerminalOutcome.NotDurable;
                }

                bool terminalCommitted = IosDownloadFinalizationRecoveryPolicy.TryCommit(
                    TransferItemManager.MarkTransfersDirty,
                    tryCheckpointTransfers,
                    () => interruptedDownloads.Forget(item.Username, item.FullFilename),
                    () => fileSystem.FinalizationJournal.Complete(item.Username, item.FullFilename));
                if (terminalCommitted)
                {
                    RetireCompletedDownloadSnapshots(item);
                }

                return terminalCommitted
                    ? DurableDownloadTerminalOutcome.Unsuccessful
                    : DurableDownloadTerminalOutcome.NotDurable;
            }

            if (args.Disposition is DownloadTerminalDisposition.Succeeded &&
                !string.IsNullOrWhiteSpace(item.FinalUri))
            {
                long actualLength = Math.Max(0, Math.Max(item.BytesTransferred, item.Size));
                bool committed = IosDownloadFinalizationRecoveryPolicy.TryCheckpointTerminal(
                    () =>
                    {
                        ApplyFinalizedState(item, item.FinalUri, actualLength);
                        TransferItemManager.MarkTransfersDirty();
                    },
                    tryCheckpointTransfers,
                    () => interruptedDownloads.Forget(item.Username, item.FullFilename));
                if (committed)
                {
                    PublishDurablyFinalizedDownloadCompletion(item);
                }

                return committed
                    ? DurableDownloadTerminalOutcome.Succeeded
                    : DurableDownloadTerminalOutcome.NotDurable;
            }

            bool unsuccessfulCommitted = IosDownloadFinalizationRecoveryPolicy.TryCheckpointTerminal(
                TransferItemManager.MarkTransfersDirty,
                tryCheckpointTransfers,
                () => interruptedDownloads.Forget(item.Username, item.FullFilename));
            if (unsuccessfulCommitted)
            {
                RetireCompletedDownloadSnapshots(item);
            }

            return unsuccessfulCommitted
                ? DurableDownloadTerminalOutcome.Unsuccessful
                : DurableDownloadTerminalOutcome.NotDurable;
        }
    }

    /// <summary>
    /// Durably marks stale partial bytes, deletes them through the throwing filesystem path, and checkpoints the
    /// corrected retry state before a size-mismatch retry can start network work.
    /// </summary>
    /// <param name="item">The manager item identifying the download whose peer-reported size changed.</param>
    /// <param name="correctedSize">The peer-authoritative byte length to persist before applying it in memory.</param>
    /// <exception cref="IOException">The corrected descriptor could not be flushed durably.</exception>
    internal void RefreshInterruptedDownloadExpectedSize(TransferItem item, long correctedSize)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(correctedSize);
        lock (downloadStartSync)
        {
            ResolveInterruptedDownloadPartialReset(item, correctedSize, markerAlreadyPersisted: false);
        }
    }

    /// <summary>
    /// Validates a persisted direct-download location and migrates a version 2 absolute path when it still belongs to
    /// this process's current Documents container.
    /// </summary>
    /// <param name="download">The decoded interrupted-download descriptor.</param>
    /// <param name="containsLegacyAbsolutePath">
    /// Whether the descriptor came from the obsolete format that stored an absolute sandbox path.
    /// </param>
    /// <param name="normalized">
    /// A descriptor containing a containment-validated Documents-relative path, or the original managed descriptor.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the descriptor is safe to retain; <see langword="false"/> when it is malformed,
    /// stale, rooted in the new format, or outside the current Documents container.
    /// </returns>
    internal bool TryNormalizeInterruptedDownload(
        SuspendedDownload download,
        bool containsLegacyAbsolutePath,
        out SuspendedDownload normalized)
    {
        ArgumentNullException.ThrowIfNull(download);
        normalized = download;
        if (download.DocumentsRelativePath is not { Length: > 0 } persistedPath)
        {
            return true;
        }

        try
        {
            string relativePath;
            if (containsLegacyAbsolutePath)
            {
                try
                {
                    relativePath = fileSystem.GetDocumentsRelativePath(persistedPath);
                }
                catch (UnauthorizedAccessException) when (
                    IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath(
                        persistedPath,
                        out string rebasedPath))
                {
                    // The absolute path belongs to a previous app container: an app update regenerates the
                    // container UUID while the partial still exists at the same Documents-relative location.
                    relativePath = rebasedPath;
                }
            }
            else
            {
                relativePath = persistedPath;
            }

            _ = fileSystem.ResolveDocumentsRelativePath(relativePath);
            normalized = download with { DocumentsRelativePath = relativePath };
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Logger.Firebase(
                $"Discarding an unsafe or stale interrupted-download path for '{download.Filename}': {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cancels transfers that cannot honestly continue in the background and optionally closes the network session.
    /// </summary>
    /// <param name="continuingTransfers">Recently progressing transfers covered by an active iOS task grant.</param>
    /// <param name="disconnect">Whether to close the Soulseek session after all transfers have been paused.</param>
    /// <returns>Downloads paused by iOS policy that should be retried on the next foreground activation.</returns>
    internal IReadOnlyList<SuspendedDownload> PauseTransfersForSuspension(
        IReadOnlyCollection<Transfer> continuingTransfers,
        bool disconnect)
    {
        ArgumentNullException.ThrowIfNull(continuingTransfers);
        var continuingKeys = continuingTransfers
            .Select(transfer => (transfer.Direction, transfer.Token))
            .ToHashSet();
        var continuingFiles = continuingTransfers
            .Select(transfer => new TransferFileKey(transfer.Direction, transfer.Username, transfer.Filename))
            .ToHashSet();

        IReadOnlyList<TransferItem> pausedDownloads = PauseManagedTransfers(
            TransferItems.TransferItemManagerDL,
            TransferDirection.Download,
            continuingFiles);
        _ = PauseManagedTransfers(
            TransferItems.TransferItemManagerUploads,
            TransferDirection.Upload,
            continuingFiles);

        if (disconnect && !Client.State.HasFlag(SoulseekClientStates.Disconnected))
        {
            DisconnectIntentionally("iOS suspended transfers without an active continued-processing grant.");
        }

        return pausedDownloads
            .Select(item => new SuspendedDownload(
                item.Username,
                item.FullFilename,
                item.Size,
                SizeIsAuthoritative: true))
            .Distinct()
            .ToArray();
    }

    /// <summary>Sends a foreground private message, reconnecting first when stored credentials are available.</summary>
    /// <param name="username">The destination Soulseek username.</param>
    /// <param name="message">The message body.</param>
    /// <param name="cancellationToken">Cancels reconnection or sending.</param>
    public async Task SendPrivateMessageAsync(
        string username,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsConnected)
        {
            if (!HasStoredCredentials)
            {
                throw new InvalidOperationException("No stored Soulseek credentials are available.");
            }

            await LoginAsync(PreferencesState.Username!, PreferencesState.Password!, cancellationToken);
        }

        await Client.SendPrivateMessageAsync(username, message, cancellationToken);
        DateTime now = DateTime.UtcNow;
        var sent = new Message(
            username,
            Interlocked.Decrement(ref nextLocalMessageId),
            false,
            now,
            message,
            true,
            SentStatus.Success,
            SpecialMessageCode.None,
            false)
        {
            LocalDateTime = now.ToLocalTime(),
        };
        privateMessages.AddMessage(username, sent);
    }

    /// <summary>Unsubscribes from client events and releases the Soulseek client.</summary>
    public void Dispose()
    {
        CancellationTokenSource? authenticationCancellation;
        CancellationTokenSource? reconnectCancellation;
        lock (authenticationSync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            authenticationGeneration++;
            foregroundReconnectAllowed = false;
            authenticationCancellation = activeAuthenticationCancellation;
            reconnectCancellation = foregroundReconnectCancellation;
        }

        CancelIgnoringDisposed(reconnectCancellation);
        CancelIgnoringDisposed(authenticationCancellation);
        AutomaticReconnectSuppressed?.Invoke(this, EventArgs.Empty);
        notifier.DirectReplySubmitted -= OnDirectReplySubmitted;
        Client.StateChanged -= OnClientStateChanged;
        Client.TransferStateChanged -= OnTransferChanged;
        Client.TransferProgressUpdated -= OnTransferChanged;
        Client.PrivateMessageReceived -= OnPrivateMessageReceived;
        privateMessages.Changed -= OnPrivateMessageStateChanged;
        _ = PauseManagedTransfers(
            TransferItems.TransferItemManagerDL,
            TransferDirection.Download,
            new HashSet<TransferFileKey>());
        _ = PauseManagedTransfers(
            TransferItems.TransferItemManagerUploads,
            TransferDirection.Upload,
            new HashSet<TransferFileKey>());
        Client.Dispose();
    }

    private void RestoreLoginIntent()
    {
        bool loggedIn = keyValueStore.GetBoolean(KeyConsts.M_CurrentlyLoggedIn, false);
        string? username = keyValueStore.GetString(KeyConsts.M_Username);
        string? password = keyValueStore.GetString(KeyConsts.M_Password);
        if (loggedIn && !string.IsNullOrWhiteSpace(username) && password is not null)
        {
            PreferencesState.SetCredentials(username, password);
            PreferencesState.CurrentlyLoggedIn = true;
        }
        else
        {
            PreferencesState.ClearCredentials();
        }
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsConnected)
        {
            throw new InvalidOperationException("A Soulseek connection is required.");
        }
    }

    private void OnClientStateChanged(object? sender, SoulseekClientStateChangedEventArgs args)
    {
        bool kicked = args.Exception is KickedFromServerException;
        CancellationTokenSource? blockedAuthentication = null;
        if (kicked)
        {
            lock (authenticationSync)
            {
                automaticReconnectBlocked = true;
                blockedAuthentication = activeAuthenticationCancellation;
            }

            CancelIgnoringDisposed(blockedAuthentication);
        }

        bool unexpected = ReconnectDisconnectClassifier.IsUnexpected(
            args,
            intentionalDisconnect: Volatile.Read(ref intentionalDisconnectDepth) > 0);
        RaiseOnMain(StateChanged);

        if (kicked)
        {
            AutomaticReconnectSuppressed?.Invoke(this, EventArgs.Empty);
        }
        else if (unexpected)
        {
            UnexpectedlyDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DisconnectIntentionally(string message)
    {
        Interlocked.Increment(ref intentionalDisconnectDepth);
        try
        {
            Client.Disconnect(message);
        }
        finally
        {
            Interlocked.Decrement(ref intentionalDisconnectDepth);
        }
    }

    private void OnTransferChanged(object? sender, TransferEventArgs args)
    {
        Transfer transfer = args.Transfer;
        var transferKey = (transfer.Direction, transfer.Token);
        transfers[transferKey] = transfer;
        TransferItem? portableTransfer = UpdatePortableTransfer(transfer);
        PublishTransferNotification(
            transfer,
            transferKey,
            portableTransfer,
            durableDownloadFinalizationCommitted: false);

        var fileKey = new TransferFileKey(transfer.Direction, transfer.Username, transfer.Filename);
        if (pendingUserDownloads.TryRemove(fileKey, out _))
        {
            userInitiatedTransferTokens[transferKey] = 0;
            RaiseOnMain(UserInitiatedTransferRequested, new UserInitiatedTransferEventArgs(
                transfer.Direction,
                transfer.Token));
        }

        RaiseOnMain(TransfersChanged);
        if (transfer.State.HasFlag(TransferStates.Completed) &&
            userInitiatedTransferTokens.TryRemove(transferKey, out _))
        {
            RaiseOnMain(UserInitiatedTransferFinished, new UserInitiatedTransferEventArgs(
                transfer.Direction,
                transfer.Token));
        }

        if (transfer.State.HasFlag(TransferStates.Completed) &&
            !DownloadCompletionPublicationPolicy.IsAwaitingDurableFinalization(
                transfer.Direction,
                transfer.State,
                portableTransfer?.FinalUri))
        {
            // The terminal notification for this snapshot has been published above; only downloads still
            // waiting for their durable finalization commit stay live until that commit publishes them.
            RetireTerminalTransfer(transferKey, transfer);
        }
    }

    private void RetireTerminalTransfer((TransferDirection Direction, int Token) transferKey, Transfer transfer)
    {
        if (transfers.TryRemove(transferKey, out _))
        {
            recentTerminalTransfers.Enqueue(transfer);
            while (recentTerminalTransfers.Count > MaxRecentTerminalTransfers &&
                   recentTerminalTransfers.TryDequeue(out _))
            {
            }
        }

        notifiedTransferStates.TryRemove(transferKey, out _);
        userInitiatedTransferTokens.TryRemove(transferKey, out _);
    }

    private void RetireCompletedDownloadSnapshots(TransferItem item)
    {
        foreach (KeyValuePair<(TransferDirection Direction, int Token), Transfer> pair in transfers)
        {
            if (pair.Value.Direction == TransferDirection.Download &&
                pair.Value.State.HasFlag(TransferStates.Completed) &&
                string.Equals(pair.Value.Username, item.Username, StringComparison.Ordinal) &&
                string.Equals(pair.Value.Filename, item.FullFilename, StringComparison.Ordinal))
            {
                RetireTerminalTransfer(pair.Key, pair.Value);
            }
        }
    }

    private void OnPrivateMessageReceived(object? sender, PrivateMessageReceivedEventArgs args)
    {
        if (SimpleHelpers.UserListService?.IsUserInIgnoreList(args.Username) != true)
        {
            var received = new Message(
                args.Username,
                args.Id,
                args.Replayed,
                args.Timestamp.ToLocalTime(),
                args.Timestamp,
                args.Message,
                fromMe: false);
            privateMessages.AddMessage(args.Username, received);
            notifier.Post(new PrivateMessageNotification(args.Username, args.Message));
        }

        _ = AcknowledgePrivateMessageAsync(args.Id);
    }

    private void OnPrivateMessageStateChanged(object? sender, PrivateMessageStateChangedEventArgs args)
    {
        try
        {
            appDataStateService.PersistPrivateMessages(
                privateMessages.ExportMessageRoots(),
                privateMessages.ExportLastReadRoots());
        }
        catch (Exception exception)
        {
            Logger.FirebaseError("Private-message state persistence failed.", exception);
        }

        RaiseOnMain(MessagesChanged);
    }

    private async Task AcknowledgePrivateMessageAsync(int messageId)
    {
        try
        {
            await Client.AcknowledgePrivateMessageAsync(messageId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.FirebaseError($"Acknowledging private message {messageId} failed.", exception);
        }
    }

    private Task OnDirectReplySubmitted(object? sender, DirectReplyEventArgs args) => SendDirectReplyAsync(args);

    private async Task SendDirectReplyAsync(DirectReplyEventArgs args)
    {
        try
        {
            await SendPrivateMessageAsync(args.Username, args.Message);
            notifier.Update(new PrivateMessageNotification(
                args.Username,
                args.Message,
                PrivateMessageDeliveryState.Sent));
        }
        catch (Exception exception)
        {
            Logger.FirebaseError("Direct reply failed.", exception);
            notifier.Update(new PrivateMessageNotification(
                args.Username,
                args.Message,
                PrivateMessageDeliveryState.Failed));
        }
    }

    private static void CancelIgnoringDisposed(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The owning attempt disposes its linked source when it completes concurrently.
        }
    }

    private void RaiseOnMain(EventHandler? handler) =>
        mainThreadRunner.RunOnUiThread(() => handler?.Invoke(this, EventArgs.Empty));

    private void RaiseOnMain<TEventArgs>(EventHandler<TEventArgs>? handler, TEventArgs args)
        where TEventArgs : EventArgs =>
        mainThreadRunner.RunOnUiThread(() => handler?.Invoke(this, args));

    private bool CommitSucceededDownload(TransferItem item, Action applyTerminalState)
    {
        bool committed = IosDownloadFinalizationRecoveryPolicy.TryCommit(
            () =>
            {
                applyTerminalState();
                TransferItemManager.MarkTransfersDirty();
            },
            tryCheckpointTransfers,
            () => interruptedDownloads.Forget(item.Username, item.FullFilename),
            () => fileSystem.FinalizationJournal.Complete(item.Username, item.FullFilename));
        if (committed)
        {
            PublishDurablyFinalizedDownloadCompletion(item);
        }

        return committed;
    }

    private void ResolveInterruptedDownloadPartialReset(
        TransferItem item,
        long correctedSize,
        bool markerAlreadyPersisted)
    {
        var resetRequired = new SuspendedDownload(
            item.Username,
            item.FullFilename,
            correctedSize,
            SizeIsAuthoritative: true,
            PartialResetRequired: true);
        var resetCompleted = resetRequired with { PartialResetRequired = false };
        string resetPath = string.Empty;
        string resetParent = string.Empty;

        InterruptedDownloadPartialResetPolicy.Resolve(
            markerAlreadyPersisted
                ? static () => { }
                : () => interruptedDownloads.Remember(resetRequired),
            () =>
            {
                fileSystem.GetOrCreateIncompleteLocation(
                    item.Username,
                    item.FullFilename,
                    depth: 1,
                    out resetPath,
                    out resetParent,
                    out _);
                _ = fileSystem.DeleteIncompleteFile(resetPath);
            },
            () =>
            {
                item.ClearStateForRetry();
                item.Size = correctedSize;
                item.FinalUri = string.Empty;
                item.IncompleteUri = resetPath;
                item.IncompleteParentUri = resetParent;
                item.State = TransferStates.Cancelled;
                item.InProcessing = false;
                item.RemainingTime = null;
                TransferItemManager.MarkTransfersDirty();
            },
            tryCheckpointTransfers,
            () => interruptedDownloads.CompletePartialReset(resetCompleted));
    }

    private void ResetPreparedFinalizationForRetry(
        TransferItem item,
        SuspendedDownload descriptor,
        PendingDownloadFinalization entry)
    {
        // Re-establish the descriptor before changing a manager snapshot that may already claim network success.
        // The journal remains until that reset is durable, so a crash at every boundary repeats this same safe step.
        interruptedDownloads.Remember(descriptor);
        item.ClearStateForRetry();
        if (descriptor.Size >= 0)
        {
            item.Size = descriptor.Size;
        }

        item.FinalUri = string.Empty;
        item.State = TransferStates.Cancelled;
        item.InProcessing = false;
        item.RemainingTime = null;
        TransferItemManager.MarkTransfersDirty();
        if (tryCheckpointTransfers())
        {
            fileSystem.FinalizationJournal.Complete(entry.Username, entry.Filename);
        }
    }

    private static void ApplyFinalizedState(TransferItem item, string finalUri, long actualLength)
    {
        item.IncompleteUri = null!;
        item.IncompleteParentUri = null!;
        item.FinalUri = finalUri;
        item.BytesTransferred = actualLength;
        item.Size = actualLength;
        item.Progress = 100;
        item.Failed = false;
        item.InProcessing = false;
        item.State = TransferStates.Completed | TransferStates.Succeeded;
    }

    private static TransferItem CreateInterruptedTransferItem(SuspendedDownload download) => new()
    {
        Filename = SimpleHelpers.GetFileNameFromFile(download.Filename),
        FolderName = global::Common.Helpers.GetFolderNameFromFile(download.Filename),
        Username = download.Username,
        FullFilename = download.Filename,
        Size = download.Size,
        QueueLength = int.MaxValue,
        State = TransferStates.Cancelled,
        TransferItemExtra = PreferencesState.NoSubfolderForSingle
            ? Seeker.Transfers.TransferItemExtras.NoSubfolder
            : default,
    };

    private TransferItem? UpdatePortableTransfer(Transfer transfer)
    {
        TransferItemManager? manager = transfer.Direction == TransferDirection.Download
            ? TransferItems.TransferItemManagerDL
            : TransferItems.TransferItemManagerUploads;
        TransferItem? item = manager?.GetTransferItemWithIndexFromAll(
            transfer.Filename,
            transfer.Username,
            out _);
        if (item is null)
        {
            return null;
        }

        bool awaitingDurableFinalization = DownloadCompletionPublicationPolicy.IsAwaitingDurableFinalization(
            transfer.Direction,
            transfer.State,
            item.FinalUri);
        item.State = awaitingDurableFinalization ? TransferStates.InProgress : transfer.State;
        item.BytesTransferred = transfer.BytesTransferred;
        item.Progress = awaitingDurableFinalization
            ? (int)Math.Clamp(transfer.PercentComplete, 0, 99)
            : (int)Math.Clamp(transfer.PercentComplete, 0, 100);
        item.RemainingTime = transfer.RemainingTime;
        item.AvgSpeed = transfer.AverageSpeed;
        item.InProcessing = awaitingDurableFinalization || !transfer.State.HasFlag(TransferStates.Completed);
        item.Failed = transfer.State.HasFlag(TransferStates.Errored) ||
            transfer.State.HasFlag(TransferStates.Rejected) ||
            transfer.State.HasFlag(TransferStates.TimedOut) ||
            transfer.State.HasFlag(TransferStates.Aborted);
        TransferItemManager.MarkTransfersDirty();
        return item;
    }

    private void PublishTransferNotification(
        Transfer transfer,
        (TransferDirection Direction, int Token) transferKey,
        TransferItem? portableTransfer,
        bool durableDownloadFinalizationCommitted)
    {
        if (!DownloadCompletionPublicationPolicy.CanPublish(
                transfer.Direction,
                transfer.State,
                portableTransfer?.FinalUri,
                durableDownloadFinalizationCommitted))
        {
            return;
        }

        NotificationTransferState state = ToNotificationState(transfer.State);
        if (!notifiedTransferStates.TryGetValue(transferKey, out NotificationTransferState previous) || previous != state)
        {
            notifiedTransferStates[transferKey] = state;
            string stableTransferId = new TransferIdentity(
                transfer.Direction,
                transfer.Username,
                transfer.Filename).StableId;
            var notification = new TransferStateNotification(
                stableTransferId,
                transfer.Username,
                transfer.Filename,
                transfer.Direction == TransferDirection.Download
                    ? NotificationTransferDirection.Download
                    : NotificationTransferDirection.Upload,
                state,
                Math.Max(0, transfer.BytesTransferred),
                transfer.Size >= 0 ? transfer.Size : null,
                transfer.AverageSpeed >= 0 ? transfer.AverageSpeed : null);

            if (state is NotificationTransferState.Completed or
                NotificationTransferState.Failed or
                NotificationTransferState.Canceled)
            {
                notifier.Post(notification);
            }
            else
            {
                notifier.Update(notification);
            }
        }

        if (transfer.Direction != TransferDirection.Download ||
            !transfer.State.HasFlag(TransferStates.Succeeded) ||
            portableTransfer is null)
        {
            return;
        }

        PublishCompletedFolderNotification(portableTransfer);
    }

    private void PublishCompletedFolderNotification(TransferItem portableTransfer)
    {
        if (!PreferencesState.NotifyOnFolderCompleted ||
            string.IsNullOrWhiteSpace(portableTransfer.FolderName) ||
            TransferItems.TransferItemManagerDL?.IsFolderNowComplete(portableTransfer, false) != true)
        {
            return;
        }

        var folderKey = (portableTransfer.Username, portableTransfer.FolderName);
        if (notifiedCompletedFolders.TryAdd(folderKey, 0))
        {
            string stableTransferId = new TransferIdentity(
                TransferDirection.Download,
                portableTransfer.Username,
                portableTransfer.FullFilename).StableId;
            notifier.Post(new FolderCompletedNotification(
                folderKey.FolderName,
                folderKey.Username,
                stableTransferId));
        }
    }

    private void PublishDurablyFinalizedDownloadCompletion(TransferItem item)
    {
        Transfer? transfer = transfers.Values
            .Concat(recentTerminalTransfers)
            .Where(candidate =>
                candidate.Direction == TransferDirection.Download &&
                candidate.State.HasFlag(TransferStates.Succeeded) &&
                string.Equals(candidate.Username, item.Username, StringComparison.Ordinal) &&
                string.Equals(candidate.Filename, item.FullFilename, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.EndTime ?? candidate.StartTime ?? DateTime.MinValue)
            .FirstOrDefault();
        if (transfer is not null)
        {
            PublishTransferNotification(
                transfer,
                (transfer.Direction, transfer.Token),
                item,
                durableDownloadFinalizationCommitted: true);
            RetireTerminalTransfer((transfer.Direction, transfer.Token), transfer);
        }
        else
        {
            notifier.Post(new TransferStateNotification(
                new TransferIdentity(
                    TransferDirection.Download,
                    item.Username,
                    item.FullFilename).StableId,
                item.Username,
                item.FullFilename,
                NotificationTransferDirection.Download,
                NotificationTransferState.Completed,
                Math.Max(0, item.BytesTransferred),
                item.Size >= 0 ? item.Size : null));
            PublishCompletedFolderNotification(item);
        }

        // The success toast is deferred to this durable boundary; DownloadService suppresses its immediate toast
        // on iOS so completion is never announced before the finalized file and metadata are provably durable.
        if (!PreferencesState.DisableDownloadToastNotification)
        {
            Toaster.ShowToastLong(
                SimpleHelpers.GetFileNameFromFile(item.FullFilename) +
                " " + Toaster.GetString(StringKey.FinishedDownloading));
        }

        RaiseOnMain(TransfersChanged);
    }

    private static NotificationTransferState ToNotificationState(TransferStates state)
    {
        if (state.HasFlag(TransferStates.Succeeded))
        {
            return NotificationTransferState.Completed;
        }

        if (state.HasFlag(TransferStates.Errored) ||
            state.HasFlag(TransferStates.Rejected) ||
            state.HasFlag(TransferStates.TimedOut) ||
            state.HasFlag(TransferStates.Aborted))
        {
            return NotificationTransferState.Failed;
        }

        if (state.HasFlag(TransferStates.Cancelled))
        {
            return NotificationTransferState.Canceled;
        }

        if (state.HasFlag(TransferStates.InProgress))
        {
            return NotificationTransferState.InProgress;
        }

        return state.HasFlag(TransferStates.Queued) ||
               state.HasFlag(TransferStates.Requested) ||
               state.HasFlag(TransferStates.Initializing)
            ? NotificationTransferState.Queued
            : NotificationTransferState.Paused;
    }

    private static IReadOnlyList<TransferItem> PauseManagedTransfers(
        TransferItemManager? manager,
        TransferDirection direction,
        IReadOnlySet<TransferFileKey> continuingFiles)
    {
        if (manager?.AllTransferItems is not { } items)
        {
            return [];
        }

        TransferItem[] toPause;
        lock (items)
        {
            toPause = items
                .Where(item => IsSuspendable(item.State))
                .Where(item => !continuingFiles.Contains(new TransferFileKey(
                    direction,
                    item.Username,
                    item.FullFilename)))
                .ToArray();
        }

        foreach (TransferItem item in toPause)
        {
            item.State = TransferStates.Cancelled;
            item.RemainingTime = null;
            item.InProcessing = false;
            if (item.CancellationTokenSource is { IsCancellationRequested: false } source)
            {
                source.Cancel();
            }
        }

        if (toPause.Length > 0)
        {
            TransferItemManager.MarkTransfersDirty();
        }

        return toPause;
    }

    private static bool IsSuspendable(TransferStates state) =>
        !state.HasFlag(TransferStates.Completed) &&
        (state &
        (
            TransferStates.Requested |
            TransferStates.Queued |
            TransferStates.Initializing |
            TransferStates.InProgress)) != 0;

    private readonly record struct TransferFileKey(
        TransferDirection Direction,
        string Username,
        string Filename);

}

/// <summary>Reports whether terminal download state crossed every required durable boundary.</summary>
internal enum DurableDownloadTerminalOutcome
{
    /// <summary>The descriptor or journal remains because a required durable boundary did not complete.</summary>
    NotDurable,

    /// <summary>Exact finalized bytes, manager success, descriptor cleanup, and journal cleanup are durable.</summary>
    Succeeded,

    /// <summary>A definitive failure or explicit clear is durable and must make a continued batch unsuccessful.</summary>
    Unsuccessful,
}

/// <summary>Identifies a transfer that was started by an explicit foreground user action.</summary>
/// <param name="Direction">Whether bytes flow into or out of the app.</param>
/// <param name="Token">The Soulseek transfer token reserved for the action.</param>
internal sealed class UserInitiatedTransferEventArgs(TransferDirection Direction, int Token) : EventArgs
{
    /// <summary>Gets whether bytes flow into or out of the app.</summary>
    public TransferDirection Direction { get; } = Direction;

    /// <summary>Gets the Soulseek transfer token reserved for the action.</summary>
    public int Token { get; } = Token;
}

/// <summary>Represents one private message observed during the current process lifetime.</summary>
/// <param name="Timestamp">The local or remote message timestamp.</param>
/// <param name="Username">The other participant's Soulseek username.</param>
/// <param name="Text">The message body.</param>
/// <param name="IsOutgoing">Whether the local user sent the message.</param>
internal sealed record ConversationMessage(
    DateTimeOffset Timestamp,
    string Username,
    string Text,
    bool IsOutgoing);

/// <summary>Describes the account connection states that presentation code may render.</summary>
internal enum SessionConnectionState
{
    /// <summary>No authenticated identity is stored.</summary>
    SignedOut = 0,

    /// <summary>An explicit or automatic authentication attempt is active.</summary>
    Connecting = 1,

    /// <summary>The client is authenticated and ready for network operations.</summary>
    Connected = 2,

    /// <summary>Credentials remain available, but the session is currently offline.</summary>
    Disconnected = 3,
}
