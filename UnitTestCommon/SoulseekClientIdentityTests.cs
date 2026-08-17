using NUnit.Framework;
using Seeker;
using Soulseek;

namespace UnitTestCommon;

[TestFixture]
public sealed class SoulseekClientIdentityTests
{
    [Test]
    public void ModifiedLibraryNotice_UsesTheRequiredNonEndorsementText()
    {
        Assert.That(
            SoulseekClientIdentity.ModifiedLibraryNotice,
            Is.EqualTo(
                "This is a modified version of Soulseek.NET.  It is not maintained by, endorsed by, or affiliated " +
                "with the Soulseek.NET project or its author(s)."));
    }

    [Test]
    public void ProductionClient_AcceptsAndReportsTheAnimaSeekIdentity()
    {
        using var client = new SoulseekClient(SoulseekClientIdentity.MinorVersion);

        Assert.That(client.MinorVersion, Is.EqualTo(793));
    }

#if MOCK
    [Test]
    public void MockClient_UsesTheProductionProtocolIdentity()
    {
        using var client = new MockSoulseekClient();

        Assert.Multiple(() =>
        {
            Assert.That(client.MajorVersion, Is.EqualTo(170));
            Assert.That(client.MinorVersion, Is.EqualTo(SoulseekClientIdentity.MinorVersion));
        });
    }
#endif
}
