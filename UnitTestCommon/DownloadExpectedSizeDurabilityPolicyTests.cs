using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Seeker.Transfers;

namespace UnitTestCommon;

[TestFixture]
public sealed class DownloadExpectedSizeDurabilityPolicyTests
{
    [Test]
    public void PersistBeforeApplying_PersistsBeforeVolatileMutation()
    {
        var order = new List<string>();

        DownloadExpectedSizeDurabilityPolicy.PersistBeforeApplying(
            () => order.Add("persist"),
            () => order.Add("apply"));

        Assert.That(order, Is.EqualTo(new[] { "persist", "apply" }));
    }

    [Test]
    public void PersistBeforeApplying_WhenPersistenceFails_DoesNotMutateOrRetry()
    {
        bool applied = false;

        Assert.Throws<IOException>(() =>
            DownloadExpectedSizeDurabilityPolicy.PersistBeforeApplying(
                () => throw new IOException("flush failed"),
                () => applied = true));

        Assert.That(applied, Is.False);
    }
}
