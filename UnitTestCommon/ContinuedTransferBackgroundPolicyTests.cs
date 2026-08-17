using System;
using AnimaSeek.iOS;
using NUnit.Framework;

namespace UnitTestCommon;

[TestFixture]
public sealed class ContinuedTransferBackgroundPolicyTests
{
    [Test]
    public void AcceptedRequest_CanContinueBeforeLaunchCallback()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.That(
            ContinuedTransferBackgroundPolicy.CanContinue(
                activeBatchCount: 1,
                hasAcceptedRequest: true,
                hasLaunchedTask: false,
                lastProgressAt: now - TimeSpan.FromSeconds(1),
                now,
                stallLimit: TimeSpan.FromSeconds(20)),
            Is.True);
    }

    [Test]
    public void SubmissionInFlight_BridgesPublicationRaceUntilSubmitRollsBackOrAccepts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.That(
            ContinuedTransferBackgroundPolicy.CanContinueDuringSubmission(
                activeBatchCount: 1,
                hasAcceptedRequest: false,
                hasLaunchedTask: false,
                submissionInFlight: true,
                lastProgressAt: now - TimeSpan.FromSeconds(1),
                now,
                stallLimit: TimeSpan.FromSeconds(20)),
            Is.True);
    }

    [TestCase(0, true, false, 1, false, TestName = "No active batch cannot continue")]
    [TestCase(1, false, false, 1, false, TestName = "No accepted or launched grant cannot continue")]
    [TestCase(1, true, false, 21, false, TestName = "Stalled accepted request cannot continue")]
    [TestCase(1, false, true, 1, true, TestName = "Launched progressing task can continue")]
    public void CanContinue_RequiresBatchGrantAndRecentProgress(
        int activeBatchCount,
        bool accepted,
        bool launched,
        int progressAgeSeconds,
        bool expected)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.That(
            ContinuedTransferBackgroundPolicy.CanContinue(
                activeBatchCount,
                accepted,
                launched,
                now - TimeSpan.FromSeconds(progressAgeSeconds),
                now,
                TimeSpan.FromSeconds(20)),
            Is.EqualTo(expected));
    }

    [Test]
    public void DownloadNetworkTerminal_WaitsForDurableFinalizationCallback()
    {
        ContinuedTransferBatchDecision decision = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 1,
            [ContinuedTransferMemberState.DownloadAwaitingDurableTerminal]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldFinish, Is.False);
            Assert.That(decision.Succeeded, Is.False);
        });
    }

    [Test]
    public void DurableDownloadCallback_CanFinishEvenWhenItBeatsLaterNetworkReconciliation()
    {
        ContinuedTransferBatchDecision first = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 1,
            [ContinuedTransferMemberState.DownloadSucceeded]);
        ContinuedTransferBatchDecision repeated = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 1,
            [ContinuedTransferMemberState.DownloadSucceeded]);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new ContinuedTransferBatchDecision(true, true)));
            Assert.That(repeated, Is.EqualTo(first), "duplicate callbacks must be idempotent");
        });
    }

    [Test]
    public void LogicalDownloadRetry_RemainsPendingAcrossReplacementNetworkToken()
    {
        ContinuedTransferBatchDecision retryActive = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 1,
            [ContinuedTransferMemberState.NetworkActive]);

        Assert.That(retryActive.ShouldFinish, Is.False);
    }

    [Test]
    public void MixedBatch_WaitsForDownloadThenSucceedsWithTerminalUpload()
    {
        ContinuedTransferBatchDecision awaiting = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 2,
            [
                ContinuedTransferMemberState.UploadSucceeded,
                ContinuedTransferMemberState.DownloadAwaitingDurableTerminal,
            ]);
        ContinuedTransferBatchDecision completed = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 2,
            [
                ContinuedTransferMemberState.UploadSucceeded,
                ContinuedTransferMemberState.DownloadSucceeded,
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(awaiting.ShouldFinish, Is.False);
            Assert.That(completed, Is.EqualTo(new ContinuedTransferBatchDecision(true, true)));
        });
    }

    [TestCase((int)ContinuedTransferMemberState.DownloadUnsuccessful)]
    [TestCase((int)ContinuedTransferMemberState.UploadUnsuccessful)]
    public void AnyDurableOrNetworkTerminalFailure_CompletesBatchUnsuccessfully(
        int failedMemberValue)
    {
        var failedMember = (ContinuedTransferMemberState)failedMemberValue;
        ContinuedTransferBatchDecision decision = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 2,
            [ContinuedTransferMemberState.UploadSucceeded, failedMember]);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ShouldFinish, Is.True);
            Assert.That(decision.Succeeded, Is.False);
        });
    }

    [Test]
    public void MissingLogicalMemberOrReleasedTask_CannotCompleteAgain()
    {
        ContinuedTransferBatchDecision missing = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: true,
            expectedLogicalMembers: 2,
            [ContinuedTransferMemberState.UploadSucceeded]);
        ContinuedTransferBatchDecision released = ContinuedTransferBatchCompletionPolicy.Evaluate(
            hasScheduledRequest: false,
            expectedLogicalMembers: 1,
            [ContinuedTransferMemberState.DownloadSucceeded]);

        Assert.Multiple(() =>
        {
            Assert.That(missing.ShouldFinish, Is.False);
            Assert.That(released.ShouldFinish, Is.False);
        });
    }
}
