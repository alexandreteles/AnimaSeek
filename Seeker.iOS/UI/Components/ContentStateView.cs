using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Describes a native empty, loading, offline, or recoverable-error presentation.</summary>
/// <param name="Title">The localized state title.</param>
/// <param name="Message">The optional localized explanation and next-step guidance.</param>
/// <param name="SymbolName">The optional SF Symbol that reinforces, but does not replace, the text.</param>
/// <param name="ActionTitle">The optional localized recovery action.</param>
/// <param name="Action">The recovery operation.</param>
/// <param name="IsLoading">Whether UIKit should render a native loading configuration.</param>
internal sealed record ContentStatePresentation(
    string Title,
    string? Message = null,
    string? SymbolName = null,
    string? ActionTitle = null,
    Action? Action = null,
    bool IsLoading = false);

/// <summary>Maps presentation state to UIKit's native content-unavailable configuration.</summary>
internal static class ContentStateView
{
    /// <summary>Shows a complete-screen state while no useful content exists.</summary>
    /// <param name="controller">The controller that owns the empty content plane.</param>
    /// <param name="presentation">The localized state presentation.</param>
    public static void Show(UIViewController controller, ContentStatePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(presentation);
        UIContentUnavailableConfiguration configuration = presentation.IsLoading
            ? UIContentUnavailableConfiguration.CreateLoadingConfiguration()
            : UIContentUnavailableConfiguration.CreateEmptyConfiguration();
        configuration.Text = presentation.Title;
        configuration.SecondaryText = presentation.Message;
        configuration.Image = string.IsNullOrWhiteSpace(presentation.SymbolName)
            ? null
            : UIImage.GetSystemImage(presentation.SymbolName);
        if (!string.IsNullOrWhiteSpace(presentation.ActionTitle) && presentation.Action is not null)
        {
            configuration.Button = UIButtonConfiguration.TintedButtonConfiguration;
            configuration.Button.Title = presentation.ActionTitle;
            configuration.ButtonProperties.PrimaryAction = UIAction.Create(_ => presentation.Action());
        }

        controller.ContentUnavailableConfiguration = configuration;
    }

    /// <summary>Restores the controller's normal content presentation.</summary>
    /// <param name="controller">The controller whose state should clear.</param>
    public static void Clear(UIViewController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        controller.ContentUnavailableConfiguration = null;
    }
}
