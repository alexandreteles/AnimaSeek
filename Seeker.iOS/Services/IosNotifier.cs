using Foundation;
using Common;
using Seeker.Services;
using UserNotifications;

namespace AnimaSeek.iOS.Services;

/// <summary>Describes notification permission without exposing UserNotifications types to presentation code.</summary>
internal enum NotificationAuthorizationState
{
    Loading,
    Unknown,
    NotDetermined,
    Denied,
    Authorized,
    Provisional,
    Ephemeral,
}

/// <summary>Maps portable semantic notifications to iOS local notifications and direct-reply actions.</summary>
internal sealed class IosNotifier : INotifier
{
    private const string PrivateMessageCategory = "private-message";
    private const string DirectReplyAction = "private-message.reply";
    private const string MarkReadAction = "private-message.mark-read";
    private readonly UNUserNotificationCenter notificationCenter = UNUserNotificationCenter.Current;
    private readonly IosNotificationCenterDelegate notificationDelegate = new();

    /// <summary>Registers notification categories without presenting a permission prompt during app launch.</summary>
    public IosNotifier()
    {
        var replyAction = UNTextInputNotificationAction.FromIdentifier(
            DirectReplyAction,
            StringResources.Get("IosNotificationReply"),
            UNNotificationActionOptions.None,
            StringResources.Get("IosNotificationSend"),
            StringResources.Get("IosNotificationMessagePlaceholder"));
        var markReadAction = UNNotificationAction.FromIdentifier(
            MarkReadAction,
            StringResources.Get("IosNotificationMarkRead"),
            UNNotificationActionOptions.None);
        var category = UNNotificationCategory.FromIdentifier(
            PrivateMessageCategory,
            [replyAction, markReadAction],
            [],
            UNNotificationCategoryOptions.None);
        notificationCenter.SetNotificationCategories(new NSSet<UNNotificationCategory>(category));
        notificationCenter.Delegate = notificationDelegate;
    }

    /// <summary>
    /// Requests local-notification permission after the user enables or reaches a feature that needs alerts.
    /// </summary>
    /// <returns><see langword="true"/> when iOS granted alert, sound, and badge authorization.</returns>
    public Task<bool> RequestAuthorizationAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        notificationCenter.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge,
            (granted, error) =>
            {
                if (error is not null)
                {
                    completion.TrySetException(new NSErrorException(error));
                    return;
                }

                completion.TrySetResult(granted);
            });
        return completion.Task;
    }

    /// <summary>Reads the current system notification permission without displaying a prompt.</summary>
    /// <returns>A UIKit-independent service state that preserves every native authorization outcome.</returns>
    public async Task<NotificationAuthorizationState> GetAuthorizationStateAsync()
    {
        UNNotificationSettings settings = await notificationCenter.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.NotDetermined => NotificationAuthorizationState.NotDetermined,
            UNAuthorizationStatus.Denied => NotificationAuthorizationState.Denied,
            UNAuthorizationStatus.Authorized => NotificationAuthorizationState.Authorized,
            UNAuthorizationStatus.Provisional => NotificationAuthorizationState.Provisional,
            UNAuthorizationStatus.Ephemeral => NotificationAuthorizationState.Ephemeral,
            _ => NotificationAuthorizationState.Unknown,
        };
    }

    /// <summary>Raised when a user submits text through the private-message notification action.</summary>
    public event DirectReplySubmittedEventHandler DirectReplySubmitted
    {
        add => notificationDelegate.DirectReplySubmitted += value;
        remove => notificationDelegate.DirectReplySubmitted -= value;
    }

    /// <summary>Raised for default notification taps and explicit Mark as Read actions.</summary>
    public event EventHandler<NotificationRouteEventArgs> RouteRequested
    {
        add => notificationDelegate.RouteRequested += value;
        remove => notificationDelegate.RouteRequested -= value;
    }

    /// <inheritdoc/>
    public void Post(SemanticNotification notification) => Schedule(notification, alert: true);

    /// <inheritdoc/>
    public void Update(SemanticNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        string identifier = notification.Id.ToString();
        notificationCenter.RemovePendingNotificationRequests([identifier]);
        notificationCenter.RemoveDeliveredNotifications([identifier]);
        Schedule(notification, alert: false);
    }

    /// <inheritdoc/>
    public void Cancel(NotificationId id)
    {
        string identifier = id.ToString();
        notificationCenter.RemovePendingNotificationRequests([identifier]);
        notificationCenter.RemoveDeliveredNotifications([identifier]);
    }

    /// <inheritdoc/>
    public void Cancel(NotificationKind kind)
    {
        string prefix = kind + ":";
        notificationCenter.GetPendingNotificationRequests(requests =>
            notificationCenter.RemovePendingNotificationRequests(
                requests.Select(request => request.Identifier).Where(identifier => identifier.StartsWith(prefix, StringComparison.Ordinal)).ToArray()));
        notificationCenter.GetDeliveredNotifications(notifications =>
            notificationCenter.RemoveDeliveredNotifications(
                notifications.Select(notification => notification.Request.Identifier).Where(identifier => identifier.StartsWith(prefix, StringComparison.Ordinal)).ToArray()));
    }

    private void Schedule(SemanticNotification notification, bool alert)
    {
        ArgumentNullException.ThrowIfNull(notification);
        (string title, string body, string category) = Present(notification);
        string routeScope = notification is FolderCompletedNotification { RouteTransferId: { } transferId }
            ? transferId
            : notification.Id.Scope;
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = body,
            CategoryIdentifier = category,
            Sound = alert ? UNNotificationSound.Default : null,
            UserInfo = NSDictionary.FromObjectsAndKeys(
                [
                    new NSString(routeScope),
                    new NSString(notification.Kind.ToString()),
                    new NSString(Guid.NewGuid().ToString("N")),
                ],
                [new NSString("scope"), new NSString("kind"), new NSString("delivery")]),
        };

        var request = UNNotificationRequest.FromIdentifier(
            notification.Id.ToString(),
            content,
            UNTimeIntervalNotificationTrigger.CreateTrigger(0.1, repeats: false));
        notificationCenter.AddNotificationRequest(request, _ => { });
    }

    private static (string Title, string Body, string Category) Present(SemanticNotification notification) => notification switch
    {
        PrivateMessageNotification { DeliveryState: PrivateMessageDeliveryState.Sent } message =>
            (StringResources.Format("IosNotificationReplySent", message.Username), message.MessageText, string.Empty),
        PrivateMessageNotification { DeliveryState: PrivateMessageDeliveryState.Failed } message =>
            (StringResources.Format("IosNotificationReplyFailed", message.Username),
                string.IsNullOrWhiteSpace(message.FailureReason)
                    ? StringResources.Get("IosNotificationReplyFailedBody")
                    : StringResources.Format("IosNotificationReplyFailedReason", message.FailureReason),
                string.Empty),
        PrivateMessageNotification { DeliveryState: PrivateMessageDeliveryState.Sending } message =>
            (StringResources.Format("IosNotificationReplySending", message.Username), message.MessageText, string.Empty),
        PrivateMessageNotification message =>
            (StringResources.Format("IosNotificationMessageFrom", message.Username), message.MessageText, PrivateMessageCategory),
        ChatroomMessageNotification message =>
            (StringResources.Format("IosNotificationRoomMessage", message.RoomName, message.Username), message.MessageText, string.Empty),
        WishlistHitNotification hit =>
            (StringResources.Get("IosNotificationWishlistTitle"),
                StringResources.Format(
                    hit.NewResultCount == 1
                        ? "IosNotificationWishlistBodySingle"
                        : "IosNotificationWishlistBodyPlural",
                    hit.NewResultCount,
                    hit.SearchTerm),
                string.Empty),
        FolderCompletedNotification folder =>
            (StringResources.Get("IosNotificationDownloadComplete"),
                StringResources.Format("IosNotificationFolderCompleteBody", folder.FolderName, folder.Username),
                string.Empty),
        UserOnlineNotification user =>
            (StringResources.Get("IosNotificationUserOnline"),
                StringResources.Format("IosNotificationUserOnlineBody", user.Username),
                string.Empty),
        TransferStateNotification transfer =>
            (StringResources.Format(
                    "IosNotificationTransferTitle",
                    StringResources.Get(transfer.Direction == NotificationTransferDirection.Download
                        ? "IosNotificationDownload"
                        : "IosNotificationUpload"),
                    Path.GetFileName(transfer.Filename.Replace('\\', '/'))),
                DescribeTransfer(transfer),
                string.Empty),
        _ => ("AnimaSeek", StringResources.Get("IosNotificationActivityUpdated"), string.Empty),
    };

    private static string DescribeTransfer(TransferStateNotification transfer)
    {
        string state = transfer.State switch
        {
            NotificationTransferState.InProgress => StringResources.Get("IosNotificationTransferring"),
            NotificationTransferState.Completed => StringResources.Get("IosNotificationCompleted"),
            NotificationTransferState.Failed => StringResources.Get("IosNotificationFailed"),
            NotificationTransferState.Canceled => StringResources.Get("IosNotificationCanceled"),
            NotificationTransferState.Paused => StringResources.Get("IosNotificationPaused"),
            _ => StringResources.Get("IosNotificationQueued"),
        };
        return transfer.TotalBytes is > 0
            ? StringResources.Format(
                "IosNotificationTransferBodyProgress",
                state,
                Math.Clamp(transfer.BytesTransferred * 100d / transfer.TotalBytes.Value, 0, 100),
                transfer.Username)
            : StringResources.Format("IosNotificationTransferBody", state, transfer.Username);
    }

    private sealed class IosNotificationCenterDelegate : UNUserNotificationCenterDelegate
    {
        private static readonly TimeSpan DirectReplyIdentityLifetime = TimeSpan.FromMinutes(10);
        private const int MaximumDirectReplyIdentities = 128;
        private readonly Lock directReplyIdentitySync = new();
        private readonly Dictionary<DirectReplyResponseIdentity, DateTimeOffset> directReplyIdentities = [];

        public event DirectReplySubmittedEventHandler? DirectReplySubmitted;

        public event EventHandler<NotificationRouteEventArgs>? RouteRequested;

        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);

        public override void DidReceiveNotificationResponse(
            UNUserNotificationCenter center,
            UNNotificationResponse response,
            Action completionHandler)
        {
            if (response.ActionIdentifier == DirectReplyAction && response is UNTextInputNotificationResponse input)
            {
                string scope = response.Notification.Request.Content.UserInfo["scope"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(input.UserText))
                {
                    DirectReplyResponseIdentity identity = CreateDirectReplyIdentity(response, input);
                    if (!TryAcceptDirectReply(identity))
                    {
                        completionHandler();
                        return;
                    }

                    _ = CompleteDirectReplyAsync(
                        new DirectReplyEventArgs(scope, input.UserText),
                        completionHandler);
                    return;
                }
            }

            if (response.IsDefaultAction || response.ActionIdentifier == MarkReadAction)
            {
                string scope = response.Notification.Request.Content.UserInfo["scope"]?.ToString() ?? string.Empty;
                string kindText = response.Notification.Request.Content.UserInfo["kind"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(scope) &&
                    Enum.TryParse(kindText, ignoreCase: false, out NotificationKind kind))
                {
                    RouteRequested?.Invoke(
                        this,
                        new NotificationRouteEventArgs(
                            kind,
                            scope,
                            response.ActionIdentifier == MarkReadAction));
                }
            }

            completionHandler();
        }

        /// <summary>Builds an exact response identity without collapsing intentional replies with different text.</summary>
        private static DirectReplyResponseIdentity CreateDirectReplyIdentity(
            UNNotificationResponse response,
            UNTextInputNotificationResponse input)
        {
            string deliveryIdentity =
                response.Notification.Request.Content.UserInfo["delivery"]?.ToString() ??
                response.Notification.Date.ToString();
            return new DirectReplyResponseIdentity(
                response.Notification.Request.Identifier,
                deliveryIdentity,
                response.ActionIdentifier,
                input.UserText);
        }

        /// <summary>Atomically admits a response once and prunes expired or oldest bounded identities.</summary>
        /// <param name="identity">The stable notification response identity.</param>
        /// <returns><see langword="true"/> only for the first callback inside the expiry window.</returns>
        private bool TryAcceptDirectReply(DirectReplyResponseIdentity identity)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (directReplyIdentitySync)
            {
                foreach (DirectReplyResponseIdentity expired in directReplyIdentities
                             .Where(pair => now - pair.Value >= DirectReplyIdentityLifetime)
                             .Select(static pair => pair.Key)
                             .ToArray())
                {
                    directReplyIdentities.Remove(expired);
                }

                if (directReplyIdentities.ContainsKey(identity))
                {
                    return false;
                }

                while (directReplyIdentities.Count >= MaximumDirectReplyIdentities)
                {
                    DirectReplyResponseIdentity oldest = directReplyIdentities.MinBy(static pair => pair.Value).Key;
                    directReplyIdentities.Remove(oldest);
                }

                directReplyIdentities[identity] = now;
                return true;
            }
        }

        private async Task CompleteDirectReplyAsync(DirectReplyEventArgs args, Action completionHandler)
        {
            try
            {
                DirectReplySubmittedEventHandler[] handlers = DirectReplySubmitted?
                    .GetInvocationList()
                    .Cast<DirectReplySubmittedEventHandler>()
                    .ToArray() ?? [];
                await Task.WhenAll(handlers.Select(handler => handler(this, args))).ConfigureAwait(false);
            }
            finally
            {
                completionHandler();
            }
        }

        /// <summary>Uniquely identifies one direct-reply callback while preserving intentional distinct text.</summary>
        private readonly record struct DirectReplyResponseIdentity(
            string RequestIdentifier,
            string DeliveryIdentifier,
            string ActionIdentifier,
            string UserText);
    }
}

/// <summary>Handles an iOS direct reply and completes only after delivery succeeds or fails.</summary>
/// <param name="sender">The notification delegate that received the response.</param>
/// <param name="args">The destination and submitted text.</param>
/// <returns>A task representing delivery and any corresponding notification update.</returns>
internal delegate Task DirectReplySubmittedEventHandler(object? sender, DirectReplyEventArgs args);

/// <summary>Contains a private-message destination and text submitted from an iOS notification.</summary>
internal sealed class DirectReplyEventArgs : EventArgs
{
    /// <summary>Creates direct-reply data.</summary>
    /// <param name="username">The destination Soulseek username.</param>
    /// <param name="message">The message entered by the user.</param>
    public DirectReplyEventArgs(string username, string message)
    {
        Username = username;
        Message = message;
    }

    /// <summary>Gets the destination Soulseek username.</summary>
    public string Username { get; }

    /// <summary>Gets the message entered by the user.</summary>
    public string Message { get; }
}

/// <summary>Identifies the typed application destination selected from a local notification.</summary>
internal sealed class NotificationRouteEventArgs : EventArgs
{
    /// <summary>Creates typed notification routing data.</summary>
    /// <param name="kind">The notification's semantic category.</param>
    /// <param name="scope">The category-specific stable destination identity.</param>
    /// <param name="markReadOnly">Whether the action should update state without navigating.</param>
    public NotificationRouteEventArgs(NotificationKind kind, string scope, bool markReadOnly)
    {
        Kind = kind;
        Scope = scope;
        MarkReadOnly = markReadOnly;
    }

    /// <summary>Gets the notification's semantic category.</summary>
    public NotificationKind Kind { get; }

    /// <summary>Gets the category-specific stable destination identity.</summary>
    public string Scope { get; }

    /// <summary>Gets whether the person requested only a state update rather than navigation.</summary>
    public bool MarkReadOnly { get; }
}
