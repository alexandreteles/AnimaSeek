using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Describes one capsule chip that carries a configuration dimension's current value and its choices.</summary>
/// <param name="Id">Stable identity used to retain the button across renders.</param>
/// <param name="Title">The visible current value, which must read correctly without its symbol.</param>
/// <param name="SymbolName">A reinforcing SF Symbol name.</param>
/// <param name="IsActive">Whether the dimension currently narrows or overrides the default behavior.</param>
/// <param name="AccessibilityIdentifier">The stable nonlocalized automation identifier.</param>
/// <param name="AccessibilityLabel">The dimension name, which stays constant as the value changes.</param>
/// <param name="AccessibilityValue">The current value spoken after the label.</param>
/// <param name="Signature">
/// A value that changes whenever the menu contents change, so an open menu is never rebuilt needlessly.
/// </param>
/// <param name="Menu">Builds the pull-down menu presented as the chip's primary action.</param>
/// <param name="Activate">The action invoked when the chip has no menu.</param>
internal sealed record ChipDescriptor(
    string Id,
    string Title,
    string? SymbolName,
    bool IsActive,
    string AccessibilityIdentifier,
    string AccessibilityLabel,
    string? AccessibilityValue,
    string Signature,
    Func<UIMenu>? Menu = null,
    Action? Activate = null);

/// <summary>
/// Presents configuration dimensions as a single horizontally scrollable row of native capsule chips, so the
/// controls stay reachable at every Dynamic Type size instead of consuming vertical space as static summary text.
/// </summary>
internal sealed class ChipBarView : UIView
{
    private const int MinimumChipHeight = 44;
    private const int HorizontalInset = 16;
    private const int VerticalInset = 6;
    private readonly UIScrollView scrollView = new()
    {
        ShowsHorizontalScrollIndicator = false,
        ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    private readonly UIStackView stack = new()
    {
        Axis = UILayoutConstraintAxis.Horizontal,
        Alignment = UIStackViewAlignment.Center,
        Spacing = 8,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    private readonly Dictionary<string, Chip> chips = new(StringComparer.Ordinal);

    /// <summary>Creates an empty chip row that sizes itself from its chips.</summary>
    public ChipBarView()
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(scrollView);
        scrollView.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints([
            scrollView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            scrollView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            scrollView.TopAnchor.ConstraintEqualTo(TopAnchor),
            scrollView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
            scrollView.HeightAnchor.ConstraintEqualTo(stack.HeightAnchor, 1, VerticalInset * 2),
            stack.LeadingAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.LeadingAnchor, HorizontalInset),
            stack.TrailingAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.TrailingAnchor, -HorizontalInset),
            stack.TopAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.TopAnchor, VerticalInset),
            stack.BottomAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.BottomAnchor, -VerticalInset),
        ]);
    }

    /// <summary>Applies chips in visual order, retaining existing buttons and rebuilding only what changed.</summary>
    /// <param name="descriptors">The chips to present, in order.</param>
    public void Apply(IReadOnlyList<ChipDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        HashSet<string> requested = descriptors.Select(descriptor => descriptor.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string removed in chips.Keys.Where(id => !requested.Contains(id)).ToArray())
        {
            chips[removed].Button.RemoveFromSuperview();
            chips.Remove(removed);
        }

        for (int index = 0; index < descriptors.Count; index++)
        {
            ChipDescriptor descriptor = descriptors[index];
            if (!chips.TryGetValue(descriptor.Id, out Chip? chip))
            {
                chip = new Chip(CreateButton());
                chips[descriptor.Id] = chip;
            }

            if (stack.ArrangedSubviews.Length <= index ||
                !ReferenceEquals(stack.ArrangedSubviews[index], chip.Button))
            {
                stack.InsertArrangedSubview(chip.Button, (nuint)index);
            }

            if (string.Equals(chip.Signature, descriptor.Signature, StringComparison.Ordinal))
            {
                continue;
            }

            chip.Signature = descriptor.Signature;
            chip.Activate = descriptor.Activate;
            Configure(chip.Button, descriptor);
        }
    }

    private static UIButton CreateButton()
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        _ = button.HeightAnchor.ConstraintGreaterThanOrEqualTo(MinimumChipHeight).Activate();

        // Each chip must be exactly as wide as its own value: without required hugging the stack shares its
        // width between the chips and a one-word chip becomes as wide as a sentence.
        button.SetContentHuggingPriority((float)UILayoutPriority.Required, UILayoutConstraintAxis.Horizontal);
        button.SetContentCompressionResistancePriority(
            (float)UILayoutPriority.Required,
            UILayoutConstraintAxis.Horizontal);
        return button;
    }

    /// <summary>Applies one chip's visible value, its state emphasis, and its pull-down menu.</summary>
    private static void Configure(UIButton button, ChipDescriptor descriptor)
    {
        UIButtonConfiguration configuration = descriptor.IsActive
            ? UIButtonConfiguration.TintedButtonConfiguration
            : UIButtonConfiguration.GrayButtonConfiguration;
        configuration.Title = descriptor.Title;
        configuration.Image = string.IsNullOrWhiteSpace(descriptor.SymbolName)
            ? null
            : UIImage.GetSystemImage(descriptor.SymbolName);
        configuration.ImagePadding = 5;
        configuration.CornerStyle = UIButtonConfigurationCornerStyle.Capsule;

        // A subheadline title keeps every chip in one row at standard Dynamic Type sizes; the transformer
        // lets UIKit rescale it on a content-size change without rebuilding the chip. Once the accessibility
        // sizes take over the row scrolls horizontally rather than wrapping or shrinking its text.
        configuration.ButtonSize = UIButtonConfigurationSize.Small;
        configuration.ContentInsets = new NSDirectionalEdgeInsets(6, 12, 6, 12);
        configuration.TitleTextAttributesTransformer = attributes => new UIStringAttributes(attributes)
        {
            Font = UIKitFactory.PreferredFont(UIFontTextStyle.Subheadline),
        }.Dictionary;
        configuration.PreferredSymbolConfigurationForImage =
            UIImageSymbolConfiguration.Create(UIImageSymbolScale.Small);
        button.Configuration = configuration;
        button.AccessibilityIdentifier = descriptor.AccessibilityIdentifier;
        button.AccessibilityLabel = descriptor.AccessibilityLabel;
        button.AccessibilityValue = descriptor.AccessibilityValue;
        button.Menu = descriptor.Menu?.Invoke();
        button.ShowsMenuAsPrimaryAction = descriptor.Menu is not null;
    }

    /// <summary>Retains one chip's button, its current menu signature, and its menu-free action.</summary>
    private sealed class Chip
    {
        /// <summary>Subscribes the chip's single activation handler exactly once.</summary>
        /// <param name="button">The retained capsule button.</param>
        public Chip(UIButton button)
        {
            Button = button;
            button.TouchUpInside += (_, _) => Activate?.Invoke();
        }

        /// <summary>Gets the retained button.</summary>
        public UIButton Button { get; }

        /// <summary>Gets or sets the state signature that last produced this chip's configuration.</summary>
        public string? Signature { get; set; }

        /// <summary>Gets or sets the action invoked when the chip presents no menu.</summary>
        public Action? Activate { get; set; }
    }
}
