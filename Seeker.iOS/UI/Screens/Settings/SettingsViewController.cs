using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Settings;

/// <summary>Presents the searchable, iOS-applicable settings catalog with transactional side effects.</summary>
internal sealed class SettingsViewController : UITableViewController
{
    private readonly IAppRouter router;
    private readonly SettingsPresentationStore store;
    private readonly SettingsImportService importer;
    private readonly UISearchController searchController = new((UIViewController?)null);
    private readonly UILabel inlineStatus = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
    private IReadOnlyList<SettingsRow> rows = [];
    private IReadOnlyList<string> sections = [];
    private string query = string.Empty;

    /// <summary>Creates the native settings catalog.</summary>
    /// <param name="router">The typed cross-feature router.</param>
    /// <param name="store">The settings presentation and transaction boundary.</param>
    /// <param name="importer">The reviewed settings importer, or the production service when omitted.</param>
    public SettingsViewController(
        IAppRouter router,
        SettingsPresentationStore store,
        SettingsImportService? importer = null)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.importer = importer ?? AppCompositionRoot.SettingsImporter;
        Title = AppStrings.Get("IosUiSettingsTitle");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "settings.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        ConfigureSearch();
        ConfigureTable();
        ReloadRows();
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        store.Changed += OnStoreChanged;
        ReloadRows();
        _ = RefreshNotificationAuthorizationAsync();
    }

    /// <inheritdoc/>
    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        TableView.ResizeTableHeader();
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        store.Changed -= OnStoreChanged;
        base.ViewDidDisappear(animated);
    }

    /// <inheritdoc/>
    public override nint NumberOfSections(UITableView tableView) => sections.Count;

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) =>
        RowsForSection((int)section).Count;

    /// <inheritdoc/>
    public override string? TitleForHeader(UITableView tableView, nint section) => sections[(int)section];

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        SettingsRow row = RowsForSection(indexPath.Section)[indexPath.Row];
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = row.Kind is SettingsControlKind.Toggle or SettingsControlKind.Information
                ? UITableViewCellSelectionStyle.None
                : UITableViewCellSelectionStyle.Default,
            AccessibilityIdentifier = $"settings.row.{row.Id}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = row.Title;
        content.SecondaryText = row.Value is null ? row.Detail : $"{row.Value}\n{row.Detail}";
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        content.TextProperties.Color = row.Kind == SettingsControlKind.Destructive
            ? UIColor.SystemRed
            : UIColor.Label;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.SecondaryTextProperties.NumberOfLines = 0;
        cell.ContentConfiguration = content;
        ConfigureAccessory(cell, row);
        cell.IsAccessibilityElement = row.Kind != SettingsControlKind.Toggle;
        cell.AccessibilityLabel = row.Title;
        cell.AccessibilityHint = row.Detail;
        cell.AccessibilityValue = row.IsOn is bool isOn
            ? AppStrings.Format(
                "IosUiAccessibilitySwitchValue",
                isOn ? AppStrings.Get("IosUiOn") : AppStrings.Get("IosUiOff"),
                row.Value ?? string.Empty)
            : row.Value;
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        SettingsRow row = RowsForSection(indexPath.Section)[indexPath.Row];
        switch (row.Kind)
        {
            case SettingsControlKind.Value:
                if (row.Id is "downloads.speed-limit" or "uploads.speed-limit")
                {
                    NavigationController?.PushViewController(new SpeedLimitViewController(store, row), true);
                }
                else
                {
                    PresentValueEditor(row);
                }
                break;
            case SettingsControlKind.Action:
                _ = RunActionAsync(row.Id);
                break;
            case SettingsControlKind.Navigation:
                Navigate(row.Id);
                break;
            case SettingsControlKind.Destructive:
                ConfirmRestore();
                break;
        }
    }

    /// <summary>Configures native searchable-settings behavior.</summary>
    private void ConfigureSearch()
    {
        searchController.ObscuresBackgroundDuringPresentation = false;
        searchController.SearchBar.Placeholder = AppStrings.Get("IosUiSettingsSearchPlaceholder");
        searchController.SearchBar.AccessibilityIdentifier = "settings.search";
        searchController.SearchResultsUpdater = new SearchUpdater(value =>
        {
            query = value;
            ReloadRows();
        });
        NavigationItem.SearchController = searchController;
        NavigationItem.HidesSearchBarWhenScrolling = false;
        DefinesPresentationContext = true;
    }

    /// <summary>Configures native self-sizing grouped rows and the inline feedback region.</summary>
    private void ConfigureTable()
    {
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 72;
        TableView.KeyboardDismissMode = UIScrollViewKeyboardDismissMode.OnDrag;
        inlineStatus.Lines = 0;
        inlineStatus.IsAccessibilityElement = true;
        inlineStatus.AccessibilityIdentifier = "settings.status";
    }

    /// <summary>Configures the row's native switch or disclosure accessory.</summary>
    /// <param name="cell">The row cell.</param>
    /// <param name="row">The row presentation.</param>
    private void ConfigureAccessory(UITableViewCell cell, SettingsRow row)
    {
        if (row.Kind == SettingsControlKind.Toggle)
        {
            var toggle = new UISwitch
            {
                On = row.IsOn ?? false,
                AccessibilityLabel = row.Title,
                AccessibilityHint = row.Detail,
                AccessibilityIdentifier = $"settings.toggle.{row.Id}",
            };
            toggle.ValueChanged += async (_, _) =>
                await ChangeToggleAsync(row, toggle);
            cell.AccessoryView = toggle;
            return;
        }

        cell.Accessory = row.Kind is SettingsControlKind.Navigation or SettingsControlKind.Value
            ? UITableViewCellAccessory.DisclosureIndicator
            : UITableViewCellAccessory.None;
        if (row.Kind is SettingsControlKind.Action or SettingsControlKind.Destructive)
        {
            cell.AccessibilityTraits |= UIAccessibilityTrait.Button;
        }
    }

    /// <summary>Refreshes notification permission on every appearance without presenting a system prompt.</summary>
    private async Task RefreshNotificationAuthorizationAsync()
    {
        try
        {
            await store.RefreshNotificationAuthorizationAsync();
        }
        catch
        {
            ShowInlineStatus(AppStrings.Get("IosUiNotificationsUnknownFeedback"), isError: true);
        }
    }

    /// <summary>Applies one switch value while preserving a recoverable row on failure.</summary>
    /// <param name="row">The selected setting.</param>
    /// <param name="toggle">The native switch initiating the change.</param>
    private async Task ChangeToggleAsync(SettingsRow row, UISwitch toggle)
    {
        bool requested = toggle.On;
        toggle.Enabled = false;
        try
        {
            await store.SetToggleAsync(row.Id, requested);
            ShowInlineStatus(AppStrings.Get("IosUiSettingChanged"), isError: false);
        }
        catch
        {
            toggle.SetState(!requested, true);
            ShowInlineStatus(AppStrings.Get("IosUiActionFailed"), isError: true);
        }
        finally
        {
            toggle.Enabled = true;
            ReloadRows();
        }
    }

    /// <summary>Runs a non-navigation settings action with bounded progress feedback.</summary>
    /// <param name="id">The stable action identifier.</param>
    private async Task RunActionAsync(string id)
    {
        ShowInlineStatus(AppStrings.Get("IosUiWorking"), isError: false);
        try
        {
            string? result = await store.RunActionAsync(id);
            ShowInlineStatus(result is null ? AppStrings.Get("IosUiSaved") : AppStrings.Get(result), isError: false);
        }
        catch
        {
            ShowInlineStatus(AppStrings.Get("IosUiActionFailed"), isError: true);
        }
    }

    /// <summary>Routes or pushes one settings destination.</summary>
    /// <param name="id">The stable row identifier.</param>
    private void Navigate(string id)
    {
        switch (id)
        {
            case "account.manage":
                router.Navigate(new AppRoute.Account());
                break;
            case "settings.import":
                NavigationController?.PushViewController(new ImportViewController(importer), true);
                break;
            case "settings.diagnostics":
                NavigationController?.PushViewController(
                    new AnimaSeek.iOS.UI.Screens.Diagnostics.DiagnosticsViewController(
                        DiagnosticsPresentationStore.CreateDefault()),
                    true);
                break;
            case "settings.about":
                router.Navigate(new AppRoute.About());
                break;
            case "settings.legal":
                router.Navigate(new AppRoute.LegalNotices());
                break;
            case "sharing.browse-self":
                router.Navigate(new AppRoute.Browse(Common.PreferencesState.Username));
                break;
        }
    }

    /// <summary>Presents a numeric editor with a visible supported range.</summary>
    /// <param name="row">The value setting.</param>
    private void PresentValueEditor(SettingsRow row)
    {
        (int minimum, int maximum) = row.Id switch
        {
            "search.result-limit" => (50, 10_000),
            "listener.port" => (1_024, 65_535),
            "downloads.concurrent-count" => (1, 20),
            _ => (0, 0),
        };
        var alert = UIAlertController.Create(
            row.Title,
            AppStrings.Format("IosUiValueRange", minimum, maximum),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Text = row.Value;
            field.KeyboardType = UIKeyboardType.NumberPad;
            field.AccessibilityLabel = row.Title;
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiSave"),
            UIAlertActionStyle.Default,
            action =>
            {
                _ = SaveIntegerAsync(row.Id, alert.TextFields?.FirstOrDefault()?.Text, minimum, maximum);
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Validates and saves an integer value without discarding the surrounding settings context.</summary>
    /// <param name="id">The stable setting identifier.</param>
    /// <param name="text">The entered text.</param>
    /// <param name="minimum">The inclusive lower bound.</param>
    /// <param name="maximum">The inclusive upper bound.</param>
    private async Task SaveIntegerAsync(string id, string? text, int minimum, int maximum)
    {
        if (!int.TryParse(text, out int value) || value < minimum || value > maximum)
        {
            ShowInlineStatus(AppStrings.Get("IosUiInvalidValue"), isError: true);
            return;
        }

        try
        {
            await store.SetIntegerAsync(id, value);
            ShowInlineStatus(AppStrings.Get("IosUiSaved"), isError: false);
        }
        catch
        {
            ShowInlineStatus(AppStrings.Get("IosUiActionFailed"), isError: true);
        }
    }

    /// <summary>Confirms the scoped defaults reset before mutation.</summary>
    private void ConfirmRestore()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiRestoreDefaults"),
            AppStrings.Get("IosUiRestoreDefaultsConfirm"),
            UIAlertControllerStyle.ActionSheet);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiRestoreDefaultsAction"),
            UIAlertActionStyle.Destructive,
            action =>
            {
                _ = RunActionAsync("settings.restore");
            }));
        if (alert.PopoverPresentationController is { } popover)
        {
            popover.SourceView = TableView;
            popover.SourceRect = TableView.Bounds;
        }
        PresentViewController(alert, true, null);
    }

    /// <summary>Shows calm inline success or recovery feedback without stealing focus.</summary>
    /// <param name="message">The localized message.</param>
    /// <param name="isError">Whether the message describes a failure.</param>
    private void ShowInlineStatus(string message, bool isError)
    {
        inlineStatus.Text = message;
        inlineStatus.TextColor = isError ? UIColor.SystemRed : UIColor.SecondaryLabel;
        inlineStatus.AccessibilityLabel = message;
        var container = new UIView();
        inlineStatus.RemoveFromSuperview();
        container.AddSubview(inlineStatus);
        NSLayoutConstraint.ActivateConstraints(
        [
            inlineStatus.LeadingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.LeadingAnchor),
            inlineStatus.TrailingAnchor.ConstraintEqualTo(container.LayoutMarginsGuide.TrailingAnchor),
            inlineStatus.TopAnchor.ConstraintEqualTo(container.TopAnchor, 8),
            inlineStatus.BottomAnchor.ConstraintEqualTo(container.BottomAnchor, -8),
        ]);
        TableView.SetSelfSizingHeader(container);
        AccessibilityExtensions.Announce(message);
    }

    /// <summary>Rebuilds stable section groups from the current search query.</summary>
    private void ReloadRows()
    {
        rows = store.GetRows(query);
        sections = rows.Select(row => row.Section).Distinct().ToArray();
        TableView.ReloadData();
        if (rows.Count == 0)
        {
            ContentStateView.Show(
                this,
                new ContentStatePresentation(
                    AppStrings.Get("IosUiNoMatchingSettings"),
                    AppStrings.Get("IosUiNoMatchingSettingsDetail"),
                    "magnifyingglass"));
        }
        else
        {
            ContentStateView.Clear(this);
        }
    }

    /// <summary>Gets the rows belonging to one displayed section.</summary>
    /// <param name="section">The section index.</param>
    /// <returns>Rows in catalog order.</returns>
    private IReadOnlyList<SettingsRow> RowsForSection(int section) =>
        rows.Where(row => row.Section == sections[section]).ToArray();

    /// <summary>Refreshes the table after an externally applied setting change.</summary>
    /// <param name="sender">The settings store.</param>
    /// <param name="args">Unused event data.</param>
    private void OnStoreChanged(object? sender, EventArgs args) => ReloadRows();

    /// <summary>Bridges UISearchController updates into a small callback.</summary>
    private sealed class SearchUpdater(Action<string> update) : UISearchResultsUpdating
    {
        private readonly Action<string> update = update;

        /// <inheritdoc/>
        public override void UpdateSearchResultsForSearchController(UISearchController searchController) =>
            update(searchController.SearchBar.Text ?? string.Empty);
    }

    /// <summary>Presents a native, reviewable editor for one live transfer speed governor.</summary>
    private sealed class SpeedLimitViewController : UITableViewController
    {
        private readonly SettingsPresentationStore store;
        private readonly SettingsRow row;
        private readonly UISwitch enableSwitch = new();
        private readonly UITextField rateField = UIKitFactory.TextField(
            AppStrings.Get("IosUiSpeedLimitRatePlaceholder"),
            NSString.Empty);
        private bool perTransfer;
        private bool busy;

        /// <summary>Creates an editor initialized from the latest persisted governor.</summary>
        /// <param name="store">The settings persistence boundary.</param>
        /// <param name="row">The download or upload speed-limit row.</param>
        public SpeedLimitViewController(SettingsPresentationStore store, SettingsRow row)
            : base(UITableViewStyle.InsetGrouped)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.row = row ?? throw new ArgumentNullException(nameof(row));
            Title = row.Title;
        }

        /// <inheritdoc/>
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.SystemGroupedBackground;
            View.AccessibilityIdentifier = $"settings.speed-limit.{row.Id}";
            TableView.RowHeight = UITableView.AutomaticDimension;
            TableView.EstimatedRowHeight = 64;
            TableView.KeyboardDismissMode = UIScrollViewKeyboardDismissMode.OnDrag;

            SettingsSpeedLimit current = store.GetSpeedLimit(row.Id);
            enableSwitch.On = current.Enabled;
            perTransfer = current.PerTransfer;
            rateField.Text = current.KilobytesPerSecond.ToString();
            rateField.KeyboardType = UIKeyboardType.NumberPad;
            rateField.AccessibilityLabel = AppStrings.Get("IosUiSpeedLimitRate");
            rateField.AccessibilityHint = AppStrings.Get("IosUiSpeedLimitRateDetail");
            rateField.AccessibilityIdentifier = $"settings.speed-limit.rate.{row.Id}";
            enableSwitch.AccessibilityLabel = AppStrings.Get("speed_limit_enable");
            enableSwitch.AccessibilityIdentifier = $"settings.speed-limit.enabled.{row.Id}";
            enableSwitch.ValueChanged += (_, _) => RefreshEnabledState();

            NavigationItem.RightBarButtonItem = new UIBarButtonItem(
                AppStrings.Get("IosUiSave"),
                UIBarButtonItemStyle.Done,
                (_, _) => _ = SaveAsync())
            {
                AccessibilityIdentifier = $"settings.speed-limit.save.{row.Id}",
            };
            RefreshEnabledState();
        }

        /// <inheritdoc/>
        public override nint NumberOfSections(UITableView tableView) => 3;

        /// <inheritdoc/>
        public override nint RowsInSection(UITableView tableView, nint section) => section == 1 ? 2 : 1;

        /// <inheritdoc/>
        public override string? TitleForHeader(UITableView tableView, nint section) => section switch
        {
            0 => row.Title,
            1 => AppStrings.Get("IosUiSpeedLimitScope"),
            _ => AppStrings.Get("IosUiSpeedLimitRate"),
        };

        /// <inheritdoc/>
        public override string? TitleForFooter(UITableView tableView, nint section) => section switch
        {
            0 => row.Detail,
            1 => AppStrings.Get("IosUiSpeedLimitScopeDetail"),
            2 => AppStrings.Format(
                "IosUiSpeedLimitRange",
                SettingsPresentationStore.MinimumSpeedLimitKilobytesPerSecond,
                SettingsPresentationStore.MaximumSpeedLimitKilobytesPerSecond),
            _ => null,
        };

        /// <inheritdoc/>
        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath) =>
            indexPath.Section switch
            {
                0 => CreateEnabledCell(),
                1 => CreateScopeCell(indexPath.Row == 0),
                _ => CreateRateCell(),
            };

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            if (indexPath.Section != 1 || !enableSwitch.On || busy)
            {
                return;
            }

            perTransfer = indexPath.Row == 0;
            TableView.ReloadData();
            AccessibilityExtensions.Announce(
                AppStrings.Get(perTransfer ? "IosUiPerTransfer" : "IosUiAcrossAllTransfers"));
        }

        /// <summary>Creates the native enable-switch row.</summary>
        /// <returns>A self-sizing settings cell.</returns>
        private UITableViewCell CreateEnabledCell()
        {
            var cell = CreateContentCell(
                AppStrings.Get("speed_limit_enable"),
                AppStrings.Get("IosUiSpeedLimitEnableDetail"));
            cell.SelectionStyle = UITableViewCellSelectionStyle.None;
            cell.IsAccessibilityElement = false;
            cell.AccessoryView = enableSwitch;
            return cell;
        }

        /// <summary>Creates one mutually exclusive scope choice with a non-color selection cue.</summary>
        /// <param name="representsPerTransfer">Whether this row represents the per-transfer scope.</param>
        /// <returns>A checkmarked scope row.</returns>
        private UITableViewCell CreateScopeCell(bool representsPerTransfer)
        {
            string title = AppStrings.Get(
                representsPerTransfer ? "IosUiPerTransfer" : "IosUiAcrossAllTransfers");
            var cell = CreateContentCell(
                title,
                AppStrings.Get(
                    representsPerTransfer
                        ? "IosUiPerTransferDetail"
                        : "IosUiAcrossAllTransfersDetail"));
            bool selected = perTransfer == representsPerTransfer;
            cell.Accessory = selected ? UITableViewCellAccessory.Checkmark : UITableViewCellAccessory.None;
            cell.SelectionStyle = enableSwitch.On && !busy
                ? UITableViewCellSelectionStyle.Default
                : UITableViewCellSelectionStyle.None;
            cell.UserInteractionEnabled = enableSwitch.On && !busy;
            cell.AccessibilityTraits = selected
                ? UIAccessibilityTrait.Button | UIAccessibilityTrait.Selected
                : UIAccessibilityTrait.Button;
            return cell;
        }

        /// <summary>Creates the numeric KB/s field without hiding units in placeholder text.</summary>
        /// <returns>A row containing the Dynamic Type text field.</returns>
        private UITableViewCell CreateRateCell()
        {
            var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
            {
                SelectionStyle = UITableViewCellSelectionStyle.None,
            };
            rateField.Frame = new CoreGraphics.CGRect(0, 0, 156, 36);
            cell.IsAccessibilityElement = false;
            cell.AccessoryView = rateField;
            UIListContentConfiguration content = cell.DefaultContentConfiguration;
            content.Text = AppStrings.Get("IosUiKilobytesPerSecond");
            content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
            content.TextProperties.AdjustsFontForContentSizeCategory = true;
            content.TextProperties.NumberOfLines = 0;
            cell.ContentConfiguration = content;
            return cell;
        }

        /// <summary>Creates a wrapping grouped cell with semantic text styles.</summary>
        /// <param name="title">The visible row title.</param>
        /// <param name="detail">The visible explanation.</param>
        /// <returns>A configured table cell.</returns>
        private static UITableViewCell CreateContentCell(string title, string detail)
        {
            var cell = new UITableViewCell(UITableViewCellStyle.Default, null);
            UIListContentConfiguration content = cell.DefaultContentConfiguration;
            content.Text = title;
            content.SecondaryText = detail;
            content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
            content.TextProperties.AdjustsFontForContentSizeCategory = true;
            content.TextProperties.NumberOfLines = 0;
            content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
            content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
            content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
            content.SecondaryTextProperties.NumberOfLines = 0;
            cell.ContentConfiguration = content;
            cell.AccessibilityLabel = title;
            cell.AccessibilityHint = detail;
            return cell;
        }

        /// <summary>Matches editable controls and scope choices to the enabled and busy states.</summary>
        private void RefreshEnabledState()
        {
            bool editable = enableSwitch.On && !busy;
            rateField.Enabled = editable;
            NavigationItem.RightBarButtonItem!.Enabled = !busy;
            TableView.ReloadData();
        }

        /// <summary>Validates the positive bounded rate, persists the whole governor, and returns to Settings.</summary>
        private async Task SaveAsync()
        {
            if (busy)
            {
                return;
            }

            if (!int.TryParse(rateField.Text, out int kilobytesPerSecond) ||
                kilobytesPerSecond is < SettingsPresentationStore.MinimumSpeedLimitKilobytesPerSecond or
                    > SettingsPresentationStore.MaximumSpeedLimitKilobytesPerSecond)
            {
                string error = AppStrings.Format(
                    "IosUiSpeedLimitInvalid",
                    SettingsPresentationStore.MinimumSpeedLimitKilobytesPerSecond,
                    SettingsPresentationStore.MaximumSpeedLimitKilobytesPerSecond);
                rateField.BecomeFirstResponder();
                AccessibilityExtensions.Announce(error);
                var alert = UIAlertController.Create(row.Title, error, UIAlertControllerStyle.Alert);
                alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDone"), UIAlertActionStyle.Default, null));
                PresentViewController(alert, true, null);
                return;
            }

            busy = true;
            RefreshEnabledState();
            try
            {
                await store.SetSpeedLimitAsync(row.Id, enableSwitch.On, kilobytesPerSecond, perTransfer);
                AccessibilityExtensions.Announce(AppStrings.Get("IosUiSaved"));
                NavigationController?.PopViewController(true);
            }
            catch
            {
                busy = false;
                RefreshEnabledState();
                string error = AppStrings.Get("IosUiActionFailed");
                AccessibilityExtensions.Announce(error);
                var alert = UIAlertController.Create(row.Title, error, UIAlertControllerStyle.Alert);
                alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiRetry"), UIAlertActionStyle.Default, null));
                PresentViewController(alert, true, null);
            }
        }
    }
}
