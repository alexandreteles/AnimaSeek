using AnimaSeek.iOS.UI.Components;
using Common.Messages;
using AnimaSeek.iOS.Services;
using Soulseek;

namespace AnimaSeek.iOS.UI.Screens.Home;

/// <summary>Identifies the mutually exclusive account presentations rendered by Home.</summary>
internal enum HomeAccountState
{
    /// <summary>No stored identity exists and credentials may be entered.</summary>
    SignedOut = 0,

    /// <summary>An authentication or foreground reconnect attempt is active.</summary>
    Connecting = 1,

    /// <summary>The client is authenticated and network features are ready.</summary>
    Connected = 2,

    /// <summary>An identity remains available, but the network session is offline.</summary>
    Disconnected = 3,
}

/// <summary>Contains all immutable presentation data needed by Home.</summary>
/// <param name="AccountState">The current account presentation.</param>
/// <param name="Username">The connected or most recently authenticated username.</param>
/// <param name="UnreadMessageCount">The current account's unread private-message count.</param>
/// <param name="UnreadRoomCount">The number of joined rooms with unread messages.</param>
/// <param name="SharedFileCount">The current Documents share-catalog file count.</param>
/// <param name="SharedDirectoryCount">The current Documents share-catalog directory count.</param>
/// <param name="IsBusy">Whether an explicit sign-in or reconnect command is active.</param>
/// <param name="IsReconnectAttempt">Whether the current connection progress is restoring a retained identity.</param>
/// <param name="ErrorMessage">An actionable localized command failure, when present.</param>
internal sealed record HomePresentationState(
    HomeAccountState AccountState,
    string? Username,
    int UnreadMessageCount,
    int UnreadRoomCount,
    int SharedFileCount,
    int SharedDirectoryCount,
    bool IsBusy,
    bool IsReconnectAttempt,
    string? ErrorMessage = null);

/// <summary>
/// Owns Home's session subscriptions and typed account commands without exposing protocol state to UIKit.
/// </summary>
internal sealed class HomePresentationStore : IDisposable
{
    private readonly AppSession session;
    private readonly IosSocialSessionService social;
    private readonly Func<(int Files, int Directories)> shareCounts;
    private CancellationTokenSource? operationCancellation;
    private bool reconnectAttempt;
    private string? errorMessage;
    private bool disposed;

    /// <summary>Creates a presentation store over established application services.</summary>
    /// <param name="session">The process session façade.</param>
    /// <param name="social">The detached social-session snapshot service.</param>
    /// <param name="shareCounts">A detached, immutable share-catalog count provider.</param>
    public HomePresentationStore(
        AppSession session,
        IosSocialSessionService social,
        Func<(int Files, int Directories)> shareCounts)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.social = social ?? throw new ArgumentNullException(nameof(social));
        this.shareCounts = shareCounts ?? throw new ArgumentNullException(nameof(shareCounts));
        session.StateChanged += OnObservableStateChanged;
        session.MessagesChanged += OnObservableStateChanged;
        social.RoomChanged += OnRoomStateChanged;
        social.RoomListChanged += OnObservableStateChanged;
        State = BuildState();
    }

    /// <summary>Gets the most recent immutable Home presentation.</summary>
    public HomePresentationState State { get; private set; }

    /// <summary>Raised on the main thread after Home's immutable presentation changes.</summary>
    public event EventHandler<HomePresentationState>? StateChanged;

    /// <summary>Validates and starts one explicit authentication attempt.</summary>
    /// <param name="username">The user-entered Soulseek account name.</param>
    /// <param name="password">The user-entered password.</param>
    /// <returns>A task that completes after success, cancellation, or published recoverable failure.</returns>
    public Task SignInAsync(string username, string password)
    {
        string? validation = ValidateCredentials(username, password);
        if (validation is not null)
        {
            errorMessage = validation;
            Publish();
            return Task.CompletedTask;
        }

        return RunAuthenticationAsync(
            token => session.LoginAsync(username.Trim(), password, token),
            isReconnectAttempt: false);
    }

    /// <summary>Retries connection using the last successfully persisted identity.</summary>
    /// <returns>A task that completes after success, cancellation, or published recoverable failure.</returns>
    public Task ReconnectAsync() => RunAuthenticationAsync(session.ReconnectAsync, isReconnectAttempt: true);

    /// <summary>Cancels the explicit command owned by this presentation store.</summary>
    public void CancelAuthentication()
    {
        try
        {
            operationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Signs out while retaining local files, transfers, history, and non-sensitive settings.</summary>
    public void SignOut()
    {
        CancelAuthentication();
        errorMessage = null;
        session.Logout();
        Publish();
    }

    /// <summary>Clears a recoverable inline error after the person edits the affected input.</summary>
    public void ClearError()
    {
        if (errorMessage is null)
        {
            return;
        }

        errorMessage = null;
        Publish();
    }

    /// <summary>Returns a localized validation error without starting network work.</summary>
    /// <param name="username">The proposed username.</param>
    /// <param name="password">The proposed password.</param>
    /// <returns>A localized error, or <see langword="null"/> when both values are valid enough to submit.</returns>
    public static string? ValidateCredentials(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return AppStrings.Get("no_empty_user_pass");
        }

        string normalized = username.Trim();
        if (normalized.Length > 30)
        {
            return AppStrings.Get("user_too_long");
        }

        return normalized.Any(character => character is < '\x01' or > '\x7F')
            ? AppStrings.Get("user_invalid_char")
            : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.StateChanged -= OnObservableStateChanged;
        session.MessagesChanged -= OnObservableStateChanged;
        social.RoomChanged -= OnRoomStateChanged;
        social.RoomListChanged -= OnObservableStateChanged;
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref operationCancellation, null);
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    /// <summary>Serializes authentication commands, publishes busy state, and maps known failures to safe copy.</summary>
    /// <param name="operation">The session command to invoke with store-owned cancellation.</param>
    /// <param name="isReconnectAttempt">Whether progress should be described as restoring a retained identity.</param>
    private async Task RunAuthenticationAsync(
        Func<CancellationToken, Task> operation,
        bool isReconnectAttempt)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(disposed, this);
        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref operationCancellation, cancellation);
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            previous?.Dispose();
        }

        errorMessage = null;
        reconnectAttempt = isReconnectAttempt;
        Publish();
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (LoginRejectedException exception)
        {
            errorMessage = ClassifyLoginRejection(exception.Message);
        }
        catch (Exception)
        {
            errorMessage = AppStrings.Get("cannot_login");
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref operationCancellation, null, cancellation), cancellation))
            {
                reconnectAttempt = false;
                cancellation.Dispose();
            }

            Publish();
        }
    }

    /// <summary>Maps protocol rejection markers to localized credential guidance without exposing raw errors.</summary>
    /// <param name="message">The optional protocol rejection message.</param>
    /// <returns>A catalog-backed user-facing validation message.</returns>
    private static string ClassifyLoginRejection(string? message) =>
        message?.Contains("INVALIDUSERNAME", StringComparison.OrdinalIgnoreCase) == true
            ? AppStrings.Get("invalid_username")
            : message?.Contains("INVALIDPASS", StringComparison.OrdinalIgnoreCase) == true
                ? AppStrings.Get("invalid_password")
                : AppStrings.Get("bad_user_pass");

    /// <summary>Builds one detached Home snapshot from the presentation-safe session and social facades.</summary>
    /// <returns>The coherent state used for one render pass.</returns>
    private HomePresentationState BuildState()
    {
        (int files, int directories) = shareCounts();
        HomeAccountState accountState = operationCancellation is not null
            ? HomeAccountState.Connecting
            : session.ConnectionState switch
            {
                SessionConnectionState.SignedOut => HomeAccountState.SignedOut,
                SessionConnectionState.Connecting => HomeAccountState.Connecting,
                SessionConnectionState.Connected => HomeAccountState.Connected,
                SessionConnectionState.Disconnected => HomeAccountState.Disconnected,
                _ => HomeAccountState.SignedOut,
            };
        return new HomePresentationState(
            accountState,
            session.Username,
            session.PrivateMessages.GetTotalUnreadCount(),
            social.Rooms.Count(room => room.HasUnreadMessages),
            files,
            directories,
            operationCancellation is not null || session.ConnectionState == SessionConnectionState.Connecting,
            accountState == HomeAccountState.Connecting &&
                (reconnectAttempt || !string.IsNullOrWhiteSpace(session.Username)),
            errorMessage);
    }

    /// <summary>Replaces the immutable snapshot and informs the current Home controller.</summary>
    private void Publish()
    {
        if (disposed)
        {
            return;
        }

        State = BuildState();
        StateChanged?.Invoke(this, State);
    }

    /// <summary>Rebuilds Home after a session, message, or room-list transition.</summary>
    /// <param name="sender">The changed presentation service.</param>
    /// <param name="args">Unused event data.</param>
    private void OnObservableStateChanged(object? sender, EventArgs args) => Publish();

    /// <summary>Rebuilds Home after one retained room's unread state changes.</summary>
    /// <param name="sender">The social presentation service.</param>
    /// <param name="args">The changed room identity and state.</param>
    private void OnRoomStateChanged(object? sender, RoomStateChangedEventArgs args) => Publish();
}
