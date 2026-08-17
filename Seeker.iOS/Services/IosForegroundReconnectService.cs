using Seeker.Helpers;
using Seeker.Services;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Applies iOS scene and Network-framework conditions to the portable stepped-backoff reconnect policy.
/// </summary>
internal sealed class IosForegroundReconnectService : IDisposable
{
    private readonly Lock sync = new();
    private readonly AppSession session;
    private readonly IosNetworkStatus networkStatus;
    private readonly ILoggerBackend logger;
    private readonly ForegroundReconnectCoordinator coordinator;
    private bool automaticAttemptInProgress;
    private bool sceneActive;
    private bool disposed;

    /// <summary>Creates and subscribes the process-wide foreground reconnect service.</summary>
    /// <param name="session">The serialized Soulseek application session.</param>
    /// <param name="networkStatus">The active Network-framework reachability monitor.</param>
    /// <param name="logger">Receives individual retry failures.</param>
    public IosForegroundReconnectService(
        AppSession session,
        IosNetworkStatus networkStatus,
        ILoggerBackend logger)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.networkStatus = networkStatus ?? throw new ArgumentNullException(nameof(networkStatus));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        coordinator = new ForegroundReconnectCoordinator(
            CanReconnect,
            ReconnectAutomaticallyAsync,
            attemptFailed: OnAttemptFailed);

        session.UnexpectedlyDisconnected += OnUnexpectedlyDisconnected;
        session.AutomaticReconnectSuppressed += OnAutomaticReconnectSuppressed;
        session.AuthenticationAttemptCompleted += OnAuthenticationAttemptCompleted;
        networkStatus.StatusChanged += OnNetworkStatusChanged;
    }

    /// <summary>
    /// Enables automatic recovery for the active scene and requests an initial reconnect for a persisted session.
    /// </summary>
    public void SceneBecameActive()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            sceneActive = true;
        }

        session.AllowForegroundReconnect();
        if (session.CanAutomaticallyReconnect && !session.IsConnected)
        {
            coordinator.RequestReconnect();
        }
    }

    /// <summary>Cancels and forgets automatic recovery before the scene leaves active use.</summary>
    public void SceneWillResignActive()
    {
        lock (sync)
        {
            sceneActive = false;
        }

        coordinator.Cancel();
        session.CancelPendingForegroundReconnect();
    }

    /// <summary>Unsubscribes lifecycle inputs and cancels the active reconnect loop.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            sceneActive = false;
        }

        session.UnexpectedlyDisconnected -= OnUnexpectedlyDisconnected;
        session.AutomaticReconnectSuppressed -= OnAutomaticReconnectSuppressed;
        session.AuthenticationAttemptCompleted -= OnAuthenticationAttemptCompleted;
        networkStatus.StatusChanged -= OnNetworkStatusChanged;
        coordinator.Dispose();
        session.CancelPendingForegroundReconnect();
    }

    private bool CanReconnect()
    {
        lock (sync)
        {
            return !disposed &&
                   sceneActive &&
                   networkStatus.DoWeHaveInternet() &&
                   session.CanAutomaticallyReconnect &&
                   (automaticAttemptInProgress || !session.IsAuthenticationInProgress) &&
                   !session.IsConnected;
        }
    }

    private async Task ReconnectAutomaticallyAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            automaticAttemptInProgress = true;
        }

        try
        {
            await session.ReconnectAutomaticallyAsync(cancellationToken);
        }
        finally
        {
            lock (sync)
            {
                automaticAttemptInProgress = false;
            }
        }
    }

    private void OnUnexpectedlyDisconnected(object? sender, EventArgs args) => coordinator.RequestReconnect();

    private void OnAutomaticReconnectSuppressed(object? sender, EventArgs args) => coordinator.Cancel();

    private void OnAuthenticationAttemptCompleted(object? sender, EventArgs args)
    {
        if (session.IsConnected)
        {
            coordinator.Cancel();
        }
        else
        {
            coordinator.NotifyConditionsChanged();
        }
    }

    private void OnNetworkStatusChanged(object? sender, EventArgs args)
    {
        // Once the protocol reports authenticated, let LoginCoreAsync finish persisting credentials before its
        // completion callback ends the loop. A genuine path loss still cancels that narrow post-connect window.
        if (networkStatus.DoWeHaveInternet() && session.IsConnected)
        {
            return;
        }

        coordinator.NotifyConditionsChanged();
    }

    private void OnAttemptFailed(Exception exception, int attempt)
    {
        logger.FirebaseError($"Foreground reconnect attempt {attempt} failed.", exception);
    }
}
