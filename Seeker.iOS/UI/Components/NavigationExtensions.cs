using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Contains navigation-hierarchy helpers that preserve standard UIKit presentation behavior.</summary>
internal static class NavigationExtensions
{
    /// <summary>Finds the currently visible controller through navigation, tab, and presentation containers.</summary>
    /// <param name="controller">The hierarchy root.</param>
    /// <returns>The most specific visible controller.</returns>
    public static UIViewController VisibleController(this UIViewController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        UIViewController current = controller;
        while (true)
        {
            current = current switch
            {
                _ when current.PresentedViewController is not null =>
                    current.PresentedViewController,
                UINavigationController navigation when navigation.VisibleViewController is not null =>
                    navigation.VisibleViewController,
                UITabBarController tabs when tabs.SelectedViewController is not null =>
                    tabs.SelectedViewController,
                _ => current,
            };

            if (current is not UINavigationController &&
                current is not UITabBarController &&
                current.PresentedViewController is null)
            {
                return current;
            }
        }
    }
}
