using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using CoreGraphics;
using Foundation;
using Seeker.Routing;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Messages;

/// <summary>Presents conversation summaries, unread state, compose lookup, batch deletion, and actionable undo.</summary>
internal sealed class MessagesViewController : UITableViewController, IAppRouteReceiving
{
    private static readonly NSString SectionIdentifier = new("conversations");
    private const string ConversationReuseIdentifier = "ConversationCell";
    private readonly IAppRouter router;
    private readonly MessagesPresentationStore store;
    private readonly bool ownsStore;
    private readonly Dictionary<string, ConversationSummary> conversationsByUsername = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedUsernames = new(StringComparer.Ordinal);
    private UITableViewDiffableDataSource<NSString, NSString> dataSource = null!;
    private MessagesOverviewPresentation presentation = new(MessagesOverviewPhase.Loading, []);
    private CancellationTokenSource? refreshCancellation;
    private UIBarButtonItem markAllReadButton = null!;
    private bool pendingConversationSnapshot;
    private bool showsUndoBanner;

    /// <summary>Creates the messages overview.</summary>
    /// <param name="router">The typed application router.</param>
    /// <param name="store">The private-message presentation store.</param>
    public MessagesViewController(IAppRouter router, MessagesPresentationStore store, bool ownsStore = true)
        : base(UITableViewStyle.Plain)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ownsStore = ownsStore;
        Title = AppStrings.Get("IosUiMessagesTitle");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = "messages.screen";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 76;
        TableView.AllowsMultipleSelectionDuringEditing = true;
        TableView.AccessibilityIdentifier = "messages.list";
        TableView.RegisterClassForCellReuse(typeof(UITableViewCell), ConversationReuseIdentifier);
        dataSource = new ConversationDataSource(TableView, ConfigureConversationCell);
        TableView.DataSource = dataSource;
        RefreshControl = new UIRefreshControl
        {
            AccessibilityLabel = AppStrings.Get("IosUiRefresh"),
            AccessibilityIdentifier = "messages.refresh",
        };
        RefreshControl.ValueChanged += (_, _) => _ = RefreshOverviewAsync(showLoading: false);
        markAllReadButton = new UIBarButtonItem(
            AppStrings.Get("IosUiMarkAllRead"),
            UIBarButtonItemStyle.Plain,
            (_, _) => store.MarkAllRead())
        {
            AccessibilityIdentifier = "messages.mark-all-read",
        };
        NavigationItem.RightBarButtonItems =
        [
            new UIBarButtonItem(UIBarButtonSystemItem.Compose, (_, _) => PresentCompose())
            {
                AccessibilityLabel = AppStrings.Get("IosUiComposeMessage"),
                AccessibilityIdentifier = "messages.compose",
            },
            markAllReadButton,
        ];
        ApplyPresentation(store.GetLoadingOverview(), animated: false);
        _ = RefreshOverviewAsync(showLoading: true);
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        _ = RefreshOverviewAsync(showLoading: presentation.Phase == MessagesOverviewPhase.Loading);
    }

    /// <inheritdoc/>
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (pendingConversationSnapshot)
        {
            pendingConversationSnapshot = false;
            ApplyConversationSnapshot(presentation.Conversations, animated: false);
        }
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnChanged;
        if (IsMovingFromParentViewController)
        {
            this.ClearSelectionAccessory(animated);
        }

        base.ViewDidDisappear(animated);
    }

    /// <inheritdoc/>
    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        TableView.ResizeTableHeader();
    }

    /// <inheritdoc/>
    public override void DidMoveToParentViewController(UIViewController? parent)
    {
        base.DidMoveToParentViewController(parent);
        if (parent is null && ownsStore)
        {
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            store.Dispose();
        }
    }

    /// <summary>Configures one reusable conversation cell from its stable username identifier.</summary>
    /// <param name="tableView">The owning table.</param>
    /// <param name="indexPath">The current visual position.</param>
    /// <param name="identifier">The username-backed diffable identifier.</param>
    /// <returns>A self-sizing native conversation cell.</returns>
    private UITableViewCell ConfigureConversationCell(
        UITableView tableView,
        NSIndexPath indexPath,
        NSObject identifier)
    {
        string username = identifier.ToString();
        UITableViewCell cell = tableView.DequeueReusableCell(ConversationReuseIdentifier, indexPath);
        if (!conversationsByUsername.TryGetValue(username, out ConversationSummary? conversation))
        {
            return cell;
        }

        cell.Accessory = Editing
            ? UITableViewCellAccessory.None
            : UITableViewCellAccessory.DisclosureIndicator;
        cell.AccessibilityIdentifier =
            $"messages.conversation.{AccessibilityIdentifiers.Opaque(conversation.Username)}";
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = conversation.Username;
        content.SecondaryText = BuildSecondaryText(conversation);
        content.Image = UIImage.GetSystemImage(PresenceSymbol(conversation.Presence));
        content.TextProperties.Font = UIKitFactory.PreferredFont(
            conversation.UnreadCount > 0 ? UIFontTextStyle.Headline : UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.SecondaryTextProperties.NumberOfLines = 2;
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.ImageProperties.TintColor = PresenceColor(conversation.Presence);
        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = conversation.Username;
        cell.AccessibilityValue = AppStrings.Format(
            "IosUiConversationSummary",
            PresenceLabel(conversation.Presence),
            conversation.UnreadCount > 0
                ? AppStrings.Format("IosUiUnreadCount", conversation.UnreadCount)
                : AppStrings.Get("IosUiRead"),
            $"{conversation.Timestamp}. {conversation.Preview}");
        cell.AccessibilityHint = AppStrings.Get("IosUiOpenConversation");
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        ConversationSummary? conversation = ConversationAt(indexPath);
        if (conversation is null)
        {
            return;
        }

        if (Editing)
        {
            selectedUsernames.Add(conversation.Username);
            UpdateSelectionToolbar();
            return;
        }

        tableView.DeselectRow(indexPath, true);
        Open(conversation.Username);
    }

    /// <inheritdoc/>
    public override void RowDeselected(UITableView tableView, NSIndexPath indexPath)
    {
        if (Editing)
        {
            if (ConversationAt(indexPath) is { } conversation)
            {
                selectedUsernames.Remove(conversation.Username);
            }

            UpdateSelectionToolbar();
        }
    }

    /// <inheritdoc/>
    public override UISwipeActionsConfiguration? GetTrailingSwipeActionsConfiguration(
        UITableView tableView,
        NSIndexPath indexPath)
    {
        if (ConversationAt(indexPath) is not { } conversation)
        {
            return null;
        }

        UIContextualAction delete = UIContextualAction.FromContextualActionStyle(
            UIContextualActionStyle.Destructive,
            AppStrings.Get("IosUiDelete"),
            (_, _, completion) =>
            {
                store.BeginDeletion();
                bool removed = store.DeleteConversation(conversation.Username);
                completion(removed);
                if (removed)
                {
                    ShowUndoBanner();
                }
            });
        delete.Image = UIImage.GetSystemImage("trash");
        return UISwipeActionsConfiguration.FromActions([delete]);
    }

    /// <inheritdoc/>
    public override UISwipeActionsConfiguration? GetLeadingSwipeActionsConfiguration(
        UITableView tableView,
        NSIndexPath indexPath)
    {
        if (ConversationAt(indexPath) is not { } conversation)
        {
            return null;
        }

        UIContextualAction read = UIContextualAction.FromContextualActionStyle(
            UIContextualActionStyle.Normal,
            AppStrings.Get("IosUiRead"),
            (_, _, completion) =>
            {
                store.MarkRead(conversation.Username);
                completion(true);
            });
        read.Image = UIImage.GetSystemImage("envelope.open");
        read.BackgroundColor = UIColor.SystemBlue;
        return UISwipeActionsConfiguration.FromActions([read]);
    }

    /// <inheritdoc/>
    public override void SetEditing(bool editing, bool animated)
    {
        base.SetEditing(editing, animated);
        TableView.SetEditing(editing, animated);
        NavigationItem.LeftBarButtonItem = conversationsByUsername.Count > 0 ? EditButtonItem : null;
        if (editing)
        {
            RestoreStableSelection();
            UpdateSelectionToolbar(animated);
        }
        else
        {
            selectedUsernames.Clear();
            this.SetSelectionAccessory([], animated);
        }

        ApplyConversationSnapshot(presentation.Conversations, animated: false);
    }

    /// <inheritdoc/>
    public override UITableViewCellEditingStyle EditingStyleForRow(
        UITableView tableView,
        NSIndexPath indexPath) => UITableViewCellEditingStyle.None;

    /// <inheritdoc/>
    public override bool ShouldIndentWhileEditing(UITableView tableView, NSIndexPath indexPath) => false;

    /// <inheritdoc/>
    public void Receive(AppRoute route)
    {
        if (route is AppRoute.Messages { Username: { Length: > 0 } username })
        {
            Open(username);
        }
    }

    /// <summary>Shows a username lookup that opens a conversation without prematurely sending content.</summary>
    private void PresentCompose()
    {
        var picker = new ComposeUserPickerViewController(store.GetComposeSuggestions(), Open);
        var navigation = new UINavigationController(picker)
        {
            ModalPresentationStyle = UIModalPresentationStyle.PageSheet,
        };
        PresentViewController(navigation, true, null);
    }

    /// <summary>Pushes one conversation and marks it read when it becomes visible.</summary>
    /// <param name="username">The remote user name.</param>
    private void Open(string username)
    {
        store.MarkRead(username);
        NavigationController?.PushViewController(
            new ConversationViewController(router, store, username, ownsStore: false),
            true);
    }

    /// <summary>Deletes every selected conversation as one undo transaction.</summary>
    private void DeleteSelection()
    {
        string[] usernames = selectedUsernames.ToArray();
        if (usernames.Length == 0)
        {
            return;
        }

        store.BeginDeletion();
        foreach (string username in usernames)
        {
            store.DeleteConversation(username);
        }

        SetEditing(false, true);
        ShowUndoBanner();
    }

    /// <summary>Updates the contextual toolbar with selected scope and a descriptive delete action.</summary>
    private void UpdateSelectionToolbar(bool animated = false)
    {
        int count = selectedUsernames.Count;
        var read = new UIBarButtonItem(
            UIImage.GetSystemImage("envelope.open")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => MarkSelectionRead())
        {
            Enabled = count > 0,
            AccessibilityLabel = AppStrings.Get("IosUiMarkSelectedRead"),
            AccessibilityIdentifier = "messages.mark-selected-read",
        };
        var scope = new UIBarButtonItem(
            AppStrings.Format("IosUiSelectedMessages", count),
            UIBarButtonItemStyle.Plain,
            (_, _) => { })
        {
            Enabled = false,
        };
        var delete = new UIBarButtonItem(
            UIImage.GetSystemImage("trash")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => DeleteSelection())
        {
            Enabled = count > 0,
            TintColor = UIColor.SystemRed,
            AccessibilityLabel = AppStrings.Get("IosUiDeleteSelectedConversations"),
            AccessibilityIdentifier = "messages.delete-selected",
        };
        var userActions = new UIBarButtonItem(
            UIImage.GetSystemImage("person.crop.circle")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentSelectedUserActions())
        {
            Enabled = count == 1,
            AccessibilityLabel = AppStrings.Get("IosUiSelectedUserActions"),
            AccessibilityIdentifier = "messages.selected-user-actions",
        };
        this.SetSelectionAccessory(
        [
            scope,
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            read,
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            userActions,
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            delete,
        ],
            animated);
    }

    /// <summary>Marks the selected conversation scope read without leaving edit mode ambiguously active.</summary>
    private void MarkSelectionRead()
    {
        string[] usernames = selectedUsernames.ToArray();
        store.MarkRead(usernames);
        SetEditing(false, true);
        AccessibilityExtensions.Announce(AppStrings.Get("IosUiMarkedSelectedRead"));
    }

    /// <summary>Shows a persistent, focus-safe undo affordance after destructive removal.</summary>
    private void ShowUndoBanner()
    {
        showsUndoBanner = true;
        var label = UIKitFactory.Label(UIFontTextStyle.Callout);
        label.Text = AppStrings.Get("IosUiConversationDeleted");
        var undo = UIKitFactory.Button(
            AppStrings.Get("IosUiUndo"),
            UIButtonConfiguration.TintedButtonConfiguration,
            () =>
            {
                if (store.UndoDelete())
                {
                    showsUndoBanner = false;
                    TableView.TableHeaderView = null;
                    _ = RefreshOverviewAsync(showLoading: false);
                    AccessibilityExtensions.Announce(AppStrings.Get("IosUiSaved"));
                }
            },
            "arrow.uturn.backward");
        var stack = new UIStackView([label, undo])
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Center,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 12,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var container = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemBackground,
            AccessibilityIdentifier = "messages.undo-banner",
        };
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor, 8),
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -8),
        ]);
        TableView.SetSelfSizingHeader(container);
        AccessibilityExtensions.Announce(
            $"{AppStrings.Get("IosUiConversationDeleted")}. {AppStrings.Get("IosUiUndo")}");
    }

    /// <summary>Shows the reusable actions supported for exactly one stable edit-mode selection.</summary>
    private void PresentSelectedUserActions()
    {
        if (selectedUsernames.SingleOrDefault() is { Length: > 0 } username)
        {
            PresentUserActions(username);
        }
    }

    /// <summary>Presents the common profile, browse, search, and conversation actions for a selected peer.</summary>
    /// <param name="username">The selected remote user.</param>
    private void PresentUserActions(string username)
    {
        var sheet = UIAlertController.Create(
            username,
            AppStrings.Get("IosUiUserActions"),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiOpenConversation"),
            UIAlertActionStyle.Default,
            _ =>
            {
                SetEditing(false, true);
                Open(username);
            }));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiViewProfile"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.UserProfile(username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiBrowseUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Browse(username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSearchUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Search(Target: SearchRouteTarget.User, Subject: username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        UIKitFactory.AnchorActionSheet(
            sheet,
            View!,
            new CGRect(View!.Bounds.GetMidX(), View.Bounds.Height - 44, 1, 1));

        PresentViewController(sheet, true, null);
    }

    /// <summary>Loads one immutable overview off the main thread and ignores obsolete refresh completions.</summary>
    /// <param name="showLoading">Whether a first-load presentation should be shown immediately.</param>
    private async Task RefreshOverviewAsync(bool showLoading)
    {
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(ref refreshCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        if (showLoading)
        {
            ApplyPresentation(store.GetLoadingOverview(), animated: false);
        }

        try
        {
            MessagesOverviewPresentation next = await store.RefreshOverviewAsync(cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                ApplyPresentation(next, animated: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(refreshCancellation, cancellation))
            {
                RefreshControl?.EndRefreshing();
            }
        }
    }

    /// <summary>Applies stable username diffs and maps the complete state to empty or retained-content UI.</summary>
    /// <param name="next">The next immutable presentation.</param>
    /// <param name="animated">Whether structural changes should animate.</param>
    private void ApplyPresentation(MessagesOverviewPresentation next, bool animated)
    {
        presentation = next;
        ApplyConversationSnapshot(next.Conversations, animated);
        NavigationItem.LeftBarButtonItem = next.Conversations.Count > 0 ? EditButtonItem : null;
        markAllReadButton.Enabled = next.Conversations.Any(item => item.UnreadCount > 0);
        UpdateStatePresentation();
    }

    /// <summary>Applies a bounded table diff while retaining edit selection by username across reordering.</summary>
    /// <param name="conversations">The ordered detached rows.</param>
    /// <param name="animated">Whether UIKit should animate the diff.</param>
    private void ApplyConversationSnapshot(
        IReadOnlyList<ConversationSummary> conversations,
        bool animated)
    {
        conversationsByUsername.Clear();
        foreach (ConversationSummary conversation in conversations)
        {
            conversationsByUsername[conversation.Username] = conversation;
        }

        selectedUsernames.IntersectWith(conversationsByUsername.Keys);
        if (TableView.Window is null)
        {
            pendingConversationSnapshot = true;
            return;
        }

        NSString[] identifiers = conversations
            .Select(conversation => new NSString(conversation.Username))
            .ToArray();
        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSString>();
        snapshot.AppendSections([SectionIdentifier]);
        snapshot.AppendItems(identifiers, SectionIdentifier);
        HashSet<string> nextIdentifiers = conversationsByUsername.Keys.ToHashSet(StringComparer.Ordinal);
        NSString[] retained = dataSource.Snapshot.ItemIdentifiers
            .Where(identifier => nextIdentifiers.Contains(identifier.ToString()))
            .ToArray();
        if (retained.Length > 0)
        {
            snapshot.ReconfigureItems(retained);
        }

        dataSource.ApplySnapshot(snapshot, animated, () =>
        {
            RestoreStableSelection();
            if (Editing)
            {
                UpdateSelectionToolbar();
            }
        });
    }

    /// <summary>Restores native edit checkmarks from the stable username selection set.</summary>
    private void RestoreStableSelection()
    {
        if (TableView.Window is null)
        {
            return;
        }

        foreach (NSIndexPath selectedPath in TableView.IndexPathsForSelectedRows ?? [])
        {
            TableView.DeselectRow(selectedPath, false);
        }

        if (!Editing)
        {
            return;
        }

        foreach (string username in selectedUsernames)
        {
            if (dataSource.GetIndexPath(new NSString(username)) is { } indexPath)
            {
                TableView.SelectRow(indexPath, false, UITableViewScrollPosition.None);
            }
        }
    }

    /// <summary>Resolves a current visual row through the diffable username identifier.</summary>
    /// <param name="indexPath">The potentially stale visual index.</param>
    /// <returns>The current conversation, or null after a concurrent diff.</returns>
    private ConversationSummary? ConversationAt(NSIndexPath indexPath) =>
        dataSource.GetItemIdentifier(indexPath) is { } identifier
            ? conversationsByUsername.GetValueOrDefault(identifier.ToString())
            : null;

    /// <summary>Maps the typed phase to full-screen or retained-content status treatment.</summary>
    private void UpdateStatePresentation()
    {
        if (showsUndoBanner)
        {
            ContentStateView.Clear(this);
            return;
        }

        if (presentation.Conversations.Count == 0)
        {
            TableView.TableHeaderView = null;
            ContentStatePresentation state = presentation.Phase switch
            {
                MessagesOverviewPhase.Loading => new ContentStatePresentation(
                    AppStrings.Get("IosUiLoadingMessages"),
                    IsLoading: true),
                MessagesOverviewPhase.Offline => new ContentStatePresentation(
                    AppStrings.Get("IosUiMessagesOffline"),
                    AppStrings.Get("IosUiMessagesOfflineDetail"),
                    "wifi.slash",
                    AppStrings.Get("IosUiComposeMessage"),
                    PresentCompose),
                MessagesOverviewPhase.RecoverableError => new ContentStatePresentation(
                    AppStrings.Get("IosUiMessagesRefreshFailed"),
                    AppStrings.Get("IosUiMessagesRefreshFailedDetail"),
                    "exclamationmark.arrow.triangle.2.circlepath",
                    AppStrings.Get("IosUiRetry"),
                    () => _ = RefreshOverviewAsync(showLoading: true)),
                _ => new ContentStatePresentation(
                    AppStrings.Get("IosUiNoMessages"),
                    AppStrings.Get("IosUiNoMessagesDetail"),
                    "bubble.left.and.bubble.right",
                    AppStrings.Get("IosUiComposeMessage"),
                    PresentCompose),
            };
            ContentStateView.Show(this, state);
            return;
        }

        ContentStateView.Clear(this);
        switch (presentation.Phase)
        {
            case MessagesOverviewPhase.Offline:
                ShowStatusHeader(
                    AppStrings.Get("IosUiMessagesOffline"),
                    AppStrings.Get("IosUiMessagesOfflineDetail"),
                    "wifi.slash");
                break;
            case MessagesOverviewPhase.RecoverableError:
                ShowStatusHeader(
                    AppStrings.Get("IosUiMessagesRefreshFailed"),
                    AppStrings.Get("IosUiMessagesRefreshFailedDetail"),
                    "exclamationmark.arrow.triangle.2.circlepath",
                    AppStrings.Get("IosUiRetry"),
                    () => _ = RefreshOverviewAsync(showLoading: false));
                break;
            default:
                TableView.TableHeaderView = null;
                break;
        }
    }

    /// <summary>Shows a self-sizing, non-color-only status above retained conversations.</summary>
    private void ShowStatusHeader(
        string title,
        string detail,
        string symbolName,
        string? actionTitle = null,
        Action? action = null)
    {
        var symbol = new UIImageView(UIImage.GetSystemImage(symbolName))
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = UIColor.SecondaryLabel,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        symbol.WidthAnchor.ConstraintEqualTo(24).Active = true;
        symbol.HeightAnchor.ConstraintEqualTo(24).Active = true;
        var titleLabel = UIKitFactory.Label(UIFontTextStyle.Headline);
        titleLabel.Text = title;
        var detailLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
        detailLabel.Text = detail;
        var text = UIKitFactory.VerticalStack(2);
        text.AddArrangedSubview(titleLabel);
        text.AddArrangedSubview(detailLabel);
        var row = new UIStackView([symbol, text])
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Top,
            Spacing = 10,
        };
        var stack = UIKitFactory.VerticalStack(8);
        stack.AddArrangedSubview(row);
        if (!string.IsNullOrWhiteSpace(actionTitle) && action is not null)
        {
            stack.AddArrangedSubview(UIKitFactory.Button(
                actionTitle,
                UIButtonConfiguration.TintedButtonConfiguration,
                action,
                "arrow.clockwise"));
        }

        var container = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemBackground,
            AccessibilityIdentifier = "messages.status",
        };
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor, 10),
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -10),
        ]);
        TableView.SetSelfSizingHeader(container);
    }

    /// <summary>Builds the two-line conversation preview with timestamp and explicit unread status.</summary>
    /// <param name="conversation">The overview row.</param>
    /// <returns>Secondary row text.</returns>
    private static string BuildSecondaryText(ConversationSummary conversation) =>
        $"{conversation.Timestamp} · {(conversation.UnreadCount > 0 ? AppStrings.Format("IosUiUnreadCount", conversation.UnreadCount) : AppStrings.Get("IosUiRead"))}\n{conversation.Preview}";

    /// <summary>Gets localized non-color presence text.</summary>
    /// <param name="presence">The optional presence.</param>
    /// <returns>A localized status.</returns>
    private static string PresenceLabel(UserPresence? presence) => presence switch
    {
        UserPresence.Online => AppStrings.Get("IosUiPresenceOnline"),
        UserPresence.Away => AppStrings.Get("IosUiPresenceAway"),
        UserPresence.Offline => AppStrings.Get("IosUiPresenceOffline"),
        _ => AppStrings.Get("IosUiPresenceUnknown"),
    };

    /// <summary>Gets an SF Symbol that reinforces presence without replacing its text.</summary>
    /// <param name="presence">The optional presence.</param>
    /// <returns>An SF Symbol name.</returns>
    private static string PresenceSymbol(UserPresence? presence) => presence switch
    {
        UserPresence.Online => "checkmark.circle.fill",
        UserPresence.Away => "clock.fill",
        UserPresence.Offline => "circle",
        _ => "questionmark.circle",
    };

    /// <summary>Gets the local semantic tint that supplements presence text and symbol.</summary>
    /// <param name="presence">The optional presence.</param>
    /// <returns>A dynamic system color.</returns>
    private static UIColor PresenceColor(UserPresence? presence) => presence switch
    {
        UserPresence.Online => UIColor.SystemGreen,
        UserPresence.Away => UIColor.SystemOrange,
        _ => UIColor.SecondaryLabel,
    };

    /// <summary>Reloads overview content after a presentation-store change.</summary>
    /// <param name="sender">The message store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args) =>
        _ = RefreshOverviewAsync(showLoading: false);

    /// <summary>Enables native swipe editing while diffable data owns row counts and cell provisioning.</summary>
    private sealed class ConversationDataSource(
        UITableView tableView,
        UITableViewDiffableDataSourceCellProvider cellProvider)
        : UITableViewDiffableDataSource<NSString, NSString>(tableView, cellProvider)
    {
        /// <inheritdoc/>
        public override bool CanEditRow(UITableView tableView, NSIndexPath indexPath) => true;
    }

    /// <summary>Presents a native, searchable list of saved/recent peers plus a validated entered username.</summary>
    private sealed class ComposeUserPickerViewController : UITableViewController, IUISearchResultsUpdating
    {
        private readonly IReadOnlyList<string> knownUsers;
        private readonly Action<string> selected;
        private readonly UISearchController searchController = new(searchResultsController: null);
        private IReadOnlyList<ComposeCandidate> candidates = [];

        /// <summary>Creates the compose picker.</summary>
        /// <param name="knownUsers">Saved and existing-conversation peers.</param>
        /// <param name="selected">Receives the chosen or entered username after dismissal.</param>
        public ComposeUserPickerViewController(IReadOnlyList<string> knownUsers, Action<string> selected)
            : base(UITableViewStyle.InsetGrouped)
        {
            this.knownUsers = knownUsers ?? throw new ArgumentNullException(nameof(knownUsers));
            this.selected = selected ?? throw new ArgumentNullException(nameof(selected));
            Title = AppStrings.Get("IosUiComposeMessage");
        }

        /// <inheritdoc/>
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.SystemGroupedBackground;
            View.AccessibilityIdentifier = "messages.compose-picker";
            TableView.RowHeight = UITableView.AutomaticDimension;
            TableView.EstimatedRowHeight = 58;
            NavigationItem.LeftBarButtonItem = new UIBarButtonItem(
                UIBarButtonSystemItem.Cancel,
                (_, _) => DismissViewController(true, null))
            {
                AccessibilityIdentifier = "messages.compose.cancel",
            };
            searchController.SearchResultsUpdater = this;
            searchController.ObscuresBackgroundDuringPresentation = false;
            searchController.SearchBar.Placeholder = AppStrings.Get("IosUiSearchPeoplePlaceholder");
            searchController.SearchBar.AutocapitalizationType = UITextAutocapitalizationType.None;
            searchController.SearchBar.AutocorrectionType = UITextAutocorrectionType.No;
            searchController.SearchBar.AccessibilityIdentifier = "messages.compose.search";
            NavigationItem.SearchController = searchController;
            NavigationItem.HidesSearchBarWhenScrolling = false;
            DefinesPresentationContext = true;
            ReloadCandidates();
        }

        /// <inheritdoc/>
        public void UpdateSearchResultsForSearchController(UISearchController searchController) =>
            ReloadCandidates();

        /// <inheritdoc/>
        public override nint RowsInSection(UITableView tableView, nint section) => candidates.Count;

        /// <inheritdoc/>
        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
        {
            ComposeCandidate candidate = candidates[indexPath.Row];
            var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
            {
                Accessory = UITableViewCellAccessory.DisclosureIndicator,
                AccessibilityIdentifier =
                    $"messages.compose.user.{AccessibilityIdentifiers.Opaque(candidate.Username)}",
            };
            UIListContentConfiguration content = cell.DefaultContentConfiguration;
            content.Text = candidate.Username;
            content.SecondaryText = AppStrings.Get(
                candidate.IsKnown ? "IosUiSavedOrRecentUser" : "IosUiEnteredUsername");
            content.Image = UIImage.GetSystemImage(candidate.IsKnown ? "person.crop.circle" : "plus.circle");
            content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
            content.TextProperties.AdjustsFontForContentSizeCategory = true;
            content.TextProperties.NumberOfLines = 0;
            content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
            content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
            content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
            cell.ContentConfiguration = content;
            cell.AccessibilityLabel = candidate.Username;
            cell.AccessibilityValue = content.SecondaryText;
            cell.AccessibilityHint = AppStrings.Get("IosUiOpenConversation");
            return cell;
        }

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            string username = candidates[indexPath.Row].Username;
            DismissViewController(true, () => selected(username));
        }

        /// <summary>Filters known peers and adds an explicit row for a non-empty unknown username.</summary>
        private void ReloadCandidates()
        {
            string query = searchController.SearchBar.Text?.Trim() ?? string.Empty;
            IEnumerable<ComposeCandidate> matches = knownUsers
                .Where(username => query.Length == 0 ||
                    username.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Select(username => new ComposeCandidate(username, IsKnown: true));
            candidates = query.Length > 0 && !knownUsers.Contains(query, StringComparer.OrdinalIgnoreCase)
                ? [new ComposeCandidate(query, IsKnown: false), .. matches]
                : matches.ToArray();
            TableView.ReloadData();

            if (candidates.Count == 0)
            {
                ContentStateView.Show(
                    this,
                    new ContentStatePresentation(
                        AppStrings.Get("IosUiNoSavedPeople"),
                        AppStrings.Get("IosUiSearchPeopleToCompose"),
                        "person.crop.circle.badge.questionmark"));
            }
            else
            {
                ContentStateView.Clear(this);
            }
        }

        private sealed record ComposeCandidate(string Username, bool IsKnown);
    }
}

/// <summary>Presents one self-sizing private-message thread and a keyboard-safe composer.</summary>
internal sealed class ConversationViewController : UIViewController
{
    private static readonly NSString MessageSectionIdentifier = new("messages");
    private const string MessageReuseIdentifier = "MessageBubbleCell";

    // A single body line centered in the 44-point resting field; the field grows from there. These match the
    // room composer so both conversations behave and measure identically.
    private const int ComposerVerticalInset = 11;
    private const int ComposerLeadingInset = 16;
    private const int ComposerMaxLines = 5;
    private const double ComposerMaxHeightFraction = 0.3;
    private const int ComposerRestingHeight = 44;
    private const int ComposerBottomInset = 20;
    private const int ComposerKeyboardGap = 8;

    // Matches the floating tab bar's inset so the composer and the bar read as one stack of controls.
    private const int ComposerSideInset = 22;
    private readonly IAppRouter router;
    private readonly MessagesPresentationStore store;
    private readonly string username;
    private readonly bool ownsStore;
    private readonly UITableView table = new(CGRect.Empty, UITableViewStyle.Plain)
    {
        TranslatesAutoresizingMaskIntoConstraints = false,
        RowHeight = UITableView.AutomaticDimension,
        EstimatedRowHeight = 72,
        KeyboardDismissMode = UIScrollViewKeyboardDismissMode.Interactive,
    };
    // The composer is an unfilled container so the field floats over the conversation, rather than a bar whose
    // edge welds itself to whatever sits below the screen.
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

    // A conversation composer has to accept line breaks, so the input is a text view whose Return key inserts
    // one and whose height follows its content. Sending is the send control's job alone — as in a room.
    private readonly UITextView messageView = new()
    {
        TranslatesAutoresizingMaskIntoConstraints = false,
        BackgroundColor = UIColor.Clear,
        Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body),
        AdjustsFontForContentSizeCategory = true,
        ScrollEnabled = false,
        TextContainerInset = UIEdgeInsets.Zero,
        ReturnKeyType = UIReturnKeyType.Default,
    };

    // A text view has no placeholder of its own, so the hint is a label the field's own text covers.
    private readonly UILabel placeholderLabel = UIKitFactory.Label(
        UIFontTextStyle.Body,
        UIColor.PlaceholderText,
        lines: 1);
    private readonly UILabel sendStatus = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
    private NSLayoutConstraint composerHeight = null!;
    private UITextViewDelegate? composerDelegate;
    private readonly Dictionary<string, PrivateMessageRow> messagesById = new(StringComparer.Ordinal);
    private UITableViewDiffableDataSource<NSString, NSString> messageDataSource = null!;
    private MessageDelegate messageDelegate = null!;
    private UIButton sendButton = null!;
    private UIButton jumpToLatestButton = null!;
    private KeyboardDismissTap? dismissTap;
    private IReadOnlyList<PrivateMessageRow> messages = [];

    // Whether the thread is following its newest message. This is latched rather than measured, because the
    // keyboard shrinks the table without anyone scrolling: measuring at that moment would report "reading
    // history" and abandon the conversation halfway up the screen.
    private bool followsLatest = true;

    // The last height the thread was laid out at, so a keyboard or a grown composer can be told apart from an
    // ordinary layout pass.
    private nfloat lastTableHeight;
    private bool swipeOpen;
    private bool reloadDeferred;
    private bool hasMessageSnapshot;
    private bool pendingMessageSnapshot;
    private bool pendingScrollToLatest;
    private bool deleted;

    /// <summary>Creates one conversation detail controller.</summary>
    /// <param name="router">The typed cross-feature router.</param>
    /// <param name="store">The private-message presentation store.</param>
    /// <param name="username">The remote user name.</param>
    public ConversationViewController(
        IAppRouter router,
        MessagesPresentationStore store,
        string username,
        bool ownsStore = true)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ownsStore = ownsStore;
        this.username = string.IsNullOrWhiteSpace(username)
            ? throw new ArgumentException("A user name is required.", nameof(username))
            : username;
        Title = AppStrings.Format("MessagesWithUser", username);
    }

    /// <summary>Gets the remote user represented by this controller for idempotent routing.</summary>
    public string Username => username;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = $"messages.thread.{AccessibilityIdentifiers.Opaque(username)}";
        ConfigureNavigation();
        ConfigureHierarchy();
        ReloadMessages(scrollToLatest: true);
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        store.MarkRead(username);
        ReloadMessages(scrollToLatest: true);
    }

    /// <inheritdoc/>
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (pendingMessageSnapshot)
        {
            pendingMessageSnapshot = false;
            bool scrollToLatest = pendingScrollToLatest;
            pendingScrollToLatest = false;
            ReloadMessages(scrollToLatest);
        }
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnChanged;
        base.ViewDidDisappear(animated);
    }

    /// <inheritdoc/>
    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();

        // The field's fitted height depends on its resolved width, so it is re-measured once layout settles —
        // which also re-measures it after a Dynamic Type change.
        UpdateComposerHeight();
        KeepFollowingLatest();
    }

    /// <summary>Re-anchors the newest message whenever the keyboard or a grown composer shortens the table.</summary>
    /// <remarks>
    /// The table's bottom rides the composer, which rides the keyboard, so raising the keyboard shortens the
    /// thread while its content offset stays put — which pushes the newest message below the fold. This runs
    /// on every frame of that animation, so the conversation stays pinned to its latest message instead of
    /// sliding out from under the person reading it.
    /// </remarks>
    private void KeepFollowingLatest()
    {
        if (Math.Abs(table.Bounds.Height - lastTableHeight) < 0.5)
        {
            return;
        }

        lastTableHeight = table.Bounds.Height;
        if (followsLatest)
        {
            ScrollToLatest(animated: false);
        }
        else
        {
            UpdateJumpToLatestVisibility();
        }
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

    /// <summary>Configures user actions and destructive conversation management.</summary>
    private void ConfigureNavigation()
    {
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            UIImage.GetSystemImage("ellipsis.circle")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentConversationActions())
        {
            AccessibilityLabel = AppStrings.Get("IosUiConversationActions"),
            AccessibilityIdentifier = "messages.thread.actions",
        };
    }

    /// <summary>Builds the table and keyboard-layout-guide composer using directional constraints.</summary>
    private void ConfigureHierarchy()
    {
        // Replying is a gesture on the row itself, so the transcript needs no native row editing at all.
        messageDataSource = new UITableViewDiffableDataSource<NSString, NSString>(table, ConfigureMessageCell);
        messageDelegate = new MessageDelegate(
            MessageAt,
            PresentMessageActions,
            ThreadScrolled);
        table.DataSource = messageDataSource;
        table.Delegate = messageDelegate;
        table.AccessibilityIdentifier = "messages.thread.list";
        messageView.AccessibilityIdentifier = "messages.thread.composer";
        messageView.AccessibilityLabel = AppStrings.Get("IosUiMessagePlaceholder");
        messageView.TextContainer.LineFragmentPadding = 0;
        composerDelegate = new ComposerTextViewDelegate(this);
        messageView.Delegate = composerDelegate;
        placeholderLabel.Text = AppStrings.Get("IosUiMessagePlaceholder");
        placeholderLabel.IsAccessibilityElement = false;
        UIKitFactory.ApplyGrowingFieldCorners(fieldBackground);
        UIKitFactory.ApplyComposerFieldBorder(fieldBackground);
        fieldBackground.AddSubviews(messageView, placeholderLabel);

        // Sending was the only way out of the keyboard, which trapped the thread at half height. A tap on the
        // conversation now puts it away and gives the history its full height back.
        dismissTap = new KeyboardDismissTap(
            table,
            () => messageView.IsFirstResponder,
            () => View!.EndEditing(true));
        sendStatus.Lines = 0;
        sendStatus.AccessibilityIdentifier = "messages.thread.send-status";
        sendStatus.Hidden = true;
        sendButton = UIKitFactory.CircularAction(
            "arrow.up",
            AppStrings.Get("IosUiSend"),
            () => _ = SendAsync(),
            filled: true);
        sendButton.AccessibilityIdentifier = "messages.thread.send";

        // The jump control floats over the conversation instead of taking a full row of the composer.
        jumpToLatestButton = UIKitFactory.CircularAction(
            "chevron.down",
            AppStrings.Get("IosUiJumpToLatest"),
            () => ScrollToLatest(animated: true));
        jumpToLatestButton.AccessibilityIdentifier = "messages.thread.jump-to-latest";
        jumpToLatestButton.Hidden = true;

        // A growing field must not drag the send control up with it: the two stay aligned on their bottom edges.
        var inputRow = new UIStackView([fieldBackground, sendButton])
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Bottom,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 8,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var stack = new UIStackView([sendStatus, inputRow])
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 6,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        composer.DirectionalLayoutMargins = new NSDirectionalEdgeInsets(0, ComposerSideInset, 0, ComposerSideInset);
        composer.AddSubview(stack);
        View!.AddSubviews(table, composer, jumpToLatestButton);
        composerHeight = messageView.HeightAnchor.ConstraintEqualTo(
            messageView.Font?.LineHeight ?? ComposerRestingHeight - (2 * ComposerVerticalInset));

        // The composer rides the keyboard, but never below a clear gap above the bottom safe area, so it keeps
        // an obvious margin from the tab bar. The keyboard constraint yields to the required clamp when the
        // keyboard is dismissed.
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
                -(ComposerBottomInset - ComposerKeyboardGap)),
            stack.TopAnchor.ConstraintEqualTo(composer.TopAnchor, 8),
            stack.BottomAnchor.ConstraintEqualTo(composer.BottomAnchor, -ComposerKeyboardGap),
            stack.LeadingAnchor.ConstraintEqualTo(composer.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(composer.LayoutMarginsGuide.TrailingAnchor),
            messageView.TopAnchor.ConstraintEqualTo(fieldBackground.TopAnchor, ComposerVerticalInset),
            messageView.BottomAnchor.ConstraintEqualTo(fieldBackground.BottomAnchor, -ComposerVerticalInset),
            messageView.LeadingAnchor.ConstraintEqualTo(fieldBackground.LeadingAnchor, ComposerLeadingInset),
            messageView.TrailingAnchor.ConstraintEqualTo(fieldBackground.TrailingAnchor, -ComposerLeadingInset),
            composerHeight,
            placeholderLabel.TopAnchor.ConstraintEqualTo(messageView.TopAnchor),
            placeholderLabel.LeadingAnchor.ConstraintEqualTo(messageView.LeadingAnchor),
            placeholderLabel.TrailingAnchor.ConstraintLessThanOrEqualTo(messageView.TrailingAnchor),
            fieldBackground.HeightAnchor.ConstraintGreaterThanOrEqualTo(ComposerRestingHeight),

            // The jump control floats over the conversation, aligned to the composer's own trailing edge.
            jumpToLatestButton.TrailingAnchor.ConstraintEqualTo(composer.LayoutMarginsGuide.TrailingAnchor),
            jumpToLatestButton.BottomAnchor.ConstraintEqualTo(composer.TopAnchor, -12),
        ]);

        // An empty draft has nothing to send, so the control starts disabled rather than inviting a no-op.
        ComposerChanged();
    }

    /// <summary>Shows or clears the composer's status line, collapsing the row when there is nothing to say.</summary>
    /// <param name="text">The status text, or empty to hide the line.</param>
    /// <param name="color">The semantic color carrying the status alongside its words.</param>
    private void ShowSendStatus(string text, UIColor color)
    {
        sendStatus.Text = text;
        sendStatus.TextColor = color;

        // A stack collapses the hidden row, so an idle composer gives its whole height to the input.
        sendStatus.Hidden = string.IsNullOrWhiteSpace(text);
    }

    /// <summary>Replaces the draft and its caret, keeping the placeholder, height, and send control truthful.</summary>
    /// <param name="text">The replacement draft.</param>
    /// <param name="caret">The caret offset to restore inside the new draft.</param>
    private void SetDraft(string text, int caret)
    {
        messageView.Text = text;

        // A programmatic assignment never reaches the delegate, so the derived composer state is refreshed here.
        ComposerChanged();
        messageView.SelectedRange = new NSRange(Math.Clamp(caret, 0, text.Length), 0);
    }

    /// <summary>Quotes one message into the draft and leaves the caret on the blank line beneath it.</summary>
    /// <param name="row">The message being replied to.</param>
    private void QuoteMessage(PrivateMessageRow row)
    {
        // A quote has to survive as one line of Markdown, so any newlines inside the original collapse to spaces.
        string quoted = string.Join(' ', row.Text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        string author = row.IsOutgoing ? store.CurrentUsername ?? AppStrings.Get("IosUiYou") : username;
        string prefix = $"{AppStrings.Format("IosUiRoomReplyQuote", author, quoted)}\n\n";
        string draft = messageView.Text ?? string.Empty;
        SetDraft(draft.Length == 0 ? prefix : prefix + draft, prefix.Length);
        messageView.BecomeFirstResponder();
        AccessibilityExtensions.Announce(AppStrings.Format("IosUiRoomReplyAdded", author));
    }

    /// <summary>Refreshes the placeholder, the composer height, and send availability after any draft edit.</summary>
    private void ComposerChanged()
    {
        placeholderLabel.Hidden = messageView.Text?.Length > 0;
        UpdateComposerHeight();
        sendButton.Enabled = messageView.Editable && !string.IsNullOrWhiteSpace(messageView.Text);
    }

    /// <summary>Grows the field with its content up to <see cref="ComposerMaxLines"/>, then lets it scroll.</summary>
    private void UpdateComposerHeight()
    {
        double width = messageView.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        double line = messageView.Font?.LineHeight ?? ComposerRestingHeight - (2 * ComposerVerticalInset);
        double fitted = messageView.SizeThatFits(new CGSize((nfloat)width, (nfloat)10000)).Height;

        // Five lines of an accessibility text size would swallow the conversation, so the field also never
        // claims more than a third of the screen — whichever limit is reached first wins.
        double ceiling = Math.Max(line, Math.Min(line * ComposerMaxLines, View!.Bounds.Height * ComposerMaxHeightFraction));
        double target = Math.Min(Math.Max(fitted, line), ceiling);
        messageView.ScrollEnabled = fitted > ceiling + 0.5;
        if (Math.Abs(composerHeight.Constant - target) < 0.5)
        {
            return;
        }

        composerHeight.Constant = (nfloat)target;
        View?.LayoutIfNeeded();
    }

    /// <summary>Sends the current draft with visible pending, failure, and completion feedback.</summary>
    private async Task SendAsync()
    {
        string text = messageView.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 || !sendButton.Enabled)
        {
            return;
        }

        // Sending is an explicit act of joining the conversation, so it always returns to the newest message
        // even when the person was reading history a moment ago.
        followsLatest = true;

        // Locking the field to block edits mid-flight also drops the keyboard, and the composer is now the only
        // way to send, so focus is handed back once the send resolves.
        bool composing = messageView.IsFirstResponder;
        sendButton.Enabled = false;
        messageView.Editable = false;
        ShowSendStatus(AppStrings.Get("IosUiMessageSending"), UIColor.SecondaryLabel);
        SetDraft(string.Empty, caret: 0);
        try
        {
            await store.SendAsync(username, text);
            ShowSendStatus(string.Empty, UIColor.SecondaryLabel);
            ReloadMessages(scrollToLatest: true);
        }
        catch
        {
            ShowSendStatus(AppStrings.Get("IosUiMessageSendFailed"), UIColor.SystemRed);
            SetDraft(text, text.Length);
            AccessibilityExtensions.Announce(sendStatus.Text);
        }
        finally
        {
            messageView.Editable = true;
            ComposerChanged();
            if (composing)
            {
                messageView.BecomeFirstResponder();
            }
        }
    }

    /// <summary>Shows copy, retry, and supported Soulseek-link actions for one message.</summary>
    /// <param name="row">The selected message.</param>
    private void PresentMessageActions(PrivateMessageRow row)
    {
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiMoreActions"),
            row.Timestamp,
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiReply"),
            UIAlertActionStyle.Default,
            _ => QuoteMessage(row)));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiCopyMessage"),
            UIAlertActionStyle.Default,
            _ => UIPasteboard.General.String = row.Text));
        if (row.DeliveryState == AppStrings.Get("IosUiMessageFailed"))
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiRetry"),
                UIAlertActionStyle.Default,
                action =>
                {
                    SetDraft(row.Text, row.Text.Length);
                    _ = SendAsync();
                }));
        }

        if (SoulseekLinkParser.TryParse(row.Text, out SoulseekLink? link) && link is not null)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiSoulseekLinkTitle"),
                UIAlertActionStyle.Default,
                _ => router.Navigate(new AppRoute.SoulseekLink(link))));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        UIKitFactory.AnchorActionSheet(sheet, table);

        PresentViewController(sheet, true, null);
    }

    /// <summary>Shows profile, browse, search, and destructive thread actions.</summary>
    private void PresentConversationActions()
    {
        var sheet = UIAlertController.Create(
            username,
            AppStrings.Get("IosUiConversationActions"),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiViewProfile"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.UserProfile(username))));
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
        sheet.AddAction(UIAlertAction.Create(
            deleted ? AppStrings.Get("IosUiUndo") : AppStrings.Get("IosUiDeleteConversation"),
            deleted ? UIAlertActionStyle.Default : UIAlertActionStyle.Destructive,
            _ =>
            {
                if (deleted)
                {
                    deleted = !store.UndoDelete();
                    if (!deleted)
                    {
                        table.TableHeaderView = null;
                        NavigationItem.RightBarButtonItem!.AccessibilityLabel =
                            AppStrings.Get("IosUiConversationActions");
                    }

                    ReloadMessages(scrollToLatest: true);
                }
                else
                {
                    ConfirmDelete();
                }
            }));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        UIKitFactory.AnchorActionSheet(sheet, View!, new CGRect(View!.Bounds.Width - 44, 0, 44, 44));

        PresentViewController(sheet, true, null);
    }

    /// <summary>Confirms local history deletion and retains a visible undo action.</summary>
    private void ConfirmDelete()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiDeleteConversation"),
            AppStrings.Get("IosUiDeleteConversationConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiDelete"),
            UIAlertActionStyle.Destructive,
            _ =>
            {
                store.BeginDeletion();
                deleted = store.DeleteConversation(username);
                if (deleted)
                {
                    ShowUndoBanner();
                    ReloadMessages(scrollToLatest: false);
                }
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Shows a persistent, self-sizing Undo action without hiding the empty thread state.</summary>
    private void ShowUndoBanner()
    {
        var label = UIKitFactory.Label(UIFontTextStyle.Callout);
        label.Text = AppStrings.Get("IosUiConversationDeleted");
        var undo = UIKitFactory.Button(
            AppStrings.Get("IosUiUndo"),
            UIButtonConfiguration.TintedButtonConfiguration,
            () =>
            {
                if (store.UndoDelete())
                {
                    deleted = false;
                    table.TableHeaderView = null;
                    NavigationItem.RightBarButtonItem!.AccessibilityLabel =
                        AppStrings.Get("IosUiConversationActions");
                    ReloadMessages(scrollToLatest: true);
                    AccessibilityExtensions.Announce(AppStrings.Get("IosUiSaved"));
                }
            },
            "arrow.uturn.backward");
        undo.AccessibilityIdentifier = "messages.thread.undo";
        var stack = new UIStackView([label, undo])
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Center,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 12,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var container = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemBackground,
            AccessibilityIdentifier = "messages.thread.undo-banner",
        };
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor, 8),
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -8),
        ]);
        table.SetSelfSizingHeader(container);
        AccessibilityExtensions.Announce(
            $"{AppStrings.Get("IosUiConversationDeleted")}. {AppStrings.Get("IosUiUndo")}");
    }

    /// <summary>Reloads self-sizing messages, empty state, and optional latest anchoring.</summary>
    /// <param name="scrollToLatest">Whether to reveal the newest row.</param>
    private void ReloadMessages(bool scrollToLatest)
    {
        messages = store.GetConversation(username);
        messagesById.Clear();
        foreach (PrivateMessageRow message in messages)
        {
            messagesById[message.StableId] = message;
        }

        NSString[] identifiers = messages
            .Select(message => new NSString(message.StableId))
            .ToArray();
        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSString>();
        snapshot.AppendSections([MessageSectionIdentifier]);
        snapshot.AppendItems(identifiers, MessageSectionIdentifier);
        HashSet<string> nextIds = messagesById.Keys.ToHashSet(StringComparer.Ordinal);
        NSString[] retained = messageDataSource.Snapshot.ItemIdentifiers
            .Where(identifier => nextIds.Contains(identifier.ToString()))
            .ToArray();
        if (retained.Length > 0)
        {
            snapshot.ReconfigureItems(retained);
        }

        table.BackgroundView = messages.Count == 0 ? CreateEmptyThreadView() : null;
        if (table.Window is null)
        {
            pendingMessageSnapshot = true;
            pendingScrollToLatest |= scrollToLatest;
            return;
        }

        bool animated = hasMessageSnapshot;
        hasMessageSnapshot = true;
        messageDataSource.ApplySnapshot(snapshot, animated, () =>
        {
            if (scrollToLatest && messages.Count > 0)
            {
                if (table.Window is null)
                {
                    pendingScrollToLatest = true;
                }
                else
                {
                    ScrollToLatest(animated: true);
                }
            }
            else if (table.Window is not null)
            {
                UpdateJumpToLatestVisibility();
            }
        });
    }

    /// <summary>Reveals the newest message and hides the explicit recovery control.</summary>
    /// <param name="animated">Whether native scrolling may animate when Reduce Motion permits it.</param>
    private void ScrollToLatest(bool animated)
    {
        followsLatest = true;
        if (messages.Count == 0)
        {
            jumpToLatestButton.Hidden = true;
            return;
        }

        // Auto-following must never yank the table out from under a touch: scrolling while a swipe or drag is
        // in flight cancels that gesture, which silently broke swipe-to-reply in a busy thread.
        if (table.Dragging || table.Tracking)
        {
            UpdateJumpToLatestVisibility();
            return;
        }

        // Self-sizing rows report an estimated height until UIKit has actually measured them, so an animated
        // scroll aims at an end that moves while it travels — which is how a long arriving message ended up
        // half-hidden behind the composer. Two unanimated passes land on the settled end instead: the first
        // forces the rows it passes to be measured, the second aims at what they turned out to be.
        if (messageDataSource.GetIndexPath(new NSString(messages[^1].StableId)) is not { } latest)
        {
            return;
        }

        CGPoint from = table.ContentOffset;
        table.LayoutIfNeeded();
        table.ScrollToRow(latest, UITableViewScrollPosition.Bottom, false);
        table.LayoutIfNeeded();
        table.ScrollToRow(latest, UITableViewScrollPosition.Bottom, false);

        // The exact destination is only known after those passes, so an animated request replays the journey
        // from where it started rather than aiming at an estimate.
        if (animated &&
            AccessibilityExtensions.AccessibleAnimationDuration(0.25) > 0 &&
            Math.Abs(from.Y - table.ContentOffset.Y) > 0.5)
        {
            CGPoint destination = table.ContentOffset;
            table.SetContentOffset(from, false);
            table.SetContentOffset(destination, true);
        }

        jumpToLatestButton.Hidden = true;
    }

    /// <summary>Shows Jump to Latest only while a person is intentionally reading older content.</summary>
    private void UpdateJumpToLatestVisibility() => jumpToLatestButton.Hidden = NearLatest;

    /// <summary>Keeps the follow decision in a person's own hands: only their own scrolling changes it.</summary>
    private void ThreadScrolled()
    {
        if (table.Dragging || table.Decelerating)
        {
            followsLatest = NearLatest;
        }

        UpdateJumpToLatestVisibility();
    }

    /// <summary>Creates a readable empty-thread state that leaves the composer available.</summary>
    /// <returns>A centered, noninteractive state view.</returns>
    private static UIView CreateEmptyThreadView()
    {
        var title = UIKitFactory.Label(UIFontTextStyle.Headline);
        title.Text = AppStrings.Get("IosUiEmptyConversation");
        title.TextAlignment = UITextAlignment.Center;
        var detail = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SecondaryLabel);
        detail.Text = AppStrings.Get("IosUiEmptyConversationDetail");
        detail.TextAlignment = UITextAlignment.Center;
        var stack = UIKitFactory.VerticalStack(8);
        stack.Alignment = UIStackViewAlignment.Fill;
        stack.AddArrangedSubview(title);
        stack.AddArrangedSubview(detail);
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

    /// <summary>Refreshes the visible thread while preserving the current reading location.</summary>
    /// <param name="sender">The message store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args)
    {
        // Reloading the table closes an open swipe, so an arriving message would snap Reply shut before it
        // could be pressed. The refresh waits until the swipe is resolved instead.
        if (swipeOpen)
        {
            reloadDeferred = true;
            return;
        }

        ReloadMessages(followsLatest);
        store.MarkRead(username);
    }

    /// <summary>Reports whether the thread is already showing its newest message.</summary>
    private bool NearLatest => messages.Count == 0 ||
        table.ContentSize.Height <= table.Bounds.Height ||
        table.ContentOffset.Y + table.Bounds.Height >= table.ContentSize.Height - 80;

    /// <summary>Tracks a reply drag and flushes whatever arrived while a row was held under a finger.</summary>
    /// <param name="open">Whether a row is currently being dragged.</param>
    private void SetSwipeOpen(bool open)
    {
        swipeOpen = open;
        if (open || !reloadDeferred)
        {
            return;
        }

        reloadDeferred = false;
        ReloadMessages(followsLatest);
        store.MarkRead(username);
    }

    /// <summary>Resolves one current message through its stable diffable identifier.</summary>
    /// <param name="indexPath">The potentially stale visual position.</param>
    /// <returns>The current row, or null after a concurrent snapshot update.</returns>
    private PrivateMessageRow? MessageAt(NSIndexPath indexPath) =>
        messageDataSource.GetItemIdentifier(indexPath) is { } identifier
            ? messagesById.GetValueOrDefault(identifier.ToString())
            : null;

    /// <summary>Configures one reusable leading/trailing message bubble.</summary>
    private UITableViewCell ConfigureMessageCell(
        UITableView tableView,
        NSIndexPath indexPath,
        NSObject identifier)
    {
        var cell = tableView.DequeueReusableCell(MessageReuseIdentifier) as MessageBubbleCell ??
            new MessageBubbleCell(MessageReuseIdentifier);
        // A reused row must never keep the previous message's reply target, so the gesture is repointed
        // even when the identifier no longer resolves after a concurrent snapshot.
        bool resolved = messagesById.TryGetValue(identifier.ToString(), out PrivateMessageRow? message);
        cell.ConfigureReply(resolved ? () => QuoteMessage(message!) : null, SetSwipeOpen);
        if (resolved)
        {
            cell.Configure(message!, username);
        }

        return cell;
    }

    /// <summary>Forwards stable message activation, swipe actions, and scrolling without owning row counts.</summary>
    private sealed class MessageDelegate(
        Func<NSIndexPath, PrivateMessageRow?> getMessage,
        Action<PrivateMessageRow> select,
        Action scrolled) : UITableViewDelegate
    {
        private readonly Func<NSIndexPath, PrivateMessageRow?> getMessage = getMessage;
        private readonly Action<PrivateMessageRow> select = select;
        private readonly Action scrolled = scrolled;

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            if (getMessage(indexPath) is { } message)
            {
                select(message);
            }
        }

        /// <inheritdoc/>
        public override void Scrolled(UIScrollView scrollView) => scrolled();
    }

    /// <summary>Keeps the placeholder, height, and send control in step with what a person types.</summary>
    /// <param name="owner">The presenting conversation controller.</param>
    private sealed class ComposerTextViewDelegate(ConversationViewController owner) : UITextViewDelegate
    {
        /// <inheritdoc/>
        public override void Changed(UITextView textView) => owner.ComposerChanged();
    }

    /// <summary>Renders a self-sizing semantic bubble aligned by incoming or outgoing direction.</summary>
    private sealed class MessageBubbleCell : UITableViewCell
    {
        private readonly UIView bubble = new()
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        private readonly UILabel body = UIKitFactory.Label(UIFontTextStyle.Body);
        private readonly UILabel metadata = UIKitFactory.Label(UIFontTextStyle.Caption1);
        private readonly UIImageView direction = new()
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        private readonly NSLayoutConstraint leadingAlignment;
        private readonly NSLayoutConstraint trailingAlignment;
        private readonly ReplySwipe replySwipe;

        /// <summary>Creates a reusable message bubble.</summary>
        /// <param name="reuseIdentifier">The table reuse identifier.</param>
        public MessageBubbleCell(string reuseIdentifier)
            : base(UITableViewCellStyle.Default, reuseIdentifier)
        {
            BackgroundColor = UIColor.SystemBackground;
            SelectionStyle = UITableViewCellSelectionStyle.Default;
            SeparatorInset = new UIEdgeInsets(0, 10_000, 0, 0);
            body.Lines = 0;
            body.IsAccessibilityElement = false;
            metadata.Lines = 0;
            metadata.IsAccessibilityElement = false;
            direction.WidthAnchor.ConstraintEqualTo(14).Active = true;
            direction.HeightAnchor.ConstraintEqualTo(14).Active = true;
            var metadataRow = new UIStackView([direction, metadata])
            {
                Axis = UILayoutConstraintAxis.Horizontal,
                Alignment = UIStackViewAlignment.Center,
                Spacing = 5,
            };
            var stack = new UIStackView([body, metadataRow])
            {
                Axis = UILayoutConstraintAxis.Vertical,
                Alignment = UIStackViewAlignment.Leading,
                Distribution = UIStackViewDistribution.Fill,
                Spacing = 5,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            UIKitFactory.ApplyRoundedContentCorners(bubble);
            bubble.AddSubview(stack);
            ContentView.AddSubview(bubble);
            leadingAlignment = bubble.LeadingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.LeadingAnchor);
            trailingAlignment = bubble.TrailingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.TrailingAnchor);
            NSLayoutConstraint.ActivateConstraints(
            [
                bubble.TopAnchor.ConstraintEqualTo(ContentView.TopAnchor, 5),
                bubble.BottomAnchor.ConstraintEqualTo(ContentView.BottomAnchor, -5),
                bubble.LeadingAnchor.ConstraintGreaterThanOrEqualTo(ContentView.LayoutMarginsGuide.LeadingAnchor),
                bubble.TrailingAnchor.ConstraintLessThanOrEqualTo(ContentView.LayoutMarginsGuide.TrailingAnchor),
                bubble.WidthAnchor.ConstraintLessThanOrEqualTo(ContentView.WidthAnchor, 0.82f),
                bubble.HeightAnchor.ConstraintGreaterThanOrEqualTo(44),
                stack.TopAnchor.ConstraintEqualTo(bubble.TopAnchor, 9),
                stack.BottomAnchor.ConstraintEqualTo(bubble.BottomAnchor, -9),
                stack.LeadingAnchor.ConstraintEqualTo(bubble.LeadingAnchor, 12),
                stack.TrailingAnchor.ConstraintEqualTo(bubble.TrailingAnchor, -12),
            ]);
            IsAccessibilityElement = true;
            AccessibilityTraits = UIAccessibilityTrait.Button;

            // The bubble carries the drag, not the content view, whose frame the table assigns directly.
            replySwipe = new ReplySwipe(this, bubble);
        }

        /// <inheritdoc/>
        public override void PrepareForReuse()
        {
            base.PrepareForReuse();
            replySwipe.Reset();
        }

        /// <summary>Points the drag and its VoiceOver equivalent at the message this row currently shows.</summary>
        /// <param name="reply">Quotes the current message into the composer, or null for a row with none.</param>
        /// <param name="dragging">Receives whether a drag is in flight, so arriving messages hold their reload.</param>
        public void ConfigureReply(Action? reply, Action<bool> dragging)
        {
            replySwipe.Configure(reply, dragging);

            // A drag is invisible to VoiceOver, so the same intent is offered as a rotor action.
            AccessibilityCustomActions = reply is null
                ? []
                :
                [
                    new UIAccessibilityCustomAction(
                        AppStrings.Get("IosUiReply"),
                        (UIAccessibilityCustomActionHandler)(_ =>
                        {
                            reply();
                            return true;
                        })),
                ];
        }

        /// <summary>Applies one immutable row and flips only the directional alignment constraint.</summary>
        /// <param name="message">The message to display.</param>
        /// <param name="username">The remote peer used by visible and assistive direction labels.</param>
        public void Configure(PrivateMessageRow message, string username)
        {
            leadingAlignment.Active = !message.IsOutgoing;
            trailingAlignment.Active = message.IsOutgoing;
            body.Text = message.Text;
            string sender = message.IsOutgoing
                ? AppStrings.Get("IosUiSentByYou")
                : AppStrings.Format("IosUiReceivedFromUser", username);
            string state = message.DeliveryState is null
                ? message.Timestamp
                : $"{message.Timestamp} · {message.DeliveryState}";
            metadata.Text = $"{sender} · {state}";
            direction.Image = UIImage.GetSystemImage(
                message.IsOutgoing ? "arrow.up.right" : "arrow.down.left");
            bubble.BackgroundColor = message.IsOutgoing
                ? UIColor.SystemBlue
                : UIColor.SecondarySystemBackground;
            body.TextColor = message.IsOutgoing ? UIColor.White : UIColor.Label;
            metadata.TextColor = message.DeliveryState == AppStrings.Get("IosUiMessageFailed")
                ? UIColor.SystemRed
                : message.IsOutgoing
                    ? UIColor.White.ColorWithAlpha(0.84f)
                    : UIColor.SecondaryLabel;
            direction.TintColor = message.IsOutgoing
                ? UIColor.White.ColorWithAlpha(0.84f)
                : UIColor.SecondaryLabel;
            AccessibilityIdentifier =
                $"messages.thread.message.{AccessibilityIdentifiers.Opaque(message.StableId)}";
            AccessibilityLabel = AppStrings.Format(
                message.IsOutgoing
                    ? "IosUiOutgoingMessageAccessibility"
                    : "IosUiIncomingMessageAccessibility",
                message.IsOutgoing ? message.Text : username,
                message.Text);
            AccessibilityValue = state;
            AccessibilityHint = AppStrings.Get("IosUiMoreActions");
        }
    }
}

/// <summary>Small convenience for safely changing a bar item's enabled state.</summary>
internal static class BarButtonItemExtensions
{
    /// <summary>Sets enabled state when the item exists.</summary>
    /// <param name="item">The optional bar item.</param>
    /// <param name="enabled">The requested state.</param>
    public static void SetEnabled(this UIBarButtonItem? item, bool enabled)
    {
        if (item is not null)
        {
            item.Enabled = enabled;
        }
    }
}
