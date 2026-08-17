using AnimaSeek.iOS.UI.App;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS;

/// <summary>Owns the iPhone window and bridges scene lifecycle, URL routing, and state checkpoints.</summary>
[Register("SceneDelegate")]
public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    private AppCoordinator? coordinator;
    private bool disconnected;

    /// <summary>Gets or sets the window attached to this scene.</summary>
    [Export("window")]
    public UIWindow? Window { get; set; }

    /// <summary>Creates the native application hierarchy before delivering any cold-start routes.</summary>
    /// <param name="scene">The connecting scene.</param>
    /// <param name="session">The persistent scene session.</param>
    /// <param name="connectionOptions">Incoming URL and handoff context.</param>
    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
        {
            return;
        }

        var window = new UIWindow(windowScene);
        Window = window;

        // Commit the launch-continuation frame first: the system launch storyboard is torn down at the
        // first committed frame, long before the composition root and tab hierarchy finish building, so
        // without this placeholder a cold start renders a blank window for the rest of that work.
        window.RootViewController = new LaunchPlaceholderViewController();
        window.MakeKeyAndVisible();

        UIOpenUrlContext[] pendingContexts = connectionOptions?.UrlContexts?.ToArray() ?? [];
        NSUserActivity? restorationActivity = session.StateRestorationActivity;
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (disconnected)
            {
                return;
            }

            coordinator = new AppCoordinator(window, restorationActivity);
            foreach (UIOpenUrlContext context in pendingContexts)
            {
                HandleUrl(context.Url);
            }

#if MOCK
            // Scene activation can precede this deferred construction, in which case the
            // activation-driven call already no-opped against a null coordinator.
            if (windowScene.ActivationState == UISceneActivationState.ForegroundActive)
            {
                coordinator.ActivateMockLaunchRoute();
            }
#endif
        });
    }

    /// <summary>Releases route subscriptions when UIKit disconnects this scene.</summary>
    /// <param name="scene">The disconnected scene.</param>
    [Export("sceneDidDisconnect:")]
    public void DidDisconnect(UIScene scene)
    {
        disconnected = true;
        coordinator?.Dispose();
        coordinator = null;
    }

    /// <summary>Reconnects a persisted Soulseek session when the scene becomes active.</summary>
    /// <param name="scene">The activated scene.</param>
    [Export("sceneDidBecomeActive:")]
    public void DidBecomeActive(UIScene scene)
    {
        // The coordinator must observe the foreground state before the activation-driven resume pass runs,
        // because that pass is refused outside an active scene.
        AppCompositionRoot.BackgroundTasks.SceneBecameActive();
        AppCompositionRoot.SetSceneActive(true);
#if MOCK
        coordinator?.ActivateMockLaunchRoute();
#endif
    }

    /// <summary>Stops new task submission while the scene is transitioning away from active use.</summary>
    /// <param name="scene">The resigning scene.</param>
    [Export("sceneWillResignActive:")]
    public void WillResignActive(UIScene scene)
    {
        AppCompositionRoot.SetSceneActive(false);
        AppCompositionRoot.BackgroundTasks.SceneWillResignActive();
    }

    /// <summary>Checkpoints transfer state before iOS suspends the scene.</summary>
    /// <param name="scene">The backgrounding scene.</param>
    [Export("sceneDidEnterBackground:")]
    public void DidEnterBackground(UIScene scene)
    {
        AppCompositionRoot.SetSceneActive(false);
        AppCompositionRoot.BackgroundTasks.SceneEnteredBackground();
        AppCompositionRoot.SuspendPortMapping();
        AppCompositionRoot.CheckpointTransfers();
    }

    /// <summary>Routes Soulseek links and imports Files/share-sheet documents.</summary>
    /// <param name="scene">The receiving scene.</param>
    /// <param name="urlContexts">The URLs UIKit asked the app to open.</param>
    [Export("scene:openURLContexts:")]
    public void OpenUrlContexts(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        if (urlContexts is null)
        {
            return;
        }

        foreach (UIOpenUrlContext context in urlContexts)
        {
            HandleUrl(context.Url);
        }
    }

    /// <summary>Returns non-sensitive navigation state for UIKit scene restoration.</summary>
    /// <param name="scene">The scene whose state UIKit is saving.</param>
    /// <returns>A restoration activity, or <see langword="null"/> before hierarchy creation.</returns>
    [Export("stateRestorationActivityForScene:")]
    public NSUserActivity? GetStateRestorationActivity(UIScene scene) =>
        coordinator?.CreateRestorationActivity();

    /// <summary>Routes validated Soulseek links or local settings documents through the UI coordinator.</summary>
    /// <param name="url">The URL delivered by UIKit.</param>
    private void HandleUrl(NSUrl url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (string.Equals(url.Scheme, "slsk", StringComparison.OrdinalIgnoreCase))
        {
            AppCompositionRoot.DeepLinks.Open(url);
            return;
        }

        if (url.IsFileUrl)
        {
            string? absolute = url.AbsoluteString;
            if (coordinator is not null &&
                !string.IsNullOrWhiteSpace(absolute) &&
                Uri.TryCreate(absolute, UriKind.Absolute, out Uri? documentUrl))
            {
                coordinator.Navigate(new AppRoute.ImportDocument(documentUrl));
            }
        }
    }
}
