using Foundation;
using CoreGraphics;
using CoreText;
using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Creates small, consistently accessible UIKit primitives without replacing native component behavior.</summary>
internal static class UIKitFactory
{
    /// <summary>Gets a Dynamic Type font while adapting the nullable platform binding to a non-null UI contract.</summary>
    /// <param name="style">The preferred system text style.</param>
    /// <returns>The preferred font, or a readable system fallback.</returns>
    public static UIFont PreferredFont(UIFontTextStyle style) =>
        UIFont.GetPreferredFontForTextStyle(style) ?? UIFont.SystemFontOfSize(UIFont.LabelFontSize)!;

    /// <summary>Gets a Dynamic Type font whose digits share one fixed width so ticking numerals do not jitter.</summary>
    /// <param name="style">The preferred system text style.</param>
    /// <returns>The preferred font with monospaced digits, which keeps scaling with the content size category.</returns>
    public static UIFont PreferredMonospacedDigitFont(UIFontTextStyle style)
    {
        UIFont font = PreferredFont(style);
        var monospacedDigits = new UIFontAttributes(
            new UIFontFeature(CTFontFeatureNumberSpacing.Selector.MonospacedNumbers));
        return UIFont.FromDescriptor(font.FontDescriptor.CreateWithAttributes(monospacedDigits), 0) ?? font;
    }

    /// <summary>The one fixed corner radius shared by rounded content containers such as conversation bubbles.</summary>
    public const int RoundedContentCornerRadius = 18;

    /// <summary>Applies the shared rounded-content corner treatment to a container view.</summary>
    /// <param name="view">The container to round.</param>
    public static void ApplyRoundedContentCorners(UIView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.CornerConfiguration = UICornerConfiguration.CreateUniformCorners(
            UICornerRadius.CreateFixed(RoundedContentCornerRadius));
    }

    /// <summary>Applies a capsule corner treatment that follows the view's measured height at every Dynamic Type size.</summary>
    /// <param name="view">The container to shape.</param>
    public static void ApplyCapsuleCorners(UIView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.CornerConfiguration = UICornerConfiguration.CreateCapsule();
    }

    /// <summary>The shared size of the circular conversation controls, which is also the touch-target minimum.</summary>
    public const int CircularActionSize = 44;

    /// <summary>The fixed glyph size for the circular conversation controls.</summary>
    /// <remarks>
    /// A button configuration scales its symbol with Dynamic Type, but these controls are pinned at 44 points,
    /// so at accessibility sizes an unconstrained glyph grows past the circle and renders as a clipped sliver.
    /// A fixed point size keeps it inside; the 44-point target already satisfies the touch minimum.
    /// </remarks>
    private static UIImageSymbolConfiguration CircularActionSymbol =>
        UIImageSymbolConfiguration.Create(20, UIImageSymbolWeight.Semibold);

    /// <summary>Creates one 44-point circular glyph control that carries its meaning through VoiceOver.</summary>
    /// <param name="symbol">The SF Symbol glyph.</param>
    /// <param name="label">The localized accessible name, since the control shows no text.</param>
    /// <param name="action">The action invoked after explicit activation.</param>
    /// <param name="filled">Whether this is the filled primary variant rather than the gray secondary one.</param>
    /// <returns>A configured, self-constraining circular button.</returns>
    public static UIButton CircularAction(string symbol, string label, Action action, bool filled = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        var button = UIButton.FromType(UIButtonType.System);
        UIButtonConfiguration configuration = filled
            ? UIButtonConfiguration.FilledButtonConfiguration
            : UIButtonConfiguration.GrayButtonConfiguration;
        configuration.Image = UIImage.GetSystemImage(symbol);
        configuration.PreferredSymbolConfigurationForImage = CircularActionSymbol;
        configuration.CornerStyle = UIButtonConfigurationCornerStyle.Capsule;
        configuration.ContentInsets = NSDirectionalEdgeInsets.Zero;
        button.Configuration = configuration;
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        button.AccessibilityLabel = label;
        button.TouchUpInside += (_, _) => action();
        NSLayoutConstraint.ActivateConstraints(
        [
            button.WidthAnchor.ConstraintEqualTo(CircularActionSize),
            button.HeightAnchor.ConstraintEqualTo(CircularActionSize),
        ]);
        return button;
    }

    /// <summary>Applies the composer field's hairline separator border and keeps it correct across appearances.</summary>
    /// <param name="view">The field container.</param>
    /// <remarks>
    /// A <c>CGColor</c> is a resolved value, not a dynamic color, so the border has to be re-resolved whenever
    /// the interface style flips.
    /// </remarks>
    public static void ApplyComposerFieldBorder(UIView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.Layer.BorderWidth = (nfloat)0.5;
        view.Layer.BorderColor = UIColor.Separator.GetResolvedColor(view.TraitCollection).CGColor;
        view.RegisterForTraitChanges<UITraitUserInterfaceStyle>((_, _) =>
            view.Layer.BorderColor = UIColor.Separator.GetResolvedColor(view.TraitCollection).CGColor);
    }

    /// <summary>Anchors an action sheet only where a popover is the platform's own presentation.</summary>
    /// <param name="sheet">The action sheet about to be presented.</param>
    /// <param name="source">The view the popover points at on a regular width.</param>
    /// <param name="sourceRect">The anchor rectangle, or the source's own bounds when omitted.</param>
    /// <remarks>
    /// iOS 26 honors a popover anchor on a compact iPhone too, so an anchor there turns the system's full-width
    /// sheet into a bubble squeezed against the anchor with its actions clipped. Compact widths therefore leave
    /// <c>SourceView</c> unset; a regular width still needs a source, which is what an anchor is actually for.
    /// </remarks>
    public static void AnchorActionSheet(UIAlertController sheet, UIView source, CGRect? sourceRect = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(source);
        if (source.TraitCollection.HorizontalSizeClass != UIUserInterfaceSizeClass.Regular ||
            sheet.PopoverPresentationController is not { } popover)
        {
            return;
        }

        popover.SourceView = source;
        popover.SourceRect = sourceRect ?? source.Bounds;
    }

    /// <summary>The fixed corner radius shared by growing text wells: half the 44 pt resting control height.</summary>
    public const int GrowingFieldCornerRadius = 22;

    /// <summary>Applies the corner treatment for a text well that grows with its content.</summary>
    /// <param name="view">The container to shape.</param>
    /// <remarks>
    /// A capsule tracks the measured height, so a field that grows past one line turns into a lozenge whose
    /// half-circle ends overrun the text and steal usable width. Pinning the radius at the resting half-height
    /// keeps a one-line field visually identical to a capsule while a grown field stays a calm rounded well.
    /// </remarks>
    public static void ApplyGrowingFieldCorners(UIView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.CornerConfiguration = UICornerConfiguration.CreateUniformCorners(
            UICornerRadius.CreateFixed(GrowingFieldCornerRadius));
    }

    /// <summary>Creates a self-sizing label that follows Dynamic Type and semantic colors.</summary>
    /// <param name="style">The preferred system text style.</param>
    /// <param name="color">The semantic text color, or <see cref="UIColor.Label"/> when omitted.</param>
    /// <param name="lines">The maximum line count; zero allows unlimited wrapping.</param>
    /// <returns>A programmatic Auto Layout label.</returns>
    public static UILabel Label(
        UIFontTextStyle style,
        UIColor? color = null,
        nint lines = 0) =>
        new()
        {
            Font = PreferredFont(style),
            TextColor = color ?? UIColor.Label,
            Lines = lines,
            AdjustsFontForContentSizeCategory = true,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

    /// <summary>Creates a toolbar status item that yields width to the actionable items beside it.</summary>
    /// <param name="text">The compact status text shown between the actions.</param>
    /// <param name="accessibilityLabel">The unabbreviated phrase announced instead of the compact text.</param>
    /// <param name="accessibilityIdentifier">The stable automation identifier.</param>
    /// <returns>A non-interactive bar item that truncates rather than pushing buttons past the toolbar edges.</returns>
    /// <remarks>
    /// A plain <see cref="UIBarButtonItem"/> keeps its full intrinsic width, so a long selection count drives the
    /// buttons on either side off the toolbar's insets. A custom label with low compression resistance shrinks
    /// first, which also reads correctly: the count reports state and is not something to press.
    /// </remarks>
    public static UIBarButtonItem ToolbarStatusItem(
        string text,
        string accessibilityLabel,
        string accessibilityIdentifier)
    {
        UILabel label = Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel, lines: 1);
        label.Text = text;
        label.TextAlignment = UITextAlignment.Center;
        label.LineBreakMode = UILineBreakMode.TailTruncation;

        // Auto Layout, not a fixed frame: a sized-to-fit label cannot compress, so it would keep its full width
        // and push the buttons beside it past the toolbar edges instead of truncating first.
        label.SetContentCompressionResistancePriority(
            (float)UILayoutPriority.DefaultLow,
            UILayoutConstraintAxis.Horizontal);
        label.SetContentHuggingPriority(
            (float)UILayoutPriority.DefaultLow,
            UILayoutConstraintAxis.Horizontal);
        return new UIBarButtonItem(label)
        {
            IsAccessibilityElement = true,
            AccessibilityLabel = accessibilityLabel,
            AccessibilityIdentifier = accessibilityIdentifier,
        };
    }

    /// <summary>Creates the fixed gap that keeps a toolbar status item off the buttons beside it.</summary>
    /// <returns>A fixed-width spacer.</returns>
    /// <remarks>
    /// Flexible spaces collapse to nothing once the items fill the toolbar, which leaves the status text touching
    /// the adjacent button. A fixed gap survives that compression; the status label truncates instead.
    /// </remarks>
    public static UIBarButtonItem ToolbarStatusGap() =>
        new(UIBarButtonSystemItem.FixedSpace) { Width = 12 };

    /// <summary>Creates a vertical stack that uses directional layout and programmatic Auto Layout.</summary>
    /// <param name="spacing">The point spacing between arranged subviews.</param>
    /// <returns>A vertical stack view.</returns>
    public static UIStackView VerticalStack(double spacing = 8) =>
        new()
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = (nfloat)spacing,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

    /// <summary>Creates a system button from a modern configuration and enforces a 44-point minimum target.</summary>
    /// <param name="title">The localized visible action label.</param>
    /// <param name="configuration">The native configuration to apply.</param>
    /// <param name="action">The action invoked after explicit activation.</param>
    /// <param name="imageName">An optional SF Symbol name.</param>
    /// <returns>A configured system button.</returns>
    public static UIButton Button(
        string title,
        UIButtonConfiguration configuration,
        Action action,
        string? imageName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(action);
        configuration.Title = title;
        configuration.Image = string.IsNullOrWhiteSpace(imageName) ? null : UIImage.GetSystemImage(imageName);
        configuration.ImagePadding = 8;
        var button = UIButton.FromType(UIButtonType.System);
        button.Configuration = configuration;
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        button.AccessibilityLabel = title;
        button.TouchUpInside += (_, _) => action();
        _ = button.HeightAnchor.ConstraintGreaterThanOrEqualTo(44).WithPriority(UILayoutPriority.Required).Activate();
        return button;
    }

    /// <summary>Creates a native text field that participates in Dynamic Type and AutoFill.</summary>
    /// <param name="placeholder">The localized hint repeated by the visible field label.</param>
    /// <param name="contentType">The system AutoFill content type.</param>
    /// <param name="secure">Whether entered text should be obscured.</param>
    /// <returns>A rounded system text field.</returns>
    public static UITextField TextField(string placeholder, NSString contentType, bool secure = false) =>
        new()
        {
            BorderStyle = UITextBorderStyle.RoundedRect,
            Placeholder = placeholder,
            TextContentType = contentType,
            SecureTextEntry = secure,
            Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body),
            AdjustsFontForContentSizeCategory = true,
            ClearButtonMode = UITextFieldViewMode.WhileEditing,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

    /// <summary>Removes every arranged subview and its hierarchy entry from a stack.</summary>
    /// <param name="stack">The stack to empty.</param>
    public static void RemoveAllArrangedSubviews(this UIStackView stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        foreach (UIView view in stack.ArrangedSubviews)
        {
            stack.RemoveArrangedSubview(view);
            view.RemoveFromSuperview();
        }
    }

    /// <summary>Installs an Auto Layout-backed table header at the height required by its Dynamic Type content.</summary>
    /// <param name="table">The owning table.</param>
    /// <param name="header">The fully constrained header view.</param>
    public static void SetSelfSizingHeader(this UITableView table, UIView header)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(header);
        table.TableHeaderView = header;
        table.ResizeTableHeader();
    }

    /// <summary>Recalculates the current table header after a width or content-size-category change.</summary>
    /// <param name="table">The table whose existing header should be measured.</param>
    public static void ResizeTableHeader(this UITableView table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.TableHeaderView is not { } header || table.Bounds.Width <= 0)
        {
            return;
        }

        CGRect frame = header.Frame;
        frame.X = 0;
        frame.Y = 0;
        frame.Width = table.Bounds.Width;
        header.Frame = frame;
        header.SetNeedsLayout();
        header.LayoutIfNeeded();
        CGSize fitted = header.SystemLayoutSizeFittingSize(
            new CGSize(table.Bounds.Width, 0),
            (float)UILayoutPriority.Required,
            (float)UILayoutPriority.FittingSizeLevel);
        nfloat height = (nfloat)Math.Max(1, fitted.Height);
        if (Math.Abs(frame.Height - height) > 0.5)
        {
            frame.Height = height;
            header.Frame = frame;
            table.TableHeaderView = header;
        }
    }

}

/// <summary>Applies an Auto Layout priority fluently while retaining the original constraint.</summary>
internal static class LayoutConstraintExtensions
{
    /// <summary>Assigns an Auto Layout priority to a constraint.</summary>
    /// <param name="constraint">The constraint to update.</param>
    /// <param name="priority">The requested priority.</param>
    /// <returns>The same constraint.</returns>
    public static NSLayoutConstraint WithPriority(
        this NSLayoutConstraint constraint,
        UILayoutPriority priority)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        constraint.Priority = (float)priority;
        return constraint;
    }

    /// <summary>Activates a single constraint and returns it for ownership or later updates.</summary>
    /// <param name="constraint">The constraint to activate.</param>
    /// <returns>The same active constraint.</returns>
    public static NSLayoutConstraint Activate(this NSLayoutConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        constraint.Active = true;
        return constraint;
    }
}
