using Common;
using Common.Messages;
using NUnit.Framework;
using Seeker;
using Seeker.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTestCommon
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PreferencesStateRestorerTests
    {
        [SetUp]
        public void SetUp() => ResetGlobalState();

        [TearDown]
        public void TearDown() => ResetGlobalState();

        [Test]
        public void RestoreAll_RestoresEveryPortablePreferenceGroup()
        {
            var store = new InMemoryKeyValueStore();
            store.PutBoolean(KeyConsts.M_CurrentlyLoggedIn, true);
            store.PutString(KeyConsts.M_Username, "alice");
            store.PutString(KeyConsts.M_Password, "secret");

            store.PutInt(KeyConsts.M_DayNightMode, 2);
            store.PutString(KeyConsts.M_Lanuage, PreferencesState.FieldLangEn);
            store.PutBoolean(KeyConsts.M_LegacyLanguageMigrated, true);
            store.PutInt(KeyConsts.M_NightVariant, (int)NightThemeType.Red);
            store.PutInt(KeyConsts.M_DayVariant, (int)DayThemeType.Blue);
            store.PutBoolean(KeyConsts.M_ShowSmartFilters, false);
            store.PutInt(KeyConsts.M_SmartFilterStyle, (int)SmartFilterStyle.Grouped);
            store.PutBoolean(KeyConsts.M_SmartFilter_KeywordsEnabled, false);
            store.PutInt(KeyConsts.M_SmartFilter_KeywordsOrder, 2);
            store.PutBoolean(KeyConsts.M_SmartFilter_TypesEnabled, false);
            store.PutInt(KeyConsts.M_SmartFilter_TypesOrder, 0);
            store.PutBoolean(KeyConsts.M_SmartFilter_CountsEnabled, true);
            store.PutInt(KeyConsts.M_SmartFilter_CountsOrder, 1);

            store.PutBoolean(KeyConsts.M_AutoClearComplete, true);
            store.PutBoolean(KeyConsts.M_AutoClearCompleteUploads, true);
            store.PutBoolean(KeyConsts.M_TransfersShowSizes, false);
            store.PutBoolean(KeyConsts.M_TransfersShowSpeed, false);
            store.PutBoolean(KeyConsts.M_TransfersGroupByFolder, true);
            store.PutBoolean(KeyConsts.M_TransfersInUploadsMode, true);
            store.PutBoolean(KeyConsts.M_DisableToastNotifications, false);
            store.PutBoolean(KeyConsts.M_MemoryBackedDownload, true);
            store.PutBoolean(KeyConsts.M_NoSubfolderForSingle, true);
            store.PutBoolean(KeyConsts.M_KeepScreenAwakeWhileTransferring, true);
            store.PutBoolean(KeyConsts.M_AdditionalUsernameSubdirectories, true);
            store.PutBoolean(KeyConsts.M_NotifyFolderComplete, false);
            store.PutBoolean(KeyConsts.M_AutoRetryBackOnline, false);

            store.PutBoolean(KeyConsts.M_UploadLimitEnabled, true);
            store.PutBoolean(KeyConsts.M_DownloadLimitEnabled, true);
            store.PutBoolean(KeyConsts.M_UploadPerTransfer, false);
            store.PutBoolean(KeyConsts.M_DownloadPerTransfer, false);
            store.PutInt(KeyConsts.M_UploadSpeedLimitBytes, 12_345);
            store.PutInt(KeyConsts.M_DownloadSpeedLimitBytes, 54_321);

            store.PutInt(KeyConsts.M_NumberSearchResults, 321);
            store.PutBoolean(KeyConsts.M_RememberSearchHistory, false);
            store.PutBoolean(KeyConsts.M_RememberUserHistory, false);
            store.PutBoolean(KeyConsts.M_OnlyFreeUploadSlots, false);
            store.PutBoolean(KeyConsts.M_HideLockedBrowse, false);
            store.PutBoolean(KeyConsts.M_HideLockedSearch, false);
            store.PutBoolean(KeyConsts.M_FilterSticky, true);
            store.PutString(KeyConsts.M_FilterStickyString, "flac -live");
            store.PutInt(KeyConsts.M_SearchResultStyleV2, (int)SearchResultStyleEnum.ModernTopExpandable);
            store.PutBoolean(KeyConsts.M_ExpandAllResults, true);
            store.PutInt(KeyConsts.M_DefaultSearchResultSortAlgorithm, (int)SearchResultSorting.BitRate);
            store.PutString(
                KeyConsts.M_SearchHistory,
                "<ArrayOfString><string>first search</string><string>second &amp; search</string></ArrayOfString>");
            store.PutInt(KeyConsts.M_FilterFormat, (int)FormatFilterType.Lossless);
            store.PutInt(KeyConsts.M_FilterMinBitrateKbs, 192);

            store.PutBoolean(KeyConsts.M_AllowPrivateRooomInvitations, true);
            store.PutBoolean(KeyConsts.M_ServiceOnStartup, false);
            store.PutBoolean(KeyConsts.M_AutoSetAwayOnInactivity, true);
            store.PutString(KeyConsts.M_UserInfoBio, "collector");
            store.PutString(KeyConsts.M_UserInfoPicture, "avatar.jpg");
            store.PutBoolean(KeyConsts.M_ShowStatusesView, false);
            store.PutBoolean(KeyConsts.M_ShowTickerView, true);
            store.PutInt(KeyConsts.M_RoomUserListSortOrder, (int)SortOrderChatroomUsers.OnlineStatus);
            store.PutBoolean(KeyConsts.M_RoomUserListShowFriendsAtTop, true);
            store.PutInt(KeyConsts.M_UserListSortOrder, (int)SortOrder.Alphabetical);

            store.PutBoolean(KeyConsts.M_SharingOn, true);
            store.PutInt(KeyConsts.M_UploadSpeed, 98_765);
            store.PutBoolean(KeyConsts.M_AllowUploadsOnMetered, false);
            store.PutBoolean(KeyConsts.M_RequireVpnForSharing, true);
            store.PutBoolean(KeyConsts.M_LOG_DIAGNOSTICS, true);
            store.PutBoolean(KeyConsts.M_LimitSimultaneousDownloads, true);
            store.PutInt(KeyConsts.M_MaxSimultaneousLimit, 7);

            store.PutBoolean(KeyConsts.M_ListenerEnabled, false);
            store.PutInt(KeyConsts.M_ListenerPort, 44_444);
            store.PutBoolean(KeyConsts.M_ListenerUPnpEnabled, false);

            new PreferencesStateRestorer(store).RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(PreferencesState.CurrentlyLoggedIn, Is.True);
                Assert.That(PreferencesState.Username, Is.EqualTo("alice"));
                Assert.That(PreferencesState.Password, Is.EqualTo("secret"));
                Assert.That(PreferencesState.DayNightMode, Is.EqualTo(2));
                Assert.That(PreferencesState.Language, Is.EqualTo(PreferencesState.FieldLangEn));
                Assert.That(PreferencesState.LegacyLanguageMigrated, Is.True);
                Assert.That(PreferencesState.NightModeVariant, Is.EqualTo(NightThemeType.Red));
                Assert.That(PreferencesState.DayModeVariant, Is.EqualTo(DayThemeType.Blue));
                Assert.That(PreferencesState.ShowSmartFilters, Is.False);
                Assert.That(PreferencesState.SmartFilterStyle, Is.EqualTo(SmartFilterStyle.Grouped));
                Assert.That(PreferencesState.SmartFilterOptions.KeywordsEnabled, Is.False);
                Assert.That(PreferencesState.SmartFilterOptions.KeywordsOrder, Is.EqualTo(2));
                Assert.That(PreferencesState.SmartFilterOptions.FileTypesEnabled, Is.False);
                Assert.That(PreferencesState.SmartFilterOptions.FileTypesOrder, Is.Zero);
                Assert.That(PreferencesState.SmartFilterOptions.NumFilesEnabled, Is.True);
                Assert.That(PreferencesState.SmartFilterOptions.NumFilesOrder, Is.EqualTo(1));

                Assert.That(PreferencesState.AutoClearCompleteDownloads, Is.True);
                Assert.That(PreferencesState.AutoClearCompleteUploads, Is.True);
                Assert.That(PreferencesState.TransferViewShowSizes, Is.False);
                Assert.That(PreferencesState.TransferViewShowSpeed, Is.False);
                Assert.That(PreferencesState.TransferViewGroupByFolder, Is.True);
                Assert.That(PreferencesState.TransferViewInUploadsMode, Is.True);
                Assert.That(PreferencesState.DisableDownloadToastNotification, Is.False);
                Assert.That(PreferencesState.MemoryBackedDownload, Is.True);
                Assert.That(PreferencesState.NoSubfolderForSingle, Is.True);
                Assert.That(PreferencesState.KeepScreenAwakeWhileTransferring, Is.True);
                Assert.That(PreferencesState.CreateUsernameSubfolders, Is.True);
                Assert.That(PreferencesState.NotifyOnFolderCompleted, Is.False);
                Assert.That(PreferencesState.AutoRetryBackOnline, Is.False);

                Assert.That(PreferencesState.SpeedLimitUploadOn, Is.True);
                Assert.That(PreferencesState.SpeedLimitDownloadOn, Is.True);
                Assert.That(PreferencesState.SpeedLimitUploadIsPerTransfer, Is.False);
                Assert.That(PreferencesState.SpeedLimitDownloadIsPerTransfer, Is.False);
                Assert.That(PreferencesState.SpeedLimitUploadBytesSec, Is.EqualTo(12_345));
                Assert.That(PreferencesState.SpeedLimitDownloadBytesSec, Is.EqualTo(54_321));

                Assert.That(PreferencesState.NumberSearchResults, Is.EqualTo(321));
                Assert.That(PreferencesState.RememberSearchHistory, Is.False);
                Assert.That(PreferencesState.ShowRecentUsers, Is.False);
                Assert.That(PreferencesState.FreeUploadSlotsOnly, Is.False);
                Assert.That(PreferencesState.HideLockedResultsInBrowse, Is.False);
                Assert.That(PreferencesState.HideLockedResultsInSearch, Is.False);
                Assert.That(PreferencesState.FilterSticky, Is.True);
                Assert.That(PreferencesState.FilterStickyString, Is.EqualTo("flac -live"));
                Assert.That(PreferencesState.SearchResultStyle, Is.EqualTo(SearchResultStyleEnum.ModernTopExpandable));
                Assert.That(PreferencesState.ExpandAllResults, Is.True);
                Assert.That(PreferencesState.DefaultSearchResultSortAlgorithm, Is.EqualTo(SearchResultSorting.BitRate));
                Assert.That(PreferencesState.SearchHistory, Is.EqualTo(new[] { "first search", "second & search" }));
                Assert.That(PreferencesState.FilterFormat, Is.EqualTo(FormatFilterType.Lossless));
                Assert.That(PreferencesState.FilterMinBitrateKbs, Is.EqualTo(192));

                Assert.That(PreferencesState.AllowPrivateRoomInvitations, Is.True);
                Assert.That(PreferencesState.StartServiceOnStartup, Is.False);
                Assert.That(PreferencesState.AutoAwayOnInactivity, Is.True);
                Assert.That(PreferencesState.UserInfoBio, Is.EqualTo("collector"));
                Assert.That(PreferencesState.UserInfoPictureName, Is.EqualTo("avatar.jpg"));
                Assert.That(PreferencesState.ShowStatusesView, Is.False);
                Assert.That(PreferencesState.ShowTickerView, Is.True);
                Assert.That(PreferencesState.SortChatroomUsersBy, Is.EqualTo(SortOrderChatroomUsers.OnlineStatus));
                Assert.That(PreferencesState.PutFriendsOnTop, Is.True);
                Assert.That(PreferencesState.UserListSortOrder, Is.EqualTo(SortOrder.Alphabetical));

                Assert.That(PreferencesState.SharingOn, Is.True);
                Assert.That(PreferencesState.UploadSpeed, Is.EqualTo(98_765));
                Assert.That(PreferencesState.AllowUploadsOnMetered, Is.False);
                Assert.That(PreferencesState.RequireVpnForSharing, Is.True);
                Assert.That(PreferencesState.LogDiagnostics, Is.True);
                Assert.That(PreferencesState.LimitSimultaneousDownloads, Is.True);
                Assert.That(PreferencesState.MaxSimultaneousLimit, Is.EqualTo(7));

                Assert.That(PreferencesState.ListenerEnabled, Is.False);
                Assert.That(PreferencesState.ListenerPort, Is.EqualTo(44_444));
                Assert.That(PreferencesState.ListenerUPnpEnabled, Is.False);
                Assert.That(store.FlushCount, Is.Zero);
            });
        }

        [Test]
        public void RestoreAll_UsesCompatibleDefaultsWithoutApplyingAndroidSafState()
        {
            PreferencesState.SaveDataDirectoryUri = "content://downloads";
            PreferencesState.SaveDataDirectoryUriIsFromTree = false;
            PreferencesState.ManualIncompleteDataDirectoryUri = "content://incomplete";
            PreferencesState.ManualIncompleteDataDirectoryUriIsFromTree = false;
            PreferencesState.OverrideDefaultIncompleteLocations = true;
            PreferencesState.CreateCompleteAndIncompleteFolders = false;

            new PreferencesStateRestorer(new InMemoryKeyValueStore()).RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(PreferencesState.CurrentlyLoggedIn, Is.False);
                Assert.That(PreferencesState.Username, Is.Empty);
                Assert.That(PreferencesState.Password, Is.Empty);
                Assert.That(PreferencesState.NumberSearchResults, Is.EqualTo(500));
                Assert.That(PreferencesState.TransferViewShowSizes, Is.True);
                Assert.That(PreferencesState.TransferViewShowSpeed, Is.True);
                Assert.That(PreferencesState.ListenerPort, Is.EqualTo(33_939));
                Assert.That(PreferencesState.SmartFilterOptions.KeywordsEnabled, Is.True);
                Assert.That(PreferencesState.SmartFilterOptions.FileTypesOrder, Is.EqualTo(1));
                Assert.That(PreferencesState.SmartFilterOptions.NumFilesOrder, Is.EqualTo(2));

                Assert.That(PreferencesState.SaveDataDirectoryUri, Is.EqualTo("content://downloads"));
                Assert.That(PreferencesState.SaveDataDirectoryUriIsFromTree, Is.False);
                Assert.That(PreferencesState.ManualIncompleteDataDirectoryUri, Is.EqualTo("content://incomplete"));
                Assert.That(PreferencesState.ManualIncompleteDataDirectoryUriIsFromTree, Is.False);
                Assert.That(PreferencesState.OverrideDefaultIncompleteLocations, Is.True);
                Assert.That(PreferencesState.CreateCompleteAndIncompleteFolders, Is.False);
            });
        }

        [Test]
        public void RestoreAll_LegacyExpandedStyleMigratesStyleButNotExpansionWhenKeyAbsent()
        {
            var store = new InMemoryKeyValueStore();
            store.PutInt(KeyConsts.M_SearchResultStyle, 3);

            new PreferencesStateRestorer(store).RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(PreferencesState.SearchResultStyle, Is.EqualTo(SearchResultStyleEnum.SimpleBottomExpandable));
                // Parity with the Android restore path: its legacy-style migration side effect was always
                // overwritten by an unconditional read with a false default, so an absent key restores false.
                Assert.That(PreferencesState.ExpandAllResults, Is.False);
            });
        }

        [Test]
        public void RestoreAll_LegacyExpandedStyleKeepsExplicitExpandAllValue()
        {
            var store = new InMemoryKeyValueStore();
            store.PutInt(KeyConsts.M_SearchResultStyle, 3);
            store.PutBoolean(KeyConsts.M_ExpandAllResults, true);

            new PreferencesStateRestorer(store).RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(PreferencesState.SearchResultStyle, Is.EqualTo(SearchResultStyleEnum.SimpleBottomExpandable));
                Assert.That(PreferencesState.ExpandAllResults, Is.True);
            });
        }

        [Test]
        public void RestoreAll_DiscardsMalformedLegacySearchHistory()
        {
            var store = new InMemoryKeyValueStore();
            store.PutString(KeyConsts.M_SearchHistory, "<ArrayOfString><string>unfinished");

            Assert.DoesNotThrow(() => new PreferencesStateRestorer(store).RestoreAll());
            Assert.That(PreferencesState.SearchHistory, Is.Empty);
        }

        [Test]
        public void Constructor_RejectsNullStore()
        {
            Assert.Throws<ArgumentNullException>(() => new PreferencesStateRestorer(null!));
        }

        private static void ResetGlobalState()
        {
            new PreferencesStateRestorer(new InMemoryKeyValueStore()).RestoreAll();
            PreferencesState.SaveDataDirectoryUri = null!;
            PreferencesState.SaveDataDirectoryUriIsFromTree = true;
            PreferencesState.ManualIncompleteDataDirectoryUri = null!;
            PreferencesState.ManualIncompleteDataDirectoryUriIsFromTree = true;
            PreferencesState.OverrideDefaultIncompleteLocations = false;
            PreferencesState.CreateCompleteAndIncompleteFolders = true;
        }

        private sealed class InMemoryKeyValueStore : IKeyValueStore
        {
            private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

            public int FlushCount { get; private set; }

            public bool GetBoolean(string key, bool defaultValue) => GetValue(key, defaultValue);

            public int GetInt(string key, int defaultValue) => GetValue(key, defaultValue);

            public long GetLong(string key, long defaultValue) => GetValue(key, defaultValue);

            public string? GetString(string key, string? defaultValue = null) => GetValue(key, defaultValue);

            public IReadOnlyCollection<string>? GetStringSet(
                string key,
                IReadOnlyCollection<string>? defaultValue = null) =>
                values.TryGetValue(key, out object? value)
                    ? ((IReadOnlyCollection<string>)value).ToArray()
                    : defaultValue;

            public void PutBoolean(string key, bool value) => values[key] = value;

            public void PutInt(string key, int value) => values[key] = value;

            public void PutLong(string key, long value) => values[key] = value;

            public void PutString(string key, string? value) => PutNullable(key, value);

            public void PutStringSet(string key, IReadOnlyCollection<string>? value) =>
                PutNullable(key, value?.Distinct(StringComparer.Ordinal).ToArray());

            public void Flush() => FlushCount++;

            private T GetValue<T>(string key, T defaultValue) =>
                values.TryGetValue(key, out object? value) ? (T)value : defaultValue;

            private void PutNullable(string key, object? value)
            {
                if (value is null)
                {
                    values.Remove(key);
                }
                else
                {
                    values[key] = value;
                }
            }
        }
    }
}
