using Soulseek;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Common.Share
{
    /// <summary>
    /// Describes one file in an immutable shared-file catalog.
    /// </summary>
    /// <remarks>
    /// <paramref name="RemotePath"/> is the exact Soulseek path advertised to peers. <paramref name="LocalPath"/>
    /// is an opaque platform path which must already have been containment-validated by the platform indexer.
    /// </remarks>
    public sealed class SharedFileCatalogEntry
    {
        /// <summary>Creates a catalog entry after validating its durable metadata.</summary>
        /// <param name="remotePath">The non-empty, relative Soulseek path using either slash style.</param>
        /// <param name="localPath">The non-empty platform path used to open the file.</param>
        /// <param name="size">The file size in bytes.</param>
        /// <param name="bitrate">The optional audio bitrate.</param>
        /// <param name="durationSeconds">The optional audio duration in seconds.</param>
        /// <exception cref="ArgumentException">A path is empty or the remote path contains traversal.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is negative.</exception>
        public SharedFileCatalogEntry(
            string remotePath,
            string localPath,
            long size,
            int? bitrate = null,
            int? durationSeconds = null)
        {
            RemotePath = SharedFileCatalog.NormalizeRemotePath(remotePath);
            if (string.IsNullOrWhiteSpace(localPath))
            {
                throw new ArgumentException("A local shared-file path is required.", nameof(localPath));
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "A shared-file size cannot be negative.");
            }

            LocalPath = localPath;
            Size = size;
            Bitrate = bitrate is > 0 ? bitrate : null;
            DurationSeconds = durationSeconds is > 0 ? durationSeconds : null;
        }

        /// <summary>Gets the exact Soulseek path advertised to peers.</summary>
        public string RemotePath { get; }

        /// <summary>Gets the platform path validated by the indexer.</summary>
        public string LocalPath { get; }

        /// <summary>Gets the indexed size in bytes.</summary>
        public long Size { get; }

        /// <summary>Gets the optional audio bitrate.</summary>
        public int? Bitrate { get; }

        /// <summary>Gets the optional audio duration in seconds.</summary>
        public int? DurationSeconds { get; }
    }

    /// <summary>
    /// Provides an immutable, detached view of a platform share root for Soulseek search, browse, and upload lookup.
    /// </summary>
    /// <remarks>
    /// Construction copies every input and precomputes protocol objects. Consumers can safely retain a catalog while a
    /// platform service atomically replaces its current catalog with a refreshed instance.
    /// </remarks>
    public sealed class SharedFileCatalog
    {
        private readonly ReadOnlyCollection<SharedFileCatalogEntry> entries;
        private readonly ReadOnlyCollection<SharedFileCatalogEntry> skippedEntries;
        private readonly ReadOnlyCollection<Soulseek.Directory> directories;
        private readonly Dictionary<string, SharedFileCatalogEntry> entriesByRemotePath;
        private readonly Dictionary<string, Soulseek.Directory> directoriesByName;
        private readonly Dictionary<string, Soulseek.File> searchFilesByRemotePath;
        private readonly string rootDirectoryName;

        /// <summary>Creates a detached immutable catalog.</summary>
        /// <param name="source">The entries to copy.</param>
        /// <param name="rootDirectoryName">
        /// The directory name advertised for files that sit directly in the share root.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="rootDirectoryName"/> is empty.</exception>
        /// <remarks>
        /// Entries whose normalized remote paths collide case-insensitively (for example APFS-legal names
        /// differing only in case or trailing segment whitespace) do not fail construction: the first entry in
        /// case-insensitive remote-path order wins deterministically and every conflicting entry is excluded
        /// from the catalog and reported through <see cref="SkippedEntries"/> so the platform indexer can log it.
        /// </remarks>
        public SharedFileCatalog(IEnumerable<SharedFileCatalogEntry> source, string rootDirectoryName = "Documents")
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(rootDirectoryName))
            {
                throw new ArgumentException("A root directory display name is required.", nameof(rootDirectoryName));
            }

            this.rootDirectoryName = rootDirectoryName;
            var accepted = new List<SharedFileCatalogEntry>();
            var skipped = new List<SharedFileCatalogEntry>();
            entriesByRemotePath = new Dictionary<string, SharedFileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            searchFilesByRemotePath = new Dictionary<string, Soulseek.File>(StringComparer.OrdinalIgnoreCase);
            foreach (SharedFileCatalogEntry entry in source
                .OrderBy(candidate => candidate.RemotePath, StringComparer.OrdinalIgnoreCase))
            {
                if (entriesByRemotePath.ContainsKey(entry.RemotePath))
                {
                    skipped.Add(entry);
                    continue;
                }

                entriesByRemotePath.Add(entry.RemotePath, entry);
                searchFilesByRemotePath.Add(entry.RemotePath, CreateSoulseekFile(entry, entry.RemotePath));
                accepted.Add(entry);
            }

            SharedFileCatalogEntry[] snapshot = accepted.ToArray();
            entries = Array.AsReadOnly(snapshot);
            skippedEntries = Array.AsReadOnly(skipped.ToArray());
            Soulseek.Directory[] directorySnapshot = snapshot
                .GroupBy(GetDirectoryName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new Soulseek.Directory(
                    group.Key,
                    group.Select(entry => CreateSoulseekFile(entry, GetFileName(entry.RemotePath)))))
                .ToArray();
            directories = Array.AsReadOnly(directorySnapshot);
            directoriesByName = directorySnapshot.ToDictionary(
                directory => directory.Name,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Gets a shared empty catalog.</summary>
        public static SharedFileCatalog Empty { get; } = new SharedFileCatalog(Array.Empty<SharedFileCatalogEntry>());

        /// <summary>Gets the detached entries in stable remote-path order.</summary>
        public IReadOnlyList<SharedFileCatalogEntry> Entries => entries;

        /// <summary>
        /// Gets the entries excluded because another entry already claimed the same case-insensitive remote path.
        /// </summary>
        public IReadOnlyList<SharedFileCatalogEntry> SkippedEntries => skippedEntries;

        /// <summary>Gets the number of advertised directories.</summary>
        public int DirectoryCount => directories.Count;

        /// <summary>Gets the number of advertised files.</summary>
        public int FileCount => entries.Count;

        /// <summary>Builds a browse response over the immutable directory snapshot.</summary>
        /// <returns>A response containing every advertised directory and no locked directories.</returns>
        public BrowseResponse CreateBrowseResponse() => new BrowseResponse(directories);

        /// <summary>Finds the exact directory requested by a peer.</summary>
        /// <param name="remoteDirectory">The directory name from the Soulseek request.</param>
        /// <param name="directory">Receives the immutable directory object when found.</param>
        /// <returns><see langword="true"/> when the directory exists.</returns>
        public bool TryGetDirectory(string remoteDirectory, out Soulseek.Directory directory)
        {
            directory = null!;
            if (!TryNormalizeRemotePath(remoteDirectory, out string normalized))
            {
                return false;
            }

            return directoriesByName.TryGetValue(normalized, out directory!);
        }

        /// <summary>Resolves only an exact advertised remote path to its platform entry.</summary>
        /// <param name="remotePath">The path supplied by the peer.</param>
        /// <param name="entry">Receives the indexed entry when found.</param>
        /// <returns><see langword="true"/> when the normalized path is present.</returns>
        public bool TryGetEntry(string remotePath, out SharedFileCatalogEntry entry)
        {
            entry = null!;
            if (!TryNormalizeRemotePath(remotePath, out string normalized))
            {
                return false;
            }

            return entriesByRemotePath.TryGetValue(normalized, out entry!);
        }

        /// <summary>Returns files matching every query term and no exclusion, up to the requested limit.</summary>
        /// <param name="query">The parsed Soulseek query.</param>
        /// <param name="limit">The maximum number of files to return.</param>
        /// <returns>Search-form files whose filenames contain their complete advertised path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than one.</exception>
        public IReadOnlyList<Soulseek.File> Search(SearchQuery query, int limit)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (limit < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), limit, "The result limit must be positive.");
            }

            string[] terms = query.Terms.Where(term => !string.IsNullOrWhiteSpace(term)).ToArray();
            string[] exclusions = query.Exclusions.Where(term => !string.IsNullOrWhiteSpace(term)).ToArray();
            if (terms.Length == 0)
            {
                return Array.Empty<Soulseek.File>();
            }

            return entries
                .Where(entry => terms.All(term => Contains(entry.RemotePath, term)))
                .Where(entry => exclusions.All(term => !Contains(entry.RemotePath, term)))
                .Where(entry => !SearchUtil.ShouldExcludeFile(entry.RemotePath))
                .Take(limit)
                .Select(entry => searchFilesByRemotePath[entry.RemotePath])
                .ToArray();
        }

        /// <summary>Normalizes a remote path or throws when it is empty, rooted, or contains traversal.</summary>
        /// <param name="remotePath">The peer-facing path.</param>
        /// <returns>A backslash-separated relative path.</returns>
        /// <exception cref="ArgumentException">The path is invalid.</exception>
        public static string NormalizeRemotePath(string remotePath)
        {
            if (!TryNormalizeRemotePath(remotePath, out string normalized))
            {
                throw new ArgumentException("A remote shared-file path must be relative and cannot contain traversal.", nameof(remotePath));
            }

            return normalized;
        }

        private static bool TryNormalizeRemotePath(string remotePath, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(remotePath) ||
                remotePath.StartsWith("/", StringComparison.Ordinal) ||
                remotePath.StartsWith("\\", StringComparison.Ordinal) ||
                Path.IsPathRooted(remotePath))
            {
                return false;
            }

            string[] segments = remotePath
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .ToArray();
            if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
            {
                return false;
            }

            normalized = string.Join("\\", segments);
            return true;
        }

        private static bool Contains(string value, string fragment) =>
            value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

        private string GetDirectoryName(SharedFileCatalogEntry entry)
        {
            int separator = entry.RemotePath.LastIndexOf('\\');
            return separator > 0 ? entry.RemotePath.Substring(0, separator) : rootDirectoryName;
        }

        private static string GetFileName(string remotePath)
        {
            int separator = remotePath.LastIndexOf('\\');
            return separator >= 0 ? remotePath.Substring(separator + 1) : remotePath;
        }

        private static Soulseek.File CreateSoulseekFile(SharedFileCatalogEntry entry, string filename)
        {
            var attributes = new List<FileAttribute>(2);
            if (entry.Bitrate.HasValue)
            {
                attributes.Add(new FileAttribute(FileAttributeType.BitRate, entry.Bitrate.Value));
            }

            if (entry.DurationSeconds.HasValue)
            {
                attributes.Add(new FileAttribute(FileAttributeType.Length, entry.DurationSeconds.Value));
            }

            return new Soulseek.File(
                1,
                filename,
                entry.Size,
                Path.GetExtension(filename),
                attributes);
        }
    }
}
