using Common;
using System;

namespace Seeker.Services
{
    /// <summary>
    /// Persists the platform-neutral values held by <see cref="PreferencesState"/>.
    /// </summary>
    /// <remarks>
    /// The writer mirrors <see cref="PreferencesStateRestorer"/>. Android storage-location values are
    /// deliberately excluded because they represent SAF permissions rather than portable preferences.
    /// Values are prepared before any store mutation, then written as one logical snapshot followed by one
    /// <see cref="IKeyValueStore.Flush"/> call. Stores that buffer writes can use that single durability boundary;
    /// the interface does not guarantee an atomic multi-key transaction. This class is an intended seam for the
    /// upcoming iOS UIKit settings screen and is currently referenced only by tests; it is not dead code.
    /// </remarks>
    public sealed class PreferencesStateWriter
    {
        private readonly IKeyValueStore store;

        /// <summary>Creates a writer over a platform-specific key-value store.</summary>
        /// <param name="store">The non-null destination store.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
        public PreferencesStateWriter(IKeyValueStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Saves every value restored by <see cref="PreferencesStateRestorer"/> and commits the batch once.
        /// </summary>
        /// <remarks>
        /// Search history is serialized before the first write. A serialization failure therefore leaves the store
        /// unchanged. Individual key writes follow the consistency guarantees of the supplied store.
        /// </remarks>
        public void SaveAll()
        {
            string searchHistory = SerializationHelper.SaveStringListToString(PreferencesState.SearchHistory);

            SaveAccount();
            SaveAppearance();
            SaveTransfers();
            SaveSpeedLimits();
            SaveSearch(searchHistory);
            SaveSocial();
            SaveSharingAndDiagnostics();
            SaveListener();
            store.Flush();
        }

        private void SaveAccount()
        {
            store.PutBoolean(KeyConsts.M_CurrentlyLoggedIn, PreferencesState.CurrentlyLoggedIn);
            store.PutString(KeyConsts.M_Username, PreferencesState.Username ?? string.Empty);
            store.PutString(KeyConsts.M_Password, PreferencesState.Password ?? string.Empty);
        }

        private void SaveAppearance()
        {
            store.PutInt(KeyConsts.M_DayNightMode, PreferencesState.DayNightMode);
            store.PutString(KeyConsts.M_Lanuage, PreferencesState.Language);
            store.PutBoolean(KeyConsts.M_LegacyLanguageMigrated, PreferencesState.LegacyLanguageMigrated);
            store.PutInt(KeyConsts.M_NightVariant, (int)PreferencesState.NightModeVariant);
            store.PutInt(KeyConsts.M_DayVariant, (int)PreferencesState.DayModeVariant);
            store.PutBoolean(KeyConsts.M_ShowSmartFilters, PreferencesState.ShowSmartFilters);
            store.PutInt(KeyConsts.M_SmartFilterStyle, (int)PreferencesState.SmartFilterStyle);
            store.PutBoolean(
                KeyConsts.M_SmartFilter_KeywordsEnabled,
                PreferencesState.SmartFilterOptions.KeywordsEnabled);
            store.PutInt(KeyConsts.M_SmartFilter_KeywordsOrder, PreferencesState.SmartFilterOptions.KeywordsOrder);
            store.PutBoolean(
                KeyConsts.M_SmartFilter_TypesEnabled,
                PreferencesState.SmartFilterOptions.FileTypesEnabled);
            store.PutInt(KeyConsts.M_SmartFilter_TypesOrder, PreferencesState.SmartFilterOptions.FileTypesOrder);
            store.PutBoolean(
                KeyConsts.M_SmartFilter_CountsEnabled,
                PreferencesState.SmartFilterOptions.NumFilesEnabled);
            store.PutInt(KeyConsts.M_SmartFilter_CountsOrder, PreferencesState.SmartFilterOptions.NumFilesOrder);
        }

        private void SaveTransfers()
        {
            store.PutBoolean(KeyConsts.M_AutoClearComplete, PreferencesState.AutoClearCompleteDownloads);
            store.PutBoolean(KeyConsts.M_AutoClearCompleteUploads, PreferencesState.AutoClearCompleteUploads);
            store.PutBoolean(KeyConsts.M_TransfersShowSizes, PreferencesState.TransferViewShowSizes);
            store.PutBoolean(KeyConsts.M_TransfersShowSpeed, PreferencesState.TransferViewShowSpeed);
            store.PutBoolean(KeyConsts.M_TransfersGroupByFolder, PreferencesState.TransferViewGroupByFolder);
            store.PutBoolean(KeyConsts.M_TransfersInUploadsMode, PreferencesState.TransferViewInUploadsMode);
            store.PutBoolean(
                KeyConsts.M_DisableToastNotifications,
                PreferencesState.DisableDownloadToastNotification);
            store.PutBoolean(KeyConsts.M_MemoryBackedDownload, PreferencesState.MemoryBackedDownload);
            store.PutBoolean(KeyConsts.M_NoSubfolderForSingle, PreferencesState.NoSubfolderForSingle);
            store.PutBoolean(
                KeyConsts.M_KeepScreenAwakeWhileTransferring,
                PreferencesState.KeepScreenAwakeWhileTransferring);
            store.PutBoolean(
                KeyConsts.M_AdditionalUsernameSubdirectories,
                PreferencesState.CreateUsernameSubfolders);
            store.PutBoolean(KeyConsts.M_NotifyFolderComplete, PreferencesState.NotifyOnFolderCompleted);
            store.PutBoolean(KeyConsts.M_AutoRetryBackOnline, PreferencesState.AutoRetryBackOnline);
        }

        private void SaveSpeedLimits()
        {
            store.PutBoolean(KeyConsts.M_UploadLimitEnabled, PreferencesState.SpeedLimitUploadOn);
            store.PutBoolean(KeyConsts.M_DownloadLimitEnabled, PreferencesState.SpeedLimitDownloadOn);
            store.PutBoolean(KeyConsts.M_UploadPerTransfer, PreferencesState.SpeedLimitUploadIsPerTransfer);
            store.PutBoolean(KeyConsts.M_DownloadPerTransfer, PreferencesState.SpeedLimitDownloadIsPerTransfer);
            store.PutInt(KeyConsts.M_UploadSpeedLimitBytes, PreferencesState.SpeedLimitUploadBytesSec);
            store.PutInt(KeyConsts.M_DownloadSpeedLimitBytes, PreferencesState.SpeedLimitDownloadBytesSec);
        }

        private void SaveSearch(string searchHistory)
        {
            store.PutInt(KeyConsts.M_NumberSearchResults, PreferencesState.NumberSearchResults);
            store.PutBoolean(KeyConsts.M_RememberSearchHistory, PreferencesState.RememberSearchHistory);
            store.PutBoolean(KeyConsts.M_RememberUserHistory, PreferencesState.ShowRecentUsers);
            store.PutBoolean(KeyConsts.M_OnlyFreeUploadSlots, PreferencesState.FreeUploadSlotsOnly);
            store.PutBoolean(KeyConsts.M_HideLockedBrowse, PreferencesState.HideLockedResultsInBrowse);
            store.PutBoolean(KeyConsts.M_HideLockedSearch, PreferencesState.HideLockedResultsInSearch);
            store.PutBoolean(KeyConsts.M_FilterSticky, PreferencesState.FilterSticky);
            store.PutString(KeyConsts.M_FilterStickyString, PreferencesState.FilterStickyString);
            store.PutInt(KeyConsts.M_SearchResultStyleV2, (int)PreferencesState.SearchResultStyle);
            store.PutBoolean(KeyConsts.M_ExpandAllResults, PreferencesState.ExpandAllResults);
            store.PutInt(
                KeyConsts.M_DefaultSearchResultSortAlgorithm,
                (int)PreferencesState.DefaultSearchResultSortAlgorithm);
            store.PutString(KeyConsts.M_SearchHistory, searchHistory);
            store.PutInt(KeyConsts.M_FilterFormat, (int)PreferencesState.FilterFormat);
            store.PutInt(KeyConsts.M_FilterMinBitrateKbs, PreferencesState.FilterMinBitrateKbs);
        }

        private void SaveSocial()
        {
            store.PutBoolean(
                KeyConsts.M_AllowPrivateRooomInvitations,
                PreferencesState.AllowPrivateRoomInvitations);
            store.PutBoolean(KeyConsts.M_ServiceOnStartup, PreferencesState.StartServiceOnStartup);
            store.PutBoolean(KeyConsts.M_AutoSetAwayOnInactivity, PreferencesState.AutoAwayOnInactivity);
            store.PutString(KeyConsts.M_UserInfoBio, PreferencesState.UserInfoBio);
            store.PutString(KeyConsts.M_UserInfoPicture, PreferencesState.UserInfoPictureName);
            store.PutBoolean(KeyConsts.M_ShowStatusesView, PreferencesState.ShowStatusesView);
            store.PutBoolean(KeyConsts.M_ShowTickerView, PreferencesState.ShowTickerView);
            store.PutInt(KeyConsts.M_RoomUserListSortOrder, (int)PreferencesState.SortChatroomUsersBy);
            store.PutBoolean(KeyConsts.M_RoomUserListShowFriendsAtTop, PreferencesState.PutFriendsOnTop);
            store.PutInt(KeyConsts.M_UserListSortOrder, (int)PreferencesState.UserListSortOrder);
        }

        private void SaveSharingAndDiagnostics()
        {
            store.PutBoolean(KeyConsts.M_SharingOn, PreferencesState.SharingOn);
            store.PutInt(KeyConsts.M_UploadSpeed, PreferencesState.UploadSpeed);
            store.PutBoolean(KeyConsts.M_AllowUploadsOnMetered, PreferencesState.AllowUploadsOnMetered);
            store.PutBoolean(KeyConsts.M_RequireVpnForSharing, PreferencesState.RequireVpnForSharing);
            store.PutBoolean(KeyConsts.M_LOG_DIAGNOSTICS, PreferencesState.LogDiagnostics);
            store.PutBoolean(
                KeyConsts.M_LimitSimultaneousDownloads,
                PreferencesState.LimitSimultaneousDownloads);
            store.PutInt(KeyConsts.M_MaxSimultaneousLimit, PreferencesState.MaxSimultaneousLimit);
        }

        private void SaveListener()
        {
            store.PutBoolean(KeyConsts.M_ListenerEnabled, PreferencesState.ListenerEnabled);
            store.PutInt(KeyConsts.M_ListenerPort, PreferencesState.ListenerPort);
            store.PutBoolean(KeyConsts.M_ListenerUPnpEnabled, PreferencesState.ListenerUPnpEnabled);
        }
    }
}
