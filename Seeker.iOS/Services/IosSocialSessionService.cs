using System.Collections.Concurrent;
using Common;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Seeker.Social;
using Soulseek;

namespace AnimaSeek.iOS.Services;

/// <summary>Describes the kind of social-session state changed by a Soulseek event or command.</summary>
internal enum SocialSessionChangeKind
{
    RoomConnection,
    RoomMessage,
    RoomMembers,
    RoomTickers,
    RoomModerators,
    RoomPreferences,
    UserProfile,
    UserList,
    Presence,
}

/// <summary>Identifies the room affected by a social-session state change.</summary>
internal sealed class RoomStateChangedEventArgs : EventArgs
{
    public RoomStateChangedEventArgs(string roomName, SocialSessionChangeKind kind)
    {
        RoomName = roomName;
        Kind = kind;
    }

    public string RoomName { get; }

    public SocialSessionChangeKind Kind { get; }
}

/// <summary>Identifies the user affected by a social-session state change.</summary>
internal sealed class SocialUserChangedEventArgs : EventArgs
{
    public SocialUserChangedEventArgs(string username, SocialSessionChangeKind kind)
    {
        Username = username;
        Kind = kind;
    }

    public string Username { get; }

    public SocialSessionChangeKind Kind { get; }
}

/// <summary>Combines independently fetched status, statistics, and peer profile data for one user.</summary>
internal sealed class UserProfileSnapshot
{
    public UserProfileSnapshot(
        string username,
        UserStatus? status = null,
        UserStatistics? statistics = null,
        UserInfo? info = null)
    {
        Username = username;
        Status = status;
        Statistics = statistics;
        Info = CopyInfo(info);
    }

    public string Username { get; }

    public UserStatus? Status { get; }

    public UserStatistics? Statistics { get; }

    public UserInfo? Info { get; }

    public UserProfileSnapshot WithStatus(UserStatus status) =>
        new(Username, status, Statistics, Info);

    public UserProfileSnapshot WithStatistics(UserStatistics statistics) =>
        new(Username, Status, statistics, Info);

    private static UserInfo? CopyInfo(UserInfo? info) => info is null
        ? null
        : new UserInfo(
            info.Description,
            info.UploadSlots,
            info.QueueLength,
            info.HasFreeUploadSlot,
            info.Picture?.ToArray());
}

/// <summary>
/// Owns iOS chatroom, presence, friend, ignore, and private-room behavior independently of UIKit controllers.
/// </summary>
/// <remarks>
/// All observable state is exposed as detached snapshots. Soulseek callbacks may arrive concurrently, but event
/// consumers are notified on the main thread only after the corresponding snapshot has been updated.
/// </remarks>
internal sealed class IosSocialSessionService : IDisposable
{
    private readonly AppSession session;
    private readonly ISoulseekClient client;
    private readonly IUserListService userListService;
    private readonly INotifier notifier;
    private readonly IMainThreadRunner mainThreadRunner;
    private readonly ILoggerBackend logger;
    private readonly IKeyValueStore keyValueStore;
    private readonly PortableAppDataStateService appDataStateService;
    private readonly RoomSessionState roomState = new();
    private readonly object stateSync = new();
    private readonly object roomPreferencesSync = new();
    private readonly HashSet<string> joinedRoomNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UserProfileSnapshot> userProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> privateRoomMembers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim connectedInitializationGate = new(1, 1);
    private readonly CancellationTokenSource disposalCancellation = new();
    private ConcurrentDictionary<string, List<string>> autoJoinRoomNames;
    private ConcurrentDictionary<string, List<string>> notifyRoomNames;
    private RoomList? roomList;
    private long roomListVersion;
    private UserPresence currentPresence = UserPresence.Online;
    private bool observedConnected;
    private string? connectedAccount;
    private bool disposed;

    /// <summary>Creates the service, restores canonical social state, and subscribes to the Soulseek session.</summary>
    public IosSocialSessionService(
        AppSession session,
        IUserListService userListService,
        INotifier notifier,
        IMainThreadRunner mainThreadRunner,
        IKeyValueStore keyValueStore,
        PortableAppDataStateService appDataStateService,
        PortableAppDataRestoreResult restored,
        ILoggerBackend logger)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        client = session.Client;
        this.userListService = userListService ?? throw new ArgumentNullException(nameof(userListService));
        this.notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this.appDataStateService = appDataStateService ?? throw new ArgumentNullException(nameof(appDataStateService));
        ArgumentNullException.ThrowIfNull(restored);
        autoJoinRoomNames = NormalizeRoomRoots(restored.State.AutoJoinRoomNames);
        notifyRoomNames = NormalizeRoomRoots(restored.State.NotifyRoomNames);
        foreach (AppDataRestoreFailure failure in restored.Failures)
        {
            logger.FirebaseError($"Portable app-data restore failed for '{failure.Key}'.", failure.Exception);
        }

        foreach (AppDataRestoreFailure failure in appDataStateService.ReloadUserMetadata())
        {
            logger.FirebaseError($"User metadata restore failed for '{failure.Key}'.", failure.Exception);
        }

        session.StateChanged += OnSessionStateChanged;
        client.RoomListReceived += OnRoomListReceived;
        client.RoomMessageReceived += OnRoomMessageReceived;
        client.RoomJoined += OnRoomJoined;
        client.RoomLeft += OnRoomLeft;
        client.RoomTickerAdded += OnRoomTickerAdded;
        client.RoomTickerRemoved += OnRoomTickerRemoved;
        client.RoomTickerListReceived += OnRoomTickerListReceived;
        client.OperatorInPrivateRoomAddedRemoved += OnPrivateRoomOperatorChanged;
        client.PrivateRoomMembershipAdded += OnPrivateRoomMembershipAdded;
        client.PrivateRoomMembershipRemoved += OnPrivateRoomMembershipRemoved;
        client.PrivateRoomModeratedUserListReceived += OnPrivateRoomModeratorsReceived;
        client.PrivateRoomUserListReceived += OnPrivateRoomUsersReceived;
        client.UserStatusChanged += OnUserStatusChanged;
        client.UserStatisticsChanged += OnUserStatisticsChanged;
        if (session.IsConnected)
        {
            OnSessionStateChanged(session, EventArgs.Empty);
        }
    }

    /// <summary>Gets the latest immutable server room list.</summary>
    public RoomList? RoomList
    {
        get
        {
            lock (stateSync)
            {
                return roomList;
            }
        }
    }

    /// <summary>
    /// Gets a counter incremented every time the server supplies a room list, so a screen can tell a
    /// genuinely newer list from an unrelated room change and retire a stale failure message.
    /// </summary>
    public long RoomListVersion
    {
        get
        {
            lock (stateSync)
            {
                return roomListVersion;
            }
        }
    }

    /// <summary>Gets bounded, detached snapshots for every room seen in this process.</summary>
    public IReadOnlyList<RoomSessionSnapshot> Rooms => roomState.Rooms;

    /// <summary>Gets the latest status selected for the current account.</summary>
    public UserPresence CurrentPresence
    {
        get
        {
            lock (stateSync)
            {
                return currentPresence;
            }
        }
    }

    /// <summary>Raised on the main thread after the room list changes.</summary>
    public event EventHandler? RoomListChanged;

    /// <summary>Raised on the main thread after a room snapshot changes.</summary>
    public event EventHandler<RoomStateChangedEventArgs>? RoomChanged;

    /// <summary>Raised on the main thread after a user profile or list entry changes.</summary>
    public event EventHandler<SocialUserChangedEventArgs>? UserChanged;

    /// <summary>Raised on the main thread after the current account presence changes.</summary>
    public event EventHandler? PresenceChanged;

    /// <summary>Gets a detached room snapshot, or <see langword="null"/> when the room is unknown.</summary>
    public RoomSessionSnapshot? GetRoom(string roomName) => roomState.GetSnapshot(RequireText(roomName, nameof(roomName)));

    /// <summary>Gets a detached user profile assembled from the latest server responses.</summary>
    public UserProfileSnapshot? GetUserProfile(string username)
    {
        username = RequireText(username, nameof(username));
        lock (stateSync)
        {
            return userProfiles.TryGetValue(username, out UserProfileSnapshot? profile)
                ? new UserProfileSnapshot(profile.Username, profile.Status, profile.Statistics, profile.Info)
                : null;
        }
    }

    /// <summary>Gets the private-room member-name list most recently sent by the server.</summary>
    public IReadOnlyList<string> GetPrivateRoomMembers(string roomName)
    {
        roomName = RequireText(roomName, nameof(roomName));
        lock (stateSync)
        {
            return privateRoomMembers.TryGetValue(roomName, out IReadOnlyList<string>? members)
                ? members.ToArray()
                : Array.Empty<string>();
        }
    }

    /// <summary>Gets account-specific auto-join rooms from canonical portable state.</summary>
    public IReadOnlyList<string> GetAutoJoinRooms() => GetAccountRoomNames(autoJoinRoomNames);

    /// <summary>Gets account-specific rooms for which incoming-message notifications are enabled.</summary>
    public IReadOnlyList<string> GetNotificationRooms() => GetAccountRoomNames(notifyRoomNames);

    /// <summary>Marks a room visible and clears its unread marker.</summary>
    public void SetVisibleRoom(string? roomName)
    {
        roomState.SetVisibleRoom(roomName);
        if (!string.IsNullOrWhiteSpace(roomName))
        {
            RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomMessage);
        }
    }

    /// <summary>Refreshes the server room list after reconnecting when necessary.</summary>
    public async Task<RoomList> RefreshRoomListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        RoomList result = await client.GetRoomListAsync(cancellationToken);
        SetRoomList(result);
        return result;
    }

    /// <summary>Joins an existing room or creates one when <paramref name="isPrivate"/> is supplied.</summary>
    public async Task<RoomSessionSnapshot> JoinRoomAsync(
        string roomName,
        bool isPrivate = false,
        CancellationToken cancellationToken = default)
    {
        roomName = RequireText(roomName, nameof(roomName));
        await EnsureConnectedAsync(cancellationToken);
        roomState.BeginJoin(roomName);
        RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomConnection);
        try
        {
            RoomData data = await client.JoinRoomAsync(roomName, isPrivate, cancellationToken);
            roomState.CompleteJoin(data);
            lock (stateSync)
            {
                joinedRoomNames.Add(roomName);
            }

            RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomConnection);
            return roomState.GetSnapshot(roomName)!;
        }
        catch (Exception exception)
        {
            bool forbidden = exception is RoomJoinForbiddenException;
            roomState.FailJoin(roomName, exception.Message, forbidden);
            if (forbidden && IsRoomPreferenceEnabled(autoJoinRoomNames, roomName))
            {
                SetAutoJoin(roomName, false);
            }

            RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomConnection);
            throw;
        }
    }

    /// <summary>Leaves a room, disables auto-join for it, and preserves its bounded local history.</summary>
    public async Task LeaveRoomAsync(string roomName, CancellationToken cancellationToken = default)
    {
        roomName = RequireText(roomName, nameof(roomName));
        await EnsureConnectedAsync(cancellationToken);
        await client.LeaveRoomAsync(roomName, cancellationToken);
        roomState.CompleteLeave(roomName);
        lock (stateSync)
        {
            joinedRoomNames.Remove(roomName);
        }

        SetAutoJoin(roomName, false);
        RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomConnection);
    }

    /// <summary>Sends a room message and updates its retained delivery state.</summary>
    public async Task<RoomMessageEntry> SendRoomMessageAsync(
        string roomName,
        string message,
        CancellationToken cancellationToken = default)
    {
        roomName = RequireText(roomName, nameof(roomName));
        message = RequireText(message, nameof(message));
        await EnsureConnectedAsync(cancellationToken);
        string username = CurrentAccountName;
        RoomMessageEntry pending = roomState.AddOutgoingMessage(roomName, username, message, DateTime.UtcNow);
        RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomMessage);
        try
        {
            await client.SendRoomMessageAsync(roomName, message, cancellationToken);
            roomState.CompleteOutgoingMessage(roomName, pending.Sequence, true);
        }
        catch (Exception exception)
        {
            roomState.CompleteOutgoingMessage(roomName, pending.Sequence, false, exception.Message);
            RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomMessage);
            throw;
        }

        RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomMessage);
        return roomState.GetSnapshot(roomName)!.Messages.First(entry => entry.Sequence == pending.Sequence);
    }

    /// <summary>Enables or disables automatic room joining for the current account.</summary>
    public bool SetAutoJoin(string roomName, bool enabled) =>
        SetRoomPreference(autoJoinRoomNames, RequireText(roomName, nameof(roomName)), enabled);

    /// <summary>Enables or disables room-message notifications for the current account.</summary>
    public bool SetRoomNotifications(string roomName, bool enabled) =>
        SetRoomPreference(notifyRoomNames, RequireText(roomName, nameof(roomName)), enabled);

    /// <summary>Fetches user status, server statistics, and peer information concurrently.</summary>
    public async Task<UserProfileSnapshot> FetchUserProfileAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        username = RequireText(username, nameof(username));
        await EnsureConnectedAsync(cancellationToken);
        Task<UserStatus> statusTask = client.GetUserStatusAsync(username, cancellationToken);
        Task<UserStatistics> statisticsTask = client.GetUserStatisticsAsync(username, cancellationToken);
        Task<UserInfo> infoTask = client.GetUserInfoAsync(username, cancellationToken);
        await Task.WhenAll(statusTask, statisticsTask, infoTask);
        var profile = new UserProfileSnapshot(
            username,
            await statusTask,
            await statisticsTask,
            await infoTask);
        SetUserProfile(profile);
        return GetUserProfile(username)!;
    }

    /// <summary>Watches a server user and adds or refreshes the canonical friend entry.</summary>
    public async Task<UserData> AddFriendAsync(string username, CancellationToken cancellationToken = default)
    {
        username = RequireText(username, nameof(username));
        await EnsureConnectedAsync(cancellationToken);
        UserData data = await client.WatchUserAsync(username, cancellationToken);
        userListService.AddUser(data, data.Status);
        SetUserProfile(new UserProfileSnapshot(
            data.Username,
            new UserStatus(data.Username, data.Status, false)));
        RaiseUserChanged(data.Username, SocialSessionChangeKind.UserList);
        return data;
    }

    /// <summary>
    /// Reconciles ephemeral server watches after settings import has durably changed the canonical friend list.
    /// </summary>
    /// <param name="previousFriendNames">The friend names captured immediately before the local import commit.</param>
    /// <returns>The number of server operations that failed and will naturally retry on the next connection.</returns>
    /// <remarks>
    /// Network failures are logged and returned rather than thrown: the local import is already durable and reporting
    /// the whole import as failed would be misleading. Connected-session initialization is serialized with this pass.
    /// </remarks>
    internal async Task<int> ReconcileImportedFriendsAsync(IReadOnlyCollection<string> previousFriendNames)
    {
        ArgumentNullException.ThrowIfNull(previousFriendNames);
        if (disposed || !session.IsConnected)
        {
            return 0;
        }

        try
        {
            await connectedInitializationGate.WaitAsync(disposalCancellation.Token).ConfigureAwait(false);
            try
            {
                if (!session.IsConnected)
                {
                    return 0;
                }

                string[] currentFriendNames;
                lock (CommonState.UserList)
                {
                    currentFriendNames = CommonState.UserList
                        .Select(item => item.Username)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToArray();
                }

                WatchedUserReconciliationResult result = await WatchedUserReconciliationService.ReconcileAsync(
                    client,
                    previousFriendNames,
                    currentFriendNames,
                    disposalCancellation.Token).ConfigureAwait(false);
                foreach (UserData data in result.WatchedUsers)
                {
                    userListService.UpdateExistingUser(
                        data.Username,
                        data,
                        new UserStatus(data.Username, data.Status, false),
                        out _);
                    SetUserProfile(new UserProfileSnapshot(
                        data.Username,
                        new UserStatus(data.Username, data.Status, false)));
                }

                foreach (WatchedUserReconciliationFailure failure in result.Failures)
                {
                    if (failure.Operation == WatchedUserReconciliationOperation.Watch &&
                        failure.Exception is UserNotFoundException)
                    {
                        userListService.SetDoesNotExist(failure.Username);
                    }

                    logger.FirebaseError(
                        $"Settings import was saved, but server {failure.Operation.ToString().ToLowerInvariant()} failed for " +
                        $"'{failure.Username}'; the subscription will retry after reconnect.",
                        failure.Exception);
                }

                return result.Failures.Count;
            }
            finally
            {
                connectedInitializationGate.Release();
            }
        }
        catch (OperationCanceledException) when (disposalCancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            logger.FirebaseError(
                "Settings import was saved, but connected friend subscriptions could not be reconciled; " +
                "they will retry after reconnect.",
                exception);
            return 1;
        }
    }

    /// <summary>Stops watching a user and removes the canonical friend entry.</summary>
    public async Task<bool> RemoveFriendAsync(string username, CancellationToken cancellationToken = default)
    {
        username = RequireText(username, nameof(username));
        await EnsureConnectedAsync(cancellationToken);
        await client.UnwatchUserAsync(username, cancellationToken);
        bool removed = userListService.RemoveUser(username);
        if (removed)
        {
            RaiseUserChanged(username, SocialSessionChangeKind.UserList);
        }

        return removed;
    }

    /// <summary>Adds a user to the canonical ignore list and unwatches a conflicting friend.</summary>
    public async Task<bool> AddIgnoredUserAsync(string username, CancellationToken cancellationToken = default)
    {
        username = RequireText(username, nameof(username));
        if (userListService.ContainsUser(username))
        {
            await EnsureConnectedAsync(cancellationToken);
            await client.UnwatchUserAsync(username, cancellationToken);
        }

        bool added = userListService.AddToIgnoreList(username);
        if (added)
        {
            RaiseUserChanged(username, SocialSessionChangeKind.UserList);
        }

        return added;
    }

    /// <summary>Removes a user from the canonical ignore list without implicitly making them a friend.</summary>
    public bool RemoveIgnoredUser(string username)
    {
        username = RequireText(username, nameof(username));
        bool removed = userListService.RemoveFromIgnoreList(username);
        if (removed)
        {
            RaiseUserChanged(username, SocialSessionChangeKind.UserList);
        }

        return removed;
    }

    /// <summary>Persists whether an offline-to-online transition should post a local notification.</summary>
    public bool SetOnlineAlert(string username, bool enabled)
    {
        username = RequireText(username, nameof(username));
        string? existingKey = UserMetadataService.UserOnlineAlerts.Keys.FirstOrDefault(value => EqualsName(value, username));
        bool changed;
        if (enabled)
        {
            if (existingKey is not null)
            {
                return false;
            }

            changed = UserMetadataService.UserOnlineAlerts.TryAdd(username, 0);
        }
        else
        {
            changed = existingKey is not null && UserMetadataService.UserOnlineAlerts.TryRemove(existingKey, out _);
        }

        if (changed)
        {
            appDataStateService.PersistUserMetadata();
            RaiseUserChanged(username, SocialSessionChangeKind.UserProfile);
        }

        return changed;
    }

    /// <summary>Sets the current account to online or away.</summary>
    public async Task SetPresenceAsync(UserPresence presence, CancellationToken cancellationToken = default)
    {
        if (presence is not UserPresence.Online and not UserPresence.Away)
        {
            throw new ArgumentOutOfRangeException(nameof(presence), presence, "Only online and away can be selected.");
        }

        await EnsureConnectedAsync(cancellationToken);
        await client.SetStatusAsync(presence, cancellationToken);
        lock (stateSync)
        {
            currentPresence = presence;
        }

        RaiseOnMain(PresenceChanged);
    }

    /// <summary>Updates whether the server should accept private-room invitations.</summary>
    public async Task SetPrivateRoomInvitationsAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        bool changed = await client.ReconfigureOptionsAsync(
            new SoulseekClientOptionsPatch(acceptPrivateRoomInvitations: enabled),
            cancellationToken);
        if (!changed && client.Options.AcceptPrivateRoomInvitations != enabled)
        {
            throw new InvalidOperationException("The Soulseek client rejected the private-room invitation setting.");
        }

        PreferencesState.AllowPrivateRoomInvitations = enabled;
        keyValueStore.PutBoolean(KeyConsts.M_AllowPrivateRooomInvitations, enabled);
        keyValueStore.Flush();
    }

    /// <summary>Adds a member to a private room.</summary>
    public async Task AddPrivateRoomMemberAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await client.AddPrivateRoomMemberAsync(
            RequireText(roomName, nameof(roomName)),
            RequireText(username, nameof(username)),
            cancellationToken);
    }

    /// <summary>Removes a member from a private room.</summary>
    public async Task RemovePrivateRoomMemberAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await client.RemovePrivateRoomMemberAsync(
            RequireText(roomName, nameof(roomName)),
            RequireText(username, nameof(username)),
            cancellationToken);
    }

    /// <summary>Adds a moderator to a private room.</summary>
    public async Task AddPrivateRoomModeratorAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await client.AddPrivateRoomModeratorAsync(
            RequireText(roomName, nameof(roomName)),
            RequireText(username, nameof(username)),
            cancellationToken);
    }

    /// <summary>Removes a moderator from a private room.</summary>
    public async Task RemovePrivateRoomModeratorAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await client.RemovePrivateRoomModeratorAsync(
            RequireText(roomName, nameof(roomName)),
            RequireText(username, nameof(username)),
            cancellationToken);
    }

    /// <summary>Drops the current account's private-room membership.</summary>
    public async Task DropPrivateRoomMembershipAsync(
        string roomName,
        CancellationToken cancellationToken = default)
    {
        roomName = RequireText(roomName, nameof(roomName));
        await EnsureConnectedAsync(cancellationToken);
        await client.DropPrivateRoomMembershipAsync(roomName, cancellationToken);
    }

    /// <summary>Drops the current account's private-room ownership.</summary>
    public async Task DropPrivateRoomOwnershipAsync(
        string roomName,
        CancellationToken cancellationToken = default)
    {
        roomName = RequireText(roomName, nameof(roomName));
        await EnsureConnectedAsync(cancellationToken);
        await client.DropPrivateRoomOwnershipAsync(roomName, cancellationToken);
    }

    /// <summary>Sets or clears the current account's ticker in a joined room.</summary>
    public async Task SetRoomTickerAsync(
        string roomName,
        string message,
        CancellationToken cancellationToken = default)
    {
        roomName = RequireText(roomName, nameof(roomName));
        ArgumentNullException.ThrowIfNull(message);
        await EnsureConnectedAsync(cancellationToken);
        await client.SetRoomTickerAsync(roomName, message, cancellationToken);
    }

    /// <summary>Unsubscribes all callbacks and cancels automatic reconnect initialization.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        disposalCancellation.Cancel();
        session.StateChanged -= OnSessionStateChanged;
        client.RoomListReceived -= OnRoomListReceived;
        client.RoomMessageReceived -= OnRoomMessageReceived;
        client.RoomJoined -= OnRoomJoined;
        client.RoomLeft -= OnRoomLeft;
        client.RoomTickerAdded -= OnRoomTickerAdded;
        client.RoomTickerRemoved -= OnRoomTickerRemoved;
        client.RoomTickerListReceived -= OnRoomTickerListReceived;
        client.OperatorInPrivateRoomAddedRemoved -= OnPrivateRoomOperatorChanged;
        client.PrivateRoomMembershipAdded -= OnPrivateRoomMembershipAdded;
        client.PrivateRoomMembershipRemoved -= OnPrivateRoomMembershipRemoved;
        client.PrivateRoomModeratedUserListReceived -= OnPrivateRoomModeratorsReceived;
        client.PrivateRoomUserListReceived -= OnPrivateRoomUsersReceived;
        client.UserStatusChanged -= OnUserStatusChanged;
        client.UserStatisticsChanged -= OnUserStatisticsChanged;
        disposalCancellation.Dispose();
        connectedInitializationGate.Dispose();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!session.IsConnected)
        {
            await session.ReconnectAsync(cancellationToken);
        }
    }

    private void OnSessionStateChanged(object? sender, EventArgs args)
    {
        string? account = TryGetCurrentAccountName();
        bool shouldInitialize = false;
        IReadOnlyList<string> disconnectedRooms = Array.Empty<string>();
        lock (stateSync)
        {
            if (session.IsConnected && account is not null)
            {
                shouldInitialize = !observedConnected || !EqualsName(connectedAccount, account);
                observedConnected = true;
                connectedAccount = account;
            }
            else if (observedConnected && !session.IsConnected)
            {
                observedConnected = false;
                disconnectedRooms = roomState.MarkDisconnected();
            }
        }

        foreach (string roomName in disconnectedRooms)
        {
            RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomConnection);
        }

        if (shouldInitialize)
        {
            _ = RunConnectedInitializationSafelyAsync(disposalCancellation.Token);
        }
    }

    private async Task RunConnectedInitializationSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await connectedInitializationGate.WaitAsync(cancellationToken);
            try
            {
                if (!session.IsConnected)
                {
                    return;
                }

                Task roomListTask = RefreshRoomListAsync(cancellationToken);
                Task friendTask = WatchPersistedFriendsAsync(cancellationToken);
                await Task.WhenAll(roomListTask, friendTask);

                string[] rooms;
                lock (stateSync)
                {
                    rooms = GetAutoJoinRooms()
                        .Concat(joinedRoomNames)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                await Task.WhenAll(rooms.Select(room => AutoJoinSafelyAsync(room, cancellationToken)));
            }
            finally
            {
                connectedInitializationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Connected social-session initialization failed.", exception);
        }
    }

    private async Task WatchPersistedFriendsAsync(CancellationToken cancellationToken)
    {
        string[] usernames;
        lock (CommonState.UserList)
        {
            usernames = CommonState.UserList
                .Select(item => item.Username)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        await Task.WhenAll(usernames.Select(username => WatchFriendSafelyAsync(username, cancellationToken)));
    }

    private async Task WatchFriendSafelyAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            UserData data = await client.WatchUserAsync(username, cancellationToken);
            userListService.UpdateExistingUser(
                username,
                data,
                new UserStatus(username, data.Status, false),
                out _);
            SetUserProfile(new UserProfileSnapshot(
                username,
                new UserStatus(username, data.Status, false)));
        }
        catch (UserNotFoundException)
        {
            userListService.SetDoesNotExist(username);
            RaiseUserChanged(username, SocialSessionChangeKind.UserList);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.FirebaseError($"Failed to watch persisted friend '{username}'.", exception);
        }
    }

    private async Task AutoJoinSafelyAsync(string roomName, CancellationToken cancellationToken)
    {
        try
        {
            await JoinRoomAsync(roomName, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.FirebaseError($"Automatic room join failed for '{roomName}'.", exception);
        }
    }

    private void OnRoomListReceived(object? sender, RoomList value) => SetRoomList(value);

    private void SetRoomList(RoomList value)
    {
        lock (stateSync)
        {
            roomList = value;
            roomListVersion++;
        }

        RaiseOnMain(RoomListChanged);
    }

    private void OnRoomMessageReceived(object? sender, RoomMessageReceivedEventArgs args)
    {
        if (userListService.IsUserInIgnoreList(args.Username) || EqualsName(args.Username, TryGetCurrentAccountName()))
        {
            return;
        }

        roomState.AddIncomingMessage(args.RoomName, args.Username, args.Message, DateTime.UtcNow);
        RoomSessionSnapshot snapshot = roomState.GetSnapshot(args.RoomName)!;
        if (snapshot.HasUnreadMessages && IsRoomPreferenceEnabled(notifyRoomNames, args.RoomName))
        {
            notifier.Post(new ChatroomMessageNotification(args.RoomName, args.Username, args.Message));
        }

        RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomMessage);
    }

    private void OnRoomJoined(object? sender, RoomJoinedEventArgs args)
    {
        if (!EqualsName(args.Username, TryGetCurrentAccountName()))
        {
            roomState.AddMember(args.RoomName, args.UserData, DateTime.UtcNow);
            RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomMembers);
        }
    }

    private void OnRoomLeft(object? sender, RoomLeftEventArgs args)
    {
        roomState.RemoveMember(args.RoomName, args.Username, DateTime.UtcNow);
        RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomMembers);
    }

    private void OnRoomTickerAdded(object? sender, RoomTickerAddedEventArgs args)
    {
        roomState.AddTicker(args.RoomName, args.Ticker);
        RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomTickers);
    }

    private void OnRoomTickerRemoved(object? sender, RoomTickerRemovedEventArgs args)
    {
        roomState.RemoveTicker(args.RoomName, args.Username);
        RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomTickers);
    }

    private void OnRoomTickerListReceived(object? sender, RoomTickerListReceivedEventArgs args)
    {
        roomState.ReplaceTickers(args.RoomName, args.Tickers);
        RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomTickers);
    }

    private void OnPrivateRoomOperatorChanged(object? sender, OperatorAddedRemovedEventArgs args)
    {
        roomState.UpdateModerator(args.RoomName, args.Username, args.Added);
        RaiseRoomChanged(args.RoomName, SocialSessionChangeKind.RoomModerators);
    }

    private void OnPrivateRoomMembershipAdded(object? sender, string roomName)
    {
        _ = RefreshRoomListAfterMembershipChangeAsync(roomName);
    }

    private void OnPrivateRoomMembershipRemoved(object? sender, string roomName)
    {
        roomState.CompleteLeave(roomName);
        lock (stateSync)
        {
            joinedRoomNames.Remove(roomName);
            privateRoomMembers.Remove(roomName);
        }

        SetAutoJoin(roomName, false);
        RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomConnection);
        _ = RefreshRoomListAfterMembershipChangeAsync(roomName);
    }

    private async Task RefreshRoomListAfterMembershipChangeAsync(string roomName)
    {
        try
        {
            await RefreshRoomListAsync(disposalCancellation.Token);
        }
        catch (OperationCanceledException) when (disposalCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.FirebaseError($"Failed to refresh private-room membership for '{roomName}'.", exception);
        }
    }

    private void OnPrivateRoomModeratorsReceived(object? sender, RoomInfo args)
    {
        roomState.ReplaceModerators(args.Name, args.Users);
        RaiseRoomChanged(args.Name, SocialSessionChangeKind.RoomModerators);
    }

    private void OnPrivateRoomUsersReceived(object? sender, RoomInfo args)
    {
        lock (stateSync)
        {
            privateRoomMembers[args.Name] = args.Users.ToArray();
        }

        RaiseRoomChanged(args.Name, SocialSessionChangeKind.RoomMembers);
    }

    private void OnUserStatusChanged(object? sender, UserStatus args)
    {
        IReadOnlyList<string> rooms = roomState.UpdateMemberStatus(args, DateTime.UtcNow);
        bool found = userListService.UpdateExistingUser(args.Username, null, args, out bool cameOnline);
        lock (stateSync)
        {
            UserProfileSnapshot profile = userProfiles.TryGetValue(args.Username, out UserProfileSnapshot? existing)
                ? existing.WithStatus(args)
                : new UserProfileSnapshot(args.Username, args);
            userProfiles[args.Username] = profile;
        }

        foreach (string roomName in rooms)
        {
            RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomMembers);
        }

        if (found)
        {
            RaiseUserChanged(args.Username, SocialSessionChangeKind.UserProfile);
            if (cameOnline && HasOnlineAlert(args.Username))
            {
                notifier.Post(new UserOnlineNotification(args.Username));
            }
        }

        if (DownloadService.Instance is not null)
        {
            DownloadService.Instance.RetryDownloadsIfUserBackOnline(args.Username, args.Presence);
        }
    }

    private void OnUserStatisticsChanged(object? sender, UserStatistics args)
    {
        UserData data = SimpleHelpers.UserStatisticsToUserData(args);
        userListService.UpdateExistingUser(args.Username, data, null, out _);
        lock (stateSync)
        {
            UserProfileSnapshot profile = userProfiles.TryGetValue(args.Username, out UserProfileSnapshot? existing)
                ? existing.WithStatistics(args)
                : new UserProfileSnapshot(args.Username, statistics: args);
            userProfiles[args.Username] = profile;
        }

        RaiseUserChanged(args.Username, SocialSessionChangeKind.UserProfile);
    }

    private void SetUserProfile(UserProfileSnapshot profile)
    {
        lock (stateSync)
        {
            userProfiles[profile.Username] = profile;
        }

        RaiseUserChanged(profile.Username, SocialSessionChangeKind.UserProfile);
    }

    private bool SetRoomPreference(
        ConcurrentDictionary<string, List<string>> roots,
        string roomName,
        bool enabled)
    {
        string account = CurrentAccountName;
        bool changed;
        ConcurrentDictionary<string, List<string>> autoJoinSnapshot;
        ConcurrentDictionary<string, List<string>> notifySnapshot;
        lock (roomPreferencesSync)
        {
            List<string> current = roots.TryGetValue(account, out List<string>? stored)
                ? stored
                : new List<string>();
            bool contains = current.Any(value => EqualsName(value, roomName));
            if (contains == enabled)
            {
                return false;
            }

            roots[account] = enabled
                ? current.Concat(new[] { roomName }).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : current.Where(value => !EqualsName(value, roomName)).ToList();
            autoJoinSnapshot = CloneRoomRoots(autoJoinRoomNames);
            notifySnapshot = CloneRoomRoots(notifyRoomNames);
            changed = true;
        }

        appDataStateService.PersistRoomPreferences(autoJoinSnapshot, notifySnapshot);
        RaiseRoomChanged(roomName, SocialSessionChangeKind.RoomPreferences);
        return changed;
    }

    private IReadOnlyList<string> GetAccountRoomNames(ConcurrentDictionary<string, List<string>> roots)
    {
        string? account = TryGetCurrentAccountName();
        if (account is null)
        {
            return Array.Empty<string>();
        }

        lock (roomPreferencesSync)
        {
            return roots.TryGetValue(account, out List<string>? roomNames)
                ? roomNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        }
    }

    private bool IsRoomPreferenceEnabled(
        ConcurrentDictionary<string, List<string>> roots,
        string roomName) => GetAccountRoomNames(roots).Any(value => EqualsName(value, roomName));

    private bool HasOnlineAlert(string username) =>
        UserMetadataService.UserOnlineAlerts.Keys.Any(value => EqualsName(value, username));

    private string CurrentAccountName => TryGetCurrentAccountName() ??
        throw new InvalidOperationException("A signed-in Soulseek account is required.");

    private string? TryGetCurrentAccountName()
    {
        string? account = PreferencesState.Username;
        if (!string.IsNullOrWhiteSpace(account))
        {
            return account;
        }

        return string.IsNullOrWhiteSpace(client.Username) ? null : client.Username;
    }

    private void RaiseRoomChanged(string roomName, SocialSessionChangeKind kind) =>
        mainThreadRunner.RunOnUiThread(() =>
            RoomChanged?.Invoke(this, new RoomStateChangedEventArgs(roomName, kind)));

    private void RaiseUserChanged(string username, SocialSessionChangeKind kind) =>
        mainThreadRunner.RunOnUiThread(() =>
            UserChanged?.Invoke(this, new SocialUserChangedEventArgs(username, kind)));

    private void RaiseOnMain(EventHandler? handler) =>
        mainThreadRunner.RunOnUiThread(() => handler?.Invoke(this, EventArgs.Empty));

    private static ConcurrentDictionary<string, List<string>> NormalizeRoomRoots(
        ConcurrentDictionary<string, List<string>> source) =>
        new(
            source
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => new KeyValuePair<string, List<string>>(
                    pair.Key,
                    (pair.Value ?? new List<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList())),
            StringComparer.OrdinalIgnoreCase);

    private static ConcurrentDictionary<string, List<string>> CloneRoomRoots(
        ConcurrentDictionary<string, List<string>> source) =>
        new(
            source.Select(pair => new KeyValuePair<string, List<string>>(pair.Key, pair.Value.ToList())),
            StringComparer.OrdinalIgnoreCase);

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A social-session value cannot be empty or whitespace.", parameterName)
            : value;

    private static bool EqualsName(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
