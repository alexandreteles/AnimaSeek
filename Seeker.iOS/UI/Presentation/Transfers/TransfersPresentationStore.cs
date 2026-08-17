using AnimaSeek.iOS.UI.Presentation;
using System.Security.Cryptography;
using System.Text;
using Common;
using Soulseek;

namespace AnimaSeek.iOS.UI.Presentation.Transfers;

/// <summary>Identifies the direction selected in the Transfers root.</summary>
internal enum TransferMode
{
    Downloads,
    Uploads,
}

/// <summary>Identifies whether the root presents individual files or aggregate folder rows.</summary>
internal enum TransferGrouping
{
    ByFolder,
    Individual,
}

/// <summary>Identifies the truthful state shown with text and symbol in one transfer row.</summary>
internal enum TransferPresentationStatus
{
    NotStarted,
    Queued,
    Active,
    Paused,
    WaitingForPeer,
    Failed,
    TimedOut,
    Denied,
    Offline,
    Aborted,
    Canceled,
    Completed,
}

/// <summary>Describes the actions the session façade can safely accept for one row.</summary>
/// <param name="CanRetry">Whether retry is valid now.</param>
/// <param name="CanPause">Whether active work can be retained and paused.</param>
/// <param name="CanClear">Whether the transfer row can be removed.</param>
/// <param name="CanOpen">Whether a verified completed file URL is available.</param>
/// <param name="UnavailableReason">Why network-affecting actions are unavailable, when useful.</param>
internal sealed record TransferCapabilities(
    bool CanRetry,
    bool CanPause,
    bool CanClear,
    bool CanOpen,
    string? UnavailableReason);

/// <summary>Describes one individual transfer row.</summary>
/// <param name="Id">The stable opaque identifier.</param>
/// <param name="Identity">The session-owned portable identity.</param>
/// <param name="Title">The final filename.</param>
/// <param name="Folder">The folder grouping label.</param>
/// <param name="Peer">The remote username.</param>
/// <param name="Status">The non-color-only presentation state.</param>
/// <param name="StatusText">Localized state text.</param>
/// <param name="Metadata">Decision-useful size, speed, and peer metadata.</param>
/// <param name="Progress">Normalized determinate progress.</param>
/// <param name="Size">The expected byte count, or a negative value when unknown.</param>
/// <param name="BytesTransferred">The observed transferred byte count.</param>
/// <param name="FinalUri">A verified completed download URI.</param>
/// <param name="Capabilities">Actions valid for this exact state.</param>
internal sealed record TransferRow(
    string Id,
    TransferIdentity Identity,
    string Title,
    string Folder,
    string Peer,
    TransferPresentationStatus Status,
    string StatusText,
    string Metadata,
    double Progress,
    long Size,
    long BytesTransferred,
    string? FinalUri,
    TransferCapabilities Capabilities);

/// <summary>Describes an aggregate folder row and its stable children.</summary>
/// <param name="Id">The stable folder identifier.</param>
/// <param name="Title">The folder name.</param>
/// <param name="Peer">The remote username.</param>
/// <param name="StatusText">A concise, non-color aggregate status.</param>
/// <param name="Metadata">File count and aggregate size metadata.</param>
/// <param name="Progress">Byte-weighted aggregate progress.</param>
/// <param name="Files">The individual rows represented by this folder.</param>
internal sealed record TransferFolderRow(
    string Id,
    string Title,
    string Peer,
    string StatusText,
    string Metadata,
    double Progress,
    IReadOnlyList<TransferRow> Files);

/// <summary>Represents the immutable Transfers root snapshot.</summary>
/// <param name="Mode">Downloads or uploads.</param>
/// <param name="Grouping">Folder or individual presentation.</param>
/// <param name="Files">All filtered individual rows for the selected mode.</param>
/// <param name="Folders">Aggregate folder rows for the selected mode.</param>
/// <param name="IsConnected">Whether network-affecting actions can be attempted.</param>
/// <param name="Message">A persistent recoverable action failure.</param>
internal sealed record TransfersScreenState(
    TransferMode Mode,
    TransferGrouping Grouping,
    IReadOnlyList<TransferRow> Files,
    IReadOnlyList<TransferFolderRow> Folders,
    bool IsConnected,
    string? Message)
{
    /// <summary>Gets whether the selected presentation has no rows.</summary>
    public bool IsEmpty => Files.Count == 0;

    /// <summary>Creates the initial Downloads-by-folder snapshot.</summary>
    public static TransfersScreenState Initial { get; } = new(
        TransferMode.Downloads,
        TransferGrouping.ByFolder,
        [],
        [],
        false,
        null);
}

/// <summary>Reports the explicit scope and accepted subset of one capability-aware transfer batch.</summary>
/// <param name="RequestedCount">The selected rows addressed by the command.</param>
/// <param name="EligibleCount">The selected rows whose current capabilities allow the command.</param>
/// <param name="AcceptedCount">The eligible rows accepted by the session façade.</param>
internal sealed record TransferBatchCommandResult(
    int RequestedCount,
    int EligibleCount,
    int AcceptedCount);

/// <summary>
/// Maps session transfer snapshots to bounded-cadence immutable rows and executes capability-driven commands.
/// </summary>
internal sealed class TransfersPresentationStore : FeaturePresentationStore, IDisposable
{
    private static readonly TimeSpan CoalescingInterval = TimeSpan.FromMilliseconds(120);
    private readonly AppSession session;
    private readonly Lock stateSync = new();
    private Timer? publicationTimer;
    private bool refreshPending;
    private bool disposed;

    /// <summary>Creates a store and subscribes to connection and high-frequency transfer events.</summary>
    /// <param name="session">The application session façade supplying detached transfer snapshots.</param>
    public TransfersPresentationStore(AppSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        State = TransfersScreenState.Initial;
        session.TransfersChanged += OnTransfersChanged;
        session.StateChanged += OnSessionStateChanged;
        RefreshNow();
    }

    /// <summary>Gets the latest immutable Transfers snapshot.</summary>
    public TransfersScreenState State { get; private set; }

    /// <summary>Raised on the UI context after a coalesced transfer snapshot is ready.</summary>
    public event EventHandler? Changed;

    /// <summary>Switches between Downloads and Uploads without changing grouping.</summary>
    /// <param name="mode">The selected transfer direction.</param>
    public void SetMode(TransferMode mode)
    {
        lock (stateSync)
        {
            State = State with { Mode = mode, Message = null };
        }

        RefreshNow();
    }

    /// <summary>Switches between folder aggregate rows and individual file rows.</summary>
    /// <param name="grouping">The selected grouping.</param>
    public void SetGrouping(TransferGrouping grouping)
    {
        lock (stateSync)
        {
            State = State with { Grouping = grouping };
        }

        Publish(Changed);
    }

    /// <summary>Returns the current file rows for one aggregate folder.</summary>
    /// <param name="folderId">The stable folder row identifier.</param>
    /// <returns>The folder's current file rows, or an empty array after it disappears.</returns>
    public IReadOnlyList<TransferRow> GetFolderFiles(string folderId) => State.Folders
        .FirstOrDefault(folder => folder.Id == folderId)?.Files ?? [];

    /// <summary>Executes retry, pause, or clear through the session façade and reports stale-row rejection.</summary>
    /// <param name="rowId">The stable row identifier.</param>
    /// <param name="command">The requested mutation.</param>
    /// <param name="cancellationToken">Cancels before the mutation begins.</param>
    /// <returns><see langword="true"/> when the current transfer accepted the action.</returns>
    public async Task<bool> ExecuteAsync(
        string rowId,
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        TransferRow? row = State.Files.FirstOrDefault(candidate => candidate.Id == rowId);
        if (row is null)
        {
            return false;
        }

        if (!CanExecute(row, command))
        {
            lock (stateSync)
            {
                State = State with { Message = StringResources.Get("IosUiActionUnavailable") };
            }

            Publish(Changed);
            return false;
        }

        try
        {
            bool accepted = await session.ExecuteTransferCommandAsync(
                row.Identity,
                command,
                cancellationToken);
            lock (stateSync)
            {
                State = State with
                {
                    Message = accepted ? null : StringResources.Get("chosen_transfer_doesnt_exist"),
                };
            }

            RefreshNow();
            return accepted;
        }
        catch (Exception)
        {
            lock (stateSync)
            {
                State = State with { Message = StringResources.Get("IosUiActionFailed") };
            }

            Publish(Changed);
            return false;
        }
    }

    /// <summary>Executes one command for every currently matching file in a folder.</summary>
    /// <param name="folderId">The stable aggregate folder identifier.</param>
    /// <param name="command">The command to apply to capable children.</param>
    /// <param name="cancellationToken">Cancels remaining mutations.</param>
    /// <returns>The number of child actions accepted by the session.</returns>
    public async Task<int> ExecuteFolderAsync(
        string folderId,
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        TransferRow[] files = [.. GetFolderFiles(folderId)];
        int accepted = 0;
        foreach (TransferRow file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool capable = CanExecute(file, command);
            if (capable && await session.ExecuteTransferCommandAsync(file.Identity, command, cancellationToken))
            {
                accepted++;
            }
        }

        RefreshNow();
        return accepted;
    }

    /// <summary>Executes one command against the currently eligible subset of an explicit stable-ID selection.</summary>
    /// <param name="rowIds">Stable individual transfer identifiers selected in native edit mode.</param>
    /// <param name="command">Retry/resume, pause, or clear.</param>
    /// <param name="cancellationToken">Cancels remaining mutations before they are attempted.</param>
    /// <returns>Requested, eligible, and accepted counts so partial capability is never hidden.</returns>
    public async Task<TransferBatchCommandResult> ExecuteBatchAsync(
        IReadOnlyCollection<string> rowIds,
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ObjectDisposedException.ThrowIf(disposed, this);
        HashSet<string> requestedIds = rowIds.ToHashSet(StringComparer.Ordinal);
        TransferRow[] requested = State.Files
            .Where(row => requestedIds.Contains(row.Id))
            .ToArray();
        TransferRow[] eligible = requested.Where(row => CanExecute(row, command)).ToArray();
        int accepted = 0;
        foreach (TransferRow row in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await session.ExecuteTransferCommandAsync(row.Identity, command, cancellationToken))
                {
                    accepted++;
                }
            }
            catch (Exception)
            {
                lock (stateSync)
                {
                    State = State with { Message = StringResources.Get("IosUiActionFailed") };
                }
            }
        }

        RefreshNow();
        return new TransferBatchCommandResult(requested.Length, eligible.Length, accepted);
    }

    /// <summary>Refreshes immediately from durable and live session state.</summary>
    public void RefreshNow()
    {
        if (disposed)
        {
            return;
        }

        TransferSnapshot[] snapshots = session.TransferSnapshots
            .Where(snapshot => snapshot.Identity.Direction == DirectionFor(State.Mode))
            .ToArray();
        TransferRow[] rows = snapshots.Select(MapRow)
            .OrderBy(row => StatusOrder(row.Status))
            .ThenBy(row => row.Folder, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        TransferFolderRow[] folders = rows
            .GroupBy(row => new FolderKey(row.Peer, row.Folder))
            .Select(group => MapFolder(group.Key, group.ToArray()))
            .OrderBy(folder => folder.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(folder => folder.Peer, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        lock (stateSync)
        {
            State = State with
            {
                Files = rows,
                Folders = folders,
                IsConnected = session.IsConnected,
            };
            refreshPending = false;
        }

        Publish(Changed);
    }

    /// <summary>Stops event observation and releases the coalescing timer.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.TransfersChanged -= OnTransfersChanged;
        session.StateChanged -= OnSessionStateChanged;
        publicationTimer?.Dispose();
        publicationTimer = null;
    }

    private void OnTransfersChanged(object? sender, EventArgs args)
    {
        lock (stateSync)
        {
            if (refreshPending || disposed)
            {
                return;
            }

            refreshPending = true;
            publicationTimer ??= new Timer(_ => RefreshNow(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            publicationTimer.Change(CoalescingInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSessionStateChanged(object? sender, EventArgs args) => RefreshNow();

    private TransferRow MapRow(TransferSnapshot snapshot)
    {
        TransferPresentationStatus status = MapStatus(snapshot);
        string statusText = StatusText(status);
        var metadataSegments = new List<string> { snapshot.Identity.Username };
        if (snapshot.Size >= 0)
        {
            metadataSegments.Add(FeatureValueFormatter.Bytes(snapshot.Size));
        }

        if (snapshot.AverageSpeed > 0 && status == TransferPresentationStatus.Active)
        {
            metadataSegments.Add(FeatureValueFormatter.Rate(snapshot.AverageSpeed));
        }

        bool canRetry = snapshot.Identity.Direction == TransferDirection.Download &&
            status is (TransferPresentationStatus.Paused or TransferPresentationStatus.Failed or
                TransferPresentationStatus.TimedOut or TransferPresentationStatus.Denied or
                TransferPresentationStatus.Offline or TransferPresentationStatus.Aborted or
                TransferPresentationStatus.Canceled) &&
            !snapshot.IsProcessing &&
            session.IsConnected;
        bool canPause = snapshot.IsProcessing &&
            !snapshot.State.HasFlag(TransferStates.Completed) &&
            !snapshot.State.HasFlag(TransferStates.Cancelled);
        string? unavailableReason = !session.IsConnected && snapshot.Identity.Direction == TransferDirection.Download
            ? StringResources.Get("MustBeLoggedInToRetryDL")
            : null;
        return new TransferRow(
            snapshot.Identity.StableId,
            snapshot.Identity,
            snapshot.DisplayName,
            string.IsNullOrWhiteSpace(snapshot.FolderName)
                ? FeatureValueFormatter.ParentPath(snapshot.Identity.RemoteFilename)
                : snapshot.FolderName,
            snapshot.Identity.Username,
            status,
            statusText,
            string.Join(" · ", metadataSegments),
            snapshot.Progress,
            snapshot.Size,
            snapshot.BytesTransferred,
            snapshot.FinalUri,
            new TransferCapabilities(canRetry, canPause, true, snapshot.CanOpen, unavailableReason));
    }

    private static TransferFolderRow MapFolder(FolderKey key, IReadOnlyList<TransferRow> rows)
    {
        long totalSize = rows.Aggregate(0L, (sum, row) => SaturatingAdd(sum, row.Size));
        long transferred = rows.Aggregate(0L, (sum, row) =>
            SaturatingAdd(sum, Math.Min(Math.Max(0, row.BytesTransferred), Math.Max(0, row.Size))));
        double progress = totalSize > 0
            ? Math.Clamp(transferred / (double)totalSize, 0, 1)
            : rows.Count == 0 ? 0 : rows.Average(row => row.Progress);
        int active = rows.Count(row => row.Status == TransferPresentationStatus.Active);
        int failed = rows.Count(row => row.Status is TransferPresentationStatus.Failed or
            TransferPresentationStatus.TimedOut or TransferPresentationStatus.Denied or
            TransferPresentationStatus.Offline or TransferPresentationStatus.Aborted);
        int completed = rows.Count(row => row.Status == TransferPresentationStatus.Completed);
        string status = active > 0
            ? StringResources.Format("IosUiTransferFolderActive", active, rows.Count)
            : failed > 0
                ? StringResources.Format("IosUiTransferFolderFailed", failed, rows.Count)
                : completed == rows.Count
                    ? StringResources.Get("completed")
                    : StringResources.Format("IosUiTransferFolderWaiting", rows.Count - completed, rows.Count);
        string title = FeatureValueFormatter.FileName(key.Folder);
        if (title.Length == 0)
        {
            title = key.Peer;
        }

        return new TransferFolderRow(
            StableFolderId(key),
            title,
            key.Peer,
            status,
            StringResources.Format("IosUiTransferFolderMetadata", rows.Count, totalSize > 0
                ? FeatureValueFormatter.Bytes(totalSize)
                : StringResources.Get("unknown")),
            progress,
            rows);
    }

    private static TransferPresentationStatus MapStatus(TransferSnapshot snapshot)
    {
        TransferStates state = snapshot.State;
        if (state.HasFlag(TransferStates.Succeeded)) return TransferPresentationStatus.Completed;
        if (state.HasFlag(TransferStates.Completed) && state.HasFlag(TransferStates.Cancelled))
            return TransferPresentationStatus.Canceled;
        if (state.HasFlag(TransferStates.UserOffline)) return TransferPresentationStatus.Offline;
        if (state.HasFlag(TransferStates.TimedOut)) return TransferPresentationStatus.TimedOut;
        if (state.HasFlag(TransferStates.Rejected)) return TransferPresentationStatus.Denied;
        if (state.HasFlag(TransferStates.Aborted)) return TransferPresentationStatus.Aborted;
        if (state.HasFlag(TransferStates.Errored) || state.HasFlag(TransferStates.CannotConnect) || state.HasFlag(TransferStates.SizeMismatch))
            return TransferPresentationStatus.Failed;
        if (snapshot.IsProcessing && state.HasFlag(TransferStates.InProgress)) return TransferPresentationStatus.Active;
        if (state.HasFlag(TransferStates.Queued)) return TransferPresentationStatus.Queued;
        if (state.HasFlag(TransferStates.Cancelled))
        {
            return snapshot.Identity.Direction == TransferDirection.Upload
                ? TransferPresentationStatus.WaitingForPeer
                : TransferPresentationStatus.Paused;
        }

        if (state.HasFlag(TransferStates.Completed)) return TransferPresentationStatus.Canceled;
        if (state.HasFlag(TransferStates.Requested) || state.HasFlag(TransferStates.Initializing))
            return TransferPresentationStatus.NotStarted;
        return TransferPresentationStatus.NotStarted;
    }

    private static string StatusText(TransferPresentationStatus status) => status switch
    {
        TransferPresentationStatus.Queued => StringResources.Get("in_queue"),
        TransferPresentationStatus.Active => StringResources.Get("in_progress"),
        TransferPresentationStatus.Paused => StringResources.Get("paused"),
        TransferPresentationStatus.WaitingForPeer => StringResources.Get("IosUiUploadWaitingForPeer"),
        TransferPresentationStatus.Failed => StringResources.Get("failed"),
        TransferPresentationStatus.TimedOut => StringResources.Get("TimedOut"),
        TransferPresentationStatus.Denied => StringResources.Get("failed_denied"),
        TransferPresentationStatus.Offline => StringResources.Get("failed_user_offline"),
        TransferPresentationStatus.Aborted => StringResources.Get("Aborted"),
        TransferPresentationStatus.Canceled => StringResources.Get("Cancelled"),
        TransferPresentationStatus.Completed => StringResources.Get("completed"),
        _ => StringResources.Get("not_started"),
    };

    private static int StatusOrder(TransferPresentationStatus status) => status switch
    {
        TransferPresentationStatus.Active => 0,
        TransferPresentationStatus.Queued => 1,
        TransferPresentationStatus.NotStarted => 2,
        TransferPresentationStatus.Paused or TransferPresentationStatus.WaitingForPeer => 3,
        TransferPresentationStatus.Failed or TransferPresentationStatus.TimedOut or
            TransferPresentationStatus.Denied or TransferPresentationStatus.Offline or
            TransferPresentationStatus.Aborted => 4,
        TransferPresentationStatus.Completed => 5,
        _ => 6,
    };

    private static TransferDirection DirectionFor(TransferMode mode) => mode == TransferMode.Downloads
        ? TransferDirection.Download
        : TransferDirection.Upload;

    private static bool CanExecute(TransferRow row, TransferCommand command) => command switch
    {
        TransferCommand.Retry => row.Capabilities.CanRetry,
        TransferCommand.Pause => row.Capabilities.CanPause,
        TransferCommand.Clear => row.Capabilities.CanClear,
        _ => false,
    };

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + Math.Max(0, right);

    private static string StableFolderId(FolderKey key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{key.Peer.Length}:{key.Peer}{key.Folder.Length}:{key.Folder}"));
        return $"folder-{Convert.ToHexString(digest.AsSpan(0, 10)).ToLowerInvariant()}";
    }

    private sealed record FolderKey(string Peer, string Folder);
}
