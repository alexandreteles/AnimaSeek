using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using Foundation;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Chatrooms;

/// <summary>Presents searchable room members with role, presence, statistics, and supported administration.</summary>
internal sealed class RoomUsersViewController : UITableViewController, IUISearchResultsUpdating
{
    private readonly IAppRouter router;
    private readonly ChatroomsPresentationStore store;
    private readonly string roomName;
    private readonly UISearchController searchController = new(searchResultsController: null);
    private IReadOnlyList<RoomUserRow> users = [];
    private string? query;
    private RoomUserSort sort = RoomUserSort.Name;
    private bool friendsFirst = true;

    /// <summary>Creates a room-user list.</summary>
    /// <param name="router">The typed app router.</param>
    /// <param name="store">The room facade.</param>
    /// <param name="roomName">The joined room.</param>
    public RoomUsersViewController(IAppRouter router, ChatroomsPresentationStore store, string roomName)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.roomName = string.IsNullOrWhiteSpace(roomName)
            ? throw new ArgumentException("A room name is required.", nameof(roomName))
            : roomName;
        Title = AppStrings.Get("IosUiRoomUsers");
    }

    /// <summary>Gets the represented room name.</summary>
    public string RoomName => roomName;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "rooms.users.screen";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 76;
        TableView.AccessibilityIdentifier = "rooms.users.list";
        searchController.SearchResultsUpdater = this;
        searchController.ObscuresBackgroundDuringPresentation = false;
        searchController.SearchBar.Placeholder = AppStrings.Get("IosUiUsername");
        searchController.SearchBar.AccessibilityIdentifier = "rooms.users.search";
        NavigationItem.SearchController = searchController;
        NavigationItem.HidesSearchBarWhenScrolling = false;
        var sortItem = new UIBarButtonItem(
            UIImage.GetSystemImage("arrow.up.arrow.down")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentSort())
        {
            AccessibilityLabel = AppStrings.Get("SortOrder"),
            AccessibilityIdentifier = "rooms.users.sort",
        };
        if (store.GetRoom(roomName)?.RoomData?.IsPrivate == true)
        {
            var invite = new UIBarButtonItem(UIBarButtonSystemItem.Add, (_, _) => PresentInvite())
            {
                AccessibilityLabel = AppStrings.Get("IosUiInviteMember"),
                AccessibilityIdentifier = "rooms.users.invite",
            };
            NavigationItem.RightBarButtonItems = [invite, sortItem];
        }
        else
        {
            NavigationItem.RightBarButtonItem = sortItem;
        }

        ReloadUsers();
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        ReloadUsers();
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnChanged;
        base.ViewDidDisappear(animated);
    }

    /// <inheritdoc/>
    public void UpdateSearchResultsForSearchController(UISearchController searchController)
    {
        query = searchController.SearchBar.Text;
        ReloadUsers();
    }

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => users.Count;

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        RoomUserRow user = users[indexPath.Row];
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            Accessory = UITableViewCellAccessory.DisclosureIndicator,
            AccessibilityIdentifier = $"rooms.users.user.{AccessibilityIdentifiers.Opaque(user.Username)}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        string presence = PresenceText(user.Presence);
        string role = RoleText(user.Role);
        string friend = user.IsFriend ? $" · {AppStrings.Get("IosUiFriends")}" : string.Empty;
        string note = string.IsNullOrWhiteSpace(user.Note) ? string.Empty : $"\n{user.Note}";
        content.Text = user.Username;
        content.SecondaryText = $"{presence} · {role}{friend} · {AppStrings.Format("IosUiProfileFiles", user.FileCount, user.DirectoryCount)}{note}";
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Subheadline);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        if (UIImage.GetSystemImage(user.IsFriend ? "person.crop.circle.fill.badge.checkmark" : "person.crop.circle") is { } image)
        {
            content.Image = image;
        }

        content.ImageProperties.TintColor = PresenceColor(user.Presence);
        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = user.Username;
        cell.AccessibilityValue = content.SecondaryText;
        cell.AccessibilityHint = AppStrings.Get("IosUiUserActions");
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        PresentUserActions(users[indexPath.Row]);
    }

    /// <summary>Shows the reusable user actions and owner-only member/moderator commands.</summary>
    /// <param name="user">The selected room user.</param>
    private void PresentUserActions(RoomUserRow user)
    {
        var sheet = UIAlertController.Create(user.Username, AppStrings.Get("IosUiUserActions"), UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiViewProfile"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.UserProfile(user.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSendMessage"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Messages(user.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiBrowseUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Browse(user.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSearchUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Search(
                Target: SearchRouteTarget.User,
                Subject: user.Username))));

        string? current = store.CurrentUsername;
        bool isOwner = current is not null && store.GetRoom(roomName)?.IsOwnedBy(current) == true;
        if (isOwner && !string.Equals(current, user.Username, StringComparison.OrdinalIgnoreCase))
        {
            sheet.AddAction(UIAlertAction.Create(
                user.Role == RoomUserRole.Moderator
                    ? AppStrings.Get("IosUiRemoveModerator")
                    : AppStrings.Get("IosUiMakeModerator"),
                UIAlertActionStyle.Default,
                action => _ = RunAsync(() => user.Role == RoomUserRole.Moderator
                    ? store.RemoveModeratorAsync(roomName, user.Username)
                    : store.AddModeratorAsync(roomName, user.Username))));
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiRemoveMember"),
                UIAlertActionStyle.Destructive,
                _ => ConfirmRemove(user.Username)));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        UIKitFactory.AnchorActionSheet(sheet, TableView);
        PresentViewController(sheet, true, null);
    }

    /// <summary>Presents a username invitation for a private room.</summary>
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
            field.AccessibilityIdentifier = "rooms.users.invite-username";
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
                    _ = RunAsync(() => store.AddPrivateMemberAsync(roomName, username));
                }
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Shows room-user sort and Friends First choices with explicit selected-state text.</summary>
    private void PresentSort()
    {
        var sheet = UIAlertController.Create(AppStrings.Get("SortOrder"), null, UIAlertControllerStyle.ActionSheet);
        AddSortAction(sheet, RoomUserSort.Name, AppStrings.Get("Name"));
        AddSortAction(sheet, RoomUserSort.Presence, AppStrings.Get("OnlineStatus"));
        AddSortAction(sheet, RoomUserSort.Speed, AppStrings.Get("IosUiAverageSpeed"));
        sheet.AddAction(UIAlertAction.Create(
            $"{(friendsFirst ? "✓ " : string.Empty)}{AppStrings.Get("IosUiFriends")}",
            UIAlertActionStyle.Default,
            _ =>
            {
                friendsFirst = !friendsFirst;
                ReloadUsers();
            }));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        UIKitFactory.AnchorActionSheet(sheet, TableView);
        PresentViewController(sheet, true, null);
    }

    /// <summary>Adds one user sort action with a textual selected-state indicator.</summary>
    /// <param name="sheet">The sort sheet.</param>
    /// <param name="value">The sort value.</param>
    /// <param name="title">The localized title.</param>
    private void AddSortAction(UIAlertController sheet, RoomUserSort value, string title)
    {
        sheet.AddAction(UIAlertAction.Create(
            $"{(sort == value ? "✓ " : string.Empty)}{title}",
            UIAlertActionStyle.Default,
            _ =>
            {
                sort = value;
                ReloadUsers();
            }));
    }

    /// <summary>Confirms a member-removal operation.</summary>
    /// <param name="username">The member to remove.</param>
    private void ConfirmRemove(string username)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiRemoveMember"),
            username,
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiRemoveMember"),
            UIAlertActionStyle.Destructive,
            action => _ = RunAsync(() => store.RemovePrivateMemberAsync(roomName, username))));
        PresentViewController(alert, true, null);
    }

    /// <summary>Runs an administration operation and exposes recoverable failure.</summary>
    /// <param name="operation">The operation to run.</param>
    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch
        {
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiOperationFailed"));
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiOperationFailed"),
                null,
                "exclamationmark.triangle",
                AppStrings.Get("IosUiDismiss"),
                () =>
                {
                    ContentStateView.Clear(this);
                    ReloadUsers();
                }));
        }
    }

    /// <summary>Reloads detached members and explicit empty/filter-zero states.</summary>
    private void ReloadUsers()
    {
        IEnumerable<RoomUserRow> ordered = store.GetRoomUsers(roomName, query);
        IOrderedEnumerable<RoomUserRow> primary = friendsFirst
            ? ordered.OrderByDescending(row => row.IsFriend)
            : ordered.OrderBy(row => 0);
        users = sort switch
        {
            RoomUserSort.Presence => primary
                .ThenByDescending(row => row.Presence)
                .ThenBy(row => row.Username, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            RoomUserSort.Speed => primary
                .ThenByDescending(row => row.AverageSpeed)
                .ThenBy(row => row.Username, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            _ => primary.ThenBy(row => row.Username, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        };
        TableView.ReloadData();
        if (users.Count > 0)
        {
            ContentStateView.Clear(this);
        }
        else
        {
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoRoomUsers"),
                string.IsNullOrWhiteSpace(query) ? null : AppStrings.Get("IosUiNoRoomsMatch"),
                "person.2.slash"));
        }
    }

    /// <summary>Maps room role to localized text.</summary>
    /// <param name="role">The presentation role.</param>
    /// <returns>The role label.</returns>
    private static string RoleText(RoomUserRole role) => role switch
    {
        RoomUserRole.Owner => AppStrings.Get("IosUiRoomOwner"),
        RoomUserRole.Moderator => AppStrings.Get("IosUiRoomModerator"),
        _ => AppStrings.Get("IosUiRoomMember"),
    };

    /// <summary>Maps presence to localized non-color text.</summary>
    /// <param name="presence">The server presence.</param>
    /// <returns>The localized presence.</returns>
    private static string PresenceText(UserPresence presence) => presence switch
    {
        UserPresence.Online => AppStrings.Get("IosUiPresenceOnline"),
        UserPresence.Away => AppStrings.Get("IosUiPresenceAway"),
        UserPresence.Offline => AppStrings.Get("IosUiPresenceOffline"),
        _ => AppStrings.Get("IosUiPresenceUnknown"),
    };

    /// <summary>Returns a semantic reinforcement color for presence.</summary>
    /// <param name="presence">The server presence.</param>
    /// <returns>A dynamic system color.</returns>
    private static UIColor PresenceColor(UserPresence presence) => presence switch
    {
        UserPresence.Online => UIColor.SystemGreen,
        UserPresence.Away => UIColor.SystemOrange,
        _ => UIColor.SecondaryLabel,
    };

    /// <summary>Reloads users after a room snapshot changes.</summary>
    /// <param name="sender">The store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args) => ReloadUsers();

    /// <summary>Identifies supported room-user sort choices.</summary>
    private enum RoomUserSort
    {
        Name,
        Presence,
        Speed,
    }
}

/// <summary>Presents the latest bounded room-ticker list with an explicit empty state.</summary>
internal sealed class RoomTickersViewController : UITableViewController
{
    private readonly ChatroomsPresentationStore store;
    private readonly string roomName;
    private IReadOnlyList<RoomTicker> tickers = [];

    /// <summary>Creates a room ticker list.</summary>
    /// <param name="store">The room presentation store.</param>
    /// <param name="roomName">The joined room.</param>
    public RoomTickersViewController(ChatroomsPresentationStore store, string roomName)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.roomName = string.IsNullOrWhiteSpace(roomName)
            ? throw new ArgumentException("A room name is required.", nameof(roomName))
            : roomName;
        Title = AppStrings.Get("IosUiRoomTickers");
    }

    /// <summary>Gets the represented room name.</summary>
    public string RoomName => roomName;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "rooms.tickers.screen";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 64;
        TableView.AccessibilityIdentifier = "rooms.tickers.list";
        ReloadTickers();
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        ReloadTickers();
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnChanged;
        base.ViewDidDisappear(animated);
    }

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => tickers.Count;

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        RoomTicker ticker = tickers[indexPath.Row];
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = $"rooms.tickers.item.{AccessibilityIdentifiers.Opaque(ticker.Username)}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = ticker.Username;
        content.SecondaryText = ticker.Message;
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Headline);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        if (UIImage.GetSystemImage("quote.bubble") is { } image)
        {
            content.Image = image;
        }

        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = AppStrings.Format("IosUiTickerByUser", ticker.Username, ticker.Message);
        return cell;
    }

    /// <summary>Reloads ticker snapshots and a descriptive empty state.</summary>
    private void ReloadTickers()
    {
        tickers = store.GetRoom(roomName)?.Tickers ?? [];
        TableView.ReloadData();
        if (tickers.Count == 0)
        {
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoTickers"),
                AppStrings.Get("IosUiNoTickersDetail"),
                "quote.bubble"));
        }
        else
        {
            ContentStateView.Clear(this);
        }
    }

    /// <summary>Reloads tickers after a room snapshot changes.</summary>
    /// <param name="sender">The store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args) => ReloadTickers();
}
