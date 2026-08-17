using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Settings;

/// <summary>Presents a native Files picker, nonmutating import preview, category selection, and explicit commit.</summary>
internal sealed class ImportViewController : UITableViewController
{
    private readonly SettingsImportService importer;
    private readonly NSUrl? initialUrl;
    private SettingsImportPreview? preview;
    private SettingsImportSelection selection = SettingsImportSelection.Empty;
    private CancellationTokenSource? activeParseCancellation;
    private long parseGeneration;
    private bool didLoadInitialUrl;
    private bool busy;
    private bool reviewingDuplicatesOnly;
    private bool reviewEnded;

    /// <summary>Creates a reviewed import flow.</summary>
    /// <param name="importer">The parse/preview/commit service.</param>
    /// <param name="initialUrl">An optional Files-opened document URL.</param>
    public ImportViewController(SettingsImportService importer, NSUrl? initialUrl = null)
        : base(UITableViewStyle.InsetGrouped)
    {
        this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
        this.initialUrl = initialUrl;
        Title = AppStrings.Get("IosUiImportTitle");
    }

    /// <summary>Raised once when this review flow is popped or its containing sheet is dismissed.</summary>
    public event EventHandler? ReviewEnded;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "settings.import.screen";
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 64;
        ShowInitialState();
    }

    /// <inheritdoc/>
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (!didLoadInitialUrl && initialUrl is not null)
        {
            didLoadInitialUrl = true;
            _ = ParseAsync(initialUrl);
        }
    }

    /// <inheritdoc/>
    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        if (preview is not null && !busy && !reviewingDuplicatesOnly)
        {
            RefreshReviewControls();
        }
    }

    /// <inheritdoc/>
    public override void ViewDidDisappear(bool animated)
    {
        base.ViewDidDisappear(animated);
        UINavigationController? navigation = NavigationController;
        bool ended = IsMovingFromParentViewController ||
                     IsBeingDismissed ||
                     navigation?.IsBeingDismissed == true ||
                     (navigation is not null &&
                      navigation.PresentingViewController is null &&
                      navigation.ViewIfLoaded?.Window is null);
        if (ended)
        {
            CancelActiveParse();
            NotifyReviewEnded();
        }
    }

    /// <summary>Hands a Files-opened document to the same nonmutating review flow used by the picker.</summary>
    /// <param name="url">The local document URI supplied by the application coordinator.</param>
    public void ReceiveDocument(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        didLoadInitialUrl = true;
        _ = ParseAsync(NSUrl.FromFilename(url.LocalPath));
    }

    /// <inheritdoc/>
    public override nint NumberOfSections(UITableView tableView) => preview is null ? 0 : 2;

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => section == 0 ? 1 : 4;

    /// <inheritdoc/>
    public override string? TitleForHeader(UITableView tableView, nint section) => section == 0
        ? AppStrings.Get("IosUiImportReview")
        : reviewingDuplicatesOnly
            ? AppStrings.Get("IosUiReviewExistingData")
            : AppStrings.Format(
                "IosUiSelectedItemCount",
                selection.Count,
                preview?.TotalSelectableItemCount ?? 0);

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        SettingsImportPreview model = preview!;
        if (indexPath.Section == 0)
        {
            var source = new UITableViewCell(UITableViewCellStyle.Default, null)
            {
                SelectionStyle = UITableViewCellSelectionStyle.None,
            };
            UIListContentConfiguration content = source.DefaultContentConfiguration;
            content.Text = model.SourceName;
            content.SecondaryText = AppStrings.Get("IosUiImportIntro");
            content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Headline);
            content.TextProperties.AdjustsFontForContentSizeCategory = true;
            content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
            content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
            content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
            content.SecondaryTextProperties.NumberOfLines = 0;
            source.ContentConfiguration = content;
            return source;
        }

        (SettingsImportCategory category, string title, int count, int present, string id) =
            DescribeCategory(model, indexPath.Row);
        int selectable = model.SelectableItemCount(category);
        int selected = selection.CountFor(category);
        SettingsImportSelectionState state = selection.State(category, model);
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = count > 0
                ? UITableViewCellSelectionStyle.Default
                : UITableViewCellSelectionStyle.None,
            AccessibilityIdentifier = $"settings.import.category.{id}",
        };
        UIListContentConfiguration categoryContent = cell.DefaultContentConfiguration;
        categoryContent.Text = title;
        categoryContent.SecondaryText = AppStrings.Format(
            "IosUiImportCategorySelectionCount",
            selected,
            selectable,
            present,
            count);
        categoryContent.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        categoryContent.TextProperties.AdjustsFontForContentSizeCategory = true;
        categoryContent.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        categoryContent.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        categoryContent.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        categoryContent.SecondaryTextProperties.NumberOfLines = 0;
        cell.ContentConfiguration = categoryContent;
        cell.IsAccessibilityElement = false;
        UIButton toggle = UIButton.FromType(UIButtonType.System);
        toggle.Enabled = selectable > 0 && !busy && !reviewingDuplicatesOnly;
        toggle.AccessibilityLabel = title;
        toggle.AccessibilityValue = AppStrings.Format(
            "IosUiImportItemSelectionAccessibilityValue",
            selected,
            selectable,
            SelectionStateText(state));
        toggle.AccessibilityHint = AppStrings.Get("IosUiImportCategorySelectionHint");
        toggle.AccessibilityIdentifier = $"settings.import.toggle.{id}";
        toggle.TranslatesAutoresizingMaskIntoConstraints = false;
        toggle.SetImage(UIImage.GetSystemImage(SelectionStateSymbol(state)), UIControlState.Normal);
        toggle.TouchUpInside += (_, _) =>
        {
            bool selectAll = state != SettingsImportSelectionState.All;
            selection = selection.SetAll(category, model.SelectableItemKeys(category), selectAll);
            RefreshReviewControls();
            AnnounceSelection();
        };
        NSLayoutConstraint.ActivateConstraints(
        [
            toggle.WidthAnchor.ConstraintEqualTo(44),
            toggle.HeightAnchor.ConstraintEqualTo(44),
        ]);
        UIButton details = UIButton.FromType(UIButtonType.System);
        details.AccessibilityLabel = $"{AppStrings.Get("IosUiImportReview")}: {title}";
        details.AccessibilityIdentifier = $"settings.import.details.{id}";
        details.Enabled = count > 0 && !busy;
        details.TranslatesAutoresizingMaskIntoConstraints = false;
        details.SetImage(UIImage.GetSystemImage("info.circle"), UIControlState.Normal);
        details.TouchUpInside += (_, _) => PresentCategoryDetails(category, title);
        NSLayoutConstraint.ActivateConstraints(
        [
            details.WidthAnchor.ConstraintEqualTo(44),
            details.HeightAnchor.ConstraintEqualTo(44),
        ]);
        cell.AccessoryView = new UIStackView([details, toggle])
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Center,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 4,
        };
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        if (indexPath.Section == 1)
        {
            var description = DescribeCategory(preview!, indexPath.Row);
            PresentCategoryDetails(description.Category, description.Title);
        }
    }

    /// <summary>Pushes a self-sizing, per-item selection list for one import category.</summary>
    /// <param name="category">The stable review category.</param>
    /// <param name="title">The localized navigation title.</param>
    private void PresentCategoryDetails(SettingsImportCategory category, string title)
    {
        if (preview is null)
        {
            return;
        }

        IReadOnlyList<string> items = preview.ItemKeys(category);
        if (items.Count == 0)
        {
            return;
        }

        NavigationController?.PushViewController(
            new ImportCategoryDetailsViewController(
                title,
                items,
                items.Where(item => selection.Contains(category, item)),
                items.Where(item => preview.IsAlreadyPresent(category, item)),
                reviewingDuplicatesOnly,
                (item, selected) => selection = selection.Set(category, item, selected)),
            true);
    }

    /// <summary>Shows the nonblocking first-use state and its clear primary action.</summary>
    private void ShowInitialState()
    {
        preview = null;
        selection = SettingsImportSelection.Empty;
        reviewingDuplicatesOnly = false;
        NavigationItem.RightBarButtonItems = [];
        TableView.ReloadData();
        ContentStateView.Show(
            this,
            new ContentStatePresentation(
                AppStrings.Get("IosUiImportTitle"),
                AppStrings.Get("IosUiImportIntro"),
                "square.and.arrow.down",
                AppStrings.Get("IosUiChooseFile"),
                PresentPicker));
    }

    /// <summary>Presents the system Files picker and retains its delegate through parsing.</summary>
    private void PresentPicker()
    {
        (CancellationTokenSource cancellation, CancellationToken token, long generation) = BeginParse();
        ContentStateView.Show(
            this,
            new ContentStatePresentation(
                AppStrings.Get("IosUiParsingImport"),
                AppStrings.Get("IosUiImportIntro"),
                IsLoading: true));
        UIDocumentPickerViewController picker = importer.CreatePreviewDocumentPicker(
            (result, error) =>
            {
                if (!CompleteParse(cancellation, generation))
                {
                    return;
                }

                if (error is not null)
                {
                    ShowError();
                    return;
                }

                if (result is not null)
                {
                    ApplyPreview(result);
                    return;
                }

                ShowInitialState();
            },
            token);
        PresentViewController(picker, true, null);
    }

    /// <summary>Parses a Files-opened document through the same review path as the picker.</summary>
    /// <param name="url">The security-scoped or local document URL.</param>
    private async Task ParseAsync(NSUrl url)
    {
        (CancellationTokenSource cancellation, CancellationToken token, long generation) = BeginParse();
        ContentStateView.Show(
            this,
            new ContentStatePresentation(
                AppStrings.Get("IosUiParsingImport"),
                AppStrings.Get("IosUiImportIntro"),
                IsLoading: true));
        try
        {
            SettingsImportPreview result = await importer.ParsePreviewAsync(url, token);
            if (!CompleteParse(cancellation, generation))
            {
                return;
            }

            ApplyPreview(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = CompleteParse(cancellation, generation);
        }
        catch
        {
            if (CompleteParse(cancellation, generation))
            {
                ShowError();
            }
        }
    }

    /// <summary>Shows a parsed preview without mutating application state.</summary>
    /// <param name="result">The normalized review model.</param>
    private void ApplyPreview(SettingsImportPreview result)
    {
        preview = result;
        reviewingDuplicatesOnly = false;
        selection = SettingsImportSelection.Selectable(result);
        ContentStateView.Clear(this);
        if (!result.HasContent)
        {
            ContentStateView.Show(
                this,
                new ContentStatePresentation(
                    AppStrings.Get("NothingToImport"),
                    AppStrings.Get("IosUiImportIntro"),
                    "tray",
                    AppStrings.Get("IosUiChooseFile"),
                    PresentPicker));
            return;
        }

        if (IsEntirePreviewAlreadyPresent(result))
        {
            reviewingDuplicatesOnly = true;
            selection = SettingsImportSelection.Empty;
            NavigationItem.RightBarButtonItems =
            [
                new UIBarButtonItem(
                    AppStrings.Get("IosUiReviewExistingData"),
                    UIBarButtonItemStyle.Plain,
                    (_, _) => ShowDuplicateReview())
                {
                    AccessibilityIdentifier = "settings.import.review-existing",
                },
            ];
            ContentStateView.Show(
                this,
                new ContentStatePresentation(
                    AppStrings.Get("NothingNewToImport"),
                    AppStrings.Get("IosUiImportNothingNewDetail"),
                    "checkmark.circle",
                    AppStrings.Get("IosUiChooseAnotherFile"),
                    PresentPicker));
            AccessibilityExtensions.Announce(
                $"{AppStrings.Get("NothingNewToImport")}. {AppStrings.Get("IosUiImportNothingNewDetail")}");
            return;
        }

        RefreshReviewControls();
        AccessibilityExtensions.FocusScreen(TableView);
    }

    /// <summary>Gets whether every supported entry in a nonempty preview already exists locally.</summary>
    /// <param name="model">The detached import preview.</param>
    /// <returns><see langword="true"/> when the import would add or update no entry.</returns>
    private static bool IsEntirePreviewAlreadyPresent(SettingsImportPreview model) =>
        model.HasContent &&
        model.FriendCount == model.FriendsAlreadyPresent &&
        model.IgnoredCount == model.IgnoredAlreadyPresent &&
        model.WishlistCount == model.WishlistAlreadyPresent &&
        model.UserNoteCount == model.UserNotesAlreadyPresent;

    /// <summary>Leaves the distinct nothing-new outcome and exposes its read-only category drill-in.</summary>
    private void ShowDuplicateReview()
    {
        ContentStateView.Clear(this);
        TableView.ReloadData();
        var chooseAnother = new UIBarButtonItem(
            AppStrings.Get("IosUiChooseAnotherFile"),
            UIBarButtonItemStyle.Plain,
            (_, _) => PresentPicker())
        {
            AccessibilityIdentifier = "settings.import.choose-another",
        };
        NavigationItem.RightBarButtonItems = [chooseAnother];
        AccessibilityExtensions.FocusScreen(TableView);
    }

    /// <summary>Refreshes category rows, select-all state, and explicit merge action.</summary>
    private void RefreshReviewControls()
    {
        if (preview is null)
        {
            return;
        }

        TableView.ReloadData();
        SettingsImportSelectionState state = selection.State(null, preview);
        string selectTitle = state switch
        {
            SettingsImportSelectionState.All => AppStrings.Get("IosUiDeselectAll"),
            SettingsImportSelectionState.Some => AppStrings.Get("IosUiSelectAllMixed"),
            _ => AppStrings.Get("IosUiSelectAll"),
        };
        var select = new UIBarButtonItem(selectTitle, UIBarButtonItemStyle.Plain, (_, _) => ToggleAll())
        {
            AccessibilityIdentifier = "settings.import.select-all",
            AccessibilityLabel = AppStrings.Get("IosUiSelectAllSelectionLabel"),
            AccessibilityValue = AppStrings.Format(
                "IosUiImportItemSelectionAccessibilityValue",
                selection.Count,
                preview.TotalSelectableItemCount,
                SelectionStateText(state)),
        };
        var merge = new UIBarButtonItem(
            AppStrings.Format("IosUiMergeSelectedCount", selection.Count),
            UIBarButtonItemStyle.Done,
            (_, _) => _ = CommitAsync())
        {
            Enabled = selection.Any && !busy,
            AccessibilityIdentifier = "settings.import.commit",
        };
        NavigationItem.RightBarButtonItems = [merge, select];
    }

    /// <summary>Selects every non-empty category or clears all when every category is already selected.</summary>
    private void ToggleAll()
    {
        SettingsImportPreview model = preview!;
        bool enabled = selection.State(null, model) != SettingsImportSelectionState.All;
        selection = enabled
            ? SettingsImportSelection.Selectable(model)
            : SettingsImportSelection.Empty;
        RefreshReviewControls();
        AnnounceSelection();
    }

    /// <summary>Commits only reviewed categories and presents an explicit completion summary.</summary>
    private async Task CommitAsync()
    {
        if (preview is null || !selection.Any || busy)
        {
            return;
        }

        busy = true;
        NavigationItem.RightBarButtonItems = [];
        ContentStateView.Show(
            this,
            new ContentStatePresentation(
                AppStrings.Get("IosUiImporting"),
                AppStrings.Get("IosUiImportIntro"),
                IsLoading: true));
        try
        {
            SettingsImportResult result = await importer.CommitSelectedAsync(preview, selection);
            string detail = AppStrings.Format(
                "IosUiImportCompleteDetail",
                result.FriendCount,
                result.IgnoredCount,
                result.WishlistCount,
                result.UserNoteCount);
            if (result.WatchReconciliationFailureCount > 0)
            {
                detail += "\n" + AppStrings.Format(
                    "IosUiImportWatchWarning",
                    result.WatchReconciliationFailureCount);
            }

            ContentStateView.Show(
                this,
                new ContentStatePresentation(
                    AppStrings.Get("IosUiImportComplete"),
                    detail,
                    "checkmark.circle",
                    AppStrings.Get("IosUiDone"),
                    CompleteFlow));
            AccessibilityExtensions.Announce($"{AppStrings.Get("IosUiImportComplete")}. {detail}");
        }
        catch
        {
            ShowError();
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Returns to settings when pushed, or dismisses the modal review flow when it is navigation root.</summary>
    private void CompleteFlow()
    {
        if (NavigationController is { ViewControllers.Length: > 1 } navigation)
        {
            navigation.PopViewController(true);
            return;
        }

        UIViewController presenter = NavigationController is { } rootNavigation ? rootNavigation : this;
        presenter.DismissViewController(true, null);
    }

    /// <summary>Shows a recoverable import failure with a direct retry path.</summary>
    private void ShowError()
    {
        preview = null;
        reviewingDuplicatesOnly = false;
        NavigationItem.RightBarButtonItems = [];
        TableView.ReloadData();
        ContentStateView.Show(
            this,
            new ContentStatePresentation(
                AppStrings.Get("IosUiImportFailed"),
                AppStrings.Get("FailedToParseContactDev"),
                "exclamationmark.triangle",
                AppStrings.Get("IosUiChooseFile"),
                PresentPicker));
        AccessibilityExtensions.Announce(AppStrings.Get("IosUiImportFailed"));
    }

    /// <summary>Announces the exact global per-item selection count and tri-state value.</summary>
    private void AnnounceSelection()
    {
        if (preview is null)
        {
            return;
        }

        AccessibilityExtensions.Announce(
            AppStrings.Format(
                "IosUiImportItemSelectionAccessibilityValue",
                selection.Count,
                preview.TotalSelectableItemCount,
                SelectionStateText(selection.State(null, preview))));
    }

    /// <summary>Starts a controller-owned parse generation after canceling any superseded operation.</summary>
    /// <returns>The source, token, and generation used to reject stale completions.</returns>
    private (CancellationTokenSource Cancellation, CancellationToken Token, long Generation) BeginParse()
    {
        CancelActiveParse();
        var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        activeParseCancellation = cancellation;
        busy = true;
        return (cancellation, token, ++parseGeneration);
    }

    /// <summary>Completes the current parse only when it still owns the active generation.</summary>
    /// <param name="cancellation">The source captured when parsing started.</param>
    /// <param name="generation">The captured generation.</param>
    /// <returns><see langword="true"/> when the completion is current and may update UIKit.</returns>
    private bool CompleteParse(CancellationTokenSource cancellation, long generation)
    {
        if (!ReferenceEquals(activeParseCancellation, cancellation) || generation != parseGeneration)
        {
            return false;
        }

        activeParseCancellation = null;
        busy = false;
        cancellation.Dispose();
        return true;
    }

    /// <summary>Cancels and invalidates parsing owned by this review without interrupting an atomic commit.</summary>
    private void CancelActiveParse()
    {
        CancellationTokenSource? cancellation = activeParseCancellation;
        activeParseCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        busy = false;
        parseGeneration++;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>Notifies coordinator ownership once after the import flow leaves the hierarchy.</summary>
    private void NotifyReviewEnded()
    {
        if (reviewEnded)
        {
            return;
        }

        reviewEnded = true;
        ReviewEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps a stable category row to its localized copy, counts, and automation identity.</summary>
    private static (
        SettingsImportCategory Category,
        string Title,
        int Count,
        int Present,
        string Id) DescribeCategory(SettingsImportPreview model, nint row) => row switch
        {
            0 => (
                SettingsImportCategory.Friends,
                AppStrings.Get("ImportFriends"),
                model.FriendCount,
                model.FriendsAlreadyPresent,
                "friends"),
            1 => (
                SettingsImportCategory.IgnoredUsers,
                AppStrings.Get("ImportIgnored"),
                model.IgnoredCount,
                model.IgnoredAlreadyPresent,
                "ignored"),
            2 => (
                SettingsImportCategory.Wishlist,
                AppStrings.Get("ImportWishlist"),
                model.WishlistCount,
                model.WishlistAlreadyPresent,
                "wishlist"),
            _ => (
                SettingsImportCategory.UserNotes,
                AppStrings.Get("ImportUserNotes"),
                model.UserNoteCount,
                model.UserNotesAlreadyPresent,
                "notes"),
        };

    /// <summary>Maps a non-color selection state to a localized visible and spoken value.</summary>
    private static string SelectionStateText(SettingsImportSelectionState state) => AppStrings.Get(state switch
    {
        SettingsImportSelectionState.None => "IosUiSelectionNone",
        SettingsImportSelectionState.Some => "IosUiSelectionSome",
        SettingsImportSelectionState.All => "IosUiSelectionAll",
        _ => "IosUiSelectionNone",
    });

    /// <summary>Maps a selection state to a distinct SF Symbol so state never depends on color.</summary>
    private static string SelectionStateSymbol(SettingsImportSelectionState state) => state switch
    {
        SettingsImportSelectionState.None => "circle",
        SettingsImportSelectionState.Some => "minus.circle.fill",
        SettingsImportSelectionState.All => "checkmark.circle.fill",
        _ => "circle",
    };

    /// <summary>Presents normalized import items with per-item choice; note rows expose usernames, never private text.</summary>
    private sealed class ImportCategoryDetailsViewController : UITableViewController
    {
        private readonly IReadOnlyList<string> items;
        private readonly HashSet<string> selected;
        private readonly HashSet<string> alreadyPresent;
        private readonly bool readOnly;
        private readonly Action<string, bool> selectionChanged;

        /// <summary>Creates one category detail list.</summary>
        /// <param name="title">The localized category title.</param>
        /// <param name="items">The normalized, non-sensitive visible values.</param>
        /// <param name="selected">The initially selected item keys.</param>
        /// <param name="alreadyPresent">The explicit duplicate item keys that cannot add new state.</param>
        /// <param name="readOnly">Whether the list is an all-duplicate review.</param>
        /// <param name="selectionChanged">Updates the parent review's detached item selection.</param>
        public ImportCategoryDetailsViewController(
            string title,
            IReadOnlyList<string> items,
            IEnumerable<string> selected,
            IEnumerable<string> alreadyPresent,
            bool readOnly,
            Action<string, bool> selectionChanged)
            : base(UITableViewStyle.InsetGrouped)
        {
            Title = title;
            this.items = items ?? throw new ArgumentNullException(nameof(items));
            this.selected = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
            this.alreadyPresent = alreadyPresent.ToHashSet(StringComparer.OrdinalIgnoreCase);
            this.readOnly = readOnly;
            this.selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
        }

        /// <inheritdoc/>
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.SystemGroupedBackground;
            View.AccessibilityIdentifier = "settings.import.category-details";
            TableView.RowHeight = UITableView.AutomaticDimension;
            TableView.EstimatedRowHeight = 52;
            RefreshSelectionControl();
        }

        /// <inheritdoc/>
        public override nint RowsInSection(UITableView tableView, nint section) => items.Count;

        /// <inheritdoc/>
        public override string? TitleForHeader(UITableView tableView, nint section) => readOnly
            ? AppStrings.Get("IosUiReviewExistingData")
            : AppStrings.Format("IosUiSelectedItemCount", selected.Count, SelectableCount);

        /// <inheritdoc/>
        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
        {
            string item = items[indexPath.Row];
            bool duplicate = alreadyPresent.Contains(item);
            bool isSelected = selected.Contains(item);
            var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
            {
                SelectionStyle = readOnly || duplicate
                    ? UITableViewCellSelectionStyle.None
                    : UITableViewCellSelectionStyle.Default,
                AccessibilityIdentifier =
                    $"settings.import.category-item.{AccessibilityIdentifiers.Opaque(item)}",
            };
            UIListContentConfiguration content = cell.DefaultContentConfiguration;
            content.Text = item;
            content.SecondaryText = duplicate ? AppStrings.Get("IosUiAlreadyPresent") : null;
            content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
            content.TextProperties.AdjustsFontForContentSizeCategory = true;
            content.TextProperties.NumberOfLines = 0;
            content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
            content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
            content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
            cell.ContentConfiguration = content;
            cell.Accessory = isSelected || duplicate
                ? UITableViewCellAccessory.Checkmark
                : UITableViewCellAccessory.None;
            cell.AccessibilityValue = duplicate
                ? AppStrings.Get("IosUiAlreadyPresent")
                : SelectionStateText(isSelected
                    ? SettingsImportSelectionState.All
                    : SettingsImportSelectionState.None);
            if (isSelected)
            {
                cell.AccessibilityTraits |= UIAccessibilityTrait.Selected;
            }

            return cell;
        }

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            string item = items[indexPath.Row];
            if (readOnly || alreadyPresent.Contains(item))
            {
                return;
            }

            bool value = !selected.Contains(item);
            if (value)
            {
                selected.Add(item);
            }
            else
            {
                selected.Remove(item);
            }

            selectionChanged(item, value);
            tableView.ReloadRows([indexPath], UITableViewRowAnimation.Automatic);
            RefreshSelectionControl();
        }

        private int SelectableCount => items.Count - alreadyPresent.Count;

        /// <summary>Refreshes the category-level Select All control with an explicit mixed state and count.</summary>
        private void RefreshSelectionControl()
        {
            if (readOnly || SelectableCount == 0)
            {
                NavigationItem.RightBarButtonItems = [];
                TableView.ReloadData();
                return;
            }

            SettingsImportSelectionState state = CurrentState;
            var selectAll = new UIBarButtonItem(
                state switch
                {
                    SettingsImportSelectionState.All => AppStrings.Get("IosUiDeselectAll"),
                    SettingsImportSelectionState.Some => AppStrings.Get("IosUiSelectAllMixed"),
                    _ => AppStrings.Get("IosUiSelectAll"),
                },
                UIBarButtonItemStyle.Plain,
                (_, _) => ToggleAll())
            {
                AccessibilityIdentifier = "settings.import.category.select-all",
                AccessibilityLabel = AppStrings.Get("IosUiSelectAllSelectionLabel"),
                AccessibilityValue = AppStrings.Format(
                    "IosUiImportItemSelectionAccessibilityValue",
                    selected.Count,
                    SelectableCount,
                    SelectionStateText(state)),
            };
            NavigationItem.RightBarButtonItems = [selectAll];
            TableView.ReloadData();
        }

        /// <summary>Selects all nonduplicate rows, or clears them when already fully selected.</summary>
        private void ToggleAll()
        {
            bool value = CurrentState != SettingsImportSelectionState.All;
            foreach (string item in items.Where(item => !alreadyPresent.Contains(item)))
            {
                bool changed = value ? selected.Add(item) : selected.Remove(item);
                if (changed)
                {
                    selectionChanged(item, value);
                }
            }

            RefreshSelectionControl();
            AccessibilityExtensions.Announce(
                AppStrings.Format(
                    "IosUiImportItemSelectionAccessibilityValue",
                    selected.Count,
                    SelectableCount,
                    SelectionStateText(CurrentState)));
        }

        private SettingsImportSelectionState CurrentState => selected.Count switch
        {
            0 => SettingsImportSelectionState.None,
            _ when selected.Count >= SelectableCount => SettingsImportSelectionState.All,
            _ => SettingsImportSelectionState.Some,
        };
    }
}
