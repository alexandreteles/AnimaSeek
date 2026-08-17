using System;
using System.IO;
using AnimaSeek.iOS.Services;
using NUnit.Framework;

namespace UnitTestCommon;

[TestFixture]
public sealed class IosTransferStateStoreTests
{
    private string temporaryDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), $"animaseek-transfers-{Guid.NewGuid():N}");
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
    public void Save_FlushesAtomicFilesAndFreshInstanceLoadsExactSnapshots()
    {
        var legacy = new TestKeyValueStore();
        var store = new IosTransferStateStore(temporaryDirectory, legacy);
        const string downloads = "{\"downloads\":[\"á\",\"β\"]}";
        const string uploads = "{\"uploads\":[1,2]}";

        store.Save(downloads, uploads);
        var restarted = new IosTransferStateStore(temporaryDirectory, legacy);

        Assert.Multiple(() =>
        {
            Assert.That(restarted.LoadDownloads(), Is.EqualTo((downloads, string.Empty)));
            Assert.That(restarted.LoadUploads(), Is.EqualTo((uploads, string.Empty)));
            Assert.That(legacy.FlushCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Save_WhenApplicationSupportIsAFile_FailsWithoutReportingDurability()
    {
        string invalidRoot = Path.Combine(temporaryDirectory, "not-a-directory");
        File.WriteAllText(invalidRoot, "occupied");
        var store = new IosTransferStateStore(invalidRoot, new TestKeyValueStore());

        Assert.That(() => store.Save("downloads", "uploads"), Throws.InstanceOf<IOException>());
    }
}
