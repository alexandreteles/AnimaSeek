using System.Runtime.CompilerServices;
using CoreGraphics;
using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Presents selection actions above the floating tab bar without covering or replacing it.</summary>
/// <remarks>
/// A navigation controller's toolbar is chrome pinned to the window edge, so the floating tab bar draws straight
/// over it; raising the navigation controller's safe area does not move it either. The tab bar's own bottom
/// accessory places content correctly but always draws its own opaque background. The controls therefore live in a
/// transparent bar owned by the screen, positioned from the tab bar's measured frame so both stay visible.
/// </remarks>
internal static class SelectionAccessory
{
    private static readonly ConditionalWeakTable<UIViewController, SelectionBar> Bars = [];

    /// <summary>Shows the given selection items above the tab bar, or removes the bar when empty.</summary>
    /// <param name="controller">The controller owning the selection mode.</param>
    /// <param name="items">The bar items to present, or an empty set to end the selection presentation.</param>
    /// <param name="animated">Whether the transition animates.</param>
    public static void SetSelectionAccessory(
        this UIViewController controller,
        IReadOnlyList<UIBarButtonItem> items,
        bool animated)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            if (Bars.TryGetValue(controller, out SelectionBar? existing))
            {
                existing.SetItems([], animated);
            }

            return;
        }

        SelectionBar bar = Bars.GetValue(controller, static owner => new SelectionBar(owner));
        bar.SetItems(items, animated);
    }

    /// <summary>Removes a selection bar this controller may still own after leaving the screen.</summary>
    /// <param name="controller">The controller leaving the hierarchy.</param>
    /// <param name="animated">Whether the removal animates.</param>
    public static void ClearSelectionAccessory(this UIViewController controller, bool animated) =>
        controller.SetSelectionAccessory([], animated);
}

/// <summary>A transparent toolbar that keeps itself directly above the floating tab bar of its screen.</summary>
internal sealed class SelectionBar : UIToolbar
{
    private const float TabBarGap = 8;

    /// <summary>The padding a toolbar's shared background draws around an item on every edge.</summary>
    private const float SharedBackgroundInset = 5;

    /// <summary>The height a toolbar reserves for its items when nothing taller is presented.</summary>
    private const float DefaultBarHeight = 44;

    /// <summary>The space a toolbar keeps above and below an item, sizing the item down to fit if it must.</summary>
    private const float ItemVerticalInset = 3;

    private readonly UIViewController controller;
    private readonly NSLayoutConstraint bottom;
    private readonly NSLayoutConstraint left;
    private readonly NSLayoutConstraint right;
    private readonly NSLayoutConstraint height;

    /// <summary>The height the presented actions need, or zero to leave the toolbar its natural height.</summary>
    private nfloat barHeight;

    /// <summary>Installs a hidden selection bar into the controller's view.</summary>
    /// <param name="controller">The screen that owns the selection mode.</param>
    public SelectionBar(UIViewController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        TranslatesAutoresizingMaskIntoConstraints = false;
        AccessibilityIdentifier = "selection.bar";
        Hidden = true;
        Alpha = 0;

        // Only the items should be visible: the buttons carry their own material, and a bar background stacked
        // under them reads as an opaque slab over the list behind it.
        var appearance = new UIKit.UIToolbarAppearance();
        appearance.ConfigureWithTransparentBackground();
        appearance.BackgroundColor = UIColor.Clear;
        appearance.BackgroundEffect = null;
        appearance.ShadowColor = UIColor.Clear;
        StandardAppearance = appearance;
        CompactAppearance = appearance;
        ScrollEdgeAppearance = appearance;
        BackgroundColor = UIColor.Clear;

        // The bar is sized to the tab bar, so its own margins would push the items back out past that width; the
        // buttons carry their own padding and align with the tab bar's items without them.
        InsetsLayoutMarginsFromSafeArea = false;
        PreservesSuperviewLayoutMargins = false;
        DirectionalLayoutMargins = new NSDirectionalEdgeInsets(0, 0, 0, 0);

        UIView view = controller.View!;
        view.AddSubview(this);

        // Absolute edges rather than leading/trailing: the constants come from the tab bar's own frame, which is
        // already in absolute coordinates and symmetric, so no direction-dependent flipping applies.
        bottom = BottomAnchor.ConstraintEqualTo(view.BottomAnchor, -TabBarGap);
        left = LeftAnchor.ConstraintEqualTo(view.LeftAnchor);
        right = RightAnchor.ConstraintEqualTo(view.RightAnchor);

        // Inactive until the bar presents actions that carry their own material: a toolbar sizes its items down
        // to the one height it reports, which would crop away the padding those actions take over from the
        // shared background they no longer draw. Anything else is left to size itself as a toolbar always has.
        height = HeightAnchor.ConstraintEqualTo(DefaultBarHeight);
        NSLayoutConstraint.ActivateConstraints([left, right, bottom]);
    }

    /// <summary>Replaces the presented items, showing or hiding the bar to match.</summary>
    /// <param name="items">The items to present, or an empty set to hide the bar.</param>
    /// <param name="animated">Whether the transition animates.</param>
    public void SetItems(IReadOnlyList<UIBarButtonItem> items, bool animated)
    {
        ArgumentNullException.ThrowIfNull(items);
        bool visible = items.Count > 0;
        if (visible)
        {
            UIBarButtonItem[] presented = [.. BuildEqualWidthActions(items)];
            foreach (UIBarButtonItem item in presented)
            {
                // A toolbar draws its own shared glass behind every item. Behind a view that already carries
                // material that capsule sits slightly proud of the button's own and reads as a halo around it,
                // and behind the status label it reads as a disc around text that is not pressable. Items with
                // no view of their own still need it: there the bar's glass is the only material they have.
                item.HidesSharedBackground = item.CustomView is not null;
            }

            Items = presented;
            height.Active = barHeight > 0;
            if (height.Active)
            {
                SetConstant(height, barHeight);
            }

            controller.View!.BringSubviewToFront(this);
        }

        UpdateOffset();
        UpdateContentClearance(visible);
        if (!animated)
        {
            Hidden = !visible;
            Alpha = visible ? 1 : 0;
            return;
        }

        Hidden = false;
        Animate(0.2, () => Alpha = visible ? 1 : 0, () => Hidden = !visible);
    }

    /// <inheritdoc/>
    public override void LayoutSubviews()
    {
        UpdateOffset();
        base.LayoutSubviews();
    }

    /// <summary>
    /// Keeps the bar aligned to the tab bar — directly above it and matching its width — across layout,
    /// rotation, and text size changes.
    /// </summary>
    private void UpdateOffset()
    {
        UIView view = controller.View!;
        nfloat offset = -TabBarGap;
        nfloat leftInset = view.SafeAreaInsets.Left;
        nfloat rightInset = -view.SafeAreaInsets.Right;
        if (controller.TabBarController?.TabBar is { Hidden: false } tabBar)
        {
            CGRect tabBarFrame = view.ConvertRectFromView(tabBar.Bounds, tabBar);
            offset = -((nfloat)Math.Max(0, view.Bounds.GetMaxY() - tabBarFrame.GetMinY()) + TabBarGap);
            leftInset = tabBarFrame.GetMinX();
            rightInset = tabBarFrame.GetMaxX() - view.Bounds.GetMaxX();
        }

        SetConstant(bottom, offset);
        SetConstant(left, leftInset);
        SetConstant(right, rightInset);
    }

    /// <summary>Rebuilds the titled actions as buttons of one width so the status between them is evenly spaced.</summary>
    /// <param name="items">The items about to be presented.</param>
    /// <returns>The items to hand the toolbar.</returns>
    /// <remarks>
    /// A toolbar centers a lone middle item on the bar's own width, so actions of different widths leave visibly
    /// different gaps on either side of the status. Setting <c>Width</c> equalizes only the layout slots and leaves
    /// each capsule hugging its own title, so the actions become real buttons constrained to a shared width: the
    /// narrower one grows to the wider, and every title stays intact.
    /// </remarks>
    private IReadOnlyList<UIBarButtonItem> BuildEqualWidthActions(IReadOnlyList<UIBarButtonItem> items)
    {
        if (!items.Any(item => item.CustomView is not null))
        {
            // Without a status item there is nothing to center, so each action keeps its natural width — and
            // the bar's shared background, which leaves the toolbar to size itself.
            barHeight = 0;
            return items;
        }

        var rebuilt = new List<UIBarButtonItem>(items.Count);
        var buttons = new List<UIButton>();
        foreach (UIBarButtonItem item in items)
        {
            if (item.CustomView is not null || string.IsNullOrEmpty(item.Title))
            {
                rebuilt.Add(item);
                continue;
            }

            UIButton button = CreateActionButton(item);
            buttons.Add(button);
            rebuilt.Add(new UIBarButtonItem(button));
        }

        // A constant each button carries itself, not a constraint between them: the toolbar has not adopted these
        // buttons yet, and anchors across views without a common ancestor cannot be activated.
        nfloat width = 0;
        nfloat tallest = 0;
        foreach (UIButton button in buttons)
        {
            CGSize fit = button.SizeThatFits(new CGSize(nfloat.MaxValue, nfloat.MaxValue));
            width = (nfloat)Math.Max(width, fit.Width);
            tallest = (nfloat)Math.Max(tallest, fit.Height);
        }

        nfloat actionHeight = tallest + (2 * SharedBackgroundInset);
        foreach (UIButton button in buttons)
        {
            button.WidthAnchor.ConstraintEqualTo(width).Active = true;
            button.HeightAnchor.ConstraintEqualTo(actionHeight).Active = true;
        }

        // The bar takes the actions plus the room a toolbar keeps around them, because it sizes an item down to
        // whatever is left otherwise — which would drop the padding these buttons took over from the shared
        // background they no longer draw.
        barHeight = (nfloat)Math.Max(DefaultBarHeight, actionHeight + (2 * ItemVerticalInset));
        return rebuilt;
    }

    /// <summary>Recreates one titled bar item as a capsule button that forwards to the item's own action.</summary>
    /// <param name="item">The item supplied by the screen.</param>
    /// <returns>A button carrying the item's title, emphasis, enablement, and identifier.</returns>
    private static UIButton CreateActionButton(UIBarButtonItem item)
    {
        bool emphasized = item.Style == UIBarButtonItemStyle.Done;

        // Glass, not a painted capsule: these buttons sit over content beside the tab bar, and a solid fill reads
        // as a white slab against a light list. The emphasized action keeps the prominent variant.
        UIButtonConfiguration configuration = emphasized
            ? UIButtonConfiguration.ProminentGlassButtonConfiguration
            : UIButtonConfiguration.GlassButtonConfiguration;
        configuration.Title = item.Title;
        configuration.CornerStyle = UIButtonConfigurationCornerStyle.Capsule;
        configuration.TitleLineBreakMode = UILineBreakMode.TailTruncation;

        // Suppressing the shared background also removes the padding it drew around the action, so the button
        // takes that padding over and keeps the footprint it presented before, without the second capsule.
        // Only the horizontal half is expressed here: a glass configuration holds its own height whatever the
        // vertical insets say, so the bar constrains that instead.
        NSDirectionalEdgeInsets insets = configuration.ContentInsets;
        configuration.ContentInsets = new NSDirectionalEdgeInsets(
            insets.Top,
            insets.Leading + SharedBackgroundInset,
            insets.Bottom,
            insets.Trailing + SharedBackgroundInset);

        var button = UIButton.FromType(UIButtonType.System);
        button.Configuration = configuration;
        button.Enabled = item.Enabled;
        button.AccessibilityLabel = item.AccessibilityLabel ?? item.Title;
        button.AccessibilityIdentifier = item.AccessibilityIdentifier;
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        button.TouchUpInside += (sender, _) =>
        {
            if (item.Target is { } target && item.Action is { } action)
            {
                UIApplication.SharedApplication.SendAction(action, target, sender as NSObject, null);
            }
        };
        return button;
    }

    /// <summary>Assigns a constraint constant only on a real change, so layout does not feed back into itself.</summary>
    /// <param name="constraint">The constraint to update.</param>
    /// <param name="constant">The measured constant.</param>
    private static void SetConstant(NSLayoutConstraint constraint, nfloat constant)
    {
        if (Math.Abs(constraint.Constant - constant) > 0.5)
        {
            constraint.Constant = constant;
        }
    }

    /// <summary>Insets the screen's content so the bar never covers the last row of a list.</summary>
    /// <param name="visible">Whether the bar is presented.</param>
    private void UpdateContentClearance(bool visible)
    {
        nfloat presented = barHeight > 0
            ? barHeight
            : (nfloat)Math.Max(IntrinsicContentSize.Height, DefaultBarHeight);
        nfloat clearance = visible ? presented + TabBarGap : 0;
        UIEdgeInsets insets = controller.AdditionalSafeAreaInsets;
        if (Math.Abs(insets.Bottom - clearance) > 0.5)
        {
            insets.Bottom = clearance;
            controller.AdditionalSafeAreaInsets = insets;
        }
    }
}
