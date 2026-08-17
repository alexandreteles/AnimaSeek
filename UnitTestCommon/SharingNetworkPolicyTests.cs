using System;
using NUnit.Framework;
using Seeker.Services;

namespace UnitTestCommon;

[TestFixture]
public sealed class SharingNetworkPolicyTests
{
    [TestCase(false, false, false, false, true, TestName = "Unmetered direct route is permitted by default")]
    [TestCase(true, false, false, false, false, TestName = "Metered route is denied when metered uploads are disabled")]
    [TestCase(true, false, true, false, true, TestName = "Metered route is permitted when explicitly allowed")]
    [TestCase(false, false, true, true, false, TestName = "VPN requirement fails closed when VPN cannot be identified")]
    [TestCase(false, true, true, true, true, TestName = "Positively identified VPN satisfies the requirement")]
    [TestCase(true, true, false, true, false, TestName = "VPN does not override the metered restriction")]
    public void PermitsUpload_RequiresEveryEnabledRestriction(
        bool isMetered,
        bool isVpnActive,
        bool allowUploadsOnMetered,
        bool requireVpnForSharing,
        bool expected)
    {
        var networkStatus = new StubNetworkStatus(isMetered, isVpnActive);

        bool permitted = SharingNetworkPolicy.PermitsUpload(
            networkStatus,
            allowUploadsOnMetered,
            requireVpnForSharing);

        Assert.That(permitted, Is.EqualTo(expected));
    }

    [Test]
    public void PermitsUpload_RejectsNullStatus() =>
        Assert.Throws<ArgumentNullException>(() =>
            SharingNetworkPolicy.PermitsUpload(null!, allowUploadsOnMetered: true, requireVpnForSharing: false));

    private sealed class StubNetworkStatus(bool isMetered, bool isVpnActive) : INetworkStatus
    {
        public bool IsMetered { get; } = isMetered;

        public bool IsVpnActive { get; } = isVpnActive;

        public bool DoWeHaveInternet() => true;

        public bool HasHandoffOccuredRecently() => false;
    }
}
