using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation.Browse;
using Foundation;
using Seeker.Routing;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Browse;

/// <summary>
/// Presents recent-user entry, generation-safe peer browsing, folder drill-in, filtering, and explicit download actions.
/// </summary>
internal sealed class BrowseViewController : UIViewController, IAppRouteReceiving
{
    private readonly BrowsePresentationStore store;
    private readonly IAppRouter router;
    private readonly UITextField username = UIKitFactory.TextField(
        AppStrings.Get("IosUiBrowseUsernamePlaceholder"),
        UITextContentType.Username);
    private readonly UIButton browseButton;
    private readonly DiffableFeatureListView history = new(UICollectionLayoutListAppearance.InsetGrouped);
    private CancellationTokenSource? requestCancellation;
    private AppRoute.Browse? pendingRoute;
    private bool loaded;

    /// <summary>Creates the Browse root with its feature-local store and typed router.</summary>
    public BrowseViewController(BrowsePresentationStore store, IAppRouter router)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        browseButton = UIKitFactory.Button(
            AppStrings.Get("IosUiBrowseGo"),
            UIButtonConfiguration.FilledButtonConfiguration,
            () => _ = BrowseAsync(username.Text ?? string.Empty),
            "folder");
        Title = AppStrings.Get("browse_tab");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        loaded = true;
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = "browse.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;
        username.AccessibilityIdentifier = "browse.username";
        username.ReturnKeyType = UIReturnKeyType.Go;
        username.ShouldReturn = field =>
        {
            field.ResignFirstResponder();
            _ = BrowseAsync(field.Text ?? string.Empty);
            return true;
        };
        browseButton.AccessibilityIdentifier = "browse.submit";
        UIStackView input = new()
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Center,
            Spacing = 12,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        input.AddArrangedSubview(username);
        input.AddArrangedSubview(browseButton);
        View.AddSubviews(input, history);
        NSLayoutConstraint.ActivateConstraints([
            input.LeadingAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.LeadingAnchor),
            input.TrailingAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.TrailingAnchor),
            input.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 12),
            username.HeightAnchor.ConstraintGreaterThanOrEqualTo(44),
            history.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            history.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            history.TopAnchor.ConstraintEqualTo(input.BottomAnchor, 12),
            history.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
        history.ItemSelected += (_, value) => _ = BrowseAsync(value);
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            UIImage.GetSystemImage("trash")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => store.ClearHistory())
        {
            AccessibilityLabel = AppStrings.Get("IosUiClearHistory"),
            AccessibilityIdentifier = "browse.clear-history",
        };
        store.Changed += OnChanged;
        Render();
        if (pendingRoute is { } route)
        {
            ApplyRoute(route);
            pendingRoute = null;
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= OnChanged;
            requestCancellation?.Cancel();
            requestCancellation?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public void Receive(AppRoute route)
    {
        if (route is not AppRoute.Browse browse)
        {
            return;
        }

        if (!loaded)
        {
            pendingRoute = browse;
            return;
        }

        ApplyRoute(browse);
    }

    private void ApplyRoute(AppRoute.Browse route)
    {
        if (string.IsNullOrWhiteSpace(route.Username))
        {
            return;
        }

        username.Text = route.Username;
        _ = BrowseAsync(route.Username, route.Path, route.RequestedDownloadPath);
    }

    private async Task BrowseAsync(string user, string? path = null, string? requestedDownloadPath = null)
    {
        user = user.Trim();
        if (user.Length == 0)
        {
            ShowMessage(AppStrings.Get("must_type_a_username_to_browse"));
            username.BecomeFirstResponder();
            return;
        }

        requestCancellation?.Cancel();
        requestCancellation?.Dispose();
        requestCancellation = new CancellationTokenSource();
        try
        {
            await store.BrowseAsync(user, requestCancellation.Token);
            if (store.State.Phase is BrowsePhase.Content or BrowsePhase.Empty)
            {
                PushLocation(
                    store.ResolveLocation(path),
                    requestedDownloadPath is null ? null : store.ResolveDownloadTarget(requestedDownloadPath));
            }
        }
        catch (ArgumentException exception)
        {
            ShowMessage(exception.ParamName == "username"
                ? AppStrings.Get("must_type_a_username_to_browse")
                : AppStrings.Get("IosUiBrowseFailedDetail"));
        }
    }

    private void PushLocation(string path, string? requestedDownloadPath)
    {
        NavigationController?.PushViewController(
            new BrowseFolderViewController(store, router, path, requestedDownloadPath),
            true);
    }

    private void OnChanged(object? sender, EventArgs args) => Render();

    private void Render()
    {
        BrowseScreenState state = store.State;
        NavigationItem.Prompt = state.Phase is BrowsePhase.Content or BrowsePhase.Empty
            ? state.Message
            : null;
        NavigationItem.RightBarButtonItem!.Enabled = state.History.Count > 0;
        username.Text = state.Username.Length == 0 ? username.Text : state.Username;
        browseButton.Enabled = state.Phase != BrowsePhase.Contacting;
        if (state.Phase == BrowsePhase.Idle && state.History.Count > 0)
        {
            ContentStateView.Clear(this);
            history.Apply(state.History.Select(user => new FeatureListItem(
                user,
                user,
                AppStrings.Get("IosUiBrowseRecentUsers"),
                "person.crop.circle",
                ShowsDisclosure: true,
                AccessibilityLabel: user)).ToArray());
            return;
        }

        history.Apply([]);
        if (state.Phase == BrowsePhase.Contacting)
        {
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiBrowseContacting"),
                AppStrings.Get("IosUiBrowseContactingDetail"),
                IsLoading: true,
                ActionTitle: AppStrings.Get("IosUiCancel"),
                Action: store.CancelRequest));
            return;
        }

        if (state.Phase is BrowsePhase.Content or BrowsePhase.Empty)
        {
            ContentStateView.Clear(this);
            history.Apply(state.History.Select(user => new FeatureListItem(
                user,
                user,
                AppStrings.Get("IosUiBrowseRecentUsers"),
                "person.crop.circle",
                ShowsDisclosure: true,
                AccessibilityLabel: user)).ToArray());
            return;
        }

        ContentStateView.Show(this, StatePresentation(state));
    }

    private ContentStatePresentation StatePresentation(BrowseScreenState state) => state.Phase switch
    {
        BrowsePhase.Offline => new(AppStrings.Get("IosUiBrowseOffline"), state.Message, "wifi.slash"),
        BrowsePhase.TimedOut => RetryState("IosUiSearchTimedOut", state),
        BrowsePhase.DirectConnectionFailed => RetryState("IosUiBrowseDirectFailed", state),
        BrowsePhase.ParseFailed => RetryState("IosUiBrowseParseFailed", state),
        BrowsePhase.Canceled => RetryState("IosUiBrowseCanceled", state),
        BrowsePhase.Failed => RetryState("IosUiBrowseFailed", state),
        _ => new(
            AppStrings.Get("IosUiBrowseStart"),
            AppStrings.Get("IosUiBrowseStartDetail"),
            "folder"),
    };

    private ContentStatePresentation RetryState(string key, BrowseScreenState state) => new(
        AppStrings.Get(key),
        state.Message,
        "exclamationmark.folder",
        AppStrings.Get("retry"),
        () => _ = BrowseAsync(state.Username));

    private void ShowMessage(string message)
    {
        var alert = UIAlertController.Create(AppStrings.Get("IosUiBrowseFailed"), message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }
}

/// <summary>Presents one browse hierarchy location with native filtering and explicit file/folder download consent.</summary>
internal sealed class BrowseFolderViewController : UIViewController
{
    private readonly BrowsePresentationStore store;
    private readonly IAppRouter router;
    private readonly string location;
    private readonly DiffableFeatureListView list = new();
    private readonly UISearchController filter = new();
    private UIBarButtonItem? folderDownloadButton;
    private UIBarButtonItem? userActionsButton;
    private string? requestedDownloadPath;
    private string filterText = string.Empty;

    /// <summary>Creates one folder location and optionally queues a validated inbound target for confirmation.</summary>
    public BrowseFolderViewController(
        BrowsePresentationStore store,
        IAppRouter router,
        string location,
        string? requestedDownloadPath = null)
    {
        this.store = store;
        this.router = router;
        this.location = location ?? string.Empty;
        this.requestedDownloadPath = requestedDownloadPath;
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        filter.ObscuresBackgroundDuringPresentation = false;
        filter.SearchBar.Placeholder = AppStrings.Get("IosUiBrowseFilter");
        filter.SearchBar.TextChanged += (_, args) =>
        {
            filterText = args.SearchText ?? string.Empty;
            Render();
        };
        NavigationItem.SearchController = filter;
        NavigationItem.HidesSearchBarWhenScrolling = true;
        folderDownloadButton = new UIBarButtonItem(
            UIImage.GetSystemImage("arrow.down.to.line")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentFolderDownload(location))
        {
            AccessibilityLabel = AppStrings.Get("IosUiDownloadThisFolder"),
            AccessibilityIdentifier = "browse.folder-download",
        };
        userActionsButton = new UIBarButtonItem(
            UIImage.GetSystemImage("person.crop.circle")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentUserActions())
        {
            AccessibilityLabel = AppStrings.Get("IosUiMoreActions"),
            AccessibilityIdentifier = "browse.user-actions",
        };
        EditButtonItem.AccessibilityIdentifier = "browse.edit";
        NavigationItem.RightBarButtonItems = [EditButtonItem, folderDownloadButton, userActionsButton];
        View.AddSubview(list);
        NSLayoutConstraint.ActivateConstraints([
            list.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            list.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            list.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            list.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
        list.ItemSelected += (_, identifier) => Select(identifier);
        list.SelectionChanged += (_, _) => UpdateSelectionToolbar();
        store.Changed += OnChanged;
        Render();
        PresentRequestedTargetIfAvailable();
    }

    /// <inheritdoc/>
    public override void SetEditing(bool editing, bool animated)
    {
        if (editing == Editing)
        {
            return;
        }

        base.SetEditing(editing, animated);
        list.SelectionMode = editing;
        if (folderDownloadButton is not null)
        {
            folderDownloadButton.Enabled = !editing;
        }

        if (userActionsButton is not null)
        {
            userActionsButton.Enabled = !editing;
        }

        UpdateSelectionToolbar(animated);
    }

    /// <inheritdoc/>
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        PresentRequestedTargetIfAvailable();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= OnChanged;
        }

        base.Dispose(disposing);
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        Render();
        PresentRequestedTargetIfAvailable();
    }

    private void Render()
    {
        BrowseLocationSnapshot snapshot = store.GetLocation(location, filterText);
        BrowseLocationSnapshot allRows = filterText.Length == 0 ? snapshot : store.GetLocation(location);
        Title = snapshot.DisplayPath.Length == 0 ? snapshot.Username : snapshot.DisplayPath;
        NavigationItem.Prompt = store.State.Message;
        EditButtonItem.Enabled = allRows.UnfilteredCount > 0;
        if (snapshot.Rows.Count == 0)
        {
            list.Apply([], selectionUniverse: allRows.Rows.Select(row => row.Id).ToArray());
            if (Editing)
            {
                UpdateSelectionToolbar();
            }
            ContentStateView.Show(this, new ContentStatePresentation(
                filterText.Length > 0 ? AppStrings.Get("IosUiSearchFilteredEmpty") : AppStrings.Get("IosUiBrowseEmpty"),
                filterText.Length > 0
                    ? AppStrings.Format("IosUiBrowseFilteredEmptyDetail", snapshot.UnfilteredCount)
                    : AppStrings.Get("IosUiBrowseEmptyDetail"),
                "folder"));
            return;
        }

        ContentStateView.Clear(this);
        list.Apply(
            snapshot.Rows.Select(row => new FeatureListItem(
                row.Id,
                row.Title,
                row.Subtitle,
                row.IsFolder ? row.IsLocked ? "folder.badge.questionmark" : "folder" : row.IsLocked ? "lock.doc" : "doc",
                ShowsDisclosure: true,
                AccessibilityLabel: string.Join(", ", row.Title, row.Subtitle))).ToArray(),
            selectionUniverse: allRows.Rows.Select(row => row.Id).ToArray());
        if (Editing)
        {
            UpdateSelectionToolbar();
        }
    }

    private void UpdateSelectionToolbar(bool animated = false)
    {
        if (!Editing)
        {
            this.SetSelectionAccessory([], animated);
            return;
        }

        BrowseLocationSnapshot allRows = store.GetLocation(location);
        int selected = list.SelectedItemIds.Count;
        UIBarButtonItem count = UIKitFactory.ToolbarStatusItem(
            AppStrings.Format("IosUiSelectedCountCompact", selected, allRows.Rows.Count),
            AppStrings.Format("IosUiSelectedCount", selected, allRows.Rows.Count),
            "browse.selection-count");
        var selection = new UIBarButtonItem(
            AppStrings.Get("IosUiSelection"),
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentSelectionActions())
        {
            Enabled = allRows.Rows.Count > 0,
            AccessibilityIdentifier = "browse.selection-scope",
        };
        var actions = new UIBarButtonItem(
            AppStrings.Get("IosUiBatchActions"),
            UIBarButtonItemStyle.Done,
            (_, _) => PresentBatchActions())
        {
            Enabled = selected > 0,
            AccessibilityIdentifier = "browse.batch-actions",
        };
        // Same arrangement as the other selection toolbars: buttons on the edges, count centered between them.
        this.SetSelectionAccessory(
        [
            selection,
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            UIKitFactory.ToolbarStatusGap(),
            count,
            UIKitFactory.ToolbarStatusGap(),
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            actions,
        ],
            animated);
    }

    private void PresentSelectionActions()
    {
        BrowseLocationSnapshot visible = store.GetLocation(location, filterText);
        BrowseLocationSnapshot allRows = filterText.Length == 0 ? visible : store.GetLocation(location);
        string[] visibleIds = visible.Rows.Select(row => row.Id).ToArray();
        string[] allIds = allRows.Rows.Select(row => row.Id).ToArray();
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiSelection"),
            AppStrings.Format("IosUiSelectionScopeBrowse", visibleIds.Length, allIds.Length),
            UIAlertControllerStyle.ActionSheet);
        if (filterText.Length > 0)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSelectFiltered"), UIAlertActionStyle.Default, _ =>
                list.SetSelectedItems(list.SelectedItemIds.Concat(visibleIds))));
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSelectAllInFolder"), UIAlertActionStyle.Default, _ =>
                list.SetSelectedItems(allIds)));
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiInvertFiltered"), UIAlertActionStyle.Default, _ =>
                list.InvertSelection(visibleIds)));
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiInvertAllInFolder"), UIAlertActionStyle.Default, _ =>
                list.InvertSelection(allIds)));
        }
        else
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSelectAll"), UIAlertActionStyle.Default, _ =>
                list.SetSelectedItems(allIds)));
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiInvertSelection"), UIAlertActionStyle.Default, _ =>
                list.InvertSelection(allIds)));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDeselectAll"), UIAlertActionStyle.Default, _ =>
            list.SetSelectedItems([])));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void PresentBatchActions()
    {
        int selected = list.SelectedItemIds.Count;
        if (selected == 0)
        {
            return;
        }

        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiBatchActions"),
            AppStrings.Format("IosUiBrowseBatchSelection", selected),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownload"), UIAlertActionStyle.Default, action =>
            _ = QueueSelectionAsync(false)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiQueuePaused"), UIAlertActionStyle.Default, action =>
            _ = QueueSelectionAsync(true)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private async Task QueueSelectionAsync(bool paused)
    {
        try
        {
            BrowseBatchQueueResult result = await store.QueueSelectionAsync(
                location,
                list.SelectedItemIds.ToArray(),
                paused);
            AccessibilityExtensions.Announce(AppStrings.Format(
                "IosUiBrowseBatchQueued",
                result.AcceptedCount,
                result.FileCount,
                result.SelectedRowCount));
            list.SetSelectedItems([]);
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiDownloadFailedDetail"));
        }
    }

    private void Select(string identifier)
    {
        BrowseRow? row = store.GetLocation(location, filterText).Rows.FirstOrDefault(candidate => candidate.Id == identifier);
        if (row is null)
        {
            return;
        }

        if (row.IsFolder)
        {
            NavigationController?.PushViewController(
                new BrowseFolderViewController(store, router, row.RemotePath),
                true);
        }
        else
        {
            PresentFileActions(row);
        }
    }

    private void PresentFileActions(BrowseRow row)
    {
        var sheet = UIAlertController.Create(row.Title, row.Subtitle, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownload"), UIAlertActionStyle.Default, action =>
            _ = QueueFileAsync(row.RemotePath, false)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiQueuePaused"), UIAlertActionStyle.Default, action =>
            _ = QueueFileAsync(row.RemotePath, true)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCopyLink"), UIAlertActionStyle.Default, _ =>
            UIPasteboard.General.String = BuildLink(row).ToString()));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiShareLink"), UIAlertActionStyle.Default, _ =>
            PresentViewController(
                new UIActivityViewController([new NSString(BuildLink(row).ToString())], null),
                true,
                null)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiViewProfile"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.UserProfile(store.State.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSendMessage"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Messages(store.State.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSearchUser"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Search(
                Target: SearchRouteTarget.User,
                Subject: store.State.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void PresentUserActions()
    {
        var sheet = UIAlertController.Create(store.State.Username, null, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiViewProfile"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.UserProfile(store.State.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSendMessage"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Messages(store.State.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSearchUser"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Search(
                Target: SearchRouteTarget.User,
                Subject: store.State.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void PresentFolderDownload(string path)
    {
        (int count, long size) = store.GetDownloadSummary(path, recursive: true);
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiDownloadThisFolder"),
            AppStrings.Format("IosUiDownloadFolderSummary", count, AnimaSeek.iOS.UI.Presentation.FeatureValueFormatter.Bytes(size)),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownloadRecursively"), UIAlertActionStyle.Default, action =>
            _ = QueueFolderAsync(path, true, false)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownloadFolderOnly"), UIAlertActionStyle.Default, action =>
            _ = QueueFolderAsync(path, false, false)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiQueuePaused"), UIAlertActionStyle.Default, action =>
            _ = QueueFolderAsync(path, true, true)));
        SoulseekLink link = BuildFolderLink(path);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCopyLink"), UIAlertActionStyle.Default, _ =>
            UIPasteboard.General.String = link.ToString()));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiShareLink"), UIAlertActionStyle.Default, _ =>
            PresentViewController(
                new UIActivityViewController([new NSString(link.ToString())], null),
                true,
                null)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private async Task QueueFileAsync(string path, bool paused)
    {
        try
        {
            await store.QueueFileAsync(path, paused);
            AccessibilityExtensions.Announce(AppStrings.Get(paused
                ? "IosUiQueuedPausedDownload"
                : "IosUiQueuedDownload"));
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiDownloadFailedDetail"));
        }
    }

    private async Task QueueFolderAsync(string path, bool recursive, bool paused)
    {
        try
        {
            int accepted = await store.QueueFolderAsync(path, recursive, paused);
            AccessibilityExtensions.Announce(AppStrings.Format("IosUiSelectedCount", accepted, accepted));
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiDownloadFailedDetail"));
        }
    }

    private void PresentRequestedTargetIfAvailable()
    {
        if (ViewIfLoaded?.Window is null || requestedDownloadPath is not { } target)
        {
            return;
        }

        BrowseLocationSnapshot snapshot = store.GetLocation(location);
        BrowseRow? exact = snapshot.Rows.FirstOrDefault(row =>
            string.Equals(row.RemotePath, target, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            requestedDownloadPath = null;
            if (exact.IsFolder)
            {
                PresentFolderDownload(exact.RemotePath);
            }
            else
            {
                PresentFileActions(exact);
            }

            return;
        }

        // A folder route addresses the directory itself, which is not one of its own child rows.
        if (string.Equals(location, target, StringComparison.OrdinalIgnoreCase) &&
            store.IsDownloadTargetAvailable(target))
        {
            requestedDownloadPath = null;
            PresentFolderDownload(location);
            return;
        }

        if (store.HasLoadedResponseForCurrentUser)
        {
            requestedDownloadPath = null;
            ShowMessage(AppStrings.Get("IosUiBrowseTargetUnavailable"));
        }
    }

    private SoulseekLink BuildLink(BrowseRow row) => new(
        store.State.Username,
        row.RemotePath,
        row.IsFolder ? row.RemotePath : AnimaSeek.iOS.UI.Presentation.FeatureValueFormatter.ParentPath(row.RemotePath),
        row.IsFolder ? SoulseekLinkKind.Folder : SoulseekLinkKind.File);

    private SoulseekLink BuildFolderLink(string path) => new(
        store.State.Username,
        path,
        path,
        SoulseekLinkKind.Folder);

    private void ShowMessage(string message)
    {
        var alert = UIAlertController.Create(AppStrings.Get("IosUiActionFailed"), message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }
}
