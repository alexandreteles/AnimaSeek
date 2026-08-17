using AnimaSeek.iOS.UI.Accessibility;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Displays long, selectable text with Dynamic Type, readable width, and standard system scrolling.</summary>
internal class ReadableTextViewController : UIViewController
{
    private readonly string body;
    private readonly string accessibilityIdentifier;

    /// <summary>Creates a long-form reading destination.</summary>
    /// <param name="title">The localized navigation title.</param>
    /// <param name="body">The complete text that must never be truncated.</param>
    /// <param name="accessibilityIdentifier">The screen's stable automation identifier.</param>
    public ReadableTextViewController(string title, string body, string accessibilityIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityIdentifier);
        Title = title;
        this.body = body;
        this.accessibilityIdentifier = accessibilityIdentifier;
    }

    /// <summary>Gets the selectable text view so subclasses can adjust semantics or initial focus.</summary>
    protected UITextView TextView { get; private set; } = null!;

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = accessibilityIdentifier;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;

        TextView = new UITextView
        {
            Text = body,
            Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body),
            TextColor = UIColor.Label,
            BackgroundColor = UIColor.SystemBackground,
            Editable = false,
            Selectable = true,
            ScrollEnabled = true,
            AdjustsFontForContentSizeCategory = true,
            TextContainerInset = new UIEdgeInsets(20, 0, 32, 0),
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        TextView.TextContainer.LineFragmentPadding = 0;
        View.AddSubview(TextView);
        UILayoutGuide readable = View.ReadableContentGuide;
        NSLayoutConstraint.ActivateConstraints([
            TextView.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            TextView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            TextView.LeadingAnchor.ConstraintEqualTo(readable.LeadingAnchor),
            TextView.TrailingAnchor.ConstraintEqualTo(readable.TrailingAnchor),
        ]);
    }

    /// <inheritdoc/>
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        AccessibilityExtensions.FocusScreen(TextView);
    }
}
