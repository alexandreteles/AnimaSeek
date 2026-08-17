using Common;
using Common.Search;
using NUnit.Framework;
using Seeker;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnitTestCommon;

[TestFixture]
public sealed class WishlistStateMutationGateTests
{
    [Test]
    public async Task ImportFirst_RefreshWaitsAndPreservesImportedEntry()
    {
        var store = CreateStore();
        var gate = new WishlistStateMutationGate();
        var importPrepared = NewSignal();
        var releaseImport = NewSignal();
        var refreshEntered = NewSignal();

        Task<ImportedSettingsMergeResult> import = gate.RunAsync(async _ =>
        {
            PreparedImportedSettingsMerge prepared = ImportedSettingsPersistence.Prepare(
                store,
                ["imported wish"],
                []);
            importPrepared.SetResult(true);
            await releaseImport.Task.ConfigureAwait(false);
            return ImportedSettingsPersistence.Apply(store, prepared);
        });
        await importPrepared.Task.ConfigureAwait(false);

        Task<bool> refresh = gate.RunAsync(() =>
        {
            refreshEntered.SetResult(true);
            ApplyRefreshResult(store, resultCount: 9);
            return true;
        });

        Assert.That(refreshEntered.Task.IsCompleted, Is.False, "The refresh entered while import held the mutation gate.");
        releaseImport.SetResult(true);
        await Task.WhenAll(import, refresh).ConfigureAwait(false);

        Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders(store);
        Assert.Multiple(() =>
        {
            Assert.That(headers.Values, Has.Some.Property(nameof(SavedStateSearchTabHeader.LastSearchTerm)).EqualTo("imported wish"));
            Assert.That(headers[-1].LastSearchResultsCount, Is.EqualTo(9));
        });
    }

    [Test]
    public async Task RefreshFirst_ImportWaitsAndPreservesRefreshResult()
    {
        var store = CreateStore();
        var gate = new WishlistStateMutationGate();
        var refreshEntered = NewSignal();
        var releaseRefresh = NewSignal();
        var importEntered = NewSignal();

        Task<bool> refresh = gate.RunAsync(async _ =>
        {
            refreshEntered.SetResult(true);
            await releaseRefresh.Task.ConfigureAwait(false);
            ApplyRefreshResult(store, resultCount: 12);
            return true;
        });
        await refreshEntered.Task.ConfigureAwait(false);

        Task<ImportedSettingsMergeResult> import = gate.RunAsync(() =>
        {
            importEntered.SetResult(true);
            return ImportedSettingsPersistence.Merge(store, ["imported wish"], []);
        });

        Assert.That(importEntered.Task.IsCompleted, Is.False, "The import entered while refresh held the mutation gate.");
        releaseRefresh.SetResult(true);
        await Task.WhenAll(refresh, import).ConfigureAwait(false);

        Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders(store);
        Assert.Multiple(() =>
        {
            Assert.That(headers.Values, Has.Some.Property(nameof(SavedStateSearchTabHeader.LastSearchTerm)).EqualTo("imported wish"));
            Assert.That(headers[-1].LastSearchResultsCount, Is.EqualTo(12));
        });
    }

    private static TestKeyValueStore CreateStore()
    {
        var store = new TestKeyValueStore();
        store.PutString(
            KeyConsts.M_SearchTabsState_Headers,
            SerializationHelper.SaveSavedStateHeaderDictToString(
                new Dictionary<int, SavedStateSearchTabHeader>
                {
                    [-1] = SavedStateSearchTabHeader.GetSavedStateHeaderFromTab(
                        "existing wish",
                        lastSearchResultsCount: 3,
                        lastRanTimeTicks: 1,
                        unseenCount: 0),
                }));
        return store;
    }

    private static void ApplyRefreshResult(TestKeyValueStore store, int resultCount)
    {
        Dictionary<int, SavedStateSearchTabHeader> headers = RestoreHeaders(store);
        (SavedStateSearchTabHeader replacement, _) = WishlistStatePolicy.ApplyResult(
            headers[-1],
            resultCount,
            DateTime.UtcNow);
        headers[-1] = replacement;
        store.PutString(
            KeyConsts.M_SearchTabsState_Headers,
            SerializationHelper.SaveSavedStateHeaderDictToString(headers));
    }

    private static Dictionary<int, SavedStateSearchTabHeader> RestoreHeaders(TestKeyValueStore store) =>
        SerializationHelper.RestoreSavedStateHeaderDictFromString(
            store.GetString(KeyConsts.M_SearchTabsState_Headers, string.Empty)!);

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
