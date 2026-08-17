using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Seeker.Routing;
using Seeker.Social;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Chatrooms;

/// <summary>Presents join state, retained room messages, a keyboard-safe composer, and supported room options.</summary>
internal sealed class RoomViewController : UIViewController
{
    private readonly IAppRouter router;
    private readonly ChatroomsPresentationStore store;
    private readonly string roomName;
    private readonly bool ownsStore;
    private readonly UITableView table = new(CGRect.Empty, UITableViewStyle.Plain)
    {
        TranslatesAutoresizingMaskIntoConstraints = false,
        RowHeight = UITableView.AutomaticDimension,
        EstimatedRowHeight = 72,
        KeyboardDismissMode = UIScrollViewKeyboardDismissMode.Interactive,
    };
    private const string MessageCellIdentifier = "rooms.message";
    private const string NoticeCellIdentifier = "rooms.notice";
    private const int CircularActionSize = 44;
    private const int ComposerBottomInset = 20;

    // A plain view defaults to 8-point margins, which made the composer visibly wider than the floating tab
    // bar beneath it. This matches the bar's inset so the two read as one stack of controls.
    private const int ComposerSideInset = 22;

    // The composer is an unfilled container so the capsule floats over the conversation, rather than a bar
    // whose edge welds itself to whatever sits below the screen.
    private readonly UIView composer = new()
    {
        TranslatesAutoresizingMaskIntoConstraints = false,
        BackgroundColor = UIColor.Clear,
    };

    private readonly UIView fieldBackground = new()
    {
        TranslatesAutoresizingMaskIntoConstraints = false,
        BackgroundColor = UIColor.TertiarySystemFill,
    };

    private readonly UITextField messageField = UIKitFactory.TextField(
        AppStrings.Get("IosUiMessagePlaceholder"),
        new NSString(string.Empty));
    private readonly UILabel statusLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
    private IReadOnlyList<RoomTimelineRow> timeline = [];
    private UIButton sendButton = null!;
    private UIButton jumpToLatestButton = null!;
    private bool joining;

    /// <summary>Creates one room detail controller.</summary>
    /// <param name="router">The typed cross-feature router.</param>
    /// <param name="store">The shared room presentation store.</param>
    /// <param name="roomName">The room to present.</param>
    public RoomViewController(
        IAppRouter router,
        ChatroomsPresentationStore store,
        string roomName,
        bool ownsStore = true)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ownsStore = ownsStore;
        this.roomName = string.IsNullOrWhiteSpace(roomName)
            ? throw new ArgumentException("A room name is required.", nameof(roomName))
            : roomName;
        Title = roomName;
    }

    /// <summary>Gets the represented room for idempotent coordinator matching.</summary>
    public string RoomName => roomName;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = $"rooms.detail.{AccessibilityIdentifiers.Opaque(roomName)}";

        // A conversation reads as a continuous transcript, so rows carry no separators or selection wash and
        // the room name stays compact rather than claiming a wrapping large title.
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        table.SeparatorStyle = UITableViewCellSeparatorStyle.None;
        table.BackgroundColor = UIColor.SystemBackground;
        table.RegisterClassForCellReuse(typeof(RoomMessageCell), MessageCellIdentifier);
        table.RegisterClassForCellReuse(typeof(RoomNoticeCell), NoticeCellIdentifier);

        // A table source already is the scroll-view delegate, so scroll tracking has to be one of its
        // overrides: adding a Scrolled event handler beside it makes UIKit throw as soon as the source is
        // first invoked, which took the app down on the first tap in a room.
        table.Source = new TimelineSource(() => timeline, PresentTimelineActions, UpdateJumpToLatestVisibility);
        table.AccessibilityIdentifier = "rooms.detail.messages";
        ConfigureNavigation();
        ConfigureComposer();
        ReloadRoom(scrollToLatest: true);
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        store.SetVisibleRoom(roomName);
        ReloadRoom(scrollToLatest: true);
        if (store.GetRoom(roomName)?.ConnectionState is not RoomConnectionState.Joined and not RoomConnectionState.Joining)
        {
            _ = JoinAsync();
        }
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnChanged;
        store.SetVisibleRoom(null);
        base.ViewDidDisappear(animated);
    }

    /// <inheritdoc/>
    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        table.ResizeTableHeader();
    }

    /// <inheritdoc/>
    public override void DidMoveToParentViewController(UIViewController? parent)
    {
        base.DidMoveToParentViewController(parent);
        if (parent is null && ownsStore)
        {
            store.Dispose();
        }
    }

    /// <summary>Builds discoverable users, ticker, and room-option actions.</summary>
    private void ConfigureNavigation()
    {
        var users = new UIBarButtonItem(
            UIImage.GetSystemImage("person.2")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => NavigationController?.PushViewController(
                new RoomUsersViewController(router, store, roomName), true))
        {
            AccessibilityLabel = AppStrings.Get("IosUiRoomUsers"),
            AccessibilityIdentifier = "rooms.detail.users",
        };
        var options = new UIBarButtonItem(
            UIImage.GetSystemImage("ellipsis.circle")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentRoomOptions())
        {
            AccessibilityLabel = AppStrings.Get("IosUiRoomOptions"),
            AccessibilityIdentifier = "rooms.detail.options",
        };
        NavigationItem.RightBarButtonItems = [options, users];
    }

    /// <summary>Builds the composer with directional constraints and the iOS keyboard layout guide.</summary>
    private void ConfigureComposer()
    {
        messageField.AccessibilityIdentifier = "rooms.detail.composer";
        messageField.ReturnKeyType = UIReturnKeyType.Send;
        messageField.ShouldReturn = field =>
        {
            _ = SendAsync();
            return false;
        };

        // The rounded-rect border is replaced by a capsule the field sits inside, so the composer matches the
        // system's own input bars instead of drawing a boxed control.
        messageField.BorderStyle = UITextBorderStyle.None;
        statusLabel.Lines = 0;
        statusLabel.AccessibilityIdentifier = "rooms.detail.status";
        statusLabel.Hidden = true;
        sendButton = CircularAction("arrow.up", AppStrings.Get("IosUiSend"), () => _ = SendAsync());
        sendButton.Configuration = FilledCircleConfiguration("arrow.up");
        sendButton.AccessibilityIdentifier = "rooms.detail.send";
        jumpToLatestButton = CircularAction(
            "chevron.down",
            AppStrings.Get("IosUiJumpToLatest"),
            () => ScrollToLatest(animated: true));
        jumpToLatestButton.AccessibilityIdentifier = "rooms.detail.jump-to-latest";
        jumpToLatestButton.Hidden = true;

        UIKitFactory.ApplyCapsuleCorners(fieldBackground);
        fieldBackground.Layer.BorderWidth = (nfloat)0.5;
        fieldBackground.Layer.BorderColor = UIColor.Separator.GetResolvedColor(fieldBackground.TraitCollection).CGColor;

        // A CGColor is a resolved value, not a dynamic color, so the border must be re-resolved when the
        // interface style flips.
        fieldBackground.RegisterForTraitChanges<UITraitUserInterfaceStyle>((_, _) =>
            fieldBackground.Layer.BorderColor = UIColor.Separator.GetResolvedColor(fieldBackground.TraitCollection).CGColor);
        fieldBackground.AddSubview(messageField);
        var input = new UIStackView([fieldBackground, sendButton])
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Center,
            Spacing = 8,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var stack = UIKitFactory.VerticalStack(6);
        stack.AddArrangedSubview(statusLabel);
        stack.AddArrangedSubview(input);
        composer.DirectionalLayoutMargins = new NSDirectionalEdgeInsets(0, ComposerSideInset, 0, ComposerSideInset);
        composer.AddSubview(stack);
        View!.AddSubviews(table, composer, jumpToLatestButton);

        // The composer rides the keyboard, but never below a clear gap above the bottom safe area, so it
        // keeps an obvious margin from the tab bar instead of resting against it. The keyboard constraint
        // yields to the required clamp whenever the keyboard is dismissed.
        NSLayoutConstraint keyboardBottom = composer.BottomAnchor
            .ConstraintEqualTo(View.KeyboardLayoutGuide.TopAnchor)
            .WithPriority(UILayoutPriority.DefaultHigh);
        NSLayoutConstraint.ActivateConstraints(
        [
            table.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            table.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            table.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            table.BottomAnchor.ConstraintEqualTo(composer.TopAnchor),
            composer.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            composer.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            keyboardBottom,
            composer.BottomAnchor.ConstraintLessThanOrEqualTo(
                View.SafeAreaLayoutGuide.BottomAnchor,
                -ComposerBottomInset),
            stack.TopAnchor.ConstraintEqualTo(composer.TopAnchor, 8),
            stack.BottomAnchor.ConstraintEqualTo(composer.BottomAnchor),
            stack.LeadingAnchor.ConstraintEqualTo(composer.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(composer.LayoutMarginsGuide.TrailingAnchor),
            messageField.TopAnchor.ConstraintEqualTo(fieldBackground.TopAnchor, 8),
            messageField.BottomAnchor.ConstraintEqualTo(fieldBackground.BottomAnchor, -8),
            messageField.LeadingAnchor.ConstraintEqualTo(fieldBackground.LeadingAnchor, 14),
            messageField.TrailingAnchor.ConstraintEqualTo(fieldBackground.TrailingAnchor, -14),
            fieldBackground.HeightAnchor.ConstraintGreaterThanOrEqualTo(CircularActionSize),
            sendButton.WidthAnchor.ConstraintEqualTo(CircularActionSize),
            sendButton.HeightAnchor.ConstraintEqualTo(CircularActionSize),

            // The jump control floats over the conversation instead of taking a full row of the composer,
            // aligned to the composer's own trailing edge.
            jumpToLatestButton.TrailingAnchor.ConstraintEqualTo(composer.LayoutMarginsGuide.TrailingAnchor),
            jumpToLatestButton.BottomAnchor.ConstraintEqualTo(composer.TopAnchor, -12),
            jumpToLatestButton.WidthAnchor.ConstraintEqualTo(CircularActionSize),
            jumpToLatestButton.HeightAnchor.ConstraintEqualTo(CircularActionSize),
        ]);
    }

    /// <summary>Creates one 44-point circular glyph control that carries its meaning through VoiceOver.</summary>
    /// <param name="symbol">The SF Symbol glyph.</param>
    /// <param name="label">The localized accessible name, since the control shows no text.</param>
    /// <param name="action">The action invoked after explicit activation.</param>
    private static UIButton CircularAction(string symbol, string label, Action action)
    {
        var button = UIButton.FromType(UIButtonType.System);
        UIButtonConfiguration configuration = UIButtonConfiguration.GrayButtonConfiguration;
        configuration.Image = UIImage.GetSystemImage(symbol);
        configuration.CornerStyle = UIButtonConfigurationCornerStyle.Capsule;
        configuration.ContentInsets = NSDirectionalEdgeInsets.Zero;
        button.Configuration = configuration;
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        button.AccessibilityLabel = label;
        button.TouchUpInside += (_, _) => action();
        return button;
    }

    /// <summary>Creates the filled variant used by the primary send control.</summary>
    /// <param name="symbol">The SF Symbol glyph.</param>
    private static UIButtonConfiguration FilledCircleConfiguration(string symbol)
    {
        UIButtonConfiguration configuration = UIButtonConfiguration.FilledButtonConfiguration;
        configuration.Image = UIImage.GetSystemImage(symbol);
        configuration.CornerStyle = UIButtonConfigurationCornerStyle.Capsule;
        configuration.ContentInsets = NSDirectionalEdgeInsets.Zero;
        return configuration;
    }

    /// <summary>Joins the room with inline loading, failure, and retry feedback.</summary>
    private async Task JoinAsync()
    {
        if (joining)
        {
            return;
        }

        joining = true;
        ApplyAvailability();
        try
        {
            await store.JoinAsync(roomName);
            statusLabel.Text = string.Empty;
        }
        catch
        {
            statusLabel.Text = store.GetRoom(roomName)?.JoinFailureReason ?? AppStrings.Get("IosUiRoomJoinFailedDetail");
            statusLabel.TextColor = UIColor.SystemRed;
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiRoomJoinFailed"));
        }
        finally
        {
            joining = false;
            ReloadRoom(scrollToLatest: false);
        }
    }

    /// <summary>Sends a non-empty message and restores the draft after failure.</summary>
    private async Task SendAsync()
    {
        string text = messageField.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 || !sendButton.Enabled)
        {
            return;
        }

        messageField.Text = string.Empty;
        sendButton.Enabled = false;
        messageField.Enabled = false;
        statusLabel.Text = AppStrings.Get("IosUiMessageSending");
        try
        {
            await store.SendAsync(roomName, text);
            statusLabel.Text = string.Empty;
            ReloadRoom(scrollToLatest: true);
        }
        catch
        {
            messageField.Text = text;
            statusLabel.Text = AppStrings.Get("IosUiRoomMessageFailed");
            statusLabel.TextColor = UIColor.SystemRed;
            AccessibilityExtensions.Announce(statusLabel.Text);
        }
        finally
        {
            messageField.Enabled = true;
            ApplyAvailability();
        }
    }

    /// <summary>Shows copy, sender, retry, and validated Soulseek-link actions.</summary>
    /// <param name="message">The selected retained message.</param>
    private void PresentMessageActions(RoomMessageEntry message)
    {
        var sheet = UIAlertController.Create(
            message.Username,
            message.TimestampUtc.ToLocalTime().ToString("g"),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiCopyMessage"),
            UIAlertActionStyle.Default,
            _ => UIPasteboard.General.String = message.Message));
        if (message.DeliveryState == RoomMessageDeliveryState.Failed)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiRetry"),
                UIAlertActionStyle.Default,
                action =>
                {
                    messageField.Text = message.Message;
                    _ = SendAsync();
                }));
        }

        if (SoulseekLinkParser.TryParse(message.Message, out SoulseekLink? link) && link is not null)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiSoulseekLinkTitle"),
                UIAlertActionStyle.Default,
                _ => router.Navigate(new AppRoute.SoulseekLink(link))));
        }

        if (!message.IsFromCurrentUser)
        {
            AddUserActions(sheet, message.Username);
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        AnchorSheet(sheet, table);
        PresentViewController(sheet, true, null);
    }

    /// <summary>Routes a selected message or member activity to its appropriate action sheet.</summary>
    /// <param name="row">The selected timeline row.</param>
    private void PresentTimelineActions(RoomTimelineRow row)
    {
        if (row.Message is { } message)
        {
            PresentMessageActions(message);
            return;
        }

        if (row.Activity is not { } activity)
        {
            return;
        }

        var sheet = UIAlertController.Create(
            activity.Username,
            ActivityText(activity),
            UIAlertControllerStyle.ActionSheet);
        AddUserActions(sheet, activity.Username);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        AnchorSheet(sheet, table);
        PresentViewController(sheet, true, null);
    }

    /// <summary>Adds reusable profile, message, browse, and user-search actions.</summary>
    /// <param name="sheet">The destination action sheet.</param>
    /// <param name="username">The selected user.</param>
    private void AddUserActions(UIAlertController sheet, string username)
    {
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiViewProfile"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.UserProfile(username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSendMessage"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Messages(username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiBrowseUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Browse(username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSearchUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Search(
                Target: SearchRouteTarget.User,
                Subject: username))));
    }

    /// <summary>Shows supported preferences, ticker actions, search, leave, and private-room destruction.</summary>
    private void PresentRoomOptions()
    {
        RoomSessionSnapshot? room = store.GetRoom(roomName);
        var sheet = UIAlertController.Create(roomName, AppStrings.Get("IosUiRoomOptions"), UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            $"{AppStrings.Get("IosUiAutoJoinRoom")}: {ToggleText(store.IsAutoJoin(roomName))}",
            UIAlertActionStyle.Default,
            action =>
            {
                store.SetAutoJoin(roomName, !store.IsAutoJoin(roomName));
                AccessibilityExtensions.Announce(AppStrings.Get("IosUiSettingChanged"));
            }));
        sheet.AddAction(UIAlertAction.Create(
            $"{AppStrings.Get("IosUiRoomNotifications")}: {ToggleText(store.HasNotifications(roomName))}",
            UIAlertActionStyle.Default,
            _ =>
            {
                store.SetNotifications(roomName, !store.HasNotifications(roomName));
                AccessibilityExtensions.Announce(AppStrings.Get("IosUiSettingChanged"));
            }));
        sheet.AddAction(UIAlertAction.Create(
            $"{AppStrings.Get("IosUiShowRoomActivity")}: {ToggleText(store.ShowsStatusEvents)}",
            UIAlertActionStyle.Default,
            _ => store.SetShowsStatusEvents(!store.ShowsStatusEvents)));
        sheet.AddAction(UIAlertAction.Create(
            $"{AppStrings.Get("IosUiShowRoomTickers")}: {ToggleText(store.ShowsTickers)}",
            UIAlertActionStyle.Default,
            _ => store.SetShowsTickers(!store.ShowsTickers)));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiRoomTickers"),
            UIAlertActionStyle.Default,
            _ => NavigationController?.PushViewController(new RoomTickersViewController(store, roomName), true)));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSetTicker"),
            UIAlertActionStyle.Default,
            _ => PresentTickerEditor()));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSearchThisRoom"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Search(
                Target: SearchRouteTarget.Room,
                Subject: roomName))));
        if (room?.ConnectionState is RoomConnectionState.Joined or RoomConnectionState.Disconnected)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiLeaveRoom"),
                UIAlertActionStyle.Destructive,
                _ => ConfirmLeave()));
        }

        string? current = store.CurrentUsername;
        if (room?.RoomData?.IsPrivate == true && current is not null)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiInviteMember"),
                UIAlertActionStyle.Default,
                _ => PresentInvite()));
            bool owner = room.IsOwnedBy(current);
            sheet.AddAction(UIAlertAction.Create(
                owner ? AppStrings.Get("IosUiDropOwnership") : AppStrings.Get("IosUiDropMembership"),
                UIAlertActionStyle.Destructive,
                _ => ConfirmDropPrivate(owner)));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        AnchorSheet(sheet, View!);
        PresentViewController(sheet, true, null);
    }

    /// <summary>Presents the account's ticker editor; empty input deliberately clears the ticker.</summary>
    private void PresentTickerEditor()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiSetTicker"),
            AppStrings.Get("IosUiTickerPlaceholder"),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiTickerPlaceholder");
            field.AccessibilityIdentifier = "rooms.detail.ticker";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSave"),
            UIAlertActionStyle.Default,
            action => _ = RunRoomActionAsync(() => store.SetTickerAsync(
                roomName,
                alert.TextFields?.FirstOrDefault()?.Text?.Trim() ?? string.Empty))));
        PresentViewController(alert, true, null);
    }

    /// <summary>Presents an explicit username invitation for private rooms.</summary>
    private void PresentInvite()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiInviteMember"),
            AppStrings.Get("IosUiInviteMemberDetail"),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiUsername");
            field.AutocapitalizationType = UITextAutocapitalizationType.None;
            field.AutocorrectionType = UITextAutocorrectionType.No;
            field.AccessibilityIdentifier = "rooms.detail.invite-username";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiInviteMember"),
            UIAlertActionStyle.Default,
            action =>
            {
                string username = alert.TextFields?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
                if (username.Length > 0)
                {
                    _ = RunRoomActionAsync(() => store.AddPrivateMemberAsync(roomName, username));
                }
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Confirms a normal room leave before executing it.</summary>
    private void ConfirmLeave()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiLeaveRoom"),
            AppStrings.Get("IosUiLeaveRoomConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiLeaveRoom"),
            UIAlertActionStyle.Destructive,
            action => _ = RunRoomActionAsync(() => store.LeaveAsync(roomName))));
        PresentViewController(alert, true, null);
    }

    /// <summary>Confirms irreversible membership or ownership removal with consequence-specific copy.</summary>
    /// <param name="owner">Whether the current account owns the room.</param>
    private void ConfirmDropPrivate(bool owner)
    {
        string title = AppStrings.Get(owner ? "IosUiDropOwnership" : "IosUiDropMembership");
        var alert = UIAlertController.Create(
            title,
            AppStrings.Get(owner ? "IosUiDropOwnershipConfirm" : "IosUiDropMembershipConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            title,
            UIAlertActionStyle.Destructive,
            action => _ = RunRoomActionAsync(() => owner
                ? store.DropOwnershipAsync(roomName)
                : store.DropMembershipAsync(roomName))));
        PresentViewController(alert, true, null);
    }

    /// <summary>Runs a room mutation with consistent failure feedback.</summary>
    /// <param name="operation">The asynchronous mutation.</param>
    private async Task RunRoomActionAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch
        {
            statusLabel.Text = AppStrings.Get("IosUiOperationFailed");
            statusLabel.TextColor = UIColor.SystemRed;
            AccessibilityExtensions.Announce(statusLabel.Text);
        }
    }

    /// <summary>Reloads the chronological timeline, ticker summary, and connection UI from detached snapshots.</summary>
    /// <param name="scrollToLatest">Whether to reveal the newest retained message.</param>
    private void ReloadRoom(bool scrollToLatest)
    {
        RoomSessionSnapshot? room = store.GetRoom(roomName);
        timeline = store.GetTimeline(roomName);
        table.ReloadData();
        UpdateTickerHeader(room);
        ApplyAvailability();
        if (timeline.Count == 0)
        {
            table.BackgroundView = EmptyRoomView(room);
        }
        else
        {
            table.BackgroundView = null;
        }

        if (scrollToLatest && timeline.Count > 0)
        {
            ScrollToLatest(animated: true);
        }
        else
        {
            UpdateJumpToLatestVisibility();
        }
    }

    /// <summary>Reveals the newest timeline item and hides the recovery control.</summary>
    /// <param name="animated">Whether native scrolling may animate when Reduce Motion permits it.</param>
    private void ScrollToLatest(bool animated)
    {
        if (timeline.Count == 0)
        {
            jumpToLatestButton.Hidden = true;
            return;
        }

        table.ScrollToRow(
            NSIndexPath.FromRowSection(timeline.Count - 1, 0),
            UITableViewScrollPosition.Bottom,
            animated && AccessibilityExtensions.AccessibleAnimationDuration(0.25) > 0);
        jumpToLatestButton.Hidden = true;
    }

    /// <summary>Shows Jump to Latest only while a person is reading older room history.</summary>
    private void UpdateJumpToLatestVisibility()
    {
        bool nearLatest = timeline.Count == 0 ||
            table.ContentSize.Height <= table.Bounds.Height ||
            table.ContentOffset.Y + table.Bounds.Height >= table.ContentSize.Height - 80;
        jumpToLatestButton.Hidden = nearLatest;
    }

    /// <summary>Builds a self-sizing ticker summary when the corresponding room preference is enabled.</summary>
    /// <param name="room">The latest room snapshot.</param>
    private void UpdateTickerHeader(RoomSessionSnapshot? room)
    {
        if (!store.ShowsTickers || room?.Tickers.Count is not > 0)
        {
            table.TableHeaderView = null;
            return;
        }

        var title = UIKitFactory.Label(UIFontTextStyle.Headline);
        title.Text = AppStrings.Get("IosUiTickerSummary");
        var detail = UIKitFactory.Label(UIFontTextStyle.Subheadline, UIColor.SecondaryLabel);
        detail.Text = string.Join("\n", room.Tickers.Select(ticker =>
            AppStrings.Format("IosUiTickerByUser", ticker.Username, ticker.Message)));
        var stack = UIKitFactory.VerticalStack(4);
        stack.AddArrangedSubview(title);
        stack.AddArrangedSubview(detail);
        var container = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemBackground,
            AccessibilityIdentifier = "rooms.detail.ticker-summary",
        };
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor, 12),
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -12),
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
        ]);
        table.SetSelfSizingHeader(container);
    }

    /// <summary>Enables the composer only while a usable joined-room session exists.</summary>
    private void ApplyAvailability()
    {
        RoomConnectionState state = store.GetRoom(roomName)?.ConnectionState ?? RoomConnectionState.NotJoined;
        bool joined = state == RoomConnectionState.Joined && !joining;
        messageField.Enabled = joined;
        sendButton.Enabled = joined;
        if (joining || state == RoomConnectionState.Joining)
        {
            statusLabel.Text = AppStrings.Get("IosUiRoomJoining");
            statusLabel.TextColor = UIColor.SecondaryLabel;
        }
        else if (state is RoomConnectionState.Failed or RoomConnectionState.Forbidden)
        {
            statusLabel.Text = store.GetRoom(roomName)?.JoinFailureReason ?? AppStrings.Get("IosUiRoomJoinFailedDetail");
            statusLabel.TextColor = UIColor.SystemRed;
        }
        else if (!joined)
        {
            statusLabel.Text = AppStrings.Get("IosUiRoomNotJoined");
            statusLabel.TextColor = UIColor.SecondaryLabel;
        }
        else
        {
            statusLabel.Text = string.Empty;
        }

        // A stack collapses the hidden row, so a joined room gives the whole composer height to its input.
        statusLabel.Hidden = string.IsNullOrWhiteSpace(statusLabel.Text);
    }

    /// <summary>Creates a self-sizing textual empty/join-failure state.</summary>
    /// <param name="room">The optional latest room snapshot.</param>
    /// <returns>A centered content-unavailable configuration view.</returns>
    private UIView EmptyRoomView(RoomSessionSnapshot? room)
    {
        var title = UIKitFactory.Label(UIFontTextStyle.Headline);
        title.Text = room?.ConnectionState is RoomConnectionState.Failed or RoomConnectionState.Forbidden
            ? AppStrings.Get("IosUiRoomJoinFailed")
            : AppStrings.Get("IosUiNoRoomMessages");
        title.TextAlignment = UITextAlignment.Center;
        var detail = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SecondaryLabel);
        detail.Text = room?.JoinFailureReason ?? AppStrings.Get("IosUiNoRoomMessagesDetail");
        detail.TextAlignment = UITextAlignment.Center;
        var retry = UIKitFactory.Button(
            AppStrings.Get("IosUiRetry"),
            UIButtonConfiguration.TintedButtonConfiguration,
            () => _ = JoinAsync());
        retry.Hidden = room?.ConnectionState is not RoomConnectionState.Failed and not RoomConnectionState.Forbidden;
        var stack = UIKitFactory.VerticalStack(12);
        stack.Alignment = UIStackViewAlignment.Fill;
        stack.AddArrangedSubview(title);
        stack.AddArrangedSubview(detail);
        stack.AddArrangedSubview(retry);
        var container = new UIView();
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.CenterYAnchor.ConstraintEqualTo(container.CenterYAnchor),
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
        ]);
        return container;
    }

    /// <summary>Updates room content while preserving reading position unless already near the latest message.</summary>
    /// <param name="sender">The room store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args)
    {
        bool nearLatest = table.ContentSize.Height <= table.Bounds.Height ||
            table.ContentOffset.Y + table.Bounds.Height >= table.ContentSize.Height - 80;
        ReloadRoom(nearLatest);
    }

    /// <summary>Returns localized switch state text for non-color status.</summary>
    /// <param name="enabled">The setting state.</param>
    /// <returns>On or Off.</returns>
    private static string ToggleText(bool enabled) => AppStrings.Get(enabled ? "IosUiOn" : "IosUiOff");

    /// <summary>Maps a member activity to localized, non-color text.</summary>
    /// <param name="activity">The detached member event.</param>
    /// <returns>A localized sentence.</returns>
    private static string ActivityText(RoomMemberActivity activity) => AppStrings.Format(
        activity.Kind switch
        {
            RoomMemberActivityKind.Joined => "IosUiRoomUserJoined",
            RoomMemberActivityKind.Left => "IosUiRoomUserLeft",
            RoomMemberActivityKind.WentAway => "IosUiRoomUserAway",
            RoomMemberActivityKind.CameOnline => "IosUiRoomUserOnline",
            _ => "IosUiRoomUserOffline",
        },
        activity.Username);

    /// <summary>Anchors an action sheet safely on iPad.</summary>
    /// <param name="sheet">The action sheet.</param>
    /// <param name="source">The source view.</param>
    private static void AnchorSheet(UIAlertController sheet, UIView source)
    {
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = source;
            popover.SourceRect = source.Bounds;
        }
    }

    /// <summary>Provides self-sizing, link-aware message rows and textual member-activity rows.</summary>
    private sealed class TimelineSource(
        Func<IReadOnlyList<RoomTimelineRow>> getRows,
        Action<RoomTimelineRow> select,
        Action scrolled) : UITableViewSource
    {
        private readonly Func<IReadOnlyList<RoomTimelineRow>> getRows = getRows;
        private readonly Action<RoomTimelineRow> select = select;
        private readonly Action scrolled = scrolled;

        /// <inheritdoc/>
        public override nint RowsInSection(UITableView tableview, nint section) => getRows().Count;

        /// <inheritdoc/>
        public override void Scrolled(UIScrollView scrollView) => scrolled();

        /// <inheritdoc/>
        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
        {
            IReadOnlyList<RoomTimelineRow> rows = getRows();
            RoomTimelineRow row = rows[indexPath.Row];
            if (row.Activity is { } activity)
            {
                var notice = (RoomNoticeCell)tableView.DequeueReusableCell(NoticeCellIdentifier, indexPath);
                notice.Apply(activity);
                return notice;
            }

            RoomMessageEntry message = row.Message!;

            // Consecutive messages from one person read as a single utterance, so only the first carries the
            // sender name and only the last carries the timestamp, the way a native conversation groups them.
            bool startsGroup = indexPath.Row == 0 ||
                rows[indexPath.Row - 1].Message is not { } previous ||
                !string.Equals(previous.Username, message.Username, StringComparison.Ordinal);
            bool endsGroup = indexPath.Row == rows.Count - 1 ||
                rows[indexPath.Row + 1].Message is not { } next ||
                !string.Equals(next.Username, message.Username, StringComparison.Ordinal);
            var cell = (RoomMessageCell)tableView.DequeueReusableCell(MessageCellIdentifier, indexPath);
            cell.Apply(message, startsGroup, endsGroup);
            return cell;
        }

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            select(getRows()[indexPath.Row]);
        }
    }

    /// <summary>Presents one message as a native conversation bubble aligned by authorship.</summary>
    private sealed class RoomMessageCell : UITableViewCell
    {
        private readonly UIStackView stack = UIKitFactory.VerticalStack(2);
        private readonly UILabel nameLabel = UIKitFactory.Label(UIFontTextStyle.Caption1, UIColor.SecondaryLabel);
        private readonly UIView bubble = new() { TranslatesAutoresizingMaskIntoConstraints = false };
        private readonly UILabel messageLabel = UIKitFactory.Label(UIFontTextStyle.Body);
        private readonly UILabel metaLabel = UIKitFactory.Label(UIFontTextStyle.Caption2, UIColor.TertiaryLabel);

        /// <summary>Creates the reusable bubble hierarchy for a table-registered cell class.</summary>
        /// <param name="handle">The native handle supplied by UIKit.</param>
        public RoomMessageCell(NativeHandle handle) : base(handle)
        {
            BackgroundColor = UIColor.Clear;

            // The row opens an action sheet rather than selecting anything, so a selection wash would be a
            // false affordance over a bubble.
            SelectionStyle = UITableViewCellSelectionStyle.None;
            UIKitFactory.ApplyRoundedContentCorners(bubble);
            bubble.AddSubview(messageLabel);
            stack.AddArrangedSubview(nameLabel);
            stack.AddArrangedSubview(bubble);
            stack.AddArrangedSubview(metaLabel);
            ContentView.AddSubview(stack);
            NSLayoutConstraint.ActivateConstraints([
                stack.TopAnchor.ConstraintEqualTo(ContentView.TopAnchor, 2),
                stack.BottomAnchor.ConstraintEqualTo(ContentView.BottomAnchor, -2),
                stack.LeadingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.LeadingAnchor),
                stack.TrailingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.TrailingAnchor),
                messageLabel.TopAnchor.ConstraintEqualTo(bubble.TopAnchor, 9),
                messageLabel.BottomAnchor.ConstraintEqualTo(bubble.BottomAnchor, -9),
                messageLabel.LeadingAnchor.ConstraintEqualTo(bubble.LeadingAnchor, 14),
                messageLabel.TrailingAnchor.ConstraintEqualTo(bubble.TrailingAnchor, -14),
                bubble.WidthAnchor.ConstraintLessThanOrEqualTo(stack.WidthAnchor, (nfloat)0.82),
            ]);
        }

        /// <summary>Applies one message, its authorship alignment, and its position within a sender group.</summary>
        /// <param name="message">The message to present.</param>
        /// <param name="startsGroup">Whether this row opens a run of messages from one sender.</param>
        /// <param name="endsGroup">Whether this row closes a run of messages from one sender.</param>
        public void Apply(RoomMessageEntry message, bool startsGroup, bool endsGroup)
        {
            bool own = message.IsFromCurrentUser;
            stack.Alignment = own ? UIStackViewAlignment.Trailing : UIStackViewAlignment.Leading;
            bubble.BackgroundColor = own ? UIColor.SystemBlue : UIColor.SystemGray5;
            messageLabel.Text = message.Message;
            messageLabel.TextColor = own ? UIColor.White : UIColor.Label;
            nameLabel.Text = message.Username;
            nameLabel.Hidden = own || !startsGroup;
            string delivery = message.DeliveryState switch
            {
                RoomMessageDeliveryState.Sending => AppStrings.Get("IosUiMessageSending"),
                RoomMessageDeliveryState.Failed => AppStrings.Get("IosUiMessageFailed"),
                _ => string.Empty,
            };
            string time = message.TimestampUtc.ToLocalTime().ToString("t");
            metaLabel.Text = delivery.Length > 0 ? $"{time} · {delivery}" : time;
            metaLabel.TextColor = message.DeliveryState == RoomMessageDeliveryState.Failed
                ? UIColor.SystemRed
                : UIColor.TertiaryLabel;
            metaLabel.Hidden = !endsGroup && delivery.Length == 0;
            AccessibilityIdentifier = $"rooms.detail.message.{message.Sequence}";
            IsAccessibilityElement = true;
            AccessibilityLabel = $"{message.Username}. {message.Message}";
            AccessibilityValue = metaLabel.Text;
            AccessibilityHint = AppStrings.Get("IosUiMoreActions");
        }
    }

    /// <summary>Presents one member-activity event as a centered, unobtrusive conversation notice.</summary>
    private sealed class RoomNoticeCell : UITableViewCell
    {
        private readonly UILabel label = UIKitFactory.Label(UIFontTextStyle.Caption1, UIColor.SecondaryLabel);

        /// <summary>Creates the reusable notice hierarchy for a table-registered cell class.</summary>
        /// <param name="handle">The native handle supplied by UIKit.</param>
        public RoomNoticeCell(NativeHandle handle) : base(handle)
        {
            BackgroundColor = UIColor.Clear;
            SelectionStyle = UITableViewCellSelectionStyle.None;
            label.TextAlignment = UITextAlignment.Center;
            ContentView.AddSubview(label);
            NSLayoutConstraint.ActivateConstraints([
                label.TopAnchor.ConstraintEqualTo(ContentView.TopAnchor, 8),
                label.BottomAnchor.ConstraintEqualTo(ContentView.BottomAnchor, -8),
                label.LeadingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.LeadingAnchor),
                label.TrailingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.TrailingAnchor),
            ]);
        }

        /// <summary>Applies one member-activity event and its time.</summary>
        /// <param name="activity">The member activity to present.</param>
        public void Apply(RoomMemberActivity activity)
        {
            string text = $"{ActivityText(activity)} · {activity.TimestampUtc.ToLocalTime().ToString("t")}";
            label.Text = text;
            AccessibilityIdentifier =
                $"rooms.detail.activity.{AccessibilityIdentifiers.Opaque(activity.Username)}.{activity.TimestampUtc.Ticks}";
            IsAccessibilityElement = true;
            AccessibilityLabel = text;
            AccessibilityHint = AppStrings.Get("IosUiUserActions");
        }
    }
}
