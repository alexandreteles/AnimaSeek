using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Common;
using NUnit.Framework;
using Seeker;
using Seeker.Services;

namespace UnitTestCommon;

[TestFixture]
public sealed class ImportedSettingsPersistenceTests
{
    [Test]
    public void Merge_PreservesExistingValuesAndAddsOnlyCaseInsensitiveNewValues()
    {
        var store = new InMemoryKeyValueStore();
        var existingHeaders = new Dictionary<int, SavedStateSearchTabHeader>
        {
            [-4] = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                "Existing Wish",
                lastSearchResultsCount: 7,
                lastRanTimeTicks: 123,
                unseenCount: 2),
        };
        store.PutString(
            KeyConsts.M_SearchTabsState_Headers,
            SerializationHelper.SaveSavedStateHeaderDictToString(existingHeaders));
        store.PutString(
            KeyConsts.M_UserNotes,
            SerializationHelper.SaveUserNotesToString(
                new ConcurrentDictionary<string, string>(
                    new[] { new KeyValuePair<string, string>("Alice", "current note") })));

        ImportedSettingsMergeResult result = ImportedSettingsPersistence.Merge(
            store,
            [" existing wish ", "New Wish", "new wish"],
            [Tuple.Create("alice", "backup note"), Tuple.Create("Bob", "new note"), Tuple.Create("bob", "other note")]);

        Dictionary<int, SavedStateSearchTabHeader> headers = SerializationHelper.RestoreSavedStateHeaderDictFromString(
            store.GetString(KeyConsts.M_SearchTabsState_Headers, string.Empty)!);
        ConcurrentDictionary<string, string> notes = SerializationHelper.RestoreUserNotesFromString(
            store.GetString(KeyConsts.M_UserNotes, string.Empty)!);

        Assert.Multiple(() =>
        {
            Assert.That(result.WishlistAddedCount, Is.EqualTo(1));
            Assert.That(result.UserNoteAddedCount, Is.EqualTo(1));
            Assert.That(headers.Keys, Is.EquivalentTo(new[] { -4, -5 }));
            Assert.That(headers[-4].LastSearchTerm, Is.EqualTo("Existing Wish"));
            Assert.That(headers[-4].LastSearchResultsCount, Is.EqualTo(7));
            Assert.That(headers[-5].LastSearchTerm, Is.EqualTo("New Wish"));
            Assert.That(notes.Count, Is.EqualTo(2));
            Assert.That(notes["Alice"], Is.EqualTo("current note"));
            Assert.That(notes["Bob"], Is.EqualTo("new note"));
        });
    }

    [Test]
    public void Merge_AssignsWishlistIdentifiersDeterministically()
    {
        var firstStore = new InMemoryKeyValueStore();
        var secondStore = new InMemoryKeyValueStore();

        ImportedSettingsPersistence.Merge(firstStore, ["zeta", "Alpha", "beta"], []);
        ImportedSettingsPersistence.Merge(secondStore, ["beta", "zeta", "Alpha"], []);

        Dictionary<int, SavedStateSearchTabHeader> first = RestoreHeaders(firstStore);
        Dictionary<int, SavedStateSearchTabHeader> second = RestoreHeaders(secondStore);
        Assert.That(
            first.ToDictionary(pair => pair.Key, pair => pair.Value.LastSearchTerm),
            Is.EqualTo(second.ToDictionary(pair => pair.Key, pair => pair.Value.LastSearchTerm)));
        Assert.That(
            first.OrderByDescending(pair => pair.Key).Select(pair => pair.Value.LastSearchTerm),
            Is.EqualTo(new[] { "Alpha", "beta", "zeta" }));
    }

    [Test]
    public void Merge_RejectsNullDependencies()
    {
        var store = new InMemoryKeyValueStore();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ImportedSettingsPersistence.Merge(null!, [], []));
            Assert.Throws<ArgumentNullException>(() => ImportedSettingsPersistence.Merge(store, null!, []));
            Assert.Throws<ArgumentNullException>(() => ImportedSettingsPersistence.Merge(store, [], null!));
        });
    }

    [TestCase(KeyConsts.M_SearchTabsState_Headers, TestName = "Malformed wishlist state is rejected before dependent commits")]
    [TestCase(KeyConsts.M_UserNotes, TestName = "Malformed note state is rejected before dependent commits")]
    public void Prepare_MalformedCanonicalPayloadDoesNotMutateStoreOrAllowDependentCommit(string malformedKey)
    {
        var store = new InMemoryKeyValueStore();
        store.PutString(KeyConsts.M_SearchTabsState_Headers, "{}");
        store.PutString(KeyConsts.M_UserNotes, "{}");
        store.PutString(KeyConsts.M_UserList, "friends-before-import");
        store.PutString(KeyConsts.M_IgnoreUserList, "ignored-before-import");
        store.PutString(malformedKey, "not-json");
        IReadOnlyDictionary<string, object> before = store.Snapshot();
        bool dependentUserListCommitCalled = false;

        Assert.Throws<JsonException>(() =>
        {
            PreparedImportedSettingsMerge prepared = ImportedSettingsPersistence.Prepare(
                store,
                ["new wish"],
                [Tuple.Create("alice", "new note")]);
            dependentUserListCommitCalled = true;
            ImportedSettingsPersistence.Apply(store, prepared);
        });

        Assert.Multiple(() =>
        {
            Assert.That(dependentUserListCommitCalled, Is.False);
            Assert.That(store.Snapshot(), Is.EqualTo(before));
        });
    }

    [Test]
    public void Merge_MalformedNotesDoesNotPartiallyWritePreparedWishlist()
    {
        var store = new InMemoryKeyValueStore();
        store.PutString(KeyConsts.M_SearchTabsState_Headers, "{}");
        store.PutString(KeyConsts.M_UserNotes, "not-json");
        IReadOnlyDictionary<string, object> before = store.Snapshot();

        Assert.Throws<JsonException>(() =>
            ImportedSettingsPersistence.Merge(
                store,
                ["new wish"],
                [Tuple.Create("alice", "new note")]));

        Assert.That(store.Snapshot(), Is.EqualTo(before));
    }

    private static Dictionary<int, SavedStateSearchTabHeader> RestoreHeaders(IKeyValueStore store) =>
        SerializationHelper.RestoreSavedStateHeaderDictFromString(
            store.GetString(KeyConsts.M_SearchTabsState_Headers, string.Empty)!);

    private sealed class InMemoryKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

        public bool GetBoolean(string key, bool defaultValue) => GetValue(key, defaultValue);

        public int GetInt(string key, int defaultValue) => GetValue(key, defaultValue);

        public long GetLong(string key, long defaultValue) => GetValue(key, defaultValue);

        public string? GetString(string key, string? defaultValue = null) => GetValue(key, defaultValue);

        public IReadOnlyCollection<string>? GetStringSet(
            string key,
            IReadOnlyCollection<string>? defaultValue = null) =>
            values.TryGetValue(key, out object? value)
                ? ((IReadOnlyCollection<string>)value).ToArray()
                : defaultValue;

        public void PutBoolean(string key, bool value) => values[key] = value;

        public void PutInt(string key, int value) => values[key] = value;

        public void PutLong(string key, long value) => values[key] = value;

        public void PutString(string key, string? value) => PutNullable(key, value);

        public void PutStringSet(string key, IReadOnlyCollection<string>? value) =>
            PutNullable(key, value?.Distinct(StringComparer.Ordinal).ToArray());

        public void Flush()
        {
        }

        public IReadOnlyDictionary<string, object> Snapshot() =>
            values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        private T GetValue<T>(string key, T defaultValue) =>
            values.TryGetValue(key, out object? value) ? (T)value : defaultValue;

        private void PutNullable(string key, object? value)
        {
            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }
        }
    }
}
