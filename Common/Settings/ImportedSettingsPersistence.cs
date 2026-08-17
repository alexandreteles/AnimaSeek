using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Seeker.Services;

namespace Seeker
{
    /// <summary>
    /// Merges imported wishlist entries and user notes into Seeker's current portable persistence formats.
    /// </summary>
    /// <remarks>
    /// Existing values win case-insensitive conflicts so importing a backup cannot silently replace a user's
    /// current note or wishlist entry. New values are normalized and ordered before insertion, which makes ID
    /// assignment and conflict resolution deterministic regardless of source collection ordering.
    /// </remarks>
    public static class ImportedSettingsPersistence
    {
        /// <summary>Merges imported wishlist entries and user notes into <paramref name="store"/>.</summary>
        /// <param name="store">The portable preference store to read and update.</param>
        /// <param name="wishlistEntries">Wishlist search terms to merge.</param>
        /// <param name="userNotes">User-name and note pairs to merge.</param>
        /// <returns>The numbers of new wishlist entries and user notes persisted.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/>, <paramref name="wishlistEntries"/>, or <paramref name="userNotes"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">No negative wishlist identifier remains available.</exception>
        public static ImportedSettingsMergeResult Merge(
            IKeyValueStore store,
            IEnumerable<string> wishlistEntries,
            IEnumerable<Tuple<string, string>> userNotes)
        {
            PreparedImportedSettingsMerge prepared = Prepare(store, wishlistEntries, userNotes);
            return Apply(store, prepared);
        }

        /// <summary>
        /// Reads, validates, normalizes, and serializes an import without changing <paramref name="store"/>.
        /// </summary>
        /// <param name="store">The portable preference store containing the current canonical state.</param>
        /// <param name="wishlistEntries">Wishlist search terms to merge.</param>
        /// <param name="userNotes">User-name and note pairs to merge.</param>
        /// <returns>An immutable merge prepared for <see cref="Apply"/>.</returns>
        /// <remarks>
        /// Both canonical payloads are deserialized before imported values are processed. Callers that also mutate
        /// friend or ignore lists can therefore prepare first and avoid committing those lists when either payload
        /// is malformed.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/>, <paramref name="wishlistEntries"/>, or <paramref name="userNotes"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">No negative wishlist identifier remains available.</exception>
        public static PreparedImportedSettingsMerge Prepare(
            IKeyValueStore store,
            IEnumerable<string> wishlistEntries,
            IEnumerable<Tuple<string, string>> userNotes)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (wishlistEntries is null)
            {
                throw new ArgumentNullException(nameof(wishlistEntries));
            }

            if (userNotes is null)
            {
                throw new ArgumentNullException(nameof(userNotes));
            }

            string? serializedHeaders = store.GetString(Common.KeyConsts.M_SearchTabsState_Headers, string.Empty);
            Dictionary<int, SavedStateSearchTabHeader> headers = string.IsNullOrWhiteSpace(serializedHeaders)
                ? new Dictionary<int, SavedStateSearchTabHeader>()
                : SerializationHelper.RestoreSavedStateHeaderDictFromString(serializedHeaders);

            string? serializedNotes = store.GetString(Common.KeyConsts.M_UserNotes, string.Empty);
            ConcurrentDictionary<string, string> notes = string.IsNullOrWhiteSpace(serializedNotes)
                ? new ConcurrentDictionary<string, string>()
                : SerializationHelper.RestoreUserNotesFromString(serializedNotes);

            PreparedValue wishlist = PrepareWishlist(headers, wishlistEntries);
            PreparedValue userNoteMerge = PrepareUserNotes(notes, userNotes);
            return new PreparedImportedSettingsMerge(
                wishlist.Payload,
                userNoteMerge.Payload,
                new ImportedSettingsMergeResult(wishlist.AddedCount, userNoteMerge.AddedCount));
        }

        /// <summary>Applies a previously validated and serialized merge to <paramref name="store"/>.</summary>
        /// <param name="store">The portable preference store to update.</param>
        /// <param name="prepared">The immutable result returned by <see cref="Prepare"/>.</param>
        /// <returns>The numbers of new wishlist entries and user notes persisted.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> or <paramref name="prepared"/> is <see langword="null"/>.
        /// </exception>
        public static ImportedSettingsMergeResult Apply(
            IKeyValueStore store,
            PreparedImportedSettingsMerge prepared)
        {
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (prepared is null)
            {
                throw new ArgumentNullException(nameof(prepared));
            }

            if (prepared.WishlistPayload is not null)
            {
                store.PutString(Common.KeyConsts.M_SearchTabsState_Headers, prepared.WishlistPayload);
            }

            if (prepared.UserNotesPayload is not null)
            {
                store.PutString(Common.KeyConsts.M_UserNotes, prepared.UserNotesPayload);
            }

            return prepared.Result;
        }

        private static PreparedValue PrepareWishlist(
            Dictionary<int, SavedStateSearchTabHeader> headers,
            IEnumerable<string> importedEntries)
        {
            var existingTerms = headers.Values
                .Where(header => header is not null && !string.IsNullOrWhiteSpace(header.LastSearchTerm))
                .Select(header => header.LastSearchTerm.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] entriesToAdd = NormalizeStrings(importedEntries)
                .Where(existingTerms.Add)
                .ToArray();

            if (entriesToAdd.Length == 0)
            {
                return new PreparedValue(payload: null, addedCount: 0);
            }

            int nextIdentifier = headers.Keys.Where(identifier => identifier < 0).DefaultIfEmpty(0).Min();
            foreach (string entry in entriesToAdd)
            {
                if (nextIdentifier == int.MinValue)
                {
                    throw new InvalidOperationException("No negative wishlist identifier remains available.");
                }

                nextIdentifier--;
                headers.Add(
                    nextIdentifier,
                    SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                        entry,
                        lastSearchResultsCount: 0,
                        lastRanTimeTicks: DateTime.MinValue.Ticks,
                        unseenCount: 0));
            }

            return new PreparedValue(
                SerializationHelper.SaveSavedStateHeaderDictToString(headers),
                entriesToAdd.Length);
        }

        private static PreparedValue PrepareUserNotes(
            ConcurrentDictionary<string, string> restored,
            IEnumerable<Tuple<string, string>> importedNotes)
        {
            var merged = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> existing in restored
                         .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                merged.TryAdd(existing.Key.Trim(), existing.Value ?? string.Empty);
            }

            int added = 0;
            foreach (Tuple<string, string> imported in importedNotes
                         .Where(pair => pair is not null && !string.IsNullOrWhiteSpace(pair.Item1))
                         .OrderBy(pair => pair.Item1, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(pair => pair.Item1, StringComparer.Ordinal)
                         .ThenBy(pair => pair.Item2, StringComparer.Ordinal))
            {
                if (merged.TryAdd(imported.Item1.Trim(), imported.Item2 ?? string.Empty))
                {
                    added++;
                }
            }

            return added == 0
                ? new PreparedValue(payload: null, addedCount: 0)
                : new PreparedValue(SerializationHelper.SaveUserNotesToString(merged), added);
        }

        private static IEnumerable<string> NormalizeStrings(IEnumerable<string> values) =>
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value, StringComparer.Ordinal)
                .Distinct(StringComparer.OrdinalIgnoreCase);

        private sealed class PreparedValue
        {
            public PreparedValue(string? payload, int addedCount)
            {
                Payload = payload;
                AddedCount = addedCount;
            }

            public string? Payload { get; }

            public int AddedCount { get; }
        }
    }

    /// <summary>
    /// Holds a fully validated and serialized wishlist/user-note merge that has not yet changed persistence.
    /// </summary>
    public sealed class PreparedImportedSettingsMerge
    {
        internal PreparedImportedSettingsMerge(
            string? wishlistPayload,
            string? userNotesPayload,
            ImportedSettingsMergeResult result)
        {
            WishlistPayload = wishlistPayload;
            UserNotesPayload = userNotesPayload;
            Result = result;
        }

        internal string? WishlistPayload { get; }

        internal string? UserNotesPayload { get; }

        /// <summary>Gets the counts that will be reported after the prepared merge is applied.</summary>
        public ImportedSettingsMergeResult Result { get; }
    }

    /// <summary>Reports how many imported values were new to portable persistence.</summary>
    public sealed class ImportedSettingsMergeResult
    {
        /// <summary>Creates a result for one completed merge.</summary>
        /// <param name="wishlistAddedCount">The number of newly persisted wishlist terms.</param>
        /// <param name="userNoteAddedCount">The number of newly persisted user notes.</param>
        public ImportedSettingsMergeResult(int wishlistAddedCount, int userNoteAddedCount)
        {
            WishlistAddedCount = wishlistAddedCount;
            UserNoteAddedCount = userNoteAddedCount;
        }

        /// <summary>Gets the number of newly persisted wishlist terms.</summary>
        public int WishlistAddedCount { get; }

        /// <summary>Gets the number of newly persisted user notes.</summary>
        public int UserNoteAddedCount { get; }
    }
}
