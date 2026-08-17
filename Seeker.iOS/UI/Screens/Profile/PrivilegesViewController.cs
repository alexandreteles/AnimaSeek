using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using CoreGraphics;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Profile;

/// <summary>
/// Presents privilege status with donation as the single acquisition path and privilege-day transfer
/// once privileges exist, refreshing automatically when the app returns from the external donation page.
/// </summary>
internal sealed class PrivilegesViewController : UIViewController
{
    private const string DonationPageUrl = "https://www.slsknet.org/userlogin.php";
    private readonly IosAccountService service;
    private readonly UITableView table = new(CGRect.Empty, UITableViewStyle.InsetGrouped)
    {
        TranslatesAutoresizingMaskIntoConstraints = false,
    };
    private readonly UIStackView statusRow = new()
    {
        Axis = UILayoutConstraintAxis.Horizontal,
        Alignment = UIStackViewAlignment.Center,
        Spacing = 8,
        LayoutMarginsRelativeArrangement = true,
        DirectionalLayoutMargins = new NSDirectionalEdgeInsets(8, 20, 8, 20),
        TranslatesAutoresizingMaskIntoConstraints = false,
    };
    private readonly UILabel statusLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
    private readonly UILabel privilegeLabel = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SecondaryLabel);
    private readonly UIActivityIndicatorView progressIndicator = new(UIActivityIndicatorViewStyle.Medium)
    {
        HidesWhenStopped = true,
        TranslatesAutoresizingMaskIntoConstraints = false,
        IsAccessibilityElement = false,
    };
    private UITableViewCell infoCell = null!;
    private UITableViewCell refreshCell = null!;
    private UITableViewCell donateCell = null!;
    private UITableViewCell giveCell = null!;
    private TableSection[] tableSections = [];
    private PrivilegesTableSource? tableSource;
    private NSObject? foregroundObserver;
    private CancellationTokenSource? lifetimeCancellation;
    private string username = string.Empty;
    private int? remainingPrivilegeSeconds;
    private bool busy;
    private bool statusShowsOutcome;

    /// <summary>Creates the Privileges screen over the account facade.</summary>
    /// <param name="service">The facade for privilege state and commands.</param>
    public PrivilegesViewController(IosAccountService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Title = AppStrings.Get("IosUiPrivileges");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "privileges.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        statusLabel.Hidden = true;
        statusLabel.IsAccessibilityElement = true;
        statusLabel.AccessibilityIdentifier = "privileges.status";
        privilegeLabel.IsAccessibilityElement = true;
        privilegeLabel.AccessibilityIdentifier = "privileges.remaining";
        privilegeLabel.Text = AppStrings.Get("IosUiPrivilegesNotChecked");
        ConfigureLayout();
        lifetimeCancellation = new CancellationTokenSource();

        // Donating happens in the browser, so a return to the foreground is the moment server state
        // most likely changed; refreshing silently keeps the screen honest without interrupting.
        foregroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            UIApplication.WillEnterForegroundNotification,
            notification => _ = RefreshAsync(announceFailure: false));
        service.SessionStateChanged += OnSessionStateChanged;
        _ = LoadAsync(lifetimeCancellation.Token);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            service.SessionStateChanged -= OnSessionStateChanged;
            if (foregroundObserver is not null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(foregroundObserver);
                foregroundObserver = null;
            }

            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Builds the static inset-grouped form and the persistent bottom status area.</summary>
    private void ConfigureLayout()
    {
        infoCell = PrivilegeInfoRow();
        refreshCell = ActionRow(
            AppStrings.Get("IosUiCheckPrivileges"), "arrow.clockwise", "privileges.refresh");
        donateCell = ActionRow(
            AppStrings.Get("IosUiDonateToSoulseek"), "heart", "privileges.donate");
        donateCell.AccessibilityHint = AppStrings.Get("IosUiDonateOpensBrowserHint");
        donateCell.AccessoryView = new UIImageView(UIImage.GetSystemImage("arrow.up.forward"))
        {
            TintColor = UIColor.TertiaryLabel,
        };
        giveCell = ActionRow(
            AppStrings.Get("IosUiGivePrivileges"), "gift", "privileges.give");

        RebuildSections();
        tableSource = new PrivilegesTableSource(this);
        table.Source = tableSource;
        table.RowHeight = UITableView.AutomaticDimension;
        table.EstimatedRowHeight = 64;

        statusRow.AddArrangedSubview(progressIndicator);
        statusRow.AddArrangedSubview(statusLabel);
        statusRow.Hidden = true;

        var rootStack = UIKitFactory.VerticalStack(0);
        rootStack.AddArrangedSubview(table);
        rootStack.AddArrangedSubview(statusRow);

        UIView rootView = View ?? throw new InvalidOperationException("The Privileges view is unavailable.");
        rootView.AddSubview(rootStack);
        NSLayoutConstraint.ActivateConstraints(
        [
            rootStack.TopAnchor.ConstraintEqualTo(rootView.TopAnchor),
            rootStack.LeadingAnchor.ConstraintEqualTo(rootView.LeadingAnchor),
            rootStack.TrailingAnchor.ConstraintEqualTo(rootView.TrailingAnchor),
            rootStack.BottomAnchor.ConstraintEqualTo(rootView.SafeAreaLayoutGuide.BottomAnchor),
        ]);
    }

    /// <summary>Projects the current privilege state into visible sections.</summary>
    /// <remarks>
    /// Donation is always available because privilege time is always extendable. The transfer row exists
    /// only while the account has privileges, and the donation explainer only while it confirmedly has
    /// none: before the first check the screen must not claim either state.
    /// </remarks>
    private void RebuildSections()
    {
        bool hasPrivileges = remainingPrivilegeSeconds > 0;
        bool confirmedNone = remainingPrivilegeSeconds == 0;
        TableRow[] actionRows = hasPrivileges
            ? [new TableRow(donateCell, OpenDonationPage), new TableRow(giveCell, PresentPrivilegeForm)]
            : [new TableRow(donateCell, OpenDonationPage)];
        tableSections =
        [
            new TableSection(
                null,
                null,
                [
                    new TableRow(infoCell),
                    new TableRow(refreshCell, () => _ = RefreshAsync()),
                ]),
            new TableSection(
                null,
                confirmedNone ? AppStrings.Get("IosUiPrivilegesDonateHelp") : null,
                actionRows),
        ];
    }

    /// <summary>Creates the non-interactive row presenting current privilege time.</summary>
    /// <returns>A self-sizing information cell.</returns>
    private UITableViewCell PrivilegeInfoRow()
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
        };
        cell.ContentView.AddSubview(privilegeLabel);
        NSLayoutConstraint.ActivateConstraints(
        [
            privilegeLabel.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor, 12),
            privilegeLabel.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor, -12),
            privilegeLabel.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.LeadingAnchor),
            privilegeLabel.TrailingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.TrailingAnchor),
        ]);
        return cell;
    }

    /// <summary>Creates one static action row with button semantics and a stable identifier.</summary>
    /// <param name="title">Visible action name.</param>
    /// <param name="symbol">Reinforcing SF Symbol.</param>
    /// <param name="identifier">Stable nonlocalized identifier.</param>
    /// <returns>A pre-built row that activates on selection.</returns>
    private static UITableViewCell ActionRow(string title, string symbol, string identifier)
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            AccessibilityIdentifier = identifier,
            AccessibilityLabel = title,
        };
        cell.AccessibilityTraits |= UIAccessibilityTrait.Button;
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = title;
        content.Image = UIImage.GetSystemImage(symbol);
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        cell.ContentConfiguration = content;
        return cell;
    }

    /// <summary>Applies interactive availability to a static row for touch and assistive technology alike.</summary>
    /// <param name="cell">The pre-built action row.</param>
    /// <param name="enabled">Whether the row's action can currently run.</param>
    private static void SetRowEnabled(UITableViewCell cell, bool enabled)
    {
        if (cell.UserInteractionEnabled == enabled)
        {
            return;
        }

        cell.UserInteractionEnabled = enabled;
        if (cell.ContentConfiguration is UIListContentConfiguration content)
        {
            content.TextProperties.Color = enabled ? UIColor.Label : UIColor.TertiaryLabel;
            content.ImageProperties.TintColor = enabled ? null : UIColor.TertiaryLabel;
            cell.ContentConfiguration = content;
        }

        if (enabled)
        {
            cell.AccessibilityTraits &= ~UIAccessibilityTrait.NotEnabled;
        }
        else
        {
            cell.AccessibilityTraits |= UIAccessibilityTrait.NotEnabled;
        }
    }

    /// <summary>Loads the donation identity and the first privilege check without blocking the screen.</summary>
    /// <param name="cancellationToken">Cancels when the controller is disposed.</param>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            IosAccountProfileSnapshot profile = await service.GetProfileAsync(cancellationToken);
            username = profile.Username;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // The donation page still opens without a prefilled username.
        }

        // At a cold launch the server connection may still be forming; the session-state
        // subscription issues the first check as soon as the connection exists.
        if (service.IsConnected)
        {
            await RefreshAsync(announceFailure: false);
        }
    }

    /// <summary>Runs the first privilege check once the server connection becomes available.</summary>
    /// <param name="sender">The forwarding account facade.</param>
    /// <param name="args">Unused event payload.</param>
    private void OnSessionStateChanged(object? sender, EventArgs args)
    {
        if (!NSThread.IsMain)
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() => OnSessionStateChanged(sender, args));
            return;
        }

        if (remainingPrivilegeSeconds is null && service.IsConnected && !busy)
        {
            _ = RefreshAsync(announceFailure: false);
        }
    }

    /// <summary>Refreshes server privilege time and re-projects the conditional rows.</summary>
    /// <param name="announceFailure">Whether a failed background refresh should be announced visibly.</param>
    private async Task RefreshAsync(bool announceFailure = true)
    {
        if (busy)
        {
            return;
        }

        SetBusy(true, AppStrings.Get("IosUiCheckingPrivileges"));
        try
        {
            remainingPrivilegeSeconds = await service.GetPrivilegesAsync(lifetimeCancellation?.Token ?? default);
            UpdatePrivilegePresentation();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (announceFailure)
            {
                ShowStatus(
                    AppStrings.Get("IosUiPrivilegesFailed"),
                    isError: true);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Opens the Soulseek donation page for this account in the system browser.</summary>
    private void OpenDonationPage()
    {
        string address = string.IsNullOrWhiteSpace(username)
            ? DonationPageUrl
            : $"{DonationPageUrl}?username={Uri.EscapeDataString(username)}";
        using var url = NSUrl.FromString(address);
        if (url is not null)
        {
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), _ => { });
        }
    }

    /// <summary>Presents recipient and whole-day fields for an explicit privilege transfer.</summary>
    private void PresentPrivilegeForm()
    {
        int availableDays = TransferablePrivilegeDays;
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiGivePrivileges"),
            AppStrings.Format("IosUiGivePrivilegesPrompt", availableDays),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiUsername");
            field.AutocapitalizationType = UITextAutocapitalizationType.None;
            field.AutocorrectionType = UITextAutocorrectionType.No;
            field.TextContentType = UITextContentType.Username;
            field.AccessibilityLabel = AppStrings.Get("IosUiUsername");
            field.AccessibilityIdentifier = "privileges.username";
        });
        alert.AddTextField(field =>
        {
            field.Placeholder = AppStrings.Get("IosUiPrivilegeDays");
            field.KeyboardType = UIKeyboardType.NumberPad;
            field.AccessibilityLabel = AppStrings.Get("IosUiPrivilegeDays");
            field.AccessibilityIdentifier = "privileges.days";
        });
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiGive"),
            UIAlertActionStyle.Default,
            _action => _ = SubmitPrivilegesAsync(alert)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Validates recipient and available whole days before issuing a privilege grant.</summary>
    /// <param name="alert">The privilege form containing recipient and day fields.</param>
    private async Task SubmitPrivilegesAsync(UIAlertController alert)
    {
        string recipient = alert.TextFields?.ElementAtOrDefault(0)?.Text?.Trim() ?? string.Empty;
        string dayText = alert.TextFields?.ElementAtOrDefault(1)?.Text ?? string.Empty;
        if (recipient.Length == 0 ||
            !int.TryParse(dayText, out int days) ||
            days <= 0 ||
            days > TransferablePrivilegeDays)
        {
            ShowStatus(
                AppStrings.Format("IosUiPrivilegesInvalid", TransferablePrivilegeDays),
                isError: true);
            return;
        }

        SetBusy(true, AppStrings.Get("IosUiGivingPrivileges"));
        try
        {
            await service.GrantPrivilegesAsync(recipient, days, lifetimeCancellation?.Token ?? default);
            remainingPrivilegeSeconds = Math.Max(0, (remainingPrivilegeSeconds ?? 0) - (days * 86_400));
            UpdatePrivilegePresentation();
            ShowStatus(
                AppStrings.Format("IosUiPrivilegesGiven", days, recipient),
                isError: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ShowStatus(
                AppStrings.Get("IosUiPrivilegesGrantFailed"),
                isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Updates privilege copy and the conditional donate/give rows without relying on color.</summary>
    private void UpdatePrivilegePresentation()
    {
        privilegeLabel.Text = remainingPrivilegeSeconds switch
        {
            null => AppStrings.Get("IosUiPrivilegesNotChecked"),
            > 0 => AppStrings.Format(
                "IosUiPrivilegesRemaining", TransferablePrivilegeDays, remainingPrivilegeSeconds),
            _ => AppStrings.Get("IosUiNoPrivilegesRemaining"),
        };
        privilegeLabel.AccessibilityLabel = privilegeLabel.Text;
        RebuildSections();
        UpdateControls();
        table.ReloadData();
    }

    /// <summary>Updates busy-dependent and privilege-dependent control availability.</summary>
    private void UpdateControls()
    {
        if (refreshCell is null)
        {
            return;
        }

        SetRowEnabled(refreshCell, !busy);
        SetRowEnabled(donateCell, !busy);
        SetRowEnabled(giveCell, TransferablePrivilegeDays > 0 && !busy);
    }

    /// <summary>Displays persistent inline progress or feedback and announces meaningful outcomes once.</summary>
    /// <param name="message">The concise user-facing state.</param>
    /// <param name="isError">Whether to apply semantic error emphasis.</param>
    private void ShowStatus(string message, bool isError)
    {
        statusShowsOutcome = true;
        statusLabel.Text = message;
        statusLabel.TextColor = isError ? UIColor.SystemRed : UIColor.SecondaryLabel;
        statusLabel.Hidden = false;
        statusRow.Hidden = false;
        AccessibilityExtensions.Announce(message);
    }

    /// <summary>Applies one coherent busy state to controls, indicator, and inline status.</summary>
    /// <param name="value">Whether an operation is active.</param>
    /// <param name="message">Optional progress text shown while active.</param>
    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        if (value)
        {
            statusShowsOutcome = false;
            progressIndicator.StartAnimating();
            statusRow.Hidden = false;
            if (!string.IsNullOrWhiteSpace(message))
            {
                statusLabel.Text = message;
                statusLabel.TextColor = UIColor.SecondaryLabel;
                statusLabel.Hidden = false;
            }
        }
        else
        {
            progressIndicator.StopAnimating();
            if (!statusShowsOutcome)
            {
                // A finished operation with no outcome message would otherwise leave stale progress
                // text; the info row already presents the resulting state.
                statusLabel.Hidden = true;
                statusRow.Hidden = true;
            }
        }

        UpdateControls();
    }

    /// <summary>Gets transferable whole days from the raw server privilege seconds.</summary>
    private int TransferablePrivilegeDays => Math.Max(0, (remainingPrivilegeSeconds ?? 0) / 86_400);

    /// <summary>One static inset-grouped section of the privileges form.</summary>
    /// <param name="Header">Optional visible section header.</param>
    /// <param name="Footer">Optional visible section footer.</param>
    /// <param name="Rows">The pre-built rows in presentation order.</param>
    private sealed record TableSection(string? Header, string? Footer, TableRow[] Rows);

    /// <summary>One pre-built form row and its optional activation.</summary>
    /// <param name="Cell">The retained static cell.</param>
    /// <param name="Activate">The action run when the row is selected, when interactive.</param>
    private sealed record TableRow(UITableViewCell Cell, Action? Activate = null);

    /// <summary>Serves the pre-built static rows so enabled state survives section re-projection.</summary>
    /// <param name="owner">The controller that owns the section model.</param>
    private sealed class PrivilegesTableSource(PrivilegesViewController owner) : UITableViewSource
    {
        /// <inheritdoc/>
        public override nint NumberOfSections(UITableView tableView) => owner.tableSections.Length;

        /// <inheritdoc/>
        public override nint RowsInSection(UITableView tableView, nint section) =>
            owner.tableSections[(int)section].Rows.Length;

        /// <inheritdoc/>
        public override string? TitleForHeader(UITableView tableView, nint section) =>
            owner.tableSections[(int)section].Header;

        /// <inheritdoc/>
        public override string? TitleForFooter(UITableView tableView, nint section) =>
            owner.tableSections[(int)section].Footer;

        /// <inheritdoc/>
        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath) =>
            owner.tableSections[indexPath.Section].Rows[indexPath.Row].Cell;

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            owner.tableSections[indexPath.Section].Rows[indexPath.Row].Activate?.Invoke();
        }
    }
}
