using Foundation;
using Seeker.Routing;

namespace AnimaSeek.iOS;

/// <summary>Queues and publishes validated <c>slsk://</c> URLs independently of scene creation order.</summary>
internal sealed class DeepLinkRouter
{
    private const int MaximumPendingLinks = 16;
    private readonly Lock sync = new();
    private readonly Queue<NSUrl> pendingUrls = new();
    private readonly HashSet<string> pendingIdentities = new(StringComparer.Ordinal);

    /// <summary>Raised when a Soulseek URL is opened while a UI consumer is attached.</summary>
    public event EventHandler<NSUrl>? UrlOpened;

    /// <summary>Raised when a validated typed Soulseek link is opened.</summary>
    public event EventHandler<SoulseekLinkOpenedEventArgs>? LinkOpened;

    /// <summary>Publishes a valid Soulseek URL or retains it until a consumer requests pending values.</summary>
    /// <param name="url">The URL supplied by UIKit.</param>
    public void Open(NSUrl url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!SoulseekLinkParser.TryParse(url.AbsoluteString, out SoulseekLink? link))
        {
            return;
        }

        string identity = link!.ToString();
        EventHandler<NSUrl>? urlHandler = UrlOpened;
        EventHandler<SoulseekLinkOpenedEventArgs>? linkHandler = LinkOpened;
        if (urlHandler is null && linkHandler is null)
        {
            lock (sync)
            {
                if (pendingIdentities.Add(identity))
                {
                    pendingUrls.Enqueue(url);
                    while (pendingUrls.Count > MaximumPendingLinks)
                    {
                        NSUrl removed = pendingUrls.Dequeue();
                        if (SoulseekLinkParser.TryParse(removed.AbsoluteString, out SoulseekLink? removedLink))
                        {
                            pendingIdentities.Remove(removedLink!.ToString());
                        }
                    }
                }
            }

            return;
        }

        urlHandler?.Invoke(this, url);
        linkHandler?.Invoke(this, new SoulseekLinkOpenedEventArgs(url, link));
    }

    /// <summary>Returns and removes the oldest URL opened before a screen was ready.</summary>
    /// <returns>The oldest pending URL, or <see langword="null"/> when none exists.</returns>
    public NSUrl? TakePending()
    {
        lock (sync)
        {
            if (!pendingUrls.TryDequeue(out NSUrl? result))
            {
                return null;
            }

            if (SoulseekLinkParser.TryParse(result.AbsoluteString, out SoulseekLink? link))
            {
                pendingIdentities.Remove(link!.ToString());
            }

            return result;
        }
    }

    /// <summary>Returns and removes every URL currently waiting for the presentation coordinator.</summary>
    /// <returns>The pending URLs in delivery order.</returns>
    public IReadOnlyList<NSUrl> TakeAllPending()
    {
        lock (sync)
        {
            NSUrl[] result = pendingUrls.ToArray();
            pendingUrls.Clear();
            pendingIdentities.Clear();
            return result;
        }
    }
}

/// <summary>Contains the platform URL and its validated portable representation.</summary>
/// <param name="Url">The URL delivered by UIKit.</param>
/// <param name="Link">The normalized safe Soulseek target.</param>
internal sealed class SoulseekLinkOpenedEventArgs(NSUrl Url, SoulseekLink Link) : EventArgs
{
    /// <summary>Gets the URL delivered by UIKit.</summary>
    public NSUrl Url { get; } = Url;

    /// <summary>Gets the normalized safe Soulseek target.</summary>
    public SoulseekLink Link { get; } = Link;
}
