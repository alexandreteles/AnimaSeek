using BackgroundTasks;
using Common;
using Foundation;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Seeker.Transfers;
using Soulseek;
using UIKit;

namespace AnimaSeek.iOS;

// The project is iOS 26-only. The binding annotates continued-processing members as unavailable to Mac Catalyst,
// and CA1416 otherwise treats this iOS-only call site as though it were also reachable from that unsupported platform.
#pragma warning disable CA1416

/// <summary>
/// Maps foreground-initiated Soulseek transfers onto iOS 26 continued-processing tasks and checkpoints interruptions.
/// </summary>
/// <remarks>
/// Only transfers in <see cref="TransferStates.InProgress"/> with recently advancing byte counts qualify. Remote queues,
/// local queues, and stalled transfers are paused instead of consuming a background execution grant.
/// </remarks>
internal sealed class IosBackgroundTaskCoordinator : IDisposable
{
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SubmissionRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SuspensionGrace = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SuspensionSafetyMargin = TimeSpan.FromSeconds(5);

    private readonly Lock sync = new();
    private readonly AppSession session;
    private readonly IosInterruptedDownloadStore interruptedDownloads;
    private readonly Action checkpointTransfers;
    private readonly Task stateRestoration;
    private readonly SemaphoreSlim resumePassGate = new(1, 1);
    private readonly IToaster toaster;
    private readonly ILoggerBackend logger;
    private readonly Timer monitorTimer;
    private readonly Dictionary<TransferKey, long> observedBytes = [];
    private readonly HashSet<TransferKey> userInitiatedTransfers = [];
    private readonly HashSet<TransferFileIdentity> userInitiatedFiles = [];
    private readonly HashSet<TransferKey> continuedTransferBatch = [];
    private readonly HashSet<TransferFileIdentity> continuedTransferFiles = [];
    private readonly Dictionary<TransferFileIdentity, DurableDownloadTerminalOutcome> durableDownloadTerminals = [];
    private BGContinuedProcessingTask? activeTask;
    private Func<CancellationToken, Task<bool>>? wishlistRefreshOperation;
    private Func<TimeSpan?>? wishlistRefreshInterval;
    private string? scheduledIdentifier;
    private bool acceptedTransferRequest;
    private bool transferSubmissionInFlight;
    private DateTimeOffset lastProgressAt;
    private DateTimeOffset submissionRetryAt;
    private bool announcedUnavailability;
    private nint suspensionAssertion = UIApplication.BackgroundTaskInvalid;
    private Timer? suspensionTimer;
    private bool suspensionPending;
    private bool foreground;
    private bool disposed;

    /// <summary>Creates and attaches the process-wide iOS background task coordinator.</summary>
    /// <param name="session">The session whose transfer events supply real progress.</param>
    /// <param name="interruptedDownloads">The durable store shared with foreground download initiation.</param>
    /// <param name="checkpointTransfers">Persists the portable transfer managers immediately.</param>
    /// <param name="stateRestoration">Completes after deferred transfer restoration and recovery reconciliation.</param>
    /// <param name="toaster">Reports a refused background-execution grant to the person using the app.</param>
    /// <param name="logger">Receives scheduling and expiration diagnostics.</param>
    public IosBackgroundTaskCoordinator(
        AppSession session,
        IosInterruptedDownloadStore interruptedDownloads,
        Action checkpointTransfers,
        Task stateRestoration,
        IToaster toaster,
        ILoggerBackend logger)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.interruptedDownloads = interruptedDownloads ?? throw new ArgumentNullException(nameof(interruptedDownloads));
        this.checkpointTransfers = checkpointTransfers ?? throw new ArgumentNullException(nameof(checkpointTransfers));
        this.stateRestoration = stateRestoration ?? throw new ArgumentNullException(nameof(stateRestoration));
        this.toaster = toaster ?? throw new ArgumentNullException(nameof(toaster));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        session.TransfersChanged += OnTransfersChanged;
        session.UserInitiatedTransferRequested += OnUserInitiatedTransferRequested;
        session.UserInitiatedTransferFinished += OnUserInitiatedTransferFinished;
        DownloadService.Instance.DownloadTerminalProcessed += OnDownloadTerminalProcessed;
        DownloadService.Instance.DownloadExpectedSizeChanged += OnDownloadExpectedSizeChanged;
        monitorTimer = new Timer(OnMonitorTick, null, MonitorInterval, MonitorInterval);
        BackgroundTaskRegistrar.Attach(this);

        if (!BackgroundTaskRegistrar.WishlistRegistrationSucceeded)
        {
            logger.Firebase("iOS rejected the wishlist background-refresh registration; foreground wishlist checks remain available.");
        }

        if (BackgroundTaskRegistrar.RegisteredTransferIdentifierCount == 0)
        {
            TransferAvailability = ContinuedTransferAvailability.Unavailable;
            logger.Firebase("iOS registered no continued-processing identifier; transfers stay foreground-only.");
        }
    }

    /// <summary>Gets the most recent outcome of asking iOS to continue transfers after backgrounding.</summary>
    public ContinuedTransferAvailability TransferAvailability { get; private set; }

    /// <summary>Marks the scene active so newly progressing user transfers may submit continued-processing requests.</summary>
    public void SceneBecameActive()
    {
        // Reaching the foreground before the grace window closes means the session was never torn down, so the
        // pending suspension is abandoned rather than completed.
        CancelDeferredSuspension();
        lock (sync)
        {
            if (!disposed)
            {
                foreground = true;
            }
        }

        ReconcileTransfers(allowSubmission: true);
    }

    /// <summary>Marks the scene inactive, preventing new continued-processing submissions until it becomes active.</summary>
    public void SceneWillResignActive()
    {
        lock (sync)
        {
            foreground = false;
            userInitiatedFiles.Clear();
        }
    }

    /// <summary>
    /// Pauses queued work when the scene backgrounds and preserves only recently progressing transfers with a grant.
    /// </summary>
    /// <remarks>
    /// Without a grant the session is not torn down here. Leaving an app for a moment — opening Files, answering a
    /// message — must not cost a reconnect, so suspension is deferred behind a short background assertion and
    /// cancelled outright if the scene comes back first.
    /// </remarks>
    public void SceneEnteredBackground()
    {
        Transfer[] continuingTransfers;
        bool canContinue;
        bool shouldFinish;
        bool completionSucceeded;

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            foreground = false;
            Transfer[] logicalBatchTransfers = GetLatestLogicalTransfers(
                session.GetTransferReconciliationSnapshot(),
                continuedTransferFiles);
            ContinuedTransferMemberState[] logicalStates = logicalBatchTransfers
                .Select(GetMemberState)
                .ToArray();
            continuingTransfers = logicalBatchTransfers
                .Where((_, index) => NeedsContinuedExecution(logicalStates[index]))
                .ToArray();
            ContinuedTransferBatchDecision completion = ContinuedTransferBatchCompletionPolicy.Evaluate(
                scheduledIdentifier is not null,
                continuedTransferFiles.Count,
                logicalStates);
            shouldFinish = completion.ShouldFinish;
            completionSucceeded = completion.Succeeded;
            canContinue = ContinuedTransferBackgroundPolicy.CanContinueDuringSubmission(
                continuingTransfers.Length,
                acceptedTransferRequest,
                activeTask is not null,
                transferSubmissionInFlight,
                lastProgressAt,
                DateTimeOffset.UtcNow,
                StallLimit);
        }

        if (canContinue)
        {
            RememberInterruptedDownloads(session.PauseTransfersForSuspension(
                continuingTransfers,
                disconnect: false));
        }

        if (shouldFinish)
        {
            checkpointTransfers();
            FinishTransferTask(completionSucceeded);
        }
        else if (!canContinue)
        {
            // This is the likeliest kill point; manager state must be durable before the grace window opens.
            checkpointTransfers();
            FinishTransferTask(success: false);
        }

        if (!canContinue)
        {
            BeginDeferredSuspension();
        }

        _ = ScheduleWishlistRefresh();
    }

    /// <summary>
    /// Retries only downloads that iOS background expiration paused, leaving user-paused transfers untouched.
    /// </summary>
    /// <remarks>
    /// Resume work is foreground-only: a bounded background connection window (such as a wishlist refresh) must
    /// never start retries it cannot supervise. The pass runs serialized on a worker thread after deferred durable
    /// state restoration completes, so it neither blocks the main thread nor observes partially restored state.
    /// </remarks>
    public void ResumeInterruptedDownloads()
    {
        lock (sync)
        {
            if (disposed || !foreground)
            {
                return;
            }
        }

        _ = Task.Run(ResumeInterruptedDownloadsAsync);
    }

    private async Task ResumeInterruptedDownloadsAsync()
    {
        try
        {
            await stateRestoration.ConfigureAwait(false);
            await resumePassGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (sync)
                {
                    if (disposed || !foreground)
                    {
                        return;
                    }
                }

                ResumeInterruptedDownloadsCore();
            }
            finally
            {
                resumePassGate.Release();
            }
        }
        catch (Exception exception)
        {
            logger.FirebaseError("The interrupted-download resume pass failed.", exception);
        }
    }

    private void ResumeInterruptedDownloadsCore()
    {
        session.ReconcileInterruptedDownloadFinalizations();
        if (!session.IsConnected)
        {
            return;
        }

        HashSet<string> encoded = interruptedDownloads.ReadEncoded();

        if (encoded.Count == 0)
        {
            return;
        }

        var remaining = new HashSet<string>(encoded, StringComparer.Ordinal);
        var resumable = new List<SuspendedDownload>();
        foreach (string entry in encoded)
        {
            if (!IosInterruptedDownloadStore.TryDecode(
                entry,
                out SuspendedDownload interrupted,
                out bool containsLegacyAbsolutePath) ||
                !session.TryNormalizeInterruptedDownload(
                    interrupted,
                    containsLegacyAbsolutePath,
                    out SuspendedDownload normalized))
            {
                remaining.Remove(entry);
                continue;
            }

            interrupted = normalized;
            if (containsLegacyAbsolutePath)
            {
                remaining.Remove(entry);
                remaining.Add(IosInterruptedDownloadStore.Encode(interrupted));
            }

            resumable.Add(interrupted);
        }

        // Commit validation and legacy-path migration before starting work. A synchronous completion below can then
        // forget its descriptor without this resume pass accidentally adding the stale snapshot back afterward.
        interruptedDownloads.Reconcile(encoded, remaining);

        foreach (SuspendedDownload interrupted in resumable)
        {
            try
            {
                // Keep the descriptor while the retry is active. Its successful file-finalization event (or an
                // explicit cancel-and-clear) is the only boundary that removes crash-resume state.
                _ = session.ResumeInterruptedDownload(interrupted);
            }
            catch (Exception exception)
            {
                logger.FirebaseError($"Resuming interrupted download '{interrupted.Filename}' failed.", exception);
            }
        }
    }

    /// <summary>
    /// Supplies the future wishlist controller's bounded connect-search-disconnect operation.
    /// </summary>
    /// <param name="operation">
    /// A cancellable operation returning whether the refresh succeeded, or <see langword="null"/> to disable scheduling.
    /// </param>
    /// <param name="intervalProvider">
    /// Returns the latest server-authorized cadence, or <see langword="null"/> while wishlist refresh is unavailable.
    /// </param>
    public void SetWishlistRefreshOperation(
        Func<CancellationToken, Task<bool>>? operation,
        Func<TimeSpan?>? intervalProvider = null)
    {
        lock (sync)
        {
            wishlistRefreshOperation = operation;
            wishlistRefreshInterval = intervalProvider;
        }
    }

    /// <summary>
    /// Arms a transfer that an iOS controller is about to start through a service that does not expose its token.
    /// </summary>
    /// <param name="direction">Whether the user action will start a download or upload.</param>
    /// <param name="username">The remote Soulseek user.</param>
    /// <param name="filename">The exact remote filename passed to Soulseek.NET.</param>
    /// <returns>
    /// <see langword="true"/> when the foreground action was armed; <see langword="false"/> when the scene was not active.
    /// </returns>
    /// <remarks>
    /// Call this immediately before the explicit enqueue/start operation. Restored and automatically resumed work must
    /// not call it, because iOS continued processing is reserved for current foreground user intent.
    /// </remarks>
    public bool ArmUserInitiatedTransfer(
        TransferDirection direction,
        string username,
        string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        lock (sync)
        {
            if (disposed || !foreground)
            {
                return false;
            }

            var identity = new TransferFileIdentity(direction, username, filename);
            durableDownloadTerminals.Remove(identity);
            userInitiatedFiles.Add(identity);
            return true;
        }
    }

    /// <summary>
    /// Asks iOS for a best-effort wishlist refresh only when a real refresh operation has been supplied.
    /// </summary>
    /// <returns><see langword="true"/> when iOS accepted the request.</returns>
    public bool ScheduleWishlistRefresh()
    {
        TimeSpan delay;
        lock (sync)
        {
            if (disposed || wishlistRefreshOperation is null || !BackgroundTaskRegistrar.WishlistRegistrationSucceeded)
            {
                return false;
            }

            if (wishlistRefreshInterval?.Invoke() is not { } serverDelay)
            {
                return false;
            }

            delay = serverDelay;
        }

        var request = new BGAppRefreshTaskRequest(BackgroundTaskRegistrar.WishlistIdentifier)
        {
            EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(delay.TotalSeconds),
        };

        bool submitted = BGTaskScheduler.Shared.Submit(request, out NSError? error);
        if (!submitted)
        {
            logger.Firebase($"iOS declined the wishlist refresh request: {error?.LocalizedDescription ?? "unknown scheduler error"}");
        }

        return submitted;
    }

    /// <summary>Releases event subscriptions and completes any task still owned by this process.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreground = false;
        }

        CancelDeferredSuspension();
        session.TransfersChanged -= OnTransfersChanged;
        session.UserInitiatedTransferRequested -= OnUserInitiatedTransferRequested;
        session.UserInitiatedTransferFinished -= OnUserInitiatedTransferFinished;
        DownloadService.Instance.DownloadTerminalProcessed -= OnDownloadTerminalProcessed;
        DownloadService.Instance.DownloadExpectedSizeChanged -= OnDownloadExpectedSizeChanged;
        monitorTimer.Dispose();
        BackgroundTaskRegistrar.Detach(this);
        FinishTransferTask(success: false);
    }

    /// <summary>Accepts the concrete continued-processing task launched for a previously submitted transfer batch.</summary>
    /// <param name="identifier">The concrete identifier registered with iOS for this batch.</param>
    /// <param name="task">The system-owned task and progress object.</param>
    internal void HandleTransferTask(string identifier, BGContinuedProcessingTask task)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(task);

        lock (sync)
        {
            if (disposed || !string.Equals(scheduledIdentifier, identifier, StringComparison.Ordinal))
            {
                task.SetTaskCompleted(false);
                return;
            }

            activeTask = task;
            acceptedTransferRequest = true;
            transferSubmissionInFlight = false;
            task.ExpirationHandler = () => ExpireTransferTask(identifier);
            task.Progress.Cancellable = true;
            task.Progress.Pausable = false;
            task.Progress.SetCancellationHandler(() => CancelTransferTask(identifier));
        }

        ReconcileTransfers(allowSubmission: false);
    }

    /// <summary>Runs the registered wishlist operation inside an opportunistic <see cref="BGAppRefreshTask"/> window.</summary>
    /// <param name="task">The system-owned short refresh task.</param>
    internal void HandleWishlistTask(BGAppRefreshTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        Func<CancellationToken, Task<bool>>? operation;

        lock (sync)
        {
            operation = disposed ? null : wishlistRefreshOperation;
        }

        if (operation is null)
        {
            task.SetTaskCompleted(false);
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        int completionClaimed = 0;
        task.ExpirationHandler = () =>
        {
            cancellationSource.Cancel();
            if (Interlocked.Exchange(ref completionClaimed, 1) == 0)
            {
                task.ExpirationHandler = null;
                task.SetTaskCompleted(false);
            }
        };

        _ = RunWishlistRefreshAsync(task, operation, cancellationSource, () =>
            Interlocked.Exchange(ref completionClaimed, 1) == 0);
    }

    /// <summary>
    /// Gets whether this process runs on the simulator, where iOS refuses every background-task submission.
    /// </summary>
    private static bool RunningInSimulator =>
        Environment.GetEnvironmentVariable("SIMULATOR_UDID") is not null;

    private static Transfer[] GetActiveTransfers(IReadOnlyList<Transfer> transfers) => transfers
        .Where(transfer =>
            transfer.State.HasFlag(TransferStates.InProgress) &&
            !transfer.State.HasFlag(TransferStates.Completed))
        .ToArray();

    private static TransferKey ToKey(Transfer transfer) => new(transfer.Direction, transfer.Token);

    private static string DescribeDirection(IReadOnlyList<Transfer> transfers) =>
        transfers.All(transfer => transfer.Direction == TransferDirection.Download)
            ? StringResources.Get("IosBackgroundDownloading")
            : transfers.All(transfer => transfer.Direction == TransferDirection.Upload)
                ? StringResources.Get("IosBackgroundUploading")
                : StringResources.Get("IosBackgroundTransferring");

    private static string BuildTitle(IReadOnlyList<Transfer> transfers) =>
        StringResources.Format(
            transfers.Count == 1 ? "IosBackgroundTransferTitleSingle" : "IosBackgroundTransferTitlePlural",
            DescribeDirection(transfers),
            transfers.Count);

    private static string BuildSubtitle(IReadOnlyList<Transfer> transfers)
    {
        if (transfers.Count == 1)
        {
            return Path.GetFileName(transfers[0].Filename.Replace('\\', '/'));
        }

        (long completed, long total) = GetAggregateProgress(transfers);
        return StringResources.Format(
            transfers.Count == 1 ? "IosBackgroundTransferProgressSingle" : "IosBackgroundTransferProgressPlural",
            Math.Clamp(completed * 100d / total, 0, 100),
            transfers.Count);
    }

    private static (long Completed, long Total) GetAggregateProgress(IReadOnlyList<Transfer> transfers)
    {
        long total = 0;
        long completed = 0;
        foreach (Transfer transfer in transfers)
        {
            long size = Math.Max(1, transfer.Size);
            total = SaturatingAdd(total, size);
            completed = SaturatingAdd(completed, Math.Clamp(transfer.BytesTransferred, 0, size));
        }

        return (completed, Math.Max(1, total));
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private void OnTransfersChanged(object? sender, EventArgs args) => ReconcileTransfers(allowSubmission: true);

    private void OnDownloadTerminalProcessed(object? sender, DownloadTerminalProcessedEventArgs args)
    {
        DurableDownloadTerminalOutcome outcome;
        try
        {
            outcome = session.CommitTerminalDownload(args);
        }
        catch (Exception exception)
        {
            logger.FirebaseError(
                $"Clearing terminal interrupted-download state for '{args.TransferItem.FullFilename}' failed.",
                exception);
            return;
        }

        if (outcome is DurableDownloadTerminalOutcome.NotDurable)
        {
            return;
        }

        bool belongsToActiveBatch;
        var identity = new TransferFileIdentity(
            TransferDirection.Download,
            args.TransferItem.Username,
            args.TransferItem.FullFilename);
        lock (sync)
        {
            belongsToActiveBatch = continuedTransferFiles.Contains(identity);
            if (belongsToActiveBatch)
            {
                durableDownloadTerminals[identity] = outcome;
            }
        }

        if (belongsToActiveBatch)
        {
            ReconcileTransfers(allowSubmission: false);
        }
    }

    private void OnDownloadExpectedSizeChanged(object? sender, DownloadExpectedSizeChangedEventArgs args) =>
        session.RefreshInterruptedDownloadExpectedSize(args.TransferItem, args.ExpectedSize);

    private void OnUserInitiatedTransferRequested(object? sender, UserInitiatedTransferEventArgs args)
    {
        lock (sync)
        {
            if (!disposed && foreground)
            {
                userInitiatedTransfers.Add(new TransferKey(args.Direction, args.Token));
            }
        }

        ReconcileTransfers(allowSubmission: false);
    }

    private void OnUserInitiatedTransferFinished(object? sender, UserInitiatedTransferEventArgs args)
    {
        lock (sync)
        {
            userInitiatedTransfers.Remove(new TransferKey(args.Direction, args.Token));
        }

        ReconcileTransfers(allowSubmission: false);
    }

    private static Transfer[] GetLatestLogicalTransfers(
        IReadOnlyList<Transfer> transfers,
        IReadOnlySet<TransferFileIdentity> identities) =>
        transfers
            .Where(transfer => identities.Contains(ToFileIdentity(transfer)))
            .GroupBy(ToFileIdentity)
            .Select(group => group
                .OrderByDescending(transfer => !transfer.State.HasFlag(TransferStates.Completed))
                .ThenByDescending(transfer => transfer.StartTime ?? DateTime.MinValue)
                .ThenByDescending(transfer => transfer.EndTime ?? DateTime.MinValue)
                .ThenByDescending(transfer => transfer.Token)
                .First())
            .ToArray();

    private ContinuedTransferMemberState GetMemberState(Transfer transfer)
    {
        TransferFileIdentity identity = ToFileIdentity(transfer);
        if (identity.Direction == TransferDirection.Download)
        {
            if (durableDownloadTerminals.TryGetValue(identity, out DurableDownloadTerminalOutcome outcome))
            {
                return outcome is DurableDownloadTerminalOutcome.Succeeded
                    ? ContinuedTransferMemberState.DownloadSucceeded
                    : ContinuedTransferMemberState.DownloadUnsuccessful;
            }

            return transfer.State.HasFlag(TransferStates.Completed)
                ? ContinuedTransferMemberState.DownloadAwaitingDurableTerminal
                : ContinuedTransferMemberState.NetworkActive;
        }

        if (!transfer.State.HasFlag(TransferStates.Completed))
        {
            return ContinuedTransferMemberState.NetworkActive;
        }

        return transfer.State.HasFlag(TransferStates.Succeeded)
            ? ContinuedTransferMemberState.UploadSucceeded
            : ContinuedTransferMemberState.UploadUnsuccessful;
    }

    private static bool NeedsContinuedExecution(ContinuedTransferMemberState state) => state is
        ContinuedTransferMemberState.NetworkActive or
        ContinuedTransferMemberState.DownloadAwaitingDurableTerminal;

    private void ReconcileTransfers(bool allowSubmission)
    {
        IReadOnlyList<Transfer> transfers = session.GetTransferReconciliationSnapshot();
        Transfer[] activeTransfers = GetActiveTransfers(transfers);
        Transfer[] monitoredTransfers;
        Transfer[] progressTransfers;
        string? identifierToSubmit = null;
        bool identifierPoolExhausted = false;
        BGContinuedProcessingTask? taskToUpdate;
        bool shouldFinish;
        bool completionSucceeded;

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool advanced = false;
            Transfer[] explicitlyStarted = activeTransfers
                .Where(transfer =>
                    userInitiatedTransfers.Contains(ToKey(transfer)) ||
                    userInitiatedFiles.Contains(ToFileIdentity(transfer)))
                .ToArray();
            foreach (Transfer transfer in explicitlyStarted)
            {
                userInitiatedFiles.Remove(ToFileIdentity(transfer));
            }

            if (scheduledIdentifier is not null)
            {
                continuedTransferFiles.UnionWith(explicitlyStarted.Select(ToFileIdentity));
            }

            Transfer[] logicalBatchTransfers = scheduledIdentifier is null
                ? explicitlyStarted
                : GetLatestLogicalTransfers(transfers, continuedTransferFiles);
            if (scheduledIdentifier is not null)
            {
                continuedTransferBatch.UnionWith(logicalBatchTransfers.Select(ToKey));
            }

            ContinuedTransferMemberState[] logicalStates = logicalBatchTransfers
                .Select(GetMemberState)
                .ToArray();
            monitoredTransfers = logicalBatchTransfers
                .Where((_, index) => NeedsContinuedExecution(logicalStates[index]))
                .ToArray();
            var currentKeys = new HashSet<TransferKey>();
            foreach (Transfer transfer in monitoredTransfers)
            {
                var key = new TransferKey(transfer.Direction, transfer.Token);
                currentKeys.Add(key);
                if (observedBytes.TryGetValue(key, out long previous))
                {
                    advanced |= transfer.BytesTransferred > previous;
                }
                else
                {
                    // Merely observing a newly active transfer is not progress. In particular, the common first
                    // observation at zero bytes must never justify continued background execution.
                    advanced |= transfer.BytesTransferred > 0;
                }

                observedBytes[key] = transfer.BytesTransferred;
            }

            foreach (TransferKey stale in observedBytes.Keys.Where(key => !currentKeys.Contains(key)).ToArray())
            {
                observedBytes.Remove(stale);
            }

            if (advanced)
            {
                lastProgressAt = now;
            }

            bool recentlyProgressing = monitoredTransfers.Length > 0 &&
                lastProgressAt != default &&
                now - lastProgressAt <= StallLimit;
            ContinuedTransferBatchDecision completion = ContinuedTransferBatchCompletionPolicy.Evaluate(
                scheduledIdentifier is not null,
                continuedTransferFiles.Count,
                logicalStates);
            shouldFinish = completion.ShouldFinish;
            completionSucceeded = completion.Succeeded;
            taskToUpdate = activeTask;

            bool hasExplicitUserTransfer = explicitlyStarted.Length > 0;
            if (allowSubmission &&
                foreground &&
                hasExplicitUserTransfer &&
                recentlyProgressing &&
                now >= submissionRetryAt &&
                scheduledIdentifier is null)
            {
                identifierToSubmit = BackgroundTaskRegistrar.ReserveTransferIdentifier();
                if (identifierToSubmit is null)
                {
                    identifierPoolExhausted = true;
                }
                else
                {
                    scheduledIdentifier = identifierToSubmit;
                    acceptedTransferRequest = false;
                    transferSubmissionInFlight = true;
                    continuedTransferBatch.UnionWith(explicitlyStarted.Select(ToKey));
                    continuedTransferFiles.UnionWith(explicitlyStarted.Select(ToFileIdentity));
                    foreach (TransferFileIdentity identity in explicitlyStarted.Select(ToFileIdentity))
                    {
                        durableDownloadTerminals.Remove(identity);
                    }

                    logicalBatchTransfers = explicitlyStarted;
                }
            }

            progressTransfers = scheduledIdentifier is null
                ? []
                : logicalBatchTransfers;
        }

        if (identifierPoolExhausted)
        {
            if (BackgroundTaskRegistrar.RegisteredTransferIdentifierCount == 0)
            {
                logger.Firebase("No continued-processing identifier is registered; transfers stay foreground-only.");
                ReportContinuationUnavailable(ContinuedTransferAvailability.Unavailable);
            }
            else
            {
                logger.Firebase("No pooled continued-processing identifier was free; transfers stay foreground-only.");
            }
        }

        if (taskToUpdate is not null && progressTransfers.Length > 0)
        {
            UpdateSystemProgress(taskToUpdate, progressTransfers);
        }

        if (identifierToSubmit is not null)
        {
            SubmitTransferTask(identifierToSubmit, monitoredTransfers);
        }

        if (shouldFinish)
        {
            checkpointTransfers();
            FinishTransferTask(success: completionSucceeded);
        }
    }

    private void SubmitTransferTask(string identifier, IReadOnlyList<Transfer> activeTransfers)
    {
        var request = new BGContinuedProcessingTaskRequest(
            identifier,
            BuildTitle(activeTransfers),
            BuildSubtitle(activeTransfers))
        {
            Strategy = BGContinuedProcessingTaskRequestSubmissionStrategy.Fail,
            RequiredResources = BGContinuedProcessingTaskRequestResources.Default,
        };

        if (BGTaskScheduler.Shared.Submit(request, out NSError? error))
        {
            bool cancelUnownedRequest;
            lock (sync)
            {
                if (string.Equals(scheduledIdentifier, identifier, StringComparison.Ordinal))
                {
                    acceptedTransferRequest = true;
                    transferSubmissionInFlight = false;
                    cancelUnownedRequest = false;
                }
                else
                {
                    cancelUnownedRequest = true;
                }
            }

            if (cancelUnownedRequest)
            {
                BGTaskScheduler.Shared.Cancel(identifier);
                return;
            }

            lock (sync)
            {
                TransferAvailability = ContinuedTransferAvailability.Available;
            }

            logger.Debug($"Submitted iOS continued-processing task '{identifier}'.");
            return;
        }

        bool pauseAfterRejectedSubmission = ClearFailedSubmission(identifier);
        logger.Firebase($"iOS declined continued-processing task '{identifier}': {error?.LocalizedDescription ?? "unknown scheduler error"}");
        ReportContinuationUnavailable(ContinuedTransferAvailability.Declined);
        if (pauseAfterRejectedSubmission)
        {
            PauseAllForSystemInterruption();
        }
    }

    /// <summary>
    /// Records a refused grant and tells the person once per run why transfers cannot survive backgrounding.
    /// </summary>
    /// <param name="availability">The refusal this run observed.</param>
    /// <remarks>
    /// Without this, a refusal is invisible: transfers simply pause the moment the app leaves the foreground and
    /// nothing on screen explains it. The simulator refuses every <c>BGTaskScheduler</c> submission, so the notice
    /// names that cause explicitly rather than implying the device would behave the same way.
    /// </remarks>
    private void ReportContinuationUnavailable(ContinuedTransferAvailability availability)
    {
        bool announce;
        lock (sync)
        {
            TransferAvailability = availability;
            announce = !announcedUnavailability && !disposed;
            announcedUnavailability = true;
        }

        if (announce)
        {
            toaster.ShowToastLong(StringResources.Get(RunningInSimulator
                ? "IosBackgroundContinuationSimulator"
                : "IosBackgroundContinuationDeclined"));
        }
    }

    /// <summary>Rolls back a rejected submission and schedules the next attempt for the same running transfers.</summary>
    /// <param name="identifier">The identifier whose submission iOS refused.</param>
    /// <returns><see langword="true"/> when the rejection landed after the scene already backgrounded.</returns>
    /// <remarks>
    /// The transfers stay marked as user-initiated: a refusal is frequently transient, and dropping that marking
    /// would disqualify a still-running download from every later attempt in this session. A backoff keeps the
    /// retries off the per-progress-event path.
    /// </remarks>
    private bool ClearFailedSubmission(string identifier)
    {
        bool pauseAfterBackgroundRace = false;
        lock (sync)
        {
            if (string.Equals(scheduledIdentifier, identifier, StringComparison.Ordinal))
            {
                scheduledIdentifier = null;
                acceptedTransferRequest = false;
                transferSubmissionInFlight = false;
                submissionRetryAt = DateTimeOffset.UtcNow + SubmissionRetryDelay;
                continuedTransferBatch.Clear();
                continuedTransferFiles.Clear();
                durableDownloadTerminals.Clear();
                observedBytes.Clear();
                lastProgressAt = default;
                pauseAfterBackgroundRace = !foreground;
                BackgroundTaskRegistrar.ReleaseTransferIdentifier(identifier);
            }
        }

        return pauseAfterBackgroundRace;
    }

    private void UpdateSystemProgress(BGContinuedProcessingTask task, IReadOnlyList<Transfer> transfers)
    {
        (long completed, long total) = GetAggregateProgress(transfers);
        task.Progress.TotalUnitCount = total;
        task.Progress.CompletedUnitCount = completed;
        task.Progress.FileTotalCount = transfers.Count;
        task.Progress.FileCompletedCount = transfers.Count(transfer =>
            transfer.State.HasFlag(TransferStates.Completed));
        task.Progress.Throughput = checked((nint)Math.Clamp(
            transfers.Sum(transfer => Math.Max(0, transfer.AverageSpeed)),
            0,
            nint.MaxValue));
        task.UpdateTitle(BuildTitle(transfers), BuildSubtitle(transfers));
    }

    private void OnMonitorTick(object? state)
    {
        string? stalledIdentifier;
        lock (sync)
        {
            stalledIdentifier = !disposed &&
                !foreground &&
                scheduledIdentifier is not null &&
                observedBytes.Count > 0 &&
                DateTimeOffset.UtcNow - lastProgressAt > StallLimit
                    ? scheduledIdentifier
                    : null;
        }

        if (stalledIdentifier is null || ClaimTransferTask(stalledIdentifier) is not { } claim)
        {
            return;
        }

        logger.Firebase("Pausing stalled transfers instead of extending background execution without progress.");
        try
        {
            PauseAllForSystemInterruption();
        }
        finally
        {
            CompleteTransferTask(claim, success: false);
        }
    }

    private void ExpireTransferTask(string identifier)
    {
        if (ClaimTransferTask(identifier) is not { } claim)
        {
            return;
        }

        logger.Firebase("The iOS continued-processing window expired; transfers were checkpointed for foreground resume.");
        try
        {
            PauseAllForSystemInterruption();
        }
        finally
        {
            CompleteTransferTask(claim, success: false);
        }
    }

    private void CancelTransferTask(string identifier)
    {
        if (ClaimTransferTask(identifier) is not { } claim)
        {
            return;
        }

        try
        {
            RememberInterruptedDownloads(session.PauseTransfersForSuspension([], disconnect: true));
            checkpointTransfers();
        }
        finally
        {
            CompleteTransferTask(claim, success: false);
        }
    }

    /// <summary>
    /// Holds the Soulseek session open for a bounded grace period after backgrounding instead of ending it at once.
    /// </summary>
    /// <remarks>
    /// iOS only keeps a backgrounded process alive while it holds an expiring assertion, so the window is the
    /// smaller of the app's own limit and what iOS reports as remaining, less a margin to finish the disconnect and
    /// the final checkpoint. If iOS refuses the assertion, the session is suspended immediately, exactly as before.
    /// </remarks>
    private void BeginDeferredSuspension()
    {
        lock (sync)
        {
            // Armed before the assertion is requested so every failure path below still suspends exactly once.
            suspensionPending = true;
        }

        UIApplication application = UIApplication.SharedApplication;
        nint assertion = application.BeginBackgroundTask(
            "com.animaseek.app.session-suspension",
            OnSuspensionAssertionExpiring);
        if (assertion == UIApplication.BackgroundTaskInvalid)
        {
            logger.Firebase("iOS refused the session grace assertion; the session is suspended immediately.");
            SuspendSession();
            return;
        }

        TimeSpan remaining = TimeSpan.FromSeconds(Math.Clamp(
            application.BackgroundTimeRemaining,
            0,
            SuspensionGrace.TotalSeconds + SuspensionSafetyMargin.TotalSeconds));
        TimeSpan grace = remaining - SuspensionSafetyMargin;
        if (grace <= TimeSpan.Zero)
        {
            application.EndBackgroundTask(assertion);
            SuspendSession();
            return;
        }

        Timer? previousTimer;
        lock (sync)
        {
            previousTimer = suspensionTimer;
            suspensionAssertion = assertion;
            suspensionTimer = new Timer(_ => SuspendSession(), null, grace, Timeout.InfiniteTimeSpan);
        }

        previousTimer?.Dispose();
    }

    /// <summary>Abandons a pending suspension because the scene returned before its grace window closed.</summary>
    private void CancelDeferredSuspension()
    {
        nint assertion;
        Timer? timer;
        lock (sync)
        {
            suspensionPending = false;
            assertion = suspensionAssertion;
            suspensionAssertion = UIApplication.BackgroundTaskInvalid;
            timer = suspensionTimer;
            suspensionTimer = null;
        }

        timer?.Dispose();
        if (assertion != UIApplication.BackgroundTaskInvalid)
        {
            UIApplication.SharedApplication.EndBackgroundTask(assertion);
        }
    }

    /// <summary>Ends the session for a real suspension, pausing transfers and disconnecting once.</summary>
    /// <remarks>
    /// Claims the assertion under the lock so the grace timer, the iOS expiration handler, and a refused assertion
    /// cannot each run this work.
    /// </remarks>
    private void SuspendSession()
    {
        nint assertion;
        Timer? timer;
        bool claimed;
        lock (sync)
        {
            assertion = suspensionAssertion;
            timer = suspensionTimer;
            claimed = suspensionPending;
            suspensionPending = false;
            suspensionAssertion = UIApplication.BackgroundTaskInvalid;
            suspensionTimer = null;
        }

        timer?.Dispose();
        try
        {
            if (claimed)
            {
                PauseAllForSystemInterruption();
            }
        }
        finally
        {
            if (assertion != UIApplication.BackgroundTaskInvalid)
            {
                UIApplication.SharedApplication.EndBackgroundTask(assertion);
            }
        }
    }

    private void OnSuspensionAssertionExpiring()
    {
        logger.Firebase("The iOS background grace window expired; the session was suspended for foreground resume.");
        SuspendSession();
    }

    private void PauseAllForSystemInterruption()
    {
        IReadOnlyList<SuspendedDownload> interrupted = session.PauseTransfersForSuspension([], disconnect: true);
        RememberInterruptedDownloads(interrupted);
        checkpointTransfers();
    }

    private void RememberInterruptedDownloads(IReadOnlyList<SuspendedDownload> downloads)
    {
        if (downloads.Count == 0)
        {
            return;
        }

        interruptedDownloads.Remember(downloads);
    }

    private void FinishTransferTask(bool success)
    {
        if (ClaimTransferTask(expectedIdentifier: null) is { } claim)
        {
            CompleteTransferTask(claim, success);
        }
    }

    private TransferTaskClaim? ClaimTransferTask(string? expectedIdentifier)
    {
        BGContinuedProcessingTask? task;
        string? identifier;

        lock (sync)
        {
            task = activeTask;
            identifier = scheduledIdentifier;
            if ((expectedIdentifier is not null &&
                 !string.Equals(identifier, expectedIdentifier, StringComparison.Ordinal)) ||
                (task is null && identifier is null))
            {
                return null;
            }

            activeTask = null;
            scheduledIdentifier = null;
            acceptedTransferRequest = false;
            transferSubmissionInFlight = false;
            userInitiatedTransfers.ExceptWith(continuedTransferBatch);
            observedBytes.Clear();
            continuedTransferBatch.Clear();
            continuedTransferFiles.Clear();
            durableDownloadTerminals.Clear();
            lastProgressAt = default;
        }

        return new TransferTaskClaim(task, identifier);
    }

    private static void CompleteTransferTask(TransferTaskClaim claim, bool success)
    {
        if (claim.Task is not null)
        {
            claim.Task.ExpirationHandler = null;
            claim.Task.SetTaskCompleted(success);
        }
        else if (claim.Identifier is not null)
        {
            BGTaskScheduler.Shared.Cancel(claim.Identifier);
        }

        if (claim.Identifier is not null)
        {
            // Released only after completion or cancellation so a new batch cannot reserve an identifier whose
            // pending request this claim is still responsible for cancelling.
            BackgroundTaskRegistrar.ReleaseTransferIdentifier(claim.Identifier);
        }
    }

    private async Task RunWishlistRefreshAsync(
        BGAppRefreshTask task,
        Func<CancellationToken, Task<bool>> operation,
        CancellationTokenSource cancellationSource,
        Func<bool> claimCompletion)
    {
        bool succeeded = false;
        try
        {
            succeeded = await operation(cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            // The expiration handler owns unsuccessful completion.
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Wishlist background refresh failed.", exception);
        }
        finally
        {
            bool ownsCompletion = claimCompletion();
            if (ownsCompletion)
            {
                task.ExpirationHandler = null;
            }

            cancellationSource.Dispose();
            if (ownsCompletion)
            {
                task.SetTaskCompleted(succeeded);
            }

            _ = ScheduleWishlistRefresh();
        }
    }

    private readonly record struct TransferKey(TransferDirection Direction, int Token);

    private sealed record TransferTaskClaim(BGContinuedProcessingTask? Task, string? Identifier);

    private static TransferFileIdentity ToFileIdentity(Transfer transfer) =>
        new(transfer.Direction, transfer.Username, transfer.Filename);

    private readonly record struct TransferFileIdentity(
        TransferDirection Direction,
        string Username,
        string Filename);
}

/// <summary>Describes whether iOS is currently willing to continue this app's transfers after backgrounding.</summary>
internal enum ContinuedTransferAvailability
{
    /// <summary>No foreground transfer has asked for a grant yet.</summary>
    Unknown,

    /// <summary>iOS accepted the most recent continued-processing request.</summary>
    Available,

    /// <summary>iOS refused the most recent continued-processing request.</summary>
    Declined,

    /// <summary>No continued-processing identifier is registered, so no request can be made at all.</summary>
    Unavailable,
}

#pragma warning restore CA1416
