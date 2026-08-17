using NUnit.Framework;
using Seeker;
using Seeker.Services;
using Seeker.Transfers;
using System;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class CoreExtractionTests
    {
        [Test]
        public void ParsingStatusState_TracksRootProgressAndResetsForANewParse()
        {
            var state = new ParsingStatusState
            {
                IsParsing = true,
            };

            state.BeginRoot("documents", 10);
            state.UpdateCurrentRoot(13);

            Assert.Multiple(() =>
            {
                Assert.That(state.GetCountForRoot("documents"), Is.EqualTo(3));
                Assert.That(state.IsRootComplete("documents"), Is.False);
            });

            state.EndRoot("documents", 15);
            state.LastParseResult = ParseResultFlags.Cancelled;

            Assert.Multiple(() =>
            {
                Assert.That(state.GetCountForRoot("documents"), Is.EqualTo(5));
                Assert.That(state.IsRootComplete("documents"), Is.True);
                Assert.That(state.WasCancelled, Is.True);
                Assert.That(state.FailedShareParse, Is.False);
            });

            state.IsParsing = true;

            Assert.Multiple(() =>
            {
                Assert.That(state.NumberParsed, Is.Zero);
                Assert.That(state.GetCountForRoot("documents"), Is.EqualTo(-1));
                Assert.That(state.IsRootComplete("documents"), Is.False);
            });
        }

        [Test]
        public void TransferDebouncer_IsActiveImmediatelyAfterTrigger()
        {
            TransferDebouncer.AbortAll.Trigger();

            Assert.That(TransferDebouncer.AbortAll.IsActive(), Is.True);
        }

        [Test]
        public void SavedStateSearchTabHeaderHelper_CopiesPortableTabMetadata()
        {
            var tab = new SearchTab
            {
                LastSearchTerm = "rare album",
                LastSearchResultsCount = 42,
                LastRanTime = new DateTime(2026, 8, 11, 12, 30, 0, DateTimeKind.Utc),
                UnseenCount = 3,
            };

            SavedStateSearchTabHeader state = SavedStateSearchTabHeaderHelper.GetSavedStateHeaderFromTab(tab);

            Assert.Multiple(() =>
            {
                Assert.That(state.LastSearchTerm, Is.EqualTo(tab.LastSearchTerm));
                Assert.That(state.LastSearchResultsCount, Is.EqualTo(tab.LastSearchResultsCount));
                Assert.That(state.LastRanTime, Is.EqualTo(tab.LastRanTime.Ticks));
                Assert.That(state.UnseenCount, Is.EqualTo(tab.UnseenCount));
            });
        }

        [TestCase("plain text", "plain text")]
        [TestCase("letter &#65;", "letter A")]
        [TestCase("emoji &#x1F600;", "emoji 😀")]
        public void HtmlUtilities_UnescapesNumericEntities(string encoded, string expected)
        {
            Assert.That(HTMLUtilities.unescapeHtml(encoded), Is.EqualTo(expected));
        }
    }
}
