#if MOCK
using NUnit.Framework;
using Seeker;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnitTestCommon;

[TestFixture]
public sealed class MockSocialSurfaceTests
{
    [Test]
    public async Task UserStatusAndOptionReconfiguration_AreImplemented()
    {
        using var client = new MockSoulseekClient { SimulatedDelayMs = 1 };
        client.StopBackgroundTimers();

        UserStatus generated = await client.GetUserStatusAsync("listener");
        bool changed = await client.ReconfigureOptionsAsync(
            new SoulseekClientOptionsPatch(acceptPrivateRoomInvitations: true));

        Assert.Multiple(() =>
        {
            Assert.That(generated.Username, Is.EqualTo("listener"));
            Assert.That(Enum.IsDefined(generated.Presence), Is.True);
            Assert.That(changed, Is.True);
            Assert.That(client.Options, Is.Not.Null);
            Assert.That(client.Options!.AcceptPrivateRoomInvitations, Is.True);
        });
    }

    [Test]
    public async Task PrivateRoomCommands_UpdateStateAndRaiseSemanticEvents()
    {
        using var client = new MockSoulseekClient { SimulatedDelayMs = 1 };
        client.StopBackgroundTimers();
        var memberSnapshots = new List<IReadOnlyCollection<string>>();
        var moderatorSnapshots = new List<IReadOnlyCollection<string>>();
        var operatorChanges = new List<OperatorAddedRemovedEventArgs>();
        var membershipRemovals = new List<string>();

        client.PrivateRoomUserListReceived += (_, room) => memberSnapshots.Add(room.Users);
        client.PrivateRoomModeratedUserListReceived += (_, room) => moderatorSnapshots.Add(room.Users);
        client.OperatorInPrivateRoomAddedRemoved += (_, change) => operatorChanges.Add(change);
        client.PrivateRoomMembershipRemoved += (_, roomName) => membershipRemovals.Add(roomName);

        await client.AddPrivateRoomMemberAsync("studio", "member");
        await client.RemovePrivateRoomMemberAsync("studio", "member");
        await client.AddPrivateRoomModeratorAsync("studio", "moderator");
        await client.RemovePrivateRoomModeratorAsync("studio", "moderator");
        await client.DropPrivateRoomMembershipAsync("studio");
        await client.DropPrivateRoomOwnershipAsync("owned");

        Assert.Multiple(() =>
        {
            Assert.That(memberSnapshots, Has.Count.EqualTo(2));
            Assert.That(memberSnapshots[0], Is.EquivalentTo(new[] { "member" }));
            Assert.That(memberSnapshots[1], Is.Empty);
            Assert.That(moderatorSnapshots, Has.Count.EqualTo(2));
            Assert.That(moderatorSnapshots[0], Is.EquivalentTo(new[] { "moderator" }));
            Assert.That(moderatorSnapshots[1], Is.Empty);
            Assert.That(operatorChanges.Select(change => change.Added), Is.EqualTo(new[] { true, false }));
            Assert.That(membershipRemovals, Is.EqualTo(new[] { "studio", "owned" }));
        });
    }

    [Test]
    public async Task TickerCommands_RaiseAddAndRemoveEvents()
    {
        using var client = new MockSoulseekClient
        {
            SimulatedDelayMs = 1,
            Username = "current-user",
        };
        client.StopBackgroundTimers();
        var added = new List<RoomTickerAddedEventArgs>();
        var removed = new List<RoomTickerRemovedEventArgs>();
        client.RoomTickerAdded += (_, args) => added.Add(args);
        client.RoomTickerRemoved += (_, args) => removed.Add(args);

        await client.SetRoomTickerAsync("ambient", "listening");
        await client.SetRoomTickerAsync("ambient", string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(added, Has.Count.EqualTo(1));
            Assert.That(added[0].RoomName, Is.EqualTo("ambient"));
            Assert.That(added[0].Ticker.Username, Is.EqualTo("current-user"));
            Assert.That(added[0].Ticker.Message, Is.EqualTo("listening"));
            Assert.That(removed, Has.Count.EqualTo(1));
            Assert.That(removed[0].RoomName, Is.EqualTo("ambient"));
            Assert.That(removed[0].Username, Is.EqualTo("current-user"));
        });
    }
}
#endif
