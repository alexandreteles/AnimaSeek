using AnimaSeek.iOS.Services;
using Common;
using Common.Messages;
using Common.Share;
using Foundation;
using Seeker;
using Seeker.Services;
using Seeker.Social;
using Soulseek;

namespace AnimaSeek.iOS.UI.Presentation;

/// <summary>Identifies the native control used to present an applicable iOS setting.</summary>
internal enum SettingsControlKind
{
    Toggle,
    Value,
    Action,
    Navigation,
    Information,
    Destructive,
}

/// <summary>Describes one catalog-backed, searchable settings row.</summary>
/// <param name="Id">A stable, nonlocalized command identifier.</param>
/// <param name="Section">The localized settings group title.</param>
/// <param name="Title">The localized row title.</param>
/// <param name="Detail">The localized explanation.</param>
/// <param name="Kind">The native presentation control.</param>
/// <param name="IsOn">The current toggle state, when applicable.</param>
/// <param name="Value">The formatted value, when applicable.</param>
internal sealed record SettingsRow(
    string Id,
    string Section,
    string Title,
    string Detail,
    SettingsControlKind Kind,
    bool? IsOn = null,
    string? Value = null);

/// <summary>Describes one persisted speed governor in understandable kilobytes-per-second units.</summary>
/// <param name="Enabled">Whether the governor currently applies.</param>
/// <param name="KilobytesPerSecond">The positive configured rate in KB/s.</param>
/// <param name="PerTransfer">Whether the configured rate applies to each transfer instead of their total.</param>
internal sealed record SettingsSpeedLimit(
    bool Enabled,
    int KilobytesPerSecond,
    bool PerTransfer);

/// <summary>Coordinates applicable portable and iOS settings without exposing raw persistence to UIKit.</summary>
internal sealed class SettingsPresentationStore
{
    /// <summary>The smallest supported nonzero speed limit, in kilobytes per second.</summary>
    public const int MinimumSpeedLimitKilobytesPerSecond = 1;

    /// <summary>The largest rate that can be represented safely by the portable bytes-per-second preference.</summary>
    public const int MaximumSpeedLimitKilobytesPerSecond = int.MaxValue / 1_024;

    private readonly IKeyValueStore keyValueStore;
    private readonly PreferencesStateWriter writer;
    private readonly IosSocialSessionService social;
    private readonly IosShareIndexService shareIndex;
    private readonly NatPmpPortMappingService portMapping;
    private readonly Func<bool, CancellationToken, Task> setSharing;
    private readonly Func<CancellationToken, Task> refreshShares;
    private readonly Func<Task<bool>> requestNotifications;
    private readonly Func<Task<NotificationAuthorizationState>> getNotificationAuthorization;
    private readonly Action<bool> setKeepAwake;
    private readonly Func<bool, int, CancellationToken, Task<bool>> reconfigureListener;
    private NotificationAuthorizationState notificationAuthorizationState = NotificationAuthorizationState.Loading;

    /// <summary>Creates a settings store over side-effect-capable application services.</summary>
    /// <param name="keyValueStore">The portable preference destination.</param>
    /// <param name="social">The live social side-effect coordinator.</param>
    /// <param name="shareIndex">The immutable share catalog source.</param>
    /// <param name="portMapping">The NAT-PMP diagnostic and refresh coordinator.</param>
    /// <param name="setSharing">Persists and applies sharing state.</param>
    /// <param name="refreshShares">Rebuilds the Documents share index.</param>
    /// <param name="requestNotifications">Requests the system notification authorization.</param>
    /// <param name="getNotificationAuthorization">Reads the current system notification authorization without prompting.</param>
    /// <param name="setKeepAwake">Applies the active-transfer idle-timer preference.</param>
    /// <param name="reconfigureListener">Applies listener enabled/port changes to the active client.</param>
    public SettingsPresentationStore(
        IKeyValueStore keyValueStore,
        IosSocialSessionService social,
        IosShareIndexService shareIndex,
        NatPmpPortMappingService portMapping,
        Func<bool, CancellationToken, Task> setSharing,
        Func<CancellationToken, Task> refreshShares,
        Func<Task<bool>> requestNotifications,
        Func<Task<NotificationAuthorizationState>> getNotificationAuthorization,
        Action<bool> setKeepAwake,
        Func<bool, int, CancellationToken, Task<bool>> reconfigureListener)
    {
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        writer = new PreferencesStateWriter(this.keyValueStore);
        this.social = social ?? throw new ArgumentNullException(nameof(social));
        this.shareIndex = shareIndex ?? throw new ArgumentNullException(nameof(shareIndex));
        this.portMapping = portMapping ?? throw new ArgumentNullException(nameof(portMapping));
        this.setSharing = setSharing ?? throw new ArgumentNullException(nameof(setSharing));
        this.refreshShares = refreshShares ?? throw new ArgumentNullException(nameof(refreshShares));
        this.requestNotifications = requestNotifications ?? throw new ArgumentNullException(nameof(requestNotifications));
        this.getNotificationAuthorization = getNotificationAuthorization ??
            throw new ArgumentNullException(nameof(getNotificationAuthorization));
        this.setKeepAwake = setKeepAwake ?? throw new ArgumentNullException(nameof(setKeepAwake));
        this.reconfigureListener = reconfigureListener ?? throw new ArgumentNullException(nameof(reconfigureListener));
    }

    /// <summary>Raised after a setting, progress state, or side-effect status changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Creates the production settings store from the initialized process composition.</summary>
    /// <returns>A settings store whose controllers never receive raw preferences.</returns>
    public static SettingsPresentationStore CreateDefault() => new(
        AppCompositionRoot.KeyValueStore,
        AppCompositionRoot.Social,
        AppCompositionRoot.ShareIndex,
        AppCompositionRoot.PortMapping,
        AppCompositionRoot.SetSharingEnabledAsync,
        AppCompositionRoot.RefreshSharesAsync,
        AppCompositionRoot.RequestNotificationAuthorizationAsync,
        AppCompositionRoot.Notifications.GetAuthorizationStateAsync,
        AppCompositionRoot.SetKeepScreenAwakeWhileTransferring,
        AppCompositionRoot.Session.ReconfigureListenerAsync);

    /// <summary>Gets every applicable iOS setting, optionally filtered by localized row content.</summary>
    /// <param name="query">The optional search query.</param>
    /// <returns>Rows grouped by their localized section in catalog order.</returns>
    public IReadOnlyList<SettingsRow> GetRows(string? query = null)
    {
        IReadOnlyList<SettingsRow> rows = BuildRows();
        if (string.IsNullOrWhiteSpace(query))
        {
            return rows;
        }

        string value = query.Trim();
        return rows.Where(row =>
                row.Title.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                row.Detail.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                row.Section.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                (row.Value?.Contains(value, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToArray();
    }

    /// <summary>Applies a toggle transactionally and restores its previous value if a live side effect fails.</summary>
    /// <param name="id">The stable setting identifier.</param>
    /// <param name="enabled">The requested switch value.</param>
    /// <param name="cancellationToken">Cancels network-affecting changes.</param>
    public async Task SetToggleAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        switch (id)
        {
            case "downloads.user-folders":
                await PersistAsync(
                    () => PreferencesState.CreateUsernameSubfolders,
                    value => PreferencesState.CreateUsernameSubfolders = value,
                    enabled);
                break;
            case "downloads.skip-single-folder":
                await PersistAsync(
                    () => PreferencesState.NoSubfolderForSingle,
                    value => PreferencesState.NoSubfolderForSingle = value,
                    enabled);
                break;
            case "downloads.clear-complete":
                await PersistAsync(
                    () => PreferencesState.AutoClearCompleteDownloads,
                    value => PreferencesState.AutoClearCompleteDownloads = value,
                    enabled);
                break;
            case "uploads.clear-complete":
                await PersistAsync(
                    () => PreferencesState.AutoClearCompleteUploads,
                    value => PreferencesState.AutoClearCompleteUploads = value,
                    enabled);
                break;
            case "downloads.retry-online":
                await PersistAsync(
                    () => PreferencesState.AutoRetryBackOnline,
                    value => PreferencesState.AutoRetryBackOnline = value,
                    enabled);
                break;
            case "downloads.keep-awake":
                await PersistAsync(
                    () => PreferencesState.KeepScreenAwakeWhileTransferring,
                    value => PreferencesState.KeepScreenAwakeWhileTransferring = value,
                    enabled,
                    () =>
                    {
                        setKeepAwake(enabled);
                        return Task.CompletedTask;
                    });
                break;
            case "downloads.folder-notifications":
                await PersistAsync(
                    () => PreferencesState.NotifyOnFolderCompleted,
                    value => PreferencesState.NotifyOnFolderCompleted = value,
                    enabled);
                break;
            case "search.remember":
                await PersistAsync(
                    () => PreferencesState.RememberSearchHistory,
                    value => PreferencesState.RememberSearchHistory = value,
                    enabled);
                break;
            case "search.recent-users":
                await PersistAsync(
                    () => PreferencesState.ShowRecentUsers,
                    value => PreferencesState.ShowRecentUsers = value,
                    enabled);
                break;
            case "search.free-slots":
                await PersistAsync(
                    () => PreferencesState.FreeUploadSlotsOnly,
                    value => PreferencesState.FreeUploadSlotsOnly = value,
                    enabled);
                break;
            case "search.hide-locked":
                await PersistAsync(
                    () => PreferencesState.HideLockedResultsInSearch,
                    value => PreferencesState.HideLockedResultsInSearch = value,
                    enabled);
                break;
            case "browse.hide-locked":
                await PersistAsync(
                    () => PreferencesState.HideLockedResultsInBrowse,
                    value => PreferencesState.HideLockedResultsInBrowse = value,
                    enabled);
                break;
            case "search.expand":
                await PersistAsync(
                    () => PreferencesState.ExpandAllResults,
                    value => PreferencesState.ExpandAllResults = value,
                    enabled);
                break;
            case "social.private-invitations":
                await PersistAsync(
                    () => PreferencesState.AllowPrivateRoomInvitations,
                    value => PreferencesState.AllowPrivateRoomInvitations = value,
                    enabled,
                    () => social.SetPrivateRoomInvitationsAsync(enabled, cancellationToken));
                break;
            case "rooms.show-status":
                await PersistAsync(
                    () => PreferencesState.ShowStatusesView,
                    value => PreferencesState.ShowStatusesView = value,
                    enabled);
                break;
            case "rooms.show-tickers":
                await PersistAsync(
                    () => PreferencesState.ShowTickerView,
                    value => PreferencesState.ShowTickerView = value,
                    enabled);
                break;
            case "sharing.enabled":
                await PersistAsync(
                    () => PreferencesState.SharingOn,
                    value => PreferencesState.SharingOn = value,
                    enabled,
                    () => setSharing(enabled, cancellationToken),
                    persistBeforeEffect: false);
                break;
            case "sharing.metered":
                await PersistAsync(
                    () => PreferencesState.AllowUploadsOnMetered,
                    value => PreferencesState.AllowUploadsOnMetered = value,
                    enabled);
                break;
            case "sharing.vpn":
                await PersistAsync(
                    () => PreferencesState.RequireVpnForSharing,
                    value => PreferencesState.RequireVpnForSharing = value,
                    enabled);
                break;
            case "listener.enabled":
                await PersistAsync(
                    () => PreferencesState.ListenerEnabled,
                    value => PreferencesState.ListenerEnabled = value,
                    enabled,
                    async () =>
                    {
                        await reconfigureListener(
                            enabled,
                            PreferencesState.ListenerPort,
                            cancellationToken);
                        portMapping.RefreshIfNeeded();
                    });
                break;
            case "listener.nat-pmp":
                await PersistAsync(
                    () => PreferencesState.ListenerUPnpEnabled,
                    value => PreferencesState.ListenerUPnpEnabled = value,
                    enabled,
                    () =>
                    {
                        portMapping.RefreshIfNeeded();
                        return Task.CompletedTask;
                    });
                break;
            case "downloads.limit-concurrent":
                await PersistAsync(
                    () => PreferencesState.LimitSimultaneousDownloads,
                    value => PreferencesState.LimitSimultaneousDownloads = value,
                    enabled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown toggle setting.");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies an integer preference after validation.</summary>
    /// <param name="id">The stable setting identifier.</param>
    /// <param name="value">The requested integer value.</param>
    public async Task SetIntegerAsync(string id, int value)
    {
        switch (id)
        {
            case "search.result-limit" when value is >= 50 and <= 10_000:
                await PersistAsync(
                    () => PreferencesState.NumberSearchResults,
                    current => PreferencesState.NumberSearchResults = current,
                    value);
                break;
            case "listener.port" when value is >= 1_024 and <= 65_535:
                await PersistAsync(
                    () => PreferencesState.ListenerPort,
                    current => PreferencesState.ListenerPort = current,
                    value,
                    async () =>
                    {
                        await reconfigureListener(
                            PreferencesState.ListenerEnabled,
                            value,
                            CancellationToken.None);
                        portMapping.RefreshIfNeeded();
                    });
                break;
            case "downloads.concurrent-count" when value is >= 1 and <= 20:
                await PersistAsync(
                    () => PreferencesState.MaxSimultaneousLimit,
                    current => PreferencesState.MaxSimultaneousLimit = current,
                    value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value is outside the supported range.");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the current download or upload speed governor.</summary>
    /// <param name="id">The stable speed-limit setting identifier.</param>
    /// <returns>The persisted governor expressed in KB/s for display and editing.</returns>
    public SettingsSpeedLimit GetSpeedLimit(string id) => id switch
    {
        "downloads.speed-limit" => new SettingsSpeedLimit(
            PreferencesState.SpeedLimitDownloadOn,
            Math.Max(MinimumSpeedLimitKilobytesPerSecond, PreferencesState.SpeedLimitDownloadBytesSec / 1_024),
            PreferencesState.SpeedLimitDownloadIsPerTransfer),
        "uploads.speed-limit" => new SettingsSpeedLimit(
            PreferencesState.SpeedLimitUploadOn,
            Math.Max(MinimumSpeedLimitKilobytesPerSecond, PreferencesState.SpeedLimitUploadBytesSec / 1_024),
            PreferencesState.SpeedLimitUploadIsPerTransfer),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown speed-limit setting."),
    };

    /// <summary>Persists a complete speed governor whose values are consumed live by transfer workers.</summary>
    /// <param name="id">The stable download or upload speed-limit identifier.</param>
    /// <param name="enabled">Whether the governor should apply.</param>
    /// <param name="kilobytesPerSecond">A positive rate that fits the portable bytes-per-second field.</param>
    /// <param name="perTransfer">Whether the rate applies per transfer rather than across all transfers.</param>
    /// <returns>A completed task after the durable preference snapshot is flushed.</returns>
    public Task SetSpeedLimitAsync(
        string id,
        bool enabled,
        int kilobytesPerSecond,
        bool perTransfer)
    {
        _ = GetSpeedLimit(id);
        if (kilobytesPerSecond is < MinimumSpeedLimitKilobytesPerSecond or > MaximumSpeedLimitKilobytesPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kilobytesPerSecond),
                kilobytesPerSecond,
                "The speed limit is outside the supported range.");
        }

        int bytesPerSecond = checked(kilobytesPerSecond * 1_024);
        bool previousEnabled;
        int previousBytesPerSecond;
        bool previousPerTransfer;
        switch (id)
        {
            case "downloads.speed-limit":
                previousEnabled = PreferencesState.SpeedLimitDownloadOn;
                previousBytesPerSecond = PreferencesState.SpeedLimitDownloadBytesSec;
                previousPerTransfer = PreferencesState.SpeedLimitDownloadIsPerTransfer;
                break;
            case "uploads.speed-limit":
                previousEnabled = PreferencesState.SpeedLimitUploadOn;
                previousBytesPerSecond = PreferencesState.SpeedLimitUploadBytesSec;
                previousPerTransfer = PreferencesState.SpeedLimitUploadIsPerTransfer;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown speed-limit setting.");
        }

        try
        {
            switch (id)
            {
                case "downloads.speed-limit":
                    PreferencesState.SpeedLimitDownloadOn = enabled;
                    PreferencesState.SpeedLimitDownloadBytesSec = bytesPerSecond;
                    PreferencesState.SpeedLimitDownloadIsPerTransfer = perTransfer;
                    break;
                case "uploads.speed-limit":
                    PreferencesState.SpeedLimitUploadOn = enabled;
                    PreferencesState.SpeedLimitUploadBytesSec = bytesPerSecond;
                    PreferencesState.SpeedLimitUploadIsPerTransfer = perTransfer;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown speed-limit setting.");
            }

            writer.SaveAll();
        }
        catch
        {
            RestoreSpeedLimit(id, previousEnabled, previousBytesPerSecond, previousPerTransfer);
            writer.SaveAll();
            throw;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Runs a bounded settings action and publishes its completion.</summary>
    /// <param name="id">The stable action identifier.</param>
    /// <param name="cancellationToken">Cancels bounded asynchronous work.</param>
    /// <returns>An optional result string key for localized feedback.</returns>
    public async Task<string?> RunActionAsync(string id, CancellationToken cancellationToken = default)
    {
        if (id == "notifications.request")
        {
            return await RequestNotificationAuthorizationAsync();
        }

        string? result = id switch
        {
            "sharing.rescan" => await RunAndReturnAsync(refreshShares, cancellationToken, "IosUiSaved"),
            "settings.restore" => await RestoreDefaultsAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown settings action."),
        };
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Refreshes the current notification authorization without presenting a system prompt.</summary>
    /// <returns>The latest UIKit-independent system authorization state.</returns>
    public async Task<NotificationAuthorizationState> RefreshNotificationAuthorizationAsync()
    {
        notificationAuthorizationState = await getNotificationAuthorization();
        Changed?.Invoke(this, EventArgs.Empty);
        return notificationAuthorizationState;
    }

    /// <summary>Requests authorization explicitly, then replaces any assumed result with the current system state.</summary>
    /// <returns>A localized feedback key describing the state iOS reports after the request.</returns>
    private async Task<string> RequestNotificationAuthorizationAsync()
    {
        try
        {
            _ = await requestNotifications();
        }
        catch
        {
            await RefreshNotificationAuthorizationAsync();
            throw;
        }

        NotificationAuthorizationState state = await RefreshNotificationAuthorizationAsync();
        return state switch
        {
            NotificationAuthorizationState.Authorized => "IosUiNotificationsAuthorizedFeedback",
            NotificationAuthorizationState.Provisional => "IosUiNotificationsProvisionalFeedback",
            NotificationAuthorizationState.Ephemeral => "IosUiNotificationsEphemeralFeedback",
            NotificationAuthorizationState.Denied => "IosUiPermissionDenied",
            NotificationAuthorizationState.NotDetermined => "IosUiNotificationsNotDeterminedFeedback",
            _ => "IosUiNotificationsUnknownFeedback",
        };
    }

    /// <summary>Runs an asynchronous action and returns a catalog key after success.</summary>
    /// <param name="action">The cancellable action.</param>
    /// <param name="cancellationToken">Cancels the action.</param>
    /// <param name="result">The result resource key.</param>
    /// <returns>The supplied result key.</returns>
    private static async Task<string> RunAndReturnAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        string result)
    {
        await action(cancellationToken);
        return result;
    }

    /// <summary>Restores only iOS-applicable values while preserving credentials and local content.</summary>
    /// <returns>The localized success resource key.</returns>
    private async Task<string> RestoreDefaultsAsync(CancellationToken cancellationToken)
    {
        bool previousPrivateInvitations = PreferencesState.AllowPrivateRoomInvitations;
        bool previousSharing = PreferencesState.SharingOn;
        bool previousListenerEnabled = PreferencesState.ListenerEnabled;
        int previousListenerPort = PreferencesState.ListenerPort;
        bool privateInvitationsChanged = false;
        bool sharingChanged = false;
        bool listenerChanged = false;
        try
        {
            if (previousPrivateInvitations)
            {
                await social.SetPrivateRoomInvitationsAsync(false, cancellationToken);
                privateInvitationsChanged = true;
            }

            if (previousSharing)
            {
                await setSharing(false, cancellationToken);
                sharingChanged = true;
            }

            if (!previousListenerEnabled || previousListenerPort != 33_939)
            {
                await reconfigureListener(true, 33_939, cancellationToken);
                listenerChanged = true;
            }
        }
        catch
        {
            if (listenerChanged)
            {
                await TryRestoreSideEffectAsync(() =>
                    reconfigureListener(previousListenerEnabled, previousListenerPort, CancellationToken.None));
            }

            if (sharingChanged)
            {
                await TryRestoreSideEffectAsync(() => setSharing(previousSharing, CancellationToken.None));
            }

            if (privateInvitationsChanged)
            {
                await TryRestoreSideEffectAsync(() =>
                    social.SetPrivateRoomInvitationsAsync(previousPrivateInvitations, CancellationToken.None));
            }

            throw;
        }

        PreferencesState.CreateUsernameSubfolders = false;
        PreferencesState.NoSubfolderForSingle = false;
        PreferencesState.AutoClearCompleteDownloads = false;
        PreferencesState.AutoClearCompleteUploads = false;
        PreferencesState.AutoRetryBackOnline = true;
        PreferencesState.KeepScreenAwakeWhileTransferring = false;
        PreferencesState.NotifyOnFolderCompleted = true;
        PreferencesState.NumberSearchResults = 250;
        PreferencesState.RememberSearchHistory = true;
        PreferencesState.ShowRecentUsers = true;
        PreferencesState.FreeUploadSlotsOnly = true;
        PreferencesState.HideLockedResultsInSearch = true;
        PreferencesState.HideLockedResultsInBrowse = true;
        PreferencesState.ExpandAllResults = false;
        PreferencesState.ShowStatusesView = true;
        PreferencesState.ShowTickerView = false;
        PreferencesState.AllowPrivateRoomInvitations = false;
        PreferencesState.SharingOn = false;
        PreferencesState.AllowUploadsOnMetered = true;
        PreferencesState.RequireVpnForSharing = false;
        PreferencesState.ListenerEnabled = true;
        PreferencesState.ListenerPort = 33_939;
        PreferencesState.ListenerUPnpEnabled = true;
        PreferencesState.LimitSimultaneousDownloads = false;
        PreferencesState.MaxSimultaneousLimit = 1;
        PreferencesState.SpeedLimitDownloadOn = false;
        PreferencesState.SpeedLimitDownloadBytesSec = 4 * 1_024 * 1_024;
        PreferencesState.SpeedLimitDownloadIsPerTransfer = true;
        PreferencesState.SpeedLimitUploadOn = false;
        PreferencesState.SpeedLimitUploadBytesSec = 4 * 1_024 * 1_024;
        PreferencesState.SpeedLimitUploadIsPerTransfer = true;
        writer.SaveAll();
        setKeepAwake(false);
        portMapping.RefreshIfNeeded();
        return "IosUiSaved";
    }

    /// <summary>Best-effort rollback used only after a later defaults side effect failed.</summary>
    /// <param name="rollback">The live side effect to restore.</param>
    private static async Task TryRestoreSideEffectAsync(Func<Task> rollback)
    {
        try
        {
            await rollback();
        }
        catch
        {
            // Preserve the original reset failure; the saved values remain readable and can be retried explicitly.
        }
    }

    /// <summary>Restores one in-memory speed governor after persistence fails.</summary>
    /// <param name="id">The stable download or upload identifier.</param>
    /// <param name="enabled">The previous enabled value.</param>
    /// <param name="bytesPerSecond">The exact previous portable rate.</param>
    /// <param name="perTransfer">The previous scope value.</param>
    private static void RestoreSpeedLimit(string id, bool enabled, int bytesPerSecond, bool perTransfer)
    {
        switch (id)
        {
            case "downloads.speed-limit":
                PreferencesState.SpeedLimitDownloadOn = enabled;
                PreferencesState.SpeedLimitDownloadBytesSec = bytesPerSecond;
                PreferencesState.SpeedLimitDownloadIsPerTransfer = perTransfer;
                break;
            case "uploads.speed-limit":
                PreferencesState.SpeedLimitUploadOn = enabled;
                PreferencesState.SpeedLimitUploadBytesSec = bytesPerSecond;
                PreferencesState.SpeedLimitUploadIsPerTransfer = perTransfer;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown speed-limit setting.");
        }
    }

    /// <summary>Persists a scalar and rolls back both memory and storage after side-effect failure.</summary>
    /// <typeparam name="T">The scalar setting type.</typeparam>
    /// <param name="read">Reads the previous value.</param>
    /// <param name="write">Updates the in-memory portable value.</param>
    /// <param name="value">The requested value.</param>
    /// <param name="effect">The optional live side effect.</param>
    /// <param name="persistBeforeEffect">Whether the generic writer owns persistence before the side effect.</param>
    private async Task PersistAsync<T>(
        Func<T> read,
        Action<T> write,
        T value,
        Func<Task>? effect = null,
        bool persistBeforeEffect = true)
    {
        T previous = read();
        try
        {
            write(value);
            if (persistBeforeEffect)
            {
                writer.SaveAll();
            }

            if (effect is not null)
            {
                await effect();
            }
        }
        catch
        {
            write(previous);
            writer.SaveAll();
            throw;
        }
    }

    /// <summary>Builds the localized settings catalog from current portable and iOS service state.</summary>
    /// <returns>Rows in stable section and item order.</returns>
    private IReadOnlyList<SettingsRow> BuildRows()
    {
        static string L(string key) => StringResources.Get(key);
        string downloads = L("IosUiDownloadsSection");
        string transferSpeeds = L("IosUiTransferSpeedsSection");
        string search = L("IosUiSearchSection");
        string socialSection = L("IosUiSocialSection");
        string sharing = L("IosUiSharingSection");
        string connection = L("IosUiConnectionSection");
        string account = L("account_tab");
        string support = L("IosUiSupportSection");
        SharedFileCatalog catalog = shareIndex.Catalog;
        return
        [
            Navigation("account.manage", account, "account_tab", "IosUiAccountDetail"),
            Toggle("downloads.user-folders", downloads, "IosUiCreateUserFolders", "IosUiCreateUserFoldersDetail", PreferencesState.CreateUsernameSubfolders),
            Toggle("downloads.skip-single-folder", downloads, "IosUiSkipSingleFolder", "IosUiSkipSingleFolderDetail", PreferencesState.NoSubfolderForSingle),
            Toggle("downloads.clear-complete", downloads, "IosUiAutoClearDownloads", "IosUiAutoClearDownloadsDetail", PreferencesState.AutoClearCompleteDownloads),
            Toggle("uploads.clear-complete", downloads, "IosUiAutoClearUploads", "IosUiAutoClearUploadsDetail", PreferencesState.AutoClearCompleteUploads),
            Toggle("downloads.retry-online", downloads, "IosUiRetryOnline", "IosUiRetryOnlineDetail", PreferencesState.AutoRetryBackOnline),
            Toggle("downloads.keep-awake", downloads, "IosUiKeepAwake", "IosUiKeepAwakeDetail", PreferencesState.KeepScreenAwakeWhileTransferring),
            Toggle("downloads.folder-notifications", downloads, "IosUiFolderNotifications", "IosUiFolderNotificationsDetail", PreferencesState.NotifyOnFolderCompleted),
            new SettingsRow("downloads.background", downloads, L("IosUiBackgroundTransfers"), L("IosUiBackgroundTransfersDetail"), SettingsControlKind.Information, Value: L("IosUiBackgroundTransfersValue")),
            Value("downloads.speed-limit", transferSpeeds, "IosUiDownloadSpeedLimit", "IosUiDownloadSpeedLimitDetail", FormatSpeedLimit(PreferencesState.SpeedLimitDownloadOn, PreferencesState.SpeedLimitDownloadBytesSec, PreferencesState.SpeedLimitDownloadIsPerTransfer)),
            Value("uploads.speed-limit", transferSpeeds, "IosUiUploadSpeedLimit", "IosUiUploadSpeedLimitDetail", FormatSpeedLimit(PreferencesState.SpeedLimitUploadOn, PreferencesState.SpeedLimitUploadBytesSec, PreferencesState.SpeedLimitUploadIsPerTransfer)),
            Value("search.result-limit", search, "IosUiResultLimit", "IosUiResultLimit", PreferencesState.NumberSearchResults.ToString()),
            Toggle("search.remember", search, "IosUiRememberSearches", "IosUiRememberSearchesDetail", PreferencesState.RememberSearchHistory),
            Toggle("search.recent-users", search, "IosUiRememberUsers", "IosUiRememberUsersDetail", PreferencesState.ShowRecentUsers),
            Toggle("search.free-slots", search, "IosUiFreeSlotsOnly", "IosUiFreeSlotsOnlyDetail", PreferencesState.FreeUploadSlotsOnly),
            Toggle("search.hide-locked", search, "IosUiHideLockedSearch", "IosUiHideLockedSearch", PreferencesState.HideLockedResultsInSearch),
            Toggle("browse.hide-locked", search, "IosUiHideLockedBrowse", "IosUiHideLockedBrowse", PreferencesState.HideLockedResultsInBrowse),
            Toggle("search.expand", search, "IosUiExpandResults", "IosUiExpandResultsDetail", PreferencesState.ExpandAllResults),
            Toggle("social.private-invitations", socialSection, "IosUiPrivateInvitations", "IosUiPrivateInvitationsDetail", PreferencesState.AllowPrivateRoomInvitations),
            new SettingsRow(
                "notifications.request",
                socialSection,
                L("IosUiNotifications"),
                NotificationAuthorizationDetail(notificationAuthorizationState),
                notificationAuthorizationState == NotificationAuthorizationState.NotDetermined
                    ? SettingsControlKind.Action
                    : SettingsControlKind.Information,
                Value: NotificationAuthorizationValue(notificationAuthorizationState)),
            Toggle("rooms.show-status", socialSection, "IosUiShowRoomStatus", "IosUiShowRoomStatusDetail", PreferencesState.ShowStatusesView),
            Toggle("rooms.show-tickers", socialSection, "IosUiShowTickers", "IosUiShowTickersDetail", PreferencesState.ShowTickerView),
            Toggle("sharing.enabled", sharing, "IosUiSharingEnabled", "IosUiSharingEnabledDetail", PreferencesState.SharingOn),
            new SettingsRow("sharing.location", sharing, L("IosUiDocumentsLocation"), L("IosUiDocumentsLocationDetail"), SettingsControlKind.Information, Value: L("IosUiDocumentsLocationValue")),
            Toggle("sharing.metered", sharing, "IosUiAllowMetered", "IosUiAllowMetered", PreferencesState.AllowUploadsOnMetered),
            Toggle("sharing.vpn", sharing, "IosUiRequireVpn", "IosUiRequireVpn", PreferencesState.RequireVpnForSharing),
            new SettingsRow("sharing.rescan", sharing, L("IosUiRescanShares"), L("IosUiRescanSharesDetail"), SettingsControlKind.Action, Value: StringResources.Format("IosUiShareCounts", catalog.FileCount, catalog.DirectoryCount)),
            new SettingsRow("sharing.browse-self", sharing, L("IosUiBrowseYourShares"), L("IosUiSharingEnabledDetail"), SettingsControlKind.Navigation),
            Toggle("listener.enabled", connection, "IosUiListener", "IosUiListenerDetail", PreferencesState.ListenerEnabled),
            Value("listener.port", connection, "IosUiListenerPort", "IosUiListenerPortDetail", PreferencesState.ListenerPort.ToString()),
            Toggle("listener.nat-pmp", connection, "IosUiNatPmp", "IosUiNatPmpDetail", PreferencesState.ListenerUPnpEnabled),
            ToggleRequiresReconnect("downloads.limit-concurrent", connection, "IosUiConcurrentDownloads", "IosUiConcurrentDownloadsDetail", PreferencesState.LimitSimultaneousDownloads),
            ValueRequiresReconnect("downloads.concurrent-count", connection, "IosUiConcurrentCount", "IosUiConcurrentDownloadsDetail", PreferencesState.MaxSimultaneousLimit.ToString()),
            Navigation("settings.import", support, "IosUiImportSettings", "IosUiImportSettingsDetail"),
            Navigation("settings.diagnostics", support, "IosUiDiagnostics", "IosUiDiagnosticsDetail"),
            Navigation("settings.about", support, "IosUiAbout", "IosUiAboutDetail"),
            Navigation("settings.legal", support, "IosUiLegal", "IosUiLegalDetail"),
            new SettingsRow("settings.restore", support, L("IosUiRestoreDefaults"), L("IosUiRestoreDefaultsDetail"), SettingsControlKind.Destructive),
        ];
    }

    /// <summary>Creates a localized toggle row.</summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="section">Localized section.</param>
    /// <param name="title">Title resource key.</param>
    /// <param name="detail">Detail resource key.</param>
    /// <param name="isOn">Current switch state.</param>
    /// <returns>A settings toggle row.</returns>
    private static SettingsRow Toggle(string id, string section, string title, string detail, bool isOn) =>
        new(id, section, StringResources.Get(title), StringResources.Get(detail), SettingsControlKind.Toggle, isOn);

    /// <summary>Creates a toggle row whose localized detail explicitly discloses reconnect timing.</summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="section">Localized section.</param>
    /// <param name="title">Title resource key.</param>
    /// <param name="detail">Detail resource key.</param>
    /// <param name="isOn">Current switch state.</param>
    /// <returns>A reconnect-disclosing settings toggle row.</returns>
    private static SettingsRow ToggleRequiresReconnect(
        string id,
        string section,
        string title,
        string detail,
        bool isOn) =>
        new(
            id,
            section,
            StringResources.Get(title),
            ReconnectDetail(detail),
            SettingsControlKind.Toggle,
            isOn);

    /// <summary>Creates a localized value row.</summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="section">Localized section.</param>
    /// <param name="title">Title resource key.</param>
    /// <param name="detail">Detail resource key.</param>
    /// <param name="value">Current formatted value.</param>
    /// <returns>A settings value row.</returns>
    private static SettingsRow Value(string id, string section, string title, string detail, string value) =>
        new(id, section, StringResources.Get(title), StringResources.Get(detail), SettingsControlKind.Value, Value: value);

    /// <summary>Formats a speed governor using the same explicit total/per-transfer language as the editor.</summary>
    /// <param name="enabled">Whether the governor currently applies.</param>
    /// <param name="bytesPerSecond">The portable configured rate.</param>
    /// <param name="perTransfer">Whether the rate applies separately to each transfer.</param>
    /// <returns>A localized, compact settings-row summary.</returns>
    private static string FormatSpeedLimit(bool enabled, int bytesPerSecond, bool perTransfer)
    {
        if (!enabled)
        {
            return StringResources.Get("speed_limit_off");
        }

        int kilobytesPerSecond = Math.Max(MinimumSpeedLimitKilobytesPerSecond, bytesPerSecond / 1_024);
        return StringResources.Format(
            perTransfer ? "speed_limit_kbs_per_transfer" : "speed_limit_kbs_total",
            kilobytesPerSecond);
    }

    /// <summary>Formats the current notification permission as concise, non-color-dependent status text.</summary>
    /// <param name="state">The UIKit-independent authorization state.</param>
    /// <returns>A localized status value.</returns>
    private static string NotificationAuthorizationValue(NotificationAuthorizationState state) =>
        StringResources.Get(state switch
        {
            NotificationAuthorizationState.NotDetermined => "IosUiNotificationsNotDetermined",
            NotificationAuthorizationState.Denied => "IosUiNotificationsDenied",
            NotificationAuthorizationState.Authorized => "IosUiNotificationsAuthorized",
            NotificationAuthorizationState.Provisional => "IosUiNotificationsProvisional",
            NotificationAuthorizationState.Ephemeral => "IosUiNotificationsEphemeral",
            NotificationAuthorizationState.Unknown => "IosUiNotificationsUnavailable",
            _ => "IosUiNotificationsChecking",
        });

    /// <summary>Explains the available next step for every native notification permission state.</summary>
    /// <param name="state">The UIKit-independent authorization state.</param>
    /// <returns>A localized, wrapping row description.</returns>
    private static string NotificationAuthorizationDetail(NotificationAuthorizationState state) =>
        StringResources.Get(state switch
        {
            NotificationAuthorizationState.NotDetermined => "IosUiNotificationsNotDeterminedDetail",
            NotificationAuthorizationState.Denied => "IosUiNotificationsDeniedDetail",
            NotificationAuthorizationState.Authorized => "IosUiNotificationsAuthorizedDetail",
            NotificationAuthorizationState.Provisional => "IosUiNotificationsProvisionalDetail",
            NotificationAuthorizationState.Ephemeral => "IosUiNotificationsEphemeralDetail",
            NotificationAuthorizationState.Unknown => "IosUiNotificationsUnavailableDetail",
            _ => "IosUiNotificationsCheckingDetail",
        });

    /// <summary>Creates a value row whose localized detail explicitly discloses reconnect timing.</summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="section">Localized section.</param>
    /// <param name="title">Title resource key.</param>
    /// <param name="detail">Detail resource key.</param>
    /// <param name="value">Current formatted value.</param>
    /// <returns>A reconnect-disclosing settings value row.</returns>
    private static SettingsRow ValueRequiresReconnect(
        string id,
        string section,
        string title,
        string detail,
        string value) =>
        new(
            id,
            section,
            StringResources.Get(title),
            ReconnectDetail(detail),
            SettingsControlKind.Value,
            Value: value);

    /// <summary>Combines localized setting guidance with the localized reconnect requirement.</summary>
    /// <param name="detail">The setting detail resource key.</param>
    /// <returns>A localized, VoiceOver-readable explanation.</returns>
    private static string ReconnectDetail(string detail) =>
        $"{StringResources.Get(detail)} {StringResources.Get("IosUiReconnectRequired")}.";

    /// <summary>Creates a localized action row.</summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="section">Localized section.</param>
    /// <param name="title">Title resource key.</param>
    /// <param name="detail">Detail resource key.</param>
    /// <returns>A settings action row.</returns>
    private static SettingsRow Action(string id, string section, string title, string detail) =>
        new(id, section, StringResources.Get(title), StringResources.Get(detail), SettingsControlKind.Action);

    /// <summary>Creates a localized navigation row.</summary>
    /// <param name="id">Stable row identifier.</param>
    /// <param name="section">Localized section.</param>
    /// <param name="title">Title resource key.</param>
    /// <param name="detail">Detail resource key.</param>
    /// <returns>A settings navigation row.</returns>
    private static SettingsRow Navigation(string id, string section, string title, string detail) =>
        new(id, section, StringResources.Get(title), StringResources.Get(detail), SettingsControlKind.Navigation);
}

/// <summary>Describes one private-message conversation in overview order.</summary>
/// <param name="Username">The remote user name.</param>
/// <param name="Preview">The latest message preview.</param>
/// <param name="Timestamp">The latest localized message time.</param>
/// <param name="TimestampUtc">The latest message time used for culture-independent ordering.</param>
/// <param name="UnreadCount">The unread message count.</param>
internal sealed record ConversationSummary(
    string Username,
    string Preview,
    string Timestamp,
    DateTime TimestampUtc,
    int UnreadCount,
    UserPresence? Presence);

/// <summary>Identifies the complete presentation phase for the conversation overview.</summary>
internal enum MessagesOverviewPhase
{
    Loading,
    Content,
    Empty,
    Offline,
    RecoverableError,
}

/// <summary>Describes an immutable message-overview snapshot, retaining useful rows through offline and error states.</summary>
/// <param name="Phase">The current loading, content, empty, offline, or recoverable-error phase.</param>
/// <param name="Conversations">Detached conversations safe to diff on the UIKit thread.</param>
internal sealed record MessagesOverviewPresentation(
    MessagesOverviewPhase Phase,
    IReadOnlyList<ConversationSummary> Conversations);

/// <summary>Describes one private-message row, including local delivery state.</summary>
/// <param name="StableId">A stable presentation identifier.</param>
/// <param name="Text">The message body.</param>
/// <param name="Timestamp">The localized message time.</param>
/// <param name="IsOutgoing">Whether the current user sent it.</param>
/// <param name="DeliveryState">A localized delivery-state label, when needed.</param>
internal sealed record PrivateMessageRow(
    string StableId,
    string Text,
    string Timestamp,
    bool IsOutgoing,
    string? DeliveryState);

/// <summary>Owns immutable private-message presentation snapshots and typed read/delete/send commands.</summary>
internal sealed class MessagesPresentationStore : IDisposable
{
    private readonly AppSession session;
    private readonly Func<IReadOnlyList<IosUserListEntrySnapshot>> userSnapshots;
    private readonly object overviewSync = new();
    private readonly Dictionary<string, PrivateMessageRow> transientMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RemovedPrivateMessageConversation> lastRemoved = [];
    private IReadOnlyList<ConversationSummary> retainedOverview = [];
    private bool disposed;

    /// <summary>Creates a message presentation store and subscribes to durable message changes.</summary>
    /// <param name="session">The application session boundary.</param>
    public MessagesPresentationStore(
        AppSession session,
        Func<IReadOnlyList<IosUserListEntrySnapshot>>? userSnapshots = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.userSnapshots = userSnapshots ?? (() => []);
        session.MessagesChanged += OnMessagesChanged;
    }

    /// <summary>Raised on the UIKit thread after a message snapshot changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Creates the production store over the initialized session.</summary>
    /// <returns>A message presentation store.</returns>
    public static MessagesPresentationStore CreateDefault() =>
        new(AppCompositionRoot.Session, AppCompositionRoot.UserLists.GetSnapshot);

    /// <summary>Gets conversation summaries in most-recent-first order.</summary>
    /// <returns>Detached conversation overview rows.</returns>
    public IReadOnlyList<ConversationSummary> GetConversations()
    {
        PrivateMessageSessionState state = session.PrivateMessages;
        string account = state.CurrentAccount;
        if (account.Length == 0 || !state.ExportMessageRoots().TryGetValue(account, out var conversations))
        {
            return [];
        }

        IReadOnlyDictionary<string, UserPresence> presence = userSnapshots()
            .GroupBy(item => item.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Presence, StringComparer.OrdinalIgnoreCase);
        return conversations
            .Select(pair =>
            {
                Message? latest = pair.Value.OrderBy(message => message.UtcDateTime).ThenBy(message => message.Id).LastOrDefault();
                return latest is null
                    ? null
                    : new ConversationSummary(
                        pair.Key,
                        latest.MessageText,
                        latest.LocalDateTime.ToString("g"),
                        latest.UtcDateTime,
                        state.GetUnreadCount(pair.Key),
                        presence.TryGetValue(pair.Key, out UserPresence currentPresence) ? currentPresence : null);
            })
            .Where(summary => summary is not null)
            .OrderByDescending(summary => summary!.TimestampUtc)
            .ThenBy(summary => summary!.Username, StringComparer.CurrentCultureIgnoreCase)
            .Cast<ConversationSummary>()
            .ToArray();
    }

    /// <summary>Gets a typed loading state while preserving the last useful overview snapshot.</summary>
    /// <returns>An immutable loading presentation.</returns>
    public MessagesOverviewPresentation GetLoadingOverview()
    {
        lock (overviewSync)
        {
            return new MessagesOverviewPresentation(MessagesOverviewPhase.Loading, retainedOverview);
        }
    }

    /// <summary>
    /// Maps the thread-safe durable message model away from the UIKit thread and retains the last good rows if
    /// presentation mapping fails.
    /// </summary>
    /// <param name="cancellationToken">Cancels an obsolete refresh.</param>
    /// <returns>A complete immutable loading outcome suitable for one diffable snapshot.</returns>
    public async Task<MessagesOverviewPresentation> RefreshOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<ConversationSummary> conversations = await Task.Run(
                GetConversations,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (overviewSync)
            {
                retainedOverview = conversations;
            }

            MessagesOverviewPhase phase = !session.IsConnected
                ? MessagesOverviewPhase.Offline
                : conversations.Count == 0
                    ? MessagesOverviewPhase.Empty
                    : MessagesOverviewPhase.Content;
            return new MessagesOverviewPresentation(phase, conversations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            lock (overviewSync)
            {
                return new MessagesOverviewPresentation(
                    MessagesOverviewPhase.RecoverableError,
                    retainedOverview);
            }
        }
    }

    /// <summary>Gets one chronological, detached conversation snapshot.</summary>
    /// <param name="username">The remote user name.</param>
    /// <returns>Message rows including transient sending or failure entries.</returns>
    public IReadOnlyList<PrivateMessageRow> GetConversation(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        PrivateMessageRow[] durable = session.PrivateMessages.GetConversationSnapshot(username)
            .Select(message => new PrivateMessageRow(
                $"{message.Id}:{message.UtcDateTime.Ticks}:{message.FromMe}",
                message.MessageText,
                message.LocalDateTime.ToString("g"),
                message.FromMe,
                message.SentMsgStatus switch
                {
                    SentStatus.Pending => StringResources.Get("IosUiMessageSending"),
                    SentStatus.Failed => StringResources.Get("IosUiMessageFailed"),
                    _ => null,
                }))
            .ToArray();
        return transientMessages.TryGetValue(username, out PrivateMessageRow? transient)
            ? [.. durable, transient]
            : durable;
    }

    /// <summary>Gets the connected or most recently authenticated account name, for attributing own messages.</summary>
    public string? CurrentUsername => AppCompositionRoot.Session.Username;

    /// <summary>Marks a conversation read through its latest message.</summary>
    /// <param name="username">The remote user name.</param>
    public void MarkRead(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (session.PrivateMessages.GetUnreadCount(username) > 0)
        {
            session.PrivateMessages.MarkRead(username);
        }
    }

    /// <summary>Marks every active-account conversation read.</summary>
    public void MarkAllRead() => session.PrivateMessages.MarkAllRead();

    /// <summary>Marks a detached set of conversations read as one presentation action.</summary>
    /// <param name="usernames">The selected remote user names.</param>
    public void MarkRead(IEnumerable<string> usernames)
    {
        ArgumentNullException.ThrowIfNull(usernames);
        foreach (string username in usernames
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            MarkRead(username);
        }
    }

    /// <summary>Returns deterministic saved-user suggestions for composing a conversation.</summary>
    /// <returns>Friend and ignored-user names followed by existing conversation peers, without duplicates.</returns>
    public IReadOnlyList<string> GetComposeSuggestions() => userSnapshots()
        .Select(static entry => entry.Username)
        .Concat(GetConversations().Select(static conversation => conversation.Username))
        .Where(static username => !string.IsNullOrWhiteSpace(username))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static username => username, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    /// <summary>Gets whether the message overview currently represents a connected or retained offline account.</summary>
    public bool IsConnected => session.IsConnected;

    /// <summary>Deletes a conversation while retaining one actionable undo payload.</summary>
    /// <param name="username">The remote user name.</param>
    /// <returns><see langword="true"/> when a conversation was removed.</returns>
    public bool DeleteConversation(string username)
    {
        RemovedPrivateMessageConversation? removed = session.PrivateMessages.RemoveConversation(username);
        if (removed is null)
        {
            return false;
        }

        lastRemoved.Add(removed);
        return true;
    }

    /// <summary>Starts a new deletion transaction and discards any expired undo payload.</summary>
    public void BeginDeletion() => lastRemoved.Clear();

    /// <summary>Restores the most recently deleted conversation.</summary>
    /// <returns><see langword="true"/> when an undo payload existed.</returns>
    public bool UndoDelete()
    {
        if (lastRemoved.Count == 0)
        {
            return false;
        }

        foreach (RemovedPrivateMessageConversation conversation in lastRemoved)
        {
            session.PrivateMessages.RestoreConversation(conversation);
        }

        lastRemoved.Clear();
        return true;
    }

    /// <summary>Sends a private message and keeps a visible transient row through failure.</summary>
    /// <param name="username">The remote user name.</param>
    /// <param name="message">The non-empty message body.</param>
    /// <param name="cancellationToken">Cancels reconnecting or sending.</param>
    public async Task SendAsync(string username, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        transientMessages[username] = new PrivateMessageRow(
            $"transient:{Guid.NewGuid():N}",
            message.Trim(),
            DateTime.Now.ToString("g"),
            true,
            StringResources.Get("IosUiMessageSending"));
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await session.SendPrivateMessageAsync(username, message.Trim(), cancellationToken);
            transientMessages.Remove(username);
        }
        catch
        {
            transientMessages[username] = transientMessages[username] with
            {
                DeliveryState = StringResources.Get("IosUiMessageFailed"),
            };
            throw;
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Releases the durable session subscription.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.MessagesChanged -= OnMessagesChanged;
    }

    /// <summary>Forwards durable message changes to presentation subscribers.</summary>
    /// <param name="sender">The session.</param>
    /// <param name="args">Unused event data.</param>
    private void OnMessagesChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

}

/// <summary>Identifies a room-list grouping.</summary>
internal enum ChatroomGroup
{
    Joined,
    Private,
    Public,
}

/// <summary>Describes one room in the searchable overview.</summary>
/// <param name="Name">The room name.</param>
/// <param name="UserCount">The latest server user count.</param>
/// <param name="Group">The room grouping.</param>
/// <param name="ConnectionState">The local connection state.</param>
/// <param name="HasUnread">Whether unread messages exist.</param>
internal sealed record ChatroomSummary(
    string Name,
    int UserCount,
    ChatroomGroup Group,
    RoomConnectionState ConnectionState,
    bool HasUnread);

/// <summary>Describes one detached user row inside a joined chatroom.</summary>
/// <param name="Username">The user name.</param>
/// <param name="Presence">The server presence.</param>
/// <param name="AverageSpeed">The advertised average upload speed.</param>
/// <param name="FileCount">The advertised shared-file count.</param>
/// <param name="DirectoryCount">The advertised shared-directory count.</param>
/// <param name="IsFriend">Whether this user appears in the local Friends list.</param>
/// <param name="Note">The optional private local note.</param>
/// <param name="Role">The localized-neutral room role identifier.</param>
internal sealed record RoomUserRow(
    string Username,
    UserPresence Presence,
    int AverageSpeed,
    int FileCount,
    int DirectoryCount,
    bool IsFriend,
    string? Note,
    RoomUserRole Role);

/// <summary>Identifies a room user's server role without depending on color or UIKit.</summary>
internal enum RoomUserRole
{
    Member,
    Moderator,
    Owner,
}

/// <summary>Represents one chronologically ordered room message or member-status event.</summary>
/// <param name="TimestampUtc">The event time.</param>
/// <param name="Message">The message payload, when this is a message.</param>
/// <param name="Activity">The member activity payload, when this is a status event.</param>
internal sealed record RoomTimelineRow(
    DateTime TimestampUtc,
    RoomMessageEntry? Message = null,
    RoomMemberActivity? Activity = null);

/// <summary>Owns chatroom snapshots and commands without exposing the raw social service to UIKit.</summary>
internal sealed class ChatroomsPresentationStore : IDisposable
{
    private readonly IosSocialSessionService social;
    private readonly IosUserListService userLists;
    private readonly IKeyValueStore keyValueStore;
    private bool disposed;

    /// <summary>Creates a chatroom store over the social service boundary.</summary>
    /// <param name="social">The non-UIKit social coordinator.</param>
    public ChatroomsPresentationStore(
        IosSocialSessionService social,
        IosUserListService? userLists = null,
        IKeyValueStore? keyValueStore = null)
    {
        this.social = social ?? throw new ArgumentNullException(nameof(social));
        this.userLists = userLists ?? AppCompositionRoot.UserLists;
        this.keyValueStore = keyValueStore ?? AppCompositionRoot.KeyValueStore;
        social.RoomListChanged += OnRoomListChanged;
        social.RoomChanged += OnRoomChanged;
    }

    /// <summary>Raised when a room list, room state, message, member, or ticker changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Creates the production chatroom store.</summary>
    /// <returns>A chatroom presentation store.</returns>
    public static ChatroomsPresentationStore CreateDefault() =>
        new(AppCompositionRoot.Social, AppCompositionRoot.UserLists, AppCompositionRoot.KeyValueStore);

    /// <summary>Gets the identity of the most recent server room list, for retiring stale failure messages.</summary>
    public long RoomListVersion => social.RoomListVersion;

    /// <summary>Gets filtered room summaries with joined rooms first and deterministic sorting.</summary>
    /// <param name="query">The optional room-name filter.</param>
    /// <returns>Detached room summaries.</returns>
    public IReadOnlyList<ChatroomSummary> GetRooms(string? query = null)
    {
        var known = social.Rooms.ToDictionary(room => room.RoomName, StringComparer.OrdinalIgnoreCase);
        var serverCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomInfo room in ServerRooms())
        {
            serverCounts[room.Name] = room.UserCount;
        }

        var summaries = new Dictionary<string, ChatroomSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomSessionSnapshot room in social.Rooms)
        {
            // Leaving retains the room locally with no room data, so grouping every retained session as
            // joined kept a left room out of the server's own listing and pinned its count at zero, because
            // the listing pass below skips names that are already present.
            if (room.ConnectionState is not (RoomConnectionState.Joined or RoomConnectionState.Joining
                or RoomConnectionState.Disconnected))
            {
                continue;
            }

            int joinedCount = room.RoomData?.UserCount ?? 0;
            summaries[room.RoomName] = new ChatroomSummary(
                room.RoomName,
                joinedCount > 0 ? joinedCount : serverCounts.GetValueOrDefault(room.RoomName),
                ChatroomGroup.Joined,
                room.ConnectionState,
                room.HasUnreadMessages);
        }

        AddServerRooms(summaries, social.RoomList?.Private, ChatroomGroup.Private, known);
        AddServerRooms(summaries, social.RoomList?.Owned, ChatroomGroup.Private, known);
        AddServerRooms(summaries, social.RoomList?.Public, ChatroomGroup.Public, known);
        AddRetainedRooms(summaries, social.Rooms);
        IEnumerable<ChatroomSummary> result = summaries.Values;
        if (!string.IsNullOrWhiteSpace(query))
        {
            result = result.Where(room => room.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        return result
            .OrderBy(room => room.Group)
            .ThenByDescending(room => room.HasUnread)
            .ThenByDescending(room => room.UserCount)
            .ThenBy(room => room.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Gets the latest detached snapshot of one known room.</summary>
    /// <param name="roomName">The room name.</param>
    /// <returns>The latest room snapshot or <see langword="null"/>.</returns>
    public RoomSessionSnapshot? GetRoom(string roomName) => social.GetRoom(roomName);

    /// <summary>Gets the connected or most recently authenticated account name for role comparisons.</summary>
    public string? CurrentUsername => AppCompositionRoot.Session.Username;

    /// <summary>Gets whether room network commands can currently reach the authenticated Soulseek session.</summary>
    public bool IsConnected => AppCompositionRoot.Session.IsConnected;

    /// <summary>Gets the latest detached private-room membership list.</summary>
    /// <param name="roomName">The private room name.</param>
    /// <returns>A copied user-name list, which may be empty before the server publishes membership.</returns>
    public IReadOnlyList<string> GetPrivateMembers(string roomName) => social.GetPrivateRoomMembers(roomName);

    /// <summary>Gets the room's chronological messages plus optional member-status events.</summary>
    /// <param name="roomName">The joined room.</param>
    /// <returns>A detached oldest-to-newest timeline.</returns>
    public IReadOnlyList<RoomTimelineRow> GetTimeline(string roomName)
    {
        RoomSessionSnapshot? room = GetRoom(roomName);
        if (room is null)
        {
            return [];
        }

        IEnumerable<RoomTimelineRow> messages = room.Messages.Select(message =>
            new RoomTimelineRow(message.TimestampUtc, Message: message));
        IEnumerable<RoomTimelineRow> activities = ShowsStatusEvents
            ? room.MemberActivity.Select(activity => new RoomTimelineRow(activity.TimestampUtc, Activity: activity))
            : [];
        return messages
            .Concat(activities)
            .OrderBy(row => row.TimestampUtc)
            .ThenBy(row => row.Message?.Sequence ?? long.MaxValue)
            .ToArray();
    }

    /// <summary>Gets whether member join/leave/presence activity appears in the room timeline.</summary>
    public bool ShowsStatusEvents => PreferencesState.ShowStatusesView;

    /// <summary>Gets whether ticker messages appear above the room timeline.</summary>
    public bool ShowsTickers => PreferencesState.ShowTickerView;

    /// <summary>Persists whether member-status events should be shown.</summary>
    /// <param name="enabled">The requested visibility.</param>
    public void SetShowsStatusEvents(bool enabled)
    {
        PreferencesState.ShowStatusesView = enabled;
        keyValueStore.PutBoolean(KeyConsts.M_ShowStatusesView, enabled);
        keyValueStore.Flush();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Persists whether the room ticker summary should be shown.</summary>
    /// <param name="enabled">The requested visibility.</param>
    public void SetShowsTickers(bool enabled)
    {
        PreferencesState.ShowTickerView = enabled;
        keyValueStore.PutBoolean(KeyConsts.M_ShowTickerView, enabled);
        keyValueStore.Flush();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets searchable room users with Friends first and immutable note/role metadata.</summary>
    /// <param name="roomName">The joined room.</param>
    /// <param name="query">An optional user-name or note filter.</param>
    /// <returns>Detached user rows.</returns>
    public IReadOnlyList<RoomUserRow> GetRoomUsers(string roomName, string? query = null)
    {
        RoomSessionSnapshot? room = GetRoom(roomName);
        if (room?.RoomData is null)
        {
            return [];
        }

        HashSet<string> friends = userLists.GetSnapshot()
            .Where(item => item.Role == Seeker.UserRole.Friend)
            .Select(item => item.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? owner = room.RoomData.Owner;
        HashSet<string> moderators = (room.RoomData.Operators ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<IosUserListEntrySnapshot> savedUsers = userLists.GetSnapshot();
        IEnumerable<RoomUserRow> rows = room.RoomData.Users.Select(user =>
        {
            string? note = UserMetadataService.UserNotes
                .FirstOrDefault(pair => string.Equals(pair.Key, user.Username, StringComparison.OrdinalIgnoreCase))
                .Value;
            RoomUserRole role = string.Equals(user.Username, owner, StringComparison.OrdinalIgnoreCase)
                ? RoomUserRole.Owner
                : moderators.Contains(user.Username) ? RoomUserRole.Moderator : RoomUserRole.Member;
            return new RoomUserRow(
                user.Username,
                user.Status,
                user.AverageSpeed,
                user.FileCount,
                user.DirectoryCount,
                friends.Contains(user.Username),
                note,
                role);
        });
        HashSet<string> activeNames = room.RoomData.Users
            .Select(user => user.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<RoomUserRow> inactivePrivateMembers = GetPrivateMembers(roomName)
            .Where(username => !activeNames.Contains(username))
            .Select(username =>
            {
                IosUserListEntrySnapshot? saved = savedUsers.FirstOrDefault(item =>
                    string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
                string? note = UserMetadataService.UserNotes
                    .FirstOrDefault(pair => string.Equals(pair.Key, username, StringComparison.OrdinalIgnoreCase))
                    .Value;
                RoomUserRole role = string.Equals(username, owner, StringComparison.OrdinalIgnoreCase)
                    ? RoomUserRole.Owner
                    : moderators.Contains(username) ? RoomUserRole.Moderator : RoomUserRole.Member;
                return new RoomUserRow(
                    username,
                    saved?.Presence ?? UserPresence.Offline,
                    saved?.AverageSpeed ?? 0,
                    saved?.FileCount ?? 0,
                    saved?.DirectoryCount ?? 0,
                    friends.Contains(username),
                    note,
                    role);
            });
        rows = rows.Concat(inactivePrivateMembers);
        if (!string.IsNullOrWhiteSpace(query))
        {
            string value = query.Trim();
            rows = rows.Where(row =>
                row.Username.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                (row.Note?.Contains(value, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        return rows
            .OrderByDescending(row => row.IsFriend)
            .ThenBy(row => row.Role)
            .ThenByDescending(row => row.Presence)
            .ThenBy(row => row.Username, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Refreshes the server room list.</summary>
    /// <param name="cancellationToken">Cancels connection or refresh.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await social.RefreshRoomListAsync(cancellationToken);
    }

    /// <summary>Joins or creates a named room.</summary>
    /// <param name="roomName">The room name.</param>
    /// <param name="isPrivate">Whether to request private-room creation.</param>
    /// <param name="cancellationToken">Cancels joining.</param>
    /// <returns>The joined room snapshot.</returns>
    public Task<RoomSessionSnapshot> JoinAsync(
        string roomName,
        bool isPrivate = false,
        CancellationToken cancellationToken = default) =>
        social.JoinRoomAsync(roomName, isPrivate, cancellationToken);

    /// <summary>Leaves a joined room.</summary>
    /// <param name="roomName">The room name.</param>
    /// <param name="cancellationToken">Cancels leaving.</param>
    public Task LeaveAsync(string roomName, CancellationToken cancellationToken = default) =>
        social.LeaveRoomAsync(roomName, cancellationToken);

    /// <summary>Sends a room message.</summary>
    /// <param name="roomName">The room name.</param>
    /// <param name="message">The non-empty message body.</param>
    /// <param name="cancellationToken">Cancels sending.</param>
    /// <returns>The resulting retained message entry.</returns>
    public Task<RoomMessageEntry> SendAsync(
        string roomName,
        string message,
        CancellationToken cancellationToken = default) =>
        social.SendRoomMessageAsync(roomName, message, cancellationToken);

    /// <summary>Marks one room visible and clears its unread state.</summary>
    /// <param name="roomName">The room name or <see langword="null"/> when leaving detail.</param>
    public void SetVisibleRoom(string? roomName) => social.SetVisibleRoom(roomName);

    /// <summary>Updates the current account's auto-join preference.</summary>
    /// <param name="roomName">The room name.</param>
    /// <param name="enabled">Whether to join automatically.</param>
    /// <returns>Whether persisted state changed.</returns>
    public bool SetAutoJoin(string roomName, bool enabled) => social.SetAutoJoin(roomName, enabled);

    /// <summary>Gets whether one room is configured for auto-join.</summary>
    /// <param name="roomName">The room name.</param>
    /// <returns>The persisted preference.</returns>
    public bool IsAutoJoin(string roomName) =>
        social.GetAutoJoinRooms().Contains(roomName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Updates room-notification preference.</summary>
    /// <param name="roomName">The room name.</param>
    /// <param name="enabled">Whether notifications are enabled.</param>
    /// <returns>Whether persisted state changed.</returns>
    public bool SetNotifications(string roomName, bool enabled) =>
        social.SetRoomNotifications(roomName, enabled);

    /// <summary>Gets whether incoming messages in one room may notify.</summary>
    /// <param name="roomName">The room name.</param>
    /// <returns>The persisted preference.</returns>
    public bool HasNotifications(string roomName) =>
        social.GetNotificationRooms().Contains(roomName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets or clears the current account's ticker message.</summary>
    /// <param name="roomName">The joined room.</param>
    /// <param name="message">The ticker text, or empty to clear.</param>
    /// <param name="cancellationToken">Cancels the server request.</param>
    public Task SetTickerAsync(
        string roomName,
        string message,
        CancellationToken cancellationToken = default) =>
        social.SetRoomTickerAsync(roomName, message, cancellationToken);

    /// <summary>Adds a user to a private room through the social command boundary.</summary>
    /// <param name="roomName">The private room.</param>
    /// <param name="username">The user to invite.</param>
    /// <param name="cancellationToken">Cancels the server operation.</param>
    public Task AddPrivateMemberAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default) =>
        social.AddPrivateRoomMemberAsync(roomName, username, cancellationToken);

    /// <summary>Removes a user from a private room through the social command boundary.</summary>
    /// <param name="roomName">The private room.</param>
    /// <param name="username">The member to remove.</param>
    /// <param name="cancellationToken">Cancels the server operation.</param>
    public Task RemovePrivateMemberAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default) =>
        social.RemovePrivateRoomMemberAsync(roomName, username, cancellationToken);

    /// <summary>Promotes a private-room member to moderator.</summary>
    /// <param name="roomName">The private room.</param>
    /// <param name="username">The member to promote.</param>
    /// <param name="cancellationToken">Cancels the server operation.</param>
    public Task AddModeratorAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default) =>
        social.AddPrivateRoomModeratorAsync(roomName, username, cancellationToken);

    /// <summary>Removes moderator privileges from a private-room user.</summary>
    /// <param name="roomName">The private room.</param>
    /// <param name="username">The moderator to demote.</param>
    /// <param name="cancellationToken">Cancels the server operation.</param>
    public Task RemoveModeratorAsync(
        string roomName,
        string username,
        CancellationToken cancellationToken = default) =>
        social.RemovePrivateRoomModeratorAsync(roomName, username, cancellationToken);

    /// <summary>Drops the current account's private-room membership.</summary>
    /// <param name="roomName">The private room.</param>
    /// <param name="cancellationToken">Cancels the server operation.</param>
    public Task DropMembershipAsync(string roomName, CancellationToken cancellationToken = default) =>
        social.DropPrivateRoomMembershipAsync(roomName, cancellationToken);

    /// <summary>Drops the current account's ownership of a private room.</summary>
    /// <param name="roomName">The owned private room.</param>
    /// <param name="cancellationToken">Cancels the server operation.</param>
    public Task DropOwnershipAsync(string roomName, CancellationToken cancellationToken = default) =>
        social.DropPrivateRoomOwnershipAsync(roomName, cancellationToken);

    /// <summary>Releases social-service event subscriptions.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        social.RoomListChanged -= OnRoomListChanged;
        social.RoomChanged -= OnRoomChanged;
    }

    /// <summary>Adds server rooms without replacing richer joined-room snapshots.</summary>
    /// <param name="destination">The destination dictionary.</param>
    /// <param name="rooms">The optional server room list.</param>
    /// <param name="group">The category for newly added rooms.</param>
    /// <param name="known">Known room session snapshots.</param>
    /// <summary>Enumerates every room the server currently lists, across all visibilities.</summary>
    private IEnumerable<RoomInfo> ServerRooms() =>
        (social.RoomList?.Public ?? [])
            .Concat(social.RoomList?.Private ?? [])
            .Concat(social.RoomList?.Owned ?? []);

    /// <summary>
    /// Keeps a locally retained room visible when the server listing does not name it, so a room left before
    /// the list refreshes cannot vanish from the overview entirely.
    /// </summary>
    /// <param name="destination">The accumulating summaries.</param>
    /// <param name="rooms">Every locally retained room session.</param>
    private static void AddRetainedRooms(
        IDictionary<string, ChatroomSummary> destination,
        IEnumerable<RoomSessionSnapshot> rooms)
    {
        foreach (RoomSessionSnapshot room in rooms)
        {
            if (destination.ContainsKey(room.RoomName))
            {
                continue;
            }

            destination[room.RoomName] = new ChatroomSummary(
                room.RoomName,
                room.RoomData?.UserCount ?? 0,
                ChatroomGroup.Public,
                room.ConnectionState,
                room.HasUnreadMessages);
        }
    }

    private static void AddServerRooms(
        IDictionary<string, ChatroomSummary> destination,
        IEnumerable<RoomInfo>? rooms,
        ChatroomGroup group,
        IReadOnlyDictionary<string, RoomSessionSnapshot> known)
    {
        foreach (RoomInfo room in rooms ?? [])
        {
            if (destination.ContainsKey(room.Name))
            {
                continue;
            }

            RoomSessionSnapshot? snapshot = known.GetValueOrDefault(room.Name);
            destination[room.Name] = new ChatroomSummary(
                room.Name,
                room.UserCount,
                group,
                snapshot?.ConnectionState ?? RoomConnectionState.NotJoined,
                snapshot?.HasUnreadMessages ?? false);
        }
    }

    /// <summary>Forwards room-list updates.</summary>
    /// <param name="sender">The social service.</param>
    /// <param name="args">Unused event data.</param>
    private void OnRoomListChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Forwards individual room updates.</summary>
    /// <param name="sender">The social service.</param>
    /// <param name="args">The changed room descriptor.</param>
    private void OnRoomChanged(object? sender, RoomStateChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Describes one detached friend or ignored-user row.</summary>
/// <param name="Username">The Soulseek user name.</param>
/// <param name="IsIgnored">Whether the user is ignored rather than a friend.</param>
/// <param name="Presence">The last observed presence.</param>
/// <param name="DoesNotExist">Whether the server reported a missing account.</param>
/// <param name="Note">The private local note.</param>
/// <param name="AlertsWhenOnline">Whether online transition alerts are enabled.</param>
/// <param name="AverageSpeed">The optional average upload speed.</param>
/// <param name="FileCount">The optional shared-file count.</param>
/// <param name="DirectoryCount">The optional shared-directory count.</param>
internal sealed record UserListRow(
    string Username,
    bool IsIgnored,
    UserPresence Presence,
    bool DoesNotExist,
    string? Note,
    bool AlertsWhenOnline,
    int? AverageSpeed,
    int? FileCount,
    int? DirectoryCount);

/// <summary>Coordinates friend, ignore, alert, and note operations through immutable snapshots.</summary>
internal sealed class UserListPresentationStore : IDisposable
{
    private readonly IosUserListService userLists;
    private readonly IosSocialSessionService social;
    private readonly PortableAppDataStateService appDataState;
    private readonly IKeyValueStore keyValueStore;
    private bool disposed;

    /// <summary>Creates a user-list presentation facade.</summary>
    /// <param name="userLists">The canonical list service.</param>
    /// <param name="social">The server-side friend and ignore coordinator.</param>
    /// <param name="appDataState">The portable metadata persistence boundary.</param>
    public UserListPresentationStore(
        IosUserListService userLists,
        IosSocialSessionService social,
        PortableAppDataStateService appDataState,
        IKeyValueStore? keyValueStore = null)
    {
        this.userLists = userLists ?? throw new ArgumentNullException(nameof(userLists));
        this.social = social ?? throw new ArgumentNullException(nameof(social));
        this.appDataState = appDataState ?? throw new ArgumentNullException(nameof(appDataState));
        this.keyValueStore = keyValueStore ?? AppCompositionRoot.KeyValueStore;
        userLists.Changed += OnChanged;
        social.UserChanged += OnUserChanged;
    }

    /// <summary>Raised after any list entry or attached metadata changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Creates the production user-list presentation facade.</summary>
    /// <returns>A safe user-list store.</returns>
    public static UserListPresentationStore CreateDefault() => new(
        AppCompositionRoot.UserLists,
        AppCompositionRoot.Social,
        AppCompositionRoot.AppDataState,
        AppCompositionRoot.KeyValueStore);

    /// <summary>Gets detached rows, optionally filtered by user name or note.</summary>
    /// <param name="query">The optional filter.</param>
    /// <returns>Friends followed by ignored users in current sort order.</returns>
    public IReadOnlyList<UserListRow> GetRows(string? query = null)
    {
        IEnumerable<UserListRow> rows = userLists.GetSnapshot().Select(item => new UserListRow(
            item.Username,
            item.Role == Seeker.UserRole.Ignored,
            item.Presence,
            item.DoesNotExist,
            UserMetadataService.UserNotes.TryGetValue(item.Username, out string? note) ? note : null,
            UserMetadataService.UserOnlineAlerts.ContainsKey(item.Username),
            item.AverageSpeed,
            item.FileCount,
            item.DirectoryCount));
        if (!string.IsNullOrWhiteSpace(query))
        {
            string value = query.Trim();
            rows = rows.Where(row =>
                row.Username.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
                (row.Note?.Contains(value, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        return rows
            .OrderBy(row => row.IsIgnored)
            .ThenBy(row => SortKey(row))
            .ThenBy(row => row.Username, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Gets the active deterministic sort order.</summary>
    public SortOrder SortOrder => PreferencesState.UserListSortOrder;

    /// <summary>Persists a user-list sort order through the portable preferences writer.</summary>
    /// <param name="order">The requested order.</param>
    public void SetSortOrder(SortOrder order)
    {
        PreferencesState.UserListSortOrder = order;
        keyValueStore.PutInt(KeyConsts.M_UserListSortOrder, (int)order);
        keyValueStore.Flush();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds or refreshes a friend and its server watch.</summary>
    /// <param name="username">The user name.</param>
    /// <param name="cancellationToken">Cancels connection or watch.</param>
    public async Task AddFriendAsync(string username, CancellationToken cancellationToken = default)
    {
        await social.AddFriendAsync(username.Trim(), cancellationToken);
    }

    /// <summary>Removes a friend and stops its server watch.</summary>
    /// <param name="username">The user name.</param>
    /// <param name="cancellationToken">Cancels connection or unwatch.</param>
    public async Task RemoveFriendAsync(string username, CancellationToken cancellationToken = default)
    {
        await social.RemoveFriendAsync(username, cancellationToken);
    }

    /// <summary>Adds a user to the ignore list and removes a conflicting friend watch.</summary>
    /// <param name="username">The user name.</param>
    /// <param name="cancellationToken">Cancels a required server unwatch.</param>
    public async Task IgnoreAsync(string username, CancellationToken cancellationToken = default)
    {
        await social.AddIgnoredUserAsync(username.Trim(), cancellationToken);
    }

    /// <summary>Removes one user from the ignore list.</summary>
    /// <param name="username">The user name.</param>
    /// <returns>Whether an entry existed.</returns>
    public bool StopIgnoring(string username) => social.RemoveIgnoredUser(username);

    /// <summary>Persists or clears a private user note.</summary>
    /// <param name="username">The user name.</param>
    /// <param name="note">The note text; blank clears the note.</param>
    public void SetNote(string username, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string? existingKey = UserMetadataService.UserNotes.Keys.FirstOrDefault(key =>
            string.Equals(key, username, StringComparison.OrdinalIgnoreCase));
        string normalized = note?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            if (existingKey is not null)
            {
                UserMetadataService.UserNotes.TryRemove(existingKey, out _);
            }
        }
        else
        {
            UserMetadataService.UserNotes[existingKey ?? username] = normalized;
        }

        appDataState.PersistUserMetadata();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Updates whether an offline-to-online transition should notify.</summary>
    /// <param name="username">The friend name.</param>
    /// <param name="enabled">Whether alerts are enabled.</param>
    public void SetOnlineAlert(string username, bool enabled)
    {
        social.SetOnlineAlert(username, enabled);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases list and social event subscriptions.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        userLists.Changed -= OnChanged;
        social.UserChanged -= OnUserChanged;
    }

    /// <summary>Computes the selected deterministic user-list ordering.</summary>
    /// <param name="row">The row to sort.</param>
    /// <returns>A comparable sort key.</returns>
    private static string SortKey(UserListRow row) => PreferencesState.UserListSortOrder switch
    {
        SortOrder.OnlineStatus => ((int)row.Presence).ToString("D2"),
        SortOrder.Alphabetical => row.Username,
        SortOrder.DateAddedDesc => string.Empty,
        _ => string.Empty,
    };

    /// <summary>Forwards canonical list changes.</summary>
    /// <param name="sender">The list service.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Forwards social user updates.</summary>
    /// <param name="sender">The social service.</param>
    /// <param name="args">The changed user descriptor.</param>
    private void OnUserChanged(object? sender, SocialUserChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Describes list membership and local metadata available to a remote-profile action surface.</summary>
/// <param name="IsFriend">Whether the remote user is in the canonical friend list.</param>
/// <param name="IsIgnored">Whether the remote user is in the canonical ignore list.</param>
/// <param name="Note">The private local note, when present.</param>
/// <param name="AlertsWhenOnline">Whether an online-transition alert is enabled.</param>
internal sealed record ProfileUserActionState(
    bool IsFriend,
    bool IsIgnored,
    string? Note,
    bool AlertsWhenOnline);

/// <summary>Owns one remote profile snapshot and typed social/user-list actions.</summary>
internal sealed class UserProfilePresentationStore : IDisposable
{
    private readonly IosSocialSessionService social;
    private readonly UserListPresentationStore? userLists;
    private readonly AppSession? session;
    private readonly string username;
    private readonly bool ownsUserLists;
    private bool disposed;

    /// <summary>Creates a remote-profile presentation store.</summary>
    /// <param name="social">The profile-fetch service.</param>
    /// <param name="username">The remote user name.</param>
    public UserProfilePresentationStore(IosSocialSessionService social, string username)
        : this(social, username, userLists: null, session: null, ownsUserLists: false)
    {
    }

    /// <summary>Creates a remote-profile store with an optional reusable user-list facade.</summary>
    /// <param name="social">The profile-fetch service.</param>
    /// <param name="username">The remote user name.</param>
    /// <param name="userLists">The facade used for friend, ignore, note, and alert commands.</param>
    /// <param name="session">The authenticated privilege-query and grant facade.</param>
    /// <param name="ownsUserLists">Whether disposal should release the supplied facade.</param>
    private UserProfilePresentationStore(
        IosSocialSessionService social,
        string username,
        UserListPresentationStore? userLists,
        AppSession? session,
        bool ownsUserLists)
    {
        this.social = social ?? throw new ArgumentNullException(nameof(social));
        this.userLists = userLists;
        this.session = session;
        this.ownsUserLists = ownsUserLists;
        this.username = string.IsNullOrWhiteSpace(username)
            ? throw new ArgumentException("A user name is required.", nameof(username))
            : username;
        social.UserChanged += OnUserChanged;
        if (userLists is not null)
        {
            userLists.Changed += OnUserListChanged;
        }
    }

    /// <summary>Raised after the selected profile changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the selected user name.</summary>
    public string Username => username;

    /// <summary>Gets the latest detached profile snapshot.</summary>
    public UserProfileSnapshot? Snapshot => social.GetUserProfile(username);

    /// <summary>Gets whether the production session facade can grant privileges to this remote account.</summary>
    public bool SupportsPrivilegeGrant =>
        session is not null &&
        !string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the last server-reported remaining privilege seconds, when successfully refreshed.</summary>
    public int? RemainingPrivilegeSeconds { get; private set; }

    /// <summary>Gets membership and local metadata when a user-list action facade is available.</summary>
    public ProfileUserActionState? UserActionState
    {
        get
        {
            if (userLists is null)
            {
                return null;
            }

            UserListRow? row = userLists.GetRows().FirstOrDefault(item =>
                string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
            string? note = row?.Note;
            if (row is null && UserMetadataService.UserNotes.TryGetValue(username, out string? storedNote))
            {
                note = storedNote;
            }

            return new ProfileUserActionState(
                row is { IsIgnored: false },
                row?.IsIgnored == true,
                note,
                row?.AlertsWhenOnline ?? UserMetadataService.UserOnlineAlerts.ContainsKey(username));
        }
    }

    /// <summary>Creates a production profile store.</summary>
    /// <param name="username">The remote user name.</param>
    /// <returns>A remote-profile store.</returns>
    public static UserProfilePresentationStore CreateDefault(string username) =>
        new(
            AppCompositionRoot.Social,
            username,
            UserListPresentationStore.CreateDefault(),
            AppCompositionRoot.Session,
            ownsUserLists: true);

    /// <summary>Fetches status, statistics, and profile information concurrently.</summary>
    /// <param name="cancellationToken">Cancels the profile request.</param>
    /// <returns>The refreshed detached profile.</returns>
    public Task<UserProfileSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        social.FetchUserProfileAsync(username, cancellationToken);

    /// <summary>Refreshes the authenticated account's transferable privilege balance.</summary>
    /// <param name="cancellationToken">Cancels the server request.</param>
    /// <returns>The non-negative remaining seconds.</returns>
    public async Task<int> RefreshPrivilegesAsync(CancellationToken cancellationToken = default)
    {
        AppSession activeSession = RequireSession();
        RemainingPrivilegeSeconds = await activeSession.GetPrivilegesAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
        return RemainingPrivilegeSeconds.Value;
    }

    /// <summary>Validates and transfers whole privilege days, then refreshes the remaining balance.</summary>
    /// <param name="days">A positive number of whole days.</param>
    /// <param name="cancellationToken">Cancels server requests.</param>
    /// <returns><see langword="false"/> when the current balance cannot cover the requested days.</returns>
    public async Task<bool> GrantPrivilegesAsync(int days, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, 36_500);
        AppSession activeSession = RequireSession();
        int availableSeconds = await activeSession.GetPrivilegesAsync(cancellationToken);
        RemainingPrivilegeSeconds = availableSeconds;
        if ((long)days * 86_400 > availableSeconds)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        await activeSession.GrantPrivilegesAsync(username, days, cancellationToken);
        try
        {
            RemainingPrivilegeSeconds = await activeSession.GetPrivilegesAsync(cancellationToken);
        }
        catch
        {
            // The transfer succeeded; a follow-up read must not turn that success into a misleading failure.
            RemainingPrivilegeSeconds = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Adds the represented user as a friend.</summary>
    /// <param name="cancellationToken">Cancels a required connection or watch.</param>
    public Task AddFriendAsync(CancellationToken cancellationToken = default) =>
        RequireUserLists().AddFriendAsync(username, cancellationToken);

    /// <summary>Removes the represented user from the friend list.</summary>
    /// <param name="cancellationToken">Cancels a required connection or unwatch.</param>
    public Task RemoveFriendAsync(CancellationToken cancellationToken = default) =>
        RequireUserLists().RemoveFriendAsync(username, cancellationToken);

    /// <summary>Adds the represented user to the ignore list.</summary>
    /// <param name="cancellationToken">Cancels a required server unwatch.</param>
    public Task IgnoreAsync(CancellationToken cancellationToken = default) =>
        RequireUserLists().IgnoreAsync(username, cancellationToken);

    /// <summary>Removes the represented user from the ignore list.</summary>
    /// <returns>Whether an ignored entry existed.</returns>
    public bool StopIgnoring() => RequireUserLists().StopIgnoring(username);

    /// <summary>Persists or clears a private local note for the represented user.</summary>
    /// <param name="note">The note; blank clears it.</param>
    public void SetNote(string? note) => RequireUserLists().SetNote(username, note);

    /// <summary>Changes online-transition alerts for the represented friend.</summary>
    /// <param name="enabled">Whether an alert should be delivered.</param>
    public void SetOnlineAlert(bool enabled) => RequireUserLists().SetOnlineAlert(username, enabled);

    /// <summary>Releases the profile change subscription.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        social.UserChanged -= OnUserChanged;
        if (userLists is not null)
        {
            userLists.Changed -= OnUserListChanged;
            if (ownsUserLists)
            {
                userLists.Dispose();
            }
        }
    }

    /// <summary>Gets the configured user-list facade or fails fast for a profile-only test store.</summary>
    /// <returns>The reusable user-list facade.</returns>
    private UserListPresentationStore RequireUserLists() =>
        userLists ?? throw new InvalidOperationException("This profile store has no user-list action facade.");

    /// <summary>Gets the configured authenticated-session facade.</summary>
    /// <returns>The privilege command source.</returns>
    private AppSession RequireSession() =>
        session ?? throw new InvalidOperationException("This profile store has no privilege command facade.");

    /// <summary>Publishes matching profile changes only.</summary>
    /// <param name="sender">The social service.</param>
    /// <param name="args">The changed user descriptor.</param>
    private void OnUserChanged(object? sender, SocialUserChangedEventArgs args)
    {
        if (string.Equals(args.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Publishes list or metadata changes that can alter profile actions.</summary>
    /// <param name="sender">The user-list facade.</param>
    /// <param name="args">Unused event data.</param>
    private void OnUserListChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Describes immutable application, connection, sharing, mapping, and log diagnostics.</summary>
/// <param name="Version">The bundled application version.</param>
/// <param name="IsConnected">Whether the Soulseek session is authenticated.</param>
/// <param name="SharedFileCount">The indexed shared-file count.</param>
/// <param name="SharedDirectoryCount">The indexed shared-directory count.</param>
/// <param name="PortMapping">The current NAT-PMP snapshot.</param>
/// <param name="BackgroundTransfers">The most recent iOS answer to a continued-processing request.</param>
/// <param name="LogPath">The local rotating diagnostic log path.</param>
internal sealed record DiagnosticsSnapshot(
    string Version,
    bool IsConnected,
    int SharedFileCount,
    int SharedDirectoryCount,
    NatPmpPortMappingSnapshot PortMapping,
    ContinuedTransferAvailability BackgroundTransfers,
    string LogPath);

/// <summary>Provides safe immutable diagnostics and a validated shareable log URL.</summary>
internal sealed class DiagnosticsPresentationStore
{
    private readonly AppSession session;
    private readonly IosShareIndexService shareIndex;
    private readonly NatPmpPortMappingService portMapping;
    private readonly Func<ContinuedTransferAvailability> backgroundTransfers;
    private readonly IosLoggerBackend logger;

    /// <summary>Creates a diagnostics presentation store.</summary>
    /// <param name="session">The connection snapshot source.</param>
    /// <param name="shareIndex">The share-count source.</param>
    /// <param name="portMapping">The NAT-PMP snapshot source.</param>
    /// <param name="backgroundTransfers">The latest continued-processing grant outcome.</param>
    /// <param name="logger">The diagnostic log source.</param>
    public DiagnosticsPresentationStore(
        AppSession session,
        IosShareIndexService shareIndex,
        NatPmpPortMappingService portMapping,
        Func<ContinuedTransferAvailability> backgroundTransfers,
        IosLoggerBackend logger)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.shareIndex = shareIndex ?? throw new ArgumentNullException(nameof(shareIndex));
        this.portMapping = portMapping ?? throw new ArgumentNullException(nameof(portMapping));
        this.backgroundTransfers = backgroundTransfers ?? throw new ArgumentNullException(nameof(backgroundTransfers));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Creates the production diagnostics store.</summary>
    /// <returns>A diagnostics store.</returns>
    public static DiagnosticsPresentationStore CreateDefault() => new(
        AppCompositionRoot.Session,
        AppCompositionRoot.ShareIndex,
        AppCompositionRoot.PortMapping,
        () => AppCompositionRoot.BackgroundTasks.TransferAvailability,
        AppCompositionRoot.LoggerBackend);

    /// <summary>Gets a detached diagnostic snapshot.</summary>
    /// <returns>The current safe diagnostic values.</returns>
    public DiagnosticsSnapshot GetSnapshot()
    {
        SharedFileCatalog catalog = shareIndex.Catalog;
        string version = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString()
            ?? StringResources.Get("IosUiUnknown");
        return new DiagnosticsSnapshot(
            version,
            session.IsConnected,
            catalog.FileCount,
            catalog.DirectoryCount,
            portMapping.Snapshot,
            backgroundTransfers(),
            logger.LogPath);
    }

    /// <summary>Refreshes share and NAT-PMP diagnostics.</summary>
    /// <param name="cancellationToken">Cancels the share scan.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await shareIndex.RefreshAsync(cancellationToken);
        portMapping.RefreshIfNeeded();
    }

    /// <summary>Gets a file URL only when the bounded log exists and is non-empty.</summary>
    /// <returns>A shareable file URL or <see langword="null"/>.</returns>
    public NSUrl? GetLogUrl()
    {
        FileInfo info = new(logger.LogPath);
        return info.Exists && info.Length > 0 && logger.IsLogPrivacySafe()
            ? NSUrl.FromFilename(info.FullName)
            : null;
    }
}
