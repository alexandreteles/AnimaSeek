using AnimaSeek.iOS.UI.Presentation;
using Common;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Soulseek;
using SoulseekDirectory = Soulseek.Directory;
using SoulseekFile = Soulseek.File;

namespace AnimaSeek.iOS.UI.Presentation.Browse;

/// <summary>Identifies the lifecycle of a user-share browse request.</summary>
internal enum BrowsePhase
{
    Idle,
    Contacting,
    Content,
    Empty,
    Offline,
    Canceled,
    TimedOut,
    DirectConnectionFailed,
    ParseFailed,
    Failed,
}

/// <summary>Describes one folder or file at the current browse location.</summary>
/// <param name="Id">The stable diffable identifier.</param>
/// <param name="IsFolder">Whether selecting the row drills into a directory.</param>
/// <param name="Title">The final path component shown as the primary label.</param>
/// <param name="Subtitle">A concise count, size, or format description.</param>
/// <param name="RemotePath">The exact remote path used for navigation and actions.</param>
/// <param name="Size">The file or recursively aggregated folder size.</param>
/// <param name="FileCount">The file or recursive descendant count.</param>
/// <param name="IsLocked">Whether access is limited by the remote peer.</param>
internal sealed record BrowseRow(
    string Id,
    bool IsFolder,
    string Title,
    string Subtitle,
    string RemotePath,
    long Size,
    int FileCount,
    bool IsLocked);

/// <summary>Describes the rows and totals at a single location in the loaded share hierarchy.</summary>
/// <param name="Username">The remote user.</param>
/// <param name="Location">The normalized full remote folder path, or an empty root location.</param>
/// <param name="DisplayPath">The concise path presented below the navigation title.</param>
/// <param name="Rows">Filtered immediate child folders and files.</param>
/// <param name="VisibleCount">The number of displayed rows.</param>
/// <param name="UnfilteredCount">The number of rows before local filtering.</param>
internal sealed record BrowseLocationSnapshot(
    string Username,
    string Location,
    string DisplayPath,
    IReadOnlyList<BrowseRow> Rows,
    int VisibleCount,
    int UnfilteredCount);

/// <summary>Reports the explicit row scope and file acceptance of one browse multi-selection queue action.</summary>
/// <param name="SelectedRowCount">The still-valid selected rows addressed by the action.</param>
/// <param name="FileCount">The distinct visible files represented by those files and recursive folders.</param>
/// <param name="AcceptedCount">The files accepted by the session download queue.</param>
internal sealed record BrowseBatchQueueResult(
    int SelectedRowCount,
    int FileCount,
    int AcceptedCount);

/// <summary>Represents the complete immutable state shared by all controllers in one browse navigation stack.</summary>
/// <param name="Phase">The current request lifecycle.</param>
/// <param name="Username">The preserved username.</param>
/// <param name="History">Recently browsed users, most recent first.</param>
/// <param name="DirectoryCount">The number of returned directories.</param>
/// <param name="FileCount">The number of returned files.</param>
/// <param name="Message">An actionable failure or status detail.</param>
internal sealed record BrowseScreenState(
    BrowsePhase Phase,
    string Username,
    IReadOnlyList<string> History,
    int DirectoryCount,
    int FileCount,
    string? Message)
{
    /// <summary>Creates the first-use Browse state.</summary>
    /// <param name="history">Restored recent usernames.</param>
    public static BrowseScreenState Initial(IReadOnlyList<string> history) => new(
        BrowsePhase.Idle,
        string.Empty,
        history,
        0,
        0,
        null);
}

/// <summary>
/// Owns generation-safe browse requests, a reusable folder index, history, filtering, and download commands.
/// </summary>
internal sealed class BrowsePresentationStore : FeaturePresentationStore, IDisposable
{
    private const string HistoryKey = "AnimaSeek.UI.Browse.History.v1";
    private const int MaximumHistoryCount = 20;
    private readonly AppSession session;
    private readonly IKeyValueStore keyValueStore;
    private readonly Lock stateSync = new();
    private readonly Dictionary<string, DirectoryEntry> directories = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? activeRequest;
    private string hiddenShareRoot = string.Empty;
    private string loadedUsername = string.Empty;
    private bool hasLoadedResponse;
    private long generation;
    private bool disposed;

    /// <summary>Creates a Browse store backed by the UI-safe session and bounded history persistence.</summary>
    /// <param name="session">The application session façade.</param>
    /// <param name="keyValueStore">The store used for recent-user history.</param>
    public BrowsePresentationStore(AppSession session, IKeyValueStore keyValueStore)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        State = BrowseScreenState.Initial(RestoreHistory());
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Gets the latest request-level Browse state.</summary>
    public BrowseScreenState State { get; private set; }

    /// <summary>Gets whether the current username has a successfully loaded response available for navigation.</summary>
    public bool HasLoadedResponseForCurrentUser
    {
        get
        {
            lock (stateSync)
            {
                return HasCurrentCacheLocked();
            }
        }
    }

    /// <summary>Raised on the UI context when request state or loaded share content changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Requests and indexes a remote user's complete share response without allowing a late result to replace a newer user.</summary>
    /// <param name="username">The non-empty remote username.</param>
    /// <param name="cancellationToken">Cancels the request in addition to a later superseding request.</param>
    public async Task BrowseAsync(string username, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        username = username?.Trim() ?? string.Empty;
        if (username.Length == 0)
        {
            throw new ArgumentException(StringResources.Get("must_type_a_username_to_browse"), nameof(username));
        }

        CancellationTokenSource requestCancellation;
        long requestedGeneration;
        lock (stateSync)
        {
            activeRequest?.Cancel();
            activeRequest?.Dispose();
            activeRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCancellation = activeRequest;
            requestedGeneration = ++generation;
            bool retainCurrentCache = hasLoadedResponse &&
                string.Equals(username, loadedUsername, StringComparison.OrdinalIgnoreCase);
            State = State with
            {
                Phase = session.IsConnected
                    ? BrowsePhase.Contacting
                    : retainCurrentCache
                        ? directories.Count == 0 ? BrowsePhase.Empty : BrowsePhase.Content
                        : BrowsePhase.Offline,
                Username = username,
                Message = session.IsConnected ? null : StringResources.Get("must_be_logged_to_browse"),
            };
        }

        Publish(Changed);
        if (!session.IsConnected)
        {
            lock (stateSync)
            {
                if (ReferenceEquals(activeRequest, requestCancellation))
                {
                    activeRequest = null;
                }
            }

            requestCancellation.Dispose();
            return;
        }

        try
        {
            BrowseResponse response = await session.BrowseAsync(username, requestCancellation.Token);
            DirectoryIndex index = await Task.Run(() => BuildIndex(response), requestCancellation.Token);
            lock (stateSync)
            {
                if (requestedGeneration != generation || requestCancellation.IsCancellationRequested)
                {
                    return;
                }

                directories.Clear();
                foreach ((string path, DirectoryEntry directory) in index.Directories)
                {
                    directories[path] = directory;
                }

                hiddenShareRoot = index.HiddenShareRoot;
                loadedUsername = username;
                hasLoadedResponse = true;
                State = State with
                {
                    Phase = index.FileCount == 0 && index.Directories.Count == 0
                        ? BrowsePhase.Empty
                        : BrowsePhase.Content,
                    DirectoryCount = index.Directories.Values.Count(entry => !entry.IsSynthetic),
                    FileCount = index.FileCount,
                    History = AddHistory(username),
                    Message = null,
                };
            }

            Publish(Changed);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            PublishFailure(requestedGeneration, BrowsePhase.Canceled, StringResources.Get("Cancelled"));
        }
        catch (TimeoutException)
        {
            PublishFailure(
                requestedGeneration,
                BrowsePhase.TimedOut,
                StringResources.Get("IosUiBrowseTimedOutDetail"));
        }
        catch (Exception exception)
        {
            BrowsePhase phase = session.IsConnected ? ClassifyFailure(exception) : BrowsePhase.Offline;
            string message = phase switch
            {
                BrowsePhase.Offline => StringResources.Get("must_be_logged_to_browse"),
                BrowsePhase.DirectConnectionFailed => StringResources.Get("IosUiBrowseDirectFailedDetail"),
                BrowsePhase.ParseFailed => StringResources.Get("IosUiBrowseParseFailedDetail"),
                BrowsePhase.TimedOut => StringResources.Get("IosUiBrowseTimedOutDetail"),
                _ => StringResources.Get("IosUiBrowseFailedDetail"),
            };

            PublishFailure(requestedGeneration, phase, message);
        }
        finally
        {
            lock (stateSync)
            {
                if (ReferenceEquals(activeRequest, requestCancellation))
                {
                    activeRequest = null;
                }
            }

            requestCancellation.Dispose();
        }
    }

    /// <summary>Cancels the current peer request without changing a newer request generation.</summary>
    public void CancelRequest()
    {
        lock (stateSync)
        {
            activeRequest?.Cancel();
        }
    }

    /// <summary>Builds a detached, filtered snapshot for one folder without mutating navigation state.</summary>
    /// <param name="location">The full remote path, or an empty value for the share root.</param>
    /// <param name="filter">Optional case-insensitive text filter for immediate child rows.</param>
    /// <returns>The current location snapshot; an empty snapshot is returned for a location that disappeared.</returns>
    public BrowseLocationSnapshot GetLocation(string? location, string? filter = null)
    {
        location = NormalizePath(location);
        filter = filter?.Trim() ?? string.Empty;
        lock (stateSync)
        {
            if (State.Phase is not (BrowsePhase.Content or BrowsePhase.Empty) && !HasCurrentCacheLocked())
            {
                return new BrowseLocationSnapshot(State.Username, location, DisplayPath(location), [], 0, 0);
            }

            string effectiveLocation = ResolveIndexedPath(location, expectFile: false);
            DirectoryEntry? current = effectiveLocation.Length == 0
                ? null
                : directories.GetValueOrDefault(effectiveLocation);
            bool hideLocked = PreferencesState.HideLockedResultsInBrowse;
            IEnumerable<DirectoryEntry> childDirectories = directories.Values.Where(entry =>
                string.Equals(entry.ParentPath, effectiveLocation, StringComparison.OrdinalIgnoreCase) &&
                (!hideLocked || !IsEffectivelyLockedFolder(entry)));
            var unfilteredRows = new List<BrowseRow>();
            foreach (DirectoryEntry child in childDirectories.OrderBy(
                         entry => entry.DisplayName,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                (int fileCount, long size) = AggregateFolder(child.Path);
                bool isLocked = IsEffectivelyLockedFolder(child);
                unfilteredRows.Add(new BrowseRow(
                    StableId("folder", child.Path, isLocked.ToString()),
                    true,
                    child.DisplayName,
                    StringResources.Format("IosUiBrowseFolderMetadata", fileCount, FeatureValueFormatter.Bytes(size)),
                    child.Path,
                    size,
                    fileCount,
                    isLocked));
            }

            if (current is not null)
            {
                unfilteredRows.AddRange(current.Files
                    .Where(file => !hideLocked || !file.IsLocked)
                    .OrderBy(file => file.File.Filename, StringComparer.CurrentCultureIgnoreCase)
                    .Select(file => new BrowseRow(
                        StableId("file", current.Path, file.File.Filename, file.IsLocked.ToString()),
                        false,
                        FeatureValueFormatter.FileName(file.File.Filename),
                        BuildFileMetadata(file.File),
                        CombineRemotePath(current.Path, file.File.Filename),
                        file.File.Size,
                        1,
                        file.IsLocked)));
            }

            BrowseRow[] filteredRows = filter.Length == 0
                ? [.. unfilteredRows]
                : unfilteredRows.Where(row =>
                    row.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                    row.RemotePath.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                    .ToArray();
            return new BrowseLocationSnapshot(
                State.Username,
                location,
                DisplayPath(location),
                filteredRows,
                filteredRows.Length,
                unfilteredRows.Count);
        }
    }

    /// <summary>Resolves a route or link folder path to the exact loaded hierarchy path, including a hidden share root.</summary>
    /// <param name="location">A normalized user-visible or protocol folder path.</param>
    /// <returns>The exact indexed path, or the normalized input if no equivalent path exists.</returns>
    public string ResolveLocation(string? location)
    {
        lock (stateSync)
        {
            return ResolveIndexedPath(NormalizePath(location), expectFile: false);
        }
    }

    /// <summary>Resolves a routed file or folder target to the exact loaded protocol path.</summary>
    /// <param name="target">The link's normalized remote target.</param>
    /// <returns>The exact indexed target, or the normalized input when it cannot be matched.</returns>
    public string ResolveDownloadTarget(string? target)
    {
        lock (stateSync)
        {
            string normalized = NormalizePath(target);
            return ResolveIndexedPath(normalized, expectFile: FindFileLocked(normalized) is not null);
        }
    }

    /// <summary>Gets whether a routed file or folder still exists and is visible under the current locked-share preference.</summary>
    /// <param name="target">The exact or hidden-root-relative remote target.</param>
    /// <returns><see langword="true"/> only for a current, downloadable visible target.</returns>
    public bool IsDownloadTargetAvailable(string? target)
    {
        lock (stateSync)
        {
            if (!HasCurrentCacheLocked())
            {
                return false;
            }

            string normalized = NormalizePath(target);
            string resolvedFile = ResolveIndexedPath(normalized, expectFile: true);
            if (FindFileLocked(resolvedFile) is { } file)
            {
                return !PreferencesState.HideLockedResultsInBrowse || !file.IsLocked;
            }

            string resolvedFolder = ResolveIndexedPath(normalized, expectFile: false);
            if (resolvedFolder.Length == 0)
            {
                return true;
            }

            return directories.GetValueOrDefault(resolvedFolder) is { } folder &&
                (!PreferencesState.HideLockedResultsInBrowse || !IsEffectivelyLockedFolder(folder));
        }
    }

    /// <summary>Queues a single visible file and returns after the session accepts local startup.</summary>
    /// <param name="remotePath">The exact full remote file path.</param>
    /// <param name="cancellationToken">Cancels startup before acceptance.</param>
    public async Task<DownloadAcceptance> QueueFileAsync(
        string remotePath,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BrowseFileEntry entry;
        string username;
        lock (stateSync)
        {
            if (!HasCurrentCacheLocked())
            {
                throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
            }

            remotePath = ResolveIndexedPath(NormalizePath(remotePath), expectFile: true);
            entry = FindFileLocked(remotePath) ??
                throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
            username = loadedUsername;
        }
        IReadOnlyList<DownloadAcceptance> acceptances = await session.QueueDownloadsAsync(
            username,
            [CreateFullFileInfo(remotePath, entry.File, depth: 1)],
            queuePaused,
            cancellationToken);
        return acceptances[0];
    }

    /// <summary>Queues every file in a folder, optionally including descendants, and returns after local startup is dispatched.</summary>
    /// <param name="folderPath">The exact remote folder path, or an empty value for the loaded share root.</param>
    /// <param name="recursive">Whether descendant folder files are included.</param>
    /// <param name="cancellationToken">Cancels startup dispatch.</param>
    /// <returns>The number of files accepted for dispatch.</returns>
    public async Task<int> QueueFolderAsync(
        string? folderPath,
        bool recursive,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        folderPath = NormalizePath(folderPath);
        string effectivePath;
        string username;
        BrowseDownloadCandidate[] files;
        lock (stateSync)
        {
            if (!HasCurrentCacheLocked())
            {
                throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
            }

            effectivePath = ResolveIndexedPath(folderPath, expectFile: false);
            bool hideLocked = PreferencesState.HideLockedResultsInBrowse;
            files = directories.Values
                .Where(directory => recursive
                    ? IsSameOrDescendant(directory.Path, effectivePath)
                    : string.Equals(directory.Path, effectivePath, StringComparison.OrdinalIgnoreCase))
                .SelectMany(directory => directory.Files
                    .Where(file => !hideLocked || !file.IsLocked)
                    .Select(file => new BrowseDownloadCandidate(
                    CombineRemotePath(directory.Path, file.File.Filename),
                    file.File)))
                .DistinctBy(candidate => candidate.RemotePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            username = loadedUsername;
        }

        if (files.Length == 0)
        {
            return 0;
        }

        int baseDepth = effectivePath.Length == 0 ? 0 : effectivePath.Count(character => character == '\\') + 1;
        FullFileInfo[] queuedFiles = files
            .OrderBy(candidate => candidate.RemotePath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => CreateFullFileInfo(
                candidate.RemotePath,
                candidate.File,
                Math.Max(1, FeatureValueFormatter.ParentPath(candidate.RemotePath)
                    .Count(character => character == '\\') + 2 - baseDepth)))
            .ToArray();
        IReadOnlyList<DownloadAcceptance> accepted = await session.QueueDownloadsAsync(
            username,
            queuedFiles,
            queuePaused,
            cancellationToken);
        return accepted.Count;
    }

    /// <summary>Queues the distinct visible files represented by selected files and recursive folders.</summary>
    /// <param name="location">The folder whose immediate rows supplied the stable selection.</param>
    /// <param name="rowIds">Selected stable row identifiers; stale or hidden rows are ignored.</param>
    /// <param name="queuePaused">Whether accepted files remain paused instead of starting immediately.</param>
    /// <param name="cancellationToken">Cancels queue acceptance before it completes.</param>
    /// <returns>Counts for selected rows, represented files, and accepted downloads.</returns>
    public async Task<BrowseBatchQueueResult> QueueSelectionAsync(
        string? location,
        IReadOnlyCollection<string> rowIds,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ObjectDisposedException.ThrowIf(disposed, this);
        BrowseLocationSnapshot snapshot = GetLocation(location);
        HashSet<string> requestedIds = rowIds.ToHashSet(StringComparer.Ordinal);
        BrowseRow[] selectedRows = snapshot.Rows.Where(row => requestedIds.Contains(row.Id)).ToArray();
        if (selectedRows.Length == 0)
        {
            return new BrowseBatchQueueResult(0, 0, 0);
        }

        BrowseDownloadCandidate[] candidates;
        string username;
        string effectiveLocation;
        lock (stateSync)
        {
            if (!HasCurrentCacheLocked())
            {
                return new BrowseBatchQueueResult(0, 0, 0);
            }

            effectiveLocation = ResolveIndexedPath(NormalizePath(location), expectFile: false);
            bool hideLocked = PreferencesState.HideLockedResultsInBrowse;
            candidates = selectedRows
                .SelectMany(row => DownloadCandidatesForRowLocked(row, hideLocked))
                .DistinctBy(candidate => candidate.RemotePath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(candidate => candidate.RemotePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            username = loadedUsername;
        }

        if (candidates.Length == 0)
        {
            return new BrowseBatchQueueResult(selectedRows.Length, 0, 0);
        }

        int baseDepth = PathDepth(effectiveLocation);
        FullFileInfo[] files = candidates.Select(candidate => CreateFullFileInfo(
            candidate.RemotePath,
            candidate.File,
            Math.Max(1, PathDepth(ParentPath(candidate.RemotePath)) - baseDepth + 1)))
            .ToArray();
        IReadOnlyList<DownloadAcceptance> accepted = await session.QueueDownloadsAsync(
            username,
            files,
            queuePaused,
            cancellationToken);
        return new BrowseBatchQueueResult(selectedRows.Length, files.Length, accepted.Count);
    }

    /// <summary>Returns aggregate file count and byte size for confirmation UI.</summary>
    /// <param name="folderPath">The exact remote folder path, or empty for root.</param>
    /// <param name="recursive">Whether descendants are included.</param>
    /// <returns>The matching file count and aggregate size.</returns>
    public (int FileCount, long Size) GetDownloadSummary(string? folderPath, bool recursive)
    {
        folderPath = NormalizePath(folderPath);
        lock (stateSync)
        {
            if (!HasCurrentCacheLocked())
            {
                return (0, 0);
            }

            string effectivePath = ResolveIndexedPath(folderPath, expectFile: false);
            bool hideLocked = PreferencesState.HideLockedResultsInBrowse;
            BrowseFileEntry[] files = directories.Values
                .Where(directory => recursive
                    ? IsSameOrDescendant(directory.Path, effectivePath)
                    : string.Equals(directory.Path, effectivePath, StringComparison.OrdinalIgnoreCase))
                .SelectMany(directory => directory.Files.Where(file => !hideLocked || !file.IsLocked))
                .ToArray();
            return (files.Length, files.Aggregate(0L, (sum, file) => SaturatingAdd(sum, file.File.Size)));
        }
    }

    /// <summary>Clears recent browse-user history without discarding loaded content.</summary>
    public void ClearHistory()
    {
        keyValueStore.PutString(HistoryKey, null);
        keyValueStore.Flush();
        State = State with { History = [] };
        Publish(Changed);
    }

    /// <summary>Cancels requests and releases session event subscriptions.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.StateChanged -= OnSessionStateChanged;
        activeRequest?.Cancel();
        activeRequest?.Dispose();
        activeRequest = null;
    }

    private void OnSessionStateChanged(object? sender, EventArgs args)
    {
        lock (stateSync)
        {
            if (!session.IsConnected && State.Phase != BrowsePhase.Idle)
            {
                State = State with
                {
                    Phase = HasCurrentCacheLocked()
                        ? directories.Count == 0 ? BrowsePhase.Empty : BrowsePhase.Content
                        : BrowsePhase.Offline,
                    Message = StringResources.Get("must_be_logged_to_browse"),
                };
            }
            else if (session.IsConnected && State.Message is not null && HasCurrentCacheLocked())
            {
                State = State with
                {
                    Phase = directories.Count == 0 ? BrowsePhase.Empty : BrowsePhase.Content,
                    Message = null,
                };
            }
        }

        Publish(Changed);
    }

    private void PublishFailure(long requestedGeneration, BrowsePhase phase, string message)
    {
        lock (stateSync)
        {
            if (requestedGeneration != generation)
            {
                return;
            }

            State = State with
            {
                Phase = HasCurrentCacheLocked()
                    ? directories.Count == 0 ? BrowsePhase.Empty : BrowsePhase.Content
                    : phase,
                Message = message,
            };
        }

        Publish(Changed);
    }

    private DirectoryIndex BuildIndex(BrowseResponse response)
    {
        var source = response.Directories.Select(directory => (Directory: directory, Locked: false))
            .Concat(response.LockedDirectories.Select(directory => (Directory: directory, Locked: true)))
            .ToArray();
        string[] normalizedPaths = source
            .Select(item => NormalizePath(item.Directory.Name))
            .Where(path => path.Length > 0)
            .ToArray();
        string commonRoot = FindHiddenShareRoot(normalizedPaths);
        var index = new Dictionary<string, DirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach ((SoulseekDirectory directory, bool locked) in source)
        {
            string path = NormalizePath(directory.Name);
            if (path.Length == 0)
            {
                continue;
            }

            EnsureAncestors(index, path, commonRoot, locked);
            index[path] = new DirectoryEntry(
                path,
                ParentPath(path),
                FeatureValueFormatter.FileName(path),
                directory.Files.Select(file => new BrowseFileEntry(file, locked)).ToArray(),
                locked,
                false);
        }

        return new DirectoryIndex(index, commonRoot, source.Sum(item => item.Directory.Files.Count));
    }

    private static void EnsureAncestors(
        IDictionary<string, DirectoryEntry> index,
        string path,
        string commonRoot,
        bool locked)
    {
        string parent = ParentPath(path);
        while (parent.Length > 0 && !string.Equals(parent, commonRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (!index.ContainsKey(parent))
            {
                index[parent] = new DirectoryEntry(
                    parent,
                    ParentPath(parent),
                    FeatureValueFormatter.FileName(parent),
                    [],
                    locked,
                    true);
            }

            parent = ParentPath(parent);
        }

        if (commonRoot.Length > 0 && !index.ContainsKey(commonRoot))
        {
            index[commonRoot] = new DirectoryEntry(
                commonRoot,
                ParentPath(commonRoot),
                FeatureValueFormatter.FileName(commonRoot),
                [],
                false,
                true);
        }
    }

    private (int FileCount, long Size) AggregateFolder(string path)
    {
        bool hideLocked = PreferencesState.HideLockedResultsInBrowse;
        BrowseFileEntry[] files = directories.Values
            .Where(directory => IsSameOrDescendant(directory.Path, path))
            .SelectMany(directory => directory.Files.Where(file => !hideLocked || !file.IsLocked))
            .ToArray();
        return (files.Length, files.Aggregate(0L, (sum, file) => SaturatingAdd(sum, file.File.Size)));
    }

    private bool HasCurrentCacheLocked() => hasLoadedResponse &&
        string.Equals(State.Username, loadedUsername, StringComparison.OrdinalIgnoreCase);

    private bool IsEffectivelyLockedFolder(DirectoryEntry directory) =>
        directory.IsLocked &&
        (!directory.IsSynthetic || !directories.Values
            .Where(candidate => IsSameOrDescendant(candidate.Path, directory.Path))
            .SelectMany(candidate => candidate.Files)
            .Any(file => !file.IsLocked));

    private IEnumerable<BrowseDownloadCandidate> DownloadCandidatesForRowLocked(BrowseRow row, bool hideLocked)
    {
        if (row.IsFolder)
        {
            return directories.Values
                .Where(directory => IsSameOrDescendant(directory.Path, row.RemotePath))
                .SelectMany(directory => directory.Files
                    .Where(file => !hideLocked || !file.IsLocked)
                    .Select(file => new BrowseDownloadCandidate(
                        CombineRemotePath(directory.Path, file.File.Filename),
                        file.File)));
        }

        BrowseFileEntry? file = FindFileLocked(row.RemotePath);
        return file is not null && (!hideLocked || !file.IsLocked)
            ? [new BrowseDownloadCandidate(row.RemotePath, file.File)]
            : [];
    }

    private BrowseFileEntry? FindFileLocked(string remotePath)
    {
        string exact = ResolveIndexedPath(remotePath, expectFile: true);
        string parent = ParentPath(exact);
        string filename = FeatureValueFormatter.FileName(exact);
        return directories.GetValueOrDefault(parent)?.Files.FirstOrDefault(file =>
            string.Equals(file.File.Filename, filename, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveIndexedPath(string normalized, bool expectFile)
    {
        if (normalized.Length == 0)
        {
            return hiddenShareRoot;
        }

        bool exists = expectFile
            ? FileExists(normalized)
            : directories.ContainsKey(normalized);
        if (exists || hiddenShareRoot.Length == 0 ||
            normalized.StartsWith(hiddenShareRoot + "\\", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, hiddenShareRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        string prefixed = CombineRemotePath(hiddenShareRoot, normalized);
        return (expectFile ? FileExists(prefixed) : directories.ContainsKey(prefixed))
            ? prefixed
            : normalized;
    }

    private bool FileExists(string remotePath)
    {
        string parent = ParentPath(remotePath);
        string filename = FeatureValueFormatter.FileName(remotePath);
        return directories.GetValueOrDefault(parent)?.Files.Any(file =>
            string.Equals(file.File.Filename, filename, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private IReadOnlyList<string> AddHistory(string username)
    {
        List<string> history = RestoreHistory()
            .Where(item => !string.Equals(item, username, StringComparison.CurrentCultureIgnoreCase))
            .Prepend(username)
            .Take(MaximumHistoryCount)
            .ToList();
        keyValueStore.PutString(HistoryKey, SerializationHelper.SaveStringListToString(history));
        keyValueStore.Flush();
        return history;
    }

    private IReadOnlyList<string> RestoreHistory()
    {
        try
        {
            return SerializationHelper.RestoreStringListFromString(
                    keyValueStore.GetString(HistoryKey, string.Empty) ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(MaximumHistoryCount)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private string DisplayPath(string location)
    {
        if (location.Length == 0)
        {
            return State.Username;
        }

        return hiddenShareRoot.Length > 0 && location.StartsWith(hiddenShareRoot, StringComparison.OrdinalIgnoreCase)
            ? location[hiddenShareRoot.Length..].TrimStart('\\')
            : location;
    }

    private static string BuildFileMetadata(SoulseekFile file)
    {
        string detail = file.BitRate is { } bitrate
            ? StringResources.Format("IosUiBitrate", bitrate)
            : file.Extension?.ToUpperInvariant() ?? StringResources.Get("file");
        return StringResources.Format("IosUiBrowseFileMetadata", FeatureValueFormatter.Bytes(file.Size), detail);
    }

    private static FullFileInfo CreateFullFileInfo(string remotePath, SoulseekFile source, int depth) => new()
    {
        FullFileName = NormalizePath(remotePath),
        Size = source.Size,
        Depth = depth,
        wasFilenameLatin1Decoded = source.IsLatin1Decoded,
        wasFolderLatin1Decoded = source.IsDirectoryLatin1Decoded,
    };

    private static BrowsePhase ClassifyFailure(Exception exception)
    {
        string name = exception.GetType().Name;
        if (name.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return BrowsePhase.TimedOut;
        }

        if (name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("direct", StringComparison.OrdinalIgnoreCase))
        {
            return BrowsePhase.DirectConnectionFailed;
        }

        if (name.Contains("Parse", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("parse", StringComparison.OrdinalIgnoreCase))
        {
            return BrowsePhase.ParseFailed;
        }

        return BrowsePhase.Failed;
    }

    private static string FindHiddenShareRoot(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        string first = paths[0].Split('\\')[0];
        return first.StartsWith("@@", StringComparison.Ordinal) && paths.All(path =>
            string.Equals(path.Split('\\')[0], first, StringComparison.OrdinalIgnoreCase))
            ? first
            : string.Empty;
    }

    private static string NormalizePath(string? path) =>
        string.Join("\\", (path ?? string.Empty)
            .Replace('/', '\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ParentPath(string path) => FeatureValueFormatter.ParentPath(path);

    private static string CombineRemotePath(string folder, string filename) =>
        folder.Length == 0 ? filename : $"{folder}\\{filename}";

    private static bool IsSameOrDescendant(string candidate, string ancestor) =>
        ancestor.Length == 0 ||
        string.Equals(candidate, ancestor, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(ancestor + "\\", StringComparison.OrdinalIgnoreCase);

    private static int PathDepth(string path)
    {
        string normalized = NormalizePath(path);
        return normalized.Length == 0 ? 0 : normalized.Count(character => character == '\\') + 1;
    }

    private static string StableId(string kind, params string[] values)
    {
        string source = string.Join("\u001f", values.Prepend(kind));
        byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source));
        return $"{kind}-{Convert.ToHexString(digest.AsSpan(0, 10)).ToLowerInvariant()}";
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + Math.Max(0, right);

    private sealed record BrowseFileEntry(SoulseekFile File, bool IsLocked);

    private sealed record DirectoryEntry(
        string Path,
        string ParentPath,
        string DisplayName,
        IReadOnlyList<BrowseFileEntry> Files,
        bool IsLocked,
        bool IsSynthetic);

    private sealed record DirectoryIndex(
        IReadOnlyDictionary<string, DirectoryEntry> Directories,
        string HiddenShareRoot,
        int FileCount);

    private sealed record BrowseDownloadCandidate(string RemotePath, SoulseekFile File);
}
