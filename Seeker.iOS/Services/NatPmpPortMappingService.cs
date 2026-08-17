using Common;
using Mono.Nat;
using Seeker.Helpers;

namespace AnimaSeek.iOS.Services;

/// <summary>Describes the current state of the iOS NAT-PMP mapping coordinator.</summary>
internal enum NatPmpPortMappingState
{
    /// <summary>The Soulseek listener or automatic port mapping preference is disabled.</summary>
    Disabled,

    /// <summary>The coordinator is waiting for an authenticated Soulseek session.</summary>
    WaitingForSession,

    /// <summary>The coordinator is waiting for a suitable local Wi-Fi or wired network.</summary>
    WaitingForLocalNetwork,

    /// <summary>The coordinator is sending NAT-PMP gateway discovery datagrams.</summary>
    Discovering,

    /// <summary>A NAT-PMP gateway was found and a TCP mapping is being requested.</summary>
    CreatingMapping,

    /// <summary>The requested Soulseek listener port is mapped.</summary>
    Mapped,

    /// <summary>No usable NAT-PMP mapping is currently available; Soulseek relay paths remain usable.</summary>
    Unavailable,

    /// <summary>Foreground discovery and renewal are paused while the scene is backgrounded.</summary>
    Suspended,
}

/// <summary>
/// Represents an immutable diagnostic snapshot of the iOS NAT-PMP coordinator.
/// </summary>
/// <param name="State">The current coordinator state.</param>
/// <param name="PrivatePort">The local Soulseek listener port, when a mapping has been requested.</param>
/// <param name="PublicPort">The external port returned by the gateway, when a mapping is active.</param>
/// <param name="ExpiresAt">The expected mapping expiration time, when known.</param>
/// <param name="Detail">A concise diagnostic intended for logs or a future settings screen.</param>
internal sealed record NatPmpPortMappingSnapshot(
    NatPmpPortMappingState State,
    int? PrivatePort = null,
    int? PublicPort = null,
    DateTimeOffset? ExpiresAt = null,
    string? Detail = null);

/// <summary>
/// Discovers a local gateway with NAT-PMP unicast and renews a TCP mapping for the Soulseek listener.
/// </summary>
/// <remarks>
/// SSDP/UPnP is deliberately never requested because its multicast discovery requires an Apple-granted
/// entitlement. Failure is non-fatal: unsupported routers leave direct inbound connectivity unavailable while
/// Soulseek's server-assisted connection paths continue to work.
/// </remarks>
internal sealed class NatPmpPortMappingService : IDisposable
{
    private const int RequestedLifetimeSeconds = 7_200;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MappingTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RenewalRetryDelay = TimeSpan.FromMinutes(2);

    private readonly IosNetworkStatus networkStatus;
    private readonly Func<bool> isSessionConnected;
    private readonly ILoggerBackend logger;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Lock stateLock = new();
    private readonly Lock timerLock = new();
    private CancellationTokenSource? attemptCancellation;
    private Timer? mappingTimer;
    private INatDevice? gateway;
    private Mapping? activeMapping;
    private long renewAfterUtcTicks;
    private long retryAfterUtcTicks;
    private NatPmpPortMappingSnapshot snapshot = new(NatPmpPortMappingState.WaitingForSession);
    private volatile bool suspended;
    private volatile bool disposed;

    /// <summary>
    /// Creates a coordinator tied to the current default network path and Soulseek session lifecycle.
    /// </summary>
    /// <param name="networkStatus">The iOS Network-framework path monitor.</param>
    /// <param name="isSessionConnected">Returns whether the Soulseek session is authenticated.</param>
    /// <param name="logger">The application diagnostics backend.</param>
    public NatPmpPortMappingService(
        IosNetworkStatus networkStatus,
        Func<bool> isSessionConnected,
        ILoggerBackend logger)
    {
        this.networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        this.isSessionConnected = isSessionConnected ?? throw new ArgumentNullException(nameof(isSessionConnected));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        networkStatus.StatusChanged += OnNetworkStatusChanged;
    }

    /// <summary>Gets a thread-safe snapshot suitable for diagnostics or a future settings screen.</summary>
    public NatPmpPortMappingSnapshot Snapshot
    {
        get
        {
            lock (stateLock)
            {
                return snapshot;
            }
        }
    }

    /// <summary>Raised on the calling thread whenever the diagnostic snapshot changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Schedules a non-blocking mapping check. Concurrent requests are serialized and redundant checks are no-ops.
    /// </summary>
    public void RefreshIfNeeded() => RunMappingAttempt(forceRenewal: false);

    /// <summary>
    /// Cancels discovery and renewal work while preserving any router lease until it expires naturally.
    /// </summary>
    public void Suspend()
    {
        if (disposed)
        {
            return;
        }

        suspended = true;
        CancelAttempt();
        StopRenewalTimer();
        StopDiscoverySafely();
        Publish(new NatPmpPortMappingSnapshot(
            NatPmpPortMappingState.Suspended,
            activeMapping?.PrivatePort,
            activeMapping?.PublicPort,
            Snapshot.ExpiresAt,
            StringResources.Get("IosUiNatSuspended")));
    }

    /// <summary>Resumes foreground mapping checks after scene activation.</summary>
    public void Resume()
    {
        if (disposed)
        {
            return;
        }

        suspended = false;
        Volatile.Write(ref retryAfterUtcTicks, 0);
        RefreshIfNeeded();
    }

    /// <summary>
    /// Releases event subscriptions, timers, and discovery sockets and cancels queued work.
    /// Existing router leases expire without explicit deletion so shutdown can never block.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        networkStatus.StatusChanged -= OnNetworkStatusChanged;
        lifetimeCancellation.Cancel();
        CancelAttempt();
        StopRenewalTimer();
        StopDiscoverySafely();
    }

    private void RunMappingAttempt(bool forceRenewal)
    {
        if (!disposed)
        {
            _ = EnsureMappedAsync(forceRenewal, lifetimeCancellation.Token);
        }
    }

    private async Task EnsureMappedAsync(bool forceRenewal, CancellationToken cancellationToken)
    {
        try
        {
            await operationGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (!CanAttemptMapping(forceRenewal, out int port))
            {
                return;
            }

            using var currentAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation = currentAttempt;
            if (suspended || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            INatDevice? discoveredGateway = gateway;
            if (discoveredGateway is null)
            {
                discoveredGateway = await DiscoverGatewayAsync(currentAttempt.Token);
                if (discoveredGateway is null)
                {
                    if (suspended || disposed || currentAttempt.IsCancellationRequested)
                    {
                        return;
                    }

                    Publish(new NatPmpPortMappingSnapshot(
                        NatPmpPortMappingState.Unavailable,
                        port,
                        Detail: StringResources.Get("IosUiNatUnavailable")));
                    logger.Debug("NAT-PMP gateway discovery timed out; a bounded foreground retry was scheduled.");
                    ScheduleRetry(RetryBackoff);
                    return;
                }

                gateway = discoveredGateway;
            }

            if (!isSessionConnected() || suspended)
            {
                return;
            }

            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.CreatingMapping,
                port,
                Detail: StringResources.Get("IosUiNatCreating")));

            var requestedMapping = new Mapping(
                Protocol.Tcp,
                port,
                port,
                RequestedLifetimeSeconds,
                "AnimaSeek");
            Task<Mapping> mappingTask = discoveredGateway.CreatePortMapAsync(requestedMapping);
            ObserveLateFault(mappingTask);

            Mapping actualMapping;
            try
            {
                actualMapping = await mappingTask.WaitAsync(MappingTimeout, currentAttempt.Token);
            }
            catch (TimeoutException)
            {
                if (suspended || disposed || currentAttempt.IsCancellationRequested)
                {
                    return;
                }

                gateway = null;
                Publish(new NatPmpPortMappingSnapshot(
                    NatPmpPortMappingState.Unavailable,
                    port,
                    Detail: StringResources.Get("IosUiNatUnavailable")));
                logger.Debug("NAT-PMP mapping timed out; a bounded foreground retry was scheduled.");
                ScheduleRetry(RetryBackoff);
                return;
            }

            if (actualMapping.PrivatePort != port || actualMapping.PublicPort != port)
            {
                if (suspended || disposed || currentAttempt.IsCancellationRequested)
                {
                    return;
                }

                Publish(new NatPmpPortMappingSnapshot(
                    NatPmpPortMappingState.Unavailable,
                    port,
                    actualMapping.PublicPort,
                    Detail: StringResources.Get("IosUiNatUnavailable")));
                logger.Debug(
                    $"NAT-PMP returned {actualMapping.PublicPort}->{actualMapping.PrivatePort}; AnimaSeek requires {port}->{port}.");
                ScheduleRetry(RetryBackoff);
                return;
            }

            if (currentAttempt.IsCancellationRequested || suspended || disposed)
            {
                return;
            }

            int lifetimeSeconds = actualMapping.Lifetime > 0
                ? actualMapping.Lifetime
                : RequestedLifetimeSeconds;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset expiresAt = now + TimeSpan.FromSeconds(lifetimeSeconds);
            activeMapping = actualMapping;
            TimeSpan renewalDelay = TimeSpan.FromSeconds(Math.Max(60, lifetimeSeconds / 2.0));
            Volatile.Write(ref renewAfterUtcTicks, (now + renewalDelay).UtcTicks);
            Volatile.Write(ref retryAfterUtcTicks, 0);
            ScheduleRenewal(renewalDelay);
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.Mapped,
                actualMapping.PrivatePort,
                actualMapping.PublicPort,
                expiresAt,
                StringResources.Format(
                    "IosUiNatMapped",
                    actualMapping.PrivatePort,
                    actualMapping.PublicPort)));
            logger.InfoFirebase(
                $"NAT-PMP mapped TCP {actualMapping.PublicPort}->{actualMapping.PrivatePort} for {lifetimeSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            // Scene suspension and network handoffs are expected cancellation paths.
        }
        catch (MappingException exception)
        {
            if (suspended || disposed || attemptCancellation?.IsCancellationRequested == true)
            {
                return;
            }

            gateway = null;
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.Unavailable,
                PreferencesState.ListenerPort,
                Detail: StringResources.Get("IosUiNatUnavailable")));
            logger.Debug($"NAT-PMP mapping was rejected: {exception.Message}");
            ScheduleRetry(activeMapping is null ? RetryBackoff : RenewalRetryDelay);
        }
        catch (Exception exception)
        {
            if (suspended || disposed || attemptCancellation?.IsCancellationRequested == true)
            {
                return;
            }

            gateway = null;
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.Unavailable,
                PreferencesState.ListenerPort,
                Detail: StringResources.Get("IosUiNatUnavailable")));
            logger.FirebaseError("Unexpected NAT-PMP mapping failure.", exception);
            ScheduleRetry(activeMapping is null ? RetryBackoff : RenewalRetryDelay);
        }
        finally
        {
            attemptCancellation = null;
            operationGate.Release();
        }
    }

    private bool CanAttemptMapping(bool forceRenewal, out int port)
    {
        port = PreferencesState.ListenerPort;
        if (disposed)
        {
            return false;
        }

        if (!PreferencesState.ListenerEnabled || !PreferencesState.ListenerUPnpEnabled || port is < 1 or > 65_535)
        {
            StopRenewalTimer();
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.Disabled,
                port is >= 1 and <= 65_535 ? port : null,
                Detail: StringResources.Get("IosUiNatDisabled")));
            return false;
        }

        if (suspended)
        {
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.Suspended,
                port,
                Detail: StringResources.Get("IosUiNatSuspended")));
            return false;
        }

        if (!isSessionConnected())
        {
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.WaitingForSession,
                port,
                Detail: StringResources.Get("IosUiNatWaitingSession")));
            return false;
        }

        if (!networkStatus.IsLocalNetworkAvailable)
        {
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.WaitingForLocalNetwork,
                port,
                Detail: StringResources.Get("IosUiNatWaitingNetwork")));
            return false;
        }

        long nowUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
        if (!forceRenewal &&
            activeMapping?.PrivatePort == port &&
            activeMapping.PublicPort == port &&
            nowUtcTicks < Volatile.Read(ref renewAfterUtcTicks))
        {
            return false;
        }

        return forceRenewal || nowUtcTicks >= Volatile.Read(ref retryAfterUtcTicks);
    }

    private async Task<INatDevice?> DiscoverGatewayAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<INatDevice>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnDeviceFound(object? sender, DeviceEventArgs args)
        {
            if (args.Device.NatProtocol == NatProtocol.Pmp)
            {
                completion.TrySetResult(args.Device);
            }
        }

        Publish(new NatPmpPortMappingSnapshot(
            NatPmpPortMappingState.Discovering,
            PreferencesState.ListenerPort,
            Detail: StringResources.Get("IosUiNatDiscovering")));
        NatUtility.DeviceFound += OnDeviceFound;
        try
        {
            StopDiscoverySafely();
            NatUtility.StartDiscovery(NatProtocol.Pmp);
            return await completion.Task.WaitAsync(DiscoveryTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            NatUtility.DeviceFound -= OnDeviceFound;
            StopDiscoverySafely();
        }
    }

    private void OnNetworkStatusChanged(object? sender, EventArgs args)
    {
        CancelAttempt();
        StopRenewalTimer();
        gateway = null;
        activeMapping = null;
        Volatile.Write(ref renewAfterUtcTicks, 0);
        Volatile.Write(ref retryAfterUtcTicks, 0);

        if (networkStatus.IsLocalNetworkAvailable)
        {
            RefreshIfNeeded();
        }
        else
        {
            Publish(new NatPmpPortMappingSnapshot(
                NatPmpPortMappingState.WaitingForLocalNetwork,
                PreferencesState.ListenerPort,
                Detail: StringResources.Get("IosUiNatWaitingNetwork")));
        }
    }

    private void ScheduleRenewal(TimeSpan delay)
    {
        ScheduleMappingTimer(delay, forceRenewal: true);
    }

    private void ScheduleRetry(TimeSpan delay)
    {
        Volatile.Write(ref retryAfterUtcTicks, (DateTimeOffset.UtcNow + delay).UtcTicks);
        ScheduleMappingTimer(delay, forceRenewal: activeMapping is not null);
    }

    private void ScheduleMappingTimer(TimeSpan delay, bool forceRenewal)
    {
        lock (timerLock)
        {
            mappingTimer?.Dispose();
            mappingTimer = null;
            if (disposed || suspended)
            {
                return;
            }

            TimeSpan boundedDelay = delay < TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : delay;
            mappingTimer = new Timer(
                _ => RunMappingAttempt(forceRenewal),
                null,
                boundedDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void StopRenewalTimer()
    {
        lock (timerLock)
        {
            mappingTimer?.Dispose();
            mappingTimer = null;
        }
    }

    private void CancelAttempt()
    {
        try
        {
            attemptCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A just-completed attempt can dispose its linked token concurrently.
        }
    }

    private void StopDiscoverySafely()
    {
        try
        {
            NatUtility.StopDiscovery();
        }
        catch (Exception exception)
        {
            logger.Debug($"Stopping NAT-PMP discovery failed harmlessly: {exception.Message}");
        }
    }

    private static void ObserveLateFault(Task<Mapping> mappingTask) =>
        _ = mappingTask.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private void Publish(NatPmpPortMappingSnapshot value)
    {
        bool changed;
        lock (stateLock)
        {
            changed = snapshot != value;
            snapshot = value;
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
