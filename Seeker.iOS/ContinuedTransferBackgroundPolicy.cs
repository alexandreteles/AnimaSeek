using System;
using System.Collections.Generic;
using System.Linq;

namespace AnimaSeek.iOS;

/// <summary>Pure policy for deciding whether a progressing accepted transfer may continue after scene backgrounding.</summary>
internal static class ContinuedTransferBackgroundPolicy
{
    /// <summary>
    /// Bridges the synchronous scheduler call: backgrounding may observe an in-flight submission, and a later
    /// rejection must roll that provisional grant back by pausing the batch.
    /// </summary>
    public static bool CanContinueDuringSubmission(
        int activeBatchCount,
        bool hasAcceptedRequest,
        bool hasLaunchedTask,
        bool submissionInFlight,
        DateTimeOffset lastProgressAt,
        DateTimeOffset now,
        TimeSpan stallLimit) =>
        CanContinue(
            activeBatchCount,
            hasAcceptedRequest || submissionInFlight,
            hasLaunchedTask,
            lastProgressAt,
            now,
            stallLimit);

    /// <summary>Determines whether the app can keep the active continued-processing batch running.</summary>
    /// <param name="activeBatchCount">The number of still-active transfers belonging to the submitted batch.</param>
    /// <param name="hasAcceptedRequest">Whether iOS accepted the continued-processing request.</param>
    /// <param name="hasLaunchedTask">Whether iOS has delivered the launch callback and task object.</param>
    /// <param name="lastProgressAt">The last observed positive byte advancement.</param>
    /// <param name="now">The decision time.</param>
    /// <param name="stallLimit">The maximum age of qualifying progress.</param>
    /// <returns>
    /// <see langword="true"/> for a live, recently progressing batch backed by either an accepted request or its
    /// launched task; the launch callback need not have arrived yet.
    /// </returns>
    public static bool CanContinue(
        int activeBatchCount,
        bool hasAcceptedRequest,
        bool hasLaunchedTask,
        DateTimeOffset lastProgressAt,
        DateTimeOffset now,
        TimeSpan stallLimit) =>
        activeBatchCount > 0 &&
        (hasAcceptedRequest || hasLaunchedTask) &&
        lastProgressAt != default &&
        now >= lastProgressAt &&
        now - lastProgressAt <= stallLimit;
}

/// <summary>Pure completion policy for a continued-processing batch keyed by logical transfer identity.</summary>
internal static class ContinuedTransferBatchCompletionPolicy
{
    /// <summary>
    /// Finishes only after every logical member is terminal, with downloads requiring an explicit durable
    /// finalization outcome instead of Soulseek's earlier network-terminal state.
    /// </summary>
    /// <param name="hasScheduledRequest">Whether the process still owns a submitted or launched task.</param>
    /// <param name="expectedLogicalMembers">The number of identities admitted to the continued batch.</param>
    /// <param name="observedStates">The latest state for each currently observed logical identity.</param>
    /// <returns>Whether the task may finish and, if so, whether every member succeeded.</returns>
    public static ContinuedTransferBatchDecision Evaluate(
        bool hasScheduledRequest,
        int expectedLogicalMembers,
        IReadOnlyCollection<ContinuedTransferMemberState> observedStates)
    {
        ArgumentNullException.ThrowIfNull(observedStates);
        bool shouldFinish = hasScheduledRequest &&
            expectedLogicalMembers > 0 &&
            observedStates.Count == expectedLogicalMembers &&
            observedStates.All(IsTerminal);
        bool succeeded = shouldFinish && observedStates.All(IsSuccessful);
        return new ContinuedTransferBatchDecision(shouldFinish, succeeded);
    }

    private static bool IsTerminal(ContinuedTransferMemberState state) => state is
        ContinuedTransferMemberState.DownloadSucceeded or
        ContinuedTransferMemberState.DownloadUnsuccessful or
        ContinuedTransferMemberState.UploadSucceeded or
        ContinuedTransferMemberState.UploadUnsuccessful;

    private static bool IsSuccessful(ContinuedTransferMemberState state) => state is
        ContinuedTransferMemberState.DownloadSucceeded or
        ContinuedTransferMemberState.UploadSucceeded;
}

/// <summary>Describes the completion-relevant state of one logical continued-transfer identity.</summary>
internal enum ContinuedTransferMemberState
{
    /// <summary>The current Soulseek transfer is still performing network work.</summary>
    NetworkActive,

    /// <summary>Soulseek ended a download, but its file and metadata have not crossed the durable terminal boundary.</summary>
    DownloadAwaitingDurableTerminal,

    /// <summary>The completed download file and its terminal metadata are durable.</summary>
    DownloadSucceeded,

    /// <summary>A definitive download failure or explicit clear is durable.</summary>
    DownloadUnsuccessful,

    /// <summary>An upload reached Soulseek network success, which is its terminal boundary.</summary>
    UploadSucceeded,

    /// <summary>An upload reached a non-success network terminal state.</summary>
    UploadUnsuccessful,
}

/// <summary>Reports whether a continued batch may complete and what result to give iOS.</summary>
/// <param name="ShouldFinish">Whether every logical member crossed its direction-specific terminal boundary.</param>
/// <param name="Succeeded">Whether every terminal member succeeded.</param>
internal readonly record struct ContinuedTransferBatchDecision(bool ShouldFinish, bool Succeeded);
