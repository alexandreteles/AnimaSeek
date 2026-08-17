using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AnimaSeek.iOS;
using NUnit.Framework;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class IosInterruptedDownloadStoreTests
    {
        [Test]
        public void Remember_PersistsCurrentDeterministicDescriptorAndFlushes()
        {
            var keyValueStore = new TestKeyValueStore();
            var store = new IosInterruptedDownloadStore(keyValueStore);
            var expected = new SuspendedDownload("alice", @"album\song.flac", 42_000);

            store.Remember(expected);

            string encoded = store.ReadEncoded().Single();
            Assert.Multiple(() =>
            {
                Assert.That(encoded, Does.StartWith("3|"));
                Assert.That(IosInterruptedDownloadStore.TryDecode(encoded, out SuspendedDownload actual, out bool legacy), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(legacy, Is.False);
                Assert.That(keyValueStore.FlushCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Remember_RejectsRootedAndTraversalPaths()
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => store.Remember(new SuspendedDownload("alice", "song.flac", 1, "/tmp/song.part")),
                    Throws.ArgumentException);
                Assert.That(
                    () => store.Remember(new SuspendedDownload("alice", "song.flac", 1, "../song.part")),
                    Throws.ArgumentException);
            });
        }

        [Test]
        public void Forget_RemovesEveryVersionOfIdentityAndPreservesUnrelatedOrMalformedEntries()
        {
            var keyValueStore = new TestKeyValueStore();
            string legacy = EncodeLegacy(1, "alice", @"album\song.flac", 42_000);
            string current = IosInterruptedDownloadStore.Encode(
                new SuspendedDownload("alice", @"album\song.flac", 43_000));
            string other = IosInterruptedDownloadStore.Encode(
                new SuspendedDownload("bob", @"album\song.flac", 42_000));
            keyValueStore.PutStringSet(
                IosInterruptedDownloadStore.StorageKey,
                [legacy, current, other, "malformed"]);
            var store = new IosInterruptedDownloadStore(keyValueStore);

            store.Forget("alice", @"album\song.flac");

            Assert.That(store.ReadEncoded(), Is.EquivalentTo(new[] { other, "malformed" }));
        }

        [Test]
        public void Reconcile_PreservesDescriptorAddedAfterSnapshotWasRead()
        {
            var keyValueStore = new TestKeyValueStore();
            var store = new IosInterruptedDownloadStore(keyValueStore);
            var first = new SuspendedDownload("alice", "first.flac", 1);
            var second = new SuspendedDownload("bob", "second.flac", 2);
            store.Remember(first);
            var original = store.ReadEncoded();
            store.Remember(second);

            store.Reconcile(original, []);

            string encoded = store.ReadEncoded().Single();
            Assert.That(IosInterruptedDownloadStore.TryDecode(encoded, out SuspendedDownload actual, out _), Is.True);
            Assert.That(actual, Is.EqualTo(second));
        }

        [Test]
        public void Reconcile_DoesNotResurrectDescriptorForgottenAfterSnapshotWasRead()
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());
            var terminal = new SuspendedDownload("alice", "terminal.flac", 42);
            store.Remember(terminal);
            var original = store.ReadEncoded();

            store.Forget(terminal.Username, terminal.Filename);
            store.Reconcile(original, original);

            Assert.That(store.ReadEncoded(), Is.Empty);
        }

        [Test]
        public void TryDecode_IdentifiesLegacyAbsolutePathForContainmentMigration()
        {
            string encoded = EncodeLegacy(2, "alice", "song.flac", 42, "/old/container/song.part");

            bool decoded = IosInterruptedDownloadStore.TryDecode(
                encoded,
                out SuspendedDownload actual,
                out bool containsLegacyAbsolutePath);

            Assert.Multiple(() =>
            {
                Assert.That(decoded, Is.True);
                Assert.That(containsLegacyAbsolutePath, Is.True);
                Assert.That(actual.DocumentsRelativePath, Is.EqualTo("/old/container/song.part"));
            });
        }

        [Test]
        public void ResumePolicy_ChoosesNewerManagerUntilAnAuthoritativeCorrectionIsDurable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(InterruptedDownloadResumePolicy.ResolveExpectedSize(80, 120, false), Is.EqualTo(120));
                Assert.That(InterruptedDownloadResumePolicy.IsComplete(90, 80, 120, false), Is.False);
                Assert.That(InterruptedDownloadResumePolicy.IsComplete(120, 80, 120, false), Is.True);
                Assert.That(InterruptedDownloadResumePolicy.IsComplete(121, 80, 120, false), Is.False);
                Assert.That(InterruptedDownloadResumePolicy.ResolveExpectedSize(120, 80, false), Is.EqualTo(80));
                Assert.That(InterruptedDownloadResumePolicy.IsComplete(80, 120, 80, false), Is.True);
            });
        }

        [Test]
        public void FinalizationPolicy_UsesAuthoritativeDescriptorAcrossManagerCheckpointWindow()
        {
            var correctedUp = new SuspendedDownload(
                "alice",
                @"music\up.flac",
                120,
                SizeIsAuthoritative: true);
            var correctedDown = new SuspendedDownload(
                "alice",
                @"music\down.flac",
                80,
                SizeIsAuthoritative: true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    InterruptedDownloadResumePolicy.ResolveFinalizationExpectedSize(120, 80, correctedUp),
                    Is.EqualTo(120),
                    "the larger exact target must commit when v4 beat the stale manager to disk");
                Assert.That(
                    InterruptedDownloadResumePolicy.ResolveFinalizationExpectedSize(80, 120, correctedDown),
                    Is.EqualTo(80),
                    "the smaller exact target must commit when v4 beat the stale manager to disk");
            });
        }

        [TestCase(80, 120, TestName = "Authoritative downward correction wins over stale larger manager")]
        [TestCase(120, 80, TestName = "Authoritative upward correction wins over stale smaller manager")]
        public void AuthoritativeCorrectedSize_SurvivesDescriptorBeforeManagerCrashWindow(
            long correctedSize,
            long staleManagerSize)
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());
            var corrected = new SuspendedDownload(
                "alice",
                @"album\song.flac",
                correctedSize,
                SizeIsAuthoritative: true);

            store.Remember(corrected);
            string encoded = store.ReadEncoded().Single();
            Assert.That(encoded, Does.StartWith("4|"));
            Assert.That(IosInterruptedDownloadStore.TryDecode(encoded, out SuspendedDownload restored, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(restored, Is.EqualTo(corrected));
                Assert.That(
                    InterruptedDownloadResumePolicy.ResolveExpectedSize(
                        restored.Size,
                        staleManagerSize,
                        restored.SizeIsAuthoritative),
                    Is.EqualTo(correctedSize));
                Assert.That(
                    InterruptedDownloadResumePolicy.IsComplete(
                        correctedSize,
                        restored.Size,
                        staleManagerSize,
                        restored.SizeIsAuthoritative),
                    Is.True);
            });
        }

        [TestCase(80, 120, TestName = "Downward correction cannot trust an equal-length stale partial")]
        [TestCase(120, 80, TestName = "Upward correction cannot append bytes from the prior remote file")]
        public void ResetRequiredDescriptor_BlocksCompletionUntilOldPartialIsDeleted(
            long correctedSize,
            long staleManagerSize)
        {
            var resetRequired = new SuspendedDownload(
                "alice",
                @"album\changed.flac",
                correctedSize,
                SizeIsAuthoritative: true,
                PartialResetRequired: true);
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());

            store.Remember(resetRequired);
            string encoded = store.ReadEncoded().Single();

            Assert.Multiple(() =>
            {
                Assert.That(encoded, Does.StartWith("5|"));
                Assert.That(IosInterruptedDownloadStore.TryDecode(encoded, out SuspendedDownload restored, out _), Is.True);
                Assert.That(restored, Is.EqualTo(resetRequired));
                Assert.That(
                    InterruptedDownloadResumePolicy.IsComplete(
                        correctedSize,
                        correctedSize,
                        staleManagerSize,
                        descriptorSizeIsAuthoritative: true,
                        partialResetRequired: true),
                    Is.False);
            });
        }

        [Test]
        public void PartialReset_MarkerDeleteCheckpointCompletionOrderIsDurable()
        {
            string partialPath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(partialPath, [0x10, 0x20, 0x30]);
                var keyValueStore = new TestKeyValueStore();
                var store = new IosInterruptedDownloadStore(keyValueStore);
                var resetRequired = new SuspendedDownload(
                    "alice",
                    @"album\changed.flac",
                    2,
                    SizeIsAuthoritative: true,
                    PartialResetRequired: true);
                var order = new List<string>();

                InterruptedDownloadPartialResetPolicy.Resolve(
                    () =>
                    {
                        store.Remember(resetRequired);
                        order.Add("v5");
                    },
                    () =>
                    {
                        File.Delete(partialPath);
                        order.Add("delete");
                    },
                    () => order.Add("manager"),
                    () =>
                    {
                        order.Add("checkpoint");
                        return true;
                    },
                    () =>
                    {
                        store.CompletePartialReset(resetRequired with { PartialResetRequired = false });
                        order.Add("v4");
                    });

                string encoded = store.ReadEncoded().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(order, Is.EqualTo(new[] { "v5", "delete", "manager", "checkpoint", "v4" }));
                    Assert.That(File.Exists(partialPath), Is.False);
                    Assert.That(encoded, Does.StartWith("4|"));
                    Assert.That(IosInterruptedDownloadStore.TryDecode(encoded, out SuspendedDownload restored, out _), Is.True);
                    Assert.That(restored.PartialResetRequired, Is.False);
                    Assert.That(restored.SizeIsAuthoritative, Is.True);
                });
            }
            finally
            {
                File.Delete(partialPath);
            }
        }

        [Test]
        public void PartialReset_WhenDeletionFails_RetainsV5AndDoesNotMutateOrComplete()
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());
            var resetRequired = new SuspendedDownload(
                "alice",
                @"album\changed.flac",
                80,
                SizeIsAuthoritative: true,
                PartialResetRequired: true);
            bool managerMutated = false;
            bool completed = false;

            Assert.Throws<IOException>(() => InterruptedDownloadPartialResetPolicy.Resolve(
                () => store.Remember(resetRequired),
                () => throw new IOException("delete failed"),
                () => managerMutated = true,
                () => true,
                () =>
                {
                    completed = true;
                    store.CompletePartialReset(resetRequired with { PartialResetRequired = false });
                }));

            string encoded = store.ReadEncoded().Single();
            Assert.Multiple(() =>
            {
                Assert.That(encoded, Does.StartWith("5|"));
                Assert.That(managerMutated, Is.False);
                Assert.That(completed, Is.False);
            });
        }

        [Test]
        public void PartialReset_WhenManagerCheckpointFails_RetainsV5AndSuppressesCompletion()
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());
            var resetRequired = new SuspendedDownload(
                "alice",
                @"album\changed.flac",
                80,
                SizeIsAuthoritative: true,
                PartialResetRequired: true);
            bool managerMutated = false;
            bool completed = false;

            Assert.Throws<IOException>(() => InterruptedDownloadPartialResetPolicy.Resolve(
                () => store.Remember(resetRequired),
                static () => { },
                () => managerMutated = true,
                () => false,
                () => completed = true));

            Assert.Multiple(() =>
            {
                Assert.That(store.ReadEncoded().Single(), Does.StartWith("5|"));
                Assert.That(managerMutated, Is.True);
                Assert.That(completed, Is.False);
            });
        }

        [Test]
        public void PartialReset_CrashAfterDeleteBeforeV4_ReplaysFromZero()
        {
            string partialPath = Path.GetTempFileName();
            try
            {
                var keyValueStore = new TestKeyValueStore();
                var resetRequired = new SuspendedDownload(
                    "alice",
                    @"album\changed.flac",
                    120,
                    SizeIsAuthoritative: true,
                    PartialResetRequired: true);
                new IosInterruptedDownloadStore(keyValueStore).Remember(resetRequired);
                File.Delete(partialPath);

                var relaunched = new IosInterruptedDownloadStore(keyValueStore);
                Assert.That(
                    IosInterruptedDownloadStore.TryDecode(
                        relaunched.ReadEncoded().Single(),
                        out SuspendedDownload restored,
                        out _),
                    Is.True);
                Assert.That(restored.PartialResetRequired, Is.True);
                bool managerReset = false;

                InterruptedDownloadPartialResetPolicy.Resolve(
                    static () => { },
                    () => File.Delete(partialPath),
                    () => managerReset = true,
                    () => true,
                    () => relaunched.CompletePartialReset(restored with { PartialResetRequired = false }));

                string encoded = relaunched.ReadEncoded().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(managerReset, Is.True);
                    Assert.That(encoded, Does.StartWith("4|"));
                    Assert.That(File.Exists(partialPath), Is.False);
                });
            }
            finally
            {
                File.Delete(partialPath);
            }
        }

        [Test]
        public void PendingPartialReset_CannotBeDowngradedBySuspensionOrStaleReconcile()
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());
            var original = new SuspendedDownload("alice", @"album\changed.flac", 100);
            store.Remember(original);
            var staleSnapshot = store.ReadEncoded();
            var resetRequired = original with
            {
                Size = 80,
                SizeIsAuthoritative = true,
                PartialResetRequired = true,
            };
            store.Remember(resetRequired);

            store.Remember(resetRequired with { PartialResetRequired = false });
            store.Remember([resetRequired with { PartialResetRequired = false }]);
            store.Reconcile(staleSnapshot, staleSnapshot);

            string encoded = store.ReadEncoded().Single();
            Assert.That(encoded, Does.StartWith("5|"));
            Assert.That(IosInterruptedDownloadStore.TryDecode(encoded, out SuspendedDownload restored, out _), Is.True);
            Assert.That(restored, Is.EqualTo(resetRequired));
        }

        [Test]
        public void PendingPartialReset_TerminalTombstoneWinsOverStaleReconcile()
        {
            var store = new IosInterruptedDownloadStore(new TestKeyValueStore());
            var resetRequired = new SuspendedDownload(
                "alice",
                @"album\changed.flac",
                80,
                SizeIsAuthoritative: true,
                PartialResetRequired: true);
            store.Remember(resetRequired);
            var staleSnapshot = store.ReadEncoded();

            store.Forget(resetRequired.Username, resetRequired.Filename);
            store.Reconcile(staleSnapshot, staleSnapshot);

            Assert.That(store.ReadEncoded(), Is.Empty);
        }

        [Test]
        public void TryRebaseLegacyAbsoluteDocumentsPath_RebasesStaleContainerPathOntoRelativeSuffix()
        {
            const string stalePath =
                "/var/mobile/Containers/Data/Application/0000-OLD-UUID/Documents/album/song.flac.part";

            bool rebased = IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath(
                stalePath,
                out string relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(rebased, Is.True);
                Assert.That(relativePath, Is.EqualTo("album/song.flac.part"));
            });
        }

        [Test]
        public void TryRebaseLegacyAbsoluteDocumentsPath_UsesFirstDocumentsSegmentAndKeepsNestedSuffix()
        {
            bool rebased = IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath(
                "/old/Documents/nested/Documents/song.part",
                out string relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(rebased, Is.True);
                Assert.That(relativePath, Is.EqualTo("nested/Documents/song.part"));
            });
        }

        [Test]
        public void TryRebaseLegacyAbsoluteDocumentsPath_RejectsTraversalMissingSegmentAndEmptySuffix()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath(
                        "/old/Documents/../escape/song.part",
                        out _),
                    Is.False);
                Assert.That(
                    IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath(
                        "/old/Library/song.part",
                        out _),
                    Is.False);
                Assert.That(
                    IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath("/old/Documents/", out _),
                    Is.False);
                Assert.That(
                    IosInterruptedDownloadStore.TryRebaseLegacyAbsoluteDocumentsPath("   ", out _),
                    Is.False);
            });
        }

        [Test]
        public void RequiresOversizedPartialDiscard_DiscardsOnlyBytesBeyondKnownExpectedSize()
        {
            Assert.Multiple(() =>
            {
                Assert.That(InterruptedDownloadResumePolicy.RequiresOversizedPartialDiscard(101, 100), Is.True);
                Assert.That(InterruptedDownloadResumePolicy.RequiresOversizedPartialDiscard(1, 0), Is.True);
                Assert.That(InterruptedDownloadResumePolicy.RequiresOversizedPartialDiscard(100, 100), Is.False);
                Assert.That(InterruptedDownloadResumePolicy.RequiresOversizedPartialDiscard(99, 100), Is.False);
                Assert.That(InterruptedDownloadResumePolicy.RequiresOversizedPartialDiscard(5, -1), Is.False);
            });
        }

        [Test]
        public void IsComplete_OversizedPartialAfterDownwardCorrectionIsNeverComplete()
        {
            Assert.Multiple(() =>
            {
                // A durable authoritative downward correction supersedes a larger stale manager size.
                Assert.That(
                    InterruptedDownloadResumePolicy.IsComplete(
                        partialLength: 150,
                        descriptorSize: 100,
                        managerSize: 150,
                        descriptorSizeIsAuthoritative: true),
                    Is.False);
                // A corrected manager size supersedes a larger non-authoritative descriptor size.
                Assert.That(
                    InterruptedDownloadResumePolicy.IsComplete(
                        partialLength: 150,
                        descriptorSize: 150,
                        managerSize: 100,
                        descriptorSizeIsAuthoritative: false),
                    Is.False);
            });
        }

        private static string EncodeLegacy(
            int version,
            string username,
            string filename,
            long size,
            string? path = null)
        {
            string result = $"{version}|{Encode(username)}|{Encode(filename)}|{size}";
            return path is null ? result : $"{result}|{Encode(path)}";
        }

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }
}
