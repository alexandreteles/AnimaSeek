using Common;
using Common.Search;
using System.Globalization;
using System.Text;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Soulseek;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Runs canonical wishlist headers at the server cadence while the app is active and supplies one-shot background work.
/// </summary>
internal sealed class IosWishlistService : IDisposable
{
    private const string ResultsKey = "AnimaSeek.Wishlist.Results.v1";
    private readonly Lock timerSync = new();
    private readonly ISoulseekClient client;
    private readonly IKeyValueStore keyValueStore;
    private readonly INotifier notifier;
    private readonly ILoggerBackend logger;
    private readonly WishlistStateMutationGate mutationGate;
    private readonly SemaphoreSlim runGate = new(1, 1);
    private Timer? foregroundTimer;
    private TimeSpan? serverInterval;
    private bool foreground;
    private bool disposed;

    /// <summary>Raised after a wishlist header or its durable result snapshot changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets immutable wishlist headers with their exact restorable results.</summary>
    /// <returns>Entries ordered case-insensitively by query.</returns>
    public IReadOnlyList<WishlistEntrySnapshot> GetSnapshot()
    {
        Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders() ?? [];
        IReadOnlyDictionary<int, IReadOnlyCollection<SearchResponse>> storedResults = RestoreResults();
        return headers
            .Where(pair => pair.Key < 0 && !string.IsNullOrWhiteSpace(pair.Value?.LastSearchTerm))
            .Select(pair => new WishlistEntrySnapshot(
                pair.Key,
                pair.Value.LastSearchTerm,
                pair.Value.LastSearchResultsCount,
                pair.Value.UnseenCount,
                pair.Value.LastRanTime > 0
                    ? new DateTimeOffset(new DateTime(pair.Value.LastRanTime, DateTimeKind.Utc))
                    : null,
                storedResults.GetValueOrDefault(pair.Key) ?? []))
            .OrderBy(entry => entry.Query, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Creates a distinct wishlist query in the canonical negative-ID namespace.</summary>
    /// <param name="query">The non-empty query to schedule.</param>
    /// <param name="cancellationToken">Cancels before the serialized mutation begins.</param>
    /// <returns>The new stable negative wishlist identifier.</returns>
    public async Task<int> CreateAsync(string query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        query = NormalizeQuery(query);
        int created = await mutationGate.RunAsync(() =>
        {
            Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders() ?? [];
            if (headers.Any(pair => pair.Key < 0 && string.Equals(
                    pair.Value.LastSearchTerm,
                    query,
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new InvalidOperationException(StringResources.Get("IosUiWishlistDuplicate"));
            }

            int identifier = headers.Keys.Where(key => key < 0).DefaultIfEmpty(0).Min() - 1;
            headers[identifier] = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(query, 0, 0, 0);
            PersistHeaders(headers);
            return identifier;
        }, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return created;
    }

    /// <summary>Renames a wishlist and clears results that belong to its old query.</summary>
    /// <param name="identifier">The stable negative wishlist identifier.</param>
    /// <param name="query">The replacement non-empty query.</param>
    /// <param name="cancellationToken">Cancels before the serialized mutation begins.</param>
    public async Task RenameAsync(int identifier, string query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        query = NormalizeQuery(query);
        await mutationGate.RunAsync(() =>
        {
            Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders() ?? [];
            if (!headers.ContainsKey(identifier) || identifier >= 0)
            {
                throw new InvalidOperationException(StringResources.Get("IosUiWishlistMissing"));
            }

            if (headers.Any(pair => pair.Key != identifier && pair.Key < 0 && string.Equals(
                    pair.Value.LastSearchTerm,
                    query,
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                throw new InvalidOperationException(StringResources.Get("IosUiWishlistDuplicate"));
            }

            headers[identifier] = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(query, 0, 0, 0);
            Dictionary<int, IReadOnlyCollection<SearchResponse>> results = RestoreResults()
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            results.Remove(identifier);
            PersistResults(results);
            PersistHeaders(headers);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes one wishlist header and its durable exact results.</summary>
    /// <param name="identifier">The stable negative wishlist identifier.</param>
    /// <param name="cancellationToken">Cancels before the serialized mutation begins.</param>
    public async Task DeleteAsync(int identifier, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await mutationGate.RunAsync(() =>
        {
            Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders() ?? [];
            headers.Remove(identifier);
            Dictionary<int, IReadOnlyCollection<SearchResponse>> results = RestoreResults()
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            results.Remove(identifier);
            PersistResults(results);
            PersistHeaders(headers);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the current results seen without changing the scheduler's result count.</summary>
    /// <param name="identifier">The stable negative wishlist identifier.</param>
    /// <param name="cancellationToken">Cancels before the serialized mutation begins.</param>
    public async Task MarkSeenAsync(int identifier, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await mutationGate.RunAsync(() =>
        {
            Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders() ?? [];
            if (headers.TryGetValue(identifier, out SavedStateSearchTabHeader? current))
            {
                headers[identifier] = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                    current.LastSearchTerm,
                    current.LastSearchResultsCount,
                    current.LastRanTime,
                    0);
                PersistHeaders(headers);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Creates a wishlist runner and begins observing server cadence updates.</summary>
    /// <param name="client">The process Soulseek client used to issue wishlist-scoped searches.</param>
    /// <param name="keyValueStore">The store containing the canonical wishlist header dictionary.</param>
    /// <param name="notifier">The semantic notification sink for newly discovered results.</param>
    /// <param name="logger">The diagnostics sink for malformed state and timer failures.</param>
    /// <param name="mutationGate">The process-wide gate shared with settings import and future wishlist editors.</param>
    public IosWishlistService(
        ISoulseekClient client,
        IKeyValueStore keyValueStore,
        INotifier notifier,
        ILoggerBackend logger,
        WishlistStateMutationGate mutationGate)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.mutationGate = mutationGate ?? throw new ArgumentNullException(nameof(mutationGate));
        client.ServerInfoReceived += OnServerInfoReceived;
        UpdateServerInterval(client.ServerInfo?.WishlistInterval);
    }

    /// <summary>Gets the usable server cadence, including the two-minute safety floor.</summary>
    public TimeSpan? ServerInterval
    {
        get
        {
            lock (timerSync)
            {
                return serverInterval;
            }
        }
    }

    /// <summary>Starts or stops the foreground timer without changing durable wishlist state.</summary>
    /// <param name="active"><see langword="true"/> while at least one application scene is active.</param>
    public void SetForegroundActive(bool active)
    {
        lock (timerSync)
        {
            if (disposed)
            {
                return;
            }

            foreground = active;
            RearmTimerLocked();
        }
    }

    /// <summary>
    /// Searches the least-recently-run canonical wishlist and commits its complete replacement header in one store write.
    /// </summary>
    /// <param name="cancellationToken">Cancels gate acquisition or the network search.</param>
    /// <returns>
    /// <see langword="true"/> when no wishlist was due or one was searched and committed; otherwise
    /// <see langword="false"/> when a connection or canonical state was unavailable.
    /// </returns>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsConnected())
        {
            return false;
        }

        await runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<int, SavedStateSearchTabHeader>? headers = RestoreHeaders();
            if (headers is null)
            {
                return false;
            }

            KeyValuePair<int, SavedStateSearchTabHeader>? selected = WishlistStatePolicy.SelectNext(headers);
            if (!selected.HasValue)
            {
                return true;
            }

            int searchId = selected.Value.Key;
            SavedStateSearchTabHeader original = selected.Value.Value;
            var options = new SearchOptions(
                searchTimeout: 12_000,
                responseLimit: Math.Max(1, PreferencesState.NumberSearchResults),
                maximumPeerQueueLength: int.MaxValue,
                responseFilter: response => !PreferencesState.FreeUploadSlotsOnly || response.HasFreeUploadSlot,
                filterResponses: true);
            (_, IReadOnlyCollection<SearchResponse> responses) = await client.SearchAsync(
                SearchQuery.FromText(original.LastSearchTerm),
                scope: SearchScope.Wishlist,
                options: options,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Reload immediately before the single serialized write. An import or future UI edit that removed or renamed the
            // wishlist while the network request was in flight wins instead of being silently overwritten.
            return await mutationGate.RunAsync(() =>
            {
                Dictionary<int, SavedStateSearchTabHeader>? latestHeaders = RestoreHeaders();
                if (latestHeaders is null ||
                    !latestHeaders.TryGetValue(searchId, out SavedStateSearchTabHeader? latest) ||
                    !string.Equals(latest.LastSearchTerm, original.LastSearchTerm, StringComparison.Ordinal))
                {
                    return false;
                }

                (SavedStateSearchTabHeader replacement, int newResults) = WishlistStatePolicy.ApplyResult(
                    latest,
                    responses.Count,
                    DateTime.UtcNow);
                latestHeaders[searchId] = replacement;
                Dictionary<int, IReadOnlyCollection<SearchResponse>> storedResults = RestoreResults()
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                storedResults[searchId] = responses;
                PersistResults(storedResults);
                PersistHeaders(latestHeaders);

                if (newResults > 0)
                {
                    notifier.Post(new WishlistHitNotification(searchId, replacement.LastSearchTerm, newResults));
                }

                Changed?.Invoke(this, EventArgs.Empty);

                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runGate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (timerSync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreground = false;
            foregroundTimer?.Dispose();
            foregroundTimer = null;
        }

        client.ServerInfoReceived -= OnServerInfoReceived;
        runGate.Dispose();
    }

    private bool IsConnected() =>
        client.State.HasFlag(SoulseekClientStates.Connected) &&
        client.State.HasFlag(SoulseekClientStates.LoggedIn);

    private Dictionary<int, SavedStateSearchTabHeader>? RestoreHeaders()
    {
        string? payload = keyValueStore.GetString(KeyConsts.M_SearchTabsState_Headers, string.Empty);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new Dictionary<int, SavedStateSearchTabHeader>();
        }

        try
        {
            return SerializationHelper.RestoreSavedStateHeaderDictFromString(payload) ??
                new Dictionary<int, SavedStateSearchTabHeader>();
        }
        catch (Exception exception)
        {
            logger.FirebaseError("The canonical wishlist header state is malformed; the refresh was skipped.", exception);
            return null;
        }
    }

    private void PersistHeaders(Dictionary<int, SavedStateSearchTabHeader> headers)
    {
        keyValueStore.PutString(
            KeyConsts.M_SearchTabsState_Headers,
            SerializationHelper.SaveSavedStateHeaderDictToString(headers));
        keyValueStore.Flush();
    }

    private IReadOnlyDictionary<int, IReadOnlyCollection<SearchResponse>> RestoreResults()
    {
        string payload = keyValueStore.GetString(ResultsKey, string.Empty) ?? string.Empty;
        if (payload.Length == 0)
        {
            return new Dictionary<int, IReadOnlyCollection<SearchResponse>>();
        }

        try
        {
            IEnumerable<StoredWishlistFile> files = payload
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseStoredFile);
            return files
                .GroupBy(file => file.WishlistId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<SearchResponse>)group
                        .GroupBy(file => new StoredResponseKey(
                            file.Username,
                            file.HasFreeSlot,
                            file.UploadSpeed,
                            file.QueueLength))
                        .Select(responseGroup => new SearchResponse(
                            responseGroup.Key.Username,
                            0,
                            responseGroup.Key.HasFreeSlot,
                            responseGroup.Key.UploadSpeed,
                            responseGroup.Key.QueueLength,
                            responseGroup.Where(file => !file.IsLocked).Select(CreateFile),
                            responseGroup.Where(file => file.IsLocked).Select(CreateFile)))
                        .ToArray());
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Durable wishlist results were malformed and ignored.", exception);
            return new Dictionary<int, IReadOnlyCollection<SearchResponse>>();
        }
    }

    private void PersistResults(IReadOnlyDictionary<int, IReadOnlyCollection<SearchResponse>> results)
    {
        IEnumerable<string> lines = results
            .OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value.SelectMany(response =>
                response.Files.Select(file => FormatStoredFile(pair.Key, response, file, false))
                    .Concat(response.LockedFiles.Select(file => FormatStoredFile(pair.Key, response, file, true))))
                .Take(WishlistUtil.MaxResultsPerWishlist));
        keyValueStore.PutString(ResultsKey, string.Join('\n', lines));
        keyValueStore.Flush();
    }

    private static string FormatStoredFile(int wishlistId, SearchResponse response, Soulseek.File file, bool locked)
    {
        string attributes = string.Join(',', file.Attributes.Select(attribute =>
            $"{(int)attribute.Type}:{attribute.Value}"));
        return string.Join('\t',
            wishlistId.ToString(CultureInfo.InvariantCulture),
            Encode(response.Username),
            response.HasFreeUploadSlot ? "1" : "0",
            response.UploadSpeed.ToString(CultureInfo.InvariantCulture),
            response.QueueLength.ToString(CultureInfo.InvariantCulture),
            locked ? "1" : "0",
            file.Code.ToString(CultureInfo.InvariantCulture),
            Encode(file.Filename),
            file.Size.ToString(CultureInfo.InvariantCulture),
            Encode(file.Extension ?? string.Empty),
            Encode(attributes),
            file.IsLatin1Decoded ? "1" : "0",
            file.IsDirectoryLatin1Decoded ? "1" : "0");
    }

    private static StoredWishlistFile ParseStoredFile(string line)
    {
        string[] fields = line.Split('\t');
        if (fields.Length != 13)
        {
            throw new FormatException("Wishlist result line has an unsupported field count.");
        }

        return new StoredWishlistFile(
            int.Parse(fields[0], CultureInfo.InvariantCulture),
            Decode(fields[1]),
            fields[2] == "1",
            int.Parse(fields[3], CultureInfo.InvariantCulture),
            int.Parse(fields[4], CultureInfo.InvariantCulture),
            fields[5] == "1",
            int.Parse(fields[6], CultureInfo.InvariantCulture),
            Decode(fields[7]),
            long.Parse(fields[8], CultureInfo.InvariantCulture),
            Decode(fields[9]),
            ParseAttributes(Decode(fields[10])),
            fields[11] == "1",
            fields[12] == "1");
    }

    private static Soulseek.File CreateFile(StoredWishlistFile stored) => new(
        stored.Code,
        stored.Filename,
        stored.Size,
        stored.Extension,
        stored.Attributes,
        stored.IsLatin1Decoded,
        stored.IsDirectoryLatin1Decoded);

    private static IReadOnlyList<FileAttribute> ParseAttributes(string payload) => payload.Length == 0
        ? []
        : payload.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split(':'))
            .Where(parts => parts.Length == 2)
            .Select(parts => new FileAttribute(
                (FileAttributeType)int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture)))
            .ToArray();

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string NormalizeQuery(string query)
    {
        query = query?.Trim() ?? string.Empty;
        return query.Length > 0
            ? query
            : throw new ArgumentException(StringResources.Get("no_search_text"), nameof(query));
    }

    private void OnServerInfoReceived(object? sender, ServerInfo info) =>
        UpdateServerInterval(info.WishlistInterval);

    private void UpdateServerInterval(int? intervalSeconds)
    {
        lock (timerSync)
        {
            if (disposed)
            {
                return;
            }

            serverInterval = WishlistStatePolicy.NormalizeServerInterval(intervalSeconds);
            RearmTimerLocked();
        }
    }

    private void RearmTimerLocked()
    {
        foregroundTimer?.Dispose();
        foregroundTimer = null;
        if (!foreground || serverInterval is not { } interval)
        {
            return;
        }

        foregroundTimer = new Timer(OnForegroundTimer, null, interval, interval);
    }

    private void OnForegroundTimer(object? state) => _ = RunForegroundTimerAsync();

    private async Task RunForegroundTimerAsync()
    {
        try
        {
            await RunOnceAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // A timer callback can race with process teardown.
        }
        catch (OperationCanceledException)
        {
            // A later foreground cycle will retry the oldest wishlist.
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Foreground wishlist refresh failed.", exception);
        }
    }

    private sealed record StoredResponseKey(
        string Username,
        bool HasFreeSlot,
        int UploadSpeed,
        int QueueLength);

    private sealed record StoredWishlistFile(
        int WishlistId,
        string Username,
        bool HasFreeSlot,
        int UploadSpeed,
        int QueueLength,
        bool IsLocked,
        int Code,
        string Filename,
        long Size,
        string Extension,
        IReadOnlyList<FileAttribute> Attributes,
        bool IsLatin1Decoded,
        bool IsDirectoryLatin1Decoded);
}

/// <summary>Represents one editable wishlist and its exact durable search responses.</summary>
/// <param name="Id">The canonical negative wishlist identifier.</param>
/// <param name="Query">The scheduled search expression.</param>
/// <param name="ResultCount">The latest peer-response count used by scheduler saturation rules.</param>
/// <param name="UnseenCount">The number of results not yet opened.</param>
/// <param name="LastRun">The last successful UTC run, or null before the first run.</param>
/// <param name="Responses">Restored exact peer/file responses, possibly bounded by the durable result cap.</param>
internal sealed record WishlistEntrySnapshot(
    int Id,
    string Query,
    int ResultCount,
    int UnseenCount,
    DateTimeOffset? LastRun,
    IReadOnlyCollection<SearchResponse> Responses);
