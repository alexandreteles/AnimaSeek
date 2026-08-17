using Seeker;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Common.Messages
{
    /// <summary>Identifies a chatroom-session state transition.</summary>
    public enum ChatroomStateChange
    {
        /// <summary>Room membership data changed.</summary>
        MembershipChanged,

        /// <summary>A member's presence changed.</summary>
        MemberPresenceChanged,

        /// <summary>A chat message was appended.</summary>
        MessageAdded,

        /// <summary>A status message was appended.</summary>
        StatusMessageAdded,

        /// <summary>A room's unread state changed.</summary>
        UnreadStateChanged,

        /// <summary>The room currently presented by the UI changed.</summary>
        CurrentRoomChanged,
    }

    /// <summary>Provides a detached description of a chatroom state transition.</summary>
    public sealed class ChatroomStateChangedEventArgs : EventArgs
    {
        /// <summary>Creates chatroom change event data.</summary>
        public ChatroomStateChangedEventArgs(ChatroomStateChange change, string? roomName, bool isUnread)
        {
            Change = change;
            RoomName = roomName;
            IsUnread = isUnread;
        }

        /// <summary>Gets the transition kind.</summary>
        public ChatroomStateChange Change { get; }

        /// <summary>Gets the affected room, or <see langword="null"/> for a session-wide transition.</summary>
        public string? RoomName { get; }

        /// <summary>Gets whether the affected room is unread after the transition.</summary>
        public bool IsUnread { get; }
    }

    /// <summary>Represents one member in a portable room-membership snapshot.</summary>
    public sealed class ChatroomMemberSnapshot
    {
        /// <summary>Creates a room-member snapshot.</summary>
        public ChatroomMemberSnapshot(string username, UserPresence presence, bool isOperator = false)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("A non-empty user name is required.", nameof(username));
            }

            Username = username;
            Presence = presence;
            IsOperator = isOperator;
        }

        /// <summary>Gets the member's user name.</summary>
        public string Username { get; }

        /// <summary>Gets the member's current Soulseek presence.</summary>
        public UserPresence Presence { get; }

        /// <summary>Gets whether the member moderates the room.</summary>
        public bool IsOperator { get; }

        internal ChatroomMemberSnapshot WithPresence(UserPresence presence) =>
            new(Username, presence, IsOperator);
    }

    /// <summary>Provides a detached snapshot of one room's session state.</summary>
    public sealed class ChatroomRoomSnapshot
    {
        /// <summary>Creates a complete detached room snapshot.</summary>
        public ChatroomRoomSnapshot(
            string roomName,
            bool isJoined,
            bool isConnected,
            bool isPrivate,
            string? owner,
            bool isUnread,
            IReadOnlyList<ChatroomMemberSnapshot> members,
            IReadOnlyList<Message> messages,
            IReadOnlyList<StatusMessageUpdate> statusMessages)
        {
            RoomName = roomName;
            IsJoined = isJoined;
            IsConnected = isConnected;
            IsPrivate = isPrivate;
            Owner = owner;
            IsUnread = isUnread;
            Members = members;
            Messages = messages;
            StatusMessages = statusMessages;
        }

        /// <summary>Gets the room name.</summary>
        public string RoomName { get; }

        /// <summary>Gets whether the user intends to remain joined.</summary>
        public bool IsJoined { get; }

        /// <summary>Gets whether the room currently has a live server membership.</summary>
        public bool IsConnected { get; }

        /// <summary>Gets whether the room is private.</summary>
        public bool IsPrivate { get; }

        /// <summary>Gets the room owner when known.</summary>
        public string? Owner { get; }

        /// <summary>Gets whether the room has unseen chat messages.</summary>
        public bool IsUnread { get; }

        /// <summary>Gets the alphabetically ordered member snapshot.</summary>
        public IReadOnlyList<ChatroomMemberSnapshot> Members { get; }

        /// <summary>Gets retained chat messages in chronological order.</summary>
        public IReadOnlyList<Message> Messages { get; }

        /// <summary>Gets retained membership/presence status messages in chronological order.</summary>
        public IReadOnlyList<StatusMessageUpdate> StatusMessages { get; }
    }

    /// <summary>
    /// Owns portable room membership, unread state, and bounded chat/status histories.
    /// </summary>
    /// <remarks>
    /// All operations are thread-safe. Events are raised after the state lock is released and run on the calling
    /// thread. Network clients translate their room events into these methods; UI code consumes detached snapshots.
    /// This class is an intended seam for the upcoming iOS UIKit chatrooms screen and is currently referenced only
    /// by tests; it is not dead code. It overlaps <see cref="Seeker.Social.RoomSessionState"/>, which the iOS
    /// session services already consume: that type models per-room connection lifecycle and delivery-tracked
    /// message entries, while this type models <see cref="Message"/>-based histories with membership snapshots
    /// and cross-room unread/current-room tracking for the chatrooms screen.
    /// </remarks>
    public sealed class ChatroomSessionState
    {
        /// <summary>The default maximum retained chat and status messages per room.</summary>
        public const int DefaultHistoryCapacity = 100;

        private readonly object sync = new();
        private readonly int messageCapacity;
        private readonly int statusCapacity;
        private readonly Dictionary<string, RoomState> rooms = new(StringComparer.Ordinal);
        private string? currentRoom;

        /// <summary>Creates an empty chatroom session.</summary>
        /// <param name="messageCapacity">The positive maximum retained chat messages per room.</param>
        /// <param name="statusCapacity">The positive maximum retained status messages per room.</param>
        public ChatroomSessionState(
            int messageCapacity = DefaultHistoryCapacity,
            int statusCapacity = DefaultHistoryCapacity)
        {
            if (messageCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(messageCapacity),
                    messageCapacity,
                    "Message capacity must be positive.");
            }

            if (statusCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(statusCapacity),
                    statusCapacity,
                    "Status capacity must be positive.");
            }

            this.messageCapacity = messageCapacity;
            this.statusCapacity = statusCapacity;
        }

        /// <summary>Raised after membership, history, presence, current-room, or unread state changes.</summary>
        public event EventHandler<ChatroomStateChangedEventArgs>? Changed;

        /// <summary>Gets the room currently presented by the UI, or <see langword="null"/>.</summary>
        public string? CurrentRoom
        {
            get
            {
                lock (sync)
                {
                    return currentRoom;
                }
            }
        }

        /// <summary>Gets the number of rooms with unseen chat messages.</summary>
        public int UnreadRoomCount
        {
            get
            {
                lock (sync)
                {
                    return rooms.Values.Count(room => room.IsUnread);
                }
            }
        }

        /// <summary>Creates or replaces a room's membership snapshot.</summary>
        /// <param name="roomName">The non-empty room name.</param>
        /// <param name="members">The room's current members.</param>
        /// <param name="isPrivate">Whether the room is private.</param>
        /// <param name="owner">The room owner when known.</param>
        /// <param name="isConnected">Whether server membership is currently live.</param>
        public void SetMembership(
            string roomName,
            IEnumerable<ChatroomMemberSnapshot> members,
            bool isPrivate = false,
            string? owner = null,
            bool isConnected = true)
        {
            ValidateRoomName(roomName);
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                RoomState room = GetOrCreateRoom(roomName);
                room.IsJoined = true;
                room.IsConnected = isConnected;
                room.IsPrivate = isPrivate;
                room.Owner = owner;
                room.Members = members
                    .GroupBy(member => member.Username, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
                eventArgs = CreateEventArgs(ChatroomStateChange.MembershipChanged, roomName);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Adds or replaces a member in a room snapshot.</summary>
        public void AddOrUpdateMember(string roomName, ChatroomMemberSnapshot member)
        {
            ValidateRoomName(roomName);
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                RoomState room = GetOrCreateRoom(roomName);
                room.Members[member.Username] = member;
                eventArgs = CreateEventArgs(ChatroomStateChange.MembershipChanged, roomName);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Removes a member from a room snapshot.</summary>
        /// <returns><see langword="true"/> when a member was removed.</returns>
        public bool RemoveMember(string roomName, string username)
        {
            ValidateRoomName(roomName);
            ValidateUsername(username);
            ChatroomStateChangedEventArgs? eventArgs = null;
            bool removed;
            lock (sync)
            {
                removed = rooms.TryGetValue(roomName, out RoomState? room) && room.Members.Remove(username);
                if (removed)
                {
                    eventArgs = CreateEventArgs(ChatroomStateChange.MembershipChanged, roomName);
                }
            }

            if (eventArgs != null)
            {
                Changed?.Invoke(this, eventArgs);
            }

            return removed;
        }

        /// <summary>Updates a member's presence without mutating previously returned snapshots.</summary>
        /// <returns><see langword="true"/> when the member existed and was updated.</returns>
        public bool UpdateMemberPresence(string roomName, string username, UserPresence presence)
        {
            ValidateRoomName(roomName);
            ValidateUsername(username);
            ChatroomStateChangedEventArgs? eventArgs = null;
            bool updated = false;
            lock (sync)
            {
                if (rooms.TryGetValue(roomName, out RoomState? room)
                    && room.Members.TryGetValue(username, out ChatroomMemberSnapshot? member))
                {
                    updated = true;
                    room.Members[username] = member.WithPresence(presence);
                    eventArgs = CreateEventArgs(ChatroomStateChange.MemberPresenceChanged, roomName);
                }
            }

            if (eventArgs != null)
            {
                Changed?.Invoke(this, eventArgs);
            }

            return updated;
        }

        /// <summary>Sets whether a room currently has live server membership.</summary>
        public void SetConnected(string roomName, bool isConnected)
        {
            ValidateRoomName(roomName);
            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                RoomState room = GetOrCreateRoom(roomName);
                room.IsConnected = isConnected;
                eventArgs = CreateEventArgs(ChatroomStateChange.MembershipChanged, roomName);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Marks all joined rooms disconnected while retaining their histories and intended membership.</summary>
        public void MarkAllDisconnected()
        {
            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                foreach (RoomState room in rooms.Values)
                {
                    room.IsConnected = false;
                }

                eventArgs = new ChatroomStateChangedEventArgs(ChatroomStateChange.MembershipChanged, null, false);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Leaves a room and optionally discards its retained history.</summary>
        /// <param name="roomName">The room to leave.</param>
        /// <param name="clearHistory">Whether to remove the entire room snapshot.</param>
        /// <returns><see langword="true"/> when a room snapshot existed.</returns>
        public bool LeaveRoom(string roomName, bool clearHistory = false)
        {
            ValidateRoomName(roomName);
            ChatroomStateChangedEventArgs? eventArgs = null;
            bool existed;
            lock (sync)
            {
                existed = rooms.TryGetValue(roomName, out RoomState? room);
                if (existed)
                {
                    if (clearHistory)
                    {
                        rooms.Remove(roomName);
                    }
                    else
                    {
                        room!.IsJoined = false;
                        room.IsConnected = false;
                        room.Members.Clear();
                        room.IsUnread = false;
                    }

                    if (currentRoom == roomName)
                    {
                        currentRoom = null;
                    }

                    eventArgs = new ChatroomStateChangedEventArgs(
                        ChatroomStateChange.MembershipChanged,
                        roomName,
                        false);
                }
            }

            if (eventArgs != null)
            {
                Changed?.Invoke(this, eventArgs);
            }

            return existed;
        }

        /// <summary>Adds a chat message and updates same-author grouping and unread state.</summary>
        /// <param name="roomName">The room receiving the message.</param>
        /// <param name="message">The message to retain.</param>
        /// <param name="canMarkUnread">
        /// Whether a non-special message received outside the current room may mark the room unread.
        /// </param>
        public void AddMessage(string roomName, Message message, bool canMarkUnread = true)
        {
            ValidateRoomName(roomName);
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                RoomState room = GetOrCreateRoom(roomName);
                Message copy = MessageStateCopy.Clone(message);
                copy.SameAsLastUser = room.LastMessageUsername == copy.Username;
                room.LastMessageUsername = copy.Username;
                EnqueueCapped(room.Messages, copy, messageCapacity);
                if (canMarkUnread && currentRoom != roomName && copy.SpecialCode == SpecialMessageCode.None)
                {
                    room.IsUnread = true;
                }

                eventArgs = CreateEventArgs(ChatroomStateChange.MessageAdded, roomName);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Adds a bounded room membership/presence status message.</summary>
        public void AddStatusMessage(string roomName, StatusMessageUpdate statusMessage)
        {
            ValidateRoomName(roomName);
            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                RoomState room = GetOrCreateRoom(roomName);
                EnqueueCapped(room.StatusMessages, statusMessage, statusCapacity);
                eventArgs = CreateEventArgs(ChatroomStateChange.StatusMessageAdded, roomName);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Selects the currently visible room and clears that room's unread state.</summary>
        /// <param name="roomName">The room name, or <see langword="null"/> when no room is visible.</param>
        public void EnterRoom(string? roomName)
        {
            if (roomName != null)
            {
                ValidateRoomName(roomName);
            }

            ChatroomStateChangedEventArgs eventArgs;
            lock (sync)
            {
                currentRoom = roomName;
                if (roomName != null)
                {
                    GetOrCreateRoom(roomName).IsUnread = false;
                }

                eventArgs = new ChatroomStateChangedEventArgs(
                    ChatroomStateChange.CurrentRoomChanged,
                    roomName,
                    false);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Explicitly clears a room's unread state.</summary>
        public void MarkRead(string roomName)
        {
            ValidateRoomName(roomName);
            ChatroomStateChangedEventArgs? eventArgs = null;
            lock (sync)
            {
                if (rooms.TryGetValue(roomName, out RoomState? room) && room.IsUnread)
                {
                    room.IsUnread = false;
                    eventArgs = CreateEventArgs(ChatroomStateChange.UnreadStateChanged, roomName);
                }
            }

            if (eventArgs != null)
            {
                Changed?.Invoke(this, eventArgs);
            }
        }

        /// <summary>Gets a detached snapshot of one room.</summary>
        /// <returns>The snapshot, or <see langword="null"/> when the room is unknown.</returns>
        public ChatroomRoomSnapshot? GetRoomSnapshot(string roomName)
        {
            ValidateRoomName(roomName);
            lock (sync)
            {
                return rooms.TryGetValue(roomName, out RoomState? room) ? CreateSnapshot(roomName, room) : null;
            }
        }

        /// <summary>Gets detached snapshots for all known rooms ordered by room name.</summary>
        public IReadOnlyList<ChatroomRoomSnapshot> GetRoomSnapshots()
        {
            lock (sync)
            {
                return rooms
                    .OrderBy(room => room.Key, StringComparer.Ordinal)
                    .Select(room => CreateSnapshot(room.Key, room.Value))
                    .ToArray();
            }
        }

        private ChatroomStateChangedEventArgs CreateEventArgs(ChatroomStateChange change, string roomName) =>
            new(change, roomName, rooms.TryGetValue(roomName, out RoomState? room) && room.IsUnread);

        private ChatroomRoomSnapshot CreateSnapshot(string roomName, RoomState room) =>
            new(
                roomName,
                room.IsJoined,
                room.IsConnected,
                room.IsPrivate,
                room.Owner,
                room.IsUnread,
                room.Members.Values.OrderBy(member => member.Username, StringComparer.Ordinal).ToArray(),
                room.Messages.Select(MessageStateCopy.Clone).ToArray(),
                room.StatusMessages.ToArray());

        private RoomState GetOrCreateRoom(string roomName)
        {
            if (!rooms.TryGetValue(roomName, out RoomState? room))
            {
                room = new RoomState();
                rooms.Add(roomName, room);
            }

            return room;
        }

        private static void EnqueueCapped<T>(Queue<T> queue, T value, int capacity)
        {
            queue.Enqueue(value);
            while (queue.Count > capacity)
            {
                queue.Dequeue();
            }
        }

        private static void ValidateRoomName(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
            {
                throw new ArgumentException("A non-empty room name is required.", nameof(roomName));
            }
        }

        private static void ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("A non-empty user name is required.", nameof(username));
            }
        }

        private sealed class RoomState
        {
            public bool IsJoined { get; set; }
            public bool IsConnected { get; set; }
            public bool IsPrivate { get; set; }
            public string? Owner { get; set; }
            public bool IsUnread { get; set; }
            public string? LastMessageUsername { get; set; }
            public Dictionary<string, ChatroomMemberSnapshot> Members { get; set; } =
                new(StringComparer.Ordinal);
            public Queue<Message> Messages { get; } = new();
            public Queue<StatusMessageUpdate> StatusMessages { get; } = new();
        }
    }
}
