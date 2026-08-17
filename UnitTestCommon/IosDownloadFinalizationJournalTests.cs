using System;
using System.Collections.Generic;
using System.IO;
using AnimaSeek.iOS;
using AnimaSeek.iOS.Services;
using NUnit.Framework;

namespace UnitTestCommon;

[TestFixture]
public sealed class IosDownloadFinalizationJournalTests
{
    private string temporaryDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), $"animaseek-finalization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void Prepare_PersistsMultipleIdentitiesAcrossFreshJournalInstance()
    {
        var first = Entry("alice", @"album\first.flac", 41, "aa.part", "album/first.flac");
        var second = Entry("bob", @"album\second.flac", 42, "bb.part", "album/second.flac");
        var journal = new IosDownloadFinalizationJournal(temporaryDirectory);

        journal.Prepare(first);
        journal.Prepare(second);

        var restarted = new IosDownloadFinalizationJournal(temporaryDirectory);
        Assert.That(restarted.Read(), Is.EquivalentTo(new[] { first, second }));

        restarted.Complete(first.Username, first.Filename);
        Assert.That(restarted.Read(), Is.EqualTo(new[] { second }));
    }

    [Test]
    public void Prepare_RejectsRootedTraversalAndNegativeLength()
    {
        var journal = new IosDownloadFinalizationJournal(temporaryDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => journal.Prepare(Entry("alice", "one", 1, "../escape.part", "one.flac")),
                Throws.ArgumentException);
            Assert.That(
                () => journal.Prepare(Entry("alice", "one", 1, "one.part", "/escape.flac")),
                Throws.ArgumentException);
            Assert.That(
                () => journal.Prepare(Entry("alice", "one", -1, "one.part", "one.flac")),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [TestCase(false, true, false, 0, (int)DownloadFinalizationRecoveryAction.RetryFromDescriptor,
        TestName = "Crash before move retries from retained source")]
    [TestCase(false, true, true, 42, (int)DownloadFinalizationRecoveryAction.RetryFromDescriptor,
        TestName = "Source-present collision never impersonates committed move")]
    [TestCase(false, false, true, 42, (int)DownloadFinalizationRecoveryAction.CommitFinalFile,
        TestName = "Crash after move commits exact destination")]
    [TestCase(false, false, true, 41, (int)DownloadFinalizationRecoveryAction.RetryFromDescriptor,
        TestName = "Wrong-length destination is never committed")]
    [TestCase(false, false, false, 0, (int)DownloadFinalizationRecoveryAction.RetryFromDescriptor,
        TestName = "Missing move endpoints retry safely")]
    [TestCase(true, false, true, 42, (int)DownloadFinalizationRecoveryAction.AlreadyCheckpointed,
        TestName = "Crash after manager checkpoint is idempotently terminal")]
    [TestCase(true, true, true, 42, (int)DownloadFinalizationRecoveryAction.RetryFromDescriptor,
        TestName = "Early manager success cannot commit while the source remains")]
    [TestCase(true, false, true, 41, (int)DownloadFinalizationRecoveryAction.RetryFromDescriptor,
        TestName = "Early manager success cannot commit a wrong-length target")]
    public void RecoveryPolicy_ClassifiesCrashBoundaries(
        bool managerSucceeded,
        bool sourceExists,
        bool targetExists,
        long targetLength,
        int expected)
    {
        Assert.That(
            IosDownloadFinalizationRecoveryPolicy.Classify(
                managerSucceeded,
                sourceExists,
                targetExists,
                targetLength,
                recordedLength: 42),
            Is.EqualTo((DownloadFinalizationRecoveryAction)expected));
    }

    [Test]
    public void PreparedMove_AndFailedCheckpointRetainEveryRecoveryMarker()
    {
        var keyValueStore = new TestKeyValueStore();
        var descriptors = new IosInterruptedDownloadStore(keyValueStore);
        var descriptor = new SuspendedDownload("alice", @"album\song.flac", 4);
        descriptors.Remember(descriptor);
        var entry = Entry(descriptor.Username, descriptor.Filename, 4, "source.part", "album/song.flac");
        var journal = new IosDownloadFinalizationJournal(temporaryDirectory);
        journal.Prepare(entry);
        string source = Path.Combine(temporaryDirectory, entry.IncompleteRelativePath);
        string target = Path.Combine(temporaryDirectory, entry.DocumentsRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(source, [1, 2, 3, 4]);

        Assert.That(
            IosDownloadFinalizationRecoveryPolicy.Classify(false, true, false, 0, entry.ActualLength),
            Is.EqualTo(DownloadFinalizationRecoveryAction.RetryFromDescriptor));
        File.Move(source, target);
        Assert.That(
            IosDownloadFinalizationRecoveryPolicy.Classify(
                false,
                File.Exists(source),
                File.Exists(target),
                new FileInfo(target).Length,
                entry.ActualLength),
            Is.EqualTo(DownloadFinalizationRecoveryAction.CommitFinalFile));

        bool committed = IosDownloadFinalizationRecoveryPolicy.TryCommit(
            applyTerminalState: static () => { },
            checkpointTransfers: static () => false,
            forgetDescriptor: () => descriptors.Forget(descriptor.Username, descriptor.Filename),
            completeJournal: () => journal.Complete(entry.Username, entry.Filename));

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.False);
            Assert.That(descriptors.ReadEncoded(), Has.Count.EqualTo(1));
            Assert.That(new IosDownloadFinalizationJournal(temporaryDirectory).Read(), Is.EqualTo(new[] { entry }));
        });
    }

    [Test]
    public void Commit_ClearsDescriptorThenJournalOnlyAfterCheckpoint()
    {
        var calls = new List<string>();

        bool committed = IosDownloadFinalizationRecoveryPolicy.TryCommit(
            () => calls.Add("manager"),
            () =>
            {
                calls.Add("checkpoint");
                return true;
            },
            () => calls.Add("descriptor"),
            () => calls.Add("journal"));

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "manager", "checkpoint", "descriptor", "journal" }));
        });
    }

    [Test]
    public void TerminalFailure_RetainsDescriptorWhenManagerCheckpointFails()
    {
        var calls = new List<string>();

        bool committed = IosDownloadFinalizationRecoveryPolicy.TryCheckpointTerminal(
            () => calls.Add("manager"),
            () =>
            {
                calls.Add("checkpoint");
                return false;
            },
            () => calls.Add("descriptor"));

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "manager", "checkpoint" }));
        });
    }

    [Test]
    public void CrashAfterDescriptorTombstone_ReplaysCheckpointedJournalIdempotently()
    {
        var keyValueStore = new TestKeyValueStore();
        var descriptors = new IosInterruptedDownloadStore(keyValueStore);
        var entry = Entry("alice", @"album\song.flac", 42, "source.part", "album/song.flac");
        var journal = new IosDownloadFinalizationJournal(temporaryDirectory);
        descriptors.Remember(new SuspendedDownload(entry.Username, entry.Filename, entry.ActualLength));
        journal.Prepare(entry);

        // Simulate termination after the manager checkpoint and descriptor tombstone but before journal cleanup.
        descriptors.Forget(entry.Username, entry.Filename);
        Assert.That(journal.Read(), Is.EqualTo(new[] { entry }));
        Assert.That(
            IosDownloadFinalizationRecoveryPolicy.Classify(true, false, true, 42, 42),
            Is.EqualTo(DownloadFinalizationRecoveryAction.AlreadyCheckpointed));

        bool committed = IosDownloadFinalizationRecoveryPolicy.TryCommit(
            applyTerminalState: static () => { },
            checkpointTransfers: static () => true,
            forgetDescriptor: () => descriptors.Forget(entry.Username, entry.Filename),
            completeJournal: () => journal.Complete(entry.Username, entry.Filename));

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.True);
            Assert.That(descriptors.ReadEncoded(), Is.Empty);
            Assert.That(journal.Read(), Is.Empty);
        });
    }

    private static PendingDownloadFinalization Entry(
        string username,
        string filename,
        long length,
        string incompletePath,
        string documentsPath) =>
        new(username, filename, length, incompletePath, documentsPath);
}
