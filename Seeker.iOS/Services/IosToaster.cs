using System.Collections.Concurrent;
using Common;
using CoreFoundation;
using Seeker;
using UIKit;

namespace AnimaSeek.iOS.Services;

/// <summary>Shows short, non-modal messages in a lightweight banner above the active scene.</summary>
internal sealed class IosToaster : IToaster
{
    private const int MaximumPendingBanners = 8;
    private static readonly TimeSpan StaleDebounceEntryAge = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> debouncer = new(StringComparer.Ordinal);
    private readonly IosMainThreadRunner mainThreadRunner;
    private readonly Queue<BannerRequest> pendingBanners = new();
    private UIView? visibleBanner;
    private long bannerGeneration;

    /// <summary>Creates a banner presenter backed by the supplied main-thread runner.</summary>
    /// <param name="mainThreadRunner">The service used to marshal UIKit changes.</param>
    public IosToaster(IosMainThreadRunner mainThreadRunner)
    {
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
    }

    /// <inheritdoc/>
    public void ShowToastShort(StringKey key) => Show(GetString(key), TimeSpan.FromSeconds(2));

    /// <inheritdoc/>
    public void ShowToastLong(StringKey key) => Show(GetString(key), TimeSpan.FromSeconds(4));

    /// <inheritdoc/>
    public void ShowToastShort(string msg) => Show(msg, TimeSpan.FromSeconds(2));

    /// <inheritdoc/>
    public void ShowToastLong(string msg) => Show(msg, TimeSpan.FromSeconds(4));

    /// <inheritdoc/>
    public void ShowToastDebounced(
        string msg,
        string debounceKey,
        string usernameIfApplicable = "",
        int seconds = 1)
    {
        string key = debounceKey + usernameIfApplicable;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset previous = debouncer.AddOrUpdate(key, now, (_, value) => now - value < TimeSpan.FromSeconds(seconds) ? value : now);
        PruneStaleDebounceEntries(now);
        if (previous == now)
        {
            ShowToastLong(msg);
        }
    }

    private void PruneStaleDebounceEntries(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, DateTimeOffset> entry in debouncer)
        {
            if (now - entry.Value > StaleDebounceEntryAge)
            {
                _ = debouncer.TryRemove(entry);
            }
        }
    }

    /// <inheritdoc/>
    public void ShowToastDebounced(
        StringKey key,
        string debounceKey,
        string usernameIfApplicable = "",
        int seconds = 1) =>
        ShowToastDebounced(GetString(key), debounceKey, usernameIfApplicable, seconds);

    /// <inheritdoc/>
    public string GetString(StringKey key) => StringResources.Get(key);

    private void Show(string message, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        mainThreadRunner.RunOnUiThread(() =>
        {
            if (pendingBanners.Count >= MaximumPendingBanners)
            {
                pendingBanners.Dequeue();
            }

            pendingBanners.Enqueue(new BannerRequest(message, duration));
            PresentNextBanner();
        });
    }

    /// <summary>Presents the next queued announcement after the current one has remained readable.</summary>
    private void PresentNextBanner()
    {
        if (visibleBanner is not null || pendingBanners.Count == 0)
        {
            return;
        }

        UIWindow? window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(candidate => candidate.IsKeyWindow);
        UIView? host = window?.RootViewController?.View;
        if (host is null)
        {
            pendingBanners.Clear();
            return;
        }

        BannerRequest request = pendingBanners.Dequeue();
        long generation = ++bannerGeneration;
        var banner = new UIView
        {
            BackgroundColor = UIColor.SecondarySystemBackground,
            Alpha = UIAccessibility.IsReduceMotionEnabled ? 1 : 0,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = false,
        };
        visibleBanner = banner;

        var label = new UILabel
        {
            Text = request.Message,
            Lines = 0,
            TextAlignment = UITextAlignment.Center,
            Font = (UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Subheadline) ??
                UIFont.SystemFontOfSize(UIFont.LabelFontSize))!,
            AdjustsFontForContentSizeCategory = true,
            TextColor = UIColor.Label,
            TranslatesAutoresizingMaskIntoConstraints = false,
            IsAccessibilityElement = true,
            AccessibilityLabel = request.Message,
        };
        banner.AddSubview(label);
        host.AddSubview(banner);

        NSLayoutConstraint.ActivateConstraints([
            banner.TopAnchor.ConstraintEqualTo(host.SafeAreaLayoutGuide.TopAnchor, 8),
            banner.LeadingAnchor.ConstraintGreaterThanOrEqualTo(host.LeadingAnchor, 20),
            banner.TrailingAnchor.ConstraintLessThanOrEqualTo(host.TrailingAnchor, -20),
            banner.CenterXAnchor.ConstraintEqualTo(host.CenterXAnchor),
            label.TopAnchor.ConstraintEqualTo(banner.TopAnchor, 10),
            label.BottomAnchor.ConstraintEqualTo(banner.BottomAnchor, -10),
            label.LeadingAnchor.ConstraintEqualTo(banner.LeadingAnchor, 14),
            label.TrailingAnchor.ConstraintEqualTo(banner.TrailingAnchor, -14),
        ]);

        if (!UIAccessibility.IsReduceMotionEnabled)
        {
            UIView.Animate(0.2, () => banner.Alpha = 1);
        }

        UIAccessibility.PostNotification(
            UIAccessibilityPostNotification.Announcement,
            new Foundation.NSString(request.Message));
        TimeSpan readableDuration = UIAccessibility.IsVoiceOverRunning
            ? TimeSpan.FromSeconds(Math.Max(request.Duration.TotalSeconds, 8))
            : request.Duration;
        DispatchQueue.MainQueue.DispatchAfter(
            new DispatchTime(DispatchTime.Now, readableDuration),
            () =>
            {
                if (generation != bannerGeneration || !ReferenceEquals(visibleBanner, banner))
                {
                    return;
                }

                void Remove()
                {
                    banner.RemoveFromSuperview();
                    if (ReferenceEquals(visibleBanner, banner))
                    {
                        visibleBanner = null;
                    }

                    PresentNextBanner();
                }

                if (UIAccessibility.IsReduceMotionEnabled)
                {
                    Remove();
                }
                else
                {
                    UIView.Animate(0.2, () => banner.Alpha = 0, Remove);
                }
            });
    }

    /// <summary>Stores one bounded, main-thread-owned banner request.</summary>
    /// <param name="Message">The localized announcement text.</param>
    /// <param name="Duration">The minimum visual presentation duration.</param>
    private sealed record BannerRequest(string Message, TimeSpan Duration);
}
