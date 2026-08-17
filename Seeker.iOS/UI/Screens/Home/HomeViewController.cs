using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using Foundation;
using Seeker;
using UIKit;

namespace AnimaSeek.iOS.UI.Screens.Home;

/// <summary>Renders signed-out, connecting, disconnected, and connected account states on one stable Home surface.</summary>
internal sealed class HomeViewController : UIViewController
{
    private readonly HomePresentationStore store;
    private readonly IAppRouter router;
    private readonly string documentsPath;
    private readonly IToaster toaster;
    private readonly UIScrollView scrollView = new()
    {
        AlwaysBounceVertical = true,
        KeyboardDismissMode = UIScrollViewKeyboardDismissMode.Interactive,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };
    private readonly UIStackView contentStack = UIKitFactory.VerticalStack(20);
    private string lastAccessibilityState = string.Empty;
    private UITextField? usernameField;
    private UITextField? passwordField;
    private UILabel? errorLabel;
    private UIButton? primaryButton;
    private bool passwordVisible;

    /// <summary>Creates Home over its presentation store and application router.</summary>
    /// <param name="store">The account presentation store.</param>
    /// <param name="router">The application router for secondary destinations.</param>
    /// <param name="documentsPath">The Files-visible Documents folder that is both the share root and download root.</param>
    /// <param name="toaster">Reports a Files-app hand-off that iOS refused.</param>
    public HomeViewController(
        HomePresentationStore store,
        IAppRouter router,
        string documentsPath,
        IToaster toaster)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsPath);
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.documentsPath = documentsPath;
        this.toaster = toaster ?? throw new ArgumentNullException(nameof(toaster));
        Title = AppStrings.Get("home_tab");
        RestorationIdentifier = "home.root";
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        View.AccessibilityIdentifier = AccessibilityIdentifiers.HomeScreen;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Always;

        // Settings carries Account, About, and Legal Notices, so it stays reachable from the bar in every
        // account state rather than depending on a list that only the connected state renders.
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(
            UIImage.GetSystemImage("gearshape")!,
            UIBarButtonItemStyle.Plain,
            (_, _) => router.Navigate(new AppRoute.Settings()))
        {
            AccessibilityLabel = AppStrings.Get("IosUiSettingsTitle"),
            AccessibilityIdentifier = AccessibilityIdentifiers.HomeSettings,
        };
        contentStack.LayoutMargins = new UIEdgeInsets(8, 0, 40, 0);
        contentStack.LayoutMarginsRelativeArrangement = true;

        View.AddSubview(scrollView);
        scrollView.AddSubview(contentStack);
        NSLayoutConstraint.ActivateConstraints([
            scrollView.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            scrollView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            scrollView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            scrollView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            contentStack.TopAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.TopAnchor),
            contentStack.BottomAnchor.ConstraintEqualTo(scrollView.ContentLayoutGuide.BottomAnchor),
            contentStack.LeadingAnchor.ConstraintEqualTo(View.ReadableContentGuide.LeadingAnchor),
            contentStack.TrailingAnchor.ConstraintEqualTo(View.ReadableContentGuide.TrailingAnchor),
        ]);

        store.StateChanged += OnStateChanged;
        Render(store.State, announce: false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.StateChanged -= OnStateChanged;
            store.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Renders and announces one published immutable account snapshot.</summary>
    /// <param name="sender">The Home presentation store.</param>
    /// <param name="state">The coherent account snapshot.</param>
    private void OnStateChanged(object? sender, HomePresentationState state) => Render(state, announce: true);

    /// <summary>Rebuilds the mutually exclusive account state while preserving active credential input and focus.</summary>
    /// <param name="state">The coherent account snapshot to render.</param>
    /// <param name="announce">Whether meaningful state transitions should be announced.</param>
    private void Render(HomePresentationState state, bool announce)
    {
        if (!NSThread.IsMain)
        {
            BeginInvokeOnMainThread(() => Render(state, announce));
            return;
        }

        string enteredUsername = usernameField?.Text ?? state.Username ?? string.Empty;
        string enteredPassword = passwordField?.Text ?? string.Empty;
        bool wasUsernameFocused = usernameField?.IsFirstResponder == true;
        bool wasPasswordFocused = passwordField?.IsFirstResponder == true;
        contentStack.RemoveAllArrangedSubviews();
        usernameField = null;
        passwordField = null;
        errorLabel = null;
        primaryButton = null;

        switch (state.AccountState)
        {
            case HomeAccountState.SignedOut:
                BuildSignedOut(state, enteredUsername, enteredPassword);
                break;
            case HomeAccountState.Connecting:
                BuildConnecting(state);
                break;
            case HomeAccountState.Connected:
                BuildConnected(state);
                break;
            case HomeAccountState.Disconnected:
                BuildDisconnected(state);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state.AccountState, "Unknown Home state.");
        }

        View!.SetNeedsLayout();
        View.LayoutIfNeeded();
        if (wasUsernameFocused)
        {
            usernameField?.BecomeFirstResponder();
        }
        else if (wasPasswordFocused)
        {
            passwordField?.BecomeFirstResponder();
        }

        if (announce)
        {
            AnnounceStateIfChanged(state);
        }
    }

    /// <summary>Builds the AutoFill-aware credential form and inline validation state.</summary>
    /// <param name="state">The signed-out snapshot.</param>
    /// <param name="username">The currently entered or restored user name.</param>
    /// <param name="password">The currently entered password retained only in memory.</param>
    private void BuildSignedOut(HomePresentationState state, string username, string password)
    {
        var heading = UIKitFactory.Label(UIFontTextStyle.Title1);
        heading.Text = AppStrings.Get("signin_or_create_account");
        heading.AccessibilityTraits |= UIAccessibilityTrait.Header;
        var explanation = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SecondaryLabel);
        explanation.Text = AppStrings.Get("no_account");

        var usernameLabel = UIKitFactory.Label(UIFontTextStyle.Subheadline);
        usernameLabel.Text = AppStrings.Get("username");
        usernameLabel.AccessibilityTraits |= UIAccessibilityTrait.Header;
        usernameField = UIKitFactory.TextField(AppStrings.Get("username"), UITextContentType.Username)
            .Identified(AccessibilityIdentifiers.HomeUsername, AppStrings.Get("username"));
        usernameField.Text = username;
        usernameField.AutocapitalizationType = UITextAutocapitalizationType.None;
        usernameField.AutocorrectionType = UITextAutocorrectionType.No;
        usernameField.SpellCheckingType = UITextSpellCheckingType.No;
        usernameField.ReturnKeyType = UIReturnKeyType.Next;
        usernameField.ShouldReturn = _ =>
        {
            passwordField?.BecomeFirstResponder();
            return false;
        };
        usernameField.EditingChanged += OnCredentialEdited;

        var passwordLabel = UIKitFactory.Label(UIFontTextStyle.Subheadline);
        passwordLabel.Text = AppStrings.Get("password");
        passwordLabel.AccessibilityTraits |= UIAccessibilityTrait.Header;
        passwordField = UIKitFactory.TextField(AppStrings.Get("password"), UITextContentType.Password, secure: !passwordVisible)
            .Identified(AccessibilityIdentifiers.HomePassword, AppStrings.Get("password"));
        passwordField.Text = password;
        passwordField.ReturnKeyType = UIReturnKeyType.Go;
        passwordField.ShouldReturn = _ =>
        {
            SubmitCredentials();
            return false;
        };
        passwordField.EditingChanged += OnCredentialEdited;
        passwordField.RightView = CreatePasswordVisibilityButton();
        passwordField.RightViewMode = UITextFieldViewMode.Always;

        errorLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SystemRed);
        errorLabel.Text = state.ErrorMessage;
        errorLabel.Hidden = string.IsNullOrWhiteSpace(state.ErrorMessage);
        errorLabel.IsAccessibilityElement = !errorLabel.Hidden;
        errorLabel.AccessibilityIdentifier = AccessibilityIdentifiers.HomeLoginError;
        errorLabel.AccessibilityTraits |= UIAccessibilityTrait.StaticText;
        if (!string.IsNullOrWhiteSpace(state.ErrorMessage))
        {
            UITextField? affectedField = IsUsernameValid(username) ? passwordField : usernameField;
            if (affectedField is not null)
            {
                affectedField.AccessibilityHint = state.ErrorMessage;
            }
        }

        primaryButton = UIKitFactory.Button(
            AppStrings.Get("signin"),
            UIButtonConfiguration.FilledButtonConfiguration,
            SubmitCredentials,
            "person.crop.circle.badge.checkmark")
            .Identified(AccessibilityIdentifiers.HomeLoginSubmit);
        primaryButton.Enabled = CanSubmit(username, password);

        foreach (UIView view in new UIView[]
        {
            heading,
            explanation,
            usernameLabel,
            usernameField,
            passwordLabel,
            passwordField,
            errorLabel,
            primaryButton,
        })
        {
            contentStack.AddArrangedSubview(view);
        }

        contentStack.SetCustomSpacing(8, heading);
        contentStack.SetCustomSpacing(6, usernameLabel);
        contentStack.SetCustomSpacing(6, passwordLabel);
    }

    /// <summary>Builds bounded connection progress with an explicit cancel action.</summary>
    /// <param name="state">The connecting snapshot.</param>
    private void BuildConnecting(HomePresentationState state)
    {
        var container = UIKitFactory.VerticalStack(12);
        container.Alignment = UIStackViewAlignment.Center;
        var indicator = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Medium)
        {
            HidesWhenStopped = false,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        indicator.StartAnimating();
        var heading = UIKitFactory.Label(UIFontTextStyle.Title2);
        heading.Text = AppStrings.Get(state.IsReconnectAttempt ? "IosUiReconnecting" : "ConnectingToServer");
        heading.TextAlignment = UITextAlignment.Center;
        heading.AccessibilityTraits |= UIAccessibilityTrait.Header;
        var detail = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SecondaryLabel);
        detail.Text = state.IsReconnectAttempt
            ? AppStrings.Format("IosUiReconnectingAs", state.Username ?? AppStrings.Get("IosUiUnknown"))
            : string.IsNullOrWhiteSpace(state.Username)
                ? AppStrings.Get("IosUiConnectingDetail")
                : AppStrings.Format("IosUiConnectingAs", state.Username);
        detail.TextAlignment = UITextAlignment.Center;
        UIButton cancel = UIKitFactory.Button(
            AppStrings.Get("IosUiCancel"),
            UIButtonConfiguration.PlainButtonConfiguration,
            store.CancelAuthentication);
        container.AddArrangedSubview(indicator);
        container.AddArrangedSubview(heading);
        container.AddArrangedSubview(detail);
        container.AddArrangedSubview(cancel);
        contentStack.AddArrangedSubview(container);
    }

    /// <summary>Builds the connected identity dashboard and every account-scoped destination.</summary>
    /// <param name="state">The connected snapshot.</param>
    private void BuildConnected(HomePresentationState state)
    {
        string username = state.Username ?? AppStrings.Get("IosUiUnknown");
        var heading = UIKitFactory.Label(UIFontTextStyle.Title1);
        heading.Text = username;
        heading.AccessibilityTraits |= UIAccessibilityTrait.Header;
        contentStack.AddArrangedSubview(heading);
        contentStack.AddArrangedSubview(CreateStatusRow(
            AppStrings.Get("status_connected"),
            AppStrings.Get("IosUiConnectionReadyDetail"),
            "checkmark.circle.fill",
            UIColor.SystemGreen,
            AccessibilityIdentifiers.HomeConnectionStatus));

        var destinationsHeading = UIKitFactory.Label(UIFontTextStyle.Headline);
        destinationsHeading.Text = AppStrings.Get("IosUiCommunity");
        destinationsHeading.AccessibilityTraits |= UIAccessibilityTrait.Header;
        contentStack.AddArrangedSubview(destinationsHeading);
        contentStack.AddArrangedSubview(CreateCommunityList(state));

        var filesHeading = UIKitFactory.Label(UIFontTextStyle.Headline);
        filesHeading.Text = AppStrings.Get("IosUiFilesAndSharing");
        filesHeading.AccessibilityTraits |= UIAccessibilityTrait.Header;
        contentStack.AddArrangedSubview(filesHeading);
        contentStack.AddArrangedSubview(CreateActionButton(
            AppStrings.Get("IosUiSharedDocuments"),
            AppStrings.Format("IosUiShareCounts", state.SharedFileCount, state.SharedDirectoryCount),
            "folder.badge.person.crop",
            AccessibilityIdentifiers.HomeSharingStatus,
            OpenSharedDocuments,
            AppStrings.Get("IosUiOpenInFiles")));
    }

    /// <summary>Opens the Files app on the sandbox Documents folder holding shares and finished downloads.</summary>
    /// <remarks>
    /// <c>UIFileSharingEnabled</c> already publishes this folder to Files; the <c>shareddocuments</c> scheme is how
    /// an app asks Files to open its own published folder, and it takes the same path the sandbox URL carries.
    /// </remarks>
    private void OpenSharedDocuments()
    {
        const string FilePrefix = "file://";
        using NSUrl fileUrl = NSUrl.FromFilename(documentsPath);
        string absolute = fileUrl.AbsoluteString ?? string.Empty;
        NSUrl? filesUrl = absolute.StartsWith(FilePrefix, StringComparison.Ordinal)
            ? NSUrl.FromString("shareddocuments://" + absolute[FilePrefix.Length..])
            : null;
        if (filesUrl is null)
        {
            toaster.ShowToastLong(AppStrings.Get("IosUiOpenInFilesFailed"));
            return;
        }

        using (filesUrl)
        {
            UIApplication.SharedApplication.OpenUrl(
                filesUrl,
                new UIApplicationOpenUrlOptions(),
                opened =>
                {
                    if (!opened)
                    {
                        toaster.ShowToastLong(AppStrings.Get("IosUiOpenInFilesFailed"));
                    }
                });
        }
    }

    /// <summary>Builds retained offline identity, reconnect recovery, and locally usable destinations.</summary>
    /// <param name="state">The disconnected snapshot.</param>
    private void BuildDisconnected(HomePresentationState state)
    {
        var heading = UIKitFactory.Label(UIFontTextStyle.Title1);
        heading.Text = state.Username ?? AppStrings.Get("app_name");
        heading.AccessibilityTraits |= UIAccessibilityTrait.Header;
        contentStack.AddArrangedSubview(heading);
        contentStack.AddArrangedSubview(CreateStatusRow(
            AppStrings.Get("status_disconnected"),
            AppStrings.Get("IosUiDisconnectedDetail"),
            "wifi.slash",
            UIColor.SystemOrange,
            AccessibilityIdentifiers.HomeConnectionStatus));
        if (!string.IsNullOrWhiteSpace(state.ErrorMessage))
        {
            var error = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.SystemRed);
            error.Text = state.ErrorMessage;
            error.AccessibilityIdentifier = AccessibilityIdentifiers.HomeLoginError;
            contentStack.AddArrangedSubview(error);
        }

        UIButton reconnect = UIKitFactory.Button(
            AppStrings.Get("IosUiReconnect"),
            UIButtonConfiguration.FilledButtonConfiguration,
            () => _ = store.ReconnectAsync(),
            "arrow.clockwise")
            .Identified(AccessibilityIdentifiers.HomeReconnect);
        reconnect.Enabled = !state.IsBusy;
        contentStack.AddArrangedSubview(reconnect);
    }

    /// <summary>
    /// Creates the connected community destinations. Account, Settings, About, and Legal Notices are reached
    /// through the navigation-bar Settings action, which stays available in every account state.
    /// </summary>
    /// <param name="state">The current account snapshot and unread totals.</param>
    /// <returns>A native vertical list of route buttons.</returns>
    private UIStackView CreateCommunityList(HomePresentationState state)
    {
        var list = UIKitFactory.VerticalStack(0);
        list.AddArrangedSubview(CreateRouteButton(
            AppStrings.Get("IosUiMessagesTitle"),
            state.UnreadMessageCount > 0
                ? AppStrings.Format("unread_count", state.UnreadMessageCount)
                : AppStrings.Get("IosUiMessagesDetail"),
            "message",
            AccessibilityIdentifiers.HomeMessages,
            new AppRoute.Messages()));
        list.AddArrangedSubview(CreateRouteButton(
            AppStrings.Get("IosUiChatroomsTitle"),
            state.UnreadRoomCount > 0
                ? AppStrings.Format("unread_count", state.UnreadRoomCount)
                : AppStrings.Get("IosUiChatroomsDetail"),
            "bubble.left.and.bubble.right",
            AccessibilityIdentifiers.HomeChatrooms,
            new AppRoute.Chatrooms()));
        list.AddArrangedSubview(CreateRouteButton(
            AppStrings.Get("IosUiUsersTitle"),
            AppStrings.Get("IosUiUsersDetail"),
            "person.2",
            AccessibilityIdentifiers.HomeUsers,
            new AppRoute.Users()));
        return list;
    }

    /// <summary>Creates a self-sizing content-plane route with stable icon, text, and disclosure columns.</summary>
    /// <param name="title">The localized destination name.</param>
    /// <param name="subtitle">The localized status or purpose.</param>
    /// <param name="symbol">The reinforcing SF Symbol.</param>
    /// <param name="identifier">The stable privacy-safe automation identifier.</param>
    /// <param name="route">The typed route to request.</param>
    /// <returns>A full-width native button whose text remains leading-aligned across title lengths and text sizes.</returns>
    private UIButton CreateRouteButton(
        string title,
        string subtitle,
        string symbol,
        string identifier,
        AppRoute route) =>
        CreateActionButton(title, subtitle, symbol, identifier, () => router.Navigate(route));

    /// <summary>Creates a content-plane row that runs an action instead of pushing a destination.</summary>
    /// <param name="title">The localized row name.</param>
    /// <param name="subtitle">The localized status or purpose.</param>
    /// <param name="symbol">The reinforcing SF Symbol.</param>
    /// <param name="identifier">The stable privacy-safe automation identifier.</param>
    /// <param name="action">The work performed on tap.</param>
    /// <param name="accessibilityHint">An optional hint describing a non-navigational outcome.</param>
    /// <returns>A full-width native button matching the route rows around it.</returns>
    private UIButton CreateActionButton(
        string title,
        string subtitle,
        string symbol,
        string identifier,
        Action action,
        string? accessibilityHint = null)
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.TranslatesAutoresizingMaskIntoConstraints = false;
        button.AccessibilityLabel = $"{title}. {subtitle}";
        button.AccessibilityIdentifier = identifier;
        button.AccessibilityHint = accessibilityHint;
        button.TouchUpInside += (_, _) => action();
        button.TouchDown += (_, _) => button.BackgroundColor = UIColor.TertiarySystemFill;
        void RestoreBackground(object? sender, EventArgs args) => button.BackgroundColor = UIColor.Clear;
        button.TouchUpInside += RestoreBackground;
        button.TouchUpOutside += RestoreBackground;
        button.TouchCancel += RestoreBackground;
        button.TouchDragExit += RestoreBackground;

        var icon = new UIImageView(UIImage.GetSystemImage(symbol))
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        var titleLabel = UIKitFactory.Label(UIFontTextStyle.Body, UIColor.Link);
        titleLabel.Text = title;
        titleLabel.TextAlignment = UITextAlignment.Natural;
        var subtitleLabel = UIKitFactory.Label(UIFontTextStyle.Subheadline, UIColor.SecondaryLabel);
        subtitleLabel.Text = subtitle;
        subtitleLabel.TextAlignment = UITextAlignment.Natural;
        var text = UIKitFactory.VerticalStack(1);
        text.AddArrangedSubview(titleLabel);
        text.AddArrangedSubview(subtitleLabel);
        text.SetContentCompressionResistancePriority((float)UILayoutPriority.DefaultLow, UILayoutConstraintAxis.Horizontal);

        var disclosure = new UIImageView(UIImage.GetSystemImage("chevron.forward"))
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = UIColor.TertiaryLabel,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        button.AddSubviews(icon, text, disclosure);

        // A stack view accepts touches by default, so the title and subtitle would absorb every tap over the
        // row and leave only the narrow gaps beside the chevron actually pressing the button. Image views
        // already decline touches; making the whole content non-interactive gives the row one hit region.
        foreach (UIView content in new UIView[] { icon, text, disclosure })
        {
            content.UserInteractionEnabled = false;
        }

        NSLayoutConstraint.ActivateConstraints([
            icon.LeadingAnchor.ConstraintEqualTo(button.LayoutMarginsGuide.LeadingAnchor),
            icon.TopAnchor.ConstraintEqualTo(text.TopAnchor),
            icon.WidthAnchor.ConstraintEqualTo(28),
            icon.HeightAnchor.ConstraintEqualTo(28),
            text.LeadingAnchor.ConstraintEqualTo(icon.TrailingAnchor, 12),
            text.TopAnchor.ConstraintGreaterThanOrEqualTo(button.TopAnchor, 10),
            text.BottomAnchor.ConstraintLessThanOrEqualTo(button.BottomAnchor, -10),
            text.CenterYAnchor.ConstraintEqualTo(button.CenterYAnchor),
            disclosure.LeadingAnchor.ConstraintGreaterThanOrEqualTo(text.TrailingAnchor, 8),
            disclosure.TrailingAnchor.ConstraintEqualTo(button.LayoutMarginsGuide.TrailingAnchor),
            disclosure.CenterYAnchor.ConstraintEqualTo(button.CenterYAnchor),
            disclosure.WidthAnchor.ConstraintEqualTo(10),
            disclosure.HeightAnchor.ConstraintEqualTo(18),
        ]);
        button.HeightAnchor.ConstraintGreaterThanOrEqualTo(52).Active = true;
        return button;
    }

    /// <summary>Creates a non-color-only status summary with symbol, title, and explanatory detail.</summary>
    /// <param name="title">The localized state title.</param>
    /// <param name="detail">The localized state explanation.</param>
    /// <param name="symbolName">The reinforcing SF Symbol name.</param>
    /// <param name="color">A semantic color used only as reinforcement.</param>
    /// <param name="identifier">The stable automation identifier.</param>
    /// <returns>A self-sizing accessible status view.</returns>
    private static UIView CreateStatusRow(
        string title,
        string detail,
        string symbolName,
        UIColor color,
        string identifier)
    {
        var row = UIKitFactory.VerticalStack(4);
        var titleRow = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Horizontal,
            Alignment = UIStackViewAlignment.Center,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 8,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var symbol = new UIImageView(UIImage.GetSystemImage(symbolName))
        {
            TintColor = color,
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        var titleLabel = UIKitFactory.Label(UIFontTextStyle.Headline);
        titleLabel.Text = title;
        var detailLabel = UIKitFactory.Label(UIFontTextStyle.Subheadline, UIColor.SecondaryLabel);
        detailLabel.Text = detail;
        titleRow.AddArrangedSubview(symbol);
        titleRow.AddArrangedSubview(titleLabel);
        row.AddArrangedSubview(titleRow);
        row.AddArrangedSubview(detailLabel);
        row.IsAccessibilityElement = true;
        row.AccessibilityLabel = $"{title}. {detail}";
        row.AccessibilityIdentifier = identifier;
        symbol.WidthAnchor.ConstraintEqualTo(24).Active = true;
        symbol.HeightAnchor.ConstraintEqualTo(24).Active = true;
        return row;
    }

    /// <summary>Creates the 44-point secure-entry visibility control without exposing password content.</summary>
    /// <returns>An icon button with a descriptive VoiceOver label.</returns>
    private UIButton CreatePasswordVisibilityButton()
    {
        string title = AppStrings.Get(passwordVisible ? "IosUiHidePassword" : "IosUiShowPassword");
        UIButton button = UIButton.FromType(UIButtonType.System);
        button.SetImage(UIImage.GetSystemImage(passwordVisible ? "eye.slash" : "eye"), UIControlState.Normal);
        button.Frame = new CoreGraphics.CGRect(0, 0, 44, 44);
        button.AccessibilityLabel = title;
        button.AccessibilityIdentifier = AccessibilityIdentifiers.HomePasswordVisibility;
        button.TouchUpInside += (_, _) =>
        {
            string current = passwordField?.Text ?? string.Empty;
            passwordVisible = !passwordVisible;
            if (passwordField is not null)
            {
                passwordField.SecureTextEntry = !passwordVisible;
                passwordField.Text = current;
                passwordField.RightView = CreatePasswordVisibilityButton();
            }
        };
        return button;
    }

    /// <summary>Clears stale validation and updates primary-action capability after input changes.</summary>
    /// <param name="sender">The edited credential field.</param>
    /// <param name="args">Unused event data.</param>
    private void OnCredentialEdited(object? sender, EventArgs args)
    {
        store.ClearError();
        if (primaryButton is not null)
        {
            primaryButton.Enabled = CanSubmit(usernameField?.Text, passwordField?.Text);
        }
    }

    /// <summary>Determines whether the current credential pair satisfies local validation.</summary>
    /// <param name="username">The proposed user name.</param>
    /// <param name="password">The proposed password.</param>
    /// <returns><see langword="true"/> when an authentication attempt may start.</returns>
    private static bool CanSubmit(string? username, string? password) =>
        HomePresentationStore.ValidateCredentials(username, password) is null;

    /// <summary>Determines whether a username is valid independently of password presence.</summary>
    /// <param name="username">The proposed user name.</param>
    /// <returns><see langword="true"/> when validation should move to the password field.</returns>
    private static bool IsUsernameValid(string? username) =>
        HomePresentationStore.ValidateCredentials(username, "validation-only") is null;

    /// <summary>Validates, focuses the affected field, and starts one bounded sign-in attempt.</summary>
    private void SubmitCredentials()
    {
        string username = usernameField?.Text ?? string.Empty;
        string password = passwordField?.Text ?? string.Empty;
        string? validation = HomePresentationStore.ValidateCredentials(username, password);
        if (validation is not null)
        {
            _ = store.SignInAsync(username, password);
            errorLabel?.SetNeedsLayout();
            UITextField? affectedField = IsUsernameValid(username) ? passwordField : usernameField;
            affectedField?.BecomeFirstResponder();
            if (affectedField is not null)
            {
                affectedField.AccessibilityHint = validation;
            }
            AccessibilityExtensions.Announce(validation);
            return;
        }

        usernameField?.ResignFirstResponder();
        passwordField?.ResignFirstResponder();
        primaryButton!.Enabled = false;
        _ = store.SignInAsync(username, password);
    }

    /// <summary>Announces only meaningful account-state transitions and suppresses duplicate speech.</summary>
    /// <param name="state">The newly rendered account snapshot.</param>
    private void AnnounceStateIfChanged(HomePresentationState state)
    {
        string current = state.AccountState switch
        {
            HomeAccountState.SignedOut when state.ErrorMessage is not null => state.ErrorMessage,
            HomeAccountState.SignedOut => AppStrings.Get("IosUiSignedOutAnnouncement"),
            HomeAccountState.Connecting when state.IsReconnectAttempt => AppStrings.Get("IosUiReconnecting"),
            HomeAccountState.Connecting => AppStrings.Get("status_connecting"),
            HomeAccountState.Connected => AppStrings.Get("IosUiConnectedAnnouncement"),
            HomeAccountState.Disconnected when state.ErrorMessage is not null => state.ErrorMessage,
            HomeAccountState.Disconnected => AppStrings.Get("IosUiDisconnectedAnnouncement"),
            _ => string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(current) && current != lastAccessibilityState)
        {
            lastAccessibilityState = current;
            AccessibilityExtensions.Announce(current);
        }
    }
}
