using Foundation;
using UIKit;

namespace AnimaSeek.iOS;

/// <summary>Initializes the iOS process service graph and creates scene configurations.</summary>
[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    /// <summary>Initializes background registrations and platform services before any scene connects.</summary>
    /// <param name="application">The shared application object.</param>
    /// <param name="launchOptions">Optional system launch context.</param>
    /// <returns><see langword="true"/> to continue launch.</returns>
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        AppCompositionRoot.Initialize();
        return true;
    }

    /// <summary>Returns the single scene configuration declared in <c>Info.plist</c>.</summary>
    /// <param name="application">The shared application object.</param>
    /// <param name="connectingSceneSession">The new window-scene session.</param>
    /// <param name="options">Connection options, including incoming URLs.</param>
    /// <returns>The default application-window configuration.</returns>
    public override UISceneConfiguration GetConfiguration(
        UIApplication application,
        UISceneSession connectingSceneSession,
        UISceneConnectionOptions options) =>
        new("Default Configuration", connectingSceneSession.Role);
}
