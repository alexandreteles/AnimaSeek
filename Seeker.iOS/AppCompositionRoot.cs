using AnimaSeek.iOS.Services;
using Common;
using Common.Messages;
using Common.Search;
using Foundation;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Soulseek;
using Soulseek.Diagnostics;

namespace AnimaSeek.iOS;

/// <summary>Builds and owns the singletons shared by all iOS scenes and controllers.</summary>
internal static class AppCompositionRoot
{
    private static readonly Lock Sync = new();
    private static bool initialized;
    private static volatile bool transfersRestored;

    /// <summary>Gets the sandbox filesystem service after <see cref="Initialize"/> completes.</summary>
    public static IosFileSystemService FileSystem { get; private set; } = null!;

    /// <summary>Gets the active application session after <see cref="Initialize"/> completes.</summary>
    public static AppSession Session { get; private set; } = null!;

    /// <summary>Gets the non-UI chatroom, user, presence, and private-room coordinator.</summary>
    public static IosSocialSessionService Social { get; private set; } = null!;

    /// <summary>Gets the canonical friend and ignored-user service through its detached snapshot surface.</summary>
    public static IosUserListService UserLists { get; private set; } = null!;

    /// <summary>Gets the transient-message presenter after <see cref="Initialize"/> completes.</summary>
    public static IosToaster Toaster { get; private set; } = null!;

    /// <summary>Gets the rotating logger after <see cref="Initialize"/> completes.</summary>
    public static IosLoggerBackend LoggerBackend { get; private set; } = null!;

    /// <summary>Gets the settings import coordinator after <see cref="Initialize"/> completes.</summary>
    public static SettingsImportService SettingsImporter { get; private set; } = null!;

    /// <summary>Gets the sandbox share indexer after <see cref="Initialize"/> completes.</summary>
    public static IosShareIndexService ShareIndex { get; private set; } = null!;

    /// <summary>Gets the server-cadenced foreground and background wishlist runner.</summary>
    public static IosWishlistService Wishlist { get; private set; } = null!;

    /// <summary>Gets own-profile, credential, privileges, and portable export operations.</summary>
    public static IosAccountService Account { get; private set; } = null!;

    /// <summary>Gets the semantic local-notification bridge and typed route events.</summary>
    public static IosNotifier Notifications { get; private set; } = null!;

    /// <summary>Gets the opt-in transfer idle-timer coordinator.</summary>
    public static IosIdleTimerCoordinator IdleTimer { get; private set; } = null!;

    /// <summary>Gets the portable iOS preference adapter after <see cref="Initialize"/> completes.</summary>
    public static IKeyValueStore KeyValueStore { get; private set; } = null!;

    /// <summary>Gets the canonical portable application-data persistence coordinator.</summary>
    public static PortableAppDataStateService AppDataState { get; private set; } = null!;

    /// <summary>Gets the atomic file-backed portable transfer snapshot store.</summary>
    public static IosTransferStateStore TransferStateStore { get; private set; } = null!;

    /// <summary>Gets the iOS-only NAT-PMP listener mapping coordinator.</summary>
    public static NatPmpPortMappingService PortMapping { get; private set; } = null!;

    /// <summary>Gets the iOS lifecycle and continued-transfer coordinator.</summary>
    public static IosBackgroundTaskCoordinator BackgroundTasks { get; private set; } = null!;

    /// <summary>Gets the foreground-only stepped-backoff session recovery service.</summary>
    public static IosForegroundReconnectService ForegroundReconnect { get; private set; } = null!;

    /// <summary>Gets the scene-independent Soulseek URL router.</summary>
    public static DeepLinkRouter DeepLinks { get; } = new();

    /// <summary>
    /// Gets a task that completes after transfer snapshots restore and crash-recovery reconciliation finish off the
    /// launch path. Resume, reconcile, and share-index work must await it so partially restored state is never observed.
    /// </summary>
    internal static Task Restoration { get; private set; } = Task.CompletedTask;

    /// <summary>Creates the process service graph once and restores local state.</summary>
    public static void Initialize()
    {
        lock (Sync)
        {
            if (initialized)
            {
                return;
            }

            // BGTask identifier registration and service wiring must stay synchronous inside FinishedLaunching;
            // the file-backed transfer restore and crash-recovery reconcile run behind the Restoration gate below.
            BackgroundTaskRegistrar.Register();
            var restorationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Restoration = restorationCompletion.Task;
            KeyValueStore = new UserDefaultsKeyValueStore(NSUserDefaults.StandardUserDefaults);
            new PreferencesStateRestorer(KeyValueStore).RestoreAll();
            AppDataState = new PortableAppDataStateService(KeyValueStore);
            PortableAppDataRestoreResult restoredAppData = AppDataState.RestoreAll();
            FileSystem = new IosFileSystemService();
            TransferStateStore = new IosTransferStateStore(FileSystem.ApplicationSupportPath, KeyValueStore);
            var mainThreadRunner = new IosMainThreadRunner();
            Toaster = new IosToaster(mainThreadRunner);
            LoggerBackend = new IosLoggerBackend(FileSystem.DocumentsPath);
            Logger.Backend = LoggerBackend;
            MicroTagReader.Instance = new MicroTagReader(LoggerBackend);

            var networkStatus = new IosNetworkStatus();
            var notifier = new IosNotifier();
            Notifications = notifier;
            var wishlistMutationGate = new WishlistStateMutationGate();
            var userList = new IosUserListService(KeyValueStore, mainThreadRunner, LoggerBackend);
            UserLists = userList;
            SimpleHelpers.UserListService = userList;
            var cache = new IosCacheDataProvider(KeyValueStore);
            cache.EnsureCacheExists();

            ShareIndex = new IosShareIndexService(
                FileSystem,
                userList,
                networkStatus,
                LoggerBackend,
                CheckpointTransfers);
            ISoulseekClient client = CreateClient(ShareIndex);
            var privateMessages = new PrivateMessageSessionState(
                restoredAppData.State.PrivateMessageRoots,
                restoredAppData.State.LastReadMessageCounts);
            var interruptedDownloads = new IosInterruptedDownloadStore(KeyValueStore);

            Session = new AppSession(
                client,
                KeyValueStore,
                FileSystem,
                notifier,
                mainThreadRunner,
                privateMessages,
                AppDataState,
                interruptedDownloads,
                CheckpointTransfers,
                TryCheckpointTransfers,
                Toaster,
                LoggerBackend);
            ShareIndex.AttachClient(client);
            Wishlist = new IosWishlistService(
                client,
                KeyValueStore,
                notifier,
                LoggerBackend,
                wishlistMutationGate);
            Account = new IosAccountService(
                Session,
                KeyValueStore,
                FileSystem,
                userList,
                Wishlist,
                AppDataState,
                LoggerBackend);
            IdleTimer = new IosIdleTimerCoordinator(Session, KeyValueStore, mainThreadRunner);
            PortMapping = new NatPmpPortMappingService(
                networkStatus,
                () => Session.IsConnected,
                LoggerBackend);
            ForegroundReconnect = new IosForegroundReconnectService(
                Session,
                networkStatus,
                LoggerBackend);
            Session.StateChanged += OnSessionStateChanged;
            var sessionService = new IosSessionService(Session, mainThreadRunner);
            DownloadService.Instance = new DownloadService(
                Toaster,
                FileSystem,
                sessionService,
                mainThreadRunner,
                () => Session.Client,
                LoggerBackend,
                networkStatus);
            Social = new IosSocialSessionService(
                Session,
                userList,
                notifier,
                mainThreadRunner,
                KeyValueStore,
                AppDataState,
                restoredAppData,
                LoggerBackend);
            BackgroundTasks = new IosBackgroundTaskCoordinator(
                Session,
                interruptedDownloads,
                CheckpointTransfers,
                Restoration,
                Toaster,
                LoggerBackend);
            BackgroundTasks.SetWishlistRefreshOperation(
                RunBackgroundWishlistRefreshAsync,
                () => Wishlist.ServerInterval);

            SettingsImporter = new SettingsImportService(
                userList,
                KeyValueStore,
                mainThreadRunner,
                AppDataState,
                LoggerBackend,
                wishlistMutationGate,
                Social);
            DownloadService.Instance.SuccessToastDeferredUntilTerminalProcessed = true;
            initialized = true;
            _ = Task.Run(() => RestoreDurableStateAsync(restorationCompletion));
        }
    }

    private static async Task RestoreDurableStateAsync(TaskCompletionSource completion)
    {
        try
        {
            RestoreTransfers();
            transfersRestored = true;
            Session.ReconcileInterruptedDownloadFinalizations();
        }
        catch (Exception exception)
        {
            LoggerBackend.FirebaseError("Deferred durable-state restoration failed.", exception);
        }
        finally
        {
            // The gate always completes so waiters cannot hang; a failed restore is logged above and the
            // checkpoint gate stays closed, protecting the on-disk snapshot from an empty overwrite.
            completion.SetResult();
        }

        await RefreshSharesAfterInitializationAsync().ConfigureAwait(false);
    }

    /// <summary>Requests notification permission only after an explicit future-UI feature action.</summary>
    /// <returns><see langword="true"/> when iOS grants notification authorization.</returns>
    public static Task<bool> RequestNotificationAuthorizationAsync() =>
        Session.RequestNotificationAuthorizationAsync();

    /// <summary>Persists and applies the keep-screen-awake preference while live transfers exist.</summary>
    /// <param name="enabled">Whether active foreground transfers may disable the idle timer.</param>
    public static void SetKeepScreenAwakeWhileTransferring(bool enabled) =>
        IdleTimer.SetKeepAwakeEnabled(enabled);

    /// <summary>Updates foreground wishlist, idle-timer, reconnect, and resume behavior for the active scene.</summary>
    /// <param name="active"><see langword="true"/> while the scene is active.</param>
    public static void SetSceneActive(bool active)
    {
        if (!initialized)
        {
            return;
        }

        Wishlist.SetForegroundActive(active);
        IdleTimer.SetSceneActive(active);
        if (active)
        {
            PortMapping.Resume();
            ForegroundReconnect.SceneBecameActive();
            PortMapping.RefreshIfNeeded();
            if (Restoration.IsCompleted)
            {
                // Before restoration completes, the restoration worker itself runs the initial share refresh.
                ShareIndex.RequestRefresh();
            }

            BackgroundTasks.ResumeInterruptedDownloads();
        }
        else
        {
            ForegroundReconnect.SceneWillResignActive();
        }
    }

    /// <summary>Refreshes the immutable Documents catalog and publishes its counts when connected.</summary>
    /// <param name="cancellationToken">Cancels filesystem enumeration or server count publication.</param>
    /// <returns>A task that completes after the new catalog is visible to every resolver.</returns>
    public static async Task RefreshSharesAsync(CancellationToken cancellationToken = default)
    {
        await Restoration.ConfigureAwait(false);
        await ShareIndex.RefreshAsync(cancellationToken);
    }

    /// <summary>Persists sharing state and publishes either a refreshed catalog or zero counts.</summary>
    /// <param name="enabled">Whether the Documents catalog is visible to peers.</param>
    /// <param name="cancellationToken">Cancels refresh or server publication.</param>
    public static async Task SetSharingEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        PreferencesState.SharingOn = enabled;
        KeyValueStore.PutBoolean(KeyConsts.M_SharingOn, enabled);
        KeyValueStore.Flush();
        if (enabled)
        {
            await Restoration.ConfigureAwait(false);
            await ShareIndex.RefreshAsync(cancellationToken);
        }
        else
        {
            await ShareIndex.PublishSharedCountsAsync(cancellationToken);
        }
    }

    /// <summary>Pauses NAT-PMP discovery and renewal before iOS suspends the scene.</summary>
    public static void SuspendPortMapping()
    {
        if (initialized)
        {
            PortMapping.Suspend();
        }
    }

    /// <summary>Saves portable transfer state when it changed, for scene backgrounding and task expiration.</summary>
    public static void CheckpointTransfers() => _ = TryCheckpointTransfers();

    /// <summary>Persists transfer state and reports whether the critical checkpoint reached atomic storage.</summary>
    /// <returns><see langword="true"/> when current manager state is durable.</returns>
    internal static bool TryCheckpointTransfers()
    {
        if (!initialized || !transfersRestored)
        {
            // Persisting before the deferred restore completes would overwrite the durable snapshot with empty state.
            return false;
        }

        try
        {
            (string downloads, string uploads)? state = TransferPersistence.SaveTransferItems(force: true);
            if (state is null)
            {
                return false;
            }

            TransferStateStore.Save(state.Value.downloads, state.Value.uploads);
            return true;
        }
        catch (Exception exception)
        {
            LoggerBackend.FirebaseError("Transfer checkpoint failed.", exception);
            return false;
        }
    }

    private static ISoulseekClient CreateClient(IosShareIndexService shareIndex)
    {
        var options = new SoulseekClientOptions(
            enableListener: PreferencesState.ListenerEnabled,
            listenPort: PreferencesState.ListenerPort,
            maximumConcurrentSearches: 5,
            maximumConcurrentDownloads: PreferencesState.LimitSimultaneousDownloads
                ? PreferencesState.MaxSimultaneousLimit
                : int.MaxValue,
            messageTimeout: 30_000,
            autoAcknowledgePrivateMessages: false,
            acceptPrivateRoomInvitations: PreferencesState.AllowPrivateRoomInvitations,
            minimumDiagnosticLevel: PreferencesState.LogDiagnostics ? DiagnosticLevel.Debug : DiagnosticLevel.Info,
            searchResponseResolver: shareIndex.ResolveSearchAsync,
            browseResponseResolver: shareIndex.ResolveBrowseAsync,
            directoryContentsResolver: shareIndex.ResolveDirectoryAsync,
            userInfoResolver: (username, endPoint) => Account.ResolveUserInfoAsync(username, endPoint),
            enqueueDownload: shareIndex.EnqueueUploadAsync);
#if MOCK
        return new MockSoulseekClient
        {
            Options = options,
        };
#else
        return new SoulseekClient(
            SoulseekClientIdentity.MinorVersion,
            options);
#endif
    }

    private static async Task<bool> RunBackgroundWishlistRefreshAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        return await Session.RunBackgroundConnectedAsync(
            Wishlist.RunOnceAsync,
            timeout.Token);
    }

    private static async Task RefreshSharesAfterInitializationAsync()
    {
        try
        {
            await ShareIndex.RefreshAsync();
        }
        catch (Exception exception)
        {
            LoggerBackend.FirebaseError("Initial Documents share indexing failed.", exception);
        }
    }

    private static void OnSessionStateChanged(object? sender, EventArgs args)
    {
        if (Session.IsConnected)
        {
            PortMapping.RefreshIfNeeded();
            BackgroundTasks.ResumeInterruptedDownloads();
        }
    }

    private static void RestoreTransfers()
    {
        try
        {
            (string downloads, string legacyDownloads) = TransferStateStore.LoadDownloads();
            TransferPersistence.RestoreDownloadTransferItems(downloads, legacyDownloads);
        }
        catch (Exception exception)
        {
            LoggerBackend.FirebaseError("Stored download restoration failed; starting with an empty download list.", exception);
            TransferItems.TransferItemManagerDL = new TransferItemManager();
        }

        try
        {
            (string uploads, string legacyUploads) = TransferStateStore.LoadUploads();
            TransferPersistence.RestoreUploadTransferItems(uploads, legacyUploads);
        }
        catch (Exception exception)
        {
            LoggerBackend.FirebaseError("Stored upload restoration failed; starting with an empty upload list.", exception);
            TransferItems.TransferItemManagerUploads = new TransferItemManager(true);
        }

        TransferItems.TransferItemManagerWrapped = new TransferItemManagerWrapper(
            TransferItems.TransferItemManagerUploads,
            TransferItems.TransferItemManagerDL,
            CleanupIncompleteTransfer);
    }

    private static void CleanupIncompleteTransfer(TransferItem transfer)
    {
        if (string.IsNullOrWhiteSpace(transfer?.IncompleteUri))
        {
            return;
        }

        try
        {
            // An app update regenerates the container UUID; rebasing onto the identity-derived current path keeps
            // an otherwise-stale absolute path from orphaning the real partial.
            FileSystem.DeleteIncompleteFile(
                FileSystem.RebaseIncompletePath(transfer.IncompleteUri, transfer.Username, transfer.FullFilename));
        }
        catch (Exception exception)
        {
            LoggerBackend.FirebaseError("Refused or failed to remove an incomplete transfer file.", exception);
        }
    }
}
