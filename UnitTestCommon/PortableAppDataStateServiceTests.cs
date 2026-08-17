using Common;
using NUnit.Framework;
using Seeker;
using Seeker.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UnitTestCommon
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PortableAppDataStateServiceTests
    {
        [TearDown]
        public void TearDown()
        {
            UserMetadataService.UserNotes = new ConcurrentDictionary<string, string>();
            UserMetadataService.UserOnlineAlerts = new ConcurrentDictionary<string, byte>();
            UserMetadataService.RecentUsersManager = new RecentUserManager();
        }

        [Test]
        public void PersistAll_AndRestoreAll_RoundTripEveryCanonicalPayloadWithOneFlush()
        {
            var state = CreateState();
            var store = new TestKeyValueStore();
            var service = new PortableAppDataStateService(store);

            service.PersistAll(state);
            PortableAppDataRestoreResult result = service.RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(store.FlushCount, Is.EqualTo(1));
                Assert.That(result.Failures, Is.Empty);
                Assert.That(result.State.UserNotes["alice"], Is.EqualTo("collector"));
                Assert.That(result.State.UserOnlineAlerts.Keys, Is.EquivalentTo(new[] { "bob" }));
                Assert.That(result.State.RecentUsers, Is.EqualTo(new[] { "charlie", "bob" }));
                Assert.That(
                    result.State.PrivateMessageRoots["local"]["alice"].Single().MessageText,
                    Is.EqualTo("hello"));
                Assert.That(result.State.LastReadMessageCounts["local"]["alice"], Is.EqualTo(1));
                Assert.That(result.State.AutoJoinRoomNames["local"], Is.EqualTo(new[] { "music" }));
                Assert.That(result.State.NotifyRoomNames["local"], Is.EqualTo(new[] { "vinyl" }));
                Assert.That(result.State.SearchHeaders[-1].LastSearchTerm, Is.EqualTo("rare album"));
                Assert.That(result.State.SearchHeaders[-1].UnseenCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void RestoreAll_IsolatesEveryDamagedPayload()
        {
            var store = new TestKeyValueStore();
            string[] keys =
            {
                KeyConsts.M_UserNotes,
                KeyConsts.M_UserOnlineAlerts,
                KeyConsts.M_RecentUsersList,
                KeyConsts.M_Messages,
                KeyConsts.M_LastReadMessageCounts,
                KeyConsts.M_AutoJoinRooms,
                KeyConsts.M_chatroomsToNotify,
                KeyConsts.M_SearchTabsState_Headers,
            };
            foreach (string key in keys)
            {
                store.PutString(key, "not-a-valid-payload");
            }

            PortableAppDataRestoreResult result = new PortableAppDataStateService(store).RestoreAll();

            Assert.Multiple(() =>
            {
                Assert.That(result.Failures.Select(failure => failure.Key), Is.EquivalentTo(keys));
                Assert.That(result.State.UserNotes, Is.Empty);
                Assert.That(result.State.UserOnlineAlerts, Is.Empty);
                Assert.That(result.State.RecentUsers, Is.Empty);
                Assert.That(result.State.PrivateMessageRoots, Is.Empty);
                Assert.That(result.State.LastReadMessageCounts, Is.Empty);
                Assert.That(result.State.AutoJoinRoomNames, Is.Empty);
                Assert.That(result.State.NotifyRoomNames, Is.Empty);
                Assert.That(result.State.SearchHeaders, Is.Empty);
            });
        }

        [Test]
        public void ReloadUserMetadata_RefreshesLiveStateAndInstallsPersistenceBackedRecentUsers()
        {
            var store = new TestKeyValueStore();
            store.PutString(
                KeyConsts.M_UserNotes,
                SerializationHelper.SaveUserNotesToString(
                    new ConcurrentDictionary<string, string>(new[]
                    {
                        new KeyValuePair<string, string>("imported", "note"),
                    })));
            store.PutString(
                KeyConsts.M_UserOnlineAlerts,
                SerializationHelper.SaveUserOnlineAlertsToString(
                    new ConcurrentDictionary<string, byte>(new[]
                    {
                        new KeyValuePair<string, byte>("online", 0),
                    })));
            store.PutString(KeyConsts.M_RecentUsersList, SerializationHelper.SaveStringListToString(new[] { "first" }));
            var service = new PortableAppDataStateService(store);

            IReadOnlyCollection<AppDataRestoreFailure> failures = service.ReloadUserMetadata();
            UserMetadataService.RecentUsersManager.AddUserToTop("second", andSave: true);

            Assert.Multiple(() =>
            {
                Assert.That(failures, Is.Empty);
                Assert.That(UserMetadataService.UserNotes["imported"], Is.EqualTo("note"));
                Assert.That(UserMetadataService.UserOnlineAlerts.Keys, Is.EquivalentTo(new[] { "online" }));
                Assert.That(
                    UserMetadataService.RecentUsersManager.GetRecentUserList(),
                    Is.EqualTo(new[] { "second", "first" }));
                Assert.That(
                    SerializationHelper.RestoreStringListFromString(
                        store.GetString(KeyConsts.M_RecentUsersList, string.Empty)!),
                    Is.EqualTo(new[] { "second", "first" }));
                Assert.That(store.FlushCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void PersistAll_SerializationFailureOccursBeforeAnyKeyIsWritten()
        {
            var store = new TestKeyValueStore();
            PortableAppDataState state = CreateState();
            state.NotifyRoomNames = null!;

            Assert.Throws<ArgumentException>(() => new PortableAppDataStateService(store).PersistAll(state));
            Assert.Multiple(() =>
            {
                Assert.That(store.Keys, Is.Empty);
                Assert.That(store.FlushCount, Is.Zero);
            });
        }

        private static PortableAppDataState CreateState()
        {
            var message = new Message(
                "alice",
                7,
                false,
                DateTime.UtcNow,
                "hello",
                false,
                SentStatus.None,
                SpecialMessageCode.None,
                false);
            return new PortableAppDataState
            {
                UserNotes = new ConcurrentDictionary<string, string>(new[]
                {
                    new KeyValuePair<string, string>("alice", "collector"),
                }),
                UserOnlineAlerts = new ConcurrentDictionary<string, byte>(new[]
                {
                    new KeyValuePair<string, byte>("bob", 0),
                }),
                RecentUsers = new List<string> { "charlie", "bob" },
                PrivateMessageRoots = new ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>>(
                    new[]
                    {
                        new KeyValuePair<string, ConcurrentDictionary<string, List<Message>>>(
                            "local",
                            new ConcurrentDictionary<string, List<Message>>(new[]
                            {
                                new KeyValuePair<string, List<Message>>("alice", new List<Message> { message }),
                            })),
                    }),
                LastReadMessageCounts =
                    new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>(new[]
                    {
                        new KeyValuePair<string, ConcurrentDictionary<string, int>>(
                            "local",
                            new ConcurrentDictionary<string, int>(new[]
                            {
                                new KeyValuePair<string, int>("alice", 1),
                            })),
                    }),
                AutoJoinRoomNames = new ConcurrentDictionary<string, List<string>>(new[]
                {
                    new KeyValuePair<string, List<string>>("local", new List<string> { "music" }),
                }),
                NotifyRoomNames = new ConcurrentDictionary<string, List<string>>(new[]
                {
                    new KeyValuePair<string, List<string>>("local", new List<string> { "vinyl" }),
                }),
                SearchHeaders = new Dictionary<int, SavedStateSearchTabHeader>
                {
                    [-1] = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                        "rare album",
                        12,
                        DateTime.UtcNow.Ticks,
                        3),
                },
            };
        }
    }
}
