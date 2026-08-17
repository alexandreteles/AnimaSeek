using Common;
using Foundation;
using Seeker.Services;
using System.Security.Cryptography;
using System.Text;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Implements Seeker storage with plain sandbox paths: completed files in Documents and partial files in Library.
/// </summary>
internal sealed class IosFileSystemService : IFileSystemService
{
    private static readonly char[] RemoteSeparators = ['\\', '/'];
    private readonly Lock incompletePathSync = new();
    private readonly Lock finalizationSync = new();

    /// <summary>
    /// Initializes the sandbox folders used by completed and incomplete downloads.
    /// </summary>
    public IosFileSystemService()
    {
        DocumentsPath = GetDirectory(NSSearchPathDirectory.DocumentDirectory);
        ApplicationSupportPath = GetDirectory(NSSearchPathDirectory.ApplicationSupportDirectory);
        IncompletePath = Path.Combine(GetDirectory(NSSearchPathDirectory.LibraryDirectory), "Incomplete");
        Directory.CreateDirectory(DocumentsPath);
        Directory.CreateDirectory(ApplicationSupportPath);
        Directory.CreateDirectory(IncompletePath);
        FinalizationJournal = new IosDownloadFinalizationJournal(ApplicationSupportPath);
    }

    /// <summary>Gets the Files-visible Documents folder used as both download and share root.</summary>
    public string DocumentsPath { get; }

    /// <summary>Gets the private Application Support directory used for durable non-user documents.</summary>
    public string ApplicationSupportPath { get; }

    /// <summary>Gets the private folder that holds resumable partial downloads.</summary>
    public string IncompletePath { get; }

    /// <summary>Gets the durable journal written before a completed partial is moved into Documents.</summary>
    internal IosDownloadFinalizationJournal FinalizationJournal { get; }

    /// <summary>Raised after a completed file becomes visible in Documents.</summary>
    internal event EventHandler? FileFinalized;

    /// <inheritdoc/>
    public void GetOrCreateIncompleteLocation(
        string username,
        string fullfilename,
        int depth,
        out string incompleteUri,
        out string parentUri,
        out long partialLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullfilename);
        lock (incompletePathSync)
        {
            incompleteUri = RequireContainedFile(
                IncompletePath,
                Path.Combine(IncompletePath, BuildIncompleteRelativePath(username, fullfilename)),
                nameof(fullfilename));
            parentUri = Path.GetDirectoryName(incompleteUri) ?? IncompletePath;
            Directory.CreateDirectory(parentUri);

            // Earlier builds keyed partials only by a sanitized, depth-truncated display path and optionally omitted
            // the username. Such a path can belong to multiple logical transfers, so assigning its bytes to the first
            // caller risks silent corruption. Restart those ambiguous legacy partials instead of guessing ownership.
            DeleteAmbiguousLegacyPartials(username, fullfilename, depth, incompleteUri);
            partialLength = File.Exists(incompleteUri) ? new FileInfo(incompleteUri).Length : 0;
        }
    }

    /// <inheritdoc/>
    public Stream OpenIncompleteStream(string incompleteUri, long partialLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incompleteUri);
        string safeIncompletePath = RequireContainedFile(IncompletePath, incompleteUri, nameof(incompleteUri));
        Directory.CreateDirectory(Path.GetDirectoryName(safeIncompletePath) ?? IncompletePath);

        var stream = new FileStream(
            safeIncompletePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = Math.Clamp(partialLength, 0, stream.Length);
        return stream;
    }

    /// <inheritdoc/>
    public string SaveToFile(
        string fullfilename,
        string username,
        ArraySegment<byte> bytes,
        string uriOfIncomplete,
        string parentUriOfIncomplete,
        bool memoryMode,
        int depth,
        bool noSubFolder,
        out string finalUri)
    {
        string baseDestination = RequireContainedFile(
            DocumentsPath,
            Path.Combine(DocumentsPath, BuildRelativePath(username, fullfilename, depth, noSubFolder)),
            nameof(fullfilename));
        lock (finalizationSync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baseDestination) ?? DocumentsPath);
            string? safeIncompletePath = memoryMode
                ? null
                : RequireContainedFile(IncompletePath, uriOfIncomplete, nameof(uriOfIncomplete));
            long actualLength = memoryMode ? bytes.Count : new FileInfo(safeIncompletePath!).Length;

            foreach (int collisionIndex in Enumerable.Range(1, int.MaxValue))
            {
                string destination = BuildCollisionDestination(baseDestination, collisionIndex);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    continue;
                }

                if (memoryMode)
                {
                    FileStream output;
                    try
                    {
                        output = new FileStream(
                            destination,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.Read);
                    }
                    catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
                    {
                        continue;
                    }

                    try
                    {
                        using (output)
                        {
                            output.Write(bytes.AsSpan());
                        }
                    }
                    catch
                    {
                        File.Delete(destination);
                        throw;
                    }
                }
                else
                {
                    FinalizationJournal.Prepare(new PendingDownloadFinalization(
                        username,
                        fullfilename,
                        actualLength,
                        Path.GetRelativePath(IncompletePath, safeIncompletePath!),
                        Path.GetRelativePath(DocumentsPath, destination)));
                    try
                    {
                        File.Move(safeIncompletePath!, destination);
                    }
                    catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
                    {
                        continue;
                    }
                }

                finalUri = NSUrl.FromFilename(destination).AbsoluteString ?? destination;
                return destination;
            }
        }

        throw new IOException($"No collision-free destination could be allocated for '{fullfilename}'.");
    }

    /// <inheritdoc/>
    public void OnFileFinalized(string path)
    {
        // Documents is already visible in Files; iOS has no third-party Music-library insertion API.
        FileFinalized?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns a Documents-relative path after verifying that an absolute path remains inside Documents.</summary>
    /// <param name="absolutePath">The absolute sandbox path to validate.</param>
    /// <returns>A portable relative path suitable for durable background state.</returns>
    /// <exception cref="UnauthorizedAccessException">The path escapes the Documents directory.</exception>
    internal string GetDocumentsRelativePath(string absolutePath)
    {
        string safePath = RequireContainedFile(DocumentsPath, absolutePath, nameof(absolutePath));
        return Path.GetRelativePath(DocumentsPath, safePath);
    }

    /// <summary>Resolves a durable Documents-relative path without allowing rooted or parent traversal.</summary>
    /// <param name="relativePath">A relative path previously returned by <see cref="GetDocumentsRelativePath"/>.</param>
    /// <returns>The canonical absolute path inside Documents.</returns>
    /// <exception cref="UnauthorizedAccessException">The path is rooted or escapes the Documents directory.</exception>
    internal string ResolveDocumentsRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException("A persisted download path must be relative to Documents.");
        }

        return RequireContainedFile(
            DocumentsPath,
            Path.Combine(DocumentsPath, relativePath),
            nameof(relativePath));
    }

    /// <summary>Inspects both sides of a prepared move using containment-validated current-container paths.</summary>
    /// <param name="entry">The portable journal entry captured before the move.</param>
    /// <returns>The exact restart state and final URI to commit when the move is proven.</returns>
    internal DownloadFinalizationFileSnapshot InspectFinalization(PendingDownloadFinalization entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string sourcePath = RequireContainedFile(
            IncompletePath,
            Path.Combine(IncompletePath, entry.IncompleteRelativePath),
            nameof(entry));
        string finalPath = ResolveDocumentsRelativePath(entry.DocumentsRelativePath);
        bool sourceExists = File.Exists(sourcePath);
        bool targetExists = File.Exists(finalPath);
        return new DownloadFinalizationFileSnapshot(
            sourceExists,
            targetExists,
            targetExists ? new FileInfo(finalPath).Length : 0,
            finalPath,
            NSUrl.FromFilename(finalPath).AbsoluteString ?? finalPath);
    }

    /// <summary>
    /// Moves a partial file created by the obsolete direct-download path into the private, portable incomplete-file
    /// layout used by <see cref="DownloadService"/>.
    /// </summary>
    /// <param name="username">The remote Soulseek username.</param>
    /// <param name="remoteFilename">The full remote filename.</param>
    /// <param name="documentsRelativePath">The containment-validated source path relative to Documents.</param>
    /// <param name="expectedSize">The newest provable expected byte length, or a negative value when unknown.</param>
    /// <param name="incompletePath">Receives the canonical private incomplete-file path.</param>
    /// <param name="incompleteParentPath">Receives the incomplete file's parent directory.</param>
    /// <param name="partialLength">Receives the retained partial byte count after migration.</param>
    internal void MigrateDocumentsPartialToIncomplete(
        string username,
        string remoteFilename,
        string documentsRelativePath,
        long expectedSize,
        out string incompletePath,
        out string incompleteParentPath,
        out long partialLength)
    {
        string sourcePath = ResolveDocumentsRelativePath(documentsRelativePath);
        GetOrCreateIncompleteLocation(
            username,
            remoteFilename,
            depth: 1,
            out incompletePath,
            out incompleteParentPath,
            out partialLength);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(incompleteParentPath);
        long sourceLength = new FileInfo(sourcePath).Length;
        if (expectedSize >= 0 && sourceLength > expectedSize)
        {
            // Bytes beyond a downward-corrected expected size are untrustworthy; an oversized stale candidate
            // must never displace a usable partial or re-enter the resume flow.
            File.Delete(sourcePath);
        }
        else if (!File.Exists(incompletePath) || sourceLength > new FileInfo(incompletePath).Length)
        {
            File.Move(sourcePath, incompletePath, overwrite: true);
        }
        else
        {
            File.Delete(sourcePath);
        }

        partialLength = File.Exists(incompletePath) ? new FileInfo(incompletePath).Length : 0;
    }

    /// <summary>Runs one crash-recovery step atomically with respect to in-flight prepared final-file moves.</summary>
    /// <param name="action">The inspection and recovery action to serialize against <see cref="SaveToFile"/>.</param>
    internal void RunFinalizationExclusive(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (finalizationSync)
        {
            action();
        }
    }

    /// <summary>
    /// Returns a persisted incomplete-file path unchanged when it is inside the current container, or the
    /// identity-derived current-container path when an app update regenerated the container UUID.
    /// </summary>
    /// <param name="persistedPath">The absolute incomplete path captured before a possible app update.</param>
    /// <param name="username">The remote Soulseek username that owns the partial.</param>
    /// <param name="remoteFilename">The exact full remote filename.</param>
    /// <returns>A containment-validated absolute path inside the current Incomplete directory.</returns>
    internal string RebaseIncompletePath(string persistedPath, string username, string remoteFilename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistedPath);
        try
        {
            return RequireContainedFile(IncompletePath, persistedPath, nameof(persistedPath));
        }
        catch (UnauthorizedAccessException)
        {
            return RequireContainedFile(
                IncompletePath,
                Path.Combine(IncompletePath, BuildIncompleteRelativePath(username, remoteFilename)),
                nameof(persistedPath));
        }
    }

    /// <summary>Deletes a partial file only when its canonical path is inside the private incomplete directory.</summary>
    /// <param name="path">The candidate incomplete-file path.</param>
    /// <returns><see langword="true"/> when an existing file was deleted.</returns>
    internal bool DeleteIncompleteFile(string path)
    {
        string safePath = RequireContainedFile(IncompletePath, path, nameof(path));
        if (!File.Exists(safePath))
        {
            return false;
        }

        File.Delete(safePath);
        string canonicalRoot = Path.GetFullPath(IncompletePath).TrimEnd(Path.DirectorySeparatorChar);
        DirectoryInfo? parent = new FileInfo(safePath).Directory;
        while (parent is not null &&
               !string.Equals(parent.FullName, canonicalRoot, StringComparison.Ordinal) &&
               !parent.EnumerateFileSystemInfos().Any())
        {
            DirectoryInfo? next = parent.Parent;
            parent.Delete();
            parent = next;
        }

        return true;
    }

    private static string GetDirectory(NSSearchPathDirectory directory) =>
        NSSearchPath.GetDirectories(directory, NSSearchPathDomain.User, expandTilde: true).First();

    private void DeleteAmbiguousLegacyPartials(
        string username,
        string remotePath,
        int depth,
        string canonicalPath)
    {
        string[] legacyRelativePaths =
        [
            BuildRelativePath(username, remotePath, depth, noSubFolder: false, includeUsername: false) + ".part",
            BuildRelativePath(username, remotePath, depth, noSubFolder: false, includeUsername: true) + ".part",
        ];

        foreach (string legacyRelativePath in legacyRelativePaths.Distinct(StringComparer.Ordinal))
        {
            string legacyPath = RequireContainedFile(
                IncompletePath,
                Path.Combine(IncompletePath, legacyRelativePath),
                nameof(remotePath));
            if (!string.Equals(legacyPath, canonicalPath, StringComparison.Ordinal) && File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
                DeleteEmptyParents(IncompletePath, Path.GetDirectoryName(legacyPath));
            }
        }
    }

    private static string BuildIncompleteRelativePath(string username, string remotePath)
    {
        string identity = $"{username.Length}:{username}{remotePath.Length}:{remotePath}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine("v2", hash[..2], hash + ".part");
    }

    private static string BuildCollisionDestination(string baseDestination, int collisionIndex)
    {
        if (collisionIndex == 1)
        {
            return baseDestination;
        }

        string directory = Path.GetDirectoryName(baseDestination) ?? throw new IOException("Final path has no parent.");
        string filename = Path.GetFileName(baseDestination);
        string stem = Path.GetFileNameWithoutExtension(filename);
        string extension = Path.GetExtension(filename);
        return Path.Combine(directory, $"{stem} ({collisionIndex}){extension}");
    }

    private static string BuildRelativePath(string username, string remotePath, int depth, bool noSubFolder)
        => BuildRelativePath(
            username,
            remotePath,
            depth,
            noSubFolder,
            includeUsername: PreferencesState.CreateUsernameSubfolders);

    private static string BuildRelativePath(
        string username,
        string remotePath,
        int depth,
        bool noSubFolder,
        bool includeUsername)
    {
        string[] parts = SplitRemotePath(remotePath);
        string filename = SanitizeSegment(parts.LastOrDefault() ?? "download");
        int folderCount = noSubFolder ? 0 : Math.Clamp(depth, 0, Math.Max(0, parts.Length - 1));
        IEnumerable<string> folders = parts
            .Skip(Math.Max(0, parts.Length - 1 - folderCount))
            .Take(folderCount)
            .Select(SanitizeSegment);

        if (includeUsername)
        {
            folders = new[] { SanitizeSegment(username) }.Concat(folders);
        }

        return folders.Append(filename).Aggregate(Path.Combine);
    }

    private static void DeleteEmptyParents(string root, string? directory)
    {
        string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var current = directory is null ? null : new DirectoryInfo(directory);
        while (current is not null &&
               !string.Equals(current.FullName, canonicalRoot, StringComparison.Ordinal) &&
               !current.EnumerateFileSystemInfos().Any())
        {
            DirectoryInfo? parent = current.Parent;
            current.Delete();
            current = parent;
        }
    }

    private static string[] SplitRemotePath(string path) =>
        (path ?? string.Empty)
            .Split(RemoteSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string sanitized = string.Concat(segment.Where(character => !invalid.Contains(character) && character != ':')).Trim();
        return string.IsNullOrEmpty(sanitized) || sanitized is "." or ".." ? "untitled" : sanitized;
    }

    private static string RequireContainedFile(string root, string candidate, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate, parameterName);
        string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string canonicalCandidate = Path.GetFullPath(candidate);
        if (!canonicalCandidate.StartsWith(canonicalRoot, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"The path must remain inside '{root}'.");
        }

        return canonicalCandidate;
    }
}

/// <summary>Describes the two filesystem sides of one prepared final-file move after restart.</summary>
/// <param name="SourceExists">Whether the private incomplete source still exists.</param>
/// <param name="TargetExists">Whether the exact selected Documents target exists.</param>
/// <param name="TargetLength">The current target length, or zero when absent.</param>
/// <param name="FinalPath">The containment-validated absolute Documents path.</param>
/// <param name="FinalUri">The Files-compatible URI for the target.</param>
internal readonly record struct DownloadFinalizationFileSnapshot(
    bool SourceExists,
    bool TargetExists,
    long TargetLength,
    string FinalPath,
    string FinalUri);
