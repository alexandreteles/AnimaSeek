using Seeker;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Common.Messages
{
    /// <summary>Identifies a private-message state transition.</summary>
    public enum PrivateMessageStateChange
    {
        /// <summary>The active signed-in account changed.</summary>
        AccountChanged,

        /// <summary>A message was appended to a conversation.</summary>
        MessageAdded,

        /// <summary>A conversation's read position changed.</summary>
        ReadPositionChanged,

        /// <summary>A conversation was removed.</summary>
        ConversationRemoved,

        /// <summary>All conversations for the active account were removed.</summary>
        ConversationsCleared,
    }

    /// <summary>Provides a detached description of a private-message state transition.</summary>
    public sealed class PrivateMessageStateChangedEventArgs : EventArgs
    {
        /// <summary>Creates event data for a private-message state transition.</summary>
        public PrivateMessageStateChangedEventArgs(
            PrivateMessageStateChange change,
            string account,
            string? conversation,
            int totalUnreadCount)
        {
            Change = change;
            Account = account;
            Conversation = conversation;
            TotalUnreadCount = totalUnreadCount;
        }

        /// <summary>Gets the kind of transition.</summary>
        public PrivateMessageStateChange Change { get; }

        /// <summary>Gets the active account after the transition.</summary>
        public string Account { get; }

        /// <summary>Gets the affected conversation, or <see langword="null"/> for an account-wide transition.</summary>
        public string? Conversation { get; }

        /// <summary>Gets the active account's total unread-message count after the transition.</summary>
        public int TotalUnreadCount { get; }
    }

    /// <summary>Contains data removed from one conversation so a caller can offer undo.</summary>
    public sealed class RemovedPrivateMessageConversation
    {
        /// <summary>Creates a detached removed-conversation value.</summary>
        public RemovedPrivateMessageConversation(string username, IReadOnlyList<Message> messages, int lastReadCount)
        {
            Username = username;
            Messages = messages;
            LastReadCount = lastReadCount;
        }

        /// <summary>Gets the remote user name.</summary>
        public string Username { get; }

        /// <summary>Gets a detached message snapshot.</summary>
        public IReadOnlyList<Message> Messages { get; }

        /// <summary>Gets the removed last-read count.</summary>
        public int LastReadCount { get; }
    }

    /// <summary>
    /// Owns private-message histories and read positions without platform or UI dependencies.
    /// </summary>
    /// <remarks>
    /// All operations are thread-safe. Events are raised after the internal lock is released and may execute on
    /// the calling thread. When a configured history limit drops an old message, the read position is shifted by
    /// the same amount so unread counts remain correct.
    /// </remarks>
    public sealed class PrivateMessageSessionState
    {
        /// <summary>The default maximum number of messages retained per conversation.</summary>
        public const int DefaultConversationCapacity = 1_000;

        private readonly object sync = new();
        private readonly int conversationCapacity;
        private ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> rootMessages;
        private ConcurrentDictionary<string, ConcurrentDictionary<string, int>> rootLastReadCounts;
        private ConcurrentDictionary<string, List<Message>> currentMessages = new();
        private ConcurrentDictionary<string, int> currentLastReadCounts = new();
        private string currentAccount = string.Empty;

        /// <summary>Creates an empty private-message session.</summary>
        /// <param name="conversationCapacity">The positive maximum messages retained per conversation.</param>
        public PrivateMessageSessionState(int conversationCapacity = DefaultConversationCapacity)
            : this(
                new ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>>(),
                new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>(),
                conversationCapacity)
        {
        }

        /// <summary>Creates a private-message session from restored persistence roots.</summary>
        /// <param name="messages">Messages grouped by account and remote user.</param>
        /// <param name="lastReadCounts">Last-read counts grouped by account and remote user.</param>
        /// <param name="conversationCapacity">The positive maximum messages retained per conversation.</param>
        public PrivateMessageSessionState(
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> messages,
            ConcurrentDictionary<string, ConcurrentDictionary<string, int>> lastReadCounts,
            int conversationCapacity = DefaultConversationCapacity)
        {
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            if (lastReadCounts == null)
            {
                throw new ArgumentNullException(nameof(lastReadCounts));
            }

            if (conversationCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(conversationCapacity),
                    conversationCapacity,
                    "Conversation capacity must be positive.");
            }

            this.conversationCapacity = conversationCapacity;
            rootMessages = CopyMessageRoots(messages, conversationCapacity, out var removedByConversation);
            rootLastReadCounts = CopyReadRoots(lastReadCounts, rootMessages, removedByConversation);
        }

        /// <summary>Raised after an account, history, or read-position transition.</summary>
        public event EventHandler<PrivateMessageStateChangedEventArgs>? Changed;

        /// <summary>Gets the current account name, or an empty string when signed out.</summary>
        public string CurrentAccount
        {
            get
            {
                lock (sync)
                {
                    return currentAccount;
                }
            }
        }

        /// <summary>Switches the active account and creates its roots when necessary.</summary>
        /// <param name="account">The account name, or <see langword="null"/> to select signed-out state.</param>
        public void SwitchAccount(string? account)
        {
            string normalized = account ?? string.Empty;
            PrivateMessageStateChangedEventArgs? eventArgs;
            lock (sync)
            {
                if (normalized == currentAccount)
                {
                    return;
                }

                currentAccount = normalized;
                if (normalized.Length == 0)
                {
                    currentMessages = new ConcurrentDictionary<string, List<Message>>();
                    currentLastReadCounts = new ConcurrentDictionary<string, int>();
                }
                else
                {
                    currentMessages = rootMessages.GetOrAdd(
                        normalized,
                        static _ => new ConcurrentDictionary<string, List<Message>>());
                    currentLastReadCounts = rootLastReadCounts.GetOrAdd(
                        normalized,
                        static _ => new ConcurrentDictionary<string, int>());
                }

                NormalizeReadCounts(currentMessages, currentLastReadCounts);
                eventArgs = CreateEventArgs(PrivateMessageStateChange.AccountChanged, null);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Appends a message and optionally marks the resulting conversation as read.</summary>
        /// <param name="username">The non-empty remote user name.</param>
        /// <param name="message">The message to append.</param>
        /// <param name="markRead">Whether the conversation is visible and should remain fully read.</param>
        public void AddMessage(string username, Message message, bool markRead = false)
        {
            ValidateUsername(username);
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            PrivateMessageStateChangedEventArgs eventArgs;
            lock (sync)
            {
                List<Message> messages = currentMessages.GetOrAdd(username, static _ => new List<Message>());
                messages.Add(MessageStateCopy.Clone(message));
                int removed = TrimToCapacity(messages, conversationCapacity);
                if (removed > 0 && currentLastReadCounts.TryGetValue(username, out int lastRead))
                {
                    currentLastReadCounts[username] = Math.Max(0, lastRead - removed);
                }

                if (markRead)
                {
                    currentLastReadCounts[username] = messages.Count;
                }

                eventArgs = CreateEventArgs(PrivateMessageStateChange.MessageAdded, username);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Marks a conversation read through its current last message.</summary>
        /// <param name="username">The remote user name.</param>
        public void MarkRead(string username)
        {
            ValidateUsername(username);
            PrivateMessageStateChangedEventArgs? eventArgs = null;
            lock (sync)
            {
                if (currentMessages.TryGetValue(username, out List<Message>? messages) &&
                    GetUnreadCountUnsafe(username) > 0)
                {
                    currentLastReadCounts[username] = messages.Count;
                    eventArgs = CreateEventArgs(PrivateMessageStateChange.ReadPositionChanged, username);
                }
            }

            if (eventArgs != null)
            {
                Changed?.Invoke(this, eventArgs);
            }
        }

        /// <summary>Marks every conversation in the active account as read.</summary>
        public void MarkAllRead()
        {
            PrivateMessageStateChangedEventArgs eventArgs;
            lock (sync)
            {
                foreach (KeyValuePair<string, List<Message>> conversation in currentMessages)
                {
                    currentLastReadCounts[conversation.Key] = conversation.Value.Count;
                }

                eventArgs = CreateEventArgs(PrivateMessageStateChange.ReadPositionChanged, null);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Gets the unread count for one conversation.</summary>
        /// <param name="username">The remote user name.</param>
        /// <returns>A non-negative unread-message count.</returns>
        public int GetUnreadCount(string username)
        {
            ValidateUsername(username);
            lock (sync)
            {
                return GetUnreadCountUnsafe(username);
            }
        }

        /// <summary>Gets the total unread count for the active account.</summary>
        /// <returns>The sum of non-negative conversation unread counts.</returns>
        public int GetTotalUnreadCount()
        {
            lock (sync)
            {
                return GetTotalUnreadCountUnsafe();
            }
        }

        /// <summary>Gets a detached chronological snapshot of one conversation.</summary>
        /// <param name="username">The remote user name.</param>
        /// <returns>The conversation messages, or an empty list when the conversation is absent.</returns>
        public IReadOnlyList<Message> GetConversationSnapshot(string username)
        {
            ValidateUsername(username);
            lock (sync)
            {
                return currentMessages.TryGetValue(username, out List<Message>? messages)
                    ? messages.Select(MessageStateCopy.Clone).ToArray()
                    : Array.Empty<Message>();
            }
        }

        /// <summary>Removes one conversation and returns detached data suitable for undo.</summary>
        /// <param name="username">The remote user name.</param>
        /// <returns>The removed data, or <see langword="null"/> when no conversation exists.</returns>
        public RemovedPrivateMessageConversation? RemoveConversation(string username)
        {
            ValidateUsername(username);
            RemovedPrivateMessageConversation? removed = null;
            PrivateMessageStateChangedEventArgs? eventArgs = null;
            lock (sync)
            {
                if (currentMessages.TryRemove(username, out List<Message>? messages))
                {
                    currentLastReadCounts.TryRemove(username, out int lastReadCount);
                    removed = new RemovedPrivateMessageConversation(
                        username,
                        messages.Select(MessageStateCopy.Clone).ToArray(),
                        lastReadCount);
                    eventArgs = CreateEventArgs(PrivateMessageStateChange.ConversationRemoved, username);
                }
            }

            if (eventArgs != null)
            {
                Changed?.Invoke(this, eventArgs);
            }

            return removed;
        }

        /// <summary>Restores a conversation previously returned by <see cref="RemoveConversation"/>.</summary>
        /// <param name="conversation">The removed conversation data.</param>
        public void RestoreConversation(RemovedPrivateMessageConversation conversation)
        {
            if (conversation == null)
            {
                throw new ArgumentNullException(nameof(conversation));
            }

            PrivateMessageStateChangedEventArgs eventArgs;
            lock (sync)
            {
                var messages = conversation.Messages.Select(MessageStateCopy.Clone).ToList();
                int removed = TrimToCapacity(messages, conversationCapacity);
                currentMessages[conversation.Username] = messages;
                currentLastReadCounts[conversation.Username] = Math.Min(
                    messages.Count,
                    Math.Max(0, conversation.LastReadCount - removed));
                eventArgs = CreateEventArgs(PrivateMessageStateChange.MessageAdded, conversation.Username);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Removes all conversations and read positions for the active account.</summary>
        public void ClearConversations()
        {
            PrivateMessageStateChangedEventArgs eventArgs;
            lock (sync)
            {
                currentMessages.Clear();
                currentLastReadCounts.Clear();
                eventArgs = CreateEventArgs(PrivateMessageStateChange.ConversationsCleared, null);
            }

            Changed?.Invoke(this, eventArgs);
        }

        /// <summary>Exports detached message roots in the canonical persistence shape.</summary>
        public ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> ExportMessageRoots()
        {
            lock (sync)
            {
                return CopyMessageRoots(rootMessages, conversationCapacity, out _);
            }
        }

        /// <summary>Exports detached last-read roots in the canonical persistence shape.</summary>
        public ConcurrentDictionary<string, ConcurrentDictionary<string, int>> ExportLastReadRoots()
        {
            lock (sync)
            {
                return new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>(
                    rootLastReadCounts.Select(account =>
                        new KeyValuePair<string, ConcurrentDictionary<string, int>>(
                            account.Key,
                            new ConcurrentDictionary<string, int>(account.Value))));
            }
        }

        private PrivateMessageStateChangedEventArgs CreateEventArgs(
            PrivateMessageStateChange change,
            string? conversation) =>
            new(change, currentAccount, conversation, GetTotalUnreadCountUnsafe());

        private int GetUnreadCountUnsafe(string username)
        {
            if (!currentMessages.TryGetValue(username, out List<Message>? messages))
            {
                return 0;
            }

            int lastRead = currentLastReadCounts.TryGetValue(username, out int value) ? value : 0;
            return Math.Max(messages.Count - Math.Max(lastRead, 0), 0);
        }

        private int GetTotalUnreadCountUnsafe() => currentMessages.Keys.Sum(GetUnreadCountUnsafe);

        private static int TrimToCapacity(List<Message> messages, int capacity)
        {
            int removeCount = Math.Max(0, messages.Count - capacity);
            if (removeCount > 0)
            {
                messages.RemoveRange(0, removeCount);
            }

            return removeCount;
        }

        private static ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> CopyMessageRoots(
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> source,
            int capacity,
            out Dictionary<string, Dictionary<string, int>> removedByConversation)
        {
            removedByConversation = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var copy = new ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>>();
            foreach (KeyValuePair<string, ConcurrentDictionary<string, List<Message>>> account in source)
            {
                var accountCopy = new ConcurrentDictionary<string, List<Message>>();
                var removedForAccount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, List<Message>> conversation in account.Value)
                {
                    var messages = conversation.Value.Select(MessageStateCopy.Clone).ToList();
                    removedForAccount[conversation.Key] = TrimToCapacity(messages, capacity);
                    accountCopy[conversation.Key] = messages;
                }

                copy[account.Key] = accountCopy;
                removedByConversation[account.Key] = removedForAccount;
            }

            return copy;
        }

        private static ConcurrentDictionary<string, ConcurrentDictionary<string, int>> CopyReadRoots(
            ConcurrentDictionary<string, ConcurrentDictionary<string, int>> source,
            ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> messages,
            IReadOnlyDictionary<string, Dictionary<string, int>> removedByConversation)
        {
            var copy = new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();
            foreach (string account in messages.Keys.Union(source.Keys, StringComparer.Ordinal))
            {
                var accountCopy = new ConcurrentDictionary<string, int>();
                if (source.TryGetValue(account, out ConcurrentDictionary<string, int>? sourceCounts))
                {
                    foreach (KeyValuePair<string, int> count in sourceCounts)
                    {
                        int removed = removedByConversation.TryGetValue(account, out Dictionary<string, int>? removedMap)
                            && removedMap.TryGetValue(count.Key, out int value)
                            ? value
                            : 0;
                        accountCopy[count.Key] = Math.Max(0, count.Value - removed);
                    }
                }

                if (messages.TryGetValue(account, out ConcurrentDictionary<string, List<Message>>? conversations))
                {
                    NormalizeReadCounts(conversations, accountCopy);
                }

                copy[account] = accountCopy;
            }

            return copy;
        }

        private static void NormalizeReadCounts(
            ConcurrentDictionary<string, List<Message>> messages,
            ConcurrentDictionary<string, int> counts)
        {
            foreach (KeyValuePair<string, int> count in counts.ToArray())
            {
                int messageCount = messages.TryGetValue(count.Key, out List<Message>? conversation)
                    ? conversation.Count
                    : 0;
                counts[count.Key] = Math.Min(messageCount, Math.Max(0, count.Value));
            }
        }

        private static void ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("A non-empty user name is required.", nameof(username));
            }
        }
    }

    internal static class MessageStateCopy
    {
        public static Message Clone(Message source)
        {
            var copy = new Message(
                source.Username,
                source.Id,
                source.Replayed,
                source.UtcDateTime,
                source.MessageText,
                source.FromMe,
                source.SentMsgStatus,
                source.SpecialCode,
                source.SameAsLastUser);
            copy.LocalDateTime = source.LocalDateTime;
            return copy;
        }
    }
}
