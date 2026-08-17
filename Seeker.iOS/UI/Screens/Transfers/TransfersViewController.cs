using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation.Transfers;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Transfers;

/// <summary>Presents downloads/uploads as stable folder or file rows with truthful state, progress, and safe actions.</summary>
internal sealed class TransfersViewController : UIViewController, IAppRouteReceiving
{
    private readonly TransfersPresentationStore store;
    private readonly IAppRouter router;
    private readonly UISegmentedControl mode = new([
        AppStrings.Get("IosUiTransfersDownloads"),
        AppStrings.Get("IosUiTransfersUploads"),
    ]);
    private readonly DiffableFeatureListView list = new();
    private string? requestedTransferId;
    private UIDocumentInteractionController? documentInteraction;
    private UIBarButtonItem? groupingButton;
    private TransferGrouping groupingBeforeEditing;

    /// <summary>Creates Transfers using a detached session-backed store.</summary>
    public TransfersViewController(TransfersPresentationStore store, IAppRouter router)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        Title = AppStrings.Get("transfer_tab");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = "transfers.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;
        mode.SelectedSegment = (nint)store.State.Mode;
        mode.TranslatesAutoresizingMaskIntoConstraints = false;
        mode.AccessibilityIdentifier = "transfers.mode";
        mode.ValueChanged += (_, _) => store.SetMode((TransferMode)(int)mode.SelectedSegment);
        // A compact near-square glyph keeps the glass bar button circular like every other header
        // action; wide symbols such as rectangle.3.group stretch it into a lozenge.
        groupingButton = new UIBarButtonItem(
            UIImage.GetSystemImage("rectangle.grid.1x2")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentGrouping())
        {
            AccessibilityLabel = AppStrings.Get("IosUiTransfersGrouped"),
            AccessibilityIdentifier = "transfers.grouping",
        };
        NavigationItem.RightBarButtonItem = groupingButton;
        NavigationItem.LeftBarButtonItem = EditButtonItem;
        View.AddSubviews(mode, list);
        NSLayoutConstraint.ActivateConstraints([
            mode.LeadingAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.LeadingAnchor),
            mode.TrailingAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.TrailingAnchor),
            mode.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 8),
            mode.HeightAnchor.ConstraintGreaterThanOrEqualTo(44),
            list.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            list.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            list.TopAnchor.ConstraintEqualTo(mode.BottomAnchor, 8),
            list.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
        list.ItemSelected += (_, identifier) => Select(identifier);
        list.SelectionChanged += (_, _) => UpdateSelectionToolbar();
        store.Changed += OnChanged;
        Render();
    }

    /// <inheritdoc/>
    public override void SetEditing(bool editing, bool animated)
    {
        if (editing == Editing)
        {
            return;
        }

        base.SetEditing(editing, animated);
        if (editing)
        {
            groupingBeforeEditing = store.State.Grouping;
            list.SelectionMode = true;
            mode.Enabled = false;
            if (groupingButton is not null)
            {
                groupingButton.Enabled = false;
            }

            if (store.State.Grouping != TransferGrouping.Individual)
            {
                store.SetGrouping(TransferGrouping.Individual);
            }
        }
        else
        {
            list.SelectionMode = false;
            mode.Enabled = true;
            if (groupingButton is not null)
            {
                groupingButton.Enabled = true;
            }

            if (store.State.Grouping != groupingBeforeEditing)
            {
                store.SetGrouping(groupingBeforeEditing);
            }
        }

        UpdateSelectionToolbar(animated);
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

    /// <inheritdoc/>
    public void Receive(AppRoute route)
    {
        if (route is AppRoute.Transfers transfers)
        {
            requestedTransferId = transfers.StableId;
            Render();
        }
    }

    private void OnChanged(object? sender, EventArgs args) => Render();

    private void Render()
    {
        TransfersScreenState state = store.State;
        mode.SelectedSegment = (nint)state.Mode;
        EditButtonItem.Enabled = !state.IsEmpty;
        if (state.IsEmpty)
        {
            list.Apply([]);
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoTransfers"),
                AppStrings.Get("IosUiNoTransfersDetail"),
                "arrow.up.arrow.down"));
            return;
        }

        ContentStateView.Clear(this);
        FeatureListItem[] items = state.Grouping == TransferGrouping.ByFolder
            ? state.Folders.Select(FolderItem).ToArray()
            : state.Files.Select(FileItem).ToArray();
        list.Apply(items);
        if (Editing)
        {
            UpdateSelectionToolbar();
        }
        if (requestedTransferId is { } requested && state.Files.Any(row => row.Id == requested))
        {
            requestedTransferId = null;
            TransferRow row = state.Files.First(candidate => candidate.Id == requested);
            PresentActions(row);
        }
    }

    private static FeatureListItem FolderItem(TransferFolderRow folder) => new(
        folder.Id,
        folder.Title,
        string.Join(" · ", folder.Peer, folder.StatusText, folder.Metadata),
        "folder",
        ShowsDisclosure: true,
        AccessibilityLabel: string.Join(", ", folder.Title, folder.Peer, folder.StatusText, folder.Metadata),
        Progress: folder.Progress,
        MonospacedDigits: true);

    private static FeatureListItem FileItem(TransferRow row) => new(
        row.Id,
        row.Title,
        string.Join(" · ", row.StatusText, row.Metadata),
        StatusSymbol(row.Status),
        ShowsDisclosure: true,
        AccessibilityLabel: string.Join(", ", row.Title, row.StatusText, row.Metadata),
        Progress: row.Progress,
        MonospacedDigits: true);

    private void Select(string identifier)
    {
        if (store.State.Grouping == TransferGrouping.ByFolder)
        {
            TransferFolderRow? folder = store.State.Folders.FirstOrDefault(candidate => candidate.Id == identifier);
            if (folder is not null)
            {
                NavigationController?.PushViewController(new TransferFolderViewController(store, folder.Id), true);
            }

            return;
        }

        TransferRow? row = store.State.Files.FirstOrDefault(candidate => candidate.Id == identifier);
        if (row is not null)
        {
            PresentActions(row);
        }
    }

    private void PresentGrouping()
    {
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiTransfersGrouped"),
            null,
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiTransfersGrouped"), UIAlertActionStyle.Default, _ =>
            store.SetGrouping(TransferGrouping.ByFolder)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiTransfersIndividual"), UIAlertActionStyle.Default, _ =>
            store.SetGrouping(TransferGrouping.Individual)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void UpdateSelectionToolbar(bool animated = false)
    {
        if (!Editing)
        {
            this.SetSelectionAccessory([], animated);
            return;
        }

        int selected = list.SelectedItemIds.Count;
        int total = store.State.Files.Count;
        UIBarButtonItem count = UIKitFactory.ToolbarStatusItem(
            AppStrings.Format("IosUiSelectedCountCompact", selected, total),
            AppStrings.Format("IosUiSelectedCount", selected, total),
            "transfers.selection-count");
        var select = new UIBarButtonItem(
            AppStrings.Get("IosUiSelection"),
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentSelectionActions())
        {
            AccessibilityIdentifier = "transfers.selection-scope",
        };
        var actions = new UIBarButtonItem(
            AppStrings.Get("IosUiBatchActions"),
            UIBarButtonItemStyle.Done,
            (_, _) => PresentBatchActions())
        {
            Enabled = selected > 0,
            AccessibilityIdentifier = "transfers.batch-actions",
        };
        // Actionable items hold the edges and the count sits between them, matching the folder review toolbar. A
        // leading count is the widest item and gets pushed past the toolbar's own inset once selection text grows.
        this.SetSelectionAccessory(
        [
            select,
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
        string[] allIds = store.State.Files.Select(row => row.Id).ToArray();
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiSelection"),
            AppStrings.Format("IosUiSelectionScopeAllTransfers", allIds.Length),
            UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiSelectAll"), UIAlertActionStyle.Default, _ =>
            list.SetSelectedItems(allIds)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiInvertSelection"), UIAlertActionStyle.Default, _ =>
            list.InvertSelection(allIds)));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDeselectAll"), UIAlertActionStyle.Default, _ =>
            list.SetSelectedItems([])));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void PresentBatchActions()
    {
        TransferRow[] selected = SelectedRows();
        if (selected.Length == 0)
        {
            return;
        }

        int retryCount = selected.Count(row => row.Capabilities.CanRetry);
        int pauseCount = selected.Count(row => row.Capabilities.CanPause);
        int clearCount = selected.Count(row => row.Capabilities.CanClear);
        var sheet = UIAlertController.Create(
            AppStrings.Get("IosUiBatchActions"),
            AppStrings.Format("IosUiSelectedCount", selected.Length, store.State.Files.Count),
            UIAlertControllerStyle.ActionSheet);
        if (retryCount > 0)
        {
            bool resumeOnly = selected
                .Where(row => row.Capabilities.CanRetry)
                .All(row => row.Status == TransferPresentationStatus.Paused);
            string label = AppStrings.Get(resumeOnly ? "IosUiResumeTransfer" : "IosUiRetryTransfer");
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Format("IosUiBatchActionEligibility", label, retryCount, selected.Length),
                UIAlertActionStyle.Default,
                action => _ = ExecuteBatchAsync(TransferCommand.Retry)));
        }

        if (pauseCount > 0)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Format(
                    "IosUiBatchActionEligibility",
                    AppStrings.Get("IosUiPauseTransfer"),
                    pauseCount,
                    selected.Length),
                UIAlertActionStyle.Default,
                action => _ = ExecuteBatchAsync(TransferCommand.Pause)));
        }

        if (clearCount > 0)
        {
            sheet.AddAction(UIAlertAction.Create(
                AppStrings.Format(
                    "IosUiBatchActionEligibility",
                    AppStrings.Get("IosUiClearTransfer"),
                    clearCount,
                    selected.Length),
                UIAlertActionStyle.Destructive,
                _ => ConfirmClearBatch(clearCount, selected.Length)));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private TransferRow[] SelectedRows()
    {
        IReadOnlySet<string> selected = list.SelectedItemIds;
        return store.State.Files.Where(row => selected.Contains(row.Id)).ToArray();
    }

    private void ConfirmClearBatch(int eligibleCount, int selectedCount)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiClearTransfer"),
            AppStrings.Format("IosUiClearBatchConfirm", eligibleCount, selectedCount),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, action =>
            _ = ExecuteBatchAsync(TransferCommand.Clear)));
        PresentViewController(alert, true, null);
    }

    private async Task ExecuteBatchAsync(TransferCommand command)
    {
        TransferBatchCommandResult result = await store.ExecuteBatchAsync(
            list.SelectedItemIds.ToArray(),
            command);
        AccessibilityExtensions.Announce(AppStrings.Format(
            "IosUiBatchActionResult",
            result.AcceptedCount,
            result.EligibleCount,
            result.RequestedCount));
        list.SetSelectedItems([]);
        UpdateSelectionToolbar();
    }

    private void PresentActions(TransferRow row)
    {
        var sheet = UIAlertController.Create(row.Title, row.StatusText, UIAlertControllerStyle.ActionSheet);
        if (row.Capabilities.CanOpen && TryGetUrl(row.FinalUri, out NSUrl? url))
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiOpenFile"), UIAlertActionStyle.Default, _ =>
                OpenFile(url!)));
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiShareFile"), UIAlertActionStyle.Default, _ =>
                PresentViewController(new UIActivityViewController([url!], null), true, null)));
        }

        if (row.Capabilities.CanRetry)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get(row.Status == TransferPresentationStatus.Paused
                ? "IosUiResumeTransfer"
                : "IosUiRetryTransfer"), UIAlertActionStyle.Default, action =>
                _ = ExecuteAsync(row.Id, TransferCommand.Retry)));
        }

        if (row.Capabilities.CanPause)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiPauseTransfer"), UIAlertActionStyle.Default, action =>
                _ = ExecuteAsync(row.Id, TransferCommand.Pause)));
        }

        if (row.Capabilities.CanClear)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, _ =>
                ConfirmClear(row)));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiBrowseUser"), UIAlertActionStyle.Default, _ =>
            router.Navigate(new AppRoute.Browse(row.Peer))));
        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void ConfirmClear(TransferRow row)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiClearTransfer"),
            AppStrings.Get("IosUiClearTransferConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, action =>
            _ = ExecuteAsync(row.Id, TransferCommand.Clear)));
        PresentViewController(alert, true, null);
    }

    private async Task ExecuteAsync(string identifier, TransferCommand command)
    {
        bool accepted = await store.ExecuteAsync(identifier, command);
        if (!accepted && store.State.Message is { } message)
        {
            ShowMessage(message);
        }
    }

    private void ShowMessage(string message)
    {
        var alert = UIAlertController.Create(AppStrings.Get("IosUiActionFailed"), message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }

    private void OpenFile(NSUrl url)
    {
        documentInteraction = UIDocumentInteractionController.FromUrl(url);
        if (!documentInteraction.PresentOpenInMenu(View!.Bounds, View, true))
        {
            ShowMessage(AppStrings.Get("IosUiNoAppCanOpenFile"));
        }
    }

    private static bool TryGetUrl(string? value, out NSUrl? url)
    {
        url = string.IsNullOrWhiteSpace(value) ? null : NSUrl.FromString(value);
        return url is not null;
    }

    internal static string StatusSymbol(TransferPresentationStatus status) => status switch
    {
        TransferPresentationStatus.Active => "arrow.down.circle",
        TransferPresentationStatus.Completed => "checkmark.circle.fill",
        TransferPresentationStatus.Failed or TransferPresentationStatus.TimedOut or
            TransferPresentationStatus.Denied or TransferPresentationStatus.Offline or
            TransferPresentationStatus.Aborted => "exclamationmark.triangle",
        TransferPresentationStatus.Paused or TransferPresentationStatus.WaitingForPeer => "pause.circle",
        TransferPresentationStatus.Canceled => "xmark.circle",
        _ => "clock",
    };
}

/// <summary>Presents the live individual transfer children of one aggregate folder.</summary>
internal sealed class TransferFolderViewController : UIViewController
{
    private readonly TransfersPresentationStore store;
    private readonly string folderId;
    private readonly DiffableFeatureListView list = new();
    private UIDocumentInteractionController? documentInteraction;

    /// <summary>Creates one folder detail using a stable aggregate identifier.</summary>
    public TransferFolderViewController(TransfersPresentationStore store, string folderId)
    {
        this.store = store;
        this.folderId = folderId;
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        TransferFolderRow? folder = store.State.Folders.FirstOrDefault(candidate => candidate.Id == folderId);
        Title = folder?.Title ?? AppStrings.Get("transfer_tab");
        View!.BackgroundColor = UIColor.SystemBackground;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            UIImage.GetSystemImage("ellipsis.circle")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentFolderActions())
        {
            AccessibilityLabel = AppStrings.Get("IosUiFolderActions"),
        };
        View.AddSubview(list);
        NSLayoutConstraint.ActivateConstraints([
            list.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            list.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            list.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            list.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
        ]);
        list.ItemSelected += (_, id) => PresentActions(id);
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
        TransferRow[] rows = [.. store.GetFolderFiles(folderId)];
        list.Apply(rows.Select(row => new FeatureListItem(
            row.Id,
            row.Title,
            string.Join(" · ", row.StatusText, row.Metadata),
            TransfersViewController.StatusSymbol(row.Status),
            ShowsDisclosure: true,
            AccessibilityLabel: string.Join(", ", row.Title, row.StatusText, row.Metadata),
            Progress: row.Progress,
            MonospacedDigits: true)).ToArray());
        if (rows.Length == 0)
        {
            ContentStateView.Show(this, new ContentStatePresentation(
                AppStrings.Get("IosUiNoTransfers"),
                AppStrings.Get("IosUiNoTransfersDetail"),
                "folder"));
        }
        else
        {
            ContentStateView.Clear(this);
        }
    }

    private void PresentActions(string id)
    {
        TransferRow? row = store.GetFolderFiles(folderId).FirstOrDefault(candidate => candidate.Id == id);
        if (row is null)
        {
            return;
        }

        var sheet = UIAlertController.Create(row.Title, row.StatusText, UIAlertControllerStyle.ActionSheet);
        if (row.Capabilities.CanOpen && TryGetUrl(row.FinalUri, out NSUrl? url))
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiOpenFile"), UIAlertActionStyle.Default, _ =>
                OpenFile(url!)));
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiShareFile"), UIAlertActionStyle.Default, _ =>
                PresentViewController(new UIActivityViewController([url!], null), true, null)));
        }

        if (row.Capabilities.CanRetry)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get(row.Status == TransferPresentationStatus.Paused
                ? "IosUiResumeTransfer"
                : "IosUiRetryTransfer"), UIAlertActionStyle.Default, action =>
                _ = store.ExecuteAsync(row.Id, TransferCommand.Retry)));
        }

        if (row.Capabilities.CanPause)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiPauseTransfer"), UIAlertActionStyle.Default, action =>
                _ = store.ExecuteAsync(row.Id, TransferCommand.Pause)));
        }

        if (row.Capabilities.CanClear)
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, _ =>
                ConfirmClear(row)));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void ConfirmClear(TransferRow row)
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiClearTransfer"),
            AppStrings.Get("IosUiClearTransferConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, action =>
            _ = store.ExecuteAsync(row.Id, TransferCommand.Clear)));
        PresentViewController(alert, true, null);
    }

    private void PresentFolderActions()
    {
        TransferRow[] rows = [.. store.GetFolderFiles(folderId)];
        var sheet = UIAlertController.Create(
            Title,
            AppStrings.Format("IosUiTransferFolderMetadata", rows.Length,
                AnimaSeek.iOS.UI.Presentation.FeatureValueFormatter.Bytes(
                    rows.Aggregate(0L, (sum, row) => sum + Math.Max(0, row.Size)))),
            UIAlertControllerStyle.ActionSheet);
        if (rows.Any(row => row.Capabilities.CanRetry))
        {
            bool resumeOnly = rows.Where(row => row.Capabilities.CanRetry)
                .All(row => row.Status == TransferPresentationStatus.Paused);
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get(resumeOnly
                ? "IosUiResumeTransfer"
                : "IosUiRetryTransfer"), UIAlertActionStyle.Default, action =>
                _ = store.ExecuteFolderAsync(folderId, TransferCommand.Retry)));
        }

        if (rows.Any(row => row.Capabilities.CanPause))
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiPauseTransfer"), UIAlertActionStyle.Default, action =>
                _ = store.ExecuteFolderAsync(folderId, TransferCommand.Pause)));
        }

        if (rows.Any(row => row.Capabilities.CanClear))
        {
            sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, _ =>
                ConfirmClearFolder()));
        }

        sheet.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        PresentViewController(sheet, true, null);
    }

    private void ConfirmClearFolder()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiClearTransfer"),
            AppStrings.Get("IosUiClearFolderConfirm"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiClearTransfer"), UIAlertActionStyle.Destructive, action =>
            _ = store.ExecuteFolderAsync(folderId, TransferCommand.Clear)));
        PresentViewController(alert, true, null);
    }

    private static bool TryGetUrl(string? value, out NSUrl? url)
    {
        url = string.IsNullOrWhiteSpace(value) ? null : NSUrl.FromString(value);
        return url is not null;
    }

    private void OpenFile(NSUrl url)
    {
        documentInteraction = UIDocumentInteractionController.FromUrl(url);
        if (!documentInteraction.PresentOpenInMenu(View!.Bounds, View, true))
        {
            var alert = UIAlertController.Create(
                AppStrings.Get("IosUiActionFailed"),
                AppStrings.Get("IosUiNoAppCanOpenFile"),
                UIAlertControllerStyle.Alert);
            alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiDismiss"), UIAlertActionStyle.Default, null));
            PresentViewController(alert, true, null);
        }
    }
}
