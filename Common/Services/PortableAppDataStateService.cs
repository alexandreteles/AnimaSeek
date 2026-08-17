using Common;
using Seeker;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Seeker.Services
{
    /// <summary>Contains the platform-neutral, non-transfer application data persisted by Seeker.</summary>
    /// <remarks>
    /// Collection properties use the same shapes consumed by <see cref="SerializationHelper"/> so callers can
    /// transfer a restored state into an application session without reflection-based conversion.
    /// </remarks>
    public sealed class PortableAppDataState
    {
        /// <summary>Gets or sets user notes keyed by user name.</summary>
        public ConcurrentDictionary<string, string> UserNotes { get; set; } = new();

        /// <summary>Gets or sets user names for which online alerts are enabled.</summary>
        public ConcurrentDictionary<string, byte> UserOnlineAlerts { get; set; } = new();

        /// <summary>Gets or sets recent user names in most-recent-first order.</summary>
        public List<string> RecentUsers { get; set; } = new();

        /// <summary>Gets or sets private-message conversations grouped by signed-in account.</summary>
        public ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> PrivateMessageRoots
        {
            get;
            set;
        } = new();

        /// <summary>Gets or sets last-read private-message counts grouped by signed-in account.</summary>
        public ConcurrentDictionary<string, ConcurrentDictionary<string, int>> LastReadMessageCounts
        {
            get;
            set;
        } = new();

        /// <summary>Gets or sets auto-join room names grouped by signed-in account.</summary>
        public ConcurrentDictionary<string, List<string>> AutoJoinRoomNames { get; set; } = new();

        /// <summary>Gets or sets notification-enabled room names grouped by signed-in account.</summary>
        public ConcurrentDictionary<string, List<string>> NotifyRoomNames { get; set; } = new();

        /// <summary>Gets or sets persisted search-tab headers keyed by tab identifier.</summary>
        public Dictionary<int, SavedStateSearchTabHeader> SearchHeaders { get; set; } = new();
    }

    /// <summary>Describes one payload that could not be restored while other app data continued loading.</summary>
    public sealed class AppDataRestoreFailure
    {
        /// <summary>Creates a failure description.</summary>
        /// <param name="key">The persistence key whose payload failed.</param>
        /// <param name="exception">The parsing exception raised for the payload.</param>
        public AppDataRestoreFailure(string key, Exception exception)
        {
            Key = key;
            Exception = exception;
        }

        /// <summary>Gets the failed persistence key.</summary>
        public string Key { get; }

        /// <summary>Gets the exception raised while parsing the key's value.</summary>
        public Exception Exception { get; }
    }

    /// <summary>Returns restored app data together with independently isolated payload failures.</summary>
    public sealed class PortableAppDataRestoreResult
    {
        /// <summary>Creates a restore result.</summary>
        /// <param name="state">The restored state, with defaults substituted only for failed values.</param>
        /// <param name="failures">The detached collection of failed keys.</param>
        public PortableAppDataRestoreResult(
            PortableAppDataState state,
            IReadOnlyCollection<AppDataRestoreFailure> failures)
        {
            State = state;
            Failures = failures;
        }

        /// <summary>Gets the restored app data.</summary>
        public PortableAppDataState State { get; }

        /// <summary>Gets payload failures encountered during this restore.</summary>
        public IReadOnlyCollection<AppDataRestoreFailure> Failures { get; }

        /// <summary>Determines whether a particular persistence key failed to restore.</summary>
        /// <param name="key">The key to inspect.</param>
        /// <returns><see langword="true"/> when the key failed; otherwise, <see langword="false"/>.</returns>
        public bool Failed(string key) => Failures.Any(failure => failure.Key == key);
    }

    /// <summary>
    /// Restores and persists portable application data using Seeker's canonical serialization formats.
    /// </summary>
    /// <remarks>
    /// Each payload is restored independently. A damaged message archive, for example, becomes an empty archive
    /// without discarding room preferences, user metadata, or search headers. Save operations serialize every
    /// requested payload before writing one logical snapshot and flush the store only once per operation. The
    /// key-value interface does not promise an atomic multi-key transaction.
    /// </remarks>
    public sealed class PortableAppDataStateService
    {
        /// <summary>
        /// Makes the <see cref="UserMetadataService"/> triple (notes, alerts, recent users) swap atomically
        /// with respect to <see cref="PersistUserMetadata"/>, which snapshots the same three statics. UI code
        /// that reads a single static directly still observes only fully built objects.
        /// </summary>
        private static readonly object UserMetadataSwapLock = new object();

        private readonly IKeyValueStore store;

        /// <summary>Creates a service over a platform-specific key-value store.</summary>
        /// <param name="store">The non-null persistence store.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
        public PortableAppDataStateService(IKeyValueStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Restores all portable app-data payloads without mutating global session state.</summary>
        /// <returns>The recovered state and a failure entry for each damaged payload.</returns>
        public PortableAppDataRestoreResult RestoreAll()
        {
            var failures = new List<AppDataRestoreFailure>();
            var state = new PortableAppDataState
            {
                UserNotes = Restore(
                    KeyConsts.M_UserNotes,
                    static payload => SerializationHelper.RestoreUserNotesFromString(payload),
                    static () => new ConcurrentDictionary<string, string>(),
                    failures),
                UserOnlineAlerts = Restore(
                    KeyConsts.M_UserOnlineAlerts,
                    static payload => SerializationHelper.RestoreUserOnlineAlertsFromString(payload),
                    static () => new ConcurrentDictionary<string, byte>(),
                    failures),
                RecentUsers = Restore(
                    KeyConsts.M_RecentUsersList,
                    RestoreRecentUsers,
                    static () => new List<string>(),
                    failures),
                PrivateMessageRoots = Restore(
                    KeyConsts.M_Messages,
                    static payload => SerializationHelper.RestoreMessagesFromString(payload),
                    static () =>
                        new ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>>(),
                    failures),
                LastReadMessageCounts = Restore(
                    KeyConsts.M_LastReadMessageCounts,
                    static payload => SerializationHelper.RestoreLastReadCountsFromString(payload),
                    static () => new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>(),
                    failures),
                AutoJoinRoomNames = Restore(
                    KeyConsts.M_AutoJoinRooms,
                    static payload => SerializationHelper.RestoreAutoJoinRoomsListFromString(payload),
                    static () => new ConcurrentDictionary<string, List<string>>(),
                    failures),
                NotifyRoomNames = Restore(
                    KeyConsts.M_chatroomsToNotify,
                    static payload => SerializationHelper.RestoreNotifyRoomsListFromString(payload),
                    static () => new ConcurrentDictionary<string, List<string>>(),
                    failures),
                SearchHeaders = Restore(
                    KeyConsts.M_SearchTabsState_Headers,
                    static payload => SerializationHelper.RestoreSavedStateHeaderDictFromString(payload),
                    static () => new Dictionary<int, SavedStateSearchTabHeader>(),
                    failures),
            };

            return new PortableAppDataRestoreResult(state, failures.ToArray());
        }

        /// <summary>
        /// Reloads canonical user notes, alerts, and recent users into <see cref="UserMetadataService"/>.
        /// </summary>
        /// <returns>Failures for damaged metadata payloads; healthy metadata is still applied.</returns>
        /// <remarks>
        /// This explicit reload entry point is suitable after settings import. The installed recent-user manager
        /// persists subsequent mutations back through this service.
        /// </remarks>
        public IReadOnlyCollection<AppDataRestoreFailure> ReloadUserMetadata()
        {
            var failures = new List<AppDataRestoreFailure>();
            ConcurrentDictionary<string, string> notes = Restore(
                KeyConsts.M_UserNotes,
                static payload => SerializationHelper.RestoreUserNotesFromString(payload),
                static () => new ConcurrentDictionary<string, string>(),
                failures);
            ConcurrentDictionary<string, byte> alerts = Restore(
                KeyConsts.M_UserOnlineAlerts,
                static payload => SerializationHelper.RestoreUserOnlineAlertsFromString(payload),
                static () => new ConcurrentDictionary<string, byte>(),
                failures);
            List<string> recentUsers = Restore(
                KeyConsts.M_RecentUsersList,
                RestoreRecentUsers,
                static () => new List<string>(),
                failures);

            var recentUserManager = new RecentUserManager(PersistRecentUsers);
            recentUserManager.SetRecentUserList(recentUsers);
            lock (UserMetadataSwapLock)
            {
                UserMetadataService.UserNotes = notes;
                UserMetadataService.UserOnlineAlerts = alerts;
                UserMetadataService.RecentUsersManager = recentUserManager;
            }

            return failures.ToArray();
        }

        /// <summary>Persists every payload in <paramref name="state"/> with a single flush.</summary>
        /// <param name="state">The complete portable app-data snapshot.</param>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
        public void PersistAll(PortableAppDataState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            SerializedAppData payloads = SerializeAll(state);
            store.PutString(KeyConsts.M_UserNotes, payloads.UserNotes);
            store.PutString(KeyConsts.M_UserOnlineAlerts, payloads.UserOnlineAlerts);
            store.PutString(KeyConsts.M_RecentUsersList, payloads.RecentUsers);
            store.PutString(KeyConsts.M_Messages, payloads.PrivateMessageRoots);
            store.PutString(KeyConsts.M_LastReadMessageCounts, payloads.LastReadMessageCounts);
            store.PutString(KeyConsts.M_AutoJoinRooms, payloads.AutoJoinRoomNames);
            store.PutString(KeyConsts.M_chatroomsToNotify, payloads.NotifyRoomNames);
            store.PutString(KeyConsts.M_SearchTabsState_Headers, payloads.SearchHeaders);
            store.Flush();
        }

        /// <summary>Persists the current global user notes, alerts, and recent-user list with one flush.</summary>
        public void PersistUserMetadata()
        {
            ConcurrentDictionary<string, string> currentNotes;
            ConcurrentDictionary<string, byte> currentAlerts;
            RecentUserManager currentRecentUsers;
            lock (UserMetadataSwapLock)
            {
                currentNotes = UserMetadataService.UserNotes;
                currentAlerts = UserMetadataService.UserOnlineAlerts;
                currentRecentUsers = UserMetadataService.RecentUsersManager;
            }

            string notes = SerializationHelper.SaveUserNotesToString(currentNotes);
            string alerts = SerializationHelper.SaveUserOnlineAlertsToString(currentAlerts);
            string recentUsers = SerializationHelper.SaveStringListToString(
                currentRecentUsers.GetRecentUserList());

            store.PutString(KeyConsts.M_UserNotes, notes);
            store.PutString(KeyConsts.M_UserOnlineAlerts, alerts);
            store.PutString(KeyConsts.M_RecentUsersList, recentUsers);
            store.Flush();
        }

        /// <summary>Persists a recent-user snapshot in canonical order.</summary>
        /// <param name="users">The ordered recent-user values.</param>
        /// <exception cref="ArgumentNullException"><paramref name="users"/> is <see langword="null"/>.</exception>
        public void PersistRecentUsers(IReadOnlyList<string> users)
        {
            if (users == null)
            {
                throw new ArgumentNullException(nameof(users));
            }

            string payload = SerializationHelper.SaveStringListToString(users);
            store.PutString(KeyConsts.M_RecentUsersList, payload);
            store.Flush();
        }

        /// <summary>Persists private-message roots and read counts with one flush.</summary>
        /// <param name="messages">Conversations grouped by account and user.</param>
        /// <param name="lastReadCounts">Last-read counts grouped by account and user.</param>
        public void PersistPrivateMessages(
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> messages,
            ConcurrentDictionary<string, ConcurrentDictionary<string, int>> lastReadCounts)
        {
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            if (lastReadCounts == null)
            {
                throw new ArgumentNullException(nameof(lastReadCounts));
            }

            string messagePayload = SerializationHelper.SaveMessagesToString(messages);
            string countPayload = SerializationHelper.SaveLastReadCountsToString(lastReadCounts);
            store.PutString(KeyConsts.M_Messages, messagePayload);
            store.PutString(KeyConsts.M_LastReadMessageCounts, countPayload);
            store.Flush();
        }

        /// <summary>Persists account-specific room preferences with one flush.</summary>
        /// <param name="autoJoinRooms">Auto-join room names grouped by account.</param>
        /// <param name="notifyRooms">Notification room names grouped by account.</param>
        public void PersistRoomPreferences(
            ConcurrentDictionary<string, List<string>> autoJoinRooms,
            ConcurrentDictionary<string, List<string>> notifyRooms)
        {
            if (autoJoinRooms == null)
            {
                throw new ArgumentNullException(nameof(autoJoinRooms));
            }

            if (notifyRooms == null)
            {
                throw new ArgumentNullException(nameof(notifyRooms));
            }

            string autoJoinPayload = SerializationHelper.SaveAutoJoinRoomsListToString(autoJoinRooms);
            string notifyPayload = SerializationHelper.SaveNotifyRoomsListToString(notifyRooms);
            store.PutString(KeyConsts.M_AutoJoinRooms, autoJoinPayload);
            store.PutString(KeyConsts.M_chatroomsToNotify, notifyPayload);
            store.Flush();
        }

        /// <summary>Persists search-tab headers in the canonical source-generated JSON format.</summary>
        /// <param name="headers">Headers keyed by search-tab identifier.</param>
        public void PersistSearchHeaders(Dictionary<int, SavedStateSearchTabHeader> headers)
        {
            if (headers == null)
            {
                throw new ArgumentNullException(nameof(headers));
            }

            string payload = SerializationHelper.SaveSavedStateHeaderDictToString(headers);
            store.PutString(KeyConsts.M_SearchTabsState_Headers, payload);
            store.Flush();
        }

        private T Restore<T>(
            string key,
            Func<string, T> deserialize,
            Func<T> createDefault,
            ICollection<AppDataRestoreFailure> failures)
        {
            try
            {
                string payload = store.GetString(key, string.Empty) ?? string.Empty;
                return payload.Length == 0 ? createDefault() : deserialize(payload);
            }
            catch (Exception exception)
            {
                failures.Add(new AppDataRestoreFailure(key, exception));
                return createDefault();
            }
        }

        private static List<string> RestoreRecentUsers(string payload)
        {
            string trimmed = payload.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                using JsonDocument document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind != JsonValueKind.Array
                    || document.RootElement.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
                {
                    throw new JsonException("Recent-user history must be an array of strings.");
                }
            }
            else if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                };
                using var reader = new StringReader(trimmed);
                using XmlReader xmlReader = XmlReader.Create(reader, settings);
                XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
                if (document.Root == null
                    || !string.Equals(document.Root.Name.LocalName, "ArrayOfString", StringComparison.Ordinal)
                    || document.Root.Elements().Any(element =>
                        !string.Equals(element.Name.LocalName, "string", StringComparison.Ordinal)))
                {
                    throw new XmlException("Recent-user history must be an ArrayOfString document.");
                }
            }
            else
            {
                throw new FormatException("Recent-user history is neither JSON nor legacy XML.");
            }

            return SerializationHelper.RestoreStringListFromString(payload);
        }

        private static SerializedAppData SerializeAll(PortableAppDataState state) =>
            new SerializedAppData(
                SerializationHelper.SaveUserNotesToString(
                    state.UserNotes ?? throw new ArgumentException("User notes cannot be null.", nameof(state))),
                SerializationHelper.SaveUserOnlineAlertsToString(
                    state.UserOnlineAlerts ??
                    throw new ArgumentException("User online alerts cannot be null.", nameof(state))),
                SerializationHelper.SaveStringListToString(
                    state.RecentUsers ?? throw new ArgumentException("Recent users cannot be null.", nameof(state))),
                SerializationHelper.SaveMessagesToString(
                    state.PrivateMessageRoots ??
                    throw new ArgumentException("Private-message roots cannot be null.", nameof(state))),
                SerializationHelper.SaveLastReadCountsToString(
                    state.LastReadMessageCounts ??
                    throw new ArgumentException("Last-read counts cannot be null.", nameof(state))),
                SerializationHelper.SaveAutoJoinRoomsListToString(
                    state.AutoJoinRoomNames ??
                    throw new ArgumentException("Auto-join rooms cannot be null.", nameof(state))),
                SerializationHelper.SaveNotifyRoomsListToString(
                    state.NotifyRoomNames ?? throw new ArgumentException("Notify rooms cannot be null.", nameof(state))),
                SerializationHelper.SaveSavedStateHeaderDictToString(
                    state.SearchHeaders ?? throw new ArgumentException("Search headers cannot be null.", nameof(state))));

        private sealed class SerializedAppData
        {
            public SerializedAppData(
                string userNotes,
                string userOnlineAlerts,
                string recentUsers,
                string privateMessageRoots,
                string lastReadMessageCounts,
                string autoJoinRoomNames,
                string notifyRoomNames,
                string searchHeaders)
            {
                UserNotes = userNotes;
                UserOnlineAlerts = userOnlineAlerts;
                RecentUsers = recentUsers;
                PrivateMessageRoots = privateMessageRoots;
                LastReadMessageCounts = lastReadMessageCounts;
                AutoJoinRoomNames = autoJoinRoomNames;
                NotifyRoomNames = notifyRoomNames;
                SearchHeaders = searchHeaders;
            }

            public string UserNotes { get; }
            public string UserOnlineAlerts { get; }
            public string RecentUsers { get; }
            public string PrivateMessageRoots { get; }
            public string LastReadMessageCounts { get; }
            public string AutoJoinRoomNames { get; }
            public string NotifyRoomNames { get; }
            public string SearchHeaders { get; }
        }
    }
}
