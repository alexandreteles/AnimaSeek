using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using CoreGraphics;
using Foundation;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Profile;

/// <summary>Presents one remote user's profile image, biography, status, sharing statistics, and user actions.</summary>
internal sealed class UserProfileViewController : UITableViewController
{
    private readonly IAppRouter router;
    private readonly UserProfilePresentationStore store;
    private readonly string username;
    private readonly bool ownsStore;
    private UserProfileSnapshot? profile;
    private bool loading;

    /// <summary>Creates a remote-profile controller.</summary>
    /// <param name="router">The typed cross-feature router.</param>
    /// <param name="store">The profile presentation store.</param>
    /// <param name="username">The remote user name.</param>
    public UserProfileViewController(
        IAppRouter router,
        UserProfilePresentationStore store,
        string username,
        bool ownsStore = true)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ownsStore = ownsStore;
        this.username = string.IsNullOrWhiteSpace(username)
            ? throw new ArgumentException("A user name is required.", nameof(username))
            : username;
        Title = username;
    }

    /// <summary>Gets the represented remote user for idempotent coordinator matching.</summary>
    public string Username => username;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = $"profile.screen.{AccessibilityIdentifiers.Opaque(username)}";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 72;
        TableView.AccessibilityIdentifier = "profile.list";
        ConfigureActions();
        profile = store.Snapshot;
        if (profile is null)
        {
            _ = RefreshAsync();
        }
        else
        {
            ReloadProfile();
        }
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        EnsureBackAction();
        store.Changed += OnChanged;
        if (store.SupportsPrivilegeGrant)
        {
            _ = RefreshPrivilegeStateAsync();
        }
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
    public override nint NumberOfSections(UITableView tableView) => profile is null ? 0 : 4;

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => section switch
    {
        0 => 2,
        1 => 1,
        2 => StatisticsRows().Count,
        _ => ActionRows().Count,
    };

    /// <inheritdoc/>
    public override string? TitleForHeader(UITableView tableView, nint section) => section switch
    {
        1 => AppStrings.Get("IosUiProfileBiography"),
        2 => AppStrings.Get("IosUiProfileStatistics"),
        3 => AppStrings.Get("IosUiProfileActions"),
        _ => null,
    };

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath) => indexPath.Section switch
    {
        0 when indexPath.Row == 0 => CreateImageCell(),
        0 => CreateStatusCell(),
        1 => CreateBiographyCell(),
        2 => CreateStatisticCell(indexPath.Row),
        _ => CreateActionCell(indexPath.Row),
    };

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        if (indexPath.Section != 3)
        {
            return;
        }

        ActionRows()[indexPath.Row].Invoke();
    }

    /// <summary>Configures refresh and share-image actions.</summary>
    private void ConfigureActions()
    {
        var refresh = new UIBarButtonItem(UIBarButtonSystemItem.Refresh, (_, _) => _ = RefreshAsync())
        {
            AccessibilityLabel = AppStrings.Get("IosUiRetry"),
            AccessibilityIdentifier = "profile.refresh",
        };
        var share = new UIBarButtonItem(UIBarButtonSystemItem.Action, (_, _) => ShareImage())
        {
            AccessibilityLabel = AppStrings.Format("IosUiProfileImage", username),
            AccessibilityIdentifier = "profile.share-image",
            Enabled = false,
        };
        NavigationItem.RightBarButtonItems = [share, refresh];
    }

    /// <summary>
    /// Keeps the secondary profile route escapable when it is opened after an asynchronously restored session.
    /// UIKit normally synthesizes this item, but the deferred launch path can attach the navigation item before the
    /// destination enters its final stack on iOS 26.
    /// </summary>
    private void EnsureBackAction()
    {
        UIViewController[] controllers = NavigationController?.ViewControllers ?? [];
        if (controllers.Length < 2 || ReferenceEquals(controllers[0], this))
        {
            return;
        }

        var back = new UIBarButtonItem(
            UIImage.GetSystemImage("chevron.backward")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => NavigationController?.PopViewController(true))
        {
            AccessibilityLabel = AppStrings.Get("back_desc"),
            AccessibilityIdentifier = "profile.back",
        };
        NavigationItem.LeftBarButtonItem = back;
    }

    /// <summary>Refreshes status, statistics, and profile concurrently through the presentation facade.</summary>
    private async Task RefreshAsync()
    {
        if (loading)
        {
            return;
        }

        bool hadCachedProfile = profile is not null;
        loading = true;
        NavigationItem.RightBarButtonItems?.ToList().ForEach(item => item.Enabled = false);
        TableView.TableHeaderView = null;
        if (!hadCachedProfile)
        {
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiLoadingProfile"),
                username,
                IsLoading: true));
        }

        try
        {
            profile = await store.RefreshAsync();
            ReloadProfile();
            if (!hadCachedProfile)
            {
                AccessibilityExtensions.FocusScreen(TableView);
            }
        }
        catch
        {
            if (hadCachedProfile)
            {
                ShowRefreshErrorBanner();
            }
            else
            {
                ContentStateView.Show(this, new ContentStatePresentation(
                    AppStrings.Get("IosUiProfileFailed"),
                    AppStrings.Get("IosUiProfileFailedDetail"),
                    "person.crop.circle.badge.exclamationmark",
                    AppStrings.Get("IosUiRetry"),
                    () => _ = RefreshAsync()));
            }

            AccessibilityExtensions.Announce(AppStrings.Get("IosUiProfileFailed"));
        }
        finally
        {
            loading = false;
            NavigationItem.RightBarButtonItems?.ToList().ForEach(item => item.Enabled = true);
            UpdateShareAvailability();
        }
    }

    /// <summary>Creates a semantic image/no-image cell with an accessible alternative.</summary>
    /// <returns>The profile image cell.</returns>
    private UITableViewCell CreateImageCell()
    {
        var cell = new ProfileImageCell
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = "profile.image",
        };
        byte[]? bytes = profile?.Info?.Picture;
        UIImage? image = bytes is { Length: > 0 }
            ? UIImage.LoadFromData(NSData.FromArray(bytes))
            : null;
        cell.Apply(image, image is null
            ? AppStrings.Get("IosUiNoProfileImage")
            : AppStrings.Format("IosUiProfileImage", username));
        return cell;
    }

    /// <summary>Creates non-color presence and slot status.</summary>
    /// <returns>A configured status cell.</returns>
    private UITableViewCell CreateStatusCell()
    {
        UserPresence presence = profile?.Status?.Presence ?? UserPresence.Offline;
        bool freeSlot = profile?.Info?.HasFreeUploadSlot == true;
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = "profile.status",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = username;
        content.SecondaryText = $"{PresenceText(presence)} · {AppStrings.Get(freeSlot ? "IosUiFreeSlot" : "IosUiNoFreeSlot")}";
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Title2);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        if (UIImage.GetSystemImage("person.crop.circle") is { } image)
        {
            content.Image = image;
        }

        content.ImageProperties.TintColor = PresenceColor(presence);
        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = username;
        cell.AccessibilityValue = content.SecondaryText;
        return cell;
    }

    /// <summary>Creates a multiline biography cell with an explicit no-biography state.</summary>
    /// <returns>A configured biography cell.</returns>
    private UITableViewCell CreateBiographyCell()
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = "profile.biography",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = string.IsNullOrWhiteSpace(profile?.Info?.Description)
            ? AppStrings.Get("IosUiNoBiography")
            : profile!.Info!.Description;
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        cell.ContentConfiguration = content;
        return cell;
    }

    /// <summary>Creates one sharing-statistic row.</summary>
    /// <param name="index">The statistic row index.</param>
    /// <returns>A configured read-only cell.</returns>
    private UITableViewCell CreateStatisticCell(int index)
    {
        (string Title, string Value, string Symbol) row = StatisticsRows()[index];
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = $"profile.statistic.{row.Symbol.Replace('.', '-')}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = row.Title;
        content.SecondaryText = row.Value;
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        if (UIImage.GetSystemImage(row.Symbol) is { } image)
        {
            content.Image = image;
        }

        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = row.Title;
        cell.AccessibilityValue = row.Value;
        return cell;
    }

    /// <summary>Creates one reusable cross-feature action row.</summary>
    /// <param name="index">The action index.</param>
    /// <returns>A disclosure action cell.</returns>
    private UITableViewCell CreateActionCell(int index)
    {
        ProfileAction row = ActionRows()[index];
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            Accessory = row.ShowsDisclosure
                ? UITableViewCellAccessory.DisclosureIndicator
                : UITableViewCellAccessory.None,
            AccessibilityIdentifier = $"profile.action.{row.Symbol.Replace('.', '-')}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = row.Title;
        content.SecondaryText = row.Detail;
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.SecondaryTextProperties.NumberOfLines = 0;
        if (row.IsDestructive)
        {
            content.TextProperties.Color = UIColor.SystemRed;
        }

        if (UIImage.GetSystemImage(row.Symbol) is { } image)
        {
            content.Image = image;
            content.ImageProperties.TintColor = row.IsDestructive ? UIColor.SystemRed : View!.TintColor;
        }

        cell.ContentConfiguration = content;
        return cell;
    }

    /// <summary>Builds navigation and capability-driven user actions from typed presentation state.</summary>
    /// <returns>The currently available localized action rows.</returns>
    private IReadOnlyList<ProfileAction> ActionRows()
    {
        var rows = new List<ProfileAction>
        {
            new(
                AppStrings.Get("IosUiSendMessage"),
                "message",
                () => router.Navigate(new AppRoute.Messages(username)),
                ShowsDisclosure: true),
            new(
                AppStrings.Get("IosUiBrowseUser"),
                "folder",
                () => router.Navigate(new AppRoute.Browse(username)),
                ShowsDisclosure: true),
            new(
                AppStrings.Get("IosUiSearchUser"),
                "magnifyingglass",
                () => router.Navigate(new AppRoute.Search(
                    Target: SearchRouteTarget.User,
                    Subject: username)),
                ShowsDisclosure: true),
        };
        if (store.UserActionState is not { } state)
        {
            return rows;
        }

        if (state.IsIgnored)
        {
            rows.Add(new ProfileAction(
                AppStrings.Get("IosUiAddFriend"),
                "person.badge.plus",
                () => _ = RunUserActionAsync(() => store.AddFriendAsync())));
            rows.Add(new ProfileAction(
                AppStrings.Get("IosUiStopIgnoring"),
                "hand.raised.slash",
                () => store.StopIgnoring()));
        }
        else if (state.IsFriend)
        {
            rows.Add(new ProfileAction(
                AppStrings.Get("IosUiRemoveFriend"),
                "person.badge.minus",
                () => ConfirmUserAction(
                    AppStrings.Get("IosUiRemoveFriend"),
                    () => store.RemoveFriendAsync()),
                IsDestructive: true));
            rows.Add(new ProfileAction(
                AppStrings.Get("IosUiIgnoreUser"),
                "hand.raised",
                () => ConfirmUserAction(
                    AppStrings.Get("IosUiIgnoreUser"),
                    () => store.IgnoreAsync()),
                IsDestructive: true));
            rows.Add(new ProfileAction(
                $"{AppStrings.Get("IosUiOnlineAlert")}: {AppStrings.Get(state.AlertsWhenOnline ? "IosUiOn" : "IosUiOff")}",
                "bell",
                () => store.SetOnlineAlert(!state.AlertsWhenOnline)));
        }
        else
        {
            rows.Add(new ProfileAction(
                AppStrings.Get("IosUiAddFriend"),
                "person.badge.plus",
                () => _ = RunUserActionAsync(() => store.AddFriendAsync())));
            rows.Add(new ProfileAction(
                AppStrings.Get("IosUiIgnoreUser"),
                "hand.raised",
                () => ConfirmUserAction(
                    AppStrings.Get("IosUiIgnoreUser"),
                    () => store.IgnoreAsync()),
                IsDestructive: true));
        }

        rows.Add(new ProfileAction(
            AppStrings.Get("IosUiEditNote"),
            "note.text",
            () => PresentNote(state.Note)));
        if (store.SupportsPrivilegeGrant)
        {
            rows.Add(new ProfileAction(
                AppStrings.Get("give_privileges"),
                "star.circle",
                () => _ = PresentGivePrivilegesAsync(),
                Detail: store.RemainingPrivilegeSeconds is { } seconds
                    ? PrivilegeBalanceText(seconds)
                    : AppStrings.Get("checking_priv_")));
        }

        return rows;
    }

    /// <summary>Refreshes transferable privilege state without obscuring retained profile content on failure.</summary>
    private async Task RefreshPrivilegeStateAsync()
    {
        try
        {
            await store.RefreshPrivilegesAsync();
            TableView.ReloadData();
        }
        catch
        {
            // The grant action remains available and can retry when explicitly selected.
        }
    }

    /// <summary>Refreshes privilege availability and presents a validated whole-day grant form.</summary>
    private async Task PresentGivePrivilegesAsync()
    {
        int availableSeconds;
        try
        {
            availableSeconds = await store.RefreshPrivilegesAsync();
        }
        catch
        {
            ShowNotice(AppStrings.Get("error_give_priv"), AppStrings.Get("IosUiOperationFailed"));
            return;
        }

        var alert = UIAlertController.Create(
            AppStrings.Get("give_privileges"),
            $"{AppStrings.Format("give_to_", username)}\n{PrivilegeBalanceText(availableSeconds)}",
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("give_hint");
            field.KeyboardType = UIKeyboardType.NumberPad;
            field.AccessibilityIdentifier = "profile.privileges.days";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("give_privileges"),
            UIAlertActionStyle.Default,
            action => _ = GrantPrivilegesAsync(alert.TextFields?.FirstOrDefault()?.Text, availableSeconds)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Validates and grants the entered privilege days with localized exact failure reasons.</summary>
    /// <param name="value">The text-field value.</param>
    /// <param name="availableSeconds">The balance displayed when the form opened.</param>
    private async Task GrantPrivilegesAsync(string? value, int availableSeconds)
    {
        if (!int.TryParse(value, out int days))
        {
            ShowNotice(AppStrings.Get("error_give_priv"), AppStrings.Get("error_days_entered_no_parse"));
            return;
        }

        if (days <= 0)
        {
            ShowNotice(AppStrings.Get("error_give_priv"), AppStrings.Get("error_days_entered_not_positive"));
            return;
        }

        if ((long)days * 86_400 > availableSeconds)
        {
            ShowNotice(
                AppStrings.Get("error_give_priv"),
                AppStrings.Format("error_insufficient_days", days));
            return;
        }

        try
        {
            if (!await store.GrantPrivilegesAsync(days))
            {
                ShowNotice(
                    AppStrings.Get("error_give_priv"),
                    AppStrings.Format("error_insufficient_days", days));
                return;
            }

            string success = AppStrings.Format("give_priv_success", days, username);
            ShowNotice(AppStrings.Get("Privileges"), success);
            AccessibilityExtensions.Announce(success);
        }
        catch
        {
            ShowNotice(AppStrings.Get("error_give_priv"), AppStrings.Get("IosUiOperationFailed"));
        }
    }

    /// <summary>Formats whole transferable days with existing singular and plural localized resources.</summary>
    /// <param name="seconds">The non-negative server-reported balance.</param>
    /// <returns>A localized whole-day balance.</returns>
    private static string PrivilegeBalanceText(int seconds)
    {
        int days = Math.Max(0, seconds) / 86_400;
        return AppStrings.Format(days == 1 ? "day_left" : "days_left", days);
    }

    /// <summary>Presents a localized acknowledgement or recoverable action error.</summary>
    /// <param name="title">The localized title.</param>
    /// <param name="detail">The localized detail.</param>
    private void ShowNotice(string title, string detail)
    {
        var alert = UIAlertController.Create(title, detail, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Cancel, null));
        PresentViewController(alert, true, null);
    }

    /// <summary>Presents a private note editor whose blank value clears the stored note.</summary>
    /// <param name="currentNote">The currently persisted note, when present.</param>
    private void PresentNote(string? currentNote)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiEditNote"),
            username,
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Text = currentNote;
            field.Placeholder = AppStrings.Get("IosUiUserNotePlaceholder");
            field.AccessibilityIdentifier = "profile.note";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSave"),
            UIAlertActionStyle.Default,
            _ => store.SetNote(alert.TextFields?.FirstOrDefault()?.Text)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Confirms a destructive user-list action before contacting the service facade.</summary>
    /// <param name="title">The localized action title.</param>
    /// <param name="operation">The typed user-list operation.</param>
    private void ConfirmUserAction(string title, Func<Task> operation)
    {
        var alert = UIAlertController.Create(title, username, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            title,
            UIAlertActionStyle.Destructive,
            action => _ = RunUserActionAsync(operation)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Runs an asynchronous profile action with localized recoverable failure feedback.</summary>
    /// <param name="operation">The action to run.</param>
    private async Task RunUserActionAsync(Func<Task> operation)
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

    /// <summary>Shows a self-sizing retry banner without covering the retained cached profile.</summary>
    private void ShowRefreshErrorBanner()
    {
        var title = UIKitFactory.Label(UIFontTextStyle.Headline);
        title.Text = AppStrings.Get("IosUiProfileFailed");
        var detail = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
        detail.Text = AppStrings.Get("IosUiProfileFailedDetail");
        var retry = UIKitFactory.Button(
            AppStrings.Get("IosUiRetry"),
            UIButtonConfiguration.TintedButtonConfiguration,
            () => _ = RefreshAsync(),
            "arrow.clockwise");
        var stack = UIKitFactory.VerticalStack(8);
        stack.AddArrangedSubview(title);
        stack.AddArrangedSubview(detail);
        stack.AddArrangedSubview(retry);
        var container = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemBackground,
            AccessibilityIdentifier = "profile.refresh-error",
        };
        container.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(
        [
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor, 12),
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -12),
            stack.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
        ]);
        TableView.SetSelfSizingHeader(container);
    }

    /// <summary>Builds available statistics without inventing placeholder server values.</summary>
    /// <returns>Localized title/value/symbol rows.</returns>
    private IReadOnlyList<(string Title, string Value, string Symbol)> StatisticsRows()
    {
        var rows = new List<(string, string, string)>();
        if (profile?.Statistics is { } statistics)
        {
            rows.Add((
                AppStrings.Get("IosUiShareStatus"),
                AppStrings.Format("IosUiProfileFiles", statistics.FileCount, statistics.DirectoryCount),
                "doc.on.doc"));
            rows.Add((
                AppStrings.Get("IosUiAverageSpeed"),
                AppStrings.Format("IosUiBytesPerSecondValue", statistics.AverageSpeed),
                "speedometer"));
            rows.Add((
                AppStrings.Get("IosUiUploadCount"),
                statistics.UploadCount.ToString("N0"),
                "arrow.up.circle"));
        }

        if (profile?.Info is { } info)
        {
            rows.Add((
                AppStrings.Get("IosUiQueueLength"),
                info.QueueLength.ToString("N0"),
                "list.number"));
            rows.Add((
                AppStrings.Get("IosUiUploadSlots"),
                $"{info.UploadSlots:N0} · {AppStrings.Get(info.HasFreeUploadSlot ? "IosUiFreeSlot" : "IosUiNoFreeSlot")}",
                "person.2"));
        }

        return rows;
    }

    /// <summary>Shares the fetched remote profile image through the native activity sheet.</summary>
    private void ShareImage()
    {
        byte[]? bytes = profile?.Info?.Picture;
        UIImage? image = bytes is { Length: > 0 }
            ? UIImage.LoadFromData(NSData.FromArray(bytes))
            : null;
        if (image is null)
        {
            return;
        }

        var share = new UIActivityViewController([image], null);
        if (share.PopoverPresentationController is { } popover)
        {
            popover.SourceView = View!;
            popover.SourceRect = new CGRect(View!.Bounds.Width - 44, 0, 44, 44);
        }

        PresentViewController(share, true, null);
    }

    /// <summary>Reloads visible rows and enables image sharing only when bytes decoded.</summary>
    private void ReloadProfile()
    {
        profile = store.Snapshot ?? profile;
        ContentStateView.Clear(this);
        TableView.TableHeaderView = null;
        TableView.ReloadData();
        UpdateShareAvailability();
    }

    /// <summary>Updates share action availability from immutable profile data.</summary>
    private void UpdateShareAvailability()
    {
        UIBarButtonItem? share = NavigationItem.RightBarButtonItems?.FirstOrDefault();
        if (share is not null)
        {
            share.Enabled = profile?.Info?.Picture is { Length: > 0 };
        }
    }

    /// <summary>Maps user presence to localized non-color text.</summary>
    /// <param name="presence">The server presence.</param>
    /// <returns>The localized status.</returns>
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

    /// <summary>Reloads when the selected profile snapshot changes.</summary>
    /// <param name="sender">The profile store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnChanged(object? sender, EventArgs args)
    {
        if (store.Snapshot is { } refreshedProfile)
        {
            profile = refreshedProfile;
            ContentStateView.Clear(this);
        }

        TableView.ReloadData();
        UpdateShareAvailability();
    }

    /// <summary>Describes one localized profile action without inferring behavior from display text.</summary>
    /// <param name="Title">The localized visible action name.</param>
    /// <param name="Symbol">The SF Symbol name.</param>
    /// <param name="Invoke">The typed action.</param>
    /// <param name="ShowsDisclosure">Whether the row navigates to another destination.</param>
    /// <param name="IsDestructive">Whether the action requires destructive styling and confirmation.</param>
    /// <param name="Detail">Optional localized status text.</param>
    private sealed record ProfileAction(
        string Title,
        string Symbol,
        Action Invoke,
        bool ShowsDisclosure = false,
        bool IsDestructive = false,
        string? Detail = null);

    /// <summary>Self-sizing profile-image cell with explicit accessible no-image fallback.</summary>
    private sealed class ProfileImageCell : UITableViewCell
    {
        private readonly UIImageView profileImage = new()
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            ClipsToBounds = true,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = true,
        };
        private readonly UILabel fallback = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SecondaryLabel);

        /// <summary>Creates and constrains the image presentation.</summary>
        public ProfileImageCell()
            : base(UITableViewCellStyle.Default, reuseIdentifier: null)
        {
            fallback.TextAlignment = UITextAlignment.Center;
            ContentView.AddSubviews(profileImage, fallback);
            NSLayoutConstraint.ActivateConstraints(
            [
                profileImage.TopAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.TopAnchor),
                profileImage.BottomAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.BottomAnchor),
                profileImage.LeadingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.LeadingAnchor),
                profileImage.TrailingAnchor.ConstraintEqualTo(ContentView.LayoutMarginsGuide.TrailingAnchor),
                profileImage.HeightAnchor.ConstraintGreaterThanOrEqualTo(160),
                fallback.CenterYAnchor.ConstraintEqualTo(profileImage.CenterYAnchor),
                fallback.LeadingAnchor.ConstraintEqualTo(profileImage.LeadingAnchor),
                fallback.TrailingAnchor.ConstraintEqualTo(profileImage.TrailingAnchor),
            ]);
        }

        /// <summary>Applies decoded image data or accessible fallback text.</summary>
        /// <param name="image">The decoded profile image.</param>
        /// <param name="label">The accessible alternative.</param>
        public void Apply(UIImage? image, string label)
        {
            profileImage.Image = image;
            profileImage.AccessibilityLabel = label;
            profileImage.IsAccessibilityElement = image is not null;
            fallback.Text = image is null ? label : string.Empty;
            fallback.Hidden = image is not null;
            fallback.IsAccessibilityElement = image is null;
        }
    }
}
