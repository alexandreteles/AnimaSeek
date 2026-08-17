using Common.Messages;
using NUnit.Framework;
using Seeker;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class PortableSessionStateTests
    {
        [Test]
        public void PrivateMessages_PreserveUnreadCountsWhenBoundedHistoryDropsOldEntries()
        {
            var state = new PrivateMessageSessionState(conversationCapacity: 2);
            var changes = new List<PrivateMessageStateChangedEventArgs>();
            state.Changed += (_, change) => changes.Add(change);
            state.SwitchAccount("local");
            state.AddMessage("remote", CreateMessage("remote", 1, "one"));
            state.AddMessage("remote", CreateMessage("remote", 2, "two"));
            state.MarkRead("remote");
            state.AddMessage("remote", CreateMessage("remote", 3, "three"));

            IReadOnlyList<Message> snapshot = state.GetConversationSnapshot("remote");
            var restored = new PrivateMessageSessionState(
                state.ExportMessageRoots(),
                state.ExportLastReadRoots(),
                conversationCapacity: 2);
            restored.SwitchAccount("local");

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Select(message => message.MessageText), Is.EqualTo(new[] { "two", "three" }));
                Assert.That(state.GetUnreadCount("remote"), Is.EqualTo(1));
                Assert.That(state.GetTotalUnreadCount(), Is.EqualTo(1));
                Assert.That(restored.GetUnreadCount("remote"), Is.EqualTo(1));
                Assert.That(changes.Last().TotalUnreadCount, Is.EqualTo(1));
            });

            snapshot[0].SentMsgStatus = SentStatus.Failed;
            Assert.That(state.GetConversationSnapshot("remote")[0].SentMsgStatus, Is.EqualTo(SentStatus.None));
        }

        [Test]
        public void PrivateMessages_RemoveAndRestoreConversation_ReturnDetachedUndoData()
        {
            var state = new PrivateMessageSessionState();
            state.SwitchAccount("local");
            state.AddMessage("remote", CreateMessage("remote", 1, "hello"));
            state.MarkRead("remote");

            RemovedPrivateMessageConversation removed = state.RemoveConversation("remote")!;
            state.RestoreConversation(removed);

            Assert.Multiple(() =>
            {
                Assert.That(removed.Messages.Single().MessageText, Is.EqualTo("hello"));
                Assert.That(state.GetConversationSnapshot("remote").Single().MessageText, Is.EqualTo("hello"));
                Assert.That(state.GetUnreadCount("remote"), Is.Zero);
            });
        }

        [Test]
        public void PrivateMessages_MarkRead_IsIdempotentAfterUnreadMessagesAreCleared()
        {
            var state = new PrivateMessageSessionState();
            int readPositionChanges = 0;
            state.Changed += (_, change) =>
            {
                if (change.Change == PrivateMessageStateChange.ReadPositionChanged)
                {
                    readPositionChanges++;
                }
            };
            state.SwitchAccount("local");
            state.AddMessage("remote", CreateMessage("remote", 1, "hello"));

            state.MarkRead("remote");
            state.MarkRead("remote");

            Assert.Multiple(() =>
            {
                Assert.That(state.GetUnreadCount("remote"), Is.Zero);
                Assert.That(readPositionChanges, Is.EqualTo(1));
            });
        }

        [Test]
        public void Chatrooms_ExposeDetachedMembershipAndBoundedHistorySnapshots()
        {
            var state = new ChatroomSessionState(messageCapacity: 2, statusCapacity: 2);
            var changes = new List<ChatroomStateChangedEventArgs>();
            state.Changed += (_, change) => changes.Add(change);
            state.SetMembership(
                "music",
                new[]
                {
                    new ChatroomMemberSnapshot("zoe", UserPresence.Away),
                    new ChatroomMemberSnapshot("alice", UserPresence.Online, isOperator: true),
                },
                isPrivate: true,
                owner: "alice");
            state.AddMessage("music", CreateMessage("alice", 1, "one"));
            state.AddMessage("music", CreateMessage("alice", 2, "two"));
            state.AddMessage("music", CreateMessage("zoe", 3, "three"));
            state.AddStatusMessage(
                "music",
                new StatusMessageUpdate(StatusMessageType.Joined, "one", DateTime.UtcNow));
            state.AddStatusMessage(
                "music",
                new StatusMessageUpdate(StatusMessageType.Joined, "two", DateTime.UtcNow));
            state.AddStatusMessage(
                "music",
                new StatusMessageUpdate(StatusMessageType.Left, "three", DateTime.UtcNow));
            Assert.That(state.UpdateMemberPresence("music", "zoe", UserPresence.Online), Is.True);

            ChatroomRoomSnapshot snapshot = state.GetRoomSnapshot("music")!;

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsJoined, Is.True);
                Assert.That(snapshot.IsConnected, Is.True);
                Assert.That(snapshot.IsPrivate, Is.True);
                Assert.That(snapshot.Owner, Is.EqualTo("alice"));
                Assert.That(snapshot.IsUnread, Is.True);
                Assert.That(snapshot.Members.Select(member => member.Username), Is.EqualTo(new[] { "alice", "zoe" }));
                Assert.That(snapshot.Members.Single(member => member.Username == "zoe").Presence, Is.EqualTo(UserPresence.Online));
                Assert.That(snapshot.Messages.Select(message => message.MessageText), Is.EqualTo(new[] { "two", "three" }));
                Assert.That(snapshot.Messages[0].SameAsLastUser, Is.True);
                Assert.That(snapshot.Messages[1].SameAsLastUser, Is.False);
                Assert.That(snapshot.StatusMessages.Select(status => status.Username), Is.EqualTo(new[] { "two", "three" }));
                Assert.That(state.UnreadRoomCount, Is.EqualTo(1));
                Assert.That(changes, Is.Not.Empty);
            });

            snapshot.Messages[0].SentMsgStatus = SentStatus.Failed;
            state.EnterRoom("music");
            Assert.Multiple(() =>
            {
                Assert.That(state.UnreadRoomCount, Is.Zero);
                Assert.That(state.GetRoomSnapshot("music")!.IsUnread, Is.False);
                Assert.That(state.GetRoomSnapshot("music")!.Messages[0].SentMsgStatus, Is.EqualTo(SentStatus.None));
            });
        }

        [Test]
        public void Chatrooms_DisconnectAndLeave_PreserveOrDiscardHistoryAsRequested()
        {
            var state = new ChatroomSessionState();
            state.SetMembership("room", Array.Empty<ChatroomMemberSnapshot>());
            state.AddMessage("room", CreateMessage("remote", 1, "retained"));

            state.MarkAllDisconnected();
            ChatroomRoomSnapshot disconnected = state.GetRoomSnapshot("room")!;
            bool left = state.LeaveRoom("room", clearHistory: false);
            ChatroomRoomSnapshot retained = state.GetRoomSnapshot("room")!;
            bool removed = state.LeaveRoom("room", clearHistory: true);

            Assert.Multiple(() =>
            {
                Assert.That(disconnected.IsConnected, Is.False);
                Assert.That(left, Is.True);
                Assert.That(retained.IsJoined, Is.False);
                Assert.That(retained.Messages.Single().MessageText, Is.EqualTo("retained"));
                Assert.That(removed, Is.True);
                Assert.That(state.GetRoomSnapshot("room"), Is.Null);
            });
        }

        private static Message CreateMessage(string username, int id, string text) =>
            new(
                username,
                id,
                false,
                DateTime.UtcNow,
                text,
                false,
                SentStatus.None,
                SpecialMessageCode.None,
                false);
    }
}
