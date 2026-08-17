using NUnit.Framework;
using Seeker.Services;
using System;

namespace UnitTestCommon
{
    [TestFixture]
    public class NotificationModelsTests
    {
        [Test]
        public void ConversationNotifications_UseStableSemanticIdentities()
        {
            var firstPrivateMessage = new PrivateMessageNotification("alice", "first");
            var secondPrivateMessage = new PrivateMessageNotification("alice", "second");
            var roomMessage = new ChatroomMessageNotification("music", "alice", "hello");

            Assert.That(secondPrivateMessage.Id, Is.EqualTo(firstPrivateMessage.Id));
            Assert.That(firstPrivateMessage.Kind, Is.EqualTo(NotificationKind.PrivateMessage));
            Assert.That(roomMessage.Id, Is.EqualTo(NotificationId.ForChatroom("music")));
            Assert.That(roomMessage.Kind, Is.EqualTo(NotificationKind.ChatroomMessage));
        }

        [Test]
        public void CompletedFolderIdentity_DoesNotCollideWhenComponentBoundariesDiffer()
        {
            NotificationId first = NotificationId.ForCompletedFolder("ab", "c");
            NotificationId second = NotificationId.ForCompletedFolder("a", "bc");

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void PrivateMessageDeliveryState_ControlsDirectReplyAvailability()
        {
            var received = new PrivateMessageNotification("alice", "hello");
            var failedReply = new PrivateMessageNotification(
                "alice",
                "reply",
                PrivateMessageDeliveryState.Failed,
                "Cannot connect");

            Assert.That(received.AllowsReply, Is.True);
            Assert.That(failedReply.AllowsReply, Is.False);
            Assert.That(failedReply.FailureReason, Is.EqualTo("Cannot connect"));
        }

        [Test]
        public void WishlistHit_RejectsEmptyResultSet()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WishlistHitNotification(42, "rare album", 0));
        }

        [TestCase(-1L, 1000L, 1.0)]
        [TestCase(0L, -1L, 1.0)]
        [TestCase(0L, 1000L, -1.0)]
        public void TransferState_RejectsInvalidProgressValues(
            long bytesTransferred,
            long totalBytes,
            double averageBytesPerSecond)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TransferStateNotification(
                    "transfer",
                    "alice",
                    "file.mp3",
                    NotificationTransferDirection.Download,
                    NotificationTransferState.InProgress,
                    bytesTransferred,
                    totalBytes,
                    averageBytesPerSecond));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void NotificationIdentity_RejectsEmptyScope(string scope)
        {
            Assert.Throws<ArgumentException>(() =>
                new NotificationId(NotificationKind.UserOnline, scope));
        }
    }
}
