using AnimaSeek.iOS.UI.Accessibility;
using CoreGraphics;
using Foundation;
using UIKit;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Drives the direct drag-to-reply gesture shared by the room and private-message transcripts.</summary>
/// <remarks>
/// A trailing swipe action would reveal a Reply button that still has to be pressed, which is two gestures for
/// one intention. This is the conversation idiom instead: the row follows the finger, a glyph fades in behind
/// it, the row resists once the reply is armed, and releasing past that point quotes the message immediately.
/// The gesture is invisible to VoiceOver, so every row that installs one also carries a Reply custom action.
/// </remarks>
internal sealed class ReplySwipe : NSObject
{
    // Far enough that an incidental horizontal drift never quotes a message, close enough to reach one-handed.
    private const double ActivationDistance = 56;

    // Past activation the row barely keeps moving, which reads as a detent rather than a row sliding away.
    private const double ResistedTravel = 20;
    private const double GlyphTrailingInset = 16;
    private const double GlyphSize = 22;

    // The glyph drifts with the row rather than sitting nailed to the edge, so the two read as one movement.
    private const double GlyphDriftFraction = 0.35;
    private const double GlyphRestingScale = 0.7;

    private readonly UIView cell;
    private readonly UIView content;
    private readonly UIImageView glyph;
    private readonly UIPanGestureRecognizer pan;
    private readonly PanDelegate panDelegate;
    private readonly UIImpactFeedbackGenerator feedback;
    private Action? reply;
    private Action<bool>? tracking;
    private bool armed;

    /// <summary>Installs the gesture and its revealed glyph on one reusable conversation row.</summary>
    /// <param name="cell">The row that receives the gesture.</param>
    /// <param name="content">The Auto Layout content that follows the finger; never the cell's content view,
    /// whose frame a table assigns directly and would fight a transform.</param>
    public ReplySwipe(UITableViewCell cell, UIView content)
    {
        this.cell = cell ?? throw new ArgumentNullException(nameof(cell));
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        glyph = new UIImageView(UIImage.GetSystemImage("arrowshape.turn.up.left.fill"))
        {
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = UIColor.SecondaryLabel,
            TranslatesAutoresizingMaskIntoConstraints = false,
            Alpha = 0,
            IsAccessibilityElement = false,
        };
        cell.ContentView.InsertSubview(glyph, 0);
        NSLayoutConstraint.ActivateConstraints(
        [
            glyph.TrailingAnchor.ConstraintEqualTo(cell.ContentView.TrailingAnchor, -(nfloat)GlyphTrailingInset),
            glyph.CenterYAnchor.ConstraintEqualTo(cell.ContentView.CenterYAnchor),
            glyph.WidthAnchor.ConstraintEqualTo((nfloat)GlyphSize),
            glyph.HeightAnchor.ConstraintEqualTo((nfloat)GlyphSize),
        ]);
        feedback = UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Light, cell);
        panDelegate = new PanDelegate(this);
        pan = new UIPanGestureRecognizer(HandlePan) { Delegate = panDelegate };
        cell.AddGestureRecognizer(pan);
    }

    /// <summary>Points the gesture at the message the row currently shows, or disables it entirely.</summary>
    /// <param name="reply">The quote action for the current message, or null for a row that cannot be replied to.</param>
    /// <param name="tracking">Receives whether a drag is in flight, so the transcript can hold its reloads.</param>
    public void Configure(Action? reply, Action<bool>? tracking)
    {
        this.reply = reply;
        this.tracking = tracking;
        Reset();
    }

    /// <summary>Returns the row to rest, which a reused cell must do before it shows another message.</summary>
    public void Reset()
    {
        armed = false;
        content.Transform = CGAffineTransform.MakeIdentity();
        glyph.Transform = CGAffineTransform.MakeIdentity();
        glyph.Alpha = 0;
    }

    /// <summary>Tracks the finger, then quotes the message when the drag is released past activation.</summary>
    private void HandlePan()
    {
        if (reply is not { } quote)
        {
            return;
        }

        switch (pan.State)
        {
            case UIGestureRecognizerState.Began:
                feedback.Prepare();
                tracking?.Invoke(true);
                break;
            case UIGestureRecognizerState.Changed:
                Track(Resisted(pan.TranslationInView(cell).X));
                break;
            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                bool quotes = armed && pan.State == UIGestureRecognizerState.Ended;
                SpringBack();
                tracking?.Invoke(false);
                if (quotes)
                {
                    quote();
                }

                break;
        }
    }

    /// <summary>Maps raw translation to travel that stops following the finger once the reply is armed.</summary>
    /// <param name="translation">The horizontal translation reported by the gesture.</param>
    /// <returns>A non-positive offset, since only a trailing drag means reply.</returns>
    private static double Resisted(double translation)
    {
        double distance = -Math.Min(0, translation);
        if (distance <= ActivationDistance)
        {
            return -distance;
        }

        double past = distance - ActivationDistance;
        return -(ActivationDistance + (ResistedTravel * past / (past + ResistedTravel)));
    }

    /// <summary>Moves the row and its glyph, and announces arming through one haptic tap.</summary>
    /// <param name="offset">The current non-positive travel.</param>
    private void Track(double offset)
    {
        double progress = Math.Min(1, -offset / ActivationDistance);
        content.Transform = CGAffineTransform.MakeTranslation((nfloat)offset, 0);
        double scale = GlyphRestingScale + ((1 - GlyphRestingScale) * progress);
        glyph.Transform = CGAffineTransform.MakeScale((nfloat)scale, (nfloat)scale) *
            CGAffineTransform.MakeTranslation((nfloat)(offset * GlyphDriftFraction), 0);
        glyph.Alpha = (nfloat)progress;
        bool nowArmed = -offset >= ActivationDistance;
        if (nowArmed == armed)
        {
            return;
        }

        armed = nowArmed;
        if (armed)
        {
            feedback.ImpactOccurred();
        }
    }

    /// <summary>Returns the row to rest, honoring Reduce Motion by snapping instead of animating.</summary>
    private void SpringBack()
    {
        double duration = AccessibilityExtensions.AccessibleAnimationDuration(0.3);
        if (duration <= 0)
        {
            Reset();
            return;
        }

        UIView.Animate(
            duration,
            0,
            UIViewAnimationOptions.CurveEaseOut | UIViewAnimationOptions.AllowUserInteraction,
            Reset,
            () => { });
    }

    /// <summary>Begins only on a clear trailing drag, so vertical reading always wins the touch.</summary>
    /// <remarks>Measured against the row, never against the content the drag is busy translating.</remarks>
    private bool ShouldBegin()
    {
        if (reply is null)
        {
            return false;
        }

        CGPoint velocity = pan.VelocityInView(cell);
        return velocity.X < 0 && Math.Abs(velocity.X) > Math.Abs(velocity.Y);
    }

    /// <summary>Gates the drag on direction and lets the transcript keep scrolling underneath it.</summary>
    /// <param name="owner">The gesture this delegate answers for.</param>
    private sealed class PanDelegate(ReplySwipe owner) : UIGestureRecognizerDelegate
    {
        /// <inheritdoc/>
        public override bool ShouldBegin(UIGestureRecognizer recognizer) => owner.ShouldBegin();

        /// <inheritdoc/>
        public override bool ShouldRecognizeSimultaneously(
            UIGestureRecognizer gestureRecognizer,
            UIGestureRecognizer otherGestureRecognizer) => true;
    }
}
