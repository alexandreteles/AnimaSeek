using Common;
using Common.Messages;
using NUnit.Framework;
using Seeker;
using Seeker.Services;
using System.Collections.Generic;

namespace UnitTestCommon
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PreferencesStateWriterTests
    {
        [TearDown]
        public void TearDown() => new PreferencesStateRestorer(new TestKeyValueStore()).RestoreAll();

        [Test]
        public void SaveAll_RoundTripsEveryPortableRestoredField_AndFlushesOnce()
        {
            SetDistinctState();
            object?[] expected = CaptureState();
            var store = new TestKeyValueStore();

            new PreferencesStateWriter(store).SaveAll();
            new PreferencesStateRestorer(new TestKeyValueStore()).RestoreAll();
            new PreferencesStateRestorer(store).RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(CaptureState(), Is.EqualTo(expected));
                Assert.That(store.FlushCount, Is.EqualTo(1));
                Assert.That(store.Keys, Does.Contain(KeyConsts.M_SearchResultStyleV2));
                Assert.That(store.Keys, Does.Not.Contain(KeyConsts.M_SearchResultStyle));
                Assert.That(
                    store.GetString(KeyConsts.M_SearchHistory),
                    Is.EqualTo("[\"first\",\"second\"]"));
            });
        }

        [Test]
        public void Constructor_RejectsNullStore()
        {
            Assert.Throws<System.ArgumentNullException>(() => new PreferencesStateWriter(null!));
        }

        [Test]
        public void SaveAll_SerializationFailureOccursBeforeAnyKeyIsWritten()
        {
            var store = new TestKeyValueStore();
            PreferencesState.SearchHistory = null!;

            Assert.Throws<System.ArgumentNullException>(() => new PreferencesStateWriter(store).SaveAll());
            Assert.Multiple(() =>
            {
                Assert.That(store.Keys, Is.Empty);
                Assert.That(store.FlushCount, Is.Zero);
            });
        }

        private static void SetDistinctState()
        {
            PreferencesState.CurrentlyLoggedIn = true;
            PreferencesState.SetCredentials("writer-user", "writer-password");
            PreferencesState.DayNightMode = 2;
            PreferencesState.Language = PreferencesState.FieldLangPtBr;
            PreferencesState.LegacyLanguageMigrated = true;
            PreferencesState.NightModeVariant = NightThemeType.AmoledGrey;
            PreferencesState.DayModeVariant = DayThemeType.Grey;
            PreferencesState.ShowSmartFilters = false;
            PreferencesState.SmartFilterStyle = SmartFilterStyle.Grouped;
            PreferencesState.SmartFilterOptions = new PreferencesState.SmartFilterState
            {
                KeywordsEnabled = false,
                KeywordsOrder = 2,
                FileTypesEnabled = true,
                FileTypesOrder = 0,
                NumFilesEnabled = false,
                NumFilesOrder = 1,
            };

            PreferencesState.AutoClearCompleteDownloads = true;
            PreferencesState.AutoClearCompleteUploads = true;
            PreferencesState.TransferViewShowSizes = false;
            PreferencesState.TransferViewShowSpeed = false;
            PreferencesState.TransferViewGroupByFolder = true;
            PreferencesState.TransferViewInUploadsMode = true;
            PreferencesState.DisableDownloadToastNotification = false;
            PreferencesState.MemoryBackedDownload = true;
            PreferencesState.NoSubfolderForSingle = true;
            PreferencesState.KeepScreenAwakeWhileTransferring = true;
            PreferencesState.CreateUsernameSubfolders = true;
            PreferencesState.NotifyOnFolderCompleted = false;
            PreferencesState.AutoRetryBackOnline = false;

            PreferencesState.SpeedLimitUploadOn = true;
            PreferencesState.SpeedLimitDownloadOn = true;
            PreferencesState.SpeedLimitUploadIsPerTransfer = false;
            PreferencesState.SpeedLimitDownloadIsPerTransfer = false;
            PreferencesState.SpeedLimitUploadBytesSec = 12_345;
            PreferencesState.SpeedLimitDownloadBytesSec = 54_321;

            PreferencesState.NumberSearchResults = 654;
            PreferencesState.RememberSearchHistory = false;
            PreferencesState.ShowRecentUsers = false;
            PreferencesState.FreeUploadSlotsOnly = false;
            PreferencesState.HideLockedResultsInBrowse = false;
            PreferencesState.HideLockedResultsInSearch = false;
            PreferencesState.FilterSticky = true;
            PreferencesState.FilterStickyString = "lossless -live";
            PreferencesState.SearchResultStyle = SearchResultStyleEnum.ModernTopExpandable;
            PreferencesState.ExpandAllResults = true;
            PreferencesState.DefaultSearchResultSortAlgorithm = SearchResultSorting.BitRate;
            PreferencesState.SearchHistory = new List<string> { "first", "second" };
            PreferencesState.FilterFormat = FormatFilterType.Lossless;
            PreferencesState.FilterMinBitrateKbs = 320;

            PreferencesState.AllowPrivateRoomInvitations = true;
            PreferencesState.StartServiceOnStartup = false;
            PreferencesState.AutoAwayOnInactivity = true;
            PreferencesState.UserInfoBio = "portable bio";
            PreferencesState.UserInfoPictureName = "avatar.png";
            PreferencesState.ShowStatusesView = false;
            PreferencesState.ShowTickerView = true;
            PreferencesState.SortChatroomUsersBy = SortOrderChatroomUsers.OnlineStatus;
            PreferencesState.PutFriendsOnTop = true;
            PreferencesState.UserListSortOrder = SortOrder.OnlineStatus;

            PreferencesState.SharingOn = true;
            PreferencesState.UploadSpeed = 88_888;
            PreferencesState.AllowUploadsOnMetered = false;
            PreferencesState.RequireVpnForSharing = true;
            PreferencesState.LogDiagnostics = true;
            PreferencesState.LimitSimultaneousDownloads = true;
            PreferencesState.MaxSimultaneousLimit = 9;

            PreferencesState.ListenerEnabled = false;
            PreferencesState.ListenerPort = 45_678;
            PreferencesState.ListenerUPnpEnabled = false;
        }

        private static object?[] CaptureState() =>
            new object?[]
            {
                PreferencesState.CurrentlyLoggedIn,
                PreferencesState.Username,
                PreferencesState.Password,
                PreferencesState.DayNightMode,
                PreferencesState.Language,
                PreferencesState.LegacyLanguageMigrated,
                PreferencesState.NightModeVariant,
                PreferencesState.DayModeVariant,
                PreferencesState.ShowSmartFilters,
                PreferencesState.SmartFilterStyle,
                PreferencesState.SmartFilterOptions.KeywordsEnabled,
                PreferencesState.SmartFilterOptions.KeywordsOrder,
                PreferencesState.SmartFilterOptions.FileTypesEnabled,
                PreferencesState.SmartFilterOptions.FileTypesOrder,
                PreferencesState.SmartFilterOptions.NumFilesEnabled,
                PreferencesState.SmartFilterOptions.NumFilesOrder,
                PreferencesState.AutoClearCompleteDownloads,
                PreferencesState.AutoClearCompleteUploads,
                PreferencesState.TransferViewShowSizes,
                PreferencesState.TransferViewShowSpeed,
                PreferencesState.TransferViewGroupByFolder,
                PreferencesState.TransferViewInUploadsMode,
                PreferencesState.DisableDownloadToastNotification,
                PreferencesState.MemoryBackedDownload,
                PreferencesState.NoSubfolderForSingle,
                PreferencesState.KeepScreenAwakeWhileTransferring,
                PreferencesState.CreateUsernameSubfolders,
                PreferencesState.NotifyOnFolderCompleted,
                PreferencesState.AutoRetryBackOnline,
                PreferencesState.SpeedLimitUploadOn,
                PreferencesState.SpeedLimitDownloadOn,
                PreferencesState.SpeedLimitUploadIsPerTransfer,
                PreferencesState.SpeedLimitDownloadIsPerTransfer,
                PreferencesState.SpeedLimitUploadBytesSec,
                PreferencesState.SpeedLimitDownloadBytesSec,
                PreferencesState.NumberSearchResults,
                PreferencesState.RememberSearchHistory,
                PreferencesState.ShowRecentUsers,
                PreferencesState.FreeUploadSlotsOnly,
                PreferencesState.HideLockedResultsInBrowse,
                PreferencesState.HideLockedResultsInSearch,
                PreferencesState.FilterSticky,
                PreferencesState.FilterStickyString,
                PreferencesState.SearchResultStyle,
                PreferencesState.ExpandAllResults,
                PreferencesState.DefaultSearchResultSortAlgorithm,
                string.Join("\u001f", PreferencesState.SearchHistory),
                PreferencesState.FilterFormat,
                PreferencesState.FilterMinBitrateKbs,
                PreferencesState.AllowPrivateRoomInvitations,
                PreferencesState.StartServiceOnStartup,
                PreferencesState.AutoAwayOnInactivity,
                PreferencesState.UserInfoBio,
                PreferencesState.UserInfoPictureName,
                PreferencesState.ShowStatusesView,
                PreferencesState.ShowTickerView,
                PreferencesState.SortChatroomUsersBy,
                PreferencesState.PutFriendsOnTop,
                PreferencesState.UserListSortOrder,
                PreferencesState.SharingOn,
                PreferencesState.UploadSpeed,
                PreferencesState.AllowUploadsOnMetered,
                PreferencesState.RequireVpnForSharing,
                PreferencesState.LogDiagnostics,
                PreferencesState.LimitSimultaneousDownloads,
                PreferencesState.MaxSimultaneousLimit,
                PreferencesState.ListenerEnabled,
                PreferencesState.ListenerPort,
                PreferencesState.ListenerUPnpEnabled,
            };
    }
}
