#if MOCK
using NUnit.Framework;
using Seeker;
using Seeker.Social;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnitTestCommon;

[TestFixture]
public sealed class WatchedUserReconciliationServiceTests
{
    [Test]
    public async Task ReconcileAsync_WatchesNewFriendsAndUnwatchesNewlyIgnoredFriends()
    {
        using var client = new MockSoulseekClient();
        client.StopBackgroundTimers();
        var watched = new List<string>();
        var unwatched = new List<string>();
        client.WatchUserAsyncHandler = (username, _) =>
        {
            watched.Add(username);
            return Task.FromResult(User(username));
        };
        client.UnwatchUserAsyncHandler = (username, _) =>
        {
            unwatched.Add(username);
            return Task.CompletedTask;
        };

        WatchedUserReconciliationResult result = await WatchedUserReconciliationService.ReconcileAsync(
            client,
            ["unchanged", "newly ignored", "Case Match"],
            ["case match", "new friend", "unchanged"]);

        Assert.Multiple(() =>
        {
            Assert.That(watched, Is.EqualTo(new[] { "new friend" }));
            Assert.That(unwatched, Is.EqualTo(new[] { "newly ignored" }));
            Assert.That(result.WatchedUsers.Select(data => data.Username), Is.EqualTo(new[] { "new friend" }));
            Assert.That(result.UnwatchedUsers, Is.EqualTo(new[] { "newly ignored" }));
            Assert.That(result.Failures, Is.Empty);
        });
    }

    [Test]
    public async Task ReconcileAsync_IsolatesNetworkFailuresAfterLocalCommit()
    {
        using var client = new MockSoulseekClient();
        client.StopBackgroundTimers();
        client.WatchUserAsyncHandler = (username, _) => username == "broken watch"
            ? Task.FromException<UserData>(new TimeoutException("watch failed"))
            : Task.FromResult(User(username));
        client.UnwatchUserAsyncHandler = (username, _) => username == "broken unwatch"
            ? Task.FromException(new InvalidOperationException("unwatch failed"))
            : Task.CompletedTask;

        WatchedUserReconciliationResult result = await WatchedUserReconciliationService.ReconcileAsync(
            client,
            ["broken unwatch", "removed"],
            ["added", "broken watch"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.WatchedUsers.Select(data => data.Username), Is.EqualTo(new[] { "added" }));
            Assert.That(result.UnwatchedUsers, Is.EqualTo(new[] { "removed" }));
            Assert.That(
                result.Failures.Select(failure => (failure.Username, failure.Operation)),
                Is.EqualTo(new[]
                {
                    ("broken watch", WatchedUserReconciliationOperation.Watch),
                    ("broken unwatch", WatchedUserReconciliationOperation.Unwatch),
                }));
        });
    }

    private static UserData User(string username) =>
        new(username, UserPresence.Online, 1, 2, 3, 4, "US");
}
#endif
