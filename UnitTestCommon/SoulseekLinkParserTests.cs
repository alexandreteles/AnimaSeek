using NUnit.Framework;
using Seeker.Routing;

namespace UnitTestCommon;

/// <summary>Verifies portable validation and normalization for inbound and copied Soulseek links.</summary>
[TestFixture]
public sealed class SoulseekLinkParserTests
{
    /// <summary>Confirms a file link exposes its peer, path, and containing folder.</summary>
    [Test]
    public void Parse_FileLink_ReturnsNormalizedTarget()
    {
        SoulseekLink link = SoulseekLinkParser.Parse("slsk://rare_user/Music/Artist/01%20Track.flac");

        Assert.Multiple(() =>
        {
            Assert.That(link.Username, Is.EqualTo("rare_user"));
            Assert.That(link.Path, Is.EqualTo(@"Music\Artist\01 Track.flac"));
            Assert.That(link.DirectoryPath, Is.EqualTo(@"Music\Artist"));
            Assert.That(link.Kind, Is.EqualTo(SoulseekLinkKind.File));
        });
    }

    /// <summary>Confirms a trailing slash distinguishes a folder from a file.</summary>
    [Test]
    public void Parse_FolderLink_PreservesFolderPath()
    {
        SoulseekLink link = SoulseekLinkParser.Parse("slsk://rare_user/Music/Live%20Sets/");

        Assert.Multiple(() =>
        {
            Assert.That(link.Path, Is.EqualTo(@"Music\Live Sets"));
            Assert.That(link.DirectoryPath, Is.EqualTo(link.Path));
            Assert.That(link.IsFile, Is.False);
            Assert.That(link.ToString(), Is.EqualTo("slsk://rare_user/Music/Live%20Sets/"));
        });
    }

    /// <summary>Confirms malformed, ambiguous, and traversal-bearing URLs never become application routes.</summary>
    /// <param name="value">The unsafe or unsupported candidate.</param>
    [TestCase("https://user/Music/file.mp3")]
    [TestCase("slsk://user")]
    [TestCase("slsk:///Music/file.mp3")]
    [TestCase("slsk://user/Music/../secret.mp3")]
    [TestCase("slsk://user/Music/%2E%2E/secret.mp3")]
    [TestCase("slsk://user/C:%5Csecret.mp3")]
    [TestCase("slsk://user/Music/file.mp3?download=true")]
    [TestCase("slsk://user/Music/file.mp3#fragment")]
    public void TryParse_UnsafeOrUnsupportedLink_ReturnsFalse(string value) =>
        Assert.That(SoulseekLinkParser.TryParse(value, out _), Is.False);
}
