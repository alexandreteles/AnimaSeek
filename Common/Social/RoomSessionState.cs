using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker.Social
{
    /// <summary>Describes the local connection state of a chatroom.</summary>
    public enum RoomConnectionState
    {
        /// <summary>The account has not joined the room in this process.</summary>
        NotJoined = 0,

        /// <summary>A join request is in flight.</summary>
        Joining = 1,

        /// <summary>The account is currently joined to the room.</summary>
        Joined = 2,

        /// <summary>The account had joined the room before the current connection was lost.</summary>
        Disconnected = 3,

        /// <summary>The most recent join request failed.</summary>
        Failed = 4,

        /// <summary>The server explicitly rejected the most recent join request.</summary>
        Forbidden = 5,
    }

    /// <summary>Describes the delivery state of a room-message entry.</summary>
    public enum RoomMessageDeliveryState
    {
        /// <summary>A message received from another user.</summary>
        Received = 0,

        /// <summary>An outgoing message waiting for server confirmation.</summary>
        Sending = 1,

        /// <summary>An outgoing message accepted by the server.</summary>
        Sent = 2,

        /// <summary>An outgoing message that could not be sent.</summary>
        Failed = 3,
    }

    /// <summary>Identifies the kind of a room-member activity entry.</summary>
    public enum RoomMemberActivityKind
    {
        /// <summary>A user joined the room.</summary>
        Joined = 0,

        /// <summary>A user left the room.</summary>
        Left = 1,

        /// <summary>A user changed to the away state.</summary>
        WentAway = 2,

        /// <summary>A user changed to the online state.</summary>
        CameOnline = 3,

        /// <summary>A user changed to the offline state.</summary>
        WentOffline = 4,
    }

    /// <summary>Represents one immutable room-message entry.</summary>
    public sealed class RoomMessageEntry
    {
        /// <summary>Creates an immutable room-message entry.</summary>
        public RoomMessageEntry(
            long sequence,
            string username,
            string message,
            DateTime timestampUtc,
            bool isFromCurrentUser,
            RoomMessageDeliveryState deliveryState,
            string? failureReason = null)
        {
            Sequence = sequence;
            Username = username;
            Message = message;
            TimestampUtc = timestampUtc;
            IsFromCurrentUser = isFromCurrentUser;
            DeliveryState = deliveryState;
            FailureReason = failureReason;
        }

        /// <summary>Gets the process-local sequence identifier.</summary>
        public long Sequence { get; }

        /// <summary>Gets the sender name.</summary>
        public string Username { get; }

        /// <summary>Gets the message body.</summary>
        public string Message { get; }

        /// <summary>Gets the UTC send or receive timestamp.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Gets whether the current account sent the message.</summary>
        public bool IsFromCurrentUser { get; }

        /// <summary>Gets the current delivery state.</summary>
        public RoomMessageDeliveryState DeliveryState { get; }

        /// <summary>Gets the diagnostic delivery failure, when available.</summary>
        public string? FailureReason { get; }
    }

    /// <summary>Represents one immutable room-member activity entry.</summary>
    public sealed class RoomMemberActivity
    {
        /// <summary>Creates an immutable member-activity entry.</summary>
        public RoomMemberActivity(string username, RoomMemberActivityKind kind, DateTime timestampUtc)
        {
            Username = username;
            Kind = kind;
            TimestampUtc = timestampUtc;
        }

        /// <summary>Gets the affected user name.</summary>
        public string Username { get; }

        /// <summary>Gets the activity kind.</summary>
        public RoomMemberActivityKind Kind { get; }

        /// <summary>Gets the UTC event timestamp.</summary>
        public DateTime TimestampUtc { get; }
    }

    /// <summary>Returns a detached, internally consistent view of one room.</summary>
    public sealed class RoomSessionSnapshot
    {
        internal RoomSessionSnapshot(
            string roomName,
            RoomConnectionState connectionState,
            RoomData? roomData,
            IReadOnlyList<RoomMessageEntry> messages,
            IReadOnlyList<RoomMemberActivity> memberActivity,
            IReadOnlyList<RoomTicker> tickers,
            bool hasUnreadMessages,
            string? joinFailureReason)
        {
            RoomName = roomName;
            ConnectionState = connectionState;
            RoomData = roomData;
            Messages = messages;
            MemberActivity = memberActivity;
            Tickers = tickers;
            HasUnreadMessages = hasUnreadMessages;
            JoinFailureReason = joinFailureReason;
        }

        /// <summary>Gets the room name.</summary>
        public string RoomName { get; }

        /// <summary>Gets the latest local connection state.</summary>
        public RoomConnectionState ConnectionState { get; }

        /// <summary>Gets the latest immutable server room data, if a join completed.</summary>
        public RoomData? RoomData { get; }

        /// <summary>Gets the bounded oldest-to-newest message history.</summary>
        public IReadOnlyList<RoomMessageEntry> Messages { get; }

        /// <summary>Gets the bounded oldest-to-newest member activity history.</summary>
        public IReadOnlyList<RoomMemberActivity> MemberActivity { get; }

        /// <summary>Gets the latest bounded ticker list.</summary>
        public IReadOnlyList<RoomTicker> Tickers { get; }

        /// <summary>Gets whether a message arrived while this room was not visible.</summary>
        public bool HasUnreadMessages { get; }

        /// <summary>Gets the most recent join failure message, when available.</summary>
        public string? JoinFailureReason { get; }

        /// <summary>Gets whether the current account owns this private room.</summary>
        /// <param name="username">The current account name.</param>
        /// <returns><see langword="true"/> when the latest room data names that account as owner.</returns>
        public bool IsOwnedBy(string username) =>
            RoomData is not null &&
            string.Equals(RoomData.Owner, username, StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets whether the current account moderates this private room.</summary>
        /// <param name="username">The current account name.</param>
        /// <returns><see langword="true"/> when the latest room data includes that account as an operator.</returns>
        public bool IsModeratedBy(string username) =>
            RoomData?.Operators?.Any(value =>
                string.Equals(value, username, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// Maintains bounded chatroom histories and immutable snapshots under a single synchronization boundary.
    /// </summary>
    /// <remarks>
    /// Network callbacks may invoke mutation methods concurrently. Every returned snapshot is detached from the
    /// mutable queues, so UIKit can enumerate it without holding a lock or observing a partially applied event.
    /// </remarks>
    public sealed class RoomSessionState
    {
        private readonly object sync = new();
        private readonly Dictionary<string, MutableRoomState> rooms =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly int messageCapacity;
        private readonly int memberActivityCapacity;
        private readonly int tickerCapacity;
        private string? visibleRoomName;
        private long nextMessageSequence;

        /// <summary>Creates an empty state container with explicit history limits.</summary>
        /// <param name="messageCapacity">Maximum messages retained per room.</param>
        /// <param name="memberActivityCapacity">Maximum member events retained per room.</param>
        /// <param name="tickerCapacity">Maximum tickers retained per room.</param>
        /// <exception cref="ArgumentOutOfRangeException">Any capacity is less than one.</exception>
        public RoomSessionState(
            int messageCapacity = 100,
            int memberActivityCapacity = 100,
            int tickerCapacity = 100)
        {
            this.messageCapacity = RequireCapacity(messageCapacity, nameof(messageCapacity));
            this.memberActivityCapacity = RequireCapacity(memberActivityCapacity, nameof(memberActivityCapacity));
            this.tickerCapacity = RequireCapacity(tickerCapacity, nameof(tickerCapacity));
        }

        /// <summary>Gets a detached snapshot for every known room, ordered by room name.</summary>
        public IReadOnlyList<RoomSessionSnapshot> Rooms
        {
            get
            {
                lock (sync)
                {
                    return rooms.Values
                        .OrderBy(room => room.RoomName, StringComparer.OrdinalIgnoreCase)
                        .Select(CreateSnapshot)
                        .ToArray();
                }
            }
        }

        /// <summary>Marks the room currently visible to the user and clears its unread flag.</summary>
        /// <param name="roomName">The visible room, or <see langword="null"/> when no room is visible.</param>
        public void SetVisibleRoom(string? roomName)
        {
            lock (sync)
            {
                visibleRoomName = string.IsNullOrWhiteSpace(roomName) ? null : roomName;
                if (visibleRoomName is not null && rooms.TryGetValue(visibleRoomName, out MutableRoomState? room))
                {
                    room.HasUnreadMessages = false;
                }
            }
        }

        /// <summary>Marks a join attempt as pending.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        public void BeginJoin(string roomName)
        {
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                room.ConnectionState = RoomConnectionState.Joining;
                room.JoinFailureReason = null;
            }
        }

        /// <summary>Applies a successful room join and the server's initial room data.</summary>
        /// <param name="roomData">The non-null join response.</param>
        public void CompleteJoin(RoomData roomData)
        {
            if (roomData is null)
            {
                throw new ArgumentNullException(nameof(roomData));
            }
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomData.Name);
                room.ConnectionState = RoomConnectionState.Joined;
                room.RoomData = CopyRoomData(roomData);
                room.JoinFailureReason = null;
            }
        }

        /// <summary>Applies a failed room join while preserving existing room history.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="failureReason">The optional diagnostic failure reason.</param>
        /// <param name="forbidden">Whether the server explicitly rejected membership.</param>
        public void FailJoin(string roomName, string? failureReason, bool forbidden)
        {
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                room.ConnectionState = forbidden ? RoomConnectionState.Forbidden : RoomConnectionState.Failed;
                room.JoinFailureReason = failureReason;
            }
        }

        /// <summary>Applies an intentional leave while retaining bounded history for later presentation.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        public void CompleteLeave(string roomName)
        {
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                room.ConnectionState = RoomConnectionState.NotJoined;
                room.RoomData = null;
                room.HasUnreadMessages = false;
                room.JoinFailureReason = null;
            }
        }

        /// <summary>Marks every joined room disconnected and returns the affected room names.</summary>
        /// <returns>A detached list of rooms whose state changed.</returns>
        public IReadOnlyList<string> MarkDisconnected()
        {
            lock (sync)
            {
                string[] affected = rooms.Values
                    .Where(room => room.ConnectionState is RoomConnectionState.Joined or RoomConnectionState.Joining)
                    .Select(room => room.RoomName)
                    .ToArray();
                foreach (string roomName in affected)
                {
                    rooms[roomName].ConnectionState = RoomConnectionState.Disconnected;
                }

                return affected;
            }
        }

        /// <summary>Adds an incoming message and updates unread state atomically.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="username">The non-empty sender name.</param>
        /// <param name="message">The non-empty message body.</param>
        /// <param name="timestampUtc">The UTC receive timestamp.</param>
        /// <returns>The immutable entry added to the room.</returns>
        public RoomMessageEntry AddIncomingMessage(
            string roomName,
            string username,
            string message,
            DateTime timestampUtc)
        {
            RequireText(username, nameof(username));
            RequireText(message, nameof(message));
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                var entry = new RoomMessageEntry(
                    ++nextMessageSequence,
                    username,
                    message,
                    NormalizeUtc(timestampUtc),
                    false,
                    RoomMessageDeliveryState.Received);
                EnqueueCapped(room.Messages, entry, messageCapacity);
                room.HasUnreadMessages = !string.Equals(
                    visibleRoomName,
                    roomName,
                    StringComparison.OrdinalIgnoreCase);
                return entry;
            }
        }

        /// <summary>Adds a pending outgoing message.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="username">The current account name.</param>
        /// <param name="message">The non-empty message body.</param>
        /// <param name="timestampUtc">The UTC send timestamp.</param>
        /// <returns>The immutable pending entry, including its sequence identifier.</returns>
        public RoomMessageEntry AddOutgoingMessage(
            string roomName,
            string username,
            string message,
            DateTime timestampUtc)
        {
            RequireText(username, nameof(username));
            RequireText(message, nameof(message));
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                var entry = new RoomMessageEntry(
                    ++nextMessageSequence,
                    username,
                    message,
                    NormalizeUtc(timestampUtc),
                    true,
                    RoomMessageDeliveryState.Sending);
                EnqueueCapped(room.Messages, entry, messageCapacity);
                return entry;
            }
        }

        /// <summary>Updates an outgoing entry after the server call completes.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="sequence">The sequence returned by <see cref="AddOutgoingMessage"/>.</param>
        /// <param name="succeeded">Whether the message was accepted.</param>
        /// <param name="failureReason">The optional diagnostic reason on failure.</param>
        /// <returns><see langword="true"/> when the still-retained entry was updated.</returns>
        public bool CompleteOutgoingMessage(
            string roomName,
            long sequence,
            bool succeeded,
            string? failureReason = null)
        {
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                RoomMessageEntry? entry = room.Messages.FirstOrDefault(candidate => candidate.Sequence == sequence);
                if (entry is null)
                {
                    return false;
                }

                var replacement = new RoomMessageEntry(
                    entry.Sequence,
                    entry.Username,
                    entry.Message,
                    entry.TimestampUtc,
                    entry.IsFromCurrentUser,
                    succeeded ? RoomMessageDeliveryState.Sent : RoomMessageDeliveryState.Failed,
                    succeeded ? null : failureReason);
                Replace(room.Messages, entry, replacement);
                return true;
            }
        }

        /// <summary>Adds or updates a member after a room-join event.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="userData">The non-null server user data.</param>
        /// <param name="timestampUtc">The UTC event timestamp.</param>
        public void AddMember(string roomName, UserData userData, DateTime timestampUtc)
        {
            if (userData is null)
            {
                throw new ArgumentNullException(nameof(userData));
            }
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                if (room.RoomData is not null)
                {
                    UserData[] users = room.RoomData.Users
                        .Where(user => !EqualsName(user.Username, userData.Username))
                        .Append(userData)
                        .ToArray();
                    room.RoomData = CopyRoomData(room.RoomData, users: users);
                }

                AddMemberActivity(room, userData.Username, RoomMemberActivityKind.Joined, timestampUtc);
            }
        }

        /// <summary>Removes a member after a room-leave event.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="username">The non-empty departing user name.</param>
        /// <param name="timestampUtc">The UTC event timestamp.</param>
        public void RemoveMember(string roomName, string username, DateTime timestampUtc)
        {
            RequireText(username, nameof(username));
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                if (room.RoomData is not null)
                {
                    UserData[] users = room.RoomData.Users
                        .Where(user => !EqualsName(user.Username, username))
                        .ToArray();
                    room.RoomData = CopyRoomData(room.RoomData, users: users);
                }

                AddMemberActivity(room, username, RoomMemberActivityKind.Left, timestampUtc);
            }
        }

        /// <summary>Applies a presence change to every joined room containing the user.</summary>
        /// <param name="status">The non-null status update.</param>
        /// <param name="timestampUtc">The UTC event timestamp.</param>
        /// <returns>Detached room names whose user data changed.</returns>
        public IReadOnlyList<string> UpdateMemberStatus(UserStatus status, DateTime timestampUtc)
        {
            if (status is null)
            {
                throw new ArgumentNullException(nameof(status));
            }
            lock (sync)
            {
                var changed = new List<string>();
                foreach (MutableRoomState room in rooms.Values)
                {
                    if (room.RoomData is null)
                    {
                        continue;
                    }

                    UserData? existing = room.RoomData.Users.FirstOrDefault(user => EqualsName(user.Username, status.Username));
                    if (existing is null || existing.Status == status.Presence)
                    {
                        continue;
                    }

                    UserData[] users = room.RoomData.Users
                        .Select(user => EqualsName(user.Username, status.Username)
                            ? user.WithStatus(status.Presence)
                            : user)
                        .ToArray();
                    room.RoomData = CopyRoomData(room.RoomData, users: users);
                    AddMemberActivity(room, status.Username, GetActivityKind(status.Presence), timestampUtc);
                    changed.Add(room.RoomName);
                }

                return changed.ToArray();
            }
        }

        /// <summary>Replaces the complete ticker list for a room, retaining at most the configured capacity.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="tickers">The non-null server ticker list.</param>
        public void ReplaceTickers(string roomName, IEnumerable<RoomTicker> tickers)
        {
            if (tickers is null)
            {
                throw new ArgumentNullException(nameof(tickers));
            }
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                room.Tickers.Clear();
                foreach (RoomTicker ticker in tickers.TakeLast(tickerCapacity))
                {
                    room.Tickers.Enqueue(ticker);
                }
            }
        }

        /// <summary>Adds or replaces the ticker owned by a user.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="ticker">The non-null ticker.</param>
        public void AddTicker(string roomName, RoomTicker ticker)
        {
            if (ticker is null)
            {
                throw new ArgumentNullException(nameof(ticker));
            }
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                RoomTicker[] retained = room.Tickers
                    .Where(item => !EqualsName(item.Username, ticker.Username))
                    .Append(ticker)
                    .TakeLast(tickerCapacity)
                    .ToArray();
                room.Tickers.Clear();
                foreach (RoomTicker item in retained)
                {
                    room.Tickers.Enqueue(item);
                }
            }
        }

        /// <summary>Removes the ticker owned by a user.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="username">The non-empty ticker owner name.</param>
        public void RemoveTicker(string roomName, string username)
        {
            RequireText(username, nameof(username));
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                RoomTicker[] retained = room.Tickers
                    .Where(ticker => !EqualsName(ticker.Username, username))
                    .ToArray();
                room.Tickers.Clear();
                foreach (RoomTicker ticker in retained)
                {
                    room.Tickers.Enqueue(ticker);
                }
            }
        }

        /// <summary>Adds or removes a private-room moderator in the latest room data.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="username">The non-empty moderator name.</param>
        /// <param name="added">Whether the moderator was added.</param>
        public void UpdateModerator(string roomName, string username, bool added)
        {
            RequireText(username, nameof(username));
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                if (room.RoomData is null)
                {
                    return;
                }

                IEnumerable<string> operators = room.RoomData.Operators ?? Array.Empty<string>();
                string[] updated = added
                    ? operators.Where(value => !EqualsName(value, username)).Append(username).ToArray()
                    : operators.Where(value => !EqualsName(value, username)).ToArray();
                room.RoomData = CopyRoomData(room.RoomData, operators: updated);
            }
        }

        /// <summary>Replaces the moderator list supplied by a private-room server update.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="moderators">The non-null moderator names.</param>
        public void ReplaceModerators(string roomName, IEnumerable<string> moderators)
        {
            if (moderators is null)
            {
                throw new ArgumentNullException(nameof(moderators));
            }
            lock (sync)
            {
                MutableRoomState room = GetOrCreate(roomName);
                if (room.RoomData is not null)
                {
                    room.RoomData = CopyRoomData(
                        room.RoomData,
                        operators: moderators.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                }
            }
        }

        /// <summary>Gets a detached snapshot for a room.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <returns>The room snapshot, or <see langword="null"/> when the room is unknown.</returns>
        public RoomSessionSnapshot? GetSnapshot(string roomName)
        {
            RequireText(roomName, nameof(roomName));
            lock (sync)
            {
                return rooms.TryGetValue(roomName, out MutableRoomState? room)
                    ? CreateSnapshot(room)
                    : null;
            }
        }

        private MutableRoomState GetOrCreate(string roomName)
        {
            RequireText(roomName, nameof(roomName));
            if (!rooms.TryGetValue(roomName, out MutableRoomState? room))
            {
                room = new MutableRoomState(roomName);
                rooms.Add(roomName, room);
            }

            return room;
        }

        private RoomSessionSnapshot CreateSnapshot(MutableRoomState room) =>
            new(
                room.RoomName,
                room.ConnectionState,
                room.RoomData is null ? null : CopyRoomData(room.RoomData),
                room.Messages.ToArray(),
                room.MemberActivity.ToArray(),
                room.Tickers.ToArray(),
                room.HasUnreadMessages,
                room.JoinFailureReason);

        private void AddMemberActivity(
            MutableRoomState room,
            string username,
            RoomMemberActivityKind kind,
            DateTime timestampUtc) =>
            EnqueueCapped(
                room.MemberActivity,
                new RoomMemberActivity(username, kind, NormalizeUtc(timestampUtc)),
                memberActivityCapacity);

        private static RoomData CopyRoomData(
            RoomData source,
            IEnumerable<UserData>? users = null,
            IEnumerable<string>? operators = null) =>
            new(
                source.Name,
                users ?? source.Users,
                source.IsPrivate,
                source.Owner,
                operators ?? source.Operators);

        private static RoomMemberActivityKind GetActivityKind(UserPresence presence) => presence switch
        {
            UserPresence.Away => RoomMemberActivityKind.WentAway,
            UserPresence.Online => RoomMemberActivityKind.CameOnline,
            _ => RoomMemberActivityKind.WentOffline,
        };

        private static void EnqueueCapped<T>(Queue<T> queue, T item, int capacity)
        {
            queue.Enqueue(item);
            while (queue.Count > capacity)
            {
                queue.Dequeue();
            }
        }

        private static void Replace<T>(Queue<T> queue, T existing, T replacement)
            where T : class
        {
            T[] values = queue.Select(value => ReferenceEquals(value, existing) ? replacement : value).ToArray();
            queue.Clear();
            foreach (T value in values)
            {
                queue.Enqueue(value);
            }
        }

        private static int RequireCapacity(int capacity, string parameterName) => capacity > 0
            ? capacity
            : throw new ArgumentOutOfRangeException(parameterName, capacity, "A history capacity must be positive.");

        private static string RequireText(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A room state value cannot be empty or whitespace.", parameterName)
                : value;

        private static bool EqualsName(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        private sealed class MutableRoomState
        {
            public MutableRoomState(string roomName)
            {
                RoomName = roomName;
            }

            public string RoomName { get; }
            public RoomConnectionState ConnectionState { get; set; }
            public RoomData? RoomData { get; set; }
            public Queue<RoomMessageEntry> Messages { get; } = new();
            public Queue<RoomMemberActivity> MemberActivity { get; } = new();
            public Queue<RoomTicker> Tickers { get; } = new();
            public bool HasUnreadMessages { get; set; }
            public string? JoinFailureReason { get; set; }
        }
    }
}
