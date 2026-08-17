using System.Security.Cryptography;
using System.Text;
using Soulseek;

namespace AnimaSeek.iOS;

/// <summary>
/// Identifies one portable transfer without relying on the protocol token, which can change after restoration.
/// </summary>
/// <param name="Direction">Whether the transfer is a download or upload.</param>
/// <param name="Username">The remote peer.</param>
/// <param name="RemoteFilename">The exact remote path.</param>
internal readonly record struct TransferIdentity(
    TransferDirection Direction,
    string Username,
    string RemoteFilename)
{
    /// <summary>
    /// Gets a presentation-safe opaque identifier suitable for restoration and accessibility identifiers.
    /// </summary>
    public string StableId
    {
        get
        {
            string source = $"{(int)Direction}:{Username.Length}:{Username}{RemoteFilename.Length}:{RemoteFilename}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..20];
        }
    }
}

/// <summary>
/// Describes an accepted download request before the underlying network operation reaches a terminal state.
/// </summary>
/// <param name="Identity">The durable file identity used by the transfer list.</param>
/// <param name="QueuedPaused">Whether the request was intentionally added in a paused state.</param>
internal readonly record struct DownloadAcceptance(TransferIdentity Identity, bool QueuedPaused);

/// <summary>Describes a user action supported by the session transfer façade.</summary>
internal enum TransferCommand
{
    /// <summary>Restarts a paused or failed download.</summary>
    Retry,

    /// <summary>Stops active work while retaining its portable row and partial bytes.</summary>
    Pause,

    /// <summary>Removes the row and any private partial bytes; completed files remain in Documents.</summary>
    Clear,
}

/// <summary>
/// Immutable transfer state consumed by UIKit. It combines protocol progress with restored portable metadata.
/// </summary>
/// <param name="Identity">The transfer's portable identity.</param>
/// <param name="DisplayName">The final path component shown to the user.</param>
/// <param name="FolderName">The portable folder grouping label.</param>
/// <param name="State">The latest protocol or restored queue state.</param>
/// <param name="Size">The total expected bytes, or a negative value when unknown.</param>
/// <param name="BytesTransferred">The current transferred byte count.</param>
/// <param name="AverageSpeed">The current average byte rate.</param>
/// <param name="RemainingTime">The estimated remaining duration.</param>
/// <param name="FinalUri">A verified Files-compatible URI after successful download finalization.</param>
/// <param name="IsProcessing">Whether live transfer work currently owns the portable entry.</param>
internal sealed record TransferSnapshot(
    TransferIdentity Identity,
    string DisplayName,
    string FolderName,
    TransferStates State,
    long Size,
    long BytesTransferred,
    double AverageSpeed,
    TimeSpan? RemainingTime,
    string? FinalUri,
    bool IsProcessing)
{
    /// <summary>Gets normalized progress without treating an unknown size as complete.</summary>
    public double Progress => Size > 0
        ? Math.Clamp(BytesTransferred / (double)Size, 0, 1)
        : State.HasFlag(TransferStates.Succeeded) ? 1 : 0;

    /// <summary>Gets whether Open and Share may be offered for this completed download.</summary>
    public bool CanOpen => Identity.Direction == TransferDirection.Download &&
        State.HasFlag(TransferStates.Succeeded) &&
        !string.IsNullOrWhiteSpace(FinalUri);
}
