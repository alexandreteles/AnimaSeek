using Seeker.Social;
using Soulseek;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UnitTestCommon;

[TestFixture]
public sealed class RoomSessionStateTests
{
    [Test]
    public void Messages_AreBoundedAndUnreadTracksVisibleRoom()
    {
        var state = new RoomSessionState(messageCapacity: 2);
        state.SetVisibleRoom("ambient");

        state.AddIncomingMessage("ambient", "one", "first", DateTime.UtcNow);
        Assert.That(state.GetSnapshot("AMBIENT")!.HasUnreadMessages, Is.False);

        state.SetVisibleRoom(null);
        state.AddIncomingMessage("ambient", "two", "second", DateTime.UtcNow);
        state.AddIncomingMessage("ambient", "three", "third", DateTime.UtcNow);

        RoomSessionSnapshot snapshot = state.GetSnapshot("ambient")!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HasUnreadMessages, Is.True);
            Assert.That(snapshot.Messages.Select(message => message.Message), Is.EqualTo(new[] { "second", "third" }));
        });

        state.SetVisibleRoom("Ambient");
        Assert.That(state.GetSnapshot("ambient")!.HasUnreadMessages, Is.False);
    }

    [Test]
    public void OutgoingDelivery_ReplacesRetainedEntryWithoutMutatingPriorSnapshot()
    {
        var state = new RoomSessionState();
        RoomMessageEntry pending = state.AddOutgoingMessage(
            "metal",
            "current-user",
            "hello",
            DateTime.UtcNow);
        RoomSessionSnapshot before = state.GetSnapshot("metal")!;

        bool updated = state.CompleteOutgoingMessage("metal", pending.Sequence, false, "network down");
        RoomSessionSnapshot after = state.GetSnapshot("metal")!;

        Assert.Multiple(() =>
        {
            Assert.That(updated, Is.True);
            Assert.That(before.Messages.Single().DeliveryState, Is.EqualTo(RoomMessageDeliveryState.Sending));
            Assert.That(after.Messages.Single().DeliveryState, Is.EqualTo(RoomMessageDeliveryState.Failed));
            Assert.That(after.Messages.Single().FailureReason, Is.EqualTo("network down"));
        });
    }

    [Test]
    public void MembersStatusesModeratorsAndTickers_ProduceDetachedCurrentSnapshot()
    {
        var state = new RoomSessionState(memberActivityCapacity: 2, tickerCapacity: 2);
        var initialUser = User("friend", UserPresence.Online);
        state.CompleteJoin(new RoomData(
            "private room",
            new[] { initialUser },
            isPrivate: true,
            owner: "owner",
            operatorList: new[] { "first-mod" }));

        state.AddMember("private room", User("new-user", UserPresence.Online), DateTime.UtcNow);
        state.UpdateMemberStatus(new UserStatus("friend", UserPresence.Away, false), DateTime.UtcNow);
        state.RemoveMember("private room", "new-user", DateTime.UtcNow);
        state.UpdateModerator("private room", "FIRST-MOD", added: false);
        state.UpdateModerator("private room", "second-mod", added: true);
        state.ReplaceTickers("private room", new[]
        {
            new RoomTicker("old", "old"),
            new RoomTicker("friend", "one"),
            new RoomTicker("other", "two"),
        });
        state.AddTicker("private room", new RoomTicker("FRIEND", "replacement"));

        RoomSessionSnapshot snapshot = state.GetSnapshot("private room")!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ConnectionState, Is.EqualTo(RoomConnectionState.Joined));
            Assert.That(snapshot.RoomData!.Users.Select(user => user.Username), Is.EqualTo(new[] { "friend" }));
            Assert.That(snapshot.RoomData.Users.Single().Status, Is.EqualTo(UserPresence.Away));
            Assert.That(snapshot.MemberActivity, Has.Count.EqualTo(2));
            Assert.That(snapshot.MemberActivity.Select(activity => activity.Kind),
                Is.EqualTo(new[] { RoomMemberActivityKind.WentAway, RoomMemberActivityKind.Left }));
            Assert.That(snapshot.RoomData.Operators, Is.EqualTo(new[] { "second-mod" }));
            Assert.That(snapshot.Tickers.Select(ticker => ticker.Username), Is.EqualTo(new[] { "other", "FRIEND" }));
            Assert.That(snapshot.Tickers.Last().Message, Is.EqualTo("replacement"));
            Assert.That(snapshot.IsOwnedBy("OWNER"), Is.True);
            Assert.That(snapshot.IsModeratedBy("SECOND-MOD"), Is.True);
        });
    }

    [Test]
    public void JoinFailureDisconnectAndLeave_PreserveHistoryWithExplicitStates()
    {
        var state = new RoomSessionState();
        state.BeginJoin("room");
        state.FailJoin("room", "forbidden", forbidden: true);
        Assert.That(state.GetSnapshot("room")!.ConnectionState, Is.EqualTo(RoomConnectionState.Forbidden));

        state.CompleteJoin(new RoomData("room", Array.Empty<UserData>()));
        state.AddIncomingMessage("room", "user", "retained", DateTime.UtcNow);
        Assert.That(state.MarkDisconnected(), Is.EqualTo(new[] { "room" }));
        Assert.That(state.GetSnapshot("room")!.ConnectionState, Is.EqualTo(RoomConnectionState.Disconnected));

        state.CompleteLeave("room");
        RoomSessionSnapshot snapshot = state.GetSnapshot("room")!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ConnectionState, Is.EqualTo(RoomConnectionState.NotJoined));
            Assert.That(snapshot.RoomData, Is.Null);
            Assert.That(snapshot.Messages.Single().Message, Is.EqualTo("retained"));
        });
    }

    [Test]
    public void ConcurrentCallbacks_KeepSnapshotsBoundedAndConsistent()
    {
        const int capacity = 32;
        var state = new RoomSessionState(messageCapacity: capacity, memberActivityCapacity: capacity);
        state.CompleteJoin(new RoomData("busy", Array.Empty<UserData>()));

        Parallel.For(0, 500, index =>
        {
            state.AddIncomingMessage("busy", $"user-{index}", $"message-{index}", DateTime.UtcNow);
            state.AddMember("busy", User($"member-{index}", UserPresence.Online), DateTime.UtcNow);
            state.RemoveMember("busy", $"member-{index}", DateTime.UtcNow);
            _ = state.Rooms;
        });

        RoomSessionSnapshot snapshot = state.GetSnapshot("busy")!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Messages, Has.Count.EqualTo(capacity));
            Assert.That(snapshot.Messages.Select(message => message.Sequence), Is.Ordered.And.Unique);
            Assert.That(snapshot.MemberActivity, Has.Count.EqualTo(capacity));
            Assert.That(snapshot.RoomData!.Users, Is.Empty);
        });
    }

    [TestCase(0, 1, 1)]
    [TestCase(1, 0, 1)]
    [TestCase(1, 1, 0)]
    public void Constructor_RejectsNonPositiveCapacity(int messages, int members, int tickers)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoomSessionState(messages, members, tickers));
    }

    private static UserData User(string username, UserPresence presence) =>
        new(username, presence, 1, 2, 3, 4, "US", 1);
}
