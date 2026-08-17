using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using UIKit;

namespace AnimaSeek.iOS.UI.App;

/// <summary>Hosts AnimaSeek's four stable tabs with one independent navigation stack per destination.</summary>
internal sealed class RootTabBarController : UITabBarController
{
    private readonly Dictionary<AppTab, UINavigationController> navigationControllers = [];
    private readonly TabSelectionDelegate selectionDelegate;

    /// <summary>Creates every tab root through the supplied feature factory.</summary>
    /// <param name="screenFactory">The feature controller factory.</param>
    /// <param name="router">The application router passed into feature controllers.</param>
    public RootTabBarController(IAppScreenFactory screenFactory, IAppRouter router)
    {
        ArgumentNullException.ThrowIfNull(screenFactory);
        ArgumentNullException.ThrowIfNull(router);
        selectionDelegate = new TabSelectionDelegate(this);
        Delegate = selectionDelegate;

        UIViewController[] controllers = Enum.GetValues<AppTab>()
            .Select(tab => CreateNavigationController(tab, screenFactory.CreateRoot(tab, router)))
            .Cast<UIViewController>()
            .ToArray();
        SetViewControllers(controllers, animated: false);
        TabBar.AccessibilityIdentifier = AccessibilityIdentifiers.AppTabBar;
        RestorationIdentifier = "app.root-tabs";
    }

    /// <summary>Raised after the person explicitly selects another top-level destination.</summary>
    public event EventHandler<AppTab>? SelectedTabChanged;

    /// <summary>Gets the currently selected stable tab.</summary>
    public AppTab ActiveTab => Enum.IsDefined((AppTab)(int)SelectedIndex)
        ? (AppTab)(int)SelectedIndex
        : AppTab.Home;

    /// <summary>Returns the independent navigation controller associated with a stable tab.</summary>
    /// <param name="tab">The destination tab.</param>
    /// <returns>The tab's navigation controller.</returns>
    public UINavigationController NavigationControllerFor(AppTab tab) =>
        navigationControllers.TryGetValue(tab, out UINavigationController? navigation)
            ? navigation
            : throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown application tab.");

    /// <summary>Selects a stable tab without disturbing any navigation stack.</summary>
    /// <param name="tab">The destination tab.</param>
    public void SelectTab(AppTab tab)
    {
        if (!navigationControllers.ContainsKey(tab))
        {
            throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown application tab.");
        }

        SelectedIndex = (nint)tab;
    }

    /// <summary>Sets or clears one tab's meaningful actionable count.</summary>
    /// <param name="tab">The destination tab.</param>
    /// <param name="count">The positive count, or zero to clear the badge.</param>
    public void SetBadge(AppTab tab, int count)
    {
        UINavigationController navigation = NavigationControllerFor(tab);
        navigation.TabBarItem.BadgeValue = count > 0
            ? count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : null;
        navigation.TabBarItem.AccessibilityValue = count > 0
            ? AppStrings.Format("IosUiUnreadItems", count)
            : null;
    }

    private UINavigationController CreateNavigationController(AppTab tab, UIViewController root)
    {
        string title = AppStrings.Get(tab.TitleKey());
        root.Title ??= title;
        root.NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;
        var navigation = new UINavigationController(root)
        {
            RestorationIdentifier = $"app.navigation.{tab.ToString().ToLowerInvariant()}",
        };
        navigation.NavigationBar.PrefersLargeTitles = true;
        navigation.TabBarItem = new UITabBarItem(
            title,
            UIImage.GetSystemImage(tab.SymbolName()),
            UIImage.GetSystemImage(tab.SelectedSymbolName()));
        navigation.TabBarItem.AccessibilityIdentifier = $"app.tab.{tab.ToString().ToLowerInvariant()}";
        navigationControllers.Add(tab, navigation);
        return navigation;
    }

    private void NotifySelectionChanged() => SelectedTabChanged?.Invoke(this, ActiveTab);

    private sealed class TabSelectionDelegate(RootTabBarController owner) : UITabBarControllerDelegate
    {
        /// <inheritdoc/>
        public override void ViewControllerSelected(
            UITabBarController tabBarController,
            UIViewController viewController) => owner.NotifySelectionChanged();
    }
}
