#if MOCK
using NUnit.Framework;
using Seeker;
using Soulseek;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnitTestCommon;

/// <summary>
/// Pins the two <see cref="ISoulseekClient"/> search overloads to the same observable contract.
/// The real client implements the collection overload by wrapping the callback overload, so a mock
/// whose overloads disagree lets a caller switch between them and silently lose every result.
/// </summary>
[TestFixture]
public sealed class MockSoulseekClientSearchTests
{
    // Curated, deterministic mock corpus; "t:0" removes the simulated inter-response delay.
    private const string CuratedQuery = "beethoven overture t:0";

    [Test]
    public async Task SearchAsync_WithResponseHandler_DeliversTheSameResponsesAsTheCollectionOverload()
    {
        using var collectionClient = new MockSoulseekClient { SimulatedDelayMs = 1 };
        collectionClient.StopBackgroundTimers();
        using var handlerClient = new MockSoulseekClient { SimulatedDelayMs = 1 };
        handlerClient.StopBackgroundTimers();

        SearchQuery query = SearchQuery.FromText(CuratedQuery);
        (_, IReadOnlyCollection<SearchResponse> expected) = await collectionClient.SearchAsync(query);

        var delivered = new List<SearchResponse>();
        Soulseek.Search completed = await handlerClient.SearchAsync(
            query,
            response =>
            {
                lock (delivered)
                {
                    delivered.Add(response);
                }
            });

        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.Not.Empty, "the curated mock corpus must produce responses");
            Assert.That(
                delivered.Select(response => response.Username),
                Is.EquivalentTo(expected.Select(response => response.Username)),
                "the callback overload must publish every response the collection overload returns");
            Assert.That(
                delivered.Sum(response => response.FileCount),
                Is.EqualTo(expected.Sum(response => response.FileCount)));
            Assert.That(completed.ResponseCount, Is.EqualTo(expected.Count));
            Assert.That(completed.State.HasFlag(SearchStates.Completed), Is.True);
        });
    }

    [Test]
    public async Task SearchAsync_WithResponseHandler_AlsoInvokesTheOptionsResponseReceivedCallback()
    {
        using var client = new MockSoulseekClient { SimulatedDelayMs = 1 };
        client.StopBackgroundTimers();

        var viaOptions = new List<SearchResponse>();
        var viaHandler = new List<SearchResponse>();
        var options = new SearchOptions(responseReceived: received =>
        {
            lock (viaOptions)
            {
                viaOptions.Add(received.Response);
            }
        });

        await client.SearchAsync(
            SearchQuery.FromText(CuratedQuery),
            response =>
            {
                lock (viaHandler)
                {
                    viaHandler.Add(response);
                }
            },
            options: options);

        Assert.Multiple(() =>
        {
            Assert.That(viaHandler, Is.Not.Empty);
            Assert.That(viaOptions.Count, Is.EqualTo(viaHandler.Count));
        });
    }

    [Test]
    public async Task SearchAsync_WithResponseHandler_DeliversGeneratedResponsesForAnArbitraryQuery()
    {
        using var client = new MockSoulseekClient { SimulatedDelayMs = 1 };
        client.StopBackgroundTimers();

        var delivered = new List<SearchResponse>();
        Soulseek.Search completed = await client.SearchAsync(
            SearchQuery.FromText("some arbitrary album n:5 t:0"),
            response =>
            {
                lock (delivered)
                {
                    delivered.Add(response);
                }
            });

        Assert.Multiple(() =>
        {
            Assert.That(delivered, Has.Count.EqualTo(5));
            Assert.That(completed.ResponseCount, Is.EqualTo(5));
        });
    }
}
#endif
