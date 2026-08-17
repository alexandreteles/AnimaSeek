using NUnit.Framework;
using Seeker.Services;
using Soulseek;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTestCommon;

[TestFixture]
public sealed class ForegroundReconnectCoordinatorTests
{
    [Test]
    public async Task RequestReconnect_UsesSteppedDelaysAndStopsAfterSuccess()
    {
        var delays = new List<TimeSpan>();
        int attempts = 0;
        using var coordinator = new ForegroundReconnectCoordinator(
            canReconnect: () => true,
            reconnectAsync: _ => ++attempts < 3
                ? Task.FromException(new IOException("offline"))
                : Task.CompletedTask,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        coordinator.RequestReconnect();
        await coordinator.CurrentRun;

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(
                delays,
                Is.EqualTo(new[]
                {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4),
                }));
            Assert.That(coordinator.IsRunning, Is.False);
        });
    }

    [Test]
    public async Task RequestReconnect_ExhaustsFiveAttemptsWithoutConcurrentLoops()
    {
        var observedAttempts = new List<int>();
        var observedDelays = new List<TimeSpan>();
        int attempts = 0;
        using var coordinator = new ForegroundReconnectCoordinator(
            canReconnect: () => true,
            reconnectAsync: _ =>
            {
                attempts++;
                throw new IOException("still offline");
            },
            delayAsync: (delay, _) =>
            {
                observedDelays.Add(delay);
                return Task.CompletedTask;
            },
            attemptFailed: (_, attempt) => observedAttempts.Add(attempt));

        coordinator.RequestReconnect();
        coordinator.RequestReconnect();
        await coordinator.CurrentRun;

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(5));
            Assert.That(observedAttempts, Is.EqualTo(Enumerable.Range(1, 5)));
            Assert.That(
                observedDelays,
                Is.EqualTo(new[]
                {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(20),
                }));
            Assert.That(coordinator.IsRunning, Is.False);
        });
    }

    [Test]
    public async Task ConditionsChanged_CancelsForNetworkLossAndRestartsWhenPermitted()
    {
        bool canReconnect = true;
        var delayStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int delayCalls = 0;
        int attempts = 0;
        using var coordinator = new ForegroundReconnectCoordinator(
            canReconnect: () => Volatile.Read(ref canReconnect),
            reconnectAsync: _ =>
            {
                Interlocked.Increment(ref attempts);
                return Task.CompletedTask;
            },
            delayAsync: async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref delayCalls) == 1)
                {
                    delayStarted.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            });

        coordinator.RequestReconnect();
        await delayStarted.Task;
        Task cancelledRun = coordinator.CurrentRun;
        Volatile.Write(ref canReconnect, false);
        coordinator.NotifyConditionsChanged();
        await cancelledRun;

        Volatile.Write(ref canReconnect, true);
        coordinator.NotifyConditionsChanged();
        await coordinator.CurrentRun;

        Assert.Multiple(() =>
        {
            Assert.That(delayCalls, Is.EqualTo(2));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(coordinator.IsRunning, Is.False);
        });
    }

    [Test]
    public async Task ConditionsChanged_WhileWaiting_SkipsRemainingDelayAndKeepsBackoffPosition()
    {
        var secondDelayStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedDelays = new List<TimeSpan>();
        int attempts = 0;
        using var coordinator = new ForegroundReconnectCoordinator(
            canReconnect: () => true,
            reconnectAsync: _ => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new IOException("offline"))
                : Task.CompletedTask,
            delayAsync: async (delay, cancellationToken) =>
            {
                observedDelays.Add(delay);
                if (observedDelays.Count == 2)
                {
                    secondDelayStarted.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            });

        coordinator.RequestReconnect();
        await secondDelayStarted.Task;
        Task run = coordinator.CurrentRun;
        coordinator.NotifyConditionsChanged();
        await run;

        Assert.Multiple(() =>
        {
            // Connectivity returning mid-delay ends only the wait: the second attempt runs immediately
            // at its original backoff position instead of sleeping out the full delay or restarting.
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(
                observedDelays,
                Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
            Assert.That(coordinator.IsRunning, Is.False);
        });
    }

    [Test]
    public async Task Cancel_ForgetsPendingReconnectAcrossLaterConditionChanges()
    {
        var delayStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        using var coordinator = new ForegroundReconnectCoordinator(
            canReconnect: () => true,
            reconnectAsync: _ =>
            {
                Interlocked.Increment(ref attempts);
                return Task.CompletedTask;
            },
            delayAsync: async (_, cancellationToken) =>
            {
                delayStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            });

        coordinator.RequestReconnect();
        await delayStarted.Task;
        Task cancelledRun = coordinator.CurrentRun;
        coordinator.Cancel();
        await cancelledRun;
        coordinator.NotifyConditionsChanged();

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.Zero);
            Assert.That(coordinator.IsRunning, Is.False);
        });
    }

    [Test]
    public void DisconnectClassifier_ExcludesIntentionalKickDisposeAndFailedLogin()
    {
        var unexpected = new SoulseekClientStateChangedEventArgs(
            SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn,
            SoulseekClientStates.Disconnecting,
            "connection reset",
            new IOException("connection reset"));
        var kicked = new SoulseekClientStateChangedEventArgs(
            SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn,
            SoulseekClientStates.Disconnecting,
            "kicked",
            new KickedFromServerException());
        var disposing = new SoulseekClientStateChangedEventArgs(
            SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn,
            SoulseekClientStates.Disconnecting,
            "disposing",
            new ObjectDisposedException("client"));
        var failedLogin = new SoulseekClientStateChangedEventArgs(
            SoulseekClientStates.Connecting,
            SoulseekClientStates.Disconnected,
            "login failed",
            new IOException("offline"));

        Assert.Multiple(() =>
        {
            Assert.That(ReconnectDisconnectClassifier.IsUnexpected(unexpected, false), Is.True);
            Assert.That(ReconnectDisconnectClassifier.IsUnexpected(unexpected, true), Is.False);
            Assert.That(ReconnectDisconnectClassifier.IsUnexpected(kicked, false), Is.False);
            Assert.That(ReconnectDisconnectClassifier.IsUnexpected(disposing, false), Is.False);
            Assert.That(ReconnectDisconnectClassifier.IsUnexpected(failedLogin, false), Is.False);
        });
    }
}
