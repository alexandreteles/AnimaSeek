using AnimaSeek.iOS;
using NUnit.Framework;

namespace UnitTestCommon;

[TestFixture]
public sealed class BackgroundConnectionEligibilityPolicyTests
{
    [TestCase(false, false, true, true, true, TestName = "Current credentials can connect")]
    [TestCase(false, true, true, true, false, TestName = "Server kick blocks background relogin")]
    [TestCase(false, false, false, true, false, TestName = "Logout removes background eligibility")]
    [TestCase(false, false, true, false, false, TestName = "Logout generation race blocks background connect")]
    [TestCase(true, false, true, true, false, TestName = "Disposed session cannot background connect")]
    public void CanConnect_RequiresLiveUnblockedCurrentCredentials(
        bool disposed,
        bool reconnectBlocked,
        bool hasCredentials,
        bool generationMatches,
        bool expected)
    {
        Assert.That(
            BackgroundConnectionEligibilityPolicy.CanConnect(
                disposed,
                reconnectBlocked,
                hasCredentials,
                generationMatches),
            Is.EqualTo(expected));
    }
}
