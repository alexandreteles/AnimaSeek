using Foundation;

namespace AnimaSeek.iOS.UI.Presentation;

/// <summary>Supplies shared, locale-aware formatting and main-thread publication for feature presentation stores.</summary>
internal abstract class FeaturePresentationStore
{
    private readonly SynchronizationContext publicationContext;

    /// <summary>Captures the UI synchronization context used for subsequent state notifications.</summary>
    protected FeaturePresentationStore()
    {
        publicationContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    /// <summary>Raises an event on the synchronization context captured when the store was created.</summary>
    /// <param name="handler">The current event delegate.</param>
    protected void Publish(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (ReferenceEquals(SynchronizationContext.Current, publicationContext))
        {
            handler(this, EventArgs.Empty);
            return;
        }

        publicationContext.Post(_ => handler(this, EventArgs.Empty), null);
    }
}

/// <summary>Formats file sizes, rates, and durations using current Apple locale conventions.</summary>
internal static class FeatureValueFormatter
{
    /// <summary>Formats a byte count for compact metadata display.</summary>
    /// <param name="bytes">The non-negative byte count, or a negative value when unknown.</param>
    /// <returns>A localized storage value or the catalog's unknown-state label.</returns>
    public static string Bytes(long bytes) => bytes < 0
        ? Common.StringResources.Get("unknown")
        : NSByteCountFormatter.Format(bytes, NSByteCountFormatterCountStyle.File);

    /// <summary>Formats a byte-per-second rate without implying precision that is not available.</summary>
    /// <param name="bytesPerSecond">The measured byte rate.</param>
    /// <returns>A localized compact rate string.</returns>
    public static string Rate(double bytesPerSecond) => Common.StringResources.Format(
        "IosUiBytesPerSecond",
        Bytes((long)Math.Max(0, bytesPerSecond)));

    /// <summary>Returns the final path component of a remote Soulseek path.</summary>
    /// <param name="path">A path that may use slash or backslash separators.</param>
    /// <returns>The final non-empty path component, or the original value when none exists.</returns>
    public static string FileName(string path) => (path ?? string.Empty)
        .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault() ?? path ?? string.Empty;

    /// <summary>Returns the parent portion of a remote Soulseek path.</summary>
    /// <param name="path">A path that may use slash or backslash separators.</param>
    /// <returns>The normalized parent path, or an empty string for a top-level file.</returns>
    public static string ParentPath(string path)
    {
        string normalized = (path ?? string.Empty).Replace('/', '\\').Trim('\\');
        int separator = normalized.LastIndexOf('\\');
        return separator > 0 ? normalized[..separator] : string.Empty;
    }
}
