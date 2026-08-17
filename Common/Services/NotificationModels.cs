using System;
using System.Globalization;
using System.Text;

namespace Seeker.Services
{
    /// <summary>
    /// Identifies the user-facing behavior and settings category for a notification.
    /// </summary>
    public enum NotificationKind
    {
        /// <summary>A private Soulseek message.</summary>
        PrivateMessage = 0,

        /// <summary>A message in a joined chatroom.</summary>
        ChatroomMessage = 1,

        /// <summary>One or more new search results for a wishlist entry.</summary>
        WishlistHit = 2,

        /// <summary>Completion of every download in a folder.</summary>
        FolderCompleted = 3,

        /// <summary>A watched user becoming online.</summary>
        UserOnline = 4,

        /// <summary>A material state or progress change for a download or upload.</summary>
        TransferState = 5,
    }

    /// <summary>
    /// Provides a stable identity for posting, updating, and canceling a semantic notification.
    /// </summary>
    public readonly record struct NotificationId
    {
        /// <summary>
        /// Initializes a new notification identity.
        /// </summary>
        /// <param name="kind">The notification's semantic category.</param>
        /// <param name="scope">A non-empty identity unique within <paramref name="kind"/>.</param>
        /// <exception cref="ArgumentException"><paramref name="scope"/> is empty or whitespace.</exception>
        public NotificationId(NotificationKind kind, string scope)
        {
            Kind = kind;
            Scope = SemanticNotification.RequireText(scope, nameof(scope));
        }

        /// <summary>Gets the notification's semantic category.</summary>
        public NotificationKind Kind { get; }

        /// <summary>Gets the opaque identity unique within <see cref="Kind"/>.</summary>
        public string Scope { get; }

        /// <summary>Creates the conversation identity for private messages with a user.</summary>
        /// <param name="username">The remote Soulseek username.</param>
        /// <returns>An identity shared by private-message updates for that conversation.</returns>
        /// <exception cref="ArgumentException"><paramref name="username"/> is empty or whitespace.</exception>
        public static NotificationId ForPrivateMessage(string username) =>
            new NotificationId(NotificationKind.PrivateMessage, username);

        /// <summary>Creates the conversation identity for a chatroom.</summary>
        /// <param name="roomName">The Soulseek room name.</param>
        /// <returns>An identity shared by chatroom-message updates for that room.</returns>
        /// <exception cref="ArgumentException"><paramref name="roomName"/> is empty or whitespace.</exception>
        public static NotificationId ForChatroom(string roomName) =>
            new NotificationId(NotificationKind.ChatroomMessage, roomName);

        /// <summary>Creates the identity for a wishlist search tab.</summary>
        /// <param name="searchId">The stable search-tab identifier used for navigation.</param>
        /// <returns>An identity shared by wishlist updates for that search tab.</returns>
        public static NotificationId ForWishlist(int searchId) =>
            new NotificationId(NotificationKind.WishlistHit, searchId.ToString(CultureInfo.InvariantCulture));

        /// <summary>Creates the identity for a completed folder from a user.</summary>
        /// <param name="username">The remote Soulseek username.</param>
        /// <param name="folderName">The completed folder's display name.</param>
        /// <returns>An identity unique to the user and folder pair.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="username"/> or <paramref name="folderName"/> is empty or whitespace.
        /// </exception>
        public static NotificationId ForCompletedFolder(string username, string folderName) =>
            new NotificationId(
                NotificationKind.FolderCompleted,
                ComposeScope(
                    SemanticNotification.RequireText(username, nameof(username)),
                    SemanticNotification.RequireText(folderName, nameof(folderName))));

        /// <summary>Creates the online-presence identity for a user.</summary>
        /// <param name="username">The watched Soulseek username.</param>
        /// <returns>An identity shared by presence updates for that user.</returns>
        /// <exception cref="ArgumentException"><paramref name="username"/> is empty or whitespace.</exception>
        public static NotificationId ForUserOnline(string username) =>
            new NotificationId(NotificationKind.UserOnline, username);

        /// <summary>Creates the identity for a transfer.</summary>
        /// <param name="transferId">The stable identifier assigned by the transfer layer.</param>
        /// <returns>An identity shared by state and progress updates for that transfer.</returns>
        /// <exception cref="ArgumentException"><paramref name="transferId"/> is empty or whitespace.</exception>
        public static NotificationId ForTransfer(string transferId) =>
            new NotificationId(NotificationKind.TransferState, transferId);

        /// <summary>Returns a stable diagnostic representation of this identity.</summary>
        /// <returns>The notification kind and opaque scope separated by a colon.</returns>
        public override string ToString() => $"{Kind}:{Scope}";

        private static string ComposeScope(params string[] values)
        {
            var builder = new StringBuilder();

            foreach (string value in values)
            {
                builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
                builder.Append(':');
                builder.Append(value);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Base class for notification data expressed in application terms rather than platform presentation terms.
    /// </summary>
    public abstract class SemanticNotification
    {
        /// <summary>Initializes a semantic notification with its stable identity.</summary>
        /// <param name="id">The identity used for posting, updating, and canceling the notification.</param>
        protected SemanticNotification(NotificationId id)
        {
            Id = id;
        }

        /// <summary>Gets the stable identity used for posting, updating, and canceling this notification.</summary>
        public NotificationId Id { get; }

        /// <summary>Gets the notification's semantic category.</summary>
        public NotificationKind Kind => Id.Kind;

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A notification value cannot be empty or whitespace.", parameterName);
            }

            return value;
        }
    }

    /// <summary>Describes the delivery state represented by a private-message notification.</summary>
    public enum PrivateMessageDeliveryState
    {
        /// <summary>A new message received from the remote user.</summary>
        Received = 0,

        /// <summary>An outgoing direct reply that has not completed.</summary>
        Sending = 1,

        /// <summary>An outgoing direct reply that was delivered to the server.</summary>
        Sent = 2,

        /// <summary>An outgoing direct reply that could not be sent.</summary>
        Failed = 3,
    }

    /// <summary>Describes a private-message notification and optional direct-reply state.</summary>
    public sealed class PrivateMessageNotification : SemanticNotification
    {
        /// <summary>Initializes a private-message notification.</summary>
        /// <param name="username">The remote Soulseek username.</param>
        /// <param name="messageText">The message text.</param>
        /// <param name="deliveryState">Whether the message was received or is an outgoing reply state.</param>
        /// <param name="failureReason">A user-facing failure reason when <paramref name="deliveryState"/> is failed.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="username"/> or <paramref name="messageText"/> is empty or whitespace.
        /// </exception>
        public PrivateMessageNotification(
            string username,
            string messageText,
            PrivateMessageDeliveryState deliveryState = PrivateMessageDeliveryState.Received,
            string? failureReason = null)
            : base(NotificationId.ForPrivateMessage(username))
        {
            Username = RequireText(username, nameof(username));
            MessageText = RequireText(messageText, nameof(messageText));
            DeliveryState = deliveryState;
            FailureReason = failureReason;
        }

        /// <summary>Gets the remote Soulseek username.</summary>
        public string Username { get; }

        /// <summary>Gets the message text.</summary>
        public string MessageText { get; }

        /// <summary>Gets the message's incoming or outgoing delivery state.</summary>
        public PrivateMessageDeliveryState DeliveryState { get; }

        /// <summary>Gets the user-facing direct-reply failure reason, when one is available.</summary>
        public string? FailureReason { get; }

        /// <summary>Gets whether the platform should offer a direct-reply action.</summary>
        public bool AllowsReply => DeliveryState == PrivateMessageDeliveryState.Received;
    }

    /// <summary>Describes a message received in a chatroom.</summary>
    public sealed class ChatroomMessageNotification : SemanticNotification
    {
        /// <summary>Initializes a chatroom-message notification.</summary>
        /// <param name="roomName">The Soulseek room name.</param>
        /// <param name="username">The message sender's Soulseek username.</param>
        /// <param name="messageText">The message text.</param>
        /// <exception cref="ArgumentException">Any argument is empty or whitespace.</exception>
        public ChatroomMessageNotification(string roomName, string username, string messageText)
            : base(NotificationId.ForChatroom(roomName))
        {
            RoomName = RequireText(roomName, nameof(roomName));
            Username = RequireText(username, nameof(username));
            MessageText = RequireText(messageText, nameof(messageText));
        }

        /// <summary>Gets the Soulseek room name.</summary>
        public string RoomName { get; }

        /// <summary>Gets the message sender's Soulseek username.</summary>
        public string Username { get; }

        /// <summary>Gets the message text.</summary>
        public string MessageText { get; }
    }

    /// <summary>Describes new results found for a wishlist entry.</summary>
    public sealed class WishlistHitNotification : SemanticNotification
    {
        /// <summary>Initializes a wishlist-hit notification.</summary>
        /// <param name="searchId">The stable search-tab identifier used for navigation.</param>
        /// <param name="searchTerm">The wishlist search term.</param>
        /// <param name="newResultCount">The positive number of newly discovered results.</param>
        /// <exception cref="ArgumentException"><paramref name="searchTerm"/> is empty or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="newResultCount"/> is not positive.</exception>
        public WishlistHitNotification(int searchId, string searchTerm, int newResultCount)
            : base(NotificationId.ForWishlist(searchId))
        {
            if (newResultCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newResultCount),
                    newResultCount,
                    "A wishlist notification requires at least one new result.");
            }

            SearchId = searchId;
            SearchTerm = RequireText(searchTerm, nameof(searchTerm));
            NewResultCount = newResultCount;
        }

        /// <summary>Gets the stable search-tab identifier used for navigation.</summary>
        public int SearchId { get; }

        /// <summary>Gets the wishlist search term.</summary>
        public string SearchTerm { get; }

        /// <summary>Gets the number of newly discovered results.</summary>
        public int NewResultCount { get; }
    }

    /// <summary>Describes completion of every download in a folder.</summary>
    public sealed class FolderCompletedNotification : SemanticNotification
    {
        /// <summary>Initializes a completed-folder notification.</summary>
        /// <param name="folderName">The completed folder's display name.</param>
        /// <param name="username">The remote Soulseek username.</param>
        /// <param name="routeTransferId">
        /// An optional opaque transfer-row identity used only for navigation after a tap. The notification itself
        /// retains its stable user/folder identity so later folder completions replace the same delivered alert.
        /// </param>
        /// <exception cref="ArgumentException">Either argument is empty or whitespace.</exception>
        public FolderCompletedNotification(
            string folderName,
            string username,
            string? routeTransferId = null)
            : base(NotificationId.ForCompletedFolder(username, folderName))
        {
            FolderName = RequireText(folderName, nameof(folderName));
            Username = RequireText(username, nameof(username));
            RouteTransferId = string.IsNullOrWhiteSpace(routeTransferId)
                ? null
                : routeTransferId;
        }

        /// <summary>Gets the completed folder's display name.</summary>
        public string FolderName { get; }

        /// <summary>Gets the remote Soulseek username.</summary>
        public string Username { get; }

        /// <summary>Gets the optional opaque completed-transfer row to reveal after a notification tap.</summary>
        public string? RouteTransferId { get; }
    }

    /// <summary>Describes a watched user becoming online.</summary>
    public sealed class UserOnlineNotification : SemanticNotification
    {
        /// <summary>Initializes a user-online notification.</summary>
        /// <param name="username">The watched Soulseek username.</param>
        /// <exception cref="ArgumentException"><paramref name="username"/> is empty or whitespace.</exception>
        public UserOnlineNotification(string username)
            : base(NotificationId.ForUserOnline(username))
        {
            Username = RequireText(username, nameof(username));
        }

        /// <summary>Gets the watched Soulseek username.</summary>
        public string Username { get; }
    }

    /// <summary>Identifies whether a transfer notification represents a download or upload.</summary>
    public enum NotificationTransferDirection
    {
        /// <summary>A file being downloaded from a remote user.</summary>
        Download = 0,

        /// <summary>A file being uploaded to a remote user.</summary>
        Upload = 1,
    }

    /// <summary>Describes the portable lifecycle states presented for a transfer.</summary>
    public enum NotificationTransferState
    {
        /// <summary>The transfer is waiting for a local or remote slot.</summary>
        Queued = 0,

        /// <summary>The transfer is actively moving bytes.</summary>
        InProgress = 1,

        /// <summary>The transfer was paused and can be resumed.</summary>
        Paused = 2,

        /// <summary>The transfer completed successfully.</summary>
        Completed = 3,

        /// <summary>The transfer stopped because of an error.</summary>
        Failed = 4,

        /// <summary>The transfer was canceled by the user or lifecycle policy.</summary>
        Canceled = 5,
    }

    /// <summary>Describes a material state or progress change for a download or upload.</summary>
    public sealed class TransferStateNotification : SemanticNotification
    {
        /// <summary>Initializes a transfer-state notification.</summary>
        /// <param name="transferId">The stable identifier assigned by the transfer layer.</param>
        /// <param name="username">The remote Soulseek username.</param>
        /// <param name="filename">The transferred file's display name.</param>
        /// <param name="direction">Whether the transfer is a download or upload.</param>
        /// <param name="state">The portable transfer lifecycle state.</param>
        /// <param name="bytesTransferred">The non-negative number of bytes transferred so far.</param>
        /// <param name="totalBytes">The non-negative total byte count, or <see langword="null"/> when unknown.</param>
        /// <param name="averageBytesPerSecond">
        /// The non-negative average throughput, or <see langword="null"/> when unavailable.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="transferId"/>, <paramref name="username"/>, or <paramref name="filename"/> is empty or
        /// whitespace.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A byte count or average throughput is negative, NaN, or infinite.
        /// </exception>
        public TransferStateNotification(
            string transferId,
            string username,
            string filename,
            NotificationTransferDirection direction,
            NotificationTransferState state,
            long bytesTransferred,
            long? totalBytes = null,
            double? averageBytesPerSecond = null)
            : base(NotificationId.ForTransfer(transferId))
        {
            if (bytesTransferred < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesTransferred));
            }

            if (totalBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalBytes));
            }

            if (averageBytesPerSecond is double average &&
                (average < 0 || double.IsNaN(average) || double.IsInfinity(average)))
            {
                throw new ArgumentOutOfRangeException(nameof(averageBytesPerSecond));
            }

            TransferId = RequireText(transferId, nameof(transferId));
            Username = RequireText(username, nameof(username));
            Filename = RequireText(filename, nameof(filename));
            Direction = direction;
            State = state;
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            AverageBytesPerSecond = averageBytesPerSecond;
        }

        /// <summary>Gets the stable identifier assigned by the transfer layer.</summary>
        public string TransferId { get; }

        /// <summary>Gets the remote Soulseek username.</summary>
        public string Username { get; }

        /// <summary>Gets the transferred file's display name.</summary>
        public string Filename { get; }

        /// <summary>Gets whether this is a download or upload.</summary>
        public NotificationTransferDirection Direction { get; }

        /// <summary>Gets the portable transfer lifecycle state.</summary>
        public NotificationTransferState State { get; }

        /// <summary>Gets the number of bytes transferred so far.</summary>
        public long BytesTransferred { get; }

        /// <summary>Gets the total byte count, or <see langword="null"/> when unknown.</summary>
        public long? TotalBytes { get; }

        /// <summary>Gets the average throughput, or <see langword="null"/> when unavailable.</summary>
        public double? AverageBytesPerSecond { get; }
    }
}
