using Soulseek;

namespace AnimaSeek.iOS;

/// <summary>Pure policy that withholds successful download presentation until final-file durability is proven.</summary>
internal static class DownloadCompletionPublicationPolicy
{
    /// <summary>Returns whether a transfer event is waiting for the platform file-finalization transaction.</summary>
    public static bool IsAwaitingDurableFinalization(
        TransferDirection direction,
        TransferStates state,
        string? finalUri) =>
        direction == TransferDirection.Download &&
        state.HasFlag(TransferStates.Succeeded) &&
        string.IsNullOrWhiteSpace(finalUri);

    /// <summary>Returns whether a transfer-state notification can truthfully be published.</summary>
    public static bool CanPublish(
        TransferDirection direction,
        TransferStates state,
        string? finalUri,
        bool durableFinalizationCommitted) =>
        direction != TransferDirection.Download ||
        !state.HasFlag(TransferStates.Succeeded) ||
        (durableFinalizationCommitted && !string.IsNullOrWhiteSpace(finalUri));
}
