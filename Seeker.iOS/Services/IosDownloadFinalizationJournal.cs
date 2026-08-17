using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Persists the narrow transaction window between moving a completed partial into Documents and checkpointing the
/// portable transfer manager.
/// </summary>
/// <remarks>
/// Entries contain only sandbox-relative paths and are replaced atomically after their contents have been flushed to
/// disk. The source and destination files are the transaction phase: an absent source plus an exact-length destination
/// proves that the move committed.
/// </remarks>
internal sealed class IosDownloadFinalizationJournal
{
    private const string JournalDirectoryName = "DownloadFinalizations";
    private const string JournalFileName = "journal-v1.txt";
    private static readonly char[] PathSeparators = ['/', '\\'];
    private static readonly Lock Sync = new();
    private readonly string journalPath;

    /// <summary>Creates a journal in the app-private Application Support directory.</summary>
    /// <param name="applicationSupportPath">The absolute Application Support directory.</param>
    public IosDownloadFinalizationJournal(string applicationSupportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationSupportPath);
        journalPath = Path.Combine(
            Path.GetFullPath(applicationSupportPath),
            JournalDirectoryName,
            JournalFileName);
    }

    /// <summary>Returns every structurally valid prepared finalization, pruning malformed records atomically.</summary>
    /// <returns>A detached snapshot of the currently prepared moves.</returns>
    public IReadOnlyList<PendingDownloadFinalization> Read()
    {
        lock (Sync)
        {
            string[] encoded = ReadEncodedUnsafe();
            PendingDownloadFinalization[] valid = encoded
                .Select(value => TryDecode(value, out PendingDownloadFinalization entry) ? entry : null)
                .Where(entry => entry is not null)
                .Cast<PendingDownloadFinalization>()
                .ToArray();
            if (valid.Length != encoded.Length)
            {
                WriteUnsafe(valid);
            }

            return valid;
        }
    }

    /// <summary>Flushes one prepared move before its source partial may be renamed.</summary>
    /// <param name="entry">The transfer identity, exact length, and containment-validated relative paths.</param>
    public void Prepare(PendingDownloadFinalization entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Validate(entry);
        lock (Sync)
        {
            var entries = ReadValidUnsafe()
                .Where(existing => !HasIdentity(existing, entry.Username, entry.Filename))
                .Append(entry)
                .ToArray();
            WriteUnsafe(entries);
        }
    }

    /// <summary>Removes one journal entry after both terminal manager state and descriptor cleanup are durable.</summary>
    /// <param name="username">The exact remote username.</param>
    /// <param name="filename">The exact full remote filename.</param>
    public void Complete(string username, string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        lock (Sync)
        {
            PendingDownloadFinalization[] current = ReadValidUnsafe();
            PendingDownloadFinalization[] remaining = current
                .Where(entry => !HasIdentity(entry, username, filename))
                .ToArray();
            if (remaining.Length != current.Length)
            {
                WriteUnsafe(remaining);
            }
        }
    }

    /// <summary>Returns whether a prepared move exists for the supplied transfer identity.</summary>
    /// <param name="username">The exact remote username.</param>
    /// <param name="filename">The exact full remote filename.</param>
    /// <returns><see langword="true"/> when a valid entry is present.</returns>
    public bool Contains(string username, string filename) =>
        Read().Any(entry => HasIdentity(entry, username, filename));

    /// <summary>Encodes an entry without reflection so the full-link iOS build retains the complete codec.</summary>
    internal static string Encode(PendingDownloadFinalization entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Validate(entry);
        return $"1|{EncodeField(entry.Username)}|{EncodeField(entry.Filename)}|" +
            $"{entry.ActualLength.ToString(CultureInfo.InvariantCulture)}|" +
            $"{EncodeField(entry.IncompleteRelativePath)}|{EncodeField(entry.DocumentsRelativePath)}";
    }

    /// <summary>Decodes one current journal record.</summary>
    internal static bool TryDecode(string value, out PendingDownloadFinalization entry)
    {
        entry = null!;
        string[] parts = value?.Split('|') ?? [];
        if (parts is not ["1", _, _, _, _, _] ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actualLength))
        {
            return false;
        }

        try
        {
            var decoded = new PendingDownloadFinalization(
                DecodeField(parts[1]),
                DecodeField(parts[2]),
                actualLength,
                DecodeField(parts[4]),
                DecodeField(parts[5]));
            Validate(decoded);
            entry = decoded;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private PendingDownloadFinalization[] ReadValidUnsafe() =>
        ReadEncodedUnsafe()
            .Select(value => TryDecode(value, out PendingDownloadFinalization entry) ? entry : null)
            .Where(entry => entry is not null)
            .Cast<PendingDownloadFinalization>()
            .ToArray();

    private string[] ReadEncodedUnsafe() =>
        File.Exists(journalPath)
            ? File.ReadAllLines(journalPath, Encoding.UTF8).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : [];

    private void WriteUnsafe(IEnumerable<PendingDownloadFinalization> entries)
    {
        string directory = Path.GetDirectoryName(journalPath)
            ?? throw new IOException("The finalization journal has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $"{JournalFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(
                           stream,
                           new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                           bufferSize: 4096,
                           leaveOpen: true))
                {
                    foreach (string encoded in entries.Select(Encode).Order(StringComparer.Ordinal))
                    {
                        writer.WriteLine(encoded);
                    }

                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, journalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Validate(PendingDownloadFinalization entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Filename);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.ActualLength);
        ValidateRelativePath(entry.IncompleteRelativePath, nameof(entry.IncompleteRelativePath));
        ValidateRelativePath(entry.DocumentsRelativePath, nameof(entry.DocumentsRelativePath));
    }

    private static void ValidateRelativePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (Path.IsPathRooted(path) ||
            path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("A journal path must be sandbox-relative without traversal.", parameterName);
        }
    }

    private static bool HasIdentity(PendingDownloadFinalization entry, string username, string filename) =>
        string.Equals(entry.Username, username, StringComparison.Ordinal) &&
        string.Equals(entry.Filename, filename, StringComparison.Ordinal);

    private static string EncodeField(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeField(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

/// <summary>Describes one completed partial whose final file move is not yet reflected in durable transfer state.</summary>
/// <param name="Username">The exact remote username.</param>
/// <param name="Filename">The exact full remote filename.</param>
/// <param name="ActualLength">The source length immediately before the move.</param>
/// <param name="IncompleteRelativePath">The source path relative to the private Incomplete directory.</param>
/// <param name="DocumentsRelativePath">The selected destination path relative to Documents.</param>
internal sealed record PendingDownloadFinalization(
    string Username,
    string Filename,
    long ActualLength,
    string IncompleteRelativePath,
    string DocumentsRelativePath);

/// <summary>Pure crash-boundary policy for a prepared finalization.</summary>
internal static class IosDownloadFinalizationRecoveryPolicy
{
    /// <summary>Classifies the durable state observed after a process restart.</summary>
    /// <param name="managerSucceeded">Whether the restored manager already contains terminal success metadata.</param>
    /// <param name="sourceExists">Whether the private source partial still exists.</param>
    /// <param name="targetExists">Whether the selected Documents target exists.</param>
    /// <param name="targetLength">The target length, or zero when it does not exist.</param>
    /// <param name="recordedLength">The exact source length recorded before the move.</param>
    /// <returns>The idempotent recovery action.</returns>
    public static DownloadFinalizationRecoveryAction Classify(
        bool managerSucceeded,
        bool sourceExists,
        bool targetExists,
        long targetLength,
        long recordedLength)
    {
        bool exactCommittedMove = !sourceExists && targetExists && targetLength == recordedLength;
        if (!exactCommittedMove)
        {
            return DownloadFinalizationRecoveryAction.RetryFromDescriptor;
        }

        return managerSucceeded
            ? DownloadFinalizationRecoveryAction.AlreadyCheckpointed
            : DownloadFinalizationRecoveryAction.CommitFinalFile;
    }

    /// <summary>Checkpoints a definitive terminal state before removing its crash-resume descriptor.</summary>
    /// <param name="applyTerminalState">Applies and marks the in-memory terminal manager state dirty.</param>
    /// <param name="checkpointTransfers">Returns whether that manager state reached durable storage.</param>
    /// <param name="forgetDescriptor">Durably removes the interrupted-download descriptor.</param>
    /// <returns><see langword="true"/> only when the checkpoint and descriptor cleanup both completed.</returns>
    public static bool TryCheckpointTerminal(
        Action applyTerminalState,
        Func<bool> checkpointTransfers,
        Action forgetDescriptor)
    {
        ArgumentNullException.ThrowIfNull(applyTerminalState);
        ArgumentNullException.ThrowIfNull(checkpointTransfers);
        ArgumentNullException.ThrowIfNull(forgetDescriptor);

        applyTerminalState();
        if (!checkpointTransfers())
        {
            return false;
        }

        forgetDescriptor();
        return true;
    }

    /// <summary>
    /// Applies terminal state, checkpoints it, then clears the descriptor and journal in the only crash-safe order.
    /// </summary>
    /// <param name="applyTerminalState">Updates or creates the in-memory completed manager item.</param>
    /// <param name="checkpointTransfers">Returns whether that manager state reached durable storage.</param>
    /// <param name="forgetDescriptor">Durably tombstones the interrupted-download descriptor.</param>
    /// <param name="completeJournal">Removes the prepared finalization last.</param>
    /// <returns><see langword="true"/> only when every durable boundary completed.</returns>
    public static bool TryCommit(
        Action applyTerminalState,
        Func<bool> checkpointTransfers,
        Action forgetDescriptor,
        Action completeJournal)
    {
        ArgumentNullException.ThrowIfNull(applyTerminalState);
        ArgumentNullException.ThrowIfNull(checkpointTransfers);
        ArgumentNullException.ThrowIfNull(forgetDescriptor);
        ArgumentNullException.ThrowIfNull(completeJournal);

        if (!TryCheckpointTerminal(applyTerminalState, checkpointTransfers, forgetDescriptor))
        {
            return false;
        }

        completeJournal();
        return true;
    }
}

/// <summary>Identifies the recovery action for a prepared final-file move.</summary>
internal enum DownloadFinalizationRecoveryAction
{
    /// <summary>The restored manager already proves that finalization metadata was checkpointed.</summary>
    AlreadyCheckpointed,

    /// <summary>The exact prepared target exists after the source move and must be committed to manager state.</summary>
    CommitFinalFile,

    /// <summary>The move cannot be proven; retain or recreate the descriptor and retry safely.</summary>
    RetryFromDescriptor,
}
