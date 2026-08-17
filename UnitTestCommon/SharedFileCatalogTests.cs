using Common.Share;
using NUnit.Framework;
using Soulseek;
using System;
using System.Linq;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class SharedFileCatalogTests
    {
        [SetUp]
        public void SetUp() => SearchUtil.ExcludedSearchPhrases = null!;

        [TearDown]
        public void TearDown() => SearchUtil.ExcludedSearchPhrases = null!;

        [Test]
        public void CatalogBuildsStableSearchBrowseAndDirectoryViews()
        {
            var catalog = new SharedFileCatalog(new[]
            {
                new SharedFileCatalogEntry("Documents/Album/Track 2.flac", "/sandbox/Track 2.flac", 20, durationSeconds: 125),
                new SharedFileCatalogEntry("Documents\\Album\\Track 1.mp3", "/sandbox/Track 1.mp3", 10, bitrate: 320),
                new SharedFileCatalogEntry("Documents\\readme.txt", "/sandbox/readme.txt", 5),
            });

            Assert.Multiple(() =>
            {
                Assert.That(catalog.DirectoryCount, Is.EqualTo(2));
                Assert.That(catalog.FileCount, Is.EqualTo(3));
                Assert.That(catalog.CreateBrowseResponse().Directories.Select(directory => directory.Name),
                    Is.EqualTo(new[] { "Documents", "Documents\\Album" }));
                Assert.That(catalog.TryGetDirectory("Documents/Album", out Soulseek.Directory? album), Is.True);
                Assert.That(album!.Files.Select(file => file.Filename), Is.EqualTo(new[] { "Track 1.mp3", "Track 2.flac" }));
                Assert.That(catalog.Search(SearchQuery.FromText("album track -2"), 10).Single().Filename,
                    Is.EqualTo("Documents\\Album\\Track 1.mp3"));
            });
        }

        [Test]
        public void SearchHonorsServerExcludedPhrasesAndLimit()
        {
            SearchUtil.ExcludedSearchPhrases = new[] { "blocked album" };
            var catalog = new SharedFileCatalog(new[]
            {
                new SharedFileCatalogEntry("Documents\\blocked album\\one.mp3", "/sandbox/one.mp3", 1),
                new SharedFileCatalogEntry("Documents\\allowed album\\two.mp3", "/sandbox/two.mp3", 2),
                new SharedFileCatalogEntry("Documents\\allowed album\\three.mp3", "/sandbox/three.mp3", 3),
            });

            Soulseek.File[] result = catalog.Search(SearchQuery.FromText("album"), 1).ToArray();

            Assert.That(result.Select(file => file.Filename), Is.EqualTo(new[] { "Documents\\allowed album\\three.mp3" }));
        }

        [TestCase("../secret")]
        [TestCase("Documents/../secret")]
        [TestCase("/absolute/file")]
        [TestCase("\\absolute\\file")]
        public void RemoteLookupRejectsTraversalAndRootedPaths(string remotePath)
        {
            var catalog = new SharedFileCatalog(new[]
            {
                new SharedFileCatalogEntry("Documents\\safe.txt", "/sandbox/safe.txt", 1),
            });

            Assert.That(catalog.TryGetEntry(remotePath, out _), Is.False);
        }

        [Test]
        public void ConstructorSkipsConflictingRemotePathsFirstWinsDeterministically()
        {
            var catalog = new SharedFileCatalog(new[]
            {
                // All three normalize to the same case-insensitive remote path: the second differs only
                // in case and the third only by APFS-legal trailing segment whitespace.
                new SharedFileCatalogEntry("Documents\\Album\\Same.mp3", "/sandbox/one", 1),
                new SharedFileCatalogEntry("documents/album/same.mp3", "/sandbox/two", 2),
                new SharedFileCatalogEntry("Documents\\Album \\Same.mp3", "/sandbox/three", 3),
            });

            Assert.Multiple(() =>
            {
                Assert.That(catalog.FileCount, Is.EqualTo(1));
                Assert.That(catalog.Entries.Single().LocalPath, Is.EqualTo("/sandbox/one"));
                Assert.That(
                    catalog.SkippedEntries.Select(entry => entry.LocalPath),
                    Is.EqualTo(new[] { "/sandbox/two", "/sandbox/three" }));
                Assert.That(catalog.TryGetEntry("documents/album/same.mp3", out SharedFileCatalogEntry? entry), Is.True);
                Assert.That(entry!.LocalPath, Is.EqualTo("/sandbox/one"));
            });
        }

        [Test]
        public void RootFilesUseConfigurableRootDirectoryName()
        {
            var catalog = new SharedFileCatalog(
                new[]
                {
                    new SharedFileCatalogEntry("readme.txt", "/sandbox/readme.txt", 5),
                },
                rootDirectoryName: "On My iPhone");

            Assert.Multiple(() =>
            {
                Assert.That(catalog.CreateBrowseResponse().Directories.Single().Name, Is.EqualTo("On My iPhone"));
                Assert.That(catalog.TryGetDirectory("On My iPhone", out Soulseek.Directory? root), Is.True);
                Assert.That(root!.Files.Single().Filename, Is.EqualTo("readme.txt"));
                Assert.That(catalog.SkippedEntries, Is.Empty);
            });
        }

        [Test]
        public void ConstructorRejectsEmptyRootDirectoryName()
        {
            Assert.Throws<ArgumentException>(() =>
                new SharedFileCatalog(Array.Empty<SharedFileCatalogEntry>(), rootDirectoryName: " "));
        }
    }
}
