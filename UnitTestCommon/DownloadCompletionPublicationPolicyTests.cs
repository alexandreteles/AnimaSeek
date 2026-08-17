using AnimaSeek.iOS;
using NUnit.Framework;
using Soulseek;

namespace UnitTestCommon;

[TestFixture]
public sealed class DownloadCompletionPublicationPolicyTests
{
    [Test]
    public void SuccessfulDownload_WaitsForDurableFinalUri()
    {
        TransferStates succeeded = TransferStates.Completed | TransferStates.Succeeded;

        Assert.Multiple(() =>
        {
            Assert.That(
                DownloadCompletionPublicationPolicy.IsAwaitingDurableFinalization(
                    TransferDirection.Download,
                    succeeded,
                    finalUri: null),
                Is.True);
            Assert.That(
                DownloadCompletionPublicationPolicy.CanPublish(
                    TransferDirection.Download,
                    succeeded,
                    finalUri: null,
                    durableFinalizationCommitted: false),
                Is.False);
            Assert.That(
                DownloadCompletionPublicationPolicy.CanPublish(
                    TransferDirection.Download,
                    succeeded,
                    "file:///Documents/song.flac",
                    durableFinalizationCommitted: true),
                Is.True);
        });
    }

    [Test]
    public void UploadSuccessAndDownloadFailure_RemainEventDriven()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                DownloadCompletionPublicationPolicy.CanPublish(
                    TransferDirection.Upload,
                    TransferStates.Completed | TransferStates.Succeeded,
                    finalUri: null,
                    durableFinalizationCommitted: false),
                Is.True);
            Assert.That(
                DownloadCompletionPublicationPolicy.CanPublish(
                    TransferDirection.Download,
                    TransferStates.Completed | TransferStates.Errored,
                    finalUri: null,
                    durableFinalizationCommitted: false),
                Is.True);
        });
    }
}
