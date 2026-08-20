using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.Components;
using AnimaSeek.iOS.UI.Presentation;
using AnimaSeek.iOS.UI.Presentation.Browse;
using AnimaSeek.iOS.UI.Presentation.Search;
using AnimaSeek.iOS.UI.Presentation.Transfers;
using AnimaSeek.iOS.UI.Screens.About;
using AnimaSeek.iOS.UI.Screens.Browse;
using AnimaSeek.iOS.UI.Screens.Chatrooms;
using AnimaSeek.iOS.UI.Screens.Diagnostics;
using AnimaSeek.iOS.UI.Screens.Home;
using AnimaSeek.iOS.UI.Screens.Legal;
using AnimaSeek.iOS.UI.Screens.Messages;
using AnimaSeek.iOS.UI.Screens.Profile;
using AnimaSeek.iOS.UI.Screens.Search;
using AnimaSeek.iOS.UI.Screens.Settings;
using AnimaSeek.iOS.UI.Screens.Transfers;
using AnimaSeek.iOS.UI.Screens.Users;
using Foundation;
using Seeker.Routing;
using Seeker.Services;
using Seeker.Social;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS.UI.App;

/// <summary>
/// Owns root construction, independent tab stacks, typed routing, modal conflict resolution, and inbound app events.
/// </summary>
internal sealed class AppCoordinator : IAppRouter, IDisposable
{
    private const int MaximumRestorableSecondaryDepth = 8;
    private const int MaximumRestorationPayloadLength = 192;
    private const string SelectedTabDefaultsKey = "AnimaSeek.UI.SelectedTab";
    private const string SecondaryStackDefaultsKey = "AnimaSeek.UI.SecondaryStack";
    private readonly UIWindow window;
    private readonly AppScreenFactory screenFactory;
    private readonly RootTabBarController rootTabs;
    private readonly Lock routeSync = new();
    private readonly Queue<AppRoute> deferredAuthenticatedRoutes = new();
    private AppRoute[] deferredRestorationStack = [];
    private AppTab? selectedTabAfterDeferredRestoration;
    private ImportViewController? activeImportReview;
    private string? activeImportIdentity;
    private bool suppressSelectedTabPersistence;
    private bool disposed;
#if MOCK
    private const string MockPreviewPassword = "animaseek-ui-preview";
    private string? mockPreviewUsername;
    private string? mockPreviewQuery;
    private bool mockConnectionStarted;
#endif

    /// <summary>Creates the root hierarchy and attaches inbound route consumers before draining cold-start input.</summary>
    /// <param name="window">The scene-owned application window.</param>
    /// <param name="restorationActivity">The optional prior non-sensitive scene navigation activity.</param>
    public AppCoordinator(UIWindow window, NSUserActivity? restorationActivity = null)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        screenFactory = CreateFactory();
        rootTabs = new RootTabBarController(screenFactory, this);
        rootTabs.SelectedTabChanged += OnSelectedTabChanged;
        AppCompositionRoot.Session.StateChanged += OnSessionStateChanged;
        AppCompositionRoot.Session.MessagesChanged += OnMessagesChanged;
        AppCompositionRoot.Session.TransfersChanged += OnMessagesChanged;
        AppCompositionRoot.Social.RoomChanged += OnRoomChanged;
        AppCompositionRoot.Social.RoomListChanged += OnMessagesChanged;
        AppCompositionRoot.Wishlist.Changed += OnMessagesChanged;
        AppCompositionRoot.DeepLinks.LinkOpened += OnSoulseekLinkOpened;
        AppCompositionRoot.Notifications.RouteRequested += OnNotificationRouteRequested;
        window.RootViewController = rootTabs;
        RestoreNavigation(restorationActivity);
        UpdateBadges();
        window.MakeKeyAndVisible();

        foreach (NSUrl pending in AppCompositionRoot.DeepLinks.TakeAllPending())
        {
            if (Seeker.Routing.SoulseekLinkParser.TryParse(pending.AbsoluteString, out SoulseekLink? link))
            {
                Navigate(new AppRoute.SoulseekLink(link!), animated: false);
            }
        }

#if MOCK
        ConfigureMockLaunchRoute();
#endif
    }

    /// <summary>Gets the root tab hierarchy owned by this scene.</summary>
    public UIViewController RootViewController => rootTabs;

    /// <inheritdoc/>
    public void Navigate(AppRoute route, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!NSThread.IsMain)
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() => Navigate(route, animated));
            return;
        }

        // A new explicit route is newer intent than a cold-start stack waiting for authentication.
        lock (routeSync)
        {
            deferredRestorationStack = [];
            selectedTabAfterDeferredRestoration = null;
        }

        if (route is not AppRoute.ImportDocument and not AppRoute.SoulseekLink && HasIncompatiblePresentation())
        {
            DismissPresentationThen(() => Navigate(route, animated));
            return;
        }

        if (RequiresAuthentication(route) && AppCompositionRoot.Session.ConnectionState is
            SessionConnectionState.SignedOut or SessionConnectionState.Connecting)
        {
            lock (routeSync)
            {
                if (!deferredAuthenticatedRoutes.Contains(route))
                {
                    deferredAuthenticatedRoutes.Enqueue(route);
                }
            }

            rootTabs.SelectTab(AppTab.Home);
            AccessibilityExtensions.Announce(AppStrings.Get("IosUiSignInToContinue"));
            return;
        }

        switch (route)
        {
            case AppRoute.SelectTab select:
                rootTabs.SelectTab(select.Tab);
                FocusSelectedTab();
                break;
            case AppRoute.Search:
                RouteWithinTab(AppTab.Search, route, animated);
                break;
            case AppRoute.Transfers:
                RouteWithinTab(AppTab.Transfers, route, animated);
                break;
            case AppRoute.Browse:
                RouteWithinTab(AppTab.Browse, route, animated);
                break;
            case AppRoute.ImportDocument import:
                if (!FocusDuplicateImport(import.Url))
                {
                    DismissPresentationThen(() =>
                    {
                        if (!FocusDuplicateImport(import.Url))
                        {
                            PresentImport(import.Url, animated);
                        }
                    });
                }
                break;
            case AppRoute.SoulseekLink link:
                PresentSoulseekLinkActions(link.Link, animated);
                break;
            default:
                PushSecondary(route, animated);
                break;
        }
    }

    /// <summary>Creates a restoration activity containing only a stable tab and bounded payload-free Home stack.</summary>
    /// <returns>A scene-restoration activity suitable for UIKit persistence.</returns>
    public NSUserActivity CreateRestorationActivity()
    {
        AppRoute[] pendingStack;
        AppTab persistedTab;
        lock (routeSync)
        {
            pendingStack = deferredRestorationStack;
            persistedTab = selectedTabAfterDeferredRestoration ?? rootTabs.ActiveTab;
        }

        var values = new List<NSObject> { NSNumber.FromInt32((int)persistedTab) };
        var keys = new List<NSObject> { new NSString("selectedTab") };
        string encodedStack = pendingStack.Length > 0
            ? EncodeRestorableSecondaryStack(pendingStack)
            : EncodeRestorableSecondaryStack();
        NSUserDefaults defaults = NSUserDefaults.StandardUserDefaults;
        defaults.SetInt((int)persistedTab, SelectedTabDefaultsKey);
        if (encodedStack.Length > 0)
        {
            values.Add(new NSString(encodedStack));
            keys.Add(new NSString("secondaryStack"));
            defaults.SetString(encodedStack, SecondaryStackDefaultsKey);
        }
        else
        {
            defaults.RemoveObject(SecondaryStackDefaultsKey);
        }

        var activity = new NSUserActivity("com.animaseek.app.navigation")
        {
            Title = AppStrings.Get("app_name"),
            UserInfo = NSDictionary.FromObjectsAndKeys(values.ToArray(), keys.ToArray()),
        };
        return activity;
    }

#if MOCK
    /// <summary>Starts the mock identity needed by a launch-harness route after the scene becomes active.</summary>
    /// <remarks>This method and its environment-variable behavior do not exist in non-Mock builds.</remarks>
    internal void ActivateMockLaunchRoute()
    {
        if (mockConnectionStarted || mockPreviewUsername is null)
        {
            return;
        }

        if (AppCompositionRoot.Session.ConnectionState == SessionConnectionState.Connected)
        {
            RunMockPreviewQuery();
            return;
        }

        mockConnectionStarted = true;
        _ = ConnectMockPreviewAsync(mockPreviewUsername).ContinueWith(
            _ => RunMockPreviewQuery(),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Runs the harness query once, on the main thread, against an authenticated mock session.</summary>
    private void RunMockPreviewQuery()
    {
        if (mockPreviewQuery is not { } query)
        {
            return;
        }

        mockPreviewQuery = null;
        Navigate(new AppRoute.Search(query), animated: false);
    }
#endif

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        rootTabs.SelectedTabChanged -= OnSelectedTabChanged;
        AppCompositionRoot.Session.StateChanged -= OnSessionStateChanged;
        AppCompositionRoot.Session.MessagesChanged -= OnMessagesChanged;
        AppCompositionRoot.Session.TransfersChanged -= OnMessagesChanged;
        AppCompositionRoot.Social.RoomChanged -= OnRoomChanged;
        AppCompositionRoot.Social.RoomListChanged -= OnMessagesChanged;
        AppCompositionRoot.Wishlist.Changed -= OnMessagesChanged;
        AppCompositionRoot.DeepLinks.LinkOpened -= OnSoulseekLinkOpened;
        AppCompositionRoot.Notifications.RouteRequested -= OnNotificationRouteRequested;
        activeImportReview = null;
        activeImportIdentity = null;
    }

    /// <summary>Registers every root and secondary destination against composition-root presentation services.</summary>
    /// <returns>A complete controller factory for the native application hierarchy.</returns>
    private AppScreenFactory CreateFactory()
    {
        var factory = new AppScreenFactory();
        factory.RegisterRoot(
            AppTab.Home,
            router => new HomeViewController(
                new HomePresentationStore(
                    AppCompositionRoot.Session,
                    AppCompositionRoot.Social,
                    () =>
                    {
                        Common.Share.SharedFileCatalog catalog = AppCompositionRoot.ShareIndex.Catalog;
                        return (catalog.FileCount, catalog.DirectoryCount);
                    }),
                router,
                AppCompositionRoot.FileSystem.DocumentsPath,
                AppCompositionRoot.Toaster));
        factory.RegisterRoot(
            AppTab.Search,
            router => new SearchViewController(
                new SearchPresentationStore(
                    AppCompositionRoot.Session,
                    AppCompositionRoot.KeyValueStore,
                    AppCompositionRoot.Wishlist,
                    AppCompositionRoot.UserLists),
                router));
        factory.RegisterRoot(
            AppTab.Transfers,
            router => new TransfersViewController(
                new TransfersPresentationStore(AppCompositionRoot.Session),
                router));
        factory.RegisterRoot(
            AppTab.Browse,
            router => new BrowseViewController(
                new BrowsePresentationStore(
                    AppCompositionRoot.Session,
                    AppCompositionRoot.KeyValueStore),
                router));
        factory.RegisterDestination<AppRoute.Settings>(
            (_, router) => new SettingsViewController(router, SettingsPresentationStore.CreateDefault()));
        factory.RegisterDestination<AppRoute.Account>(
            (_, router) => new AccountViewController(router, AppCompositionRoot.Account));
        factory.RegisterDestination<AppRoute.Privileges>(
            (_, _) => new PrivilegesViewController(AppCompositionRoot.Account));
        factory.RegisterDestination<AppRoute.About>((_, router) => new AboutViewController(router));
        factory.RegisterDestination<AppRoute.LegalNotices>((_, _) => new LegalNoticesViewController());
        factory.RegisterDestination<AppRoute.Diagnostics>(
            (_, _) => new DiagnosticsViewController(DiagnosticsPresentationStore.CreateDefault()));
        factory.RegisterDestination<AppRoute.Messages>(
            (route, router) => string.IsNullOrWhiteSpace(route.Username)
                ? new MessagesViewController(router, MessagesPresentationStore.CreateDefault())
                : new ConversationViewController(router, MessagesPresentationStore.CreateDefault(), route.Username));
        factory.RegisterDestination<AppRoute.Chatrooms>(
            (route, router) => string.IsNullOrWhiteSpace(route.RoomName)
                ? new ChatroomsViewController(router, ChatroomsPresentationStore.CreateDefault())
                : new RoomViewController(router, ChatroomsPresentationStore.CreateDefault(), route.RoomName));
        factory.RegisterDestination<AppRoute.Users>(
            (_, router) => new UsersViewController(router, UserListPresentationStore.CreateDefault()));
        factory.RegisterDestination<AppRoute.UserProfile>(
            (route, router) => new UserProfileViewController(
                router,
                UserProfilePresentationStore.CreateDefault(route.Username),
                route.Username));
        return factory;
    }

    /// <summary>Pushes a secondary account destination or reuses its matching controller idempotently.</summary>
    /// <param name="route">The requested typed destination.</param>
    /// <param name="animated">Whether the visible transition should animate.</param>
    private void PushSecondary(AppRoute route, bool animated)
    {
        rootTabs.SelectTab(AppTab.Home);
        UINavigationController navigation = rootTabs.NavigationControllerFor(AppTab.Home);
        UIViewController[] controllers = navigation.ViewControllers ?? [];
        UIViewController? existing = controllers.FirstOrDefault(controller => Matches(controller, route));
        if (existing is not null)
        {
            if (navigation.TopViewController is AccountViewController { HasUnsavedChanges: true } account &&
                !ReferenceEquals(existing, account))
            {
                // Keep the unsaved draft in the stack. Popping to an older matching destination would bypass the
                // Account screen's explicit discard confirmation; a temporary duplicate is safer and naturally
                // returns to the draft through the system Back action.
                UIViewController preservedDraftDestination = screenFactory.CreateDestination(route, this);
                navigation.PushViewController(preservedDraftDestination, animated);
                AccessibilityExtensions.FocusScreen(preservedDraftDestination.View);
                return;
            }

            navigation.PopToViewController(existing, animated);
            if (existing is IAppRouteReceiving receiver)
            {
                receiver.Receive(route);
            }

            AccessibilityExtensions.FocusScreen(existing.View);
            return;
        }

        UIViewController destination = screenFactory.CreateDestination(route, this);
        navigation.PushViewController(destination, animated);
        AccessibilityExtensions.FocusScreen(destination.View);
    }

    /// <summary>Selects a stable tab and forwards any route payload to its existing feature controller.</summary>
    /// <param name="tab">The destination tab.</param>
    /// <param name="route">The feature-specific route payload.</param>
    /// <param name="animated">Whether returning to the feature root should animate.</param>
    private void RouteWithinTab(AppTab tab, AppRoute route, bool animated)
    {
        rootTabs.SelectTab(tab);
        UINavigationController navigation = rootTabs.NavigationControllerFor(tab);
        UIViewController? root = navigation.ViewControllers?.FirstOrDefault();
        if (navigation.TopViewController is IAppRouteReceiving currentReceiver)
        {
            currentReceiver.Receive(route);
        }
        else if (root is IAppRouteReceiving rootReceiver)
        {
            navigation.PopToRootViewController(animated);
            rootReceiver.Receive(route);
        }

        AccessibilityExtensions.FocusScreen(navigation.VisibleViewController?.View);
    }

    /// <summary>Presents a Files-delivered document in the same nonmutating review flow as an in-app import.</summary>
    /// <param name="url">The local file URI supplied by UIKit.</param>
    /// <param name="animated">Whether the modal presentation should animate.</param>
    private void PresentImport(Uri url, bool animated)
    {
        NSUrl? sourceUrl = url.IsFile
            ? NSUrl.FromFilename(url.LocalPath)
            : NSUrl.FromString(url.AbsoluteUri);
        if (sourceUrl is null)
        {
            return;
        }

        var importer = new ImportViewController(AppCompositionRoot.SettingsImporter, sourceUrl);
        var navigation = new UINavigationController(importer)
        {
            ModalPresentationStyle = UIModalPresentationStyle.FormSheet,
        };
        importer.NavigationItem.LeftBarButtonItem = new UIBarButtonItem(
            AppStrings.Get("IosUiCancel"),
            UIBarButtonItemStyle.Plain,
            (_, _) => navigation.DismissViewController(true, null))
        {
            AccessibilityIdentifier = "settings.import.cancel",
        };
        activeImportReview = importer;
        activeImportIdentity = CanonicalImportIdentity(url);
        EventHandler? ended = null;
        ended = (_, _) =>
        {
            importer.ReviewEnded -= ended;
            if (ReferenceEquals(activeImportReview, importer))
            {
                activeImportReview = null;
                activeImportIdentity = null;
            }
        };
        importer.ReviewEnded += ended;
        PresentingController.PresentViewController(navigation, animated, completionHandler: null);
    }

    /// <summary>Suppresses duplicate delivery of the same canonical document while its review is active.</summary>
    /// <param name="url">The newly delivered document URL.</param>
    /// <returns><see langword="true"/> when an existing review already owns the document.</returns>
    private bool FocusDuplicateImport(Uri url)
    {
        if (activeImportReview is null ||
            !string.Equals(activeImportIdentity, CanonicalImportIdentity(url), StringComparison.Ordinal))
        {
            return false;
        }

        UIViewController focusTarget = activeImportReview.NavigationController?.VisibleViewController ?? activeImportReview;
        AccessibilityExtensions.FocusScreen(focusTarget.View);
        AccessibilityExtensions.Announce(AppStrings.Get("IosUiImportAlreadyReviewing"));
        return true;
    }

    /// <summary>Normalizes a file URL to one stable route identity without reading private document contents.</summary>
    /// <param name="url">The imported local or external URL.</param>
    /// <returns>A canonical identity suitable only for in-process route de-duplication.</returns>
    private static string CanonicalImportIdentity(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsFile)
        {
            return url.GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped);
        }

        string path = Path.GetFullPath(Uri.UnescapeDataString(url.LocalPath));
        string root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length > root.Length
            ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;
    }

    /// <summary>Presents explicit safe actions for one already validated Soulseek link.</summary>
    /// <param name="link">The normalized file or folder link.</param>
    /// <param name="animated">Whether presentation and subsequent navigation should animate.</param>
    private void PresentSoulseekLinkActions(SoulseekLink link, bool animated)
    {
        UIAlertController actions = UIAlertController.Create(
            AppStrings.Get("IosUiSoulseekLinkTitle"),
            AppStrings.Format("IosUiSoulseekLinkDetail", link.Username, link.Path),
            UIAlertControllerStyle.ActionSheet);
        actions.AddAction(UIAlertAction.Create(
            link.IsFile ? AppStrings.Get("IosUiDownload") : AppStrings.Get("IosUiDownloadFolder"),
            UIAlertActionStyle.Default,
            _ => Navigate(new AppRoute.Browse(link.Username, link.DirectoryPath, link.Path), animated)));
        actions.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiBrowseLocation"),
            UIAlertActionStyle.Default,
            _ => Navigate(new AppRoute.Browse(link.Username, link.DirectoryPath), animated)));
        actions.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiCopyLink"),
            UIAlertActionStyle.Default,
            _ =>
            {
                UIPasteboard.General.String = link.ToString();
                AccessibilityExtensions.Announce(AppStrings.Get("IosUiLinkCopied"));
            }));
        actions.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiShareLink"),
            UIAlertActionStyle.Default,
            _ => DismissPresentationThen(() => PresentShareSheet(link.ToString(), animated))));
        actions.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiCancel"),
            UIAlertActionStyle.Cancel,
            null));
        DismissPresentationThen(() =>
        {
            UIViewController presenter = PresentingController;
            ConfigurePopover(actions.PopoverPresentationController, presenter);
            presenter.PresentViewController(actions, animated, null);
        });
    }

    /// <summary>Presents the standard activity sheet for a canonical Soulseek link.</summary>
    /// <param name="canonicalLink">The privacy-safe canonical URL chosen for sharing.</param>
    /// <param name="animated">Whether presentation should animate.</param>
    private void PresentShareSheet(string canonicalLink, bool animated)
    {
        var share = new UIActivityViewController([new NSString(canonicalLink)], null);
        UIViewController presenter = PresentingController;
        ConfigurePopover(share.PopoverPresentationController, presenter);
        presenter.PresentViewController(share, animated, null);
    }

    /// <summary>Dismisses an incompatible modal before starting the next presentation.</summary>
    /// <param name="continuation">The presentation operation to run after dismissal.</param>
    private void DismissPresentationThen(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        UIViewController candidate = rootTabs.VisibleController();
        while (candidate.PresentingViewController is null && candidate.ParentViewController is not null)
        {
            candidate = candidate.ParentViewController;
        }

        if (candidate.PresentingViewController is null)
        {
            continuation();
            return;
        }

        candidate.DismissViewController(animated: false, continuation);
    }

    /// <summary>Determines whether navigation is currently obscured by a modal controller or system sheet.</summary>
    /// <returns><see langword="true"/> when routing should dismiss a presentation before changing stacks.</returns>
    private bool HasIncompatiblePresentation()
    {
        UINavigationController selected = rootTabs.NavigationControllerFor(rootTabs.ActiveTab);
        UIViewController? stackVisible = selected.VisibleViewController;
        return stackVisible is not null && !ReferenceEquals(rootTabs.VisibleController(), stackVisible);
    }

    /// <summary>Gets the deepest controller currently able to present standard UIKit UI.</summary>
    private UIViewController PresentingController => rootTabs.VisibleController();

    /// <summary>Configures an iPad-safe center anchor for an action or activity sheet.</summary>
    /// <param name="popover">The optional popover presentation owned by UIKit.</param>
    /// <param name="presenter">The controller whose content provides the source rectangle.</param>
    private static void ConfigurePopover(
        UIPopoverPresentationController? popover,
        UIViewController presenter)
    {
        if (popover is null || presenter.View is null)
        {
            return;
        }

        popover.SourceView = presenter.View;
        popover.SourceRect = new CoreGraphics.CGRect(
            presenter.View.Bounds.GetMidX(),
            presenter.View.Bounds.GetMidY(),
            1,
            1);
        popover.PermittedArrowDirections = (UIPopoverArrowDirection)0;
    }

    /// <summary>Routes a warm validated Soulseek URL delivered by the process router.</summary>
    /// <param name="sender">The deep-link router.</param>
    /// <param name="args">The validated link payload.</param>
    private void OnSoulseekLinkOpened(object? sender, SoulseekLinkOpenedEventArgs args) =>
        Navigate(new AppRoute.SoulseekLink(args.Link));

    /// <summary>Maps semantic notification actions to navigation without moving UI for Mark Read.</summary>
    /// <param name="sender">The notification bridge.</param>
    /// <param name="args">The typed notification action and scope.</param>
    private void OnNotificationRouteRequested(object? sender, NotificationRouteEventArgs args)
    {
        if (!NSThread.IsMain)
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                OnNotificationRouteRequested(sender, args));
            return;
        }

        if (args.MarkReadOnly && args.Kind == NotificationKind.PrivateMessage)
        {
            AppCompositionRoot.Session.PrivateMessages.MarkRead(args.Scope);
            return;
        }

        AppRoute? route = args.Kind switch
        {
            NotificationKind.PrivateMessage => new AppRoute.Messages(args.Scope),
            NotificationKind.ChatroomMessage => new AppRoute.Chatrooms(args.Scope),
            NotificationKind.WishlistHit => int.TryParse(args.Scope, out int wishlistId)
                ? new AppRoute.Search(WishlistId: wishlistId)
                : new AppRoute.Search(),
            NotificationKind.FolderCompleted => new AppRoute.Transfers(args.Scope),
            NotificationKind.TransferState => new AppRoute.Transfers(args.Scope),
            NotificationKind.UserOnline => new AppRoute.UserProfile(args.Scope),
            _ => null,
        };
        if (route is not null)
        {
            Navigate(route);
        }
    }

    /// <summary>Refreshes badges and drains authentication-deferred routes after a usable session is restored.</summary>
    /// <param name="sender">The application session.</param>
    /// <param name="args">Unused event data.</param>
    private void OnSessionStateChanged(object? sender, EventArgs args)
    {
        if (!NSThread.IsMain)
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() => OnSessionStateChanged(sender, args));
            return;
        }

        UpdateBadges();
        if (AppCompositionRoot.Session.ConnectionState is
            SessionConnectionState.SignedOut or SessionConnectionState.Connecting)
        {
            return;
        }

        AppRoute[] restorationStack;
        AppTab? restoredTab;
        AppRoute[] pending;
        lock (routeSync)
        {
            restorationStack = deferredRestorationStack;
            restoredTab = selectedTabAfterDeferredRestoration;
            deferredRestorationStack = [];
            selectedTabAfterDeferredRestoration = null;
            pending = deferredAuthenticatedRoutes.ToArray();
            deferredAuthenticatedRoutes.Clear();
        }

        if (restorationStack.Length > 0)
        {
            ApplyRestoredHomeStack(restorationStack);
        }

        if (restoredTab is { } selectedTab)
        {
            rootTabs.SelectTab(selectedTab);
        }

        foreach (AppRoute route in pending)
        {
            Navigate(route);
        }
    }

    /// <summary>Reconciles tab badges after private-message or room-list changes.</summary>
    /// <param name="sender">The changed presentation service.</param>
    /// <param name="args">Unused event data.</param>
    private void OnMessagesChanged(object? sender, EventArgs args) => UpdateBadges();

    /// <summary>Reconciles tab badges after a single room changes.</summary>
    /// <param name="sender">The social presentation service.</param>
    /// <param name="args">The changed room identity and state.</param>
    private void OnRoomChanged(object? sender, RoomStateChangedEventArgs args) => UpdateBadges();

    /// <summary>Publishes the current actionable unread total on Home's tab item.</summary>
    private void UpdateBadges()
    {
        if (!NSThread.IsMain)
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(UpdateBadges);
            return;
        }

        int unseenWishlistResults = AppCompositionRoot.Wishlist.GetSnapshot()
            .Sum(static entry => Math.Max(0, entry.UnseenCount));
        int actionableTransfers = AppCompositionRoot.Session.TransferSnapshots.Count(static transfer =>
            transfer.State.HasFlag(TransferStates.Errored) ||
            transfer.State.HasFlag(TransferStates.Rejected) ||
            transfer.State.HasFlag(TransferStates.TimedOut) ||
            transfer.State.HasFlag(TransferStates.Aborted));
        rootTabs.SetBadge(
            AppTab.Home,
            AppCompositionRoot.Session.PrivateMessages.GetTotalUnreadCount() +
            AppCompositionRoot.Social.Rooms.Count(room => room.HasUnreadMessages) +
            unseenWishlistResults +
            actionableTransfers);
    }

    /// <summary>Persists a non-sensitive tab selection and restores assistive focus after explicit selection.</summary>
    /// <param name="sender">The root tab controller.</param>
    /// <param name="tab">The newly selected stable tab.</param>
    private void OnSelectedTabChanged(object? sender, AppTab tab)
    {
        if (!suppressSelectedTabPersistence)
        {
            NSUserDefaults.StandardUserDefaults.SetInt((int)tab, SelectedTabDefaultsKey);
            lock (routeSync)
            {
                if (deferredRestorationStack.Length > 0)
                {
                    selectedTabAfterDeferredRestoration = tab;
                }
            }
        }

        FocusSelectedTab();
    }

    /// <summary>Restores the stable tab and exact bounded payload-free Home stack, deferring it as one auth unit.</summary>
    /// <param name="activity">UIKit's optional scene-restoration activity.</param>
    private void RestoreNavigation(NSUserActivity? activity)
    {
        int stored = activity?.UserInfo?["selectedTab"] is NSNumber restored
            ? restored.Int32Value
            : (int)NSUserDefaults.StandardUserDefaults.IntForKey(SelectedTabDefaultsKey);
        AppTab selectedTab = Enum.IsDefined((AppTab)stored) ? (AppTab)stored : AppTab.Home;
        rootTabs.SelectTab(selectedTab);

        AppRoute[] stack = ReadRestorableSecondaryStack(activity);
        if (stack.Length == 0)
        {
            return;
        }

        bool requiresDeferredAuthentication = stack.Any(RequiresAuthentication) &&
                                              AppCompositionRoot.Session.ConnectionState is
                                                  SessionConnectionState.SignedOut or
                                                  SessionConnectionState.Connecting;
        if (!requiresDeferredAuthentication)
        {
            ApplyRestoredHomeStack(stack);
            rootTabs.SelectTab(selectedTab);
            return;
        }

        suppressSelectedTabPersistence = true;
        try
        {
            rootTabs.SelectTab(AppTab.Home);
        }
        finally
        {
            suppressSelectedTabPersistence = false;
        }

        lock (routeSync)
        {
            deferredRestorationStack = stack;
            selectedTabAfterDeferredRestoration = selectedTab;
        }

        AccessibilityExtensions.Announce(AppStrings.Get("IosUiSignInToContinue"));
    }

    /// <summary>Encodes the ordered allow-listed Home stack without arbitrary values or route payloads.</summary>
    /// <returns>A delimiter-separated value bounded by <see cref="MaximumRestorableSecondaryDepth"/>.</returns>
    private string EncodeRestorableSecondaryStack()
    {
        UIViewController[] controllers = rootTabs.NavigationControllerFor(AppTab.Home).ViewControllers ?? [];
        string[] names = controllers
            .Skip(1)
            .Select(RestorableDestinationName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .TakeLast(MaximumRestorableSecondaryDepth)
            .ToArray();
        return string.Join('|', names);
    }

    /// <summary>Encodes an already validated authentication-deferred stack without exposing route payloads.</summary>
    /// <param name="routes">The pending payload-free restoration routes.</param>
    /// <returns>The same bounded storage format used for visible controllers.</returns>
    private static string EncodeRestorableSecondaryStack(IEnumerable<AppRoute> routes) => string.Join(
        '|',
        routes
            .Select(RestorableDestinationName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .TakeLast(MaximumRestorableSecondaryDepth));

    /// <summary>Returns the payload-free name of one controller that is safe to persist.</summary>
    /// <param name="controller">A controller in Home's independent navigation stack.</param>
    /// <returns>A stable allow-listed name, or <see langword="null"/> for roots and payload-bearing screens.</returns>
    private static string? RestorableDestinationName(UIViewController controller) => controller switch
        {
            SettingsViewController => "settings",
            AccountViewController => "account",
            PrivilegesViewController => "privileges",
            AboutViewController => "about",
            LegalNoticesViewController => "legal",
            DiagnosticsViewController => "diagnostics",
            MessagesViewController => "messages",
            ChatroomsViewController => "rooms",
            UsersViewController => "users",
            _ => null,
        };

    /// <summary>Maps only payload-free typed destinations to their bounded persisted name.</summary>
    /// <param name="route">A validated restoration route.</param>
    /// <returns>The allow-listed name, or <see langword="null"/> for any payload-bearing route.</returns>
    private static string? RestorableDestinationName(AppRoute route) => route switch
    {
        AppRoute.Settings => "settings",
        AppRoute.Account => "account",
        AppRoute.Privileges => "privileges",
        AppRoute.About => "about",
        AppRoute.LegalNotices => "legal",
        AppRoute.Diagnostics => "diagnostics",
        AppRoute.Messages { Username: null } => "messages",
        AppRoute.Chatrooms { RoomName: null } => "rooms",
        AppRoute.Users => "users",
        _ => null,
    };

    /// <summary>Reads and validates a bounded stack from scene activity, legacy activity, or defaults fallback.</summary>
    /// <param name="activity">UIKit's optional current scene-restoration activity.</param>
    /// <returns>Only exact allow-listed payload-free routes, or an empty stack on corruption.</returns>
    private static AppRoute[] ReadRestorableSecondaryStack(NSUserActivity? activity)
    {
        NSObject? activityValue = activity?.UserInfo?["secondaryStack"];
        if (activityValue is not null)
        {
            return activityValue is NSString encoded
                ? DecodeRestorableSecondaryStack(encoded.ToString())
                : [];
        }

        if (activity?.UserInfo?["destination"] is NSString legacyDestination)
        {
            return RestoredDestination(legacyDestination.ToString()) is { } legacyRoute
                ? [legacyRoute]
                : [];
        }

        string? persisted = NSUserDefaults.StandardUserDefaults.StringForKey(SecondaryStackDefaultsKey);
        return persisted is null ? [] : DecodeRestorableSecondaryStack(persisted);
    }

    /// <summary>Rejects oversized, malformed, unknown, or over-depth stack storage as one corrupt unit.</summary>
    /// <param name="encoded">The delimiter-separated allow-list names.</param>
    /// <returns>The exact ordered typed routes, or an empty stack when validation fails.</returns>
    private static AppRoute[] DecodeRestorableSecondaryStack(string encoded)
    {
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumRestorationPayloadLength)
        {
            return [];
        }

        string[] names = encoded.Split('|');
        if (names.Length is 0 or > MaximumRestorableSecondaryDepth ||
            names.Any(static name => string.IsNullOrWhiteSpace(name)))
        {
            return [];
        }

        AppRoute?[] routes = names.Select(RestoredDestination).ToArray();
        return routes.Any(static route => route is null)
            ? []
            : routes.Select(static route => route!).ToArray();
    }

    /// <summary>Rebuilds Home's exact safe controller sequence without altering another selected tab.</summary>
    /// <param name="routes">Validated payload-free routes in bottom-to-top order.</param>
    private void ApplyRestoredHomeStack(IReadOnlyList<AppRoute> routes)
    {
        UINavigationController navigation = rootTabs.NavigationControllerFor(AppTab.Home);
        UIViewController? root = navigation.ViewControllers?.FirstOrDefault();
        if (root is null)
        {
            return;
        }

        UIViewController[] restored =
        [
            root,
            .. routes.Take(MaximumRestorableSecondaryDepth)
                .Select(route => screenFactory.CreateDestination(route, this)),
        ];
        navigation.SetViewControllers(restored, animated: false);
        if (rootTabs.ActiveTab == AppTab.Home)
        {
            AccessibilityExtensions.FocusScreen(navigation.VisibleViewController?.View);
        }
    }

    /// <summary>Maps one bounded restoration name back to a typed route without accepting arbitrary payloads.</summary>
    /// <param name="name">The stable destination name stored by <see cref="CreateRestorationActivity"/>.</param>
    /// <returns>The matching route, or <see langword="null"/> for an unknown or obsolete value.</returns>
    private static AppRoute? RestoredDestination(string name) => name switch
    {
        "settings" => new AppRoute.Settings(),
        "account" => new AppRoute.Account(),
        "privileges" => new AppRoute.Privileges(),
        "about" => new AppRoute.About(),
        "legal" => new AppRoute.LegalNotices(),
        "diagnostics" => new AppRoute.Diagnostics(),
        "messages" => new AppRoute.Messages(),
        "rooms" => new AppRoute.Chatrooms(),
        "users" => new AppRoute.Users(),
        _ => null,
    };

    /// <summary>Moves assistive focus to the visible controller in the selected independent stack.</summary>
    private void FocusSelectedTab()
    {
        UINavigationController navigation = rootTabs.NavigationControllerFor(rootTabs.ActiveTab);
        AccessibilityExtensions.FocusScreen(navigation.VisibleViewController?.View);
    }

#if MOCK
    /// <summary>Reads an optional simulator launch destination and routes it through production navigation rules.</summary>
    /// <remarks>
    /// Supported <c>ANIMASEEK_UI_ROUTE</c> values are home, search, transfers, browse, settings, account,
    /// privileges, messages, rooms, users, profile, about, legal, and diagnostics. <c>ANIMASEEK_UI_USER</c> supplies the Browse or
    /// Profile identity and the private mock login identity. <c>ANIMASEEK_UI_ROOM</c> opens one named
    /// chatroom directly on the rooms route. <c>ANIMASEEK_UI_QUERY</c> runs a search
    /// immediately on the search route, so a simulator smoke can exercise the whole request-to-rows path
    /// without keyboard automation. This code is excluded from non-Mock assemblies.
    /// </remarks>
    private void ConfigureMockLaunchRoute()
    {
        string? requestedName = Environment.GetEnvironmentVariable("ANIMASEEK_UI_ROUTE")?
            .Trim()
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return;
        }

        string requestedUser = Environment.GetEnvironmentVariable("ANIMASEEK_UI_USER")?.Trim() is { Length: > 0 } user
            ? user
            : "animaseek-preview";

        // The room list reorders itself as unread counts arrive, so naming a room is the only way to land on
        // one conversation repeatably.
        string? requestedRoom = Environment.GetEnvironmentVariable("ANIMASEEK_UI_ROOM")?.Trim() is { Length: > 0 } room
            ? room
            : null;
        AppRoute? route = requestedName switch
        {
            "home" => new AppRoute.SelectTab(AppTab.Home),
            "search" => new AppRoute.Search(),
            "transfers" => new AppRoute.Transfers(),
            "browse" => new AppRoute.Browse(requestedUser),
            "settings" => new AppRoute.Settings(),
            "account" => new AppRoute.Account(),
            "privileges" => new AppRoute.Privileges(),
            "messages" => new AppRoute.Messages(),
            "rooms" => new AppRoute.Chatrooms(requestedRoom),
            "users" => new AppRoute.Users(),
            "profile" => new AppRoute.UserProfile(requestedUser),
            "about" => new AppRoute.About(),
            "legal" => new AppRoute.LegalNotices(),
            "diagnostics" => new AppRoute.Diagnostics(),
            _ => null,
        };
        if (route is null)
        {
            return;
        }

        if (RequiresAuthentication(route))
        {
            mockPreviewUsername = requestedUser;
        }

        // A query is issued only after the mock identity is authenticated, because an unauthenticated
        // search correctly reports the offline state instead of exercising the result path.
        if (route is AppRoute.Search &&
            Environment.GetEnvironmentVariable("ANIMASEEK_UI_QUERY")?.Trim() is { Length: > 0 } query)
        {
            mockPreviewQuery = query;
        }

        Navigate(route, animated: false);
    }

    /// <summary>Authenticates the launch harness against only the compiled-in mock client.</summary>
    /// <param name="username">The deterministic preview identity.</param>
    private static async Task ConnectMockPreviewAsync(string username)
    {
        try
        {
            if (AppCompositionRoot.Session.ConnectionState == SessionConnectionState.SignedOut)
            {
                await AppCompositionRoot.Session.LoginAsync(username, MockPreviewPassword);
            }
            else if (AppCompositionRoot.Session.ConnectionState == SessionConnectionState.Disconnected)
            {
                await AppCompositionRoot.Session.ReconnectAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppCompositionRoot.LoggerBackend.FirebaseError("Mock UI launch-route authentication failed.", exception);
        }
    }
#endif

    /// <summary>Determines whether a route requires at least an established account identity.</summary>
    /// <param name="route">The route to classify.</param>
    /// <returns><see langword="true"/> for network or account-scoped destinations.</returns>
    private static bool RequiresAuthentication(AppRoute route) => route is
        AppRoute.Messages or
        AppRoute.Chatrooms or
        AppRoute.Users or
        AppRoute.UserProfile or
        AppRoute.Account or
        AppRoute.Privileges or
        AppRoute.Search or
        AppRoute.Browse;

    /// <summary>Determines whether an existing controller already represents the exact requested destination.</summary>
    /// <param name="controller">A controller in Home's navigation stack.</param>
    /// <param name="route">The requested route and identity payload.</param>
    /// <returns><see langword="true"/> when the controller can be reused idempotently.</returns>
    private static bool Matches(UIViewController controller, AppRoute route) => (controller, route) switch
    {
        (SettingsViewController, AppRoute.Settings) => true,
        (AccountViewController, AppRoute.Account) => true,
        (PrivilegesViewController, AppRoute.Privileges) => true,
        (AboutViewController, AppRoute.About) => true,
        (LegalNoticesViewController, AppRoute.LegalNotices) => true,
        (DiagnosticsViewController, AppRoute.Diagnostics) => true,
        (MessagesViewController, AppRoute.Messages { Username: null }) => true,
        (ConversationViewController conversation, AppRoute.Messages message) =>
            string.Equals(conversation.Username, message.Username, StringComparison.OrdinalIgnoreCase),
        (ChatroomsViewController, AppRoute.Chatrooms { RoomName: null }) => true,
        (RoomViewController room, AppRoute.Chatrooms requested) =>
            string.Equals(room.RoomName, requested.RoomName, StringComparison.OrdinalIgnoreCase),
        (UsersViewController, AppRoute.Users) => true,
        (UserProfileViewController profile, AppRoute.UserProfile requested) =>
            string.Equals(profile.Username, requested.Username, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
