using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using CoreGraphics;
using Foundation;
using Seeker.Social;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Chatrooms;

/// <summary>Presents searchable public, private, joined, and recent chatrooms with stable retained state.</summary>
internal sealed class ChatroomsViewController : UITableViewController, IUISearchResultsUpdating, IAppRouteReceiving
{
    private const string RoomReuseIdentifier = "RoomCell";
    private const string RefreshErrorIdentity = "refresh-error";
    private static readonly IReadOnlyDictionary<ChatroomGroup, NSString> SectionIdentifiers =
        Enum.GetValues<ChatroomGroup>().ToDictionary(
            group => group,
            group => new NSString($"rooms.{group.ToString().ToLowerInvariant()}"));
    private readonly IAppRouter router;
    private readonly ChatroomsPresentationStore store;
    private readonly bool ownsStore;
    private readonly UISearchController searchController = new(searchResultsController: null);
    private readonly Dictionary<string, ChatroomSummary> roomsByIdentifier = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> sectionTitles = new(StringComparer.Ordinal);
    private UITableViewDiffableDataSource<NSString, NSString> dataSource = null!;
    private CancellationTokenSource? refreshCancellation;
    private RoomListStatus? operationStatus;
    private long roomListVersionAtFailure;
    private string? statusHeaderIdentity;
    private string? query;
    private long snapshotGeneration;
    private bool refreshing;
    private bool removed;

    /// <summary>Creates the room overview over a presentation-only social facade.</summary>
    /// <param name="router">The application router.</param>
    /// <param name="store">The room presentation store.</param>
    /// <param name="ownsStore">Whether this controller owns the store lifetime.</param>
    public ChatroomsViewController(IAppRouter router, ChatroomsPresentationStore store, bool ownsStore = true)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ownsStore = ownsStore;
        Title = AppStrings.Get("IosUiChatroomsTitle");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "rooms.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 64;
        TableView.AccessibilityIdentifier = "rooms.list";
        TableView.RegisterClassForCellReuse(typeof(UITableViewCell), RoomReuseIdentifier);
        dataSource = new RoomDataSource(TableView, ConfigureRoomCell, TitleForSection);
        TableView.DataSource = dataSource;
        ConfigureSearch();
        ConfigureActions();
        RefreshControl = new UIRefreshControl
        {
            AccessibilityLabel = AppStrings.Get("IosUiRefreshRooms"),
            AccessibilityIdentifier = "rooms.refresh",
        };
        RefreshControl.ValueChanged += (_, _) => _ = RefreshAsync();
        ReloadRooms();

        // While signed out or still connecting, CurrentStatus already presents a live offline state that
        // retires itself on connect. Requesting anyway would only strand a failure message over the list
        // the session fetches moments later.
        if (store.IsConnected)
        {
            _ = RefreshAsync();
        }
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        ReloadRooms();
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
        TableView.ResizeTableHeader();
    }

    /// <inheritdoc/>
    public override void DidMoveToParentViewController(UIViewController? parent)
    {
        base.DidMoveToParentViewController(parent);
        if (parent is not null)
        {
            return;
        }

        removed = true;
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        if (ownsStore)
        {
            store.Dispose();
        }
    }

    /// <summary>Gets a stable route-matching marker used by the coordinator.</summary>
    public string RouteIdentity => "chatrooms";

    /// <inheritdoc/>
    public void Receive(AppRoute route)
    {
        if (route is AppRoute.Chatrooms { RoomName: { Length: > 0 } roomName })
        {
            router.Navigate(new AppRoute.Chatrooms(roomName));
        }
    }

    /// <inheritdoc/>
    public void UpdateSearchResultsForSearchController(UISearchController searchController)
    {
        query = searchController.SearchBar.Text;
        ReloadRooms();
    }

    /// <summary>Configures a reusable room cell from its stable diffable identifier.</summary>
    /// <param name="tableView">The table requesting the row.</param>
    /// <param name="indexPath">The row's current visual position.</param>
    /// <param name="identifier">The stable room identifier.</param>
    /// <returns>A self-sizing accessible room cell.</returns>
    private UITableViewCell ConfigureRoomCell(
        UITableView tableView,
        NSIndexPath indexPath,
        NSObject identifier)
    {
        UITableViewCell cell = tableView.DequeueReusableCell(RoomReuseIdentifier, indexPath);
        if (!roomsByIdentifier.TryGetValue(identifier.ToString(), out ChatroomSummary? room))
        {
            return cell;
        }

        cell.Accessory = UITableViewCellAccessory.DisclosureIndicator;
        cell.AccessibilityIdentifier = $"rooms.room.{AccessibilityIdentifiers.Opaque(room.Name)}";
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        string connection = ConnectionText(room.ConnectionState);
        string unread = room.HasUnread ? AppStrings.Get("IosUiUnread") : AppStrings.Get("IosUiRead");
        content.Text = room.Name;
        content.SecondaryText = $"{AppStrings.Format("IosUiRoomCount", room.UserCount)} · {connection} · {unread}";
        content.TextProperties.Font = UIKitFactory.PreferredFont(
            room.HasUnread ? UIFontTextStyle.Headline : UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Subheadline);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        content.Image = UIImage.GetSystemImage(room.Group == ChatroomGroup.Private ? "lock" : "person.3");
        content.ImageProperties.TintColor = room.HasUnread ? tableView.TintColor : UIColor.SecondaryLabel;
        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = room.Name;
        cell.AccessibilityValue = content.SecondaryText;
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        if (RoomAt(indexPath) is { } room)
        {
            router.Navigate(new AppRoute.Chatrooms(room.Name));
        }
    }

    /// <inheritdoc/>
    public override UISwipeActionsConfiguration? GetTrailingSwipeActionsConfiguration(
        UITableView tableView,
        NSIndexPath indexPath)
    {
        if (RoomAt(indexPath) is not { } room ||
            room.ConnectionState is not RoomConnectionState.Joined and not RoomConnectionState.Disconnected)
        {
            return null;
        }

        UIContextualAction leave = UIContextualAction.FromContextualActionStyle(
            UIContextualActionStyle.Destructive,
            AppStrings.Get("IosUiLeaveRoom"),
            (_, _, completion) =>
            {
                ConfirmLeave(room.Name);
                completion(true);
            });
        leave.Image = UIImage.GetSystemImage("rectangle.portrait.and.arrow.right");
        return UISwipeActionsConfiguration.FromActions([leave]);
    }

    /// <summary>Installs native filtering behavior.</summary>
    private void ConfigureSearch()
    {
        searchController.SearchResultsUpdater = this;
        searchController.ObscuresBackgroundDuringPresentation = false;
        searchController.SearchBar.Placeholder = AppStrings.Get("IosUiRoomSearchPlaceholder");
        searchController.SearchBar.AccessibilityIdentifier = "rooms.search";
        NavigationItem.SearchController = searchController;
        NavigationItem.HidesSearchBarWhenScrolling = false;
        DefinesPresentationContext = true;
    }

    /// <summary>Installs refresh and create-room actions with descriptive accessibility labels.</summary>
    private void ConfigureActions()
    {
        var refresh = new UIBarButtonItem(UIBarButtonSystemItem.Refresh, (_, _) => _ = RefreshAsync())
        {
            AccessibilityLabel = AppStrings.Get("IosUiRefreshRooms"),
            AccessibilityIdentifier = "rooms.refresh-button",
        };
        var add = new UIBarButtonItem(UIBarButtonSystemItem.Add, (_, _) => PresentCreateRoom())
        {
            AccessibilityLabel = AppStrings.Get("IosUiCreateRoom"),
            AccessibilityIdentifier = "rooms.create",
        };
        NavigationItem.RightBarButtonItems = [add, refresh];
    }

    /// <summary>Refreshes room metadata while retaining rows and generation-safe recovery state.</summary>
    private async Task RefreshAsync()
    {
        if (refreshing)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(ref refreshCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        refreshing = true;
        operationStatus = null;
        NavigationItem.RightBarButtonItems?.ToList().ForEach(item => item.Enabled = false);
        ReloadRooms();
        try
        {
            await store.RefreshAsync(cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                operationStatus = null;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            roomListVersionAtFailure = store.RoomListVersion;
            operationStatus = new RoomListStatus(
                RefreshErrorIdentity,
                AppStrings.Get("IosUiRoomsRefreshFailed"),
                null,
                "exclamationmark.arrow.triangle.2.circlepath",
                AppStrings.Get("IosUiRetry"),
                () => _ = RefreshAsync());
            AccessibilityExtensions.Announce(operationStatus.Title);
        }
        finally
        {
            if (ReferenceEquals(refreshCancellation, cancellation))
            {
                refreshing = false;
                RefreshControl?.EndRefreshing();
                NavigationItem.RightBarButtonItems?.ToList().ForEach(item => item.Enabled = true);
                if (!removed)
                {
                    ReloadRooms();
                }
            }
        }
    }

    /// <summary>Shows public/private creation choices without placing a switch inside an alert.</summary>
    private void PresentCreateRoom()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiCreateRoom"),
            AppStrings.Get("IosUiRoomCreateDetail"),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiRoomName");
            field.AutocorrectionType = UITextAutocorrectionType.No;
            field.AutocapitalizationType = UITextAutocapitalizationType.None;
            field.AccessibilityIdentifier = "rooms.create.name";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiPublicRooms"),
            UIAlertActionStyle.Default,
            _ => BeginCreate(alert.TextFields?.FirstOrDefault()?.Text, false)));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiPrivateRoom"),
            UIAlertActionStyle.Default,
            _ => BeginCreate(alert.TextFields?.FirstOrDefault()?.Text, true)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Validates and starts a room join/create before opening detail.</summary>
    /// <param name="value">The entered room name.</param>
    /// <param name="isPrivate">Whether creation is private.</param>
    private void BeginCreate(string? value, bool isPrivate)
    {
        string roomName = value?.Trim() ?? string.Empty;
        if (roomName.Length > 0)
        {
            _ = CreateAndOpenAsync(roomName, isPrivate);
        }
    }

    /// <summary>Creates or joins a room with retained retry feedback on failure.</summary>
    /// <param name="roomName">The validated room name.</param>
    /// <param name="isPrivate">Whether the server should create a private room.</param>
    private async Task CreateAndOpenAsync(string roomName, bool isPrivate)
    {
        try
        {
            await store.JoinAsync(roomName, isPrivate);
            operationStatus = null;
            router.Navigate(new AppRoute.Chatrooms(roomName));
        }
        catch
        {
            operationStatus = new RoomListStatus(
                $"join-error:{RoomIdentifier(roomName)}",
                AppStrings.Get("IosUiRoomJoinFailed"),
                AppStrings.Get("IosUiRoomJoinFailedDetail"),
                "wifi.exclamationmark",
                AppStrings.Get("IosUiRetry"),
                () => _ = CreateAndOpenAsync(roomName, isPrivate));
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiRoomJoinFailed"));
            ReloadRooms();
        }
    }

    /// <summary>Confirms and executes a recoverable room leave operation.</summary>
    /// <param name="roomName">The room to leave.</param>
    private void ConfirmLeave(string roomName)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiLeaveRoom"),
            AppStrings.Get("IosUiLeaveRoomConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiLeaveRoom"),
            UIAlertActionStyle.Destructive,
            action =>
            {
                _ = LeaveAsync(roomName);
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Leaves a room and retains an inline retry on connection failure.</summary>
    /// <param name="roomName">The room to leave.</param>
    private async Task LeaveAsync(string roomName)
    {
        try
        {
            await store.LeaveAsync(roomName);
            operationStatus = null;
            ReloadRooms();
        }
        catch
        {
            operationStatus = new RoomListStatus(
                $"leave-error:{RoomIdentifier(roomName)}",
                AppStrings.Get("IosUiOperationFailed"),
                null,
                "wifi.exclamationmark",
                AppStrings.Get("IosUiRetry"),
                () => _ = LeaveAsync(roomName));
            AccessibilityExtensions.Announce(operationStatus.Title);
            ReloadRooms();
        }
    }

    /// <summary>Applies deterministic stable room and section snapshots plus complete retained-content states.</summary>
    private void ReloadRooms()
    {
        IReadOnlyList<ChatroomSummary> rooms = store.GetRooms(query);
        ScrollAnchor? scrollAnchor = CaptureScrollAnchor();
        string[] selectedIdentifiers = (TableView.IndexPathsForSelectedRows ?? [])
            .Select(path => dataSource.GetItemIdentifier(path)?.ToString())
            .OfType<string>()
            .ToArray();
        roomsByIdentifier.Clear();
        foreach (ChatroomSummary room in rooms)
        {
            roomsByIdentifier[RoomIdentifier(room.Name)] = room;
        }

        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSString>();
        foreach (ChatroomGroup group in Enum.GetValues<ChatroomGroup>())
        {
            ChatroomSummary[] groupedRooms = rooms.Where(room => room.Group == group).ToArray();
            if (groupedRooms.Length == 0)
            {
                continue;
            }

            NSString sectionIdentifier = SectionIdentifiers[group];
            sectionTitles[sectionIdentifier.ToString()] = SectionTitle(group);
            snapshot.AppendSections([sectionIdentifier]);
            snapshot.AppendItems(
                groupedRooms.Select(room => new NSString(RoomIdentifier(room.Name))).ToArray(),
                sectionIdentifier);
        }

        HashSet<string> nextIdentifiers = roomsByIdentifier.Keys.ToHashSet(StringComparer.Ordinal);
        NSString[] retained = dataSource.Snapshot.ItemIdentifiers
            .Where(identifier => nextIdentifiers.Contains(identifier.ToString()))
            .ToArray();
        if (retained.Length > 0)
        {
            snapshot.ReconfigureItems(retained);
        }

        long generation = ++snapshotGeneration;
        dataSource.ApplySnapshot(snapshot, TableView.Window is not null, () =>
        {
            if (generation != snapshotGeneration)
            {
                return;
            }

            RestoreSelection(selectedIdentifiers);
            RestoreScrollAnchor(scrollAnchor);
        });
        UpdateStatePresentation(rooms.Count);
    }

    /// <summary>Maps refresh, failure, and connectivity to empty or retained-content treatment.</summary>
    /// <param name="visibleRoomCount">The number of rows remaining after the local filter.</param>
    private void UpdateStatePresentation(int visibleRoomCount)
    {
        bool filtering = !string.IsNullOrWhiteSpace(query);
        RoomListStatus? status = CurrentStatus();
        if (visibleRoomCount > 0)
        {
            ContentStateView.Clear(this);
            if (status is null)
            {
                ClearStatusHeader();
            }
            else
            {
                ShowStatusHeader(status);
            }

            return;
        }

        ClearStatusHeader();
        if (filtering)
        {
            string? statusDetail = status is null
                ? null
                : string.Join(" ", new[] { status.Title, status.Detail }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoRoomsMatch"),
                statusDetail,
                "line.3.horizontal.decrease.circle",
                status?.ActionTitle,
                status?.Action,
                status?.IsLoading ?? false));
            return;
        }

        ContentStateView.Show(this, status is null
            ? new ContentStatePresentation(
                AppStrings.Get("IosUiNoRooms"),
                AppStrings.Get("IosUiNoRoomsDetail"),
                "person.3")
            : new ContentStatePresentation(
                status.Title,
                status.Detail,
                status.SymbolName,
                status.ActionTitle,
                status.Action,
                status.IsLoading));
    }

    /// <summary>Builds the one coherent status that accompanies the last useful room snapshot.</summary>
    /// <returns>A refresh/offline/failure state, or null for ordinary content.</returns>
    private RoomListStatus? CurrentStatus()
    {
        if (refreshing)
        {
            return new RoomListStatus(
                "refreshing",
                AppStrings.Get("IosUiWorking"),
                AppStrings.Get("IosUiRefreshRooms"),
                "arrow.clockwise",
                IsLoading: true);
        }

        if (operationStatus is not null)
        {
            return operationStatus;
        }

        return store.IsConnected
            ? null
            : new RoomListStatus(
                "offline",
                AppStrings.Get("IosUiNotConnected"),
                AppStrings.Get("IosUiNoRoomsDetail"),
                "wifi.slash",
                AppStrings.Get("IosUiRetry"),
                () => _ = RefreshAsync());
    }

    /// <summary>Shows a self-sizing status with an optional recovery action above retained cached rooms.</summary>
    /// <param name="status">The status to show.</param>
    private void ShowStatusHeader(RoomListStatus status)
    {
        if (string.Equals(statusHeaderIdentity, status.Identity, StringComparison.Ordinal))
        {
            TableView.ResizeTableHeader();
            return;
        }

        UIView indicator;
        if (status.IsLoading)
        {
            var activity = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Medium)
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                HidesWhenStopped = false,
                IsAccessibilityElement = false,
            };
            activity.StartAnimating();
            indicator = activity;
        }
        else
        {
            indicator = new UIImageView(UIImage.GetSystemImage(status.SymbolName))
            {
                ContentMode = UIViewContentMode.ScaleAspectFit,
                TintColor = UIColor.SecondaryLabel,
                TranslatesAutoresizingMaskIntoConstraints = false,
                IsAccessibilityElement = false,
            };
        }

        indicator.WidthAnchor.ConstraintEqualTo(24).Active = true;
        indicator.HeightAnchor.ConstraintEqualTo(24).Active = true;
        var title = UIKitFactory.Label(UIFontTextStyle.Headline);
        title.Text = status.Title;
        var labels = UIKitFactory.VerticalStack(2);
        labels.AddArrangedSubview(title);
        if (!string.IsNullOrWhiteSpace(status.Detail))
        {
            var detail = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
            detail.Text = status.Detail;
            labels.AddArrangedSubview(detail);
        }

        var summary = new UIStackView([indicator, labels])
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Top,
            Spacing = 10,
        };
        var stack = UIKitFactory.VerticalStack(8);
        stack.AddArrangedSubview(summary);
        if (!string.IsNullOrWhiteSpace(status.ActionTitle) && status.Action is not null)
        {
            UIButton retry = UIKitFactory.Button(
                status.ActionTitle,
                UIButtonConfiguration.TintedButtonConfiguration,
                status.Action,
                "arrow.clockwise");
            retry.AccessibilityIdentifier = "rooms.status.retry";
            stack.AddArrangedSubview(retry);
        }

        var container = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemGroupedBackground,
            AccessibilityIdentifier = "rooms.status",
        };
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor, 10),
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -10),
        ]);
        statusHeaderIdentity = status.Identity;
        TableView.SetSelfSizingHeader(container);
    }

    /// <summary>Removes only the overview-owned status header.</summary>
    private void ClearStatusHeader()
    {
        if (statusHeaderIdentity is null)
        {
            return;
        }

        statusHeaderIdentity = null;
        TableView.TableHeaderView = null;
    }

    /// <summary>Captures the first visible stable room and its visual offset before a diff.</summary>
    /// <returns>A stable scroll anchor, or null when no room is visible.</returns>
    private ScrollAnchor? CaptureScrollAnchor()
    {
        if (TableView.Window is null)
        {
            return null;
        }

        NSIndexPath? indexPath = TableView.IndexPathsForVisibleRows?
            .OrderBy(path => path.Section)
            .ThenBy(path => path.Row)
            .FirstOrDefault(path => dataSource.GetItemIdentifier(path) is not null);
        if (indexPath is null || dataSource.GetItemIdentifier(indexPath) is not { } identifier)
        {
            return null;
        }

        return new ScrollAnchor(
            identifier.ToString(),
            TableView.RectForRowAtIndexPath(indexPath).Y - TableView.ContentOffset.Y);
    }

    /// <summary>Restores the captured room's visual offset without interrupting active touch scrolling.</summary>
    /// <param name="anchor">The optional pre-diff anchor.</param>
    private void RestoreScrollAnchor(ScrollAnchor? anchor)
    {
        if (anchor is null || TableView.Dragging || TableView.Decelerating || TableView.Tracking ||
            dataSource.GetIndexPath(new NSString(anchor.Identifier)) is not { } indexPath)
        {
            return;
        }

        nfloat minimumY = -TableView.AdjustedContentInset.Top;
        nfloat maximumY = (nfloat)Math.Max(
            minimumY,
            TableView.ContentSize.Height + TableView.AdjustedContentInset.Bottom - TableView.Bounds.Height);
        nfloat targetY = (nfloat)Math.Clamp(
            (double)(TableView.RectForRowAtIndexPath(indexPath).Y - anchor.Offset),
            (double)minimumY,
            (double)maximumY);
        TableView.SetContentOffset(new CGPoint(TableView.ContentOffset.X, targetY), false);
    }

    /// <summary>Restores native selection by stable room identity after reordering.</summary>
    /// <param name="identifiers">The room identifiers selected before the diff.</param>
    private void RestoreSelection(IEnumerable<string> identifiers)
    {
        foreach (string identifier in identifiers)
        {
            if (dataSource.GetIndexPath(new NSString(identifier)) is { } indexPath)
            {
                TableView.SelectRow(indexPath, false, UITableViewScrollPosition.None);
            }
        }
    }

    /// <summary>Resolves a visual index through the current stable diffable identifier.</summary>
    /// <param name="indexPath">The potentially stale row position.</param>
    /// <returns>The represented room, or null after a concurrent diff.</returns>
    private ChatroomSummary? RoomAt(NSIndexPath indexPath) =>
        dataSource.GetItemIdentifier(indexPath) is { } identifier
            ? roomsByIdentifier.GetValueOrDefault(identifier.ToString())
            : null;

    /// <summary>Creates a stable case-insensitive room identity without using an array index.</summary>
    /// <param name="roomName">The protocol room name.</param>
    /// <returns>A stable diffable identity.</returns>
    private static string RoomIdentifier(string roomName) => $"room:{roomName.ToUpperInvariant()}";

    /// <summary>Gets the localized section title for one semantic room group.</summary>
    /// <param name="group">The room group.</param>
    /// <returns>The localized header.</returns>
    private static string SectionTitle(ChatroomGroup group) => group switch
    {
        ChatroomGroup.Joined => AppStrings.Get("IosUiJoinedRooms"),
        ChatroomGroup.Private => AppStrings.Get("IosUiPrivateRooms"),
        _ => AppStrings.Get("IosUiPublicRooms"),
    };

    /// <summary>Looks up a section header from its stable diffable identity.</summary>
    /// <param name="identifier">The section identity.</param>
    /// <returns>The localized header, or null after a concurrent diff.</returns>
    private string? TitleForSection(string identifier) => sectionTitles.GetValueOrDefault(identifier);

    /// <summary>Maps room connection state to localized non-color text.</summary>
    /// <param name="state">The room connection state.</param>
    /// <returns>A localized status phrase.</returns>
    private static string ConnectionText(RoomConnectionState state) => state switch
    {
        RoomConnectionState.Joined => AppStrings.Get("IosUiRoomJoined"),
        RoomConnectionState.Joining => AppStrings.Get("IosUiRoomJoining"),
        RoomConnectionState.Disconnected => AppStrings.Get("IosUiRoomDisconnected"),
        RoomConnectionState.Failed or RoomConnectionState.Forbidden => AppStrings.Get("IosUiRoomJoinFailed"),
        _ => AppStrings.Get("IosUiRoomNotJoined"),
    };

    /// <summary>Rebuilds rows after a service snapshot changes.</summary>
    /// <param name="sender">The room store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args)
    {
        // A room list that arrived after the failure — typically the session's own refresh once it
        // connects — answers the question the failed request asked, so the message must not outlive it.
        if (!refreshing &&
            operationStatus is { Identity: RefreshErrorIdentity } &&
            store.RoomListVersion != roomListVersionAtFailure)
        {
            operationStatus = null;
        }

        ReloadRooms();
    }

    /// <summary>Owns table-data-source behavior that remains active beside the controller's swipe delegate.</summary>
    private sealed class RoomDataSource(
        UITableView tableView,
        UITableViewDiffableDataSourceCellProvider cellProvider,
        Func<string, string?> sectionTitle)
        : UITableViewDiffableDataSource<NSString, NSString>(tableView, cellProvider)
    {
        /// <inheritdoc/>
        public override bool CanEditRow(UITableView tableView, NSIndexPath indexPath) => true;

        /// <inheritdoc/>
        public override string? TitleForHeader(UITableView tableView, nint section) =>
            GetSectionIdentifier(section) is { } identifier
                ? sectionTitle(identifier.ToString())
                : null;
    }

    /// <summary>Describes one inline or full-screen room-list state with optional recovery.</summary>
    private sealed record RoomListStatus(
        string Identity,
        string Title,
        string? Detail,
        string SymbolName,
        string? ActionTitle = null,
        Action? Action = null,
        bool IsLoading = false);

    /// <summary>Retains one visible row's stable identity and position through a structural diff.</summary>
    private sealed record ScrollAnchor(string Identifier, nfloat Offset);
}
