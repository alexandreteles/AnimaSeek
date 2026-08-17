using System.Collections.Immutable;
using AnimaSeek.iOS.UI.Accessibility;
using CoreGraphics;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Defines one self-sizing native list item with stable identity and optional determinate progress.</summary>
/// <param name="Id">Stable, non-index identity used by the diffable data source.</param>
/// <param name="Title">Primary visible text.</param>
/// <param name="Subtitle">Secondary visible state and metadata.</param>
/// <param name="SymbolName">A reinforcing SF Symbol name.</param>
/// <param name="ShowsDisclosure">Whether selection navigates or expands.</param>
/// <param name="Indentation">Native list indentation level.</param>
/// <param name="AccessibilityLabel">A complete reading-order label.</param>
/// <param name="Progress">Normalized determinate progress, or null when progress is not applicable.</param>
/// <param name="MonospacedDigits">Whether subtitle digits keep one fixed width so in-place updates do not jitter.</param>
internal sealed record FeatureListItem(
    string Id,
    string Title,
    string? Subtitle = null,
    string? SymbolName = null,
    bool ShowsDisclosure = false,
    nint Indentation = 0,
    string? AccessibilityLabel = null,
    double? Progress = null,
    bool MonospacedDigits = false);

/// <summary>
/// Hosts a compositional UIKit list and applies stable, reconfigurable diffable snapshots for high-volume features.
/// </summary>
internal sealed class DiffableFeatureListView : UIView
{
    private static readonly NSString SectionIdentifier = new("content");
    private const string ReuseIdentifier = "FeatureListCell";
    private const int ProgressAccessoryWidth = 64;
    private const int ProgressAccessoryHeight = 4;
    private readonly UICollectionView collectionView;
    private readonly UICollectionViewDiffableDataSource<NSString, NSString> dataSource;
    private readonly FeatureListDelegate collectionDelegate;
    private IReadOnlyDictionary<string, FeatureListItem> items = new Dictionary<string, FeatureListItem>();
    private readonly HashSet<string> selectedItemIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> validSelectionIds = new(StringComparer.Ordinal);
    private bool selectionMode;

    /// <summary>Creates a plain or inset-grouped native list.</summary>
    /// <param name="appearance">The standard UIKit list appearance.</param>
    public DiffableFeatureListView(UICollectionLayoutListAppearance appearance = UICollectionLayoutListAppearance.Plain)
    {
        var configuration = new UICollectionLayoutListConfiguration(appearance)
        {
            ShowsSeparators = true,
        };
        collectionView = new UICollectionView(
            CGRect.Empty,
            UICollectionViewCompositionalLayout.GetLayout(configuration))
        {
            BackgroundColor = UIColor.SystemBackground,
            AlwaysBounceVertical = true,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        collectionView.RegisterClassForCell(typeof(UICollectionViewListCell), ReuseIdentifier);
        collectionDelegate = new FeatureListDelegate(
            () => selectionMode,
            id => ItemSelected?.Invoke(this, id),
            (id, selected) => UpdateSelection(id, selected));
        collectionView.Delegate = collectionDelegate;
        dataSource = new UICollectionViewDiffableDataSource<NSString, NSString>(
            collectionView,
            ConfigureCell);
        TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(collectionView);
        NSLayoutConstraint.ActivateConstraints([
            collectionView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            collectionView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            collectionView.TopAnchor.ConstraintEqualTo(TopAnchor),
            collectionView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        ]);
    }

    /// <summary>Raised with a stable item identifier after native primary selection.</summary>
    public event EventHandler<string>? ItemSelected;

    /// <summary>Raised after native selection or deselection changes the stable edit-mode selection.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Gets the underlying scroll view for refresh control, keyboard dismissal, and scroll restoration.</summary>
    public UICollectionView CollectionView => collectionView;

    /// <summary>Gets or sets whether rows use native multi-select accessories instead of primary navigation.</summary>
    public bool SelectionMode
    {
        get => selectionMode;
        set
        {
            if (selectionMode == value)
            {
                return;
            }

            selectionMode = value;
            collectionView.AllowsMultipleSelection = value;
            collectionView.AllowsMultipleSelectionDuringEditing = value;
            if (!value)
            {
                SetSelectedItems([]);
            }

            ReconfigureVisibleItems();
        }
    }

    /// <summary>Gets the stable identifiers selected in the current edit session.</summary>
    public IReadOnlySet<string> SelectedItemIds => selectedItemIds;

    /// <summary>Applies a stable snapshot and reconfigures retained identifiers without resetting scroll position.</summary>
    /// <param name="nextItems">Items in their requested visual order.</param>
    /// <param name="animated">Whether UIKit should animate structural differences.</param>
    /// <param name="selectionUniverse">
    /// Optional valid IDs outside the visible snapshot, allowing selection to survive a local text filter.
    /// Omit this when every selectable row is visible.
    /// </param>
    public void Apply(
        IReadOnlyList<FeatureListItem> nextItems,
        bool animated = true,
        IReadOnlyCollection<string>? selectionUniverse = null)
    {
        ArgumentNullException.ThrowIfNull(nextItems);
        items = nextItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        validSelectionIds.Clear();
        validSelectionIds.UnionWith(selectionUniverse ?? items.Keys);
        selectedItemIds.IntersectWith(validSelectionIds);
        ImmutableHashSet<string> nextIdentifierIds = items.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        NSString[] identifiers = nextItems.Select(item => new NSString(item.Id)).ToArray();
        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSString>();
        snapshot.AppendSections([SectionIdentifier]);
        snapshot.AppendItems(identifiers, SectionIdentifier);
        NSString[] retained = dataSource.Snapshot.ItemIdentifiers
            .Where(identifier => nextIdentifierIds.Contains(identifier.ToString()))
            .ToArray();
        if (retained.Length > 0)
        {
            snapshot.ReconfigureItems(retained);
        }

        dataSource.ApplySnapshot(snapshot, animated, RestoreNativeSelection);
    }

    /// <summary>Replaces native selection with the supplied stable identifiers that still exist in the snapshot.</summary>
    /// <param name="identifiers">Stable item identifiers to select.</param>
    public void SetSelectedItems(IEnumerable<string> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        selectedItemIds.Clear();
        selectedItemIds.UnionWith(identifiers.Where(validSelectionIds.Contains));
        RestoreNativeSelection();
        ReconfigureVisibleItems();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inverts selection only within an explicit visible or unfiltered scope.</summary>
    /// <param name="scopeIdentifiers">The stable identifiers whose selected state should be inverted.</param>
    public void InvertSelection(IEnumerable<string> scopeIdentifiers)
    {
        ArgumentNullException.ThrowIfNull(scopeIdentifiers);
        foreach (string identifier in scopeIdentifiers.Where(validSelectionIds.Contains).Distinct(StringComparer.Ordinal))
        {
            if (!selectedItemIds.Remove(identifier))
            {
                selectedItemIds.Add(identifier);
            }
        }

        RestoreNativeSelection();
        ReconfigureVisibleItems();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the item at a current index path, or null after a concurrent snapshot change.</summary>
    /// <param name="indexPath">The current collection index path.</param>
    public FeatureListItem? GetItem(NSIndexPath indexPath)
    {
        NSString? identifier = dataSource.GetItemIdentifier(indexPath);
        return identifier is null ? null : items.GetValueOrDefault(identifier.ToString());
    }

    /// <summary>Configures one reusable cell from the current immutable presentation item.</summary>
    /// <param name="owner">The collection view requesting the cell.</param>
    /// <param name="indexPath">The current visual position.</param>
    /// <param name="identifierObject">The stable diffable item identifier.</param>
    /// <returns>A self-sizing, accessible native list cell.</returns>
    private UICollectionViewCell ConfigureCell(
        UICollectionView owner,
        NSIndexPath indexPath,
        NSObject identifierObject)
    {
        var identifier = (NSString)identifierObject;
        var cell = (UICollectionViewListCell)owner.DequeueReusableCell(ReuseIdentifier, indexPath);
        if (!items.TryGetValue(identifier.ToString(), out FeatureListItem? item))
        {
            return cell;
        }

        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = item.Title;
        content.SecondaryText = item.Subtitle;
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        content.SecondaryTextProperties.Font = item.MonospacedDigits
            ? UIKitFactory.PreferredMonospacedDigitFont(UIFontTextStyle.Subheadline)
            : UIKitFactory.PreferredFont(UIFontTextStyle.Subheadline);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.NumberOfLines = 0;
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.Image = string.IsNullOrWhiteSpace(item.SymbolName)
            ? null
            : UIImage.GetSystemImage(item.SymbolName);
        cell.ContentConfiguration = content;
        cell.IndentationLevel = item.Indentation;
        var accessories = new List<UICellAccessory>();
        if (item.Progress is { } progress)
        {
            // UICellAccessoryCustomView asserts that the custom view still translates its autoresizing
            // mask, so the accessory's width comes from the frame rather than a self-imposed constraint.
            var progressView = new UIProgressView(UIProgressViewStyle.Default)
            {
                Progress = (float)Math.Clamp(progress, 0, 1),
                IsAccessibilityElement = false,
            };
            // A progress view has no intrinsic width, so the accessory takes its size from this frame.
            progressView.Frame = new CGRect(0, 0, ProgressAccessoryWidth, ProgressAccessoryHeight);
            accessories.Add(new UICellAccessoryCustomView(progressView, UICellAccessoryPlacement.Trailing)
            {
                MaintainsFixedSize = true,
                ReservedLayoutWidth = ProgressAccessoryWidth,
            });
        }

        if (selectionMode)
        {
            accessories.Insert(0, new UICellAccessoryMultiselect());
        }
        else if (item.ShowsDisclosure)
        {
            accessories.Add(new UICellAccessoryDisclosureIndicator());
        }

        cell.Accessories = [.. accessories];
        cell.IsAccessibilityElement = true;
        cell.AccessibilityIdentifier = $"feature.item.{AccessibilityIdentifiers.Opaque(item.Id)}";
        cell.AccessibilityLabel = item.AccessibilityLabel ?? string.Join(
            ", ",
            new[] { item.Title, item.Subtitle }.Where(value => !string.IsNullOrWhiteSpace(value)));
        cell.AccessibilityValue = item.Progress is { } value
            ? AppStrings.Format("IosUiPercentComplete", Math.Round(Math.Clamp(value, 0, 1) * 100))
            : null;
        cell.AccessibilityTraits = selectionMode && selectedItemIds.Contains(item.Id)
            ? UIAccessibilityTrait.Button | UIAccessibilityTrait.Selected
            : item.ShowsDisclosure || selectionMode
                ? UIAccessibilityTrait.Button
                : UIAccessibilityTrait.None;
        return cell;
    }

    /// <summary>Updates edit-mode selection by stable identity and refreshes only the affected row.</summary>
    /// <param name="identifier">The stable identifier whose selection changed.</param>
    /// <param name="selected">Whether the row is now selected.</param>
    private void UpdateSelection(string identifier, bool selected)
    {
        if (selected)
        {
            selectedItemIds.Add(identifier);
        }
        else
        {
            selectedItemIds.Remove(identifier);
        }

        ReconfigureItems([identifier]);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Projects retained stable selection identifiers back onto the current native snapshot.</summary>
    private void RestoreNativeSelection()
    {
        foreach (NSIndexPath indexPath in collectionView.GetIndexPathsForSelectedItems() ?? [])
        {
            collectionView.DeselectItem(indexPath, false);
        }

        if (!selectionMode)
        {
            return;
        }

        foreach (string identifier in selectedItemIds)
        {
            NSIndexPath? indexPath = dataSource.GetIndexPath(new NSString(identifier));
            if (indexPath is not null)
            {
                collectionView.SelectItem(indexPath, false, UICollectionViewScrollPosition.None);
            }
        }
    }

    /// <summary>Reconfigures every item currently represented by the presentation dictionary.</summary>
    private void ReconfigureVisibleItems() => ReconfigureItems(items.Keys);

    /// <summary>Reconfigures requested stable identifiers that still exist in the current snapshot.</summary>
    /// <param name="identifiers">Stable identifiers that may need visible-state updates.</param>
    private void ReconfigureItems(IEnumerable<string> identifiers)
    {
        ImmutableHashSet<string> current = dataSource.Snapshot.ItemIdentifiers
            .Select(identifier => identifier.ToString())
            .ToImmutableHashSet(StringComparer.Ordinal);
        NSString[] requested = identifiers
            .Distinct(StringComparer.Ordinal)
            .Where(current.Contains)
            .Select(identifier => new NSString(identifier))
            .ToArray();
        if (requested.Length == 0)
        {
            return;
        }

        NSDiffableDataSourceSnapshot<NSString, NSString> snapshot = dataSource.Snapshot;
        snapshot.ReconfigureItems(requested);
        dataSource.ApplySnapshot(snapshot, false, RestoreNativeSelection);
    }

    private sealed class FeatureListDelegate(
        Func<bool> selectionMode,
        Action<string> activated,
        Action<string, bool> selectionChanged) : UICollectionViewDelegate
    {
        /// <inheritdoc/>
        public override void ItemSelected(UICollectionView collectionView, NSIndexPath indexPath)
        {
            if (collectionView.DataSource is UICollectionViewDiffableDataSource<NSString, NSString> source &&
                source.GetItemIdentifier(indexPath) is { } identifier)
            {
                if (selectionMode())
                {
                    selectionChanged(identifier.ToString(), true);
                }
                else
                {
                    collectionView.DeselectItem(indexPath, true);
                    activated(identifier.ToString());
                }
            }
        }

        /// <inheritdoc/>
        public override void ItemDeselected(UICollectionView collectionView, NSIndexPath indexPath)
        {
            if (selectionMode() &&
                collectionView.DataSource is UICollectionViewDiffableDataSource<NSString, NSString> source &&
                source.GetItemIdentifier(indexPath) is { } identifier)
            {
                selectionChanged(identifier.ToString(), false);
            }
        }
    }
}
