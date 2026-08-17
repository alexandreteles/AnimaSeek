using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using Foundation;
using Seeker;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Users;

/// <summary>Presents searchable Friends and Ignored lists with notes, alerts, statistics, and reusable user actions.</summary>
internal sealed class UsersViewController : UITableViewController, IUISearchResultsUpdating
{
    private readonly IAppRouter router;
    private readonly UserListPresentationStore store;
    private readonly bool ownsStore;
    private readonly UISearchController searchController = new(searchResultsController: null);
    private IReadOnlyList<(bool Ignored, IReadOnlyList<UserListRow> Rows)> sections = [];
    private string? query;

    /// <summary>Creates the saved-user overview.</summary>
    /// <param name="router">The typed cross-feature router.</param>
    /// <param name="store">The immutable user-list presentation facade.</param>
    public UsersViewController(IAppRouter router, UserListPresentationStore store, bool ownsStore = true)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ownsStore = ownsStore;
        Title = AppStrings.Get("IosUiUsersTitle");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "users.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 84;
        TableView.AccessibilityIdentifier = "users.list";
        ConfigureSearch();
        ConfigureActions();
        ReloadRows();
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnChanged;
        ReloadRows();
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnChanged;
        base.ViewDidDisappear(animated);
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

    /// <inheritdoc/>
    public void UpdateSearchResultsForSearchController(UISearchController searchController)
    {
        query = searchController.SearchBar.Text;
        ReloadRows();
    }

    /// <inheritdoc/>
    public override nint NumberOfSections(UITableView tableView) => sections.Count;

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => sections[(int)section].Rows.Count;

    /// <inheritdoc/>
    public override string? TitleForHeader(UITableView tableView, nint section) =>
        AppStrings.Get(sections[(int)section].Ignored ? "IosUiIgnoredUsers" : "IosUiFriends");

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        UserListRow row = sections[indexPath.Section].Rows[indexPath.Row];
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            Accessory = UITableViewCellAccessory.DisclosureIndicator,
            AccessibilityIdentifier = $"users.user.{AccessibilityIdentifiers.Opaque(row.Username)}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        string status = row.DoesNotExist ? AppStrings.Get("IosUiUserDoesNotExist") : PresenceText(row.Presence);
        string statistics = row.FileCount is null
            ? string.Empty
            : $" · {AppStrings.Format("IosUiProfileFiles", row.FileCount.Value, row.DirectoryCount ?? 0)}";
        string alert = row.AlertsWhenOnline ? $" · {AppStrings.Get("IosUiOnlineAlert")}" : string.Empty;
        string note = string.IsNullOrWhiteSpace(row.Note) ? string.Empty : $"\n{row.Note}";
        content.Text = row.Username;
        content.SecondaryText = $"{status}{statistics}{alert}{note}";
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Subheadline);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        if (UIImage.GetSystemImage(row.IsIgnored ? "person.crop.circle.badge.xmark" : "person.crop.circle") is { } image)
        {
            content.Image = image;
        }

        content.ImageProperties.TintColor = row.DoesNotExist ? UIColor.SystemRed : PresenceColor(row.Presence);
        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = row.Username;
        cell.AccessibilityValue = $"{(row.IsIgnored ? AppStrings.Get("IosUiIgnoredUsers") : AppStrings.Get("IosUiFriends"))}. {content.SecondaryText}";
        cell.AccessibilityHint = AppStrings.Get("IosUiUserActions");
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        PresentUserActions(sections[indexPath.Section].Rows[indexPath.Row]);
    }

    /// <inheritdoc/>
    public override UISwipeActionsConfiguration? GetTrailingSwipeActionsConfiguration(
        UITableView tableView,
        NSIndexPath indexPath)
    {
        UserListRow row = sections[indexPath.Section].Rows[indexPath.Row];
        UIContextualAction remove = UIContextualAction.FromContextualActionStyle(
            UIContextualActionStyle.Destructive,
            AppStrings.Get(row.IsIgnored ? "IosUiStopIgnoring" : "IosUiRemoveFriend"),
            (_, _, completion) =>
            {
                ConfirmRemove(row);
                completion(true);
            });
        remove.Image = UIImage.GetSystemImage("trash");
        return UISwipeActionsConfiguration.FromActions([remove]);
    }

    /// <summary>Configures native filtering without obscuring list context.</summary>
    private void ConfigureSearch()
    {
        searchController.SearchResultsUpdater = this;
        searchController.ObscuresBackgroundDuringPresentation = false;
        searchController.SearchBar.Placeholder = AppStrings.Get("IosUiUsername");
        searchController.SearchBar.AccessibilityIdentifier = "users.search";
        NavigationItem.SearchController = searchController;
        NavigationItem.HidesSearchBarWhenScrolling = false;
        DefinesPresentationContext = true;
    }

    /// <summary>Installs add and sort controls with descriptive labels.</summary>
    private void ConfigureActions()
    {
        var sort = new UIBarButtonItem(
            UIImage.GetSystemImage("arrow.up.arrow.down")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentSort())
        {
            AccessibilityLabel = AppStrings.Get("SortOrder"),
            AccessibilityIdentifier = "users.sort",
        };
        var add = new UIBarButtonItem(UIBarButtonSystemItem.Add, (_, _) => PresentAdd())
        {
            AccessibilityLabel = AppStrings.Get("IosUiAddFriend"),
            AccessibilityIdentifier = "users.add",
        };
        NavigationItem.RightBarButtonItems = [add, sort];
    }

    /// <summary>Presents explicit Friend and Ignored destinations for a validated user name.</summary>
    private void PresentAdd()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiUsersTitle"),
            AppStrings.Get("IosUiAddUserDetail"),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiUsername");
            field.AutocapitalizationType = UITextAutocapitalizationType.None;
            field.AutocorrectionType = UITextAutocorrectionType.No;
            field.AccessibilityIdentifier = "users.add.username";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiAddFriend"),
            UIAlertActionStyle.Default,
            action => AddEnteredUser(alert, ignored: false)));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiIgnoreUser"),
            UIAlertActionStyle.Default,
            action => AddEnteredUser(alert, ignored: true)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Starts an add operation after trimming and validating the entered name.</summary>
    /// <param name="alert">The owning input alert.</param>
    /// <param name="ignored">Whether the destination is Ignored instead of Friends.</param>
    private void AddEnteredUser(UIAlertController alert, bool ignored)
    {
        string username = alert.TextFields?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
        if (username.Length > 0)
        {
            _ = RunAsync(() => ignored ? store.IgnoreAsync(username) : store.AddFriendAsync(username));
        }
    }

    /// <summary>Presents supported deterministic sort choices.</summary>
    private void PresentSort()
    {
        var sheet = UIAlertController.Create(
            AppStrings.Get("SortOrder"),
            null,
            UIAlertControllerStyle.ActionSheet);
        AddSortAction(sheet, SortOrder.Alphabetical, AppStrings.Get("Name"));
        AddSortAction(sheet, SortOrder.OnlineStatus, AppStrings.Get("OnlineStatus"));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = View!;
            popover.SourceRect = new CoreGraphics.CGRect(View!.Bounds.Width - 44, 0, 44, 44);
        }

        PresentViewController(sheet, true, null);
    }

    /// <summary>Adds one sort action with a non-color selected-state indicator.</summary>
    /// <param name="sheet">The action sheet.</param>
    /// <param name="order">The sort order.</param>
    /// <param name="title">The localized title.</param>
    private void AddSortAction(UIAlertController sheet, SortOrder order, string title)
    {
        string selected = store.SortOrder == order ? $"✓ {title}" : title;
        sheet.AddAction(UIAlertAction.Create(selected, UIAlertActionStyle.Default, _ => store.SetSortOrder(order)));
    }

    /// <summary>Shows profile, communication, note, alert, list conversion, and removal actions.</summary>
    /// <param name="row">The selected user.</param>
    private void PresentUserActions(UserListRow row)
    {
        var sheet = UIAlertController.Create(row.Username, AppStrings.Get("IosUiUserActions"), UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiViewProfile"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.UserProfile(row.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSendMessage"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Messages(row.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiBrowseUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Browse(row.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSearchUser"),
            UIAlertActionStyle.Default,
            _ => router.Navigate(new AppRoute.Search(
                Target: SearchRouteTarget.User,
                Subject: row.Username))));
        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiEditNote"),
            UIAlertActionStyle.Default,
            _ => PresentNote(row)));
        if (!row.IsIgnored)
        {
            sheet.AddAction(UIAlertAction.Create(
                $"{AppStrings.Get("IosUiOnlineAlert")}: {AppStrings.Get(row.AlertsWhenOnline ? "IosUiOn" : "IosUiOff")}",
                UIAlertActionStyle.Default,
                _ => store.SetOnlineAlert(row.Username, !row.AlertsWhenOnline)));
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiIgnoreUser"),
                UIAlertActionStyle.Destructive,
                action => _ = RunAsync(() => store.IgnoreAsync(row.Username))));
        }
        else
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Get("IosUiAddFriend"),
                UIAlertActionStyle.Default,
                action => _ = RunAsync(() => store.AddFriendAsync(row.Username))));
        }

        sheet.AddAction(UIAlertAction.Create(
            AppStrings.Get(row.IsIgnored ? "IosUiStopIgnoring" : "IosUiRemoveFriend"),
            UIAlertActionStyle.Destructive,
            _ => ConfirmRemove(row)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.SourceView = TableView;
            popover.SourceRect = TableView.Bounds;
        }

        PresentViewController(sheet, true, null);
    }

    /// <summary>Presents a private note editor whose empty value clears the note.</summary>
    /// <param name="row">The selected user.</param>
    private void PresentNote(UserListRow row)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiEditNote"),
            row.Username,
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Text = row.Note;
            field.Placeholder = AppStrings.Get("IosUiUserNotePlaceholder");
            field.AccessibilityIdentifier = "users.note";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSave"),
            UIAlertActionStyle.Default,
            _ => store.SetNote(row.Username, alert.TextFields?.FirstOrDefault()?.Text)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Confirms friend removal or stopping ignore.</summary>
    /// <param name="row">The selected list row.</param>
    private void ConfirmRemove(UserListRow row)
    {
        string title = AppStrings.Get(row.IsIgnored ? "IosUiStopIgnoring" : "IosUiRemoveFriend");
        var alert = UIAlertController.Create(title, row.Username, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            title,
            UIAlertActionStyle.Destructive,
            action =>
            {
                if (row.IsIgnored)
                {
                    store.StopIgnoring(row.Username);
                }
                else
                {
                    _ = RunAsync(() => store.RemoveFriendAsync(row.Username));
                }
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Runs an asynchronous list mutation with recoverable visible failure feedback.</summary>
    /// <param name="operation">The mutation.</param>
    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch
        {
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiOperationFailed"));
            var alert = UIAlertController.Create(
                AppStrings.Get("IosUiOperationFailed"),
                null,
                UIAlertControllerStyle.Alert);
            alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Cancel, null));
            PresentViewController(alert, true, null);
        }
    }

    /// <summary>Rebuilds grouped rows and explicit empty/filter-zero states.</summary>
    private void ReloadRows()
    {
        IReadOnlyList<UserListRow> rows = store.GetRows(query);
        sections = new[] { false, true }
            .Select(ignored => (Ignored: ignored, Rows: (IReadOnlyList<UserListRow>)rows.Where(row => row.IsIgnored == ignored).ToArray()))
            .Where(section => section.Rows.Count > 0)
            .ToArray();
        TableView.ReloadData();
        if (rows.Count > 0)
        {
            ContentStateView.Clear(this);
        }
        else
        {
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoUsers"),
                string.IsNullOrWhiteSpace(query) ? AppStrings.Get("IosUiNoUsersDetail") : AppStrings.Get("NoUsersFound"),
                "person.2.slash",
                string.IsNullOrWhiteSpace(query) ? AppStrings.Get("IosUiAddFriend") : null,
                string.IsNullOrWhiteSpace(query) ? PresentAdd : null));
        }
    }

    /// <summary>Maps user presence to localized, non-color text.</summary>
    /// <param name="presence">The last observed presence.</param>
    /// <returns>The localized status.</returns>
    private static string PresenceText(UserPresence presence) => presence switch
    {
        UserPresence.Online => AppStrings.Get("IosUiPresenceOnline"),
        UserPresence.Away => AppStrings.Get("IosUiPresenceAway"),
        UserPresence.Offline => AppStrings.Get("IosUiPresenceOffline"),
        _ => AppStrings.Get("IosUiPresenceUnknown"),
    };

    /// <summary>Returns a semantic reinforcement color for presence.</summary>
    /// <param name="presence">The last observed presence.</param>
    /// <returns>A dynamic system color.</returns>
    private static UIColor PresenceColor(UserPresence presence) => presence switch
    {
        UserPresence.Online => UIColor.SystemGreen,
        UserPresence.Away => UIColor.SystemOrange,
        _ => UIColor.SecondaryLabel,
    };

    /// <summary>Reloads rows after immutable user-list state changes.</summary>
    /// <param name="sender">The user-list store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args) => ReloadRows();
}
