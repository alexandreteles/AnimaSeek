using UIKit;

namespace AnimaSeek.iOS.UI.App;

/// <summary>Mirrors the launch storyboard frame while the native hierarchy is composed.</summary>
/// <remarks>
/// The system launch storyboard disappears as soon as the process commits its first frame, which
/// happens before the composition root and tab hierarchy finish building. This controller reproduces
/// the storyboard's exact layout — the Vacuum Navy brand field with the reversed stacked lockup,
/// identical in light and dark — so cold start shows one continuous brand frame instead of a blank
/// window, and it is replaced as the window root the moment the real hierarchy exists.
/// </remarks>
internal sealed class LaunchPlaceholderViewController : UIViewController
{
    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.FromName("BrandNavy") ?? UIColor.SystemBackground;
        var lockup = new UIImageView(UIImage.FromBundle("LaunchSplash"))
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TranslatesAutoresizingMaskIntoConstraints = false,
            UserInteractionEnabled = false,
            IsAccessibilityElement = false,
        };
        View.AddSubview(lockup);
        NSLayoutConstraint.ActivateConstraints([
            lockup.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
            lockup.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor, -30),
            lockup.WidthAnchor.ConstraintEqualTo(250),
            lockup.HeightAnchor.ConstraintEqualTo(267),
        ]);
    }

    /// <summary>Keeps the status bar legible on the fixed navy brand field.</summary>
    public override UIStatusBarStyle PreferredStatusBarStyle() => UIStatusBarStyle.LightContent;
}
