using Seeker;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Common.Search
{
    /// <summary>Pure scheduling and state-transition rules shared by platform wishlist runners.</summary>
    /// <remarks>
    /// <para>
    /// Tick base: <see cref="SavedStateSearchTabHeader.LastRanTime"/> is canonically UTC ticks going forward
    /// (written by <see cref="ApplyResult"/>). Historic Android builds wrote LOCAL ticks
    /// (<c>DateTime.Now.Ticks</c>), so persisted stores can contain a mix of both bases after an upgrade.
    /// <see cref="SelectNext"/> therefore normalizes on read: any stored value later than now-UTC plus a small
    /// clock-skew allowance can only be a legacy-local timestamp (local time runs ahead of UTC in positive-offset
    /// zones) and is clamped to now for ordering purposes. Stored values are never rewritten by the read path;
    /// a legacy value is replaced with UTC ticks only when its entry is next updated via <see cref="ApplyResult"/>.
    /// </para>
    /// </remarks>
    public static class WishlistStatePolicy
    {
        /// <summary>
        /// The clock drift tolerated before a stored <see cref="SavedStateSearchTabHeader.LastRanTime"/> in the
        /// future is treated as a legacy local-ticks value rather than genuine UTC.
        /// </summary>
        private static readonly TimeSpan LegacyLocalSkewAllowance = TimeSpan.FromMinutes(5);

        /// <summary>Gets the minimum permitted server wishlist cadence.</summary>
        public static TimeSpan MinimumInterval { get; } = TimeSpan.FromMinutes(2);

        /// <summary>Clamps a positive server interval to the app's two-minute safety floor.</summary>
        /// <param name="serverIntervalSeconds">The interval supplied by Soulseek, in seconds.</param>
        /// <returns>The usable interval, or <see langword="null"/> when the server disables wishlist searches.</returns>
        public static TimeSpan? NormalizeServerInterval(int? serverIntervalSeconds)
        {
            if (serverIntervalSeconds is null || serverIntervalSeconds <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(Math.Max(serverIntervalSeconds.Value, MinimumInterval.TotalSeconds));
        }

        /// <summary>Selects the least-recently-run non-full wishlist entry.</summary>
        /// <param name="headers">The canonical wishlist header dictionary.</param>
        /// <param name="utcNow">The current UTC time, or <see langword="null"/> to use the system clock.</param>
        /// <returns>The selected key/value pair, or <see langword="null"/> when nothing is eligible.</returns>
        /// <exception cref="ArgumentException"><paramref name="utcNow"/> is provided but not UTC.</exception>
        /// <remarks>
        /// Ordering normalizes mixed tick bases: a stored <see cref="SavedStateSearchTabHeader.LastRanTime"/>
        /// later than <paramref name="utcNow"/> plus <see cref="LegacyLocalSkewAllowance"/> is a legacy Android
        /// local-ticks value and is treated as having just run, without rewriting the stored value.
        /// </remarks>
        public static KeyValuePair<int, SavedStateSearchTabHeader>? SelectNext(
            IReadOnlyDictionary<int, SavedStateSearchTabHeader> headers,
            DateTime? utcNow = null)
        {
            if (headers == null)
            {
                throw new ArgumentNullException(nameof(headers));
            }

            if (utcNow.HasValue && utcNow.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Wishlist selection time must be UTC.", nameof(utcNow));
            }

            long utcNowTicks = (utcNow ?? DateTime.UtcNow).Ticks;
            return headers
                .Where(pair => pair.Key < 0)
                .Where(pair => pair.Value != null)
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.LastSearchTerm))
                .Where(pair => pair.Value.LastSearchResultsCount < WishlistUtil.MaxResultsPerWishlist)
                .OrderBy(pair => NormalizeLastRanTicks(pair.Value.LastRanTime, utcNowTicks))
                .ThenBy(pair => pair.Key)
                .Cast<KeyValuePair<int, SavedStateSearchTabHeader>?>()
                .FirstOrDefault();
        }

        /// <summary>Clamps a legacy local-ticks value that lies in the future to now, for ordering only.</summary>
        private static long NormalizeLastRanTicks(long storedTicks, long utcNowTicks) =>
            storedTicks > utcNowTicks + LegacyLocalSkewAllowance.Ticks ? utcNowTicks : storedTicks;

        /// <summary>Creates the next complete header after one search result snapshot.</summary>
        /// <param name="current">The header that was searched.</param>
        /// <param name="resultCount">The number of unique peer responses returned by this search.</param>
        /// <param name="utcNow">The completion time, which must be UTC.</param>
        /// <returns>The replacement header and number of newly unseen results.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="current"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="resultCount"/> is negative.</exception>
        /// <exception cref="ArgumentException"><paramref name="utcNow"/> is not UTC.</exception>
        public static (SavedStateSearchTabHeader Header, int NewResultCount) ApplyResult(
            SavedStateSearchTabHeader current,
            int resultCount,
            DateTime utcNow)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (resultCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resultCount), resultCount, "A result count cannot be negative.");
            }

            if (utcNow.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Wishlist completion time must be UTC.", nameof(utcNow));
            }

            int newResults = Math.Max(0, resultCount - Math.Max(0, current.LastSearchResultsCount));
            int unseen = current.UnseenCount > int.MaxValue - newResults
                ? int.MaxValue
                : Math.Max(0, current.UnseenCount) + newResults;
            SavedStateSearchTabHeader replacement = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                current.LastSearchTerm,
                resultCount,
                utcNow.Ticks,
                unseen);
            return (replacement, newResults);
        }
    }
}
