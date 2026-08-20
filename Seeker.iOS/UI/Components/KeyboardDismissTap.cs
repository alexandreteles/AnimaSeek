using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Dismisses the keyboard when a person taps the transcript above an open composer.</summary>
/// <remarks>
/// The recognizer only accepts touches while the composer is actually editing, so ordinary row activation is
/// untouched with the keyboard down. While it is up the tap cancels the touch it consumed, so putting the
/// keyboard away never doubles as opening whatever sat under the finger.
/// </remarks>
internal sealed class KeyboardDismissTap : NSObject
{
    private readonly UITapGestureRecognizer tap;
    private readonly TapDelegate tapDelegate;

    /// <summary>Installs the tap on the surface that should give the transcript its full height back.</summary>
    /// <param name="target">The view the tap listens on, normally the transcript itself.</param>
    /// <param name="isEditing">Reports whether the composer currently holds the keyboard.</param>
    /// <param name="dismiss">Resigns first responder.</param>
    public KeyboardDismissTap(UIView target, Func<bool> isEditing, Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isEditing);
        ArgumentNullException.ThrowIfNull(dismiss);
        tapDelegate = new TapDelegate(isEditing);
        tap = new UITapGestureRecognizer(dismiss)
        {
            CancelsTouchesInView = true,
            Delegate = tapDelegate,
        };
        target.AddGestureRecognizer(tap);
    }

    /// <summary>Keeps the recognizer inert whenever there is no keyboard to put away.</summary>
    /// <param name="isEditing">Reports whether the composer currently holds the keyboard.</param>
    private sealed class TapDelegate(Func<bool> isEditing) : UIGestureRecognizerDelegate
    {
        /// <inheritdoc/>
        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch) => isEditing();
    }
}
