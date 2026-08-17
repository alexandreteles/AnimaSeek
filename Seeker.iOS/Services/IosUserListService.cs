using Common;
using Seeker;
using Seeker.Services;
using Soulseek;
using SeekerUserRole = Seeker.UserRole;

namespace AnimaSeek.iOS.Services;

/// <summary>Describes one detached friend or ignored-user entry for presentation consumers.</summary>
/// <param name="Username">The Soulseek user name.</param>
/// <param name="Role">Whether the entry is a friend or an ignored user.</param>
/// <param name="Presence">The most recently observed presence.</param>
/// <param name="DoesNotExist">Whether the server reported that the account does not exist.</param>
/// <param name="AverageSpeed">The most recently observed average upload speed.</param>
/// <param name="FileCount">The most recently observed shared-file count.</param>
/// <param name="DirectoryCount">The most recently observed shared-directory count.</param>
internal sealed record IosUserListEntrySnapshot(
    string Username,
    SeekerUserRole Role,
    UserPresence Presence,
    bool DoesNotExist,
    int? AverageSpeed,
    int? FileCount,
    int? DirectoryCount);

/// <summary>Captures the canonical in-memory user lists before a multi-store import transaction.</summary>
/// <param name="Friends">The friend entries as they existed before the transaction.</param>
/// <param name="IgnoredUsers">The ignored entries as they existed before the transaction.</param>
internal sealed record IosUserListRollbackSnapshot(
    IReadOnlyList<UserListItem> Friends,
    IReadOnlyList<UserListItem> IgnoredUsers);

/// <summary>Maintains the portable friend and ignore lists and persists their user-name snapshots.</summary>
internal sealed class IosUserListService : IUserListService
{
    private const string LegacyUserListKey = "ios.user-list";
    private const string LegacyIgnoreListKey = "ios.ignore-list";
    private readonly IKeyValueStore store;
    private readonly IMainThreadRunner mainThreadRunner;
    private readonly Seeker.Helpers.ILoggerBackend logger;

    /// <summary>Creates the service and restores any previously stored user names.</summary>
    /// <param name="store">The preference store used for list snapshots.</param>
    /// <param name="mainThreadRunner">Delivers observable list changes to UIKit consumers.</param>
    /// <param name="logger">Records corruption isolated to either canonical list.</param>
    public IosUserListService(
        IKeyValueStore store,
        IMainThreadRunner mainThreadRunner,
        Seeker.Helpers.ILoggerBackend logger)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Restore();
    }

    /// <summary>Raised when either list changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Captures list membership so an importing coordinator can restore it after a later failure.</summary>
    /// <returns>A detached membership snapshot retaining each entry's current metadata.</returns>
    internal IosUserListRollbackSnapshot CaptureRollbackSnapshot()
    {
        lock (CommonState.UserList)
        {
            lock (CommonState.IgnoreUserList)
            {
                return new IosUserListRollbackSnapshot(
                    CommonState.UserList.ToArray(),
                    CommonState.IgnoreUserList.ToArray());
            }
        }
    }

    /// <summary>Restores a captured membership snapshot and persists the restored canonical lists.</summary>
    /// <param name="snapshot">The state captured immediately before an import commit.</param>
    internal void RestoreRollbackSnapshot(IosUserListRollbackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (CommonState.UserList)
        {
            lock (CommonState.IgnoreUserList)
            {
                CommonState.UserList.Clear();
                CommonState.UserList.AddRange(snapshot.Friends);
                CommonState.IgnoreUserList.Clear();
                CommonState.IgnoreUserList.AddRange(snapshot.IgnoredUsers);
            }
        }

        try
        {
            Persist();
        }
        finally
        {
            // Even a storage failure must publish the restored in-memory state to active UIKit consumers.
            RaiseChanged();
        }
    }

    /// <summary>Gets a stable, detached, deterministically ordered snapshot of both canonical lists.</summary>
    /// <returns>Friend entries followed by ignored entries, with each group ordered by user name.</returns>
    public IReadOnlyList<IosUserListEntrySnapshot> GetSnapshot()
    {
        UserListItem[] friends;
        UserListItem[] ignored;
        lock (CommonState.UserList)
        {
            friends = CommonState.UserList.ToArray();
        }

        lock (CommonState.IgnoreUserList)
        {
            ignored = CommonState.IgnoreUserList.ToArray();
        }

        return friends
            .Concat(ignored)
            .Where(item => item is not null && IsValidUsername(item.Username))
            .OrderBy(item => item.Role == SeekerUserRole.Ignored)
            .ThenBy(item => item.Username, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new IosUserListEntrySnapshot(
                item.Username,
                item.Role,
                item.UserStatus?.Presence ?? item.UserData?.Status ?? UserPresence.Offline,
                item.DoesNotExist,
                item.UserData?.AverageSpeed,
                item.UserData?.FileCount,
                item.UserData?.DirectoryCount))
            .ToArray();
    }

    /// <inheritdoc/>
    public bool ContainsUser(string username)
    {
        lock (CommonState.UserList)
        {
            return CommonState.UserList.Any(item => EqualsUsername(item.Username, username));
        }
    }

    /// <inheritdoc/>
    public bool UpdateExistingUser(
        string username,
        UserData? userData,
        UserStatus? userStatus,
        out bool transitionedOfflineToOnline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        transitionedOfflineToOnline = false;
        bool found = false;
        lock (CommonState.UserList)
        {
            UserListItem? existing = CommonState.UserList.FirstOrDefault(item => EqualsUsername(item.Username, username));
            if (existing is not null)
            {
                UserPresence previousPresence = existing.UserStatus?.Presence ??
                    existing.UserData?.Status ??
                    UserPresence.Offline;
                existing.UserData = userData ?? existing.UserData;
                existing.UserStatus = userStatus ?? existing.UserStatus;
                existing.DoesNotExist = false;
                transitionedOfflineToOnline =
                    userStatus is not null &&
                    previousPresence == UserPresence.Offline &&
                    userStatus.Presence != UserPresence.Offline;
                found = true;
            }
        }

        if (found)
        {
            RaiseChanged();
        }

        return found;
    }

    /// <inheritdoc/>
    public bool SetDoesNotExist(string username)
    {
        lock (CommonState.UserList)
        {
            UserListItem? item = CommonState.UserList.FirstOrDefault(candidate => EqualsUsername(candidate.Username, username));
            if (item is null)
            {
                return false;
            }

            item.DoesNotExist = true;
        }

        Persist();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc/>
    public bool AddUser(UserData userData, UserPresence? status = null)
    {
        ArgumentNullException.ThrowIfNull(userData);
        RemoveFromIgnoreList(userData.Username);
        bool updatedExisting;

        lock (CommonState.UserList)
        {
            UserListItem? existing = CommonState.UserList.FirstOrDefault(item => EqualsUsername(item.Username, userData.Username));
            if (existing is not null)
            {
                existing.UserData = userData;
                existing.UserStatus = status is null
                    ? existing.UserStatus
                    : new UserStatus(userData.Username, status.Value, existing.UserStatus?.IsPrivileged ?? false);
                existing.DoesNotExist = false;
                updatedExisting = true;
            }
            else
            {
                CommonState.UserList.Add(new UserListItem(userData.Username, SeekerUserRole.Friend)
                {
                    UserData = userData,
                    UserStatus = status is null ? null : new UserStatus(userData.Username, status.Value, false),
                });
                updatedExisting = false;
            }
        }

        Persist();
        RaiseChanged();
        return updatedExisting;
    }

    /// <inheritdoc/>
    public bool RemoveUser(string username)
    {
        bool removed;
        lock (CommonState.UserList)
        {
            removed = CommonState.UserList.RemoveAll(item => EqualsUsername(item.Username, username)) > 0;
        }

        if (removed)
        {
            Persist();
            RaiseChanged();
        }

        return removed;
    }

    /// <inheritdoc/>
    public bool AddToIgnoreList(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        RemoveUser(username);

        lock (CommonState.IgnoreUserList)
        {
            if (CommonState.IgnoreUserList.Any(item => EqualsUsername(item.Username, username)))
            {
                return false;
            }

            CommonState.IgnoreUserList.Add(new UserListItem(username, SeekerUserRole.Ignored));
        }

        Persist();
        RaiseChanged();
        return true;
    }

    /// <inheritdoc/>
    public bool RemoveFromIgnoreList(string username)
    {
        bool removed;
        lock (CommonState.IgnoreUserList)
        {
            removed = CommonState.IgnoreUserList.RemoveAll(item => EqualsUsername(item.Username, username)) > 0;
        }

        if (removed)
        {
            Persist();
            RaiseChanged();
        }

        return removed;
    }

    /// <inheritdoc/>
    public bool IsUserInIgnoreList(string username)
    {
        lock (CommonState.IgnoreUserList)
        {
            return CommonState.IgnoreUserList.Any(item => EqualsUsername(item.Username, username));
        }
    }

    /// <summary>Merges imported friend and ignore user names without requiring a network connection.</summary>
    /// <param name="friends">Friend user names from an import file.</param>
    /// <param name="ignoredUsers">Ignored user names from an import file.</param>
    /// <returns>The numbers of new friend and ignore entries added to canonical state.</returns>
    public (int FriendsAdded, int IgnoredAdded) Import(
        IEnumerable<string> friends,
        IEnumerable<string> ignoredUsers) =>
        ImportCore(friends, ignoredUsers, publishChanges: true);

    /// <summary>Merges imported membership while deferring the observable change until a wider commit succeeds.</summary>
    /// <param name="friends">Friend user names from an import file.</param>
    /// <param name="ignoredUsers">Ignored user names from an import file.</param>
    /// <returns>The numbers of new friend and ignore entries added to canonical state.</returns>
    internal (int FriendsAdded, int IgnoredAdded) ImportForTransaction(
        IEnumerable<string> friends,
        IEnumerable<string> ignoredUsers) =>
        ImportCore(friends, ignoredUsers, publishChanges: false);

    /// <summary>Publishes a previously persisted transactional list change to UIKit observers.</summary>
    internal void PublishTransactionalChange() => RaiseChanged();

    /// <summary>Merges normalized membership and optionally publishes the resulting snapshot.</summary>
    /// <param name="friends">Friend user names from an import file.</param>
    /// <param name="ignoredUsers">Ignored user names from an import file.</param>
    /// <param name="publishChanges">Whether observers should see the new membership immediately.</param>
    /// <returns>The numbers of new friend and ignore entries added to canonical state.</returns>
    private (int FriendsAdded, int IgnoredAdded) ImportCore(
        IEnumerable<string> friends,
        IEnumerable<string> ignoredUsers,
        bool publishChanges)
    {
        var ignored = ignoredUsers.Where(IsValidUsername).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var friendNames = friends.Where(IsValidUsername).Where(username => !ignored.Contains(username));
        int friendsAdded = 0;
        int ignoredAdded = 0;

        lock (CommonState.UserList)
        {
            lock (CommonState.IgnoreUserList)
            {
                foreach (string username in friendNames)
                {
                    if (!CommonState.UserList.Any(item => EqualsUsername(item.Username, username)) &&
                        !CommonState.IgnoreUserList.Any(item => EqualsUsername(item.Username, username)))
                    {
                        CommonState.UserList.Add(new UserListItem(username, SeekerUserRole.Friend));
                        friendsAdded++;
                    }
                }

                foreach (string username in ignored)
                {
                    CommonState.UserList.RemoveAll(item => EqualsUsername(item.Username, username));
                    if (!CommonState.IgnoreUserList.Any(item => EqualsUsername(item.Username, username)))
                    {
                        CommonState.IgnoreUserList.Add(new UserListItem(username, SeekerUserRole.Ignored));
                        ignoredAdded++;
                    }
                }
            }
        }

        Persist();
        if (publishChanges)
        {
            RaiseChanged();
        }

        return (friendsAdded, ignoredAdded);
    }

    private void Restore()
    {
        string? serializedUsers = store.GetString(KeyConsts.M_UserList);
        string? serializedIgnoredUsers = store.GetString(KeyConsts.M_IgnoreUserList);
        bool hasCanonicalState = serializedUsers is not null || serializedIgnoredUsers is not null;

        CommonState.UserList = RestoreCanonical(serializedUsers, SeekerUserRole.Friend);
        CommonState.IgnoreUserList = RestoreCanonical(serializedIgnoredUsers, SeekerUserRole.Ignored);

        if (!hasCanonicalState)
        {
            CommonState.UserList = RestoreLegacySet(LegacyUserListKey, SeekerUserRole.Friend);
            CommonState.IgnoreUserList = RestoreLegacySet(LegacyIgnoreListKey, SeekerUserRole.Ignored);
            Persist();
        }
    }

    private void Persist()
    {
        List<UserListItem> userSnapshot;
        List<UserListItem> ignoredSnapshot;
        lock (CommonState.UserList)
        {
            lock (CommonState.IgnoreUserList)
            {
                userSnapshot = CommonState.UserList.ToList();
                ignoredSnapshot = CommonState.IgnoreUserList.ToList();
            }
        }

        store.PutString(KeyConsts.M_UserList, SerializationHelper.SaveUserListToString(userSnapshot));
        store.PutString(KeyConsts.M_IgnoreUserList, SerializationHelper.SaveUserListToString(ignoredSnapshot));
        store.PutStringSet(LegacyUserListKey, null);
        store.PutStringSet(LegacyIgnoreListKey, null);
        store.Flush();
    }

    private List<UserListItem> RestoreCanonical(string? payload, SeekerUserRole role)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return [];
        }

        try
        {
            return SerializationHelper.RestoreUserListFromString(payload)
                .Where(item => item is not null && IsValidUsername(item.Username))
                .GroupBy(item => item.Username, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch (Exception exception)
        {
            logger.FirebaseError($"The stored {role} user list is corrupt; only that list was reset.", exception);
            return [];
        }
    }

    private List<UserListItem> RestoreLegacySet(string key, SeekerUserRole role) =>
        (store.GetStringSet(key) ?? [])
            .Where(IsValidUsername)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(username => new UserListItem(username, role))
            .ToList();

    private void RaiseChanged() =>
        mainThreadRunner.RunOnUiThread(() => Changed?.Invoke(this, EventArgs.Empty));

    private static bool EqualsUsername(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidUsername(string username) => !string.IsNullOrWhiteSpace(username);
}
