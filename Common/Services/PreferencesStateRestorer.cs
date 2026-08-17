using Common;
using Common.Messages;
using System;
using System.Collections.Generic;

namespace Seeker.Services
{
    /// <summary>
    /// Restores the platform-neutral application preferences into <see cref="PreferencesState"/>.
    /// </summary>
    /// <remarks>
    /// This service deliberately has no knowledge of Android shared preferences, iOS user defaults,
    /// UI objects, or storage permission APIs. Android SAF locations and their permission-dependent
    /// side effects are not restored: iOS uses fixed sandbox locations instead. The portable folder
    /// shape settings that affect <c>Common</c> download behavior are restored.
    /// </remarks>
    public sealed class PreferencesStateRestorer
    {
        private const int MissingInteger = int.MinValue;
        private const int DefaultTransferSpeedBytesPerSecond = 4 * 1024 * 1024;

        private readonly IKeyValueStore store;

        /// <summary>
        /// Creates a preference restorer over a platform-specific key-value store.
        /// </summary>
        /// <param name="store">The non-null store from which persisted preferences are read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
        public PreferencesStateRestorer(IKeyValueStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Restores every persisted <see cref="PreferencesState"/> value that is meaningful to the
        /// portable core or iOS head.
        /// </summary>
        /// <remarks>
        /// Missing values receive the same defaults as the Android preference restore path. Search history is
        /// accepted in current JSON or legacy XML form; a malformed value is treated as empty so one damaged
        /// preference cannot prevent application startup. This method never writes to or flushes the backing store.
        /// </remarks>
        public void RestoreAll()
        {
            RestoreAccount();
            RestoreAppearance();
            RestoreTransfers();
            RestoreSpeedLimits();
            RestoreSearch();
            RestoreSocial();
            RestoreSharingAndDiagnostics();
            RestoreListener();
        }

        private void RestoreAccount()
        {
            PreferencesState.CurrentlyLoggedIn = store.GetBoolean(KeyConsts.M_CurrentlyLoggedIn, false);
            PreferencesState.SetCredentials(
                GetString(KeyConsts.M_Username, string.Empty),
                GetString(KeyConsts.M_Password, string.Empty));
        }

        private void RestoreAppearance()
        {
            PreferencesState.DayNightMode = store.GetInt(KeyConsts.M_DayNightMode, -1);
            PreferencesState.Language = GetString(KeyConsts.M_Lanuage, PreferencesState.FieldLangAuto);
            PreferencesState.LegacyLanguageMigrated = store.GetBoolean(KeyConsts.M_LegacyLanguageMigrated, false);
            PreferencesState.NightModeVariant = (NightThemeType)store.GetInt(
                KeyConsts.M_NightVariant,
                (int)NightThemeType.ClassicPurple);
            PreferencesState.DayModeVariant = (DayThemeType)store.GetInt(
                KeyConsts.M_DayVariant,
                (int)DayThemeType.ClassicPurple);
            PreferencesState.ShowSmartFilters = store.GetBoolean(KeyConsts.M_ShowSmartFilters, true);
            PreferencesState.SmartFilterStyle = (SmartFilterStyle)store.GetInt(
                KeyConsts.M_SmartFilterStyle,
                (int)SmartFilterStyle.Flat);
            PreferencesState.SmartFilterOptions = new PreferencesState.SmartFilterState
            {
                KeywordsEnabled = store.GetBoolean(KeyConsts.M_SmartFilter_KeywordsEnabled, true),
                KeywordsOrder = store.GetInt(KeyConsts.M_SmartFilter_KeywordsOrder, 0),
                FileTypesEnabled = store.GetBoolean(KeyConsts.M_SmartFilter_TypesEnabled, true),
                FileTypesOrder = store.GetInt(KeyConsts.M_SmartFilter_TypesOrder, 1),
                NumFilesEnabled = store.GetBoolean(KeyConsts.M_SmartFilter_CountsEnabled, true),
                NumFilesOrder = store.GetInt(KeyConsts.M_SmartFilter_CountsOrder, 2),
            };
        }

        private void RestoreTransfers()
        {
            PreferencesState.AutoClearCompleteDownloads = store.GetBoolean(KeyConsts.M_AutoClearComplete, false);
            PreferencesState.AutoClearCompleteUploads = store.GetBoolean(KeyConsts.M_AutoClearCompleteUploads, false);
            PreferencesState.TransferViewShowSizes = store.GetBoolean(KeyConsts.M_TransfersShowSizes, true);
            PreferencesState.TransferViewShowSpeed = store.GetBoolean(KeyConsts.M_TransfersShowSpeed, true);
            PreferencesState.TransferViewGroupByFolder = store.GetBoolean(KeyConsts.M_TransfersGroupByFolder, false);
            PreferencesState.TransferViewInUploadsMode = store.GetBoolean(KeyConsts.M_TransfersInUploadsMode, false);
            PreferencesState.DisableDownloadToastNotification = store.GetBoolean(
                KeyConsts.M_DisableToastNotifications,
                true);
            PreferencesState.MemoryBackedDownload = store.GetBoolean(KeyConsts.M_MemoryBackedDownload, false);
            PreferencesState.NoSubfolderForSingle = store.GetBoolean(KeyConsts.M_NoSubfolderForSingle, false);
            PreferencesState.KeepScreenAwakeWhileTransferring = store.GetBoolean(
                KeyConsts.M_KeepScreenAwakeWhileTransferring,
                false);
            PreferencesState.CreateUsernameSubfolders = store.GetBoolean(
                KeyConsts.M_AdditionalUsernameSubdirectories,
                false);
            PreferencesState.NotifyOnFolderCompleted = store.GetBoolean(KeyConsts.M_NotifyFolderComplete, true);
            PreferencesState.AutoRetryBackOnline = store.GetBoolean(KeyConsts.M_AutoRetryBackOnline, true);
        }

        private void RestoreSpeedLimits()
        {
            PreferencesState.SpeedLimitUploadOn = store.GetBoolean(KeyConsts.M_UploadLimitEnabled, false);
            PreferencesState.SpeedLimitDownloadOn = store.GetBoolean(KeyConsts.M_DownloadLimitEnabled, false);
            PreferencesState.SpeedLimitUploadIsPerTransfer = store.GetBoolean(KeyConsts.M_UploadPerTransfer, true);
            PreferencesState.SpeedLimitDownloadIsPerTransfer = store.GetBoolean(KeyConsts.M_DownloadPerTransfer, true);
            PreferencesState.SpeedLimitUploadBytesSec = store.GetInt(
                KeyConsts.M_UploadSpeedLimitBytes,
                DefaultTransferSpeedBytesPerSecond);
            PreferencesState.SpeedLimitDownloadBytesSec = store.GetInt(
                KeyConsts.M_DownloadSpeedLimitBytes,
                DefaultTransferSpeedBytesPerSecond);
        }

        private void RestoreSearch()
        {
            PreferencesState.NumberSearchResults = store.GetInt(KeyConsts.M_NumberSearchResults, 500);
            PreferencesState.RememberSearchHistory = store.GetBoolean(KeyConsts.M_RememberSearchHistory, true);
            PreferencesState.ShowRecentUsers = store.GetBoolean(KeyConsts.M_RememberUserHistory, true);
            PreferencesState.FreeUploadSlotsOnly = store.GetBoolean(KeyConsts.M_OnlyFreeUploadSlots, true);
            PreferencesState.HideLockedResultsInBrowse = store.GetBoolean(KeyConsts.M_HideLockedBrowse, true);
            PreferencesState.HideLockedResultsInSearch = store.GetBoolean(KeyConsts.M_HideLockedSearch, true);
            PreferencesState.FilterSticky = store.GetBoolean(KeyConsts.M_FilterSticky, false);
            PreferencesState.FilterStickyString = GetString(KeyConsts.M_FilterStickyString, string.Empty);

            PreferencesState.SearchResultStyle = RestoreSearchResultStyle();
            // Parity with the Android restore path: the legacy "ExpandedAll" style migration never influenced
            // this flag there (the migration side effect was unconditionally overwritten by a read with a
            // false default), so an absent key always restores false.
            PreferencesState.ExpandAllResults = store.GetBoolean(KeyConsts.M_ExpandAllResults, false);
            PreferencesState.DefaultSearchResultSortAlgorithm = (SearchResultSorting)store.GetInt(
                KeyConsts.M_DefaultSearchResultSortAlgorithm,
                (int)SearchResultSorting.Available);
            PreferencesState.SearchHistory = RestoreSearchHistory();
            PreferencesState.FilterFormat = (FormatFilterType)store.GetInt(
                KeyConsts.M_FilterFormat,
                (int)FormatFilterType.Any);
            PreferencesState.FilterMinBitrateKbs = store.GetInt(KeyConsts.M_FilterMinBitrateKbs, 0);
        }

        private void RestoreSocial()
        {
            PreferencesState.AllowPrivateRoomInvitations = store.GetBoolean(
                KeyConsts.M_AllowPrivateRooomInvitations,
                false);
            PreferencesState.StartServiceOnStartup = store.GetBoolean(KeyConsts.M_ServiceOnStartup, true);
            PreferencesState.AutoAwayOnInactivity = store.GetBoolean(KeyConsts.M_AutoSetAwayOnInactivity, false);
            PreferencesState.UserInfoBio = GetString(KeyConsts.M_UserInfoBio, string.Empty);
            PreferencesState.UserInfoPictureName = GetString(KeyConsts.M_UserInfoPicture, string.Empty);
            PreferencesState.ShowStatusesView = store.GetBoolean(KeyConsts.M_ShowStatusesView, true);
            PreferencesState.ShowTickerView = store.GetBoolean(KeyConsts.M_ShowTickerView, false);
            PreferencesState.SortChatroomUsersBy = (SortOrderChatroomUsers)store.GetInt(
                KeyConsts.M_RoomUserListSortOrder,
                (int)SortOrderChatroomUsers.Alphabetical);
            PreferencesState.PutFriendsOnTop = store.GetBoolean(KeyConsts.M_RoomUserListShowFriendsAtTop, false);
            PreferencesState.UserListSortOrder = (SortOrder)store.GetInt(
                KeyConsts.M_UserListSortOrder,
                (int)SortOrder.DateAddedAsc);
        }

        private void RestoreSharingAndDiagnostics()
        {
            PreferencesState.SharingOn = store.GetBoolean(KeyConsts.M_SharingOn, false);
            PreferencesState.UploadSpeed = store.GetInt(KeyConsts.M_UploadSpeed, -1);
            PreferencesState.AllowUploadsOnMetered = store.GetBoolean(KeyConsts.M_AllowUploadsOnMetered, true);
            PreferencesState.RequireVpnForSharing = store.GetBoolean(KeyConsts.M_RequireVpnForSharing, false);
            PreferencesState.LogDiagnostics = store.GetBoolean(KeyConsts.M_LOG_DIAGNOSTICS, false);
            PreferencesState.LimitSimultaneousDownloads = store.GetBoolean(
                KeyConsts.M_LimitSimultaneousDownloads,
                false);
            PreferencesState.MaxSimultaneousLimit = store.GetInt(KeyConsts.M_MaxSimultaneousLimit, 1);
        }

        private void RestoreListener()
        {
            PreferencesState.ListenerEnabled = store.GetBoolean(KeyConsts.M_ListenerEnabled, true);
            PreferencesState.ListenerPort = store.GetInt(KeyConsts.M_ListenerPort, 33939);
            PreferencesState.ListenerUPnpEnabled = store.GetBoolean(KeyConsts.M_ListenerUPnpEnabled, true);
        }

        private SearchResultStyleEnum RestoreSearchResultStyle()
        {
            int current = store.GetInt(KeyConsts.M_SearchResultStyleV2, MissingInteger);
            if (current != MissingInteger)
            {
                return (SearchResultStyleEnum)current;
            }

            return store.GetInt(KeyConsts.M_SearchResultStyle, MissingInteger) switch
            {
                0 => SearchResultStyleEnum.Compact,
                1 => SearchResultStyleEnum.ModernBottom,
                2 => SearchResultStyleEnum.SimpleBottomExpandable,
                3 => SearchResultStyleEnum.SimpleBottomExpandable,
                4 => SearchResultStyleEnum.ModernBottomExpandable,
                5 => SearchResultStyleEnum.ModernTop,
                11 => SearchResultStyleEnum.SimpleBottom,
                _ => SearchResultStyleEnum.ModernBottom,
            };
        }

        private List<string> RestoreSearchHistory()
        {
            string payload = GetString(KeyConsts.M_SearchHistory, string.Empty);
            return SerializationHelper.RestoreStringListFromString(payload);
        }

        private string GetString(string key, string defaultValue) =>
            store.GetString(key, defaultValue) ?? defaultValue;
    }
}
