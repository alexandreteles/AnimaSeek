using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Seeker.Services;

namespace AnimaSeek.iOS;

/// <summary>
/// Persists the small set of downloads that must survive process termination and resume on the next foreground.
/// </summary>
/// <remarks>
/// Version 3 stores only base64-encoded transfer identity fields and an optional sandbox-relative Documents path.
/// Version 4 uses the same safe shape and marks a peer-corrected expected size as authoritative over an older manager
/// snapshot. Version 5 additionally requires stale partial bytes to be deleted before that corrected size may be used.
/// A missing path identifies the current managed-download flow, whose private incomplete path is derived
/// deterministically from the username and remote filename rather than persisting an absolute container path.
/// </remarks>
internal sealed class IosInterruptedDownloadStore
{
    internal const string StorageKey = "ios.background.interrupted-downloads";
    private static readonly char[] PathSeparators = ['/', '\\'];
    private static readonly Lock Sync = new();
    private readonly IKeyValueStore keyValueStore;
    private readonly HashSet<DownloadIdentity> terminalIdentities = [];

    /// <summary>Creates a crash-durable interrupted-download store over the supplied preference adapter.</summary>
    /// <param name="keyValueStore">The synchronously flushable small-value store used by the iOS head.</param>
    public IosInterruptedDownloadStore(IKeyValueStore keyValueStore) =>
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));

    /// <summary>Returns a detached snapshot of every encoded entry, including malformed legacy entries.</summary>
    /// <returns>The exact encoded values observed at one persistence boundary.</returns>
    public HashSet<string> ReadEncoded()
    {
        lock (Sync)
        {
            return new HashSet<string>(
                keyValueStore.GetStringSet(StorageKey) ?? [],
                StringComparer.Ordinal);
        }
    }

    /// <summary>Adds or upgrades one resumable download and synchronously flushes it before network work starts.</summary>
    /// <param name="download">The transfer identity and optional containment-validated relative path.</param>
    /// <exception cref="ArgumentException">
    /// The identity is incomplete, or a current-format path is rooted or contains traversal segments.
    /// </exception>
    public void Remember(SuspendedDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        ValidateCurrent(download);
        lock (Sync)
        {
            var encoded = ReadEncodedUnsafe();
            if (!download.PartialResetRequired && HasPendingPartialReset(encoded, download.Username, download.Filename))
            {
                return;
            }

            terminalIdentities.Remove(ToIdentity(download));
            RemoveIdentity(encoded, download.Username, download.Filename);
            encoded.Add(Encode(download));
            PersistUnsafe(encoded);
        }
    }

    /// <summary>Adds or upgrades a group of resumable downloads at one synchronous persistence boundary.</summary>
    /// <param name="downloads">The transfer identities and optional containment-validated relative paths.</param>
    public void Remember(IReadOnlyCollection<SuspendedDownload> downloads)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        if (downloads.Count == 0)
        {
            return;
        }

        foreach (SuspendedDownload download in downloads)
        {
            ArgumentNullException.ThrowIfNull(download);
            ValidateCurrent(download);
        }

        lock (Sync)
        {
            var encoded = ReadEncodedUnsafe();
            bool changed = false;
            foreach (SuspendedDownload download in downloads)
            {
                if (!download.PartialResetRequired &&
                    HasPendingPartialReset(encoded, download.Username, download.Filename))
                {
                    continue;
                }

                terminalIdentities.Remove(ToIdentity(download));
                RemoveIdentity(encoded, download.Username, download.Filename);
                encoded.Add(Encode(download));
                changed = true;
            }

            if (changed)
            {
                PersistUnsafe(encoded);
            }
        }
    }

    /// <summary>
    /// Durably clears a version-5 reset marker after the stale partial is absent and the corrected manager state is
    /// checkpointed. A concurrent terminal tombstone wins and cannot be resurrected.
    /// </summary>
    /// <param name="download">The authoritative corrected descriptor with its reset requirement cleared.</param>
    public void CompletePartialReset(SuspendedDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (!download.SizeIsAuthoritative || download.PartialResetRequired)
        {
            throw new ArgumentException(
                "A completed partial reset must retain an authoritative size and clear the reset marker.",
                nameof(download));
        }

        ValidateCurrent(download);
        lock (Sync)
        {
            DownloadIdentity identity = ToIdentity(download);
            if (terminalIdentities.Contains(identity))
            {
                return;
            }

            var encoded = ReadEncodedUnsafe();
            if (!HasPendingPartialReset(encoded, download.Username, download.Filename))
            {
                return;
            }

            RemoveIdentity(encoded, download.Username, download.Filename);
            encoded.Add(Encode(download));
            PersistUnsafe(encoded);
        }
    }

    /// <summary>Removes every format version of a terminal download and synchronously flushes the change.</summary>
    /// <param name="username">The remote Soulseek username.</param>
    /// <param name="filename">The exact remote filename.</param>
    public void Forget(string username, string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        lock (Sync)
        {
            terminalIdentities.Add(new DownloadIdentity(username, filename));
            var encoded = ReadEncodedUnsafe();
            if (RemoveIdentity(encoded, username, filename))
            {
                PersistUnsafe(encoded);
            }
        }
    }

    /// <summary>
    /// Reconciles a previously read snapshot while preserving descriptors added concurrently after that read.
    /// </summary>
    /// <param name="original">Every encoded entry in the previously read snapshot.</param>
    /// <param name="remaining">The validated and migrated entries that should remain from that snapshot.</param>
    public void Reconcile(IReadOnlyCollection<string> original, IReadOnlyCollection<string> remaining)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(remaining);
        lock (Sync)
        {
            var merged = ReadEncodedUnsafe();
            merged.ExceptWith(original);
            foreach (string value in remaining.OrderByDescending(GetDescriptorPriority))
            {
                if (!TryDecode(value, out SuspendedDownload download, out _))
                {
                    merged.Add(value);
                    continue;
                }

                if (terminalIdentities.Contains(ToIdentity(download)) ||
                    ContainsIdentity(merged, download.Username, download.Filename))
                {
                    continue;
                }

                merged.Add(value);
            }

            PersistUnsafe(merged);
        }
    }

    /// <summary>Encodes one validated descriptor using version 3, authoritative version 4, or reset-required version 5.</summary>
    /// <param name="value">The descriptor to encode.</param>
    /// <returns>A delimiter-safe representation containing no absolute current-container path.</returns>
    internal static string Encode(SuspendedDownload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateCurrent(value);
        string version = value.PartialResetRequired ? "5" : value.SizeIsAuthoritative ? "4" : "3";
        return $"{version}|{EncodeField(value.Username)}|{EncodeField(value.Filename)}|" +
            $"{value.Size.ToString(CultureInfo.InvariantCulture)}|{EncodeField(value.DocumentsRelativePath ?? string.Empty)}";
    }

    /// <summary>Decodes supported version-1 through version-5 descriptors.</summary>
    /// <param name="value">The encoded descriptor.</param>
    /// <param name="result">Receives the decoded transfer identity.</param>
    /// <param name="containsLegacyAbsolutePath">Receives whether a version-2 path still requires containment migration.</param>
    /// <returns><see langword="true"/> when the descriptor is structurally valid.</returns>
    internal static bool TryDecode(
        string value,
        out SuspendedDownload result,
        out bool containsLegacyAbsolutePath)
    {
        result = null!;
        containsLegacyAbsolutePath = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('|');
        bool managedLegacy = parts is ["1", _, _, _];
        bool absolutePathLegacy = parts is ["2", _, _, _, _];
        bool current = parts is ["3", _, _, _, _];
        bool authoritativeSize = parts is ["4", _, _, _, _];
        bool resetRequired = parts is ["5", _, _, _, _];
        if ((!managedLegacy && !absolutePathLegacy && !current && !authoritativeSize && !resetRequired) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size))
        {
            return false;
        }

        try
        {
            string username = DecodeField(parts[1]);
            string filename = DecodeField(parts[2]);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            result = new SuspendedDownload(
                username,
                filename,
                size,
                managedLegacy
                    ? null
                    : DecodeField(parts[4]) is { Length: > 0 } path
                        ? path
                        : null,
                SizeIsAuthoritative: authoritativeSize || resetRequired,
                PartialResetRequired: resetRequired);
            containsLegacyAbsolutePath = absolutePathLegacy;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rebases a version-2 absolute Documents path persisted by a previous app container onto its Documents-relative
    /// suffix, because iOS regenerates the container UUID on app update while the partial keeps its relative location.
    /// </summary>
    /// <param name="absolutePath">The stale absolute path from a version-2 descriptor.</param>
    /// <param name="documentsRelativePath">Receives the traversal-free suffix after the Documents segment.</param>
    /// <returns><see langword="true"/> when a safe relative suffix could be recovered.</returns>
    internal static bool TryRebaseLegacyAbsoluteDocumentsPath(string absolutePath, out string documentsRelativePath)
    {
        documentsRelativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        const string DocumentsSegment = "/Documents/";
        int segmentIndex = absolutePath.IndexOf(DocumentsSegment, StringComparison.Ordinal);
        if (segmentIndex < 0)
        {
            return false;
        }

        string suffix = absolutePath[(segmentIndex + DocumentsSegment.Length)..];
        if (suffix.Length == 0 ||
            Path.IsPathRooted(suffix) ||
            suffix.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            return false;
        }

        documentsRelativePath = suffix;
        return true;
    }

    private HashSet<string> ReadEncodedUnsafe() => new(
        keyValueStore.GetStringSet(StorageKey) ?? [],
        StringComparer.Ordinal);

    private void PersistUnsafe(HashSet<string> encoded)
    {
        keyValueStore.PutStringSet(StorageKey, encoded.Count == 0 ? null : encoded);
        keyValueStore.Flush();
    }

    private static bool RemoveIdentity(HashSet<string> encoded, string username, string filename) =>
        encoded.RemoveWhere(value =>
            TryDecode(value, out SuspendedDownload existing, out _) &&
            string.Equals(existing.Username, username, StringComparison.Ordinal) &&
            string.Equals(existing.Filename, filename, StringComparison.Ordinal)) > 0;

    private static bool ContainsIdentity(HashSet<string> encoded, string username, string filename) =>
        encoded.Any(value =>
            TryDecode(value, out SuspendedDownload existing, out _) &&
            string.Equals(existing.Username, username, StringComparison.Ordinal) &&
            string.Equals(existing.Filename, filename, StringComparison.Ordinal));

    private static bool HasPendingPartialReset(HashSet<string> encoded, string username, string filename) =>
        encoded.Any(value =>
            TryDecode(value, out SuspendedDownload existing, out _) &&
            existing.PartialResetRequired &&
            string.Equals(existing.Username, username, StringComparison.Ordinal) &&
            string.Equals(existing.Filename, filename, StringComparison.Ordinal));

    private static int GetDescriptorPriority(string value) =>
        TryDecode(value, out SuspendedDownload descriptor, out _)
            ? descriptor.PartialResetRequired
                ? 2
                : descriptor.SizeIsAuthoritative
                    ? 1
                    : 0
            : -1;

    private static void ValidateCurrent(SuspendedDownload download)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(download.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(download.Filename);
        if (download.PartialResetRequired &&
            (!download.SizeIsAuthoritative || download.Size < 0 || download.DocumentsRelativePath is not null))
        {
            throw new ArgumentException(
                "A partial-reset descriptor requires a non-negative authoritative size and a derived private path.",
                nameof(download));
        }

        if (download.DocumentsRelativePath is not { Length: > 0 } relativePath)
        {
            return;
        }

        if (Path.IsPathRooted(relativePath) ||
            relativePath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                "A current interrupted-download path must remain relative and cannot contain traversal segments.",
                nameof(download));
        }
    }

    private static string EncodeField(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeField(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static DownloadIdentity ToIdentity(SuspendedDownload download) =>
        new(download.Username, download.Filename);

    private readonly record struct DownloadIdentity(string Username, string Filename);
}

/// <summary>Identifies a download retained across process suspension or termination.</summary>
/// <param name="Username">The remote Soulseek username.</param>
/// <param name="Filename">The exact full remote filename.</param>
/// <param name="Size">The expected file size in bytes.</param>
/// <param name="DocumentsRelativePath">
/// A containment-validated path relative to Documents for an obsolete direct download, or <see langword="null"/>
/// when the private partial-file path must be derived deterministically.
/// </param>
/// <param name="SizeIsAuthoritative">
/// Whether a peer size-mismatch response durably superseded any older restored manager size.
/// </param>
/// <param name="PartialResetRequired">
/// Whether bytes retained for the previous remote size must be discarded before resume or exact-completion checks.
/// </param>
internal sealed record SuspendedDownload(
    string Username,
    string Filename,
    long Size,
    string? DocumentsRelativePath = null,
    bool SizeIsAuthoritative = false,
    bool PartialResetRequired = false);

/// <summary>Pure conservative size policy for deciding whether a restored partial is actually complete.</summary>
internal static class InterruptedDownloadResumePolicy
{
    /// <summary>Returns the newest provable expected size from the descriptor and restored manager.</summary>
    /// <param name="descriptorSize">The size made durable before transfer I/O.</param>
    /// <param name="managerSize">The latest size retained by the portable transfer manager.</param>
    /// <param name="descriptorSizeIsAuthoritative">
    /// Whether a version-4 peer correction supersedes the potentially older manager snapshot.
    /// </param>
    /// <returns>The authoritative correction, otherwise the restored manager size, or <c>-1</c> when unknown.</returns>
    public static long ResolveExpectedSize(
        long descriptorSize,
        long managerSize,
        bool descriptorSizeIsAuthoritative)
    {
        if (descriptorSizeIsAuthoritative && descriptorSize >= 0)
        {
            return descriptorSize;
        }

        return managerSize >= 0
            ? managerSize
            : descriptorSize >= 0
                ? descriptorSize
                : -1;
    }

    /// <summary>
    /// Resolves the expected length of a prepared finalization, honoring a durable peer correction even when the
    /// journal move committed before the corrected manager snapshot reached disk.
    /// </summary>
    /// <param name="journalActualLength">The exact source length recorded before the final move.</param>
    /// <param name="managerSize">The size restored from the portable manager snapshot.</param>
    /// <param name="durableDescriptor">The matching interrupted descriptor, when it still exists.</param>
    /// <returns>The newest provable expected byte length.</returns>
    public static long ResolveFinalizationExpectedSize(
        long journalActualLength,
        long managerSize,
        SuspendedDownload? durableDescriptor) =>
        durableDescriptor is null
            ? ResolveExpectedSize(journalActualLength, managerSize, descriptorSizeIsAuthoritative: false)
            : ResolveExpectedSize(
                durableDescriptor.Size,
                managerSize,
                durableDescriptor.SizeIsAuthoritative);

    /// <summary>Returns whether the partial exactly matches the safest restored expected size.</summary>
    /// <param name="partialLength">The actual private partial length.</param>
    /// <param name="descriptorSize">The durable interrupted-download size.</param>
    /// <param name="managerSize">The restored manager size, which may contain a newer mismatch correction.</param>
    /// <param name="descriptorSizeIsAuthoritative">Whether the descriptor contains a version-4 peer correction.</param>
    public static bool IsComplete(
        long partialLength,
        long descriptorSize,
        long managerSize,
        bool descriptorSizeIsAuthoritative,
        bool partialResetRequired = false)
    {
        if (partialResetRequired)
        {
            return false;
        }

        long expectedSize = ResolveExpectedSize(
            descriptorSize,
            managerSize,
            descriptorSizeIsAuthoritative);
        return expectedSize >= 0 && partialLength == expectedSize;
    }

    /// <summary>
    /// Returns whether retained partial bytes exceed the newest provable expected size and must be discarded, because
    /// bytes written for a larger previous remote size are untrustworthy after a downward correction.
    /// </summary>
    /// <param name="partialLength">The actual retained partial byte count.</param>
    /// <param name="expectedSize">The resolved expected size, or a negative value when unknown.</param>
    public static bool RequiresOversizedPartialDiscard(long partialLength, long expectedSize) =>
        expectedSize >= 0 && partialLength > expectedSize;
}

/// <summary>Crash-safe ordering for discarding bytes that predate a peer-authoritative size correction.</summary>
internal static class InterruptedDownloadPartialResetPolicy
{
    /// <summary>
    /// Persists a reset marker, deletes stale bytes, checkpoints corrected retry state, and only then clears the
    /// marker. Every callback exception intentionally propagates so no live retry can cross a failed boundary.
    /// </summary>
    /// <param name="persistResetRequired">Flushes the version-5 descriptor before filesystem mutation.</param>
    /// <param name="deleteStalePartial">Deletes the old partial through an observable, throwing path.</param>
    /// <param name="applyResetManagerState">Applies corrected cancelled state after deletion succeeds.</param>
    /// <param name="checkpointResetManagerState">Durably checkpoints that corrected manager state.</param>
    /// <param name="persistResetCompleted">Replaces version 5 with authoritative version 4.</param>
    /// <exception cref="IOException">The reset manager checkpoint did not reach durable storage.</exception>
    public static void Resolve(
        Action persistResetRequired,
        Action deleteStalePartial,
        Action applyResetManagerState,
        Func<bool> checkpointResetManagerState,
        Action persistResetCompleted)
    {
        ArgumentNullException.ThrowIfNull(persistResetRequired);
        ArgumentNullException.ThrowIfNull(deleteStalePartial);
        ArgumentNullException.ThrowIfNull(applyResetManagerState);
        ArgumentNullException.ThrowIfNull(checkpointResetManagerState);
        ArgumentNullException.ThrowIfNull(persistResetCompleted);

        persistResetRequired();
        deleteStalePartial();
        applyResetManagerState();
        if (!checkpointResetManagerState())
        {
            throw new IOException("The corrected download state could not be checkpointed after resetting its partial.");
        }

        persistResetCompleted();
    }
}

/// <summary>Describes whether foreground restoration started work or reached a durable terminal boundary.</summary>
internal enum InterruptedDownloadResumeResult
{
    /// <summary>The session could not start or recognize the interrupted download.</summary>
    NotStarted,

    /// <summary>The download is already active or a retry was initiated while its descriptor remains durable.</summary>
    Active,

    /// <summary>The completed partial was finalized or an already-finalized transfer was recognized.</summary>
    Terminal,
}
