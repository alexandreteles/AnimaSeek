using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using AnimaSeek.iOS.UI.Presentation.Search;
using Foundation;
using Seeker.Routing;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Search;

/// <summary>
/// Presents scoped progressive search, history, wishlists, native result expansion, filtering, sorting, and download actions.
/// </summary>
internal sealed class SearchViewController : UIViewController, IAppRouteReceiving
{
    private readonly SearchPresentationStore store;
    private readonly IAppRouter router;
    private readonly DiffableFeatureListView list = new();
    private readonly ChipBarView chipBar = new();
    private readonly UIView statusBar = new() { TranslatesAutoresizingMaskIntoConstraints = false };
    private readonly UILabel statusLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel, 2);
    private readonly UISearchController searchController = new();
    private UIBarButtonItem? wishlistAction;
    private UIBarButtonItem? stopAction;
    private CancellationTokenSource? operationCancellation;
    private string? pendingQuery;
    private SearchTarget pendingTarget = SearchTarget.Network;
    private bool hasPendingTarget;
    private int? pendingWishlistId;
    private bool loaded;
    private bool showsStopAction;
    private bool hidesSearchBarWhenScrolling;

    /// <summary>Creates Search with a feature-local store and typed application router.</summary>
    public SearchViewController(SearchPresentationStore store, IAppRouter router)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        Title = AppStrings.Get("searches_tab2");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        loaded = true;
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = "search.screen";

        // The search field, the chip row, and the tab bar already identify this screen, so a large title
        // would only repeat that word across roughly forty points that the result list needs instead.
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        ConfigureSearch();
        ConfigureNavigation();
        ConfigureLayout();
        store.Changed += OnStoreChanged;
        Render();
        if (!string.IsNullOrWhiteSpace(pendingQuery))
        {
            searchController.SearchBar.Text = pendingQuery;
            SearchTarget target = pendingTarget;
            pendingQuery = null;
            pendingTarget = SearchTarget.Network;
            hasPendingTarget = false;
            _ = StartSearchAsync(searchController.SearchBar.Text!, target);
        }
        else if (pendingWishlistId is { } wishlistId)
        {
            pendingWishlistId = null;
            _ = OpenWishlistRouteAsync(wishlistId);
        }
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        base.ViewDidDisappear(animated);
        if (IsMovingFromParentViewController)
        {
            operationCancellation?.Cancel();
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= OnStoreChanged;
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public void Receive(AppRoute route)
    {
        if (route is not AppRoute.Search search)
        {
            return;
        }

        if (search.WishlistId is { } wishlistId)
        {
            pendingTarget = SearchTarget.Network;
            hasPendingTarget = false;
            if (!loaded)
            {
                pendingWishlistId = wishlistId;
            }
            else
            {
                _ = OpenWishlistRouteAsync(wishlistId);
            }

            return;
        }

        SearchTarget target = MapRouteTarget(search);
        if (string.IsNullOrWhiteSpace(search.Query))
        {
            pendingTarget = target;
            hasPendingTarget = true;
            if (loaded)
            {
                Render();
                searchController.SearchBar.BecomeFirstResponder();
            }

            return;
        }

        if (!loaded)
        {
            pendingQuery = search.Query;
            pendingTarget = target;
            hasPendingTarget = true;
            return;
        }

        searchController.SearchBar.Text = search.Query;
        pendingTarget = SearchTarget.Network;
        hasPendingTarget = false;
        _ = StartSearchAsync(search.Query, target);
    }

    private static SearchTarget MapRouteTarget(AppRoute.Search route) => route.Target switch
    {
        SearchRouteTarget.User => new SearchTarget(SearchAudience.User, route.Subject),
        SearchRouteTarget.Room => new SearchTarget(SearchAudience.Room, route.Subject),
        SearchRouteTarget.UserList => new SearchTarget(SearchAudience.UserList),
        SearchRouteTarget.Wishlist => new SearchTarget(SearchAudience.Wishlist),
        _ => SearchTarget.Network,
    };

    private async Task OpenWishlistRouteAsync(int wishlistId)
    {
        try
        {
            pendingTarget = SearchTarget.Network;
            hasPendingTarget = false;
            await store.OpenWishlistAsync(wishlistId);
            searchController.SearchBar.Text = store.State.Query;
            AccessibilityExtensions.FocusScreen(list);
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiWishlistMissing"), AppStrings.Get("IosUiWishlistMissing"));
        }
    }

    private void ConfigureSearch()
    {
        searchController.ObscuresBackgroundDuringPresentation = false;

        // UIKit hides the navigation bar for the duration of a search presentation by default, which used to
        // take every result control away for as long as a query was being entered or had just been submitted.
        searchController.HidesNavigationBarDuringPresentation = false;
        searchController.SearchBar.Placeholder = AppStrings.Get("IosUiSearchPlaceholder");
        searchController.SearchBar.AccessibilityIdentifier = "search.query";
        searchController.SearchBar.SearchButtonClicked += (_, _) =>
        {
            searchController.SearchBar.ResignFirstResponder();
            SearchTarget target = hasPendingTarget ? pendingTarget : SearchTarget.Network;
            pendingTarget = SearchTarget.Network;
            hasPendingTarget = false;
            _ = StartSearchAsync(searchController.SearchBar.Text ?? string.Empty, target);
        };

        // Dismissing the search field must not discard received results; stopping a running query is an
        // explicit navigation-bar action instead.
        NavigationItem.SearchController = searchController;
        NavigationItem.HidesSearchBarWhenScrolling = false;
        NavigationItem.PreferredSearchBarPlacement = UINavigationItemSearchBarPlacement.Stacked;
        DefinesPresentationContext = true;
    }

    private void ConfigureNavigation()
    {
        wishlistAction = new UIBarButtonItem(
            UIImage.GetSystemImage("bookmark")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => NavigationController?.PushViewController(new WishlistViewController(store), true))
        {
            AccessibilityLabel = AppStrings.Get("IosUiWishlists"),
            AccessibilityIdentifier = "search.wishlists",
        };
        stopAction = new UIBarButtonItem(
            UIImage.GetSystemImage("stop.circle")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => store.CancelSearch())
        {
            AccessibilityLabel = AppStrings.Get("IosUiSearchStop"),
            AccessibilityIdentifier = "search.stop",
        };
        NavigationItem.RightBarButtonItems = [wishlistAction];
    }

    private void ConfigureLayout()
    {
        statusLabel.AccessibilityIdentifier = "search.summary";
        statusBar.DirectionalLayoutMargins = new NSDirectionalEdgeInsets(0, 16, 8, 16);
        statusBar.AddSubview(statusLabel);
        statusBar.Hidden = true;

        // A stack collapses the status row entirely when there is nothing to say, so the only permanent
        // chrome below the search field is the one-line chip row.
        UIStackView header = UIKitFactory.VerticalStack(0);
        header.AddArrangedSubview(chipBar);
        header.AddArrangedSubview(statusBar);
        View!.AddSubviews(header, list);
        list.ItemSelected += (_, identifier) => SelectItem(identifier);
        list.CollectionView.KeyboardDismissMode = UIScrollViewKeyboardDismissMode.OnDrag;
        NSLayoutConstraint.ActivateConstraints([
            statusLabel.LeadingAnchor.ConstraintEqualTo(statusBar.LayoutMarginsGuide.LeadingAnchor),
            statusLabel.TrailingAnchor.ConstraintEqualTo(statusBar.LayoutMarginsGuide.TrailingAnchor),
            statusLabel.TopAnchor.ConstraintEqualTo(statusBar.LayoutMarginsGuide.TopAnchor),
            statusLabel.BottomAnchor.ConstraintEqualTo(statusBar.LayoutMarginsGuide.BottomAnchor),
            header.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            header.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            header.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor),
            list.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            list.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            list.TopAnchor.ConstraintEqualTo(header.BottomAnchor),
            list.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
    }

    private async Task StartSearchAsync(string query, SearchTarget? target = null)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            ShowMessage(AppStrings.Get("IosUiSearchFailed"), AppStrings.Get("no_search_text"));
            return;
        }

        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
        try
        {
            await store.SearchAsync(query, target, operationCancellation.Token);
        }
        catch (ArgumentException exception)
        {
            ShowMessage(
                AppStrings.Get("IosUiSearchFailed"),
                exception.ParamName == "target"
                    ? AppStrings.Get("IosUiSearchTargetInvalid")
                    : AppStrings.Get("no_search_text"));
        }
    }

    private void OnStoreChanged(object? sender, EventArgs args) => Render();

    private void Render()
    {
        SearchScreenState state = store.State;
        RenderTitle(state);
        RenderNavigationActions(state);
        RenderChips(state);
        RenderStatus(state);
        if (state.Rows.Count > 0)
        {
            ContentStateView.Clear(this);
            list.Apply(state.Rows.Select(ToListItem).ToArray());
            return;
        }

        if (state.Phase == SearchPhase.Idle && state.History.Count > 0)
        {
            ContentStateView.Clear(this);
            list.Apply(state.History.Select(query => new FeatureListItem(
                $"history\u001f{query}",
                query,
                AppStrings.Get("IosUiSearchHistory"),
                "clock.arrow.circlepath",
                ShowsDisclosure: true,
                AccessibilityLabel: query)).ToArray());
            return;
        }

        list.Apply([]);
        ContentStateView.Show(this, StatePresentation(state));
    }

    /// <summary>
    /// Carries the live result count in the navigation title, which the tab bar and search field would
    /// otherwise leave as a restatement of the word "Search".
    /// </summary>
    private void RenderTitle(SearchScreenState state) => NavigationItem.Title =
        state.Rows.Count > 0 || state.Phase == SearchPhase.FilteredEmpty
            ? AppStrings.Format(
                "IosUiSearchResultCount",
                state.VisibleFileCount,
                state.TotalFileCount,
                state.ResponseCount)
            : state.Phase == SearchPhase.Loading
                ? AppStrings.Get("IosUiSearchLoading")
                : AppStrings.Get("searches_tab2");

    /// <summary>Exposes an explicit stop control while a query runs, and reclaims the search bar once results scroll.</summary>
    private void RenderNavigationActions(SearchScreenState state)
    {
        bool showsStop = state.Phase == SearchPhase.Loading;
        if (showsStop != showsStopAction && wishlistAction is not null && stopAction is not null)
        {
            showsStopAction = showsStop;
            NavigationItem.RightBarButtonItems = showsStop
                ? [wishlistAction, stopAction]
                : [wishlistAction];
        }

        bool hidesSearchBar = state.Rows.Count > 0;
        if (hidesSearchBar != hidesSearchBarWhenScrolling)
        {
            hidesSearchBarWhenScrolling = hidesSearchBar;
            NavigationItem.HidesSearchBarWhenScrolling = hidesSearchBar;
        }
    }

    /// <summary>Shows only a retained loading or actionable status, so the row costs nothing when silent.</summary>
    private void RenderStatus(SearchScreenState state)
    {
        string? status = state.Phase == SearchPhase.Loading && state.Rows.Count > 0
            ? AppStrings.Get("IosUiSearchLoading")
            : state.Phase is SearchPhase.Content or SearchPhase.FilteredEmpty
                ? state.Message
                : null;
        statusLabel.Text = status;
        statusBar.Hidden = string.IsNullOrWhiteSpace(status);
    }

    /// <summary>Presents scope, ordering, and filters as always-reachable controls beside the current result set.</summary>
    private void RenderChips(SearchScreenState state)
    {
        SearchTarget target = hasPendingTarget ? pendingTarget : state.Target;
        var descriptors = new List<ChipDescriptor>
        {
            ScopeChip(target),
            OrderingChip(state.Ordering),
            FiltersChip(state.Filters),
        };
        if (state.Rows.Count == 0 && state.Phase == SearchPhase.Idle && state.History.Count > 0)
        {
            descriptors.Add(ClearHistoryChip());
        }

        chipBar.Apply(descriptors);
    }

    private ChipDescriptor ScopeChip(SearchTarget target) => new(
        "scope",
        ScopeChipTitle(target),
        "scope",
        target.Audience != SearchAudience.Network,
        "search.target",
        AppStrings.Get("IosUiSearchTarget"),
        TargetSummary(target),
        $"scope{target.Audience}{target.Subject}",
        () => ScopeMenu(target));

    private ChipDescriptor OrderingChip(SearchOrdering ordering) => new(
        "ordering",
        OrderingTitle(ordering),
        "arrow.up.arrow.down",
        false,
        "search.sort",
        AppStrings.Get("IosUiSortResults"),
        OrderingTitle(ordering),
        $"ordering{ordering}",
        () => OrderingMenu(ordering));

    private ChipDescriptor FiltersChip(SearchFilters filters) => new(
        "filters",
        filters.ActiveCount > 0
            ? AppStrings.Format("IosUiFiltersCount", filters.ActiveCount)
            : AppStrings.Get("IosUiFilters"),
        "line.3.horizontal.decrease",
        filters.ActiveCount > 0,
        "search.filters",
        AppStrings.Get("IosUiSearchFilters"),
        AppStrings.Format("IosUiFiltersCount", filters.ActiveCount),
        $"filters{filters}",
        () => FiltersMenu(filters));

    private ChipDescriptor ClearHistoryChip() => new(
        "clear-history",
        AppStrings.Get("IosUiClearHistory"),
        "clock.arrow.circlepath",
        false,
        "search.clear-history",
        AppStrings.Get("IosUiClearHistory"),
        null,
        "clear-history",
        Activate: store.ClearHistory);

    private UIMenu ScopeMenu(SearchTarget current) => UIMenu.Create(
        AppStrings.Get("IosUiSearchTarget"),
        [
            Choice(
                AppStrings.Get("IosUiSearchNetwork"),
                current.Audience == SearchAudience.Network,
                () => RestartWithTarget(SearchTarget.Network)),
            Choice(
                AppStrings.Get("IosUiSearchChosenUser"),
                current.Audience == SearchAudience.User,
                () => PromptTarget(SearchAudience.User)),
            Choice(
                AppStrings.Get("IosUiSearchScopeRoom"),
                current.Audience == SearchAudience.Room,
                () => PromptTarget(SearchAudience.Room)),
            Choice(
                AppStrings.Get("IosUiSearchUserList"),
                current.Audience == SearchAudience.UserList,
                () => RestartWithTarget(new SearchTarget(SearchAudience.UserList))),
            Choice(
                AppStrings.Get("IosUiSearchWishlistTarget"),
                current.Audience == SearchAudience.Wishlist,
                () => RestartWithTarget(new SearchTarget(SearchAudience.Wishlist))),
        ]);

    private UIMenu OrderingMenu(SearchOrdering current) => UIMenu.Create(
        AppStrings.Get("IosUiSortResults"),
        Enum.GetValues<SearchOrdering>()
            .Select(ordering => (UIMenuElement)Choice(
                OrderingTitle(ordering),
                ordering == current,
                () => _ = store.SetOrderingAsync(ordering)))
            .ToArray());

    /// <summary>
    /// Puts the two most frequently changed filters and both single-choice dimensions one tap away, while
    /// keeping the complete form and a clear action in the same menu.
    /// </summary>
    private UIMenu FiltersMenu(SearchFilters filters)
    {
        UIMenuElement[] quick =
        [
            Choice(
                AppStrings.Get("IosUiOnlyFreeSlots"),
                filters.OnlyFreeSlots,
                () => _ = store.SetFiltersAsync(filters with { OnlyFreeSlots = !filters.OnlyFreeSlots })),
            Choice(
                AppStrings.Get("IosUiHideLocked"),
                filters.HideLocked,
                () => _ = store.SetFiltersAsync(filters with { HideLocked = !filters.HideLocked })),
            UIMenu.Create(
                AppStrings.Get("IosUiFormat"),
                Enum.GetValues<SearchFormat>()
                    .Select(format => (UIMenuElement)Choice(
                        FormatTitle(format),
                        format == filters.Format,
                        () => _ = store.SetFiltersAsync(filters with { Format = format })))
                    .ToArray()),
            UIMenu.Create(
                AppStrings.Get("IosUiCategory"),
                Enum.GetValues<SearchCategory>()
                    .Select(category => (UIMenuElement)Choice(
                        SearchFilterViewController.CategoryTitle(category),
                        category == filters.Category,
                        () => _ = store.SetFiltersAsync(filters with { Category = category })))
                    .ToArray()),
        ];
        var editing = new List<UIMenuElement>
        {
            UIAction.Create(
                AppStrings.Get("IosUiSearchAllFilters"),
                UIImage.GetSystemImage("slider.horizontal.3"),
                null,
                _ => PresentFilters()),
        };
        if (filters.ActiveCount > 0)
        {
            UIAction clear = UIAction.Create(
                AppStrings.Get("IosUiClearFilters"),
                UIImage.GetSystemImage("xmark.circle"),
                null,
                action => _ = store.SetFiltersAsync(SearchFilters.None));
            clear.Attributes = UIMenuElementAttributes.Destructive;
            editing.Add(clear);
        }

        return UIMenu.Create(
            AppStrings.Get("IosUiSearchFilters"),
            [
                UIMenu.Create(string.Empty, null, null, UIMenuOptions.DisplayInline, quick),
                UIMenu.Create(string.Empty, null, null, UIMenuOptions.DisplayInline, [.. editing]),
            ]);
    }

    /// <summary>Builds one menu choice whose selection is carried by the system checkmark, not by color.</summary>
    private static UIAction Choice(string title, bool selected, Action action)
    {
        UIAction choice = UIAction.Create(title, null, null, _ => action());
        choice.State = selected ? UIMenuElementState.On : UIMenuElementState.Off;
        return choice;
    }

    /// <summary>Returns the localized label for one file-family filter option.</summary>
    private static string FormatTitle(SearchFormat format) => AppStrings.Get(format switch
    {
        SearchFormat.Lossless => "IosUiFormatLossless",
        SearchFormat.Lossy => "IosUiFormatLossy",
        _ => "IosUiFormatAny",
    });

    private FeatureListItem ToListItem(SearchResultRow row) => new(
        row.Id,
        row.Title,
        row.Subtitle,
        row.IsFolder ? row.IsExpanded ? "folder.fill.badge.minus" : "folder" : row.IsLocked ? "lock" : "doc",
        ShowsDisclosure: true,
        Indentation: row.IsFolder ? 0 : 1,
        AccessibilityLabel: string.Join(", ", row.Title, row.Subtitle),
        Progress: null);

    private ContentStatePresentation StatePresentation(SearchScreenState state) => state.Phase switch
    {
        SearchPhase.Loading => new(
            AppStrings.Get("IosUiSearchLoading"),
            AppStrings.Get("IosUiSearchLoadingDetail"),
            IsLoading: true,
            ActionTitle: AppStrings.Get("IosUiCancel"),
            Action: store.CancelSearch),
        SearchPhase.Empty => new(
            AppStrings.Get("IosUiNoSearchResults"),
            AppStrings.Get("IosUiNoSearchResultsDetail"),
            "magnifyingglass"),
        SearchPhase.FilteredEmpty => new(
            AppStrings.Get("IosUiSearchFilteredEmpty"),
            AppStrings.Format("IosUiSearchFilteredEmptyDetail", state.TotalFileCount),
            "line.3.horizontal.decrease.circle",
            AppStrings.Get("IosUiClearFilters"),
            () => _ = store.SetFiltersAsync(SearchFilters.None)),
        SearchPhase.Offline => new(
            AppStrings.Get("IosUiSearchOffline"),
            state.Message,
            "wifi.slash"),
        SearchPhase.TimedOut => RetryState("IosUiSearchTimedOut", state),
        SearchPhase.Canceled => RetryState("IosUiSearchCanceled", state),
        SearchPhase.Failed => RetryState("IosUiSearchFailed", state),
        _ => new(
            AppStrings.Get("IosUiNoSearches"),
            AppStrings.Get("IosUiNoSearchesDetail"),
            "magnifyingglass"),
    };

    private ContentStatePresentation RetryState(string titleKey, SearchScreenState state) => new(
        AppStrings.Get(titleKey),
        state.Message,
        "exclamationmark.magnifyingglass",
        AppStrings.Get("retry"),
        () => _ = StartSearchAsync(state.Query, state.Target));

    private void SelectItem(string identifier)
    {
        const string historyPrefix = "history\u001f";
        if (identifier.StartsWith(historyPrefix, StringComparison.Ordinal))
        {
            string query = identifier[historyPrefix.Length..];
            searchController.SearchBar.Text = query;
            _ = StartSearchAsync(query);
            return;
        }

        SearchResultRow? row = store.GetRow(identifier);
        if (row is null)
        {
            return;
        }

        if (row.IsFolder)
        {
            PresentFolderActions(row);
            return;
        }

        PresentResultActions(row);
    }

    private void PresentFolderActions(SearchResultRow row)
    {
        var sheet = UIAlertController.Create(row.Title, row.Subtitle, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get(row.IsExpanded ? "IosUiCollapseFolder" : "IosUiExpandFolder"),
            UIAlertActionStyle.Default,
            _ => store.ToggleFolder(row.Id)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownloadFolder"), UIAlertActionStyle.Default, _ =>
            PresentDownloadReview(row)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiBrowseLocation"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Browse(row.Username, row.RemotePath))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiBrowseUser"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Browse(row.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSendMessage"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Messages(row.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCopyLink"), UIAlertActionStyle.Default, _ =>
            UIPasteboard.General.String = BuildFolderLink(row).ToString()));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiShareLink"), UIAlertActionStyle.Default, _ =>
            PresentShare([new NSString(BuildFolderLink(row).ToString())])));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    /// <summary>Presents a reviewable, selectable folder batch before accepting transfer work.</summary>
    /// <param name="folder">The current folder row.</param>
    private void PresentDownloadReview(SearchResultRow folder)
    {
        IReadOnlyList<SearchResultRow> files = store.GetFolderFiles(folder.Id);
        if (files.Count == 0)
        {
            ShowMessage(AppStrings.Get("IosUiCouldNotCompleteAction"), AppStrings.Get("nothing_to_download"));
            return;
        }

        NavigationController?.PushViewController(
            new SearchDownloadReviewViewController(store, folder, files),
            true);
    }

    private void PresentResultActions(SearchResultRow row)
    {
        var sheet = UIAlertController.Create(row.Title, row.Subtitle, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownload"), UIAlertActionStyle.Default, action =>
            _ = QueueAsync(row.Id, false)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiQueuePaused"), UIAlertActionStyle.Default, action =>
            _ = QueueAsync(row.Id, true)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiBrowseLocation"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Browse(row.Username, FeatureValueFormatter.ParentPath(row.RemotePath)))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiBrowseUser"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Browse(row.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiViewProfile"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.UserProfile(row.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSendMessage"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Messages(row.Username))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCopyLink"), UIAlertActionStyle.Default, _ =>
            UIPasteboard.General.String = BuildLink(row).ToString()));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiShareLink"), UIAlertActionStyle.Default, _ =>
            PresentShare([new NSString(BuildLink(row).ToString())])));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private async Task QueueAsync(string rowId, bool paused)
    {
        try
        {
            await store.QueueDownloadAsync(rowId, paused);
            AccessibilityExtensions.Announce(AppStrings.Get(
                paused ? "IosUiQueuedPausedDownload" : "IosUiQueuedDownload"));
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiCouldNotCompleteAction"), AppStrings.Get("IosUiDownloadFailedDetail"));
        }
    }

    private async Task QueueFolderAsync(string rowId, bool paused)
    {
        try
        {
            IReadOnlyList<DownloadAcceptance> accepted = await store.QueueFolderAsync(rowId, paused);
            AccessibilityExtensions.Announce(AppStrings.Format("IosUiSelectedCount", accepted.Count, accepted.Count));
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiCouldNotCompleteAction"), AppStrings.Get("IosUiDownloadFailedDetail"));
        }
    }

    private void PromptTarget(SearchAudience audience)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get(audience == SearchAudience.Room ? "IosUiSearchScopeRoom" : "IosUiSearchChosenUser"),
            null,
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = audience == SearchAudience.Room
                ? AppStrings.Get("IosUiRoomName")
                : AppStrings.Get("IosUiUsername");
            field.ClearButtonMode = UITextFieldViewMode.WhileEditing;
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSearchNow"), UIAlertActionStyle.Default, _ =>
            RestartWithTarget(new SearchTarget(audience, alert.TextFields![0].Text))));
        PresentViewController(alert, true, null);
    }

    private void RestartWithTarget(SearchTarget target)
    {
        string query = searchController.SearchBar.Text?.Trim() ?? store.State.Query;
        if (query.Length == 0)
        {
            pendingTarget = target;
            hasPendingTarget = true;
            Render();
            searchController.SearchBar.BecomeFirstResponder();
            return;
        }

        pendingTarget = SearchTarget.Network;
        hasPendingTarget = false;
        _ = StartSearchAsync(query, target);
    }

    /// <summary>
    /// Returns the chip's target name, shortening only the default network scope so that the three chips
    /// stay visible together at standard Dynamic Type sizes.
    /// </summary>
    private static string ScopeChipTitle(SearchTarget target) => target.Audience == SearchAudience.Network
        ? AppStrings.Get("IosUiSearchNetworkChip")
        : TargetSummary(target);

    /// <summary>Returns a localized target name that retains the selected room or username.</summary>
    private static string TargetSummary(SearchTarget target) => target.Audience switch
    {
        SearchAudience.User => AppStrings.Format("IosUiSearchTargetUserSummary", target.Subject ?? string.Empty),
        SearchAudience.Room => AppStrings.Format("IosUiSearchTargetRoomSummary", target.Subject ?? string.Empty),
        SearchAudience.UserList => AppStrings.Get("IosUiSearchUserList"),
        SearchAudience.Wishlist => AppStrings.Get("IosUiSearchWishlistTarget"),
        _ => AppStrings.Get("IosUiSearchNetwork"),
    };

    /// <summary>Returns the localized label for one ordering option.</summary>
    private static string OrderingTitle(SearchOrdering ordering) => AppStrings.Get(ordering switch
    {
        SearchOrdering.Speed => "IosUiSortSpeed",
        SearchOrdering.Folder => "IosUiSortFolder",
        SearchOrdering.Bitrate => "IosUiSortBitrate",
        _ => "IosUiSortAvailability",
    });

    private void PresentFilters()
    {
        var controller = new SearchFilterViewController(store.State.Filters, filters =>
            _ = store.SetFiltersAsync(filters));
        PresentViewController(new UINavigationController(controller), true, null);
    }

    private static SoulseekLink BuildLink(SearchResultRow row) => new(
        row.Username,
        row.RemotePath,
        FeatureValueFormatter.ParentPath(row.RemotePath),
        SoulseekLinkKind.File);

    /// <summary>Creates a canonical typed link for the exact remote folder represented by a grouped row.</summary>
    private static SoulseekLink BuildFolderLink(SearchResultRow row) => new(
        row.Username,
        row.RemotePath,
        row.RemotePath,
        SoulseekLinkKind.Folder);

    private void PresentShare(NSObject[] items) => PresentViewController(
        new UIActivityViewController(items, null),
        true,
        null);

    private void ShowMessage(string title, string message)
    {
        var alert = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }
}

/// <summary>Reviews and selects a filtered Search folder batch before queue acceptance.</summary>
internal sealed class SearchDownloadReviewViewController : UIViewController
{
    private readonly SearchPresentationStore store;
    private readonly SearchResultRow folder;
    private IReadOnlyList<SearchResultRow> files;
    private readonly DiffableFeatureListView list = new(UICollectionLayoutListAppearance.InsetGrouped);
    private bool applyFilters = true;
    private bool includeSubfolders;

    /// <summary>Creates a folder review from detached file presentation rows.</summary>
    /// <param name="store">The owner of the current result generation and queue command.</param>
    /// <param name="folder">The reviewed folder.</param>
    /// <param name="files">The detached filtered file set.</param>
    public SearchDownloadReviewViewController(
        SearchPresentationStore store,
        SearchResultRow folder,
        IReadOnlyList<SearchResultRow> files)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.folder = folder ?? throw new ArgumentNullException(nameof(folder));
        this.files = files ?? throw new ArgumentNullException(nameof(files));
        Title = AppStrings.Get("IosUiDownloadFolder");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "search.download-review";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        NavigationItem.RightBarButtonItems =
        [
            new UIBarButtonItem(
                UIBarButtonSystemItem.Refresh,
                (_, _) => RefreshFiles())
            {
                AccessibilityLabel = AppStrings.Get("IosUiRefresh"),
                AccessibilityIdentifier = "search.download-review.refresh",
            },
            new UIBarButtonItem(
                UIImage.GetSystemImage("line.3.horizontal.decrease.circle")!,
                UIBarButtonItemStyle.Plain,
                (_, _) => PresentDownloadScope())
            {
                AccessibilityLabel = AppStrings.Get("IosUiDownloadScope"),
                AccessibilityIdentifier = "search.download-review.scope",
            },
        ];
        list.SelectionMode = true;
        View.AddSubview(list);
        NSLayoutConstraint.ActivateConstraints([
            list.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            list.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            list.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            list.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
        ApplyFiles();
        list.SetSelectedItems(files.Select(file => file.Id));
        list.SelectionChanged += (_, _) => ConfigureToolbar();
        ConfigureToolbar();
    }

    /// <summary>Refreshes the detached candidate set while retaining selection by stable identity.</summary>
    private void RefreshFiles()
    {
        files = store.GetFolderFiles(folder.Id, applyFilters, includeSubfolders);
        ApplyFiles();
        ConfigureToolbar();
        AccessibilityExtensions.Announce(AppStrings.Format("IosUiSelectedCount", list.SelectedItemIds.Count, files.Count));
    }

    /// <summary>Applies current candidates with multiline metadata and stable selection retention.</summary>
    private void ApplyFiles() => list.Apply(files.Select(file => new FeatureListItem(
        file.Id,
        file.Title,
        file.Subtitle,
        file.IsLocked ? "lock" : "doc",
        AccessibilityLabel: string.Join(", ", file.Title, file.Subtitle))).ToArray());

    /// <summary>Lets the person choose exact-folder or recursive and filtered or complete candidates.</summary>
    private void PresentDownloadScope()
    {
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiDownloadScope"),
            AppStrings.Get("IosUiDownloadScopeDetail"),
            UIAlertControllerStyle.ActionSheet);
        foreach ((string titleKey, bool filtered, bool recursive) in new[]
                 {
                     ("IosUiDownloadFilteredFiles", true, false),
                     ("IosUiDownloadAllFiles", false, false),
                     ("IosUiDownloadRecursiveFiltered", true, true),
                     ("IosUiDownloadRecursiveAll", false, true),
                 })
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get(titleKey), UIAlertActionStyle.Default, _ =>
            {
                applyFilters = filtered;
                includeSubfolders = recursive;
                RefreshFiles();
            }));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = View!;
            popover.SourceRect = View!.Bounds;
        }

        PresentViewController(sheet, true, null);
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        base.ViewDidDisappear(animated);
        if (IsMovingFromParentViewController)
        {
            this.ClearSelectionAccessory(animated);
        }
    }

    /// <summary>Rebuilds selection-scope and queue controls with an explicit count.</summary>
    private void ConfigureToolbar()
    {
        string[] allIds = files.Select(file => file.Id).ToArray();
        int selected = list.SelectedItemIds.Count;
        UIBarButtonItem count = UIKitFactory.ToolbarStatusItem(
            AppStrings.Format("IosUiSelectedCountCompact", selected, allIds.Length),
            AppStrings.Format("IosUiSelectedCount", selected, allIds.Length),
            "search.download-review.selection-count");
        var selection = new UIBarButtonItem(
            AppStrings.Get("IosUiSelection"),
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentSelection(allIds));
        var queue = new UIBarButtonItem(
            AppStrings.Get("IosUiDownload"),
            UIBarButtonItemStyle.Done,
            (_, _) => PresentQueueChoice())
        {
            Enabled = selected > 0,
        };
        this.SetSelectionAccessory(
        [
            selection,
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            UIKitFactory.ToolbarStatusGap(),
            count,
            UIKitFactory.ToolbarStatusGap(),
            new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace),
            queue,
        ],
            animated: false);
    }

    /// <summary>Presents standard Select All, Invert, and Clear selection scopes.</summary>
    /// <param name="allIds">Every current filtered file identifier.</param>
    private void PresentSelection(IReadOnlyCollection<string> allIds)
    {
        var sheet = UIAlertController.Create(AppStrings.Get("IosUiSelection"), null, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSelectAll"), UIAlertActionStyle.Default, _ =>
            list.SetSelectedItems(allIds)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiInvertSelection"), UIAlertActionStyle.Default, _ =>
            list.InvertSelection(allIds)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearSelection"), UIAlertActionStyle.Default, _ =>
            list.SetSelectedItems([])));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = View!;
            popover.SourceRect = View!.Bounds;
        }

        PresentViewController(sheet, true, null);
    }

    /// <summary>Confirms active versus paused acceptance for the reviewed filtered set.</summary>
    private void PresentQueueChoice()
    {
        int selected = list.SelectedItemIds.Count;
        var sheet = UIAlertController.Create(
            folder.Title,
            AppStrings.Format("IosUiSelectedCount", selected, files.Count),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDownload"), UIAlertActionStyle.Default, action =>
            _ = QueueSelectedAsync(queuePaused: false)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiQueuePaused"), UIAlertActionStyle.Default, action =>
            _ = QueueSelectedAsync(queuePaused: true)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = View!;
            popover.SourceRect = View!.Bounds;
        }

        PresentViewController(sheet, true, null);
    }

    /// <summary>Queues exactly the selected file rows and reports partial stale-result rejection.</summary>
    /// <param name="queuePaused">Whether accepted work begins paused.</param>
    private async Task QueueSelectedAsync(bool queuePaused)
    {
        string[] selectedIds = list.SelectedItemIds.ToArray();
        int accepted;
        try
        {
            IReadOnlyList<DownloadAcceptance> acceptances = await store.QueueFolderSelectionAsync(
                folder.Id,
                selectedIds,
                queuePaused);
            accepted = acceptances.Count;
        }
        catch
        {
            accepted = 0;
        }

        string resultMessage = AppStrings.Format("IosUiSearchBatchQueued", accepted, selectedIds.Length);
        AccessibilityExtensions.Announce(resultMessage);
        if (accepted == selectedIds.Length)
        {
            NavigationController?.PopViewController(true);
            return;
        }

        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiCouldNotCompleteAction"),
            resultMessage,
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }
}

/// <summary>Presents all local search filter dimensions in a native, Dynamic Type-safe form sheet.</summary>
internal sealed class SearchFilterViewController : UIViewController
{
    private readonly Action<SearchFilters> apply;
    private readonly UITextField textField = UIKitFactory.TextField(AppStrings.Get("IosUiFilterText"), NSString.Empty);
    private readonly UITextField excludedTextField = UIKitFactory.TextField(
        AppStrings.Get("IosUiExcludeText"),
        NSString.Empty);
    private readonly UISwitch freeSlots = new();
    private readonly UISwitch hideLocked = new();
    private readonly UISegmentedControl format = new([
        AppStrings.Get("IosUiFormatAny"),
        AppStrings.Get("IosUiFormatLossless"),
        AppStrings.Get("IosUiFormatLossy"),
    ]);
    private readonly UIButton categoryButton;
    private readonly UITextField bitrate = UIKitFactory.TextField(AppStrings.Get("IosUiMinimumBitrate"), NSString.Empty);
    private SearchCategory category;

    /// <summary>Creates a form initialized from the active filter state.</summary>
    public SearchFilterViewController(SearchFilters filters, Action<SearchFilters> apply)
    {
        this.apply = apply;
        Title = AppStrings.Get("IosUiSearchFilters");
        textField.Text = filters.Text;
        excludedTextField.Text = filters.ExcludedText;
        freeSlots.On = filters.OnlyFreeSlots;
        hideLocked.On = filters.HideLocked;
        format.SelectedSegment = (nint)filters.Format;
        category = filters.Category;
        categoryButton = UIKitFactory.Button(
            CategoryTitle(category),
            UIButtonConfiguration.TintedButtonConfiguration,
            PresentCategory,
            "chevron.up.chevron.down");
        categoryButton.AccessibilityIdentifier = "search.filters.category";
        UpdateCategoryButton();
        bitrate.Text = filters.MinimumBitrate > 0 ? filters.MinimumBitrate.ToString() : string.Empty;
        bitrate.KeyboardType = UIKeyboardType.NumberPad;
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        NavigationItem.LeftBarButtonItem = new UIBarButtonItem(
            AppStrings.Get("IosUiCancel"),
            UIBarButtonItemStyle.Plain,
            (_, _) => DismissViewController(true, null));
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            AppStrings.Get("IosUiApplyFilters"),
            UIBarButtonItemStyle.Done,
            (_, _) => Apply());
        UIStackView stack = UIKitFactory.VerticalStack(16);
        textField.AccessibilityIdentifier = "search.filters.include";
        excludedTextField.AccessibilityIdentifier = "search.filters.exclude";
        stack.AddArrangedSubview(LabeledControl("IosUiIncludeTerms", textField));
        stack.AddArrangedSubview(LabeledControl("IosUiExcludeTerms", excludedTextField));
        stack.AddArrangedSubview(LabeledSwitch("IosUiOnlyFreeSlots", freeSlots));
        stack.AddArrangedSubview(LabeledSwitch("IosUiHideLocked", hideLocked));
        stack.AddArrangedSubview(LabeledControl("IosUiFormat", format));
        stack.AddArrangedSubview(LabeledControl("IosUiCategory", categoryButton));
        stack.AddArrangedSubview(LabeledControl("IosUiMinimumBitrate", bitrate));
        var scrollView = new UIScrollView
        {
            AlwaysBounceVertical = true,
            KeyboardDismissMode = UIScrollViewKeyboardDismissMode.Interactive,
        };
        scrollView.TranslatesAutoresizingMaskIntoConstraints = false;
        scrollView.AccessibilityIdentifier = "search.filters.scroll";
        View.AddSubview(scrollView);
        scrollView.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints([
            scrollView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            scrollView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            scrollView.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor),
            scrollView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            stack.LeadingAnchor.ConstraintEqualTo(scrollView.FrameLayoutGuide.LeadingAnchor, 20),
            stack.TrailingAnchor.ConstraintEqualTo(scrollView.FrameLayoutGuide.TrailingAnchor, -20),
            stack.TopAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.TopAnchor, 24),
            stack.BottomAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.BottomAnchor, -24),
        ]);
    }

    private UIView LabeledSwitch(string key, UISwitch control)
    {
        string title = AppStrings.Get(key);
        var row = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Center,
            Spacing = 12,
        };
        UILabel label = UIKitFactory.Label(UIFontTextStyle.Body, lines: 0);
        label.Text = title;
        label.IsAccessibilityElement = false;
        control.AccessibilityLabel = title;
        control.AccessibilityIdentifier = key switch
        {
            "IosUiOnlyFreeSlots" => "search.filters.free-slots",
            "IosUiHideLocked" => "search.filters.hide-locked",
            _ => $"search.filters.{AccessibilityIdentifiers.Opaque(key)}",
        };
        row.AddArrangedSubview(label);
        row.AddArrangedSubview(control);
        return row;
    }

    private UIView LabeledControl(string key, UIView control)
    {
        UIStackView stack = UIKitFactory.VerticalStack(8);
        UILabel label = UIKitFactory.Label(UIFontTextStyle.Subheadline, UIColor.SecondaryLabel, 0);
        label.Text = AppStrings.Get(key);
        stack.AddArrangedSubview(label);
        stack.AddArrangedSubview(control);
        return stack;
    }

    /// <summary>Presents every broad file category as a compact native single-choice sheet.</summary>
    private void PresentCategory()
    {
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiCategory"),
            AppStrings.Format("IosUiCurrentCategory", CategoryTitle(category)),
            UIAlertControllerStyle.ActionSheet);
        foreach (SearchCategory option in Enum.GetValues<SearchCategory>())
        {
            string title = CategoryTitle(option);
            sheet.AddAction(UIAlertAction.Create(
                option == category ? AppStrings.Format("IosUiCurrentChoice", title) : title,
                UIAlertActionStyle.Default,
                _ =>
                {
                    category = option;
                    UpdateCategoryButton();
                }));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = categoryButton;
            popover.SourceRect = categoryButton.Bounds;
        }

        PresentViewController(sheet, true, null);
    }

    /// <summary>Updates the visible and VoiceOver category value without relying on tint alone.</summary>
    private void UpdateCategoryButton()
    {
        string title = CategoryTitle(category);
        UIButtonConfiguration configuration = categoryButton.Configuration ??
            UIButtonConfiguration.TintedButtonConfiguration;
        configuration.Title = title;
        categoryButton.Configuration = configuration;
        categoryButton.AccessibilityLabel = AppStrings.Get("IosUiCategory");
        categoryButton.AccessibilityValue = title;
    }

    /// <summary>Returns the localized display title for one broad file category.</summary>
    internal static string CategoryTitle(SearchCategory value) => AppStrings.Get(value switch
    {
        SearchCategory.Audio => "IosUiCategoryAudio",
        SearchCategory.Video => "IosUiCategoryVideo",
        SearchCategory.Image => "IosUiCategoryImage",
        SearchCategory.Document => "IosUiCategoryDocument",
        SearchCategory.Archive => "IosUiCategoryArchive",
        SearchCategory.Other => "IosUiCategoryOther",
        _ => "IosUiCategoryAny",
    });

    private void Apply()
    {
        int minimum = int.TryParse(bitrate.Text, out int value) ? Math.Max(0, value) : 0;
        apply(new SearchFilters(
            textField.Text?.Trim() ?? string.Empty,
            excludedTextField.Text?.Trim() ?? string.Empty,
            freeSlots.On,
            hideLocked.On,
            (SearchFormat)(int)format.SelectedSegment,
            category,
            minimum));
        DismissViewController(true, null);
    }
}

/// <summary>Presents durable wishlists, their unseen counts, exact stored results, and serialized editing actions.</summary>
internal sealed class WishlistViewController : UIViewController
{
    private readonly SearchPresentationStore store;
    private readonly DiffableFeatureListView list = new(UICollectionLayoutListAppearance.InsetGrouped);

    /// <summary>Creates the wishlist editor and result browser.</summary>
    public WishlistViewController(SearchPresentationStore store)
    {
        this.store = store;
        Title = AppStrings.Get("IosUiWishlists");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            UIBarButtonSystemItem.Add,
            (_, _) => PresentEditor(null));
        View.AddSubview(list);
        NSLayoutConstraint.ActivateConstraints([
            list.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            list.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            list.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            list.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
        list.ItemSelected += (_, identifier) => PresentActions(int.Parse(identifier));
        store.Changed += OnChanged;
        Render();
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

    private void OnChanged(object? sender, EventArgs args) => Render();

    private void Render()
    {
        IReadOnlyList<AnimaSeek.iOS.Services.WishlistEntrySnapshot> entries = store.Wishlists;
        if (entries.Count == 0)
        {
            list.Apply([]);
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoWishlists"),
                AppStrings.Get("IosUiNoWishlistsDetail"),
                "bookmark"));
            return;
        }

        ContentStateView.Clear(this);
        list.Apply(entries.Select(entry => new FeatureListItem(
            entry.Id.ToString(),
            entry.Query,
            WishlistMetadata(entry),
            entry.UnseenCount > 0 ? "bookmark.fill" : "bookmark",
            ShowsDisclosure: true,
            AccessibilityLabel: string.Join(", ", entry.Query, WishlistMetadata(entry))))
            .ToArray());
    }

    private string WishlistMetadata(AnimaSeek.iOS.Services.WishlistEntrySnapshot entry)
    {
        string schedule = entry.LastRun is { } lastRun && store.WishlistInterval is { } interval
            ? AppStrings.Format("IosUiWishlistNextRun", lastRun.Add(interval).ToLocalTime().ToString("g"))
            : AppStrings.Get("IosUiWishlistScheduleUnavailable");
        return string.Join(
            " · ",
            AppStrings.Format("IosUiWishlistMetadata", entry.ResultCount, entry.UnseenCount),
            schedule);
    }

    private async Task OpenAsync(int identifier)
    {
        try
        {
            await store.OpenWishlistAsync(identifier);
            NavigationController?.PopViewController(true);
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiWishlistMissing"));
        }
    }

    private void PresentActions(int identifier)
    {
        AnimaSeek.iOS.Services.WishlistEntrySnapshot? entry = store.Wishlists
            .FirstOrDefault(candidate => candidate.Id == identifier);
        if (entry is null)
        {
            return;
        }

        var sheet = UIAlertController.Create(entry.Query, null, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiOpenResults"), UIAlertActionStyle.Default, action =>
            _ = OpenAsync(entry.Id)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiEditWishlist"), UIAlertActionStyle.Default, _ =>
            PresentEditor(entry)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDeleteWishlist"), UIAlertActionStyle.Destructive, action =>
            _ = ConfirmDeleteAsync(entry.Id)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private Task ConfirmDeleteAsync(int identifier)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiDeleteWishlist"),
            AppStrings.Get("IosUiDeleteConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDeleteWishlist"), UIAlertActionStyle.Destructive, action =>
            _ = DeleteAsync(identifier)));
        PresentViewController(alert, true, null);
        return Task.CompletedTask;
    }

    private void PresentEditor(AnimaSeek.iOS.Services.WishlistEntrySnapshot? entry)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get(entry is null ? "IosUiAddWishlist" : "IosUiEditWishlist"),
            null,
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiWishlistQuery");
            field.Text = entry?.Query;
            field.ClearButtonMode = UITextFieldViewMode.WhileEditing;
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSave"), UIAlertActionStyle.Default, action =>
            _ = SaveAsync(entry?.Id, alert.TextFields![0].Text ?? string.Empty)));
        PresentViewController(alert, true, null);
    }

    private async Task SaveAsync(int? identifier, string query)
    {
        try
        {
            if (identifier is { } id)
            {
                await store.RenameWishlistAsync(id, query);
            }
            else
            {
                await store.CreateWishlistAsync(query);
            }
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiActionFailed"));
        }
    }

    private async Task DeleteAsync(int identifier)
    {
        try
        {
            await store.DeleteWishlistAsync(identifier);
        }
        catch (Exception)
        {
            ShowMessage(AppStrings.Get("IosUiActionFailed"));
        }
    }

    private void ShowMessage(string message)
    {
        var alert = UIAlertController.Create(AppStrings.Get("IosUiActionFailed"), message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }
}
