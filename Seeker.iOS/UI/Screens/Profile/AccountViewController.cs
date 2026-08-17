using AnimaSeek.iOS.Services;
using AnimaSeek.iOS.UI.Accessibility;
using AnimaSeek.iOS.UI.App;
using AnimaSeek.iOS.UI.Components;
using Common;
using CoreGraphics;
using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace AnimaSeek.iOS.UI.Screens.Profile;

/// <summary>
/// Presents native own-profile editing, password changes, a privilege summary that opens the Privileges
/// screen, and portable data export as an inset-grouped form with one filled primary action.
/// </summary>
internal sealed class AccountViewController : UIViewController
{
    private const int MaximumBiographyCharacters = 2_000;
    private readonly IAppRouter router;
    private readonly IosAccountService service;
    private readonly UITableView table = new(CGRect.Empty, UITableViewStyle.InsetGrouped)
    {
        KeyboardDismissMode = UIScrollViewKeyboardDismissMode.Interactive,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };
    private readonly UIStackView statusRow = new()
    {
        Axis = UILayoutConstraintAxis.Horizontal,
        Alignment = UIStackViewAlignment.Center,
        Spacing = 8,
        LayoutMarginsRelativeArrangement = true,
        DirectionalLayoutMargins = new NSDirectionalEdgeInsets(8, 20, 8, 20),
        TranslatesAutoresizingMaskIntoConstraints = false,
    };
    private readonly UIImageView profileImage = new()
    {
        ContentMode = UIViewContentMode.ScaleAspectFit,
        BackgroundColor = UIColor.SecondarySystemGroupedBackground,
        ClipsToBounds = true,
        TranslatesAutoresizingMaskIntoConstraints = false,
        IsAccessibilityElement = true,
    };
    private readonly UILabel imageNameLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
    private readonly UITextView biographyView = new()
    {
        BackgroundColor = UIColor.Clear,
        Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body),
        AdjustsFontForContentSizeCategory = true,
        ScrollEnabled = false,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };
    private readonly UILabel statusLabel = UIKitFactory.Label(UIFontTextStyle.Footnote, UIColor.SecondaryLabel);
    private readonly UIActivityIndicatorView progressIndicator = new(UIActivityIndicatorViewStyle.Medium)
    {
        HidesWhenStopped = true,
        TranslatesAutoresizingMaskIntoConstraints = false,
        IsAccessibilityElement = false,
    };
    private UIButton saveButton = null!;
    private UITableViewCell chooseImageCell = null!;
    private UITableViewCell clearImageCell = null!;
    private UITableViewCell viewProfileCell = null!;
    private UITableViewCell changePasswordCell = null!;
    private UITableViewCell privilegesCell = null!;
    private UITableViewCell exportCell = null!;
    private UITableViewCell signOutCell = null!;
    private TableSection[] tableSections = [];
    private AccountTableSource? tableSource;
    private IosAccountProfileSnapshot? savedProfile;
    private byte[]? pendingImageBytes;
    private string? pendingImageName;
    private UIDocumentPickerDelegate? imagePickerDelegate;
    private UITextViewDelegate? biographyDelegate;
    private CancellationTokenSource? lifetimeCancellation;
    private int? remainingPrivilegeSeconds;
    private bool busy;

    /// <summary>Creates the Account screen over the typed router and account facade.</summary>
    /// <param name="router">The application router used to open the own-profile viewer.</param>
    /// <param name="service">The facade for account state and commands.</param>
    public AccountViewController(IAppRouter router, IosAccountService service)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Title = AppStrings.Get("IosUiAccountTitle");
    }

    /// <inheritdoc/>
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemGroupedBackground;
        View.AccessibilityIdentifier = "account.screen";
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        biographyDelegate = new BiographyTextViewDelegate(this);
        biographyView.Delegate = biographyDelegate;
        service.SessionStateChanged += OnSessionStateChanged;
        biographyView.AccessibilityIdentifier = "account.biography";
        biographyView.AccessibilityLabel = AppStrings.Get("IosUiAccountBiography");
        profileImage.AccessibilityIdentifier = "account.profile-image";
        imageNameLabel.AccessibilityIdentifier = "account.image-name";
        statusLabel.Hidden = true;
        statusLabel.IsAccessibilityElement = true;
        statusLabel.AccessibilityIdentifier = "account.status";
        ConfigureLayout();
        lifetimeCancellation = new CancellationTokenSource();
        _ = LoadAsync(lifetimeCancellation.Token);
    }

    /// <inheritdoc/>
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        UpdateNavigationGuard();
        if (savedProfile is not null)
        {
            // Returning from the Privileges screen is the moment the summary most likely changed.
            _ = RefreshPrivilegeSummaryAsync();
        }
    }

    /// <inheritdoc/>
    public override void ViewWillDisappear(bool animated)
    {
        if (IsMovingFromParentViewController && IsDirty)
        {
            // The interactive pop gesture is disabled while dirty, but this guard covers programmatic stack changes.
            RestoreSavedDraft();
        }

        base.ViewWillDisappear(animated);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            service.SessionStateChanged -= OnSessionStateChanged;
            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
            imagePickerDelegate = null;
            biographyDelegate = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Constrains biography input, refreshes the dirty-state affordances, and re-measures the row.</summary>
    /// <param name="textView">The editable biography control.</param>
    private void BiographyChanged(UITextView textView)
    {
        string biography = textView.Text ?? string.Empty;
        if (biography.Length > MaximumBiographyCharacters)
        {
            textView.Text = biography[..MaximumBiographyCharacters];
            ShowStatus(
                AppStrings.Get("IosUiAccountBiographyLimit"),
                isError: true);
        }

        UpdateControls();

        // Re-measures the growing biography row without reloading it, which would end editing.
        table.BeginUpdates();
        table.EndUpdates();
    }

    /// <summary>Builds the static inset-grouped account form and the persistent bottom status area.</summary>
    private void ConfigureLayout()
    {
        UIKitFactory.ApplyRoundedContentCorners(profileImage);
        imageNameLabel.TextAlignment = UITextAlignment.Center;
        imageNameLabel.LineBreakMode = UILineBreakMode.MiddleTruncation;

        saveButton = UIKitFactory.Button(
            AppStrings.Get("IosUiSaveProfile"),
            UIButtonConfiguration.FilledButtonConfiguration,
            () => _ = SaveProfileAsync(),
            "checkmark");
        saveButton.AccessibilityIdentifier = "account.profile.save";

        chooseImageCell = ActionRow(
            AppStrings.Get("IosUiAccountChooseImage"), "photo.on.rectangle", "account.image.choose");
        clearImageCell = ActionRow(
            AppStrings.Get("IosUiAccountClearImage"), "trash", "account.image.clear");
        viewProfileCell = ActionRow(
            AppStrings.Get("IosUiViewMyProfile"), "person.crop.circle", "account.profile.view");
        changePasswordCell = ActionRow(
            AppStrings.Get("IosUiChangePassword"), "key", "account.password.change");
        privilegesCell = ValueNavigationRow(
            AppStrings.Get("IosUiPrivileges"), "star", "account.privileges");
        exportCell = ActionRow(
            AppStrings.Get("IosUiExportData"), "square.and.arrow.up", "account.export");
        signOutCell = ActionRow(
            AppStrings.Get("logout"), "rectangle.portrait.and.arrow.right", "account.sign-out", UIColor.SystemRed);

        tableSections =
        [
            new TableSection(
                AppStrings.Get("IosUiAccountProfile"),
                AppStrings.Get("IosUiAccountImageHelp"),
                [
                    new TableRow(ImagePreviewRow()),
                    new TableRow(chooseImageCell, PresentImagePicker),
                    new TableRow(clearImageCell, ClearPendingImage),
                    new TableRow(viewProfileCell, ViewOwnProfile),
                ]),
            new TableSection(
                AppStrings.Get("IosUiAccountBiography"),
                AppStrings.Get("IosUiAccountBiographyHelp"),
                [new TableRow(BiographyRow())]),
            new TableSection(null, null, [new TableRow(SaveRow())]),
            new TableSection(
                null,
                AppStrings.Get("IosUiChangePasswordHelp"),
                [new TableRow(changePasswordCell, PresentPasswordForm)]),
            new TableSection(
                null,
                null,
                [new TableRow(privilegesCell, () => router.Navigate(new AppRoute.Privileges()))]),
            new TableSection(
                null,
                AppStrings.Get("IosUiExportDataHelp"),
                [new TableRow(exportCell, () => _ = ExportAsync())]),
            new TableSection(
                null,
                AppStrings.Get("IosUiSignOutMessage"),
                [new TableRow(signOutCell, ConfirmSignOut)]),
        ];

        tableSource = new AccountTableSource(this);
        table.Source = tableSource;
        table.RowHeight = UITableView.AutomaticDimension;
        table.EstimatedRowHeight = 64;

        statusRow.AddArrangedSubview(progressIndicator);
        statusRow.AddArrangedSubview(statusLabel);
        statusRow.Hidden = true;

        var rootStack = UIKitFactory.VerticalStack(0);
        rootStack.AddArrangedSubview(table);
        rootStack.AddArrangedSubview(statusRow);

        UIView rootView = View ?? throw new InvalidOperationException("The Account view is unavailable.");
        rootView.AddSubview(rootStack);
        NSLayoutConstraint.ActivateConstraints(
        [
            rootStack.TopAnchor.ConstraintEqualTo(rootView.TopAnchor),
            rootStack.LeadingAnchor.ConstraintEqualTo(rootView.LeadingAnchor),
            rootStack.TrailingAnchor.ConstraintEqualTo(rootView.TrailingAnchor),
            rootStack.BottomAnchor.ConstraintEqualTo(rootView.KeyboardLayoutGuide.TopAnchor),
        ]);
    }

    /// <summary>Creates the non-interactive row presenting the pending profile image and its file name.</summary>
    /// <returns>A self-sizing preview cell.</returns>
    private UITableViewCell ImagePreviewRow()
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
        };
        cell.ContentView.AddSubview(profileImage);
        cell.ContentView.AddSubview(imageNameLabel);
        NSLayoutConstraint.ActivateConstraints(
        [
            profileImage.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor, 12),
            profileImage.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.LeadingAnchor),
            profileImage.TrailingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.TrailingAnchor),
            profileImage.HeightAnchor.ConstraintEqualTo(200),
            imageNameLabel.TopAnchor.ConstraintEqualTo(profileImage.BottomAnchor, 8),
            imageNameLabel.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.LeadingAnchor),
            imageNameLabel.TrailingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.TrailingAnchor),
            imageNameLabel.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor, -12),
        ]);
        return cell;
    }

    /// <summary>Creates the row hosting the editable biography without ending editing on re-measure.</summary>
    /// <returns>A self-sizing editor cell.</returns>
    private UITableViewCell BiographyRow()
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
        };
        cell.ContentView.AddSubview(biographyView);
        NSLayoutConstraint.ActivateConstraints(
        [
            biographyView.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor, 4),
            biographyView.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor, -4),
            biographyView.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.LeadingAnchor),
            biographyView.TrailingAnchor.ConstraintEqualTo(cell.ContentView.LayoutMarginsGuide.TrailingAnchor),
            biographyView.HeightAnchor.ConstraintGreaterThanOrEqualTo(132),
        ]);
        return cell;
    }

    /// <summary>Creates the transparent row hosting the screen's single filled primary action.</summary>
    /// <returns>A row whose card chrome is removed so only the save button shows.</returns>
    private UITableViewCell SaveRow()
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            SelectionStyle = UITableViewCellSelectionStyle.None,
            BackgroundConfiguration = UIBackgroundConfiguration.ClearConfiguration,
        };
        cell.ContentView.AddSubview(saveButton);
        NSLayoutConstraint.ActivateConstraints(
        [
            saveButton.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor),
            saveButton.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor),
            saveButton.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LeadingAnchor),
            saveButton.TrailingAnchor.ConstraintEqualTo(cell.ContentView.TrailingAnchor),
        ]);
        return cell;
    }

    /// <summary>Creates one static action row with button semantics and a stable identifier.</summary>
    /// <param name="title">Visible action name.</param>
    /// <param name="symbol">Reinforcing SF Symbol.</param>
    /// <param name="identifier">Stable nonlocalized identifier.</param>
    /// <param name="tint">Optional emphasis color for destructive actions.</param>
    /// <returns>A pre-built row that activates on selection.</returns>
    private static UITableViewCell ActionRow(string title, string symbol, string identifier, UIColor? tint = null)
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null)
        {
            AccessibilityIdentifier = identifier,
            AccessibilityLabel = title,
        };
        cell.AccessibilityTraits |= UIAccessibilityTrait.Button;
        UIListContentConfiguration content = cell.DefaultContentConfiguration;
        content.Text = title;
        content.Image = UIImage.GetSystemImage(symbol);
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        if (tint is not null)
        {
            content.TextProperties.Color = tint;
            content.ImageProperties.TintColor = tint;
        }

        cell.ContentConfiguration = content;
        return cell;
    }

    /// <summary>Creates the disclosure row pairing a destination name with its current summary value.</summary>
    /// <param name="title">Visible destination name.</param>
    /// <param name="symbol">Reinforcing SF Symbol.</param>
    /// <param name="identifier">Stable nonlocalized identifier.</param>
    /// <returns>A pre-built row that navigates on selection and carries a trailing value.</returns>
    private static UITableViewCell ValueNavigationRow(string title, string symbol, string identifier)
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Value1, null)
        {
            AccessibilityIdentifier = identifier,
            AccessibilityLabel = title,
            Accessory = UITableViewCellAccessory.DisclosureIndicator,
        };
        cell.AccessibilityTraits |= UIAccessibilityTrait.Button;
        UIListContentConfiguration content = UIListContentConfiguration.ValueCellConfiguration;
        content.Text = title;
        content.Image = UIImage.GetSystemImage(symbol);
        content.TextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.TextProperties.AdjustsFontForContentSizeCategory = true;
        content.TextProperties.NumberOfLines = 0;
        content.SecondaryTextProperties.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        content.SecondaryTextProperties.AdjustsFontForContentSizeCategory = true;
        content.SecondaryTextProperties.Color = UIColor.SecondaryLabel;
        content.SecondaryTextProperties.NumberOfLines = 0;
        cell.ContentConfiguration = content;
        return cell;
    }

    /// <summary>Applies interactive availability to a static row for touch and assistive technology alike.</summary>
    /// <param name="cell">The pre-built action row.</param>
    /// <param name="enabled">Whether the row's action can currently run.</param>
    private static void SetRowEnabled(UITableViewCell cell, bool enabled)
    {
        if (cell.UserInteractionEnabled == enabled)
        {
            return;
        }

        cell.UserInteractionEnabled = enabled;
        if (cell.ContentConfiguration is UIListContentConfiguration content)
        {
            content.TextProperties.Color = enabled ? UIColor.Label : UIColor.TertiaryLabel;
            content.ImageProperties.TintColor = enabled ? null : UIColor.TertiaryLabel;
            cell.ContentConfiguration = content;
        }

        if (enabled)
        {
            cell.AccessibilityTraits &= ~UIAccessibilityTrait.NotEnabled;
        }
        else
        {
            cell.AccessibilityTraits |= UIAccessibilityTrait.NotEnabled;
        }
    }

    /// <summary>Loads the initial profile while preserving an explicit nonblocking progress state.</summary>
    /// <param name="cancellationToken">Cancels when the controller is disposed.</param>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        SetBusy(true, AppStrings.Get("IosUiLoadingAccount"));
        try
        {
            savedProfile = await service.GetProfileAsync(cancellationToken);
            ApplyProfile(savedProfile);
            await RefreshPrivilegeSummaryAsync();
            cancellationToken.ThrowIfCancellationRequested();
            AccessibilityExtensions.FocusScreen(biographyView);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ShowStatus(
                AppStrings.Get("IosUiAccountLoadFailed"),
                isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Applies one coherent saved profile to the editable draft.</summary>
    /// <param name="profile">The saved detached profile.</param>
    private void ApplyProfile(IosAccountProfileSnapshot profile)
    {
        biographyView.Text = profile.Biography;
        pendingImageBytes = profile.ImageBytes?.ToArray();
        pendingImageName = profile.ImageName;
        UpdateImagePresentation();
        UpdateControls();
    }

    /// <summary>Opens a system Files picker restricted to image content.</summary>
    private void PresentImagePicker()
    {
        if (busy)
        {
            return;
        }

        var picker = new UIDocumentPickerViewController([UTTypes.Image], asCopy: true)
        {
            AllowsMultipleSelection = false,
        };
        imagePickerDelegate = new ImagePickerDelegate(this);
        picker.Delegate = imagePickerDelegate;
        PresentViewController(picker, true, null);
    }

    /// <summary>Reads and validates a selected Files image without committing the profile.</summary>
    /// <param name="url">The copied or security-scoped local image URL.</param>
    private async Task ReceiveImageAsync(NSUrl url)
    {
        SetBusy(true, AppStrings.Get("IosUiReadingImage"));
        bool scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            string path = url.Path ?? throw new InvalidDataException("The selected image has no local path.");
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > IosAccountService.MaximumImageBytes)
            {
                throw new InvalidDataException("The selected image must be no larger than 5 MB.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(path);
            UIImage? decoded = UIImage.LoadFromData(NSData.FromArray(bytes));
            if (decoded is null)
            {
                throw new InvalidDataException("The selected file is not a supported image.");
            }

            pendingImageBytes = bytes;
            pendingImageName = info.Name;
            UpdateImagePresentation();
            UpdateControls();
            ShowStatus(AppStrings.Get("IosUiImageReady"), isError: false);
        }
        catch
        {
            ShowStatus(
                AppStrings.Get("IosUiImageInvalid"),
                isError: true);
        }
        finally
        {
            if (scoped)
            {
                url.StopAccessingSecurityScopedResource();
            }

            imagePickerDelegate = null;
            SetBusy(false);
        }
    }

    /// <summary>Marks the current image for removal while retaining undo through discard.</summary>
    private void ClearPendingImage()
    {
        pendingImageBytes = null;
        pendingImageName = null;
        UpdateImagePresentation();
        UpdateControls();
        ShowStatus(AppStrings.Get("IosUiImageWillClear"), isError: false);
    }

    /// <summary>Commits biography and image together through the atomic account service.</summary>
    private async Task SaveProfileAsync()
    {
        if (!IsDirty || busy)
        {
            return;
        }

        SetBusy(true, AppStrings.Get("IosUiSavingProfile"));
        try
        {
            savedProfile = await service.SaveProfileAsync(
                biographyView.Text,
                pendingImageBytes,
                pendingImageName,
                lifetimeCancellation?.Token ?? default);
            ApplyProfile(savedProfile);
            ShowStatus(AppStrings.Get("IosUiProfileSaved"), isError: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ShowStatus(
                AppStrings.Get("IosUiProfileSaveFailed"),
                isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Opens the current account through the existing read-only profile destination.</summary>
    private void ViewOwnProfile()
    {
        string? username = savedProfile?.Username;
        if (!string.IsNullOrWhiteSpace(username))
        {
            router.Navigate(new AppRoute.UserProfile(username));
        }
    }

    /// <summary>Presents a secure password and confirmation form.</summary>
    private void PresentPasswordForm()
    {
        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiChangePassword"),
            AppStrings.Get("IosUiChangePasswordPrompt"),
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field => ConfigureSecurePasswordField(
            field,
            AppStrings.Get("IosUiNewPassword"),
            "account.password.new"));
        alert.AddTextField(field => ConfigureSecurePasswordField(
            field,
            AppStrings.Get("IosUiConfirmPassword"),
            "account.password.confirm"));
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiCancel"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiChangePassword"),
            UIAlertActionStyle.Default,
            _action => _ = SubmitPasswordAsync(alert)));
        PresentViewController(alert, true, null);
    }

    /// <summary>Validates matching secure fields before issuing the account command.</summary>
    /// <param name="alert">The password form containing both fields.</param>
    private async Task SubmitPasswordAsync(UIAlertController alert)
    {
        string password = alert.TextFields?.ElementAtOrDefault(0)?.Text ?? string.Empty;
        string confirmation = alert.TextFields?.ElementAtOrDefault(1)?.Text ?? string.Empty;
        if (password.Length == 0 || !string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            ShowStatus(
                AppStrings.Get("IosUiPasswordsDoNotMatch"),
                isError: true);
            return;
        }

        SetBusy(true, AppStrings.Get("IosUiChangingPassword"));
        try
        {
            await service.ChangePasswordAsync(password, lifetimeCancellation?.Token ?? default);
            ShowStatus(AppStrings.Get("IosUiPasswordChanged"), isError: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ShowStatus(
                AppStrings.Get("IosUiPasswordChangeFailed"),
                isError: true);
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
            SetBusy(false);
        }
    }

    /// <summary>Fills the privilege summary once the server connection becomes available.</summary>
    /// <param name="sender">The forwarding account facade.</param>
    /// <param name="args">Unused event payload.</param>
    private void OnSessionStateChanged(object? sender, EventArgs args)
    {
        if (!NSThread.IsMain)
        {
            UIApplication.SharedApplication.BeginInvokeOnMainThread(() => OnSessionStateChanged(sender, args));
            return;
        }

        if (remainingPrivilegeSeconds is null && service.IsConnected)
        {
            _ = RefreshPrivilegeSummaryAsync();
        }
    }

    /// <summary>Quietly refreshes the privilege summary value carried by the disclosure row.</summary>
    /// <remarks>
    /// The summary never seizes the form's busy state and never announces failure: the row keeps its
    /// last-known value, and the Privileges screen owns visible privilege errors and recovery.
    /// </remarks>
    private async Task RefreshPrivilegeSummaryAsync()
    {
        try
        {
            remainingPrivilegeSeconds = await service.GetPrivilegesAsync(lifetimeCancellation?.Token ?? default);
        }
        catch
        {
            return;
        }

        UpdatePrivilegeSummary();
    }

    /// <summary>Creates portable data and opens the native activity sheet over its file URL.</summary>
    private async Task ExportAsync()
    {
        if (busy)
        {
            return;
        }

        SetBusy(true, AppStrings.Get("IosUiExportingData"));
        try
        {
            string path = await service.ExportPortableDataAsync(lifetimeCancellation?.Token ?? default);
            var activity = new UIActivityViewController([NSUrl.FromFilename(path)], null);
            if (activity.PopoverPresentationController is { } popover)
            {
                popover.SourceView = exportCell;
                popover.SourceRect = exportCell.Bounds;
            }

            PresentViewController(activity, true, null);
            ShowStatus(AppStrings.Get("IosUiExportReady"), isError: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ShowStatus(
                AppStrings.Get("IosUiExportFailed"),
                isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Confirms credential removal while clearly stating which local content is retained, then returns to
    /// Home so the signed-out state replaces this account editor.
    /// </summary>
    private void ConfirmSignOut()
    {
        UIAlertController alert = UIAlertController.Create(
            AppStrings.Get("IosUiSignOutTitle"),
            AppStrings.Get("IosUiSignOutMessage"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiCancel"),
            UIAlertActionStyle.Cancel,
            null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("logout"),
            UIAlertActionStyle.Destructive,
            _ =>
            {
                service.SignOut();
                NavigationController?.PopToRootViewController(animated: true);
            }));
        PresentViewController(alert, animated: true, completionHandler: null);
    }

    /// <summary>Shows a discard confirmation when leaving with unsaved profile edits.</summary>
    private void ConfirmDiscard()
    {
        if (!IsDirty)
        {
            NavigationController?.PopViewController(true);
            return;
        }

        var alert = UIAlertController.Create(
            AppStrings.Get("IosUiDiscardProfileTitle"),
            AppStrings.Get("IosUiDiscardProfileMessage"),
            UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create(AppStrings.Get("IosUiKeepEditing"), UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create(
            AppStrings.Get("IosUiDiscardChanges"),
            UIAlertActionStyle.Destructive,
            _ =>
            {
                RestoreSavedDraft();
                NavigationController?.PopViewController(true);
            }));
        PresentViewController(alert, true, null);
    }

    /// <summary>Projects the saved profile back into the editable draft without writing storage.</summary>
    private void RestoreSavedDraft()
    {
        if (savedProfile is not null)
        {
            ApplyProfile(savedProfile);
        }
    }

    /// <summary>Configures navigation interception only while unsaved edits exist.</summary>
    private void UpdateNavigationGuard()
    {
        if (NavigationController is not { ViewControllers.Length: > 1 } navigation)
        {
            return;
        }

        navigation.InteractivePopGestureRecognizer!.Enabled = !IsDirty && !busy;
        NavigationItem.HidesBackButton = IsDirty;
        NavigationItem.LeftBarButtonItem = IsDirty
            ? new UIBarButtonItem(
                UIImage.GetSystemImage("chevron.backward")!,
                UIBarButtonItemStyle.Plain,
                (_, _) => ConfirmDiscard())
            {
                AccessibilityLabel = AppStrings.Get("back_desc"),
                AccessibilityHint = AppStrings.Get("IosUiUnsavedChangesHint"),
                AccessibilityIdentifier = "account.back",
            }
            : null;
    }

    /// <summary>Updates image, name, and accessible no-image presentation from the pending draft.</summary>
    private void UpdateImagePresentation()
    {
        UIImage? image = pendingImageBytes is { Length: > 0 }
            ? UIImage.LoadFromData(NSData.FromArray(pendingImageBytes))
            : null;
        profileImage.Image = image;
        profileImage.AccessibilityLabel = image is null
            ? AppStrings.Get("IosUiNoProfileImage")
            : AppStrings.Get("IosUiOwnProfileImage");
        profileImage.AccessibilityIgnoresInvertColors = image is not null;
        imageNameLabel.Text = pendingImageName ?? AppStrings.Get("IosUiNoProfileImage");
    }

    /// <summary>Projects the last-known privilege state into the disclosure row's trailing value.</summary>
    private void UpdatePrivilegeSummary()
    {
        if (privilegesCell.ContentConfiguration is not UIListContentConfiguration content)
        {
            return;
        }

        string value = PrivilegeSummaryValue(remainingPrivilegeSeconds);
        content.SecondaryText = value;
        privilegesCell.ContentConfiguration = content;
        string title = AppStrings.Get("IosUiPrivileges");
        privilegesCell.AccessibilityLabel = value.Length == 0 ? title : $"{title}, {value}";
        table.BeginUpdates();
        table.EndUpdates();
    }

    /// <summary>Compresses raw privilege seconds into a short glanceable row value.</summary>
    /// <param name="seconds">The server-reported remaining seconds, or <see langword="null"/> before any check.</param>
    /// <returns>A concise value, or an empty string while the state is unknown.</returns>
    private static string PrivilegeSummaryValue(int? seconds) => seconds switch
    {
        null => string.Empty,
        0 => AppStrings.Get("IosUiPrivilegesValueNone"),
        < 86_400 => AppStrings.Get("IosUiPrivilegesValueUnderDay"),
        < 172_800 => AppStrings.Get("IosUiPrivilegesValueOneDay"),
        _ => AppStrings.Format("IosUiPrivilegesValueDays", seconds / 86_400),
    };

    /// <summary>Updates dirty, busy, connectivity-independent, and image-dependent control availability.</summary>
    private void UpdateControls()
    {
        if (chooseImageCell is null)
        {
            return;
        }

        saveButton.Enabled = IsDirty && !busy;
        SetRowEnabled(chooseImageCell, !busy);
        SetRowEnabled(clearImageCell, pendingImageBytes is not null && !busy);
        SetRowEnabled(viewProfileCell, !string.IsNullOrWhiteSpace(savedProfile?.Username) && !busy);
        SetRowEnabled(changePasswordCell, !busy);
        SetRowEnabled(privilegesCell, !busy);
        SetRowEnabled(exportCell, !busy);
        biographyView.Editable = !busy;
        UpdateNavigationGuard();
    }

    /// <summary>Displays persistent inline progress or feedback and announces meaningful outcomes once.</summary>
    /// <param name="message">The concise user-facing state.</param>
    /// <param name="isError">Whether to apply semantic error emphasis.</param>
    private void ShowStatus(string message, bool isError)
    {
        statusLabel.Text = message;
        statusLabel.TextColor = isError ? UIColor.SystemRed : UIColor.SecondaryLabel;
        statusLabel.Hidden = false;
        statusRow.Hidden = false;
        statusLabel.AccessibilityTraits = isError ? UIAccessibilityTrait.StaticText : UIAccessibilityTrait.None;
        AccessibilityExtensions.Announce(message);
    }

    /// <summary>Applies one coherent busy state to controls, indicator, and inline status.</summary>
    /// <param name="value">Whether an operation is active.</param>
    /// <param name="message">Optional progress text shown while active.</param>
    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        if (value)
        {
            progressIndicator.StartAnimating();
            statusRow.Hidden = false;
            if (!string.IsNullOrWhiteSpace(message))
            {
                statusLabel.Text = message;
                statusLabel.TextColor = UIColor.SecondaryLabel;
                statusLabel.Hidden = false;
            }
        }
        else
        {
            progressIndicator.StopAnimating();
        }

        UpdateControls();
    }

    /// <summary>Determines whether biography or image content differs from the last saved snapshot.</summary>
    private bool IsDirty => savedProfile is not null &&
        (!string.Equals(biographyView.Text, savedProfile.Biography, StringComparison.Ordinal) ||
         !string.Equals(pendingImageName, savedProfile.ImageName, StringComparison.Ordinal) ||
         !(pendingImageBytes ?? []).AsSpan().SequenceEqual(savedProfile.ImageBytes ?? []));

    /// <summary>Gets whether routing away without the screen's guard would discard a local profile draft.</summary>
    internal bool HasUnsavedChanges => IsDirty;

    /// <summary>Applies secure, Dynamic Type, AutoFill-aware configuration to a password field.</summary>
    /// <param name="field">The alert text field.</param>
    /// <param name="placeholder">Visible field prompt.</param>
    /// <param name="identifier">Stable automation identifier.</param>
    private static void ConfigureSecurePasswordField(UITextField field, string placeholder, string identifier)
    {
        field.Placeholder = placeholder;
        field.AccessibilityLabel = placeholder;
        field.AccessibilityIdentifier = identifier;
        field.SecureTextEntry = true;
        field.TextContentType = UITextContentType.NewPassword;
        field.AutocapitalizationType = UITextAutocapitalizationType.None;
        field.AutocorrectionType = UITextAutocorrectionType.No;
        field.Font = UIKitFactory.PreferredFont(UIFontTextStyle.Body);
        field.AdjustsFontForContentSizeCategory = true;
    }

    /// <summary>One static inset-grouped section of the account form.</summary>
    /// <param name="Header">Optional visible section header.</param>
    /// <param name="Footer">Optional visible section footer.</param>
    /// <param name="Rows">The pre-built rows in presentation order.</param>
    private sealed record TableSection(string? Header, string? Footer, TableRow[] Rows);

    /// <summary>One pre-built form row and its optional activation.</summary>
    /// <param name="Cell">The retained static cell.</param>
    /// <param name="Activate">The action run when the row is selected, when interactive.</param>
    private sealed record TableRow(UITableViewCell Cell, Action? Activate = null);

    /// <summary>Serves the pre-built static form rows so editing and enabled state survive without reloads.</summary>
    /// <param name="owner">The controller that owns the section model.</param>
    private sealed class AccountTableSource(AccountViewController owner) : UITableViewSource
    {
        /// <inheritdoc/>
        public override nint NumberOfSections(UITableView tableView) => owner.tableSections.Length;

        /// <inheritdoc/>
        public override nint RowsInSection(UITableView tableView, nint section) =>
            owner.tableSections[(int)section].Rows.Length;

        /// <inheritdoc/>
        public override string? TitleForHeader(UITableView tableView, nint section) =>
            owner.tableSections[(int)section].Header;

        /// <inheritdoc/>
        public override string? TitleForFooter(UITableView tableView, nint section) =>
            owner.tableSections[(int)section].Footer;

        /// <inheritdoc/>
        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath) =>
            owner.tableSections[indexPath.Section].Rows[indexPath.Row].Cell;

        /// <inheritdoc/>
        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            tableView.DeselectRow(indexPath, true);
            owner.tableSections[indexPath.Section].Rows[indexPath.Row].Activate?.Invoke();
        }
    }

    /// <summary>Forwards optional biography changes without making the controller implement a native protocol.</summary>
    /// <param name="owner">The controller that owns the editable biography.</param>
    private sealed class BiographyTextViewDelegate(AccountViewController owner) : UITextViewDelegate
    {
        /// <inheritdoc/>
        public override void Changed(UITextView textView) => owner.BiographyChanged(textView);
    }

    /// <summary>Retains the image picker callback until selection or cancellation completes.</summary>
    /// <param name="owner">The controller that owns the pending profile-image draft.</param>
    private sealed class ImagePickerDelegate(AccountViewController owner) : UIDocumentPickerDelegate
    {
        /// <inheritdoc/>
        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            NSUrl? url = urls.FirstOrDefault();
            if (url is null)
            {
                owner.ShowStatus(
                    AppStrings.Get("IosUiImageInvalid"),
                    isError: true);
                owner.imagePickerDelegate = null;
                return;
            }

            _ = owner.ReceiveImageAsync(url);
        }

        /// <inheritdoc/>
        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            owner.imagePickerDelegate = null;
    }
}
