using Common.Search;
using NUnit.Framework;
using Seeker;
using System;
using System.Collections.Generic;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class WishlistStatePolicyTests
    {
        [TestCase(null, null)]
        [TestCase(0, null)]
        [TestCase(-1, null)]
        [TestCase(1, 120)]
        [TestCase(119, 120)]
        [TestCase(120, 120)]
        [TestCase(300, 300)]
        public void NormalizeServerIntervalAppliesSafetyFloor(int? seconds, int? expectedSeconds)
        {
            TimeSpan? result = WishlistStatePolicy.NormalizeServerInterval(seconds);

            Assert.That(result?.TotalSeconds, Is.EqualTo(expectedSeconds));
        }

        [Test]
        public void SelectNextChoosesOldestEligibleCanonicalWishlist()
        {
            var headers = new Dictionary<int, SavedStateSearchTabHeader>
            {
                [-1] = Header("newer", 5, 20),
                [-2] = Header("full", WishlistUtil.MaxResultsPerWishlist, 1),
                [-3] = Header("oldest", 4, 10),
                [4] = Header("not a wishlist id", 0, 0),
            };

            KeyValuePair<int, SavedStateSearchTabHeader>? result = WishlistStatePolicy.SelectNext(headers);

            Assert.Multiple(() =>
            {
                Assert.That(result.HasValue, Is.True);
                Assert.That(result!.Value.Key, Is.EqualTo(-3));
                Assert.That(result.Value.Value.LastSearchTerm, Is.EqualTo("oldest"));
            });
        }

        [Test]
        public void SelectNextTreatsFutureLegacyLocalTicksAsJustRanWithoutRewritingThem()
        {
            DateTime utcNow = new DateTime(638900000000000000, DateTimeKind.Utc);
            // Written by a legacy Android build as LOCAL ticks in a UTC+13 zone ten hours ago: the raw
            // value sits three hours in the future relative to UTC now, so unnormalized ordering would
            // wrongly treat it as the most recently run entry.
            long legacyLocalTicks = utcNow.AddHours(3).Ticks;
            long olderUtcTicks = utcNow.AddHours(-1).Ticks;
            var headers = new Dictionary<int, SavedStateSearchTabHeader>
            {
                [-1] = Header("legacy local", 1, legacyLocalTicks),
                [-2] = Header("older utc", 1, olderUtcTicks),
            };

            KeyValuePair<int, SavedStateSearchTabHeader>? result = WishlistStatePolicy.SelectNext(headers, utcNow);

            Assert.Multiple(() =>
            {
                Assert.That(result!.Value.Key, Is.EqualTo(-2));
                Assert.That(headers[-1].LastRanTime, Is.EqualTo(legacyLocalTicks));
                Assert.That(headers[-2].LastRanTime, Is.EqualTo(olderUtcTicks));
            });
        }

        [Test]
        public void SelectNextClampsOnlyValuesBeyondTheSkewAllowance()
        {
            DateTime utcNow = new DateTime(638900000000000000, DateTimeKind.Utc);
            var headers = new Dictionary<int, SavedStateSearchTabHeader>
            {
                // Within the small skew allowance: genuine UTC from a slightly fast clock, kept as-is.
                [-1] = Header("small skew", 1, utcNow.AddMinutes(2).Ticks),
                // Beyond the allowance: legacy local ticks, clamped to now for ordering.
                [-2] = Header("legacy local", 1, utcNow.AddHours(3).Ticks),
            };

            KeyValuePair<int, SavedStateSearchTabHeader>? result = WishlistStatePolicy.SelectNext(headers, utcNow);

            Assert.That(result!.Value.Key, Is.EqualTo(-2));
        }

        [Test]
        public void SelectNextRejectsNonUtcSelectionTime()
        {
            var headers = new Dictionary<int, SavedStateSearchTabHeader>();

            Assert.Throws<ArgumentException>(() =>
                WishlistStatePolicy.SelectNext(headers, new DateTime(638900000000000000, DateTimeKind.Local)));
        }

        [Test]
        public void ApplyResultReturnsDetachedAtomicHeaderAndAccumulatesUnseen()
        {
            SavedStateSearchTabHeader current = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                "rare record",
                7,
                1,
                3);
            DateTime completion = new DateTime(638900000000000000, DateTimeKind.Utc);

            (SavedStateSearchTabHeader replacement, int newCount) =
                WishlistStatePolicy.ApplyResult(current, 11, completion);

            Assert.Multiple(() =>
            {
                Assert.That(newCount, Is.EqualTo(4));
                Assert.That(replacement, Is.Not.SameAs(current));
                Assert.That(replacement.LastSearchTerm, Is.EqualTo("rare record"));
                Assert.That(replacement.LastSearchResultsCount, Is.EqualTo(11));
                Assert.That(replacement.LastRanTime, Is.EqualTo(completion.Ticks));
                Assert.That(replacement.UnseenCount, Is.EqualTo(7));
                Assert.That(current.LastSearchResultsCount, Is.EqualTo(7));
            });
        }

        private static SavedStateSearchTabHeader Header(string term, int count, long ticks) =>
            SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(term, count, ticks, 0);
    }
}
