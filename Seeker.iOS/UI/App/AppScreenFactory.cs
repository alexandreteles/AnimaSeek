using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using UIKit;

namespace AnimaSeek.iOS.UI.App;

/// <summary>
/// Registers feature-owned controller factories and provides an honest unavailable state while a destination is absent.
/// </summary>
internal sealed class AppScreenFactory : IAppScreenFactory
{
    private readonly Dictionary<AppTab, Func<IAppRouter, UIViewController>> rootFactories = [];
    private readonly Dictionary<Type, Func<AppRoute, IAppRouter, UIViewController>> destinationFactories = [];

    /// <summary>Registers a root factory for one stable tab.</summary>
    /// <param name="tab">The stable tab to register.</param>
    /// <param name="factory">A factory that creates a fresh root controller.</param>
    public void RegisterRoot(AppTab tab, Func<IAppRouter, UIViewController> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        rootFactories[tab] = factory;
    }

    /// <summary>Registers a controller factory for one typed route.</summary>
    /// <typeparam name="TRoute">The concrete route type.</typeparam>
    /// <param name="factory">A factory that creates a fresh destination controller.</param>
    public void RegisterDestination<TRoute>(Func<TRoute, IAppRouter, UIViewController> factory)
        where TRoute : AppRoute
    {
        ArgumentNullException.ThrowIfNull(factory);
        destinationFactories[typeof(TRoute)] = (route, router) => factory((TRoute)route, router);
    }

    /// <inheritdoc/>
    public UIViewController CreateRoot(AppTab tab, IAppRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        return rootFactories.TryGetValue(tab, out Func<IAppRouter, UIViewController>? factory)
            ? factory(router)
            : new FeatureUnavailableViewController(
                AppStrings.Get(tab.TitleKey()),
                AppStrings.Get("IosUiFeatureUnavailableDetail"),
                tab.SymbolName());
    }

    /// <inheritdoc/>
    public UIViewController CreateDestination(AppRoute route, IAppRouter router)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(router);
        return destinationFactories.TryGetValue(route.GetType(), out Func<AppRoute, IAppRouter, UIViewController>? factory)
            ? factory(route, router)
            : new FeatureUnavailableViewController(
                AppStrings.Get("IosUiFeatureUnavailable"),
                AppStrings.Get("IosUiFeatureUnavailableDetail"),
                "ellipsis.circle");
    }
}

/// <summary>Explains that a known route has no available presentation rather than leaving a blank or inert screen.</summary>
internal sealed class FeatureUnavailableViewController : UIViewController
{
    private readonly string stateTitle;
    private readonly string stateMessage;
    private readonly string symbolName;

    /// <summary>Creates a native unavailable destination.</summary>
    /// <param name="title">The localized navigation title.</param>
    /// <param name="message">The localized explanation.</param>
    /// <param name="symbolName">The reinforcing SF Symbol name.</param>
    public FeatureUnavailableViewController(string title, string message, string symbolName)
    {
        stateTitle = title;
        stateMessage = message;
        this.symbolName = symbolName;
        Title = title;
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = AccessibilityIdentifiers.Route(GetType().FullName ?? GetType().Name);
        ContentStateView.Show(
            this,
            new ContentStatePresentation(stateTitle, stateMessage, symbolName));
    }
}
