using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Diagnostics;

/// <summary>Presents safe, read-only diagnostics and a reviewable system log-sharing action.</summary>
internal sealed class DiagnosticsViewController : UITableViewController
{
    private readonly DiagnosticsPresentationStore store;
    private DiagnosticsSnapshot snapshot = null!;
    private bool refreshing;

    /// <summary>Creates the diagnostics destination.</summary>
    /// <param name="store">The immutable diagnostic and log facade.</param>
    public DiagnosticsViewController(DiagnosticsPresentationStore store)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        Title = AppStrings.Get("IosUiDiagnosticsTitle");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "diagnostics.screen";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 62;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            UIBarButtonSystemItem.Refresh,
            (_, _) => _ = RefreshAsync())
        {
            AccessibilityLabel = AppStrings.Get("IosUiRefreshDiagnostics"),
            AccessibilityIdentifier = "diagnostics.refresh",
        };
        snapshot = store.GetSnapshot();
    }

    /// <inheritdoc/>
    public override nint NumberOfSections(UITableView tableView) => 2;

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => section == 0 ? 5 : 1;

    /// <inheritdoc/>
    public override string? TitleForHeader(UITableView tableView, nint section) => section == 0
        ? AppStrings.Get("IosUiDiagnosticsTitle")
        : AppStrings.Get("IosUiSupportSection");

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        if (indexPath.Section == 1)
        {
            return CreateLogCell();
        }

        (string title, string value, string id, string symbol) = indexPath.Row switch
        {
            0 => (AppStrings.Get("IosUiAppVersion"), snapshot.Version, "version", "app.badge"),
            1 => (
                AppStrings.Get("IosUiConnectionStatus"),
                snapshot.IsConnected ? AppStrings.Get("IosUiConnected") : AppStrings.Get("IosUiNotConnected"),
                "connection",
                snapshot.IsConnected ? "checkmark.circle" : "network.slash"),
            2 => (
                AppStrings.Get("IosUiShareStatus"),
                AppStrings.Format("IosUiShareCounts", snapshot.SharedFileCount, snapshot.SharedDirectoryCount),
                "shares",
                "folder"),
            3 => (
                AppStrings.Get("IosUiPortMapping"),
                DescribePortMapping(snapshot.PortMapping),
                "nat-pmp",
                "network"),
            _ => (
                AppStrings.Get("IosUiBackgroundTransfers"),
                DescribeBackgroundTransfers(snapshot.BackgroundTransfers),
                "background-transfers",
                snapshot.BackgroundTransfers is ContinuedTransferAvailability.Available
                    ? "arrow.down.circle"
                    : "moon.zzz"),
        };
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = $"diagnostics.{id}",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = title;
        content.SecondaryText = value;
        content.Image = UIImage.GetSystemImage(symbol);
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.SecondaryTextProperties.NumberOfLines = 0;
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        cell.ContentConfiguration = content;
        cell.IsAccessibilityElement = true;
        cell.AccessibilityLabel = title;
        cell.AccessibilityValue = value;
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        if (indexPath.Section == 1)
        {
            ShareLog();
        }
    }

    /// <summary>Creates the privacy-explained log-sharing action row.</summary>
    /// <returns>A native disclosure cell.</returns>
    private UITableViewCell CreateLogCell()
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            Accessory = UITableViewCellAccessory.DisclosureIndicator,
            AccessibilityIdentifier = "diagnostics.share-log",
        };
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = AppStrings.Get("IosUiShareLogs");
        content.SecondaryText = AppStrings.Get("IosUiShareLogsDetail");
        content.Image = UIImage.GetSystemImage("square.and.arrow.up");
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.SecondaryTextProperties.NumberOfLines = 0;
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        cell.ContentConfiguration = content;
        cell.AccessibilityLabel = AppStrings.Get("IosUiShareLogs");
        cell.AccessibilityHint = AppStrings.Get("IosUiShareLogsDetail");
        return cell;
    }

    /// <summary>Refreshes bounded diagnostics while keeping the current snapshot visible.</summary>
    private async Task RefreshAsync()
    {
        if (refreshing)
        {
            return;
        }

        refreshing = true;
        NavigationItem.RightBarButtonItem!.Enabled = false;
        try
        {
            await store.RefreshAsync();
            snapshot = store.GetSnapshot();
            TableView.ReloadData();
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiSaved"));
        }
        catch
        {
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiActionFailed"));
        }
        finally
        {
            refreshing = false;
            NavigationItem.RightBarButtonItem!.Enabled = true;
        }
    }

    /// <summary>Presents the system share sheet for an existing non-empty log file.</summary>
    private void ShareLog()
    {
        NSUrl? url = store.GetLogUrl();
        if (url is null)
        {
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiLogUnavailable"));
            var alert = UIAlertController.Create(
                AppStrings.Get("IosUiShareLogs"),
                AppStrings.Get("IosUiLogUnavailable"),
                UIAlertControllerStyle.Alert);
            alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClose"), UIAlertActionStyle.Cancel, null));
            PresentViewController(alert, true, null);
            return;
        }

        var activity = new UIActivityViewController([url], null);
        if (activity.PopoverPresentationController is { } popover)
        {
            popover.SourceView = TableView;
            popover.SourceRect = TableView.Bounds;
        }

        PresentViewController(activity, true, null);
    }

    /// <summary>Maps NAT-PMP state to localized, non-color diagnostic text.</summary>
    /// <param name="value">The immutable mapping snapshot.</param>
    /// <returns>A localized status description.</returns>
    private static string DescribePortMapping(NatPmpPortMappingSnapshot value) => value.State switch
    {
        NatPmpPortMappingState.Disabled => AppStrings.Get("IosUiNatDisabled"),
        NatPmpPortMappingState.WaitingForSession => AppStrings.Get("IosUiNatWaitingSession"),
        NatPmpPortMappingState.WaitingForLocalNetwork => AppStrings.Get("IosUiNatWaitingNetwork"),
        NatPmpPortMappingState.Discovering => AppStrings.Get("IosUiNatDiscovering"),
        NatPmpPortMappingState.CreatingMapping => AppStrings.Get("IosUiNatCreating"),
        NatPmpPortMappingState.Mapped => AppStrings.Format(
            "IosUiNatMapped",
            value.PrivatePort ?? 0,
            value.PublicPort ?? 0),
        NatPmpPortMappingState.Unavailable => AppStrings.Get("IosUiNatUnavailable"),
        NatPmpPortMappingState.Suspended => AppStrings.Get("IosUiNatSuspended"),
        _ => AppStrings.Get("IosUiUnknown"),
    };

    /// <summary>Describes whether iOS is currently continuing transfers after the app leaves the foreground.</summary>
    /// <param name="value">The latest continued-processing grant outcome.</param>
    /// <returns>A localized explanation of the current background-execution reality.</returns>
    private static string DescribeBackgroundTransfers(ContinuedTransferAvailability value) => value switch
    {
        ContinuedTransferAvailability.Available => AppStrings.Get("IosUiBackgroundTransfersAvailable"),
        ContinuedTransferAvailability.Declined => AppStrings.Get("IosUiBackgroundTransfersDeclined"),
        ContinuedTransferAvailability.Unavailable => AppStrings.Get("IosUiBackgroundTransfersUnavailable"),
        _ => AppStrings.Get("IosUiBackgroundTransfersUnknown"),
    };
}
