using Foundation;
using Seeker.Routing;
using UIKit;

namespace AnimaSeek.iOS.UI.App;

/// <summary>Identifies one of AnimaSeek's stable top-level destinations.</summary>
internal enum AppTab
{
    /// <summary>Account, connection, and secondary destinations.</summary>
    Home = 0,

    /// <summary>Network, saved, and wishlist searches.</summary>
    Search = 1,

    /// <summary>Downloads and uploads.</summary>
    Transfers = 2,

    /// <summary>Another user's shared hierarchy.</summary>
    Browse = 3,
}

/// <summary>Identifies the persisted peer population for a routed Search destination.</summary>
internal enum SearchRouteTarget
{
    /// <summary>Search every reachable peer.</summary>
    Network,

    /// <summary>Search one chosen user supplied in the route subject.</summary>
    User,

    /// <summary>Search one room supplied in the route subject.</summary>
    Room,

    /// <summary>Search the current saved friend list.</summary>
    UserList,

    /// <summary>Issue a one-time wishlist-scoped query.</summary>
    Wishlist,
}

/// <summary>
/// Describes every application-level destination without coupling feature presentation to UIKit navigation.
/// </summary>
internal abstract record AppRoute
{
    /// <summary>Selects a stable top-level tab without changing that tab's navigation stack.</summary>
    /// <param name="Tab">The destination tab.</param>
    internal sealed record SelectTab(AppTab Tab) : AppRoute;

    /// <summary>Opens the conversation overview or one conversation.</summary>
    /// <param name="Username">The optional remote user whose conversation should open.</param>
    internal sealed record Messages(string? Username = null) : AppRoute;

    /// <summary>Opens the room overview or one chatroom.</summary>
    /// <param name="RoomName">The optional room that should open.</param>
    internal sealed record Chatrooms(string? RoomName = null) : AppRoute;

    /// <summary>Opens the saved friends and ignored-users destination.</summary>
    internal sealed record Users : AppRoute;

    /// <summary>Opens one remote user's profile.</summary>
    /// <param name="Username">The remote user name.</param>
    internal sealed record UserProfile(string Username) : AppRoute;

    /// <summary>Opens the native settings catalog.</summary>
    internal sealed record Settings : AppRoute;

    /// <summary>Opens own-profile editing, password, privileges, and portable data export.</summary>
    internal sealed record Account : AppRoute;

    /// <summary>Opens privilege status, donation, and privilege-day transfer.</summary>
    internal sealed record Privileges : AppRoute;

    /// <summary>Opens product identity and version information.</summary>
    internal sealed record About : AppRoute;

    /// <summary>Opens bundled licenses and permanent notices.</summary>
    internal sealed record LegalNotices : AppRoute;

    /// <summary>Opens presentation-safe connection, build, sharing, and log diagnostics.</summary>
    internal sealed record Diagnostics : AppRoute;

    /// <summary>Opens Search and optionally begins or restores a query.</summary>
    /// <param name="Query">The optional search expression.</param>
    /// <param name="Target">The peer population to retain when Search opens.</param>
    /// <param name="Subject">The username or room name required by a targeted route.</param>
    /// <param name="WishlistId">An exact durable wishlist result set to open after a notification tap.</param>
    internal sealed record Search(
        string? Query = null,
        SearchRouteTarget Target = SearchRouteTarget.Network,
        string? Subject = null,
        int? WishlistId = null) : AppRoute;

    /// <summary>Opens Transfers and optionally selects a stable transfer.</summary>
    /// <param name="StableId">The optional presentation-safe transfer identity.</param>
    internal sealed record Transfers(string? StableId = null) : AppRoute;

    /// <summary>Opens Browse at an optional user and remote directory.</summary>
    /// <param name="Username">The optional remote user name.</param>
    /// <param name="Path">The optional normalized remote directory path.</param>
    /// <param name="RequestedDownloadPath">
    /// The optional validated file or folder to resolve and offer for explicit download after browsing.
    /// </param>
    internal sealed record Browse(
        string? Username = null,
        string? Path = null,
        string? RequestedDownloadPath = null) : AppRoute;

    /// <summary>Opens a Files-delivered settings document for review before mutation.</summary>
    /// <param name="Url">The local document URL.</param>
    internal sealed record ImportDocument(Uri Url) : AppRoute;

    /// <summary>Opens safe actions for a validated Soulseek file or folder link.</summary>
    /// <param name="Link">The validated portable link target.</param>
    internal sealed record SoulseekLink(Seeker.Routing.SoulseekLink Link) : AppRoute;
}

/// <summary>Receives application-level navigation requests from feature controllers.</summary>
internal interface IAppRouter
{
    /// <summary>Navigates to a typed application destination.</summary>
    /// <param name="route">The destination and optional payload.</param>
    /// <param name="animated">Whether UIKit should animate a visible navigation transition.</param>
    void Navigate(AppRoute route, bool animated = true);
}

/// <summary>Optionally lets an existing destination consume a repeated or more-specific route in place.</summary>
internal interface IAppRouteReceiving
{
    /// <summary>Applies a route that addresses the receiver's current feature.</summary>
    /// <param name="route">The typed application route.</param>
    void Receive(AppRoute route);
}

/// <summary>Builds feature controllers while keeping the app coordinator free of feature construction details.</summary>
internal interface IAppScreenFactory
{
    /// <summary>Creates the root controller for a stable tab.</summary>
    /// <param name="tab">The tab whose root is required.</param>
    /// <param name="router">The application router exposed to the feature.</param>
    /// <returns>A new feature root controller.</returns>
    UIViewController CreateRoot(AppTab tab, IAppRouter router);

    /// <summary>Creates a pushed or presented controller for a secondary route.</summary>
    /// <param name="route">The route requiring a destination controller.</param>
    /// <param name="router">The application router exposed to the feature.</param>
    /// <returns>A new destination controller.</returns>
    UIViewController CreateDestination(AppRoute route, IAppRouter router);
}

/// <summary>Provides UIKit and localization metadata for stable tabs.</summary>
internal static class AppTabPresentation
{
    /// <summary>Gets the catalog key used for a tab's visible title.</summary>
    /// <param name="tab">The tab to describe.</param>
    /// <returns>A catalog key.</returns>
    public static string TitleKey(this AppTab tab) => tab switch
    {
        AppTab.Home => "home_tab",
        AppTab.Search => "searches_tab2",
        AppTab.Transfers => "transfer_tab",
        AppTab.Browse => "browse_tab",
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown application tab."),
    };

    /// <summary>Gets the unselected SF Symbol name for a tab.</summary>
    /// <param name="tab">The tab to describe.</param>
    /// <returns>An SF Symbol name.</returns>
    public static string SymbolName(this AppTab tab) => tab switch
    {
        AppTab.Home => "house",
        AppTab.Search => "magnifyingglass",
        AppTab.Transfers => "arrow.up.arrow.down",
        AppTab.Browse => "folder",
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown application tab."),
    };

    /// <summary>Gets the selected SF Symbol name when a distinct filled variant exists.</summary>
    /// <param name="tab">The tab to describe.</param>
    /// <returns>An SF Symbol name.</returns>
    public static string SelectedSymbolName(this AppTab tab) => tab switch
    {
        AppTab.Home => "house.fill",
        AppTab.Search => "magnifyingglass",
        AppTab.Transfers => "arrow.up.arrow.down",
        AppTab.Browse => "folder.fill",
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown application tab."),
    };
}
