using AnimaSeek.iOS.UI.Presentation;
using AnimaSeek.iOS.Services;
using Common;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Soulseek;
using SoulseekFile = Soulseek.File;
using System.Security.Cryptography;
using System.Text;

namespace AnimaSeek.iOS.UI.Presentation.Search;

/// <summary>Identifies the mutually exclusive lifecycle states of one interactive search.</summary>
internal enum SearchPhase
{
    Idle,
    Loading,
    Content,
    Empty,
    FilteredEmpty,
    Offline,
    Canceled,
    TimedOut,
    Failed,
}

/// <summary>Identifies the peer population searched by a query.</summary>
internal enum SearchAudience
{
    Network,
    User,
    Room,
    UserList,
    Wishlist,
}

/// <summary>Identifies the supported result ordering choices.</summary>
internal enum SearchOrdering
{
    Availability,
    Speed,
    Folder,
    Bitrate,
}

/// <summary>Identifies the file-format filter applied without repeating the network request.</summary>
internal enum SearchFormat
{
    Any,
    Lossless,
    Lossy,
}

/// <summary>Identifies the broad file category filter applied without repeating the network request.</summary>
internal enum SearchCategory
{
    Any,
    Audio,
    Video,
    Image,
    Document,
    Archive,
    Other,
}

/// <summary>Defines one validated search target selected by the person.</summary>
/// <param name="Audience">The target population.</param>
/// <param name="Subject">The required username or room name for a targeted search.</param>
internal sealed record SearchTarget(SearchAudience Audience, string? Subject = null)
{
    /// <summary>Creates a network-wide target.</summary>
    public static SearchTarget Network { get; } = new(SearchAudience.Network);
}

/// <summary>Defines local filters that can be changed without discarding received results.</summary>
/// <param name="Text">Text terms that must each occur in the result path, peer, or extension.</param>
/// <param name="ExcludedText">Text terms that must not occur in the result path, peer, or extension.</param>
/// <param name="OnlyFreeSlots">Whether peers without an immediately free upload slot are hidden.</param>
/// <param name="HideLocked">Whether locked files are hidden.</param>
/// <param name="Format">The requested file family.</param>
/// <param name="Category">The requested broad file category.</param>
/// <param name="MinimumBitrate">The minimum advertised bitrate, or zero for no minimum.</param>
internal sealed record SearchFilters(
    string Text,
    string ExcludedText,
    bool OnlyFreeSlots,
    bool HideLocked,
    SearchFormat Format,
    SearchCategory Category,
    int MinimumBitrate)
{
    /// <summary>Gets a filter that includes every result.</summary>
    public static SearchFilters None { get; } = new(
        string.Empty,
        string.Empty,
        false,
        false,
        SearchFormat.Any,
        SearchCategory.Any,
        0);

    /// <summary>Gets the number of active, independently removable filter dimensions.</summary>
    public int ActiveCount =>
        (string.IsNullOrWhiteSpace(Text) ? 0 : 1) +
        (string.IsNullOrWhiteSpace(ExcludedText) ? 0 : 1) +
        (OnlyFreeSlots ? 1 : 0) +
        (HideLocked ? 1 : 0) +
        (Format == SearchFormat.Any ? 0 : 1) +
        (Category == SearchCategory.Any ? 0 : 1) +
        (MinimumBitrate <= 0 ? 0 : 1);
}

/// <summary>Describes either one peer folder or one file in the high-volume native result list.</summary>
/// <param name="Id">The stable identifier used by diffable snapshots.</param>
/// <param name="ParentId">The owning folder identifier for file rows.</param>
/// <param name="IsFolder">Whether this row expands into file rows.</param>
/// <param name="Title">The filename or folder display name.</param>
/// <param name="Subtitle">The concise availability and transfer metadata.</param>
/// <param name="Username">The peer that supplied the result.</param>
/// <param name="RemotePath">The exact remote folder or file path.</param>
/// <param name="FileCount">The number of visible files represented by a folder row.</param>
/// <param name="Size">The file size, or aggregate folder size.</param>
/// <param name="IsLocked">Whether the peer marked the item as locked.</param>
/// <param name="HasFreeSlot">Whether the peer advertised a free upload slot.</param>
/// <param name="IsExpanded">Whether a folder's file rows are currently visible.</param>
internal sealed record SearchResultRow(
    string Id,
    string? ParentId,
    bool IsFolder,
    string Title,
    string Subtitle,
    string Username,
    string RemotePath,
    int FileCount,
    long Size,
    bool IsLocked,
    bool HasFreeSlot,
    bool IsExpanded);

/// <summary>Represents the complete immutable Search screen snapshot.</summary>
/// <param name="Phase">The current request lifecycle.</param>
/// <param name="Query">The preserved network query.</param>
/// <param name="Target">The selected target.</param>
/// <param name="Rows">Visible folder and expanded-file rows.</param>
/// <param name="VisibleFileCount">The number of files matching local filters.</param>
/// <param name="TotalFileCount">The number of files received before local filters.</param>
/// <param name="ResponseCount">The number of peer responses received.</param>
/// <param name="Filters">The active local filters.</param>
/// <param name="Ordering">The selected ordering.</param>
/// <param name="History">Most-recent-first search history.</param>
/// <param name="Message">An actionable error or status detail, when needed.</param>
internal sealed record SearchScreenState(
    SearchPhase Phase,
    string Query,
    SearchTarget Target,
    IReadOnlyList<SearchResultRow> Rows,
    int VisibleFileCount,
    int TotalFileCount,
    int ResponseCount,
    SearchFilters Filters,
    SearchOrdering Ordering,
    IReadOnlyList<string> History,
    string? Message)
{
    /// <summary>Creates the first-use Search snapshot.</summary>
    /// <param name="history">Restored search history.</param>
    public static SearchScreenState Initial(IReadOnlyList<string> history) => new(
        SearchPhase.Idle,
        string.Empty,
        SearchTarget.Network,
        [],
        0,
        0,
        0,
        SearchFilters.None,
        SearchOrdering.Availability,
        history,
        null);
}

/// <summary>
/// Owns cancellation, history, filtering, stable expansion identity, and download commands for Search UIKit screens.
/// </summary>
internal sealed class SearchPresentationStore : FeaturePresentationStore, IDisposable
{
    private const string HistoryKey = "AnimaSeek.UI.Search.History.v1";
    private const string ExpandAllSentinel = "__expand_all__";
    private const int MaximumHistoryCount = 20;
    private static readonly TimeSpan ProgressivePublicationInterval = TimeSpan.FromMilliseconds(140);
    private readonly AppSession session;
    private readonly IKeyValueStore keyValueStore;
    private readonly IosWishlistService wishlistService;
    private readonly IosUserListService? userListService;
    private readonly Lock stateSync = new();
    private readonly Dictionary<string, SearchResultBacking> backingByRowId = new(StringComparer.Ordinal);
    private readonly HashSet<string> expandedFolderIds = new(StringComparer.Ordinal);
    private CancellationTokenSource? activeSearch;
    private IReadOnlyCollection<SearchResponse> responses = [];
    private readonly List<SearchResponse> progressiveResponses = [];
    private bool progressivePublicationPending;
    private long terminalGeneration = -1;
    private long generation;
    private bool disposed;

    /// <summary>Creates a Search presentation store backed only by the application session and preference abstraction.</summary>
    /// <param name="session">The UI-safe application session façade.</param>
    /// <param name="keyValueStore">The store used for bounded search history.</param>
    public SearchPresentationStore(
        AppSession session,
        IKeyValueStore keyValueStore,
        IosWishlistService wishlistService,
        IosUserListService? userListService = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.wishlistService = wishlistService ?? throw new ArgumentNullException(nameof(wishlistService));
        this.userListService = userListService;
        State = SearchScreenState.Initial(RestoreHistory()) with
        {
            Filters = DefaultFilters(),
            Ordering = DefaultOrdering(),
        };
        ResetExpansionPreference();
        session.StateChanged += OnSessionStateChanged;
        wishlistService.Changed += OnWishlistChanged;
    }

    /// <summary>Gets the latest immutable Search screen snapshot.</summary>
    public SearchScreenState State { get; private set; }

    /// <summary>Raised on the UI context after a coherent snapshot replaces <see cref="State"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets immutable editable wishlist entries and their durable result snapshots.</summary>
    public IReadOnlyList<WishlistEntrySnapshot> Wishlists => wishlistService.GetSnapshot();

    /// <summary>Gets the active server-provided wishlist cadence, or null while unavailable.</summary>
    public TimeSpan? WishlistInterval => wishlistService.ServerInterval;

    /// <summary>Creates a scheduled wishlist query.</summary>
    public Task<int> CreateWishlistAsync(string query, CancellationToken cancellationToken = default) =>
        wishlistService.CreateAsync(query, cancellationToken);

    /// <summary>Renames a scheduled wishlist and clears results belonging to its prior query.</summary>
    public Task RenameWishlistAsync(int identifier, string query, CancellationToken cancellationToken = default) =>
        wishlistService.RenameAsync(identifier, query, cancellationToken);

    /// <summary>Deletes one scheduled wishlist and its durable results.</summary>
    public Task DeleteWishlistAsync(int identifier, CancellationToken cancellationToken = default) =>
        wishlistService.DeleteAsync(identifier, cancellationToken);

    /// <summary>Loads durable exact results for a wishlist without issuing a new network request.</summary>
    /// <param name="identifier">The canonical negative wishlist identifier.</param>
    /// <param name="cancellationToken">Cancels background projection or the seen-state mutation.</param>
    public async Task OpenWishlistAsync(int identifier, CancellationToken cancellationToken = default)
    {
        WishlistEntrySnapshot entry = Wishlists.FirstOrDefault(candidate => candidate.Id == identifier) ??
            throw new InvalidOperationException(StringResources.Get("IosUiWishlistMissing"));
        SearchFilters filters;
        SearchOrdering ordering;
        HashSet<string> expanded;
        long requestedGeneration;
        lock (stateSync)
        {
            requestedGeneration = ++generation;
            activeSearch?.Cancel();
            ResetExpansionPreference();
            filters = State.Filters;
            ordering = State.Ordering;
            expanded = [.. expandedFolderIds];
        }

        SearchProjection projection = await Task.Run(
            () => Project(entry.Responses, filters, ordering, expanded),
            cancellationToken);
        lock (stateSync)
        {
            if (requestedGeneration != generation)
            {
                return;
            }

            responses = entry.Responses;
            State = State with
            {
                Query = entry.Query,
                Target = new SearchTarget(SearchAudience.Wishlist),
            };
            ApplyProjection(projection, entry.Responses.Count == 0 ? SearchPhase.Empty : null);
        }

        Publish(Changed);
        await wishlistService.MarkSeenAsync(identifier, cancellationToken);
    }

    /// <summary>Starts a generation-safe search and preserves any prior useful result list while the request begins.</summary>
    /// <param name="query">The non-empty Soulseek query.</param>
    /// <param name="target">The validated target population.</param>
    /// <param name="cancellationToken">Cancels the request in addition to a later superseding search.</param>
    public async Task SearchAsync(
        string query,
        SearchTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            throw new ArgumentException(StringResources.Get("no_search_text"), nameof(query));
        }

        target ??= SearchTarget.Network;
        ValidateTarget(target);
        CancellationTokenSource searchCancellation;
        long requestedGeneration;
        lock (stateSync)
        {
            activeSearch?.Cancel();
            activeSearch?.Dispose();
            activeSearch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            searchCancellation = activeSearch;
            requestedGeneration = ++generation;
            terminalGeneration = -1;
            progressiveResponses.Clear();
            progressivePublicationPending = false;
            ResetExpansionPreference();
            State = State with
            {
                Phase = session.IsConnected ? SearchPhase.Loading : SearchPhase.Offline,
                Query = query,
                Target = target,
                Message = session.IsConnected ? null : StringResources.Get("must_be_logged_to_search"),
            };
        }

        Publish(Changed);
        if (!session.IsConnected)
        {
            lock (stateSync)
            {
                if (ReferenceEquals(activeSearch, searchCancellation))
                {
                    activeSearch = null;
                }
            }

            searchCancellation.Dispose();
            return;
        }

        AddHistory(query);
        try
        {
            int responseLimit = Math.Max(1, PreferencesState.NumberSearchResults);
            var options = new SearchOptions(
                responseLimit: responseLimit,
                maximumPeerQueueLength: int.MaxValue,
                filterResponses: true);
            IReadOnlyCollection<SearchResponse> completed = await session.SearchAsync(
                query,
                MapScope(target),
                options,
                response => AcceptProgressiveResponse(requestedGeneration, response, searchCancellation.Token),
                searchCancellation.Token);
            SearchFilters filters;
            SearchOrdering ordering;
            HashSet<string> expanded;
            lock (stateSync)
            {
                if (requestedGeneration != generation || searchCancellation.IsCancellationRequested)
                {
                    return;
                }

                filters = State.Filters;
                ordering = State.Ordering;
                expanded = [.. expandedFolderIds];
            }

            SearchProjection projection = await Task.Run(
                () => Project(completed, filters, ordering, expanded),
                searchCancellation.Token);
            lock (stateSync)
            {
                if (requestedGeneration != generation || searchCancellation.IsCancellationRequested)
                {
                    return;
                }

                responses = completed;
                terminalGeneration = requestedGeneration;
                ApplyProjection(projection, completed.Count == 0 ? SearchPhase.Empty : null);
                State = State with
                {
                    History = RestoreHistory(),
                    Message = completed.Count >= responseLimit
                        ? StringResources.Format("IosUiSearchResponseLimitReached", responseLimit)
                        : null,
                };
            }

            Publish(Changed);
        }
        catch (OperationCanceledException) when (searchCancellation.IsCancellationRequested)
        {
            lock (stateSync)
            {
                if (requestedGeneration != generation)
                {
                    return;
                }

                State = State with
                {
                    Phase = State.Rows.Count == 0 ? SearchPhase.Canceled : SearchPhase.Content,
                    Message = StringResources.Get("Cancelled"),
                };
            }

            Publish(Changed);
        }
        catch (TimeoutException)
        {
            PublishFailure(
                requestedGeneration,
                SearchPhase.TimedOut,
                StringResources.Get("IosUiSearchTimedOutDetail"));
        }
        catch (Exception)
        {
            PublishFailure(
                requestedGeneration,
                session.IsConnected ? SearchPhase.Failed : SearchPhase.Offline,
                session.IsConnected
                    ? StringResources.Get("IosUiSearchFailedDetail")
                    : StringResources.Get("must_be_logged_to_search"));
        }
        finally
        {
            lock (stateSync)
            {
                if (ReferenceEquals(activeSearch, searchCancellation))
                {
                    activeSearch = null;
                }
            }

            searchCancellation.Dispose();
        }
    }

    /// <summary>Cancels the active query without clearing useful results or the preserved query.</summary>
    public void CancelSearch()
    {
        lock (stateSync)
        {
            activeSearch?.Cancel();
        }
    }

    /// <summary>Reprojects received data through new local filters without repeating the network request.</summary>
    /// <param name="filters">The complete new filter value.</param>
    public async Task SetFiltersAsync(SearchFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ObjectDisposedException.ThrowIf(disposed, this);
        filters = filters with
        {
            Text = NormalizeTextExpression(filters.Text),
            ExcludedText = NormalizeTextExpression(filters.ExcludedText),
        };
        IReadOnlyCollection<SearchResponse> snapshot;
        SearchOrdering ordering;
        HashSet<string> expanded;
        string? retainedMessage;
        long requestedGeneration;
        lock (stateSync)
        {
            State = State with { Filters = filters };
            snapshot = responses;
            ordering = State.Ordering;
            expanded = [.. expandedFolderIds];
            retainedMessage = State.Message;
            requestedGeneration = generation;
        }

        PersistFilters(filters);

        SearchProjection projection = await Task.Run(
            () => Project(snapshot, filters, ordering, expanded));
        lock (stateSync)
        {
            if (requestedGeneration != generation)
            {
                return;
            }

            ApplyProjection(projection);
            State = State with { Message = retainedMessage };
        }

        Publish(Changed);
    }

    /// <summary>Changes result ordering while retaining query, filters, expansion, and selection identity.</summary>
    /// <param name="ordering">The new ordering.</param>
    public async Task SetOrderingAsync(SearchOrdering ordering)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IReadOnlyCollection<SearchResponse> snapshot;
        SearchFilters filters;
        HashSet<string> expanded;
        string? retainedMessage;
        long requestedGeneration;
        lock (stateSync)
        {
            State = State with { Ordering = ordering };
            snapshot = responses;
            filters = State.Filters;
            expanded = [.. expandedFolderIds];
            retainedMessage = State.Message;
            requestedGeneration = generation;
        }

        PersistOrdering(ordering);

        SearchProjection projection = await Task.Run(
            () => Project(snapshot, filters, ordering, expanded));
        lock (stateSync)
        {
            if (requestedGeneration != generation)
            {
                return;
            }

            ApplyProjection(projection);
            State = State with { Message = retainedMessage };
        }

        Publish(Changed);
    }

    /// <summary>Toggles one folder's expansion while retaining stable file identifiers.</summary>
    /// <param name="folderId">The stable folder row identifier.</param>
    public void ToggleFolder(string folderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);
        lock (stateSync)
        {
            string? retainedMessage = State.Message;
            bool wasExpandAll = expandedFolderIds.Remove(ExpandAllSentinel);
            if (wasExpandAll)
            {
                expandedFolderIds.UnionWith(State.Rows
                    .Where(row => row.IsFolder && row.Id != folderId)
                    .Select(row => row.Id));
            }
            else if (!expandedFolderIds.Remove(folderId))
            {
                expandedFolderIds.Add(folderId);
            }

            ApplyProjection(Project(responses, State.Filters, State.Ordering, expandedFolderIds));
            State = State with { Message = retainedMessage };
        }

        Publish(Changed);
    }

    /// <summary>Returns a detached immutable row for action-sheet presentation.</summary>
    /// <param name="rowId">The stable diffable identifier.</param>
    /// <returns>The current row, or <see langword="null"/> when it no longer matches the active result generation.</returns>
    public SearchResultRow? GetRow(string rowId) => State.Rows.FirstOrDefault(row => row.Id == rowId);

    /// <summary>Returns detached file rows represented by one current folder, independent of expansion state.</summary>
    /// <param name="folderId">The stable identifier of a folder row in the current generation.</param>
    /// <returns>Visible, locally filtered file summaries ordered by filename.</returns>
    public IReadOnlyList<SearchResultRow> GetFolderFiles(
        string folderId,
        bool applyFilters = true,
        bool includeSubfolders = false)
    {
        SearchResultRow? folder = State.Rows.FirstOrDefault(row => row.Id == folderId && row.IsFolder);
        if (folder is null)
        {
            return [];
        }

        lock (stateSync)
        {
            return responses
                .Where(response => string.Equals(response.Username, folder.Username, StringComparison.Ordinal))
                .SelectMany(response => response.Files.Select(file => new SearchFileCandidate(response, file, false))
                    .Concat(response.LockedFiles.Select(file => new SearchFileCandidate(response, file, true))))
                .Where(candidate =>
                    FolderMatches(
                        folder.RemotePath,
                        FeatureValueFormatter.ParentPath(candidate.File.Filename),
                        includeSubfolders) &&
                    candidate.IsLocked == folder.IsLocked &&
                    (!applyFilters || Matches(candidate, State.Filters)))
                .DistinctBy(candidate => candidate.File.Filename, StringComparer.Ordinal)
                .OrderBy(candidate => candidate.File.Filename, StringComparer.CurrentCultureIgnoreCase)
                .Select(candidate => new SearchResultRow(
                    StableId("file", candidate.Response.Username, candidate.File.Filename, candidate.IsLocked.ToString()),
                    folder.Id,
                    false,
                    FeatureValueFormatter.FileName(candidate.File.Filename),
                    StringResources.Format(
                        "IosUiSearchFileMetadata",
                        FeatureValueFormatter.Bytes(candidate.File.Size),
                        candidate.File.BitRate is { } bitrate
                            ? StringResources.Format("IosUiBitrate", bitrate)
                            : StringResources.Get("IosUiBitrateUnknown")),
                    candidate.Response.Username,
                    candidate.File.Filename,
                    1,
                    candidate.File.Size,
                    candidate.IsLocked,
                    candidate.Response.HasFreeUploadSlot,
                    false))
                .ToArray();
        }
    }

    /// <summary>Queues exactly the reviewed file identifiers from one current folder.</summary>
    /// <param name="folderId">The stable folder identity.</param>
    /// <param name="fileIds">The selected stable file identities returned by <see cref="GetFolderFiles"/>.</param>
    /// <param name="queuePaused">Whether accepted work should remain paused.</param>
    /// <param name="cancellationToken">Cancels acceptance before the batch enters Transfers.</param>
    /// <returns>Stable acceptances for the selected files that still exist in the active result generation.</returns>
    public async Task<IReadOnlyList<DownloadAcceptance>> QueueFolderSelectionAsync(
        string folderId,
        IReadOnlyCollection<string> fileIds,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        SearchResultRow folder = State.Rows.FirstOrDefault(row => row.Id == folderId && row.IsFolder) ??
            throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
        HashSet<string> selected = fileIds.ToHashSet(StringComparer.Ordinal);
        FullFileInfo[] files;
        lock (stateSync)
        {
            files = responses
                .Where(response => string.Equals(response.Username, folder.Username, StringComparison.Ordinal))
                .SelectMany(response => response.Files.Select(file => new SearchFileCandidate(response, file, false))
                    .Concat(response.LockedFiles.Select(file => new SearchFileCandidate(response, file, true))))
                .Where(candidate =>
                    candidate.IsLocked == folder.IsLocked &&
                    selected.Contains(StableId(
                        "file",
                        candidate.Response.Username,
                        candidate.File.Filename,
                        candidate.IsLocked.ToString())))
                .DistinctBy(candidate => candidate.File.Filename, StringComparer.Ordinal)
                .Select(candidate => new FullFileInfo
                {
                    FullFileName = candidate.File.Filename,
                    Size = candidate.File.Size,
                    Depth = 1,
                    wasFilenameLatin1Decoded = candidate.File.IsLatin1Decoded,
                    wasFolderLatin1Decoded = candidate.File.IsDirectoryLatin1Decoded,
                })
                .ToArray();
        }

        if (files.Length == 0)
        {
            throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
        }

        return await session.QueueDownloadsAsync(folder.Username, files, queuePaused, cancellationToken);
    }

    /// <summary>Tests an exact or descendant remote-folder relationship across Soulseek path separators.</summary>
    private static bool FolderMatches(string root, string candidate, bool includeSubfolders)
    {
        if (string.Equals(root, candidate, StringComparison.Ordinal))
        {
            return true;
        }

        if (!includeSubfolders || root.Length == 0 || candidate.Length <= root.Length)
        {
            return false;
        }

        return candidate.StartsWith(root, StringComparison.Ordinal) &&
            candidate[root.Length] is '\\' or '/';
    }

    /// <summary>Queues one selected file and returns as soon as its asynchronous work has been accepted locally.</summary>
    /// <param name="rowId">The stable identifier of a file row.</param>
    /// <param name="cancellationToken">Cancels startup before the session accepts the request.</param>
    public async Task<DownloadAcceptance> QueueDownloadAsync(
        string rowId,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SearchResultBacking backing;
        lock (stateSync)
        {
            if (!backingByRowId.TryGetValue(rowId, out backing!) || backing.File is null)
            {
                throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
            }
        }

        return await session.QueueDownloadAsync(
            backing.Response,
            backing.File,
            queuePaused,
            cancellationToken);
    }

    /// <summary>Queues every currently received file represented by a folder row.</summary>
    /// <param name="folderId">The stable identifier of a result folder.</param>
    /// <param name="queuePaused">Whether to retain the accepted items without starting network work.</param>
    /// <param name="cancellationToken">Cancels acceptance before the batch enters the transfer manager.</param>
    /// <returns>One stable acceptance for each distinct file in the folder.</returns>
    public async Task<IReadOnlyList<DownloadAcceptance>> QueueFolderAsync(
        string folderId,
        bool queuePaused = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SearchResultRow row = State.Rows.FirstOrDefault(candidate => candidate.Id == folderId && candidate.IsFolder) ??
            throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
        SearchFileCandidate[] candidates;
        lock (stateSync)
        {
            candidates = responses
                .Where(response => string.Equals(response.Username, row.Username, StringComparison.Ordinal))
                .SelectMany(response => response.Files.Select(file => new SearchFileCandidate(response, file, false))
                    .Concat(response.LockedFiles.Select(file => new SearchFileCandidate(response, file, true))))
                .Where(candidate =>
                    string.Equals(
                        FeatureValueFormatter.ParentPath(candidate.File.Filename),
                        row.RemotePath,
                        StringComparison.Ordinal) &&
                    candidate.IsLocked == row.IsLocked &&
                    Matches(candidate, State.Filters))
                .ToArray();
        }

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(StringResources.Get("nothing_to_download"));
        }

        FullFileInfo[] files = candidates
            .DistinctBy(candidate => candidate.File.Filename, StringComparer.Ordinal)
            .Select(candidate => new FullFileInfo
            {
                FullFileName = candidate.File.Filename,
                Size = candidate.File.Size,
                Depth = 1,
                wasFilenameLatin1Decoded = candidate.File.IsLatin1Decoded,
                wasFolderLatin1Decoded = candidate.File.IsDirectoryLatin1Decoded,
            })
            .ToArray();
        return await session.QueueDownloadsAsync(
            row.Username,
            files,
            queuePaused,
            cancellationToken);
    }

    /// <summary>Removes all saved Search history without altering the active query or results.</summary>
    public void ClearHistory()
    {
        keyValueStore.PutString(HistoryKey, null);
        keyValueStore.Flush();
        State = State with { History = [] };
        Publish(Changed);
    }

    /// <summary>Cancels owned work and detaches session observers.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.StateChanged -= OnSessionStateChanged;
        wishlistService.Changed -= OnWishlistChanged;
        activeSearch?.Cancel();
        activeSearch?.Dispose();
        activeSearch = null;
    }

    private void AcceptProgressiveResponse(
        long requestedGeneration,
        SearchResponse response,
        CancellationToken cancellationToken)
    {
        lock (stateSync)
        {
            if (disposed || requestedGeneration != generation || cancellationToken.IsCancellationRequested ||
                terminalGeneration == requestedGeneration)
            {
                return;
            }

            progressiveResponses.Add(response);
        }

        ScheduleProgressivePublication(requestedGeneration, cancellationToken);
    }

    private void ScheduleProgressivePublication(long requestedGeneration, CancellationToken cancellationToken)
    {
        bool shouldSchedule;
        lock (stateSync)
        {
            if (disposed || requestedGeneration != generation || cancellationToken.IsCancellationRequested ||
                terminalGeneration == requestedGeneration)
            {
                return;
            }

            shouldSchedule = !progressivePublicationPending;
            progressivePublicationPending = true;
        }

        if (shouldSchedule)
        {
            _ = PublishProgressiveAsync(requestedGeneration, cancellationToken);
        }
    }

    private async Task PublishProgressiveAsync(long requestedGeneration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ProgressivePublicationInterval, cancellationToken).ConfigureAwait(false);
            SearchResponse[] snapshot;
            SearchFilters filters;
            SearchOrdering ordering;
            HashSet<string> expanded;
            lock (stateSync)
            {
                if (requestedGeneration != generation || terminalGeneration == requestedGeneration)
                {
                    progressivePublicationPending = false;
                    return;
                }

                snapshot = [.. progressiveResponses];
                filters = State.Filters;
                ordering = State.Ordering;
                expanded = [.. expandedFolderIds];
            }

            SearchProjection projection = await Task.Run(
                () => Project(snapshot, filters, ordering, expanded),
                cancellationToken).ConfigureAwait(false);
            bool publishAgain;
            lock (stateSync)
            {
                if (requestedGeneration != generation || terminalGeneration == requestedGeneration)
                {
                    progressivePublicationPending = false;
                    return;
                }

                responses = snapshot;
                ApplyProjection(projection);
                progressivePublicationPending = false;
                publishAgain = progressiveResponses.Count > snapshot.Length;
            }

            Publish(Changed);
            if (publishAgain)
            {
                ScheduleProgressivePublication(requestedGeneration, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            lock (stateSync)
            {
                progressivePublicationPending = false;
            }
        }
    }

    private void OnSessionStateChanged(object? sender, EventArgs args)
    {
        lock (stateSync)
        {
            if (session.IsConnected || State.Phase == SearchPhase.Idle)
            {
                if (session.IsConnected && State.Phase == SearchPhase.Offline && State.Rows.Count > 0)
                {
                    State = State with { Phase = SearchPhase.Content, Message = null };
                }
            }
            else
            {
                State = State with
                {
                    Phase = SearchPhase.Offline,
                    Message = StringResources.Get("must_be_logged_to_search"),
                };
            }
        }

        Publish(Changed);
    }

    private void OnWishlistChanged(object? sender, EventArgs args) => Publish(Changed);

    private void PublishFailure(long requestedGeneration, SearchPhase phase, string message)
    {
        lock (stateSync)
        {
            if (requestedGeneration != generation)
            {
                return;
            }

            State = State with
            {
                Phase = State.Rows.Count == 0 ? phase : SearchPhase.Content,
                Message = message,
            };
        }

        Publish(Changed);
    }

    private void ApplyProjection(SearchProjection projection, SearchPhase? emptyOverride = null)
    {
        backingByRowId.Clear();
        foreach ((string id, SearchResultBacking backing) in projection.BackingByRowId)
        {
            backingByRowId[id] = backing;
        }

        SearchPhase phase = projection.VisibleFileCount == 0
            ? projection.TotalFileCount > 0 ? SearchPhase.FilteredEmpty : emptyOverride ?? SearchPhase.Empty
            : SearchPhase.Content;
        State = State with
        {
            Phase = phase,
            Rows = projection.Rows,
            VisibleFileCount = projection.VisibleFileCount,
            TotalFileCount = projection.TotalFileCount,
            ResponseCount = projection.ResponseCount,
            Message = null,
        };
    }

    private SearchProjection Project(
        IReadOnlyCollection<SearchResponse> source,
        SearchFilters filters,
        SearchOrdering ordering,
        IReadOnlySet<string> expandedIds)
    {
        SearchFileCandidate[] allCandidates = source
            .SelectMany(response => response.Files.Select(file => new SearchFileCandidate(response, file, false)))
            .Concat(source.SelectMany(response =>
                response.LockedFiles.Select(file => new SearchFileCandidate(response, file, true))))
            .ToArray();
        SearchFileCandidate[] candidates = allCandidates
            .Where(candidate => Matches(candidate, filters))
            .ToArray();
        IEnumerable<IGrouping<SearchFolderKey, SearchFileCandidate>> groups = candidates.GroupBy(candidate =>
            new SearchFolderKey(
                candidate.Response.Username,
                FeatureValueFormatter.ParentPath(candidate.File.Filename),
                candidate.IsLocked));
        groups = ordering switch
        {
            SearchOrdering.Speed => groups
                .OrderByDescending(group => group.First().Response.UploadSpeed)
                .ThenBy(group => group.Key.FolderPath, StringComparer.CurrentCultureIgnoreCase),
            SearchOrdering.Folder => groups
                .OrderBy(group => group.Key.FolderPath, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Key.Username, StringComparer.CurrentCultureIgnoreCase),
            SearchOrdering.Bitrate => groups
                .OrderByDescending(group => group.Max(candidate => candidate.File.BitRate ?? 0))
                .ThenBy(group => group.Key.FolderPath, StringComparer.CurrentCultureIgnoreCase),
            _ => groups
                .OrderByDescending(group => group.First().Response.HasFreeUploadSlot)
                .ThenBy(group => group.First().Response.QueueLength)
                .ThenByDescending(group => group.First().Response.UploadSpeed),
        };

        var rows = new List<SearchResultRow>(candidates.Length + Math.Min(candidates.Length, source.Count));
        var backing = new Dictionary<string, SearchResultBacking>(StringComparer.Ordinal);
        foreach (IGrouping<SearchFolderKey, SearchFileCandidate> group in groups)
        {
            SearchFileCandidate first = group.First();
            string folderId = StableId("folder", group.Key.Username, group.Key.FolderPath, group.Key.IsLocked.ToString());
            bool expanded = expandedIds.Contains(ExpandAllSentinel) || expandedIds.Contains(folderId);
            SearchFileCandidate[] files = group
                .OrderBy(candidate => candidate.File.Filename, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            long aggregateSize = files.Aggregate(0L, (sum, candidate) => SaturatingAdd(sum, candidate.File.Size));
            string folderName = FeatureValueFormatter.FileName(group.Key.FolderPath);
            if (folderName.Length == 0)
            {
                folderName = group.Key.Username;
            }

            string availability = first.Response.HasFreeUploadSlot
                ? StringResources.Get("IosUiFreeSlot")
                : StringResources.Format("IosUiSearchQueue", first.Response.QueueLength);
            var folderRow = new SearchResultRow(
                folderId,
                null,
                true,
                folderName,
                StringResources.Format(
                    "IosUiSearchFolderMetadata",
                    group.Key.Username,
                    files.Length,
                    FeatureValueFormatter.Bytes(aggregateSize),
                    availability),
                group.Key.Username,
                group.Key.FolderPath,
                files.Length,
                aggregateSize,
                group.Key.IsLocked,
                first.Response.HasFreeUploadSlot,
                expanded);
            rows.Add(folderRow);
            backing[folderId] = new SearchResultBacking(first.Response, null);
            if (!expanded)
            {
                continue;
            }

            foreach (SearchFileCandidate candidate in files)
            {
                string fileId = StableId(
                    "file",
                    candidate.Response.Username,
                    candidate.File.Filename,
                    candidate.IsLocked.ToString());
                string bitrate = candidate.File.BitRate is { } value
                    ? StringResources.Format("IosUiBitrate", value)
                    : StringResources.Get("IosUiBitrateUnknown");
                rows.Add(new SearchResultRow(
                    fileId,
                    folderId,
                    false,
                    FeatureValueFormatter.FileName(candidate.File.Filename),
                    StringResources.Format(
                        "IosUiSearchFileMetadata",
                        FeatureValueFormatter.Bytes(candidate.File.Size),
                        bitrate),
                    candidate.Response.Username,
                    candidate.File.Filename,
                    1,
                    candidate.File.Size,
                    candidate.IsLocked,
                    candidate.Response.HasFreeUploadSlot,
                    false));
                backing[fileId] = new SearchResultBacking(candidate.Response, candidate.File);
            }
        }

        return new SearchProjection(rows, backing, candidates.Length, allCandidates.Length, source.Count);
    }

    private static bool Matches(SearchFileCandidate candidate, SearchFilters filters)
    {
        if (filters.OnlyFreeSlots && !candidate.Response.HasFreeUploadSlot ||
            filters.HideLocked && candidate.IsLocked ||
            filters.MinimumBitrate > 0 && (candidate.File.BitRate ?? 0) < filters.MinimumBitrate)
        {
            return false;
        }

        string extension = candidate.File.Extension?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
        if (filters.Format == SearchFormat.Lossless && extension is not ("flac" or "wav" or "aiff" or "alac") ||
            filters.Format == SearchFormat.Lossy && extension is not ("mp3" or "ogg" or "m4a" or "aac" or "opus"))
        {
            return false;
        }

        if (filters.Category != SearchCategory.Any && CategoryForExtension(extension) != filters.Category)
        {
            return false;
        }

        string[] searchableValues =
        [
            candidate.File.Filename,
            candidate.Response.Username,
            extension,
        ];
        bool includesEveryTerm = SplitTextExpression(filters.Text).All(term =>
            searchableValues.Any(value => value.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        bool includesExcludedTerm = SplitTextExpression(filters.ExcludedText).Any(term =>
            searchableValues.Any(value => value.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        return includesEveryTerm && !includesExcludedTerm;
    }

    /// <summary>Maps common Soulseek filename extensions into mutually exclusive broad media categories.</summary>
    private static SearchCategory CategoryForExtension(string extension) => extension switch
    {
        "aac" or "aiff" or "alac" or "ape" or "flac" or "m4a" or "mp3" or "ogg" or "opus" or "wav" or
            "wma" => SearchCategory.Audio,
        "3gp" or "avi" or "flv" or "m4v" or "mkv" or "mov" or "mp4" or "mpeg" or "mpg" or "webm" or
            "wmv" => SearchCategory.Video,
        "bmp" or "gif" or "heic" or "heif" or "jpeg" or "jpg" or "png" or "svg" or "tif" or "tiff" or
            "webp" => SearchCategory.Image,
        "csv" or "doc" or "docx" or "epub" or "md" or "mobi" or "odp" or "ods" or "odt" or "pdf" or
            "ppt" or "pptx" or "rtf" or "txt" or "xls" or "xlsx" => SearchCategory.Document,
        "7z" or "bz2" or "gz" or "rar" or "tar" or "tgz" or "xz" or "zip" => SearchCategory.Archive,
        _ => SearchCategory.Other,
    };

    /// <summary>Splits one legacy-compatible text expression into independently matched non-empty terms.</summary>
    private static IEnumerable<string> SplitTextExpression(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Creates the whitespace-normalized representation shown after a filter is applied.</summary>
    private static string NormalizeTextExpression(string expression) =>
        string.Join(' ', SplitTextExpression(expression));

    /// <summary>Persists filter defaults through portable keys, honoring the legacy sticky-text opt-in.</summary>
    private void PersistFilters(SearchFilters filters)
    {
        PreferencesState.FreeUploadSlotsOnly = filters.OnlyFreeSlots;
        PreferencesState.HideLockedResultsInSearch = filters.HideLocked;
        PreferencesState.FilterFormat = filters.Format switch
        {
            SearchFormat.Lossless => FormatFilterType.Lossless,
            SearchFormat.Lossy => FormatFilterType.Lossy,
            _ => FormatFilterType.Any,
        };
        PreferencesState.FilterMinBitrateKbs = filters.MinimumBitrate;
        keyValueStore.PutBoolean(KeyConsts.M_OnlyFreeUploadSlots, filters.OnlyFreeSlots);
        keyValueStore.PutBoolean(KeyConsts.M_HideLockedSearch, filters.HideLocked);
        keyValueStore.PutInt(KeyConsts.M_FilterFormat, (int)PreferencesState.FilterFormat);
        keyValueStore.PutInt(KeyConsts.M_FilterMinBitrateKbs, filters.MinimumBitrate);
        keyValueStore.PutInt(KeyConsts.M_IosSearchFilterCategory, (int)filters.Category);

        if (PreferencesState.FilterSticky)
        {
            string inclusive = string.Join(' ', SplitTextExpression(filters.Text));
            string exclusive = string.Join(' ', SplitTextExpression(filters.ExcludedText).Select(term => $"-{term}"));
            string serialized = string.Join(
                ' ',
                new[] { inclusive, exclusive }.Where(value => !string.IsNullOrWhiteSpace(value)));
            PreferencesState.FilterStickyString = serialized;
            keyValueStore.PutString(KeyConsts.M_FilterStickyString, serialized);
        }

        keyValueStore.Flush();
    }

    /// <summary>Persists the current ordering through the shared portable preference key.</summary>
    private void PersistOrdering(SearchOrdering ordering)
    {
        PreferencesState.DefaultSearchResultSortAlgorithm = ordering switch
        {
            SearchOrdering.Speed => SearchResultSorting.Fastest,
            SearchOrdering.Folder => SearchResultSorting.FolderAlphabetical,
            SearchOrdering.Bitrate => SearchResultSorting.BitRate,
            _ => SearchResultSorting.Available,
        };
        keyValueStore.PutInt(
            KeyConsts.M_DefaultSearchResultSortAlgorithm,
            (int)PreferencesState.DefaultSearchResultSortAlgorithm);
        keyValueStore.Flush();
    }

    private void AddHistory(string query)
    {
        if (!PreferencesState.RememberSearchHistory)
        {
            return;
        }

        List<string> history = RestoreHistory()
            .Where(item => !string.Equals(item, query, StringComparison.CurrentCultureIgnoreCase))
            .Prepend(query)
            .Take(MaximumHistoryCount)
            .ToList();
        keyValueStore.PutString(HistoryKey, SerializationHelper.SaveStringListToString(history));
        keyValueStore.Flush();
        lock (stateSync)
        {
            State = State with { History = history };
        }
    }

    private IReadOnlyList<string> RestoreHistory()
    {
        string payload = keyValueStore.GetString(HistoryKey, string.Empty) ?? string.Empty;
        try
        {
            return SerializationHelper.RestoreStringListFromString(payload)
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

    private static void ValidateTarget(SearchTarget target)
    {
        if (target.Audience is SearchAudience.User or SearchAudience.Room &&
            string.IsNullOrWhiteSpace(target.Subject))
        {
            throw new ArgumentException(StringResources.Get("request_user_error_empty"), nameof(target));
        }
    }

    private SearchScope MapScope(SearchTarget target) => target.Audience switch
    {
        SearchAudience.User => SearchScope.User(target.Subject!),
        SearchAudience.Room => SearchScope.Room(target.Subject!),
        SearchAudience.UserList => SearchScope.User(GetSavedUsernames()),
        SearchAudience.Wishlist => SearchScope.Wishlist,
        _ => SearchScope.Network,
    };

    private string[] GetSavedUsernames()
    {
        string[] usernames = userListService?.GetSnapshot()
            .Where(entry => entry.Role == Seeker.UserRole.Friend)
            .Select(entry => entry.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        return usernames.Length > 0
            ? usernames
            : throw new InvalidOperationException(StringResources.Get("IosUiSearchUserListEmpty"));
    }

    private SearchFilters DefaultFilters()
    {
        string[] stickyTerms = PreferencesState.FilterSticky
            ? SplitTextExpression(PreferencesState.FilterStickyString ?? string.Empty).ToArray()
            : [];
        int storedCategory = keyValueStore.GetInt(
            KeyConsts.M_IosSearchFilterCategory,
            (int)SearchCategory.Any);
        return new SearchFilters(
            string.Join(' ', stickyTerms.Where(term => !term.StartsWith("-", StringComparison.Ordinal))),
            string.Join(' ', stickyTerms
                .Where(term => term.StartsWith("-", StringComparison.Ordinal) && term.Length > 1)
                .Select(term => term[1..])),
            PreferencesState.FreeUploadSlotsOnly,
            PreferencesState.HideLockedResultsInSearch,
            PreferencesState.FilterFormat switch
            {
                FormatFilterType.Lossless => SearchFormat.Lossless,
                FormatFilterType.Lossy => SearchFormat.Lossy,
                _ => SearchFormat.Any,
            },
            Enum.IsDefined((SearchCategory)storedCategory)
                ? (SearchCategory)storedCategory
                : SearchCategory.Any,
            Math.Max(0, PreferencesState.FilterMinBitrateKbs));
    }

    private static SearchOrdering DefaultOrdering() => PreferencesState.DefaultSearchResultSortAlgorithm switch
    {
        SearchResultSorting.Fastest => SearchOrdering.Speed,
        SearchResultSorting.FolderAlphabetical => SearchOrdering.Folder,
        SearchResultSorting.BitRate => SearchOrdering.Bitrate,
        _ => SearchOrdering.Availability,
    };

    private void ResetExpansionPreference()
    {
        expandedFolderIds.Clear();
        if (PreferencesState.ExpandAllResults)
        {
            expandedFolderIds.Add(ExpandAllSentinel);
        }
    }

    private static string StableId(string kind, params string[] values)
    {
        string source = string.Join("\u001f", values.Prepend(kind));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{kind}-{Convert.ToHexString(digest.AsSpan(0, 10)).ToLowerInvariant()}";
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + Math.Max(0, right);

    private sealed record SearchFileCandidate(SearchResponse Response, SoulseekFile File, bool IsLocked);

    private sealed record SearchFolderKey(string Username, string FolderPath, bool IsLocked);

    private sealed record SearchResultBacking(SearchResponse Response, SoulseekFile? File);

    private sealed record SearchProjection(
        IReadOnlyList<SearchResultRow> Rows,
        IReadOnlyDictionary<string, SearchResultBacking> BackingByRowId,
        int VisibleFileCount,
        int TotalFileCount,
        int ResponseCount);
}
