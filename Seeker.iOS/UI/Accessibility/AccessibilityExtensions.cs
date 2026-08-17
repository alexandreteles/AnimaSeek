using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Accessibility;

/// <summary>Contains focused UIKit accessibility helpers shared across AnimaSeek screens.</summary>
internal static class AccessibilityExtensions
{
    /// <summary>Applies a stable automation identifier and optional VoiceOver label to a view.</summary>
    /// <typeparam name="TView">The concrete UIKit view type.</typeparam>
    /// <param name="view">The view to configure.</param>
    /// <param name="identifier">A stable, nonlocalized identifier.</param>
    /// <param name="label">An optional localized accessibility label.</param>
    /// <returns>The same view for declarative construction.</returns>
    public static TView Identified<TView>(this TView view, string identifier, string? label = null)
        where TView : UIView
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        view.AccessibilityIdentifier = identifier;
        if (!string.IsNullOrWhiteSpace(label))
        {
            view.AccessibilityLabel = label;
        }

        return view;
    }

    /// <summary>Posts a concise VoiceOver announcement for a meaningful state transition.</summary>
    /// <param name="announcement">The localized announcement text.</param>
    public static void Announce(string announcement)
    {
        if (!string.IsNullOrWhiteSpace(announcement))
        {
            UIAccessibility.PostNotification(
                UIAccessibilityPostNotification.Announcement,
                new NSString(announcement));
        }
    }

    /// <summary>Moves assistive-technology focus to a newly shown route or state.</summary>
    /// <param name="target">The most useful first element, or <see langword="null"/> for the screen itself.</param>
    public static void FocusScreen(UIView? target = null) =>
        UIAccessibility.PostNotification(
            UIAccessibilityPostNotification.ScreenChanged,
            (NSObject?)target);

    /// <summary>Returns a duration that removes custom motion when Reduce Motion is enabled.</summary>
    /// <param name="preferredDuration">The normal animation duration in seconds.</param>
    /// <returns>Zero with Reduce Motion, otherwise <paramref name="preferredDuration"/>.</returns>
    public static double AccessibleAnimationDuration(double preferredDuration) =>
        UIAccessibility.IsReduceMotionEnabled ? 0 : preferredDuration;
}
