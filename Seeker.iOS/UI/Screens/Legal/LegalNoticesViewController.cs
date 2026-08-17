using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Legal;

/// <summary>Provides permanent access to the required modified-library notice and every bundled license.</summary>
internal sealed class LegalNoticesViewController : UITableViewController
{
    private const string CellIdentifier = "legal.notice";
    private readonly IReadOnlyList<LegalNoticeItem> items;

    /// <summary>Creates the permanent Legal Notices destination from distribution bundle resources.</summary>
    public LegalNoticesViewController() : base(UITableViewStyle.InsetGrouped)
    {
        Title = AppStrings.Get("IosUiLegal");
        items = BuildItems();
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = AccessibilityIdentifiers.LegalScreen;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        TableView.RegisterClassForCellReuse(typeof(UITableViewCell), CellIdentifier);
        TableView.RowHeight = UITableView.AutomaticDimension;
        TableView.EstimatedRowHeight = 64;
    }

    /// <inheritdoc/>
    public override nint RowsInSection(UITableView tableView, nint section) => items.Count;

    /// <inheritdoc/>
    public override nint NumberOfSections(UITableView tableView) => 1;

    /// <inheritdoc/>
    public override string? TitleForHeader(UITableView tableView, nint section) =>
        AppStrings.Get("IosUiNoticesAndLicenses");

    /// <inheritdoc/>
    public override string? TitleForFooter(UITableView tableView, nint section) =>
        AppStrings.Get("IosUiLegalFooter");

    /// <inheritdoc/>
    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        UITableViewCell cell = tableView.DequeueReusableCell(CellIdentifier, indexPath);
        LegalNoticeItem item = items[indexPath.Row];
        UIListContentConfiguration content = UIListContentConfiguration.SubtitleCellConfiguration;
        content.Text = item.Title;
        content.SecondaryText = item.Summary;
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Footnote);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        cell.ContentConfiguration = content;
        cell.Accessory = UITableViewCellAccessory.DisclosureIndicator;
        cell.AccessibilityIdentifier = item.IsModifiedLibraryNotice
            ? AccessibilityIdentifiers.LegalLibraryNotice
            : AccessibilityIdentifiers.LegalDocument(item.ResourceName);
        cell.AccessibilityLabel = $"{item.Title}. {item.Summary}";
        cell.AccessibilityTraits |= UIAccessibilityTrait.Button;
        return cell;
    }

    /// <inheritdoc/>
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, animated: true);
        LegalNoticeItem item = items[indexPath.Row];
        NavigationController?.PushViewController(
            new ReadableTextViewController(
                item.Title,
                item.Body,
                item.IsModifiedLibraryNotice
                    ? AccessibilityIdentifiers.LegalLibraryNotice
                    : AccessibilityIdentifiers.LegalDocument(item.ResourceName)),
            animated: true);
    }

    /// <summary>Builds the permanent notice plus every available distribution license.</summary>
    /// <returns>Legal items in stable, user-readable order.</returns>
    private static IReadOnlyList<LegalNoticeItem> BuildItems()
    {
        var modifiedNotice = new LegalNoticeItem(
            AppStrings.Get("IosUiModifiedLibraryNoticeTitle"),
            AppStrings.Get("IosUiModifiedLibraryNoticeSummary"),
            Seeker.SoulseekClientIdentity.ModifiedLibraryNotice,
            "modified-library-notice",
            IsModifiedLibraryNotice: true);
        LegalNoticeItem[] bundleItems =
        [
            ReadResource("IosUiLegalSoulseekLicense", "LICENSE"),
            ReadResource("IosUiLegalSoulseekNotice", "NOTICE"),
            ReadResource("IosUiLegalThirdParty", "THIRD-PARTY-NOTICES"),
            ReadResource("IosUiLegalDotNetLicense", "DOTNET-RUNTIME-10.0.10-LICENSE"),
            ReadResource("IosUiLegalDotNetNotices", "DOTNET-RUNTIME-10.0.10-NOTICES"),
            ReadResource("IosUiLegalMicrosoftIos", "MICROSOFT-IOS-26.5.10315-LICENSE"),
            ReadResource("IosUiLegalStringToolsLicense", "MICROSOFT-NET-STRINGTOOLS-17.11.4-LICENSE"),
            ReadResource("IosUiLegalStringToolsNotices", "MICROSOFT-NET-STRINGTOOLS-17.11.4-NOTICES"),
            ReadResource("IosUiLegalProvenance", "PROVENANCE.md"),
        ];
        return [modifiedNotice, .. bundleItems.Where(static item => item.Body.Length > 0)];
    }

    /// <summary>Reads one bundled license without failing the legal screen when a package resource is absent.</summary>
    /// <param name="titleKey">The catalog key used for the document title.</param>
    /// <param name="resourceName">The exact bundle logical name.</param>
    /// <returns>A legal item whose body is empty when the resource was unavailable.</returns>
    private static LegalNoticeItem ReadResource(string titleKey, string resourceName)
    {
        string? path = NSBundle.MainBundle.PathForResource(resourceName, null);
        string body = path is null ? string.Empty : File.ReadAllText(path);
        return new LegalNoticeItem(
            AppStrings.Get(titleKey),
            AppStrings.Get("IosUiOpenFullNotice"),
            body,
            resourceName,
            IsModifiedLibraryNotice: false);
    }

    private sealed record LegalNoticeItem(
        string Title,
        string Summary,
        string Body,
        string ResourceName,
        bool IsModifiedLibraryNotice);
}
