using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Seeker.Services
{
    /// <summary>
    /// Runs one cancellable foreground reconnect loop using the application's stepped-backoff policy.
    /// </summary>
    /// <remarks>
    /// The coordinator contains no platform or Soulseek dependencies. Callers supply both the eligibility predicate
    /// and reconnect operation, notify it when external conditions change, and explicitly cancel it for intentional
    /// lifecycle transitions such as scene backgrounding or logout.
    /// </remarks>
    public sealed class ForegroundReconnectCoordinator : IDisposable
    {
        private static readonly IReadOnlyList<TimeSpan> StandardRetryDelays = new[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
        };

        private readonly object sync = new object();
        private readonly Func<bool> canReconnect;
        private readonly Func<CancellationToken, Task> reconnectAsync;
        private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
        private readonly Action<Exception, int>? attemptFailed;
        private readonly IReadOnlyList<TimeSpan> retryDelays;
        private CancellationTokenSource? runCancellation;
        private CancellationTokenSource? delaySkip;
        private Task currentRun = Task.CompletedTask;
        private bool reconnectRequested;
        private bool disposed;

        /// <summary>
        /// Creates a foreground reconnect coordinator.
        /// </summary>
        /// <param name="canReconnect">
        /// Returns <see langword="true"/> only while lifecycle, credentials, reachability, and authentication state
        /// permit an automatic reconnect attempt.
        /// </param>
        /// <param name="reconnectAsync">
        /// Performs one connection attempt and completes only after authentication succeeds or the attempt fails.
        /// </param>
        /// <param name="retryDelays">
        /// Optional retry delays. The production default is 1, 2, 4, 10, and 20 seconds.
        /// </param>
        /// <param name="delayAsync">
        /// Optional cancellable delay implementation, primarily for deterministic tests.
        /// </param>
        /// <param name="attemptFailed">
        /// Optional observer invoked after a failed attempt with the one-based attempt number. Observer failures are
        /// isolated from the reconnect loop.
        /// </param>
        /// <exception cref="ArgumentNullException">A required delegate is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The retry schedule is empty or contains a non-positive delay.</exception>
        public ForegroundReconnectCoordinator(
            Func<bool> canReconnect,
            Func<CancellationToken, Task> reconnectAsync,
            IReadOnlyList<TimeSpan>? retryDelays = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
            Action<Exception, int>? attemptFailed = null)
        {
            this.canReconnect = canReconnect ?? throw new ArgumentNullException(nameof(canReconnect));
            this.reconnectAsync = reconnectAsync ?? throw new ArgumentNullException(nameof(reconnectAsync));
            this.delayAsync = delayAsync ?? Task.Delay;
            this.attemptFailed = attemptFailed;
            this.retryDelays = (retryDelays ?? StandardRetryDelays).ToArray();

            if (this.retryDelays.Count == 0 || this.retryDelays.Any(delay => delay <= TimeSpan.Zero))
            {
                throw new ArgumentException(
                    "At least one positive reconnect delay is required.",
                    nameof(retryDelays));
            }
        }

        /// <summary>Gets whether a reconnect loop is currently waiting or attempting authentication.</summary>
        public bool IsRunning
        {
            get
            {
                lock (sync)
                {
                    return runCancellation is not null;
                }
            }
        }

        /// <summary>
        /// Gets the currently scheduled loop task, or a completed task when no loop has ever been scheduled.
        /// </summary>
        /// <remarks>The returned snapshot does not include a later loop restarted by a condition change.</remarks>
        public Task CurrentRun
        {
            get
            {
                lock (sync)
                {
                    return currentRun;
                }
            }
        }

        /// <summary>
        /// Records that an unexpected disconnection needs recovery and starts one loop when conditions permit.
        /// Repeated calls never create concurrent loops.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The coordinator has been disposed.</exception>
        public void RequestReconnect()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                reconnectRequested = true;
                StartIfEligibleLocked();
            }
        }

        /// <summary>
        /// Re-evaluates reachability and lifecycle conditions. A disallowed condition cancels the active loop but
        /// retains the request so a later allowed condition can restart from the first delay. An allowed condition
        /// while the active loop is mid-delay skips only the remainder of that delay so the loop re-evaluates and
        /// attempts immediately, retaining its current backoff position (matching Android's wake-on-connectivity).
        /// </summary>
        public void NotifyConditionsChanged()
        {
            CancellationTokenSource? cancellation = null;
            CancellationTokenSource? skip = null;
            lock (sync)
            {
                if (disposed || !reconnectRequested)
                {
                    return;
                }

                if (canReconnect())
                {
                    if (runCancellation is null)
                    {
                        StartIfEligibleLocked();
                    }
                    else
                    {
                        skip = delaySkip;
                    }
                }
                else
                {
                    cancellation = runCancellation;
                }
            }

            CancelIgnoringDisposed(cancellation);
            CancelIgnoringDisposed(skip);
        }

        /// <summary>
        /// Cancels the active loop and forgets the pending request. Use this for intentional logout, kick,
        /// backgrounding, successful external login, and process shutdown.
        /// </summary>
        public void Cancel()
        {
            CancellationTokenSource? cancellation;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                reconnectRequested = false;
                cancellation = runCancellation;
            }

            CancelIgnoringDisposed(cancellation);
        }

        /// <summary>Cancels the active loop and releases coordinator resources.</summary>
        public void Dispose()
        {
            CancellationTokenSource? cancellation;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                reconnectRequested = false;
                cancellation = runCancellation;
            }

            CancelIgnoringDisposed(cancellation);
        }

        private void StartIfEligibleLocked()
        {
            if (runCancellation is not null || !reconnectRequested || !canReconnect())
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            runCancellation = cancellation;
            currentRun = Task.Run(() => RunAsync(cancellation));
        }

        private async Task RunAsync(CancellationTokenSource cancellation)
        {
            try
            {
                for (int index = 0; index < retryDelays.Count; index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!canReconnect())
                    {
                        return;
                    }

                    await DelayWithSkipAsync(retryDelays[index], cancellation).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!canReconnect())
                    {
                        return;
                    }

                    try
                    {
                        await reconnectAsync(cancellation.Token).ConfigureAwait(false);
                        lock (sync)
                        {
                            reconnectRequested = false;
                        }

                        return;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        try
                        {
                            attemptFailed?.Invoke(exception, index + 1);
                        }
                        catch
                        {
                            // Diagnostics must never terminate the recovery policy.
                        }
                    }
                }

                lock (sync)
                {
                    // A completed five-step policy is terminal until a new unexpected disconnect is observed.
                    reconnectRequested = false;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Lifecycle and reachability cancellations are normal control flow.
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(runCancellation, cancellation))
                    {
                        runCancellation = null;
                        if (!disposed && reconnectRequested && canReconnect())
                        {
                            StartIfEligibleLocked();
                        }
                    }
                }

                cancellation.Dispose();
            }
        }

        /// <summary>
        /// Waits one backoff step through a skippable token. <see cref="NotifyConditionsChanged"/> cancels only
        /// this token when conditions become favorable mid-delay, which ends the wait early without cancelling
        /// the loop; whole-run cancellation still flows through the linked token and propagates normally.
        /// </summary>
        private async Task DelayWithSkipAsync(TimeSpan delay, CancellationTokenSource cancellation)
        {
            var skipSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            lock (sync)
            {
                delaySkip = skipSource;
            }

            try
            {
                await delayAsync(delay, skipSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                // Favorable conditions ended the wait early; the loop re-evaluates immediately while
                // keeping its current backoff position.
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(delaySkip, skipSource))
                    {
                        delaySkip = null;
                    }
                }

                skipSource.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ForegroundReconnectCoordinator));
            }
        }

        private static void CancelIgnoringDisposed(CancellationTokenSource? cancellation)
        {
            try
            {
                cancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The loop may have completed between taking the snapshot and issuing cancellation.
            }
        }
    }
}
