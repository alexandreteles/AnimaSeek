using Common;
using Common.Share;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Soulseek;
using System.Net;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Owns the atomically replaceable Documents share catalog and the Soulseek resolvers backed by that catalog.
/// </summary>
internal sealed class IosShareIndexService : IDisposable
{
    private static readonly EnumerationOptions ShareEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    private readonly IosFileSystemService fileSystem;
    private readonly IUserListService userListService;
    private readonly INetworkStatus networkStatus;
    private readonly ILoggerBackend logger;
    private readonly Action checkpointTransfers;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Lock queuedRefreshSync = new();
    private SharedFileCatalog currentCatalog = SharedFileCatalog.Empty;
    private ISoulseekClient? client;
    private bool queuedRefreshRequested;
    private bool queuedRefreshRunning;
    private bool disposed;

    /// <summary>Creates a sandbox share index and its protocol resolver surface.</summary>
    /// <param name="fileSystem">The service exposing the Documents share root.</param>
    /// <param name="userListService">The canonical ignore-list service used to reject blocked peers.</param>
    /// <param name="networkStatus">The route status used to enforce metered and VPN sharing restrictions.</param>
    /// <param name="logger">The logger used for indexing, resolver, and upload failures.</param>
    /// <param name="checkpointTransfers">Persists upload transfer state after lifecycle changes.</param>
    public IosShareIndexService(
        IosFileSystemService fileSystem,
        IUserListService userListService,
        INetworkStatus networkStatus,
        ILoggerBackend logger,
        Action? checkpointTransfers = null)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.userListService = userListService ?? throw new ArgumentNullException(nameof(userListService));
        this.networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.checkpointTransfers = checkpointTransfers ?? (() => { });
        fileSystem.FileFinalized += OnFileFinalized;
    }

    /// <summary>Gets the immutable catalog currently served to peers.</summary>
    public SharedFileCatalog Catalog => Volatile.Read(ref currentCatalog);

    /// <summary>
    /// Attaches the one client whose login and upload lifecycle this index serves.
    /// </summary>
    /// <param name="value">The process Soulseek client.</param>
    /// <exception cref="InvalidOperationException">A different client was already attached.</exception>
    public void AttachClient(ISoulseekClient value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ObjectDisposedException.ThrowIf(disposed, this);
        ISoulseekClient? existing = Interlocked.CompareExchange(ref client, value, null);
        if (existing is not null && !ReferenceEquals(existing, value))
        {
            throw new InvalidOperationException("The share index is already attached to another Soulseek client.");
        }

        if (existing is null)
        {
            value.LoggedIn += OnClientLoggedIn;
            value.ExcludedSearchPhrasesReceived += OnExcludedSearchPhrasesReceived;
        }
    }

    /// <summary>Scans Documents and atomically publishes a detached catalog before updating server counts.</summary>
    /// <param name="cancellationToken">Cancels enumeration between files.</param>
    /// <returns>The newly published catalog.</returns>
    public async Task<SharedFileCatalog> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SharedFileCatalog refreshed = await BuildCatalogAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref currentCatalog, refreshed);
            await PublishSharedCountsAsync(cancellationToken).ConfigureAwait(false);
            return refreshed;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>
    /// Coalesces filesystem and foreground refresh requests into the fewest complete scans while guaranteeing that a
    /// request arriving during a scan causes one follow-up scan.
    /// </summary>
    public void RequestRefresh()
    {
        lock (queuedRefreshSync)
        {
            if (disposed)
            {
                return;
            }

            queuedRefreshRequested = true;
            if (queuedRefreshRunning)
            {
                return;
            }

            queuedRefreshRunning = true;
        }

        _ = RunQueuedRefreshesAsync();
    }

    /// <summary>Builds a detached catalog without changing the catalog currently served to peers.</summary>
    /// <param name="cancellationToken">Cancels enumeration between files.</param>
    /// <returns>A complete immutable snapshot.</returns>
    public Task<SharedFileCatalog> BuildCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var entries = new Dictionary<string, SharedFileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in System.IO.Directory.EnumerateFiles(
                fileSystem.DocumentsPath,
                "*",
                ShareEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateRegularSharedFile(path, out string safePath) || IsApplicationArtifact(safePath))
                {
                    continue;
                }

                SharedFileCatalogEntry entry = Describe(safePath);
                if (!entries.TryAdd(entry.RemotePath, entry))
                {
                    logger.Firebase($"Skipping case-insensitive duplicate share path '{entry.RemotePath}'.");
                }
            }

            return new SharedFileCatalog(entries.Values);
        }, cancellationToken);

    /// <summary>Builds a search response for an incoming peer query.</summary>
    internal Task<SearchResponse> ResolveSearchAsync(string username, int token, SearchQuery query)
    {
        if (!PreferencesState.SharingOn ||
            string.IsNullOrWhiteSpace(PreferencesState.Username) ||
            userListService.IsUserInIgnoreList(username))
        {
            return Task.FromResult<SearchResponse>(null!);
        }

        IReadOnlyList<Soulseek.File> files = Catalog.Search(
            query,
            Math.Max(1, PreferencesState.NumberSearchResults));
        if (files.Count == 0)
        {
            return Task.FromResult<SearchResponse>(null!);
        }

        int uploadSpeed = PreferencesState.UploadSpeed > 0
            ? PreferencesState.UploadSpeed
            : 256 * 1024;
        return Task.FromResult(new SearchResponse(
            PreferencesState.Username,
            token,
            hasFreeUploadSlot: true,
            uploadSpeed,
            queueLength: 0,
            files));
    }

    /// <summary>Builds a browse response for an incoming peer request.</summary>
    internal Task<BrowseResponse> ResolveBrowseAsync(string username, IPEndPoint endpoint) =>
        Task.FromResult(PreferencesState.SharingOn && !userListService.IsUserInIgnoreList(username)
            ? Catalog.CreateBrowseResponse()
            : SharedFileCatalog.Empty.CreateBrowseResponse());

    /// <summary>Resolves an exact advertised directory for an incoming peer request.</summary>
    internal Task<IEnumerable<Soulseek.Directory>> ResolveDirectoryAsync(
        string username,
        IPEndPoint endpoint,
        int token,
        string remoteDirectory) =>
        Task.FromResult(
            PreferencesState.SharingOn &&
            !userListService.IsUserInIgnoreList(username) &&
            Catalog.TryGetDirectory(remoteDirectory, out Soulseek.Directory? directory)
                ? Enumerable.Repeat(directory, 1)
                : Enumerable.Empty<Soulseek.Directory>());

    /// <summary>
    /// Validates an incoming queue request against the immutable catalog and begins an upload tracked by the portable manager.
    /// </summary>
    internal Task EnqueueUploadAsync(string username, IPEndPoint endpoint, string remotePath)
    {
        if (!PreferencesState.SharingOn ||
            userListService.IsUserInIgnoreList(username) ||
            !SharingNetworkPolicy.PermitsUpload(
                networkStatus,
                PreferencesState.AllowUploadsOnMetered,
                PreferencesState.RequireVpnForSharing))
        {
            throw new DownloadEnqueueException("Sharing is unavailable on the current network.");
        }

        SharedFileCatalog catalog = Catalog;
        if (!catalog.TryGetEntry(remotePath, out SharedFileCatalogEntry? entry) ||
            !TryValidateRegularSharedFile(entry.LocalPath, out string safePath))
        {
            throw new DownloadEnqueueException("The requested file is not shared.");
        }

        var currentInfo = new FileInfo(safePath);
        if (!currentInfo.Exists || currentInfo.Length != entry.Size)
        {
            _ = RefreshAfterCatalogMismatchAsync();
            throw new DownloadEnqueueException("The shared file changed; retry after the share index refreshes.");
        }

        ISoulseekClient activeClient = Volatile.Read(ref client) ??
            throw new DownloadEnqueueException("The sharing session is not ready.");
        TransferItemManager manager = TransferItems.TransferItemManagerUploads ??
            throw new DownloadEnqueueException("The upload manager is not ready.");
        var cancellationSource = new CancellationTokenSource();
        var candidate = new TransferItem
        {
            Username = username,
            FullFilename = entry.RemotePath,
            Filename = Path.GetFileName(entry.RemotePath.Replace('\\', Path.DirectorySeparatorChar)),
            FolderName = global::Common.Helpers.GetFolderNameFromFile(entry.RemotePath),
            Size = entry.Size,
            QueueLength = 0,
            State = TransferStates.Requested,
            isUpload = true,
            CancellationTokenSource = cancellationSource,
            InProcessing = true,
        };
        TransferItem item = manager.AddIfNotExistAndReturnTransfer(candidate, out bool exists);
        if (exists && IsActive(item.State))
        {
            cancellationSource.Dispose();
            throw new DownloadEnqueueException("The requested file is already queued for this user.");
        }

        if (exists)
        {
            item.ClearStateForRetry();
            item.Size = entry.Size;
            item.State = TransferStates.Requested;
            item.InProcessing = true;
            item.isUpload = true;
        }

        TransferState.SetupCancellationToken(item, cancellationSource, out CancellationTokenSource? previousSource);
        if (previousSource is not null && !ReferenceEquals(previousSource, cancellationSource))
        {
            previousSource.Cancel();
            previousSource.Dispose();
        }

        TransferItemManager.MarkTransfersDirty();
        checkpointTransfers();
        _ = RunUploadAsync(activeClient, item, safePath, cancellationSource);
        return Task.CompletedTask;
    }

    /// <summary>Publishes either the current catalog counts or zero when sharing is disabled.</summary>
    internal async Task PublishSharedCountsAsync(CancellationToken cancellationToken = default)
    {
        ISoulseekClient? activeClient = Volatile.Read(ref client);
        if (activeClient is null ||
            !activeClient.State.HasFlag(SoulseekClientStates.Connected) ||
            !activeClient.State.HasFlag(SoulseekClientStates.LoggedIn))
        {
            return;
        }

        SharedFileCatalog catalog = Catalog;
        int directories = PreferencesState.SharingOn ? catalog.DirectoryCount : 0;
        int files = PreferencesState.SharingOn ? catalog.FileCount : 0;
        await activeClient.SetSharedCountsAsync(directories, files, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (queuedRefreshSync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queuedRefreshRequested = false;
        }

        fileSystem.FileFinalized -= OnFileFinalized;
        ISoulseekClient? activeClient = Interlocked.Exchange(ref client, null);
        if (activeClient is not null)
        {
            activeClient.LoggedIn -= OnClientLoggedIn;
            activeClient.ExcludedSearchPhrasesReceived -= OnExcludedSearchPhrasesReceived;
        }

        refreshGate.Dispose();
    }

    private void OnFileFinalized(object? sender, EventArgs args) => RequestRefresh();

    private async Task RunQueuedRefreshesAsync()
    {
        while (true)
        {
            lock (queuedRefreshSync)
            {
                if (disposed || !queuedRefreshRequested)
                {
                    queuedRefreshRunning = false;
                    return;
                }

                queuedRefreshRequested = false;
            }

            try
            {
                await RefreshAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (disposed)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.FirebaseError("Queued Documents share-index refresh failed.", exception);
            }
        }
    }

    private SharedFileCatalogEntry Describe(string path)
    {
        var info = new FileInfo(path);
        int? bitrate = null;
        int? duration = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3":
                    MicroTagReader.Instance.GetMp3Metadata(stream, -1, info.Length, out int mp3Bitrate);
                    // MicroTagReader reports bits/second; the Soulseek attribute is conventionally kilobits/second.
                    bitrate = mp3Bitrate > 0 ? mp3Bitrate / 1000 : null;
                    break;
                case ".aiff":
                case ".aif":
                    MicroTagReader.Instance.GetAiffMetadata(stream, out _, out _, out int aiffDuration);
                    duration = aiffDuration >= 0 ? aiffDuration : null;
                    break;
                case ".ape":
                    MicroTagReader.Instance.GetApeMetadata(stream, out _, out _, out int apeDuration);
                    duration = apeDuration >= 0 ? apeDuration : null;
                    break;
                case ".flac":
                    MicroTagReader.Instance.GetFlacMetadata(stream, out _, out _);
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.Debug($"Metadata read failed for {path}: {exception.Message}");
        }

        string relativePath = Path.GetRelativePath(fileSystem.DocumentsPath, path)
            .Replace(Path.DirectorySeparatorChar, '\\');
        return new SharedFileCatalogEntry(
            $"Documents\\{relativePath}",
            path,
            info.Length,
            bitrate,
            duration);
    }

    private async Task RunUploadAsync(
        ISoulseekClient activeClient,
        TransferItem item,
        string safePath,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await activeClient.UploadAsync(
                item.Username,
                item.FullFilename,
                item.Size,
                offset => Task.FromResult<Stream>(OpenUploadStream(safePath, item.Size, offset)),
                options: new TransferOptions(governor: SpeedLimitHelper.OurUploadGovernor),
                cancellationToken: cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            SetTerminalFallback(item, TransferStates.Completed | TransferStates.Cancelled, failed: false);
        }
        catch (Exception exception)
        {
            logger.FirebaseError($"Upload of '{item.FullFilename}' to '{item.Username}' failed.", exception);
            SetTerminalFallback(item, TransferStates.Completed | TransferStates.Errored, failed: true);
        }
        finally
        {
            item.InProcessing = false;
            TransferItemManager.MarkTransfersDirty();
            checkpointTransfers();
        }
    }

    private Stream OpenUploadStream(string safePath, long indexedSize, long offset)
    {
        if (!TryValidateRegularSharedFile(safePath, out string validatedPath))
        {
            throw new UnauthorizedAccessException("The indexed shared file no longer resolves inside Documents.");
        }

        var stream = new FileStream(
            validatedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != indexedSize || offset < 0 || offset > stream.Length)
        {
            stream.Dispose();
            throw new IOException("The shared file size or requested resume offset is no longer valid.");
        }

        stream.Position = offset;
        return stream;
    }

    private bool TryValidateRegularSharedFile(string candidate, out string safePath)
    {
        safePath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string root = Path.GetFullPath(fileSystem.DocumentsPath).TrimEnd(Path.DirectorySeparatorChar);
        string canonical = Path.GetFullPath(candidate);
        string prefix = root + Path.DirectorySeparatorChar;
        if (!canonical.StartsWith(prefix, StringComparison.Ordinal) || !System.IO.File.Exists(canonical))
        {
            return false;
        }

        FileSystemInfo? current = new FileInfo(canonical);
        while (current is not null && !string.Equals(current.FullName, root, StringComparison.Ordinal))
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null,
            };
        }

        if (current is null)
        {
            return false;
        }

        safePath = canonical;
        return true;
    }

    private bool IsApplicationArtifact(string path) =>
        Path.GetFileName(path).StartsWith("AnimaSeek.log", StringComparison.Ordinal) ||
        Path.GetRelativePath(fileSystem.DocumentsPath, path)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => segment.StartsWith(".", StringComparison.Ordinal));

    private static bool IsActive(TransferStates state) =>
        !state.HasFlag(TransferStates.Completed) &&
        (state & (TransferStates.Requested | TransferStates.Queued | TransferStates.Initializing | TransferStates.InProgress)) != 0;

    private static void SetTerminalFallback(TransferItem item, TransferStates state, bool failed)
    {
        if (!item.State.HasFlag(TransferStates.Completed))
        {
            item.State = state;
            item.Failed = failed;
        }
    }

    private async Task RefreshAfterCatalogMismatchAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Share index refresh after a changed file failed.", exception);
        }
    }

    private void OnClientLoggedIn(object? sender, EventArgs args) => _ = PublishCountsAfterLoginAsync();

    private static void OnExcludedSearchPhrasesReceived(
        object? sender,
        IReadOnlyCollection<string> excludedPhrases) =>
        SearchUtil.ExcludedSearchPhrases = excludedPhrases.ToArray();

    private async Task PublishCountsAfterLoginAsync()
    {
        try
        {
            await PublishSharedCountsAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Publishing shared counts after login failed.", exception);
        }
    }
}
