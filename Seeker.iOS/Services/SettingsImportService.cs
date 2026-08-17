using Foundation;
using Common;
using Common.Search;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using UIKit;
using UniformTypeIdentifiers;

namespace AnimaSeek.iOS.Services;

/// <summary>Imports Seeker, SoulseekQt, or Nicotine settings selected from Files or opened through a share sheet.</summary>
internal sealed class SettingsImportService
{
    private const string LegacyWishlistKey = "ios.wishlist";
    private readonly IosUserListService userListService;
    private readonly IKeyValueStore keyValueStore;
    private readonly IMainThreadRunner mainThreadRunner;
    private readonly PortableAppDataStateService appDataStateService;
    private readonly ILoggerBackend logger;
    private readonly WishlistStateMutationGate wishlistMutationGate;
    private readonly IosSocialSessionService socialSessionService;
    private UIDocumentPickerDelegate? activePickerDelegate;

    /// <summary>Creates an import service over the portable parser and iOS persistence services.</summary>
    /// <param name="userListService">The destination for friend and ignore lists.</param>
    /// <param name="keyValueStore">The destination for wishlist entries and user notes.</param>
    /// <param name="mainThreadRunner">The service used to deliver completion on the UIKit thread.</param>
    /// <param name="appDataStateService">Reloads the newly merged canonical metadata into the live process.</param>
    /// <param name="logger">Records isolated live-state reload failures without failing a healthy import.</param>
    /// <param name="wishlistMutationGate">Serializes the import with server-driven wishlist mutations.</param>
    /// <param name="socialSessionService">Reconciles connected server watches after the local commit.</param>
    public SettingsImportService(
        IosUserListService userListService,
        IKeyValueStore keyValueStore,
        IMainThreadRunner mainThreadRunner,
        PortableAppDataStateService appDataStateService,
        ILoggerBackend logger,
        WishlistStateMutationGate wishlistMutationGate,
        IosSocialSessionService socialSessionService)
    {
        this.userListService = userListService ?? throw new ArgumentNullException(nameof(userListService));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.mainThreadRunner = mainThreadRunner ?? throw new ArgumentNullException(nameof(mainThreadRunner));
        this.appDataStateService = appDataStateService ?? throw new ArgumentNullException(nameof(appDataStateService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.wishlistMutationGate = wishlistMutationGate ?? throw new ArgumentNullException(nameof(wishlistMutationGate));
        this.socialSessionService = socialSessionService ?? throw new ArgumentNullException(nameof(socialSessionService));
    }

    /// <summary>Creates a Files picker and retains its weak delegate through completion.</summary>
    /// <param name="completion">Receives either a summary or the parser exception.</param>
    /// <returns>A configured document picker that imports a copy into the sandbox.</returns>
    public UIDocumentPickerViewController CreateDocumentPicker(Action<SettingsImportResult?, Exception?> completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var picker = new UIDocumentPickerViewController([UTTypes.Data], asCopy: true)
        {
            AllowsMultipleSelection = false,
        };
        activePickerDelegate = new ImportPickerDelegate(this, completion);
        picker.Delegate = activePickerDelegate;
        return picker;
    }

    /// <summary>Creates a Files picker that parses a document without changing application state.</summary>
    /// <param name="completion">Receives either a review-safe preview, a parser error, or two nulls after cancellation.</param>
    /// <param name="cancellationToken">Cancels parsing when the owning review is dismissed or superseded.</param>
    /// <returns>A configured single-document picker that imports a sandbox copy.</returns>
    public UIDocumentPickerViewController CreatePreviewDocumentPicker(
        Action<SettingsImportPreview?, Exception?> completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var picker = new UIDocumentPickerViewController([UTTypes.Data], asCopy: true)
        {
            AllowsMultipleSelection = false,
        };
        activePickerDelegate = new PreviewPickerDelegate(this, completion, cancellationToken);
        picker.Delegate = activePickerDelegate;
        return picker;
    }

    /// <summary>Parses and normalizes an import document without mutating preferences or live state.</summary>
    /// <param name="url">A local or security-scoped document URL.</param>
    /// <param name="cancellationToken">Cancels file validation or parsing.</param>
    /// <returns>A detached preview suitable for user review.</returns>
    public Task<SettingsImportPreview> ParsePreviewAsync(
        NSUrl url,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool scoped = url.StartAccessingSecurityScopedResource();
            try
            {
                string path = url.Path ?? throw new InvalidDataException("The selected document has no local path.");
                ValidateSourceSize(path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                ImportedData data = Normalize(ImportHelper.ImportFile(Path.GetFileName(path), stream));
                return CreatePreview(Path.GetFileName(path), data);
            }
            finally
            {
                if (scoped)
                {
                    url.StopAccessingSecurityScopedResource();
                }
            }
        }, cancellationToken);
    }

    /// <summary>Atomically merges only the categories selected on an import preview.</summary>
    /// <param name="preview">The detached parsed preview.</param>
    /// <param name="selection">The categories the person chose to merge.</param>
    /// <param name="cancellationToken">Cancels before the durable merge begins.</param>
    /// <returns>Counts of newly imported portable entities.</returns>
    public async Task<SettingsImportResult> CommitSelectedAsync(
        SettingsImportPreview preview,
        SettingsImportSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!selection.Any)
        {
            return new SettingsImportResult(0, 0, 0, 0, 0);
        }

        ImportedData selected = preview.CreateSelectedData(selection);
        SettingsImportCommit commit = await wishlistMutationGate.RunAsync(
            () => Apply(selected),
            cancellationToken).ConfigureAwait(false);
        int watchFailures = await socialSessionService
            .ReconcileImportedFriendsAsync(commit.PreviousFriendNames)
            .ConfigureAwait(false);
        return new SettingsImportResult(
            commit.FriendCount,
            commit.IgnoredCount,
            commit.WishlistCount,
            commit.UserNoteCount,
            watchFailures);
    }

    /// <summary>Parses and applies an import document URL.</summary>
    /// <param name="url">A local or security-scoped document URL.</param>
    /// <param name="cancellationToken">Cancels before the parser starts applying data.</param>
    /// <returns>Counts of imported portable entities.</returns>
    public Task<SettingsImportResult> ImportAsync(NSUrl url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return ImportAllAsync(url, cancellationToken);
    }

    /// <summary>Runs the backwards-compatible parse-and-commit path with every category selected.</summary>
    /// <param name="url">The import document URL.</param>
    /// <param name="cancellationToken">Cancels parsing or commit.</param>
    /// <returns>Counts of newly imported portable entities.</returns>
    private async Task<SettingsImportResult> ImportAllAsync(NSUrl url, CancellationToken cancellationToken)
    {
        SettingsImportPreview preview = await ParsePreviewAsync(url, cancellationToken).ConfigureAwait(false);
        return await CommitSelectedAsync(
            preview,
            SettingsImportSelection.All(preview),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rejects source files larger than the format-specific parser limit.</summary>
    /// <param name="path">The resolved local source path.</param>
    private static void ValidateSourceSize(string path)
    {
        long sourceLength = new FileInfo(path).Length;
        int maximumSourceBytes = ImportHelper.GetMaximumSourceBytes(path);
        if (sourceLength <= maximumSourceBytes)
        {
            return;
        }

        int maximumMebibytes = maximumSourceBytes / (1024 * 1024);
        throw new InvalidDataException(
            $"The selected import file exceeds the {maximumMebibytes} MiB safety limit.");
    }

    /// <summary>Normalizes parsed values once so preview and commit describe identical data.</summary>
    /// <param name="data">The parser output.</param>
    /// <returns>A normalized, detached portable import value.</returns>
    private static ImportedData Normalize(ImportedData data)
    {
        List<string> ignored = NormalizeUsernames(data.IgnoredBanned ?? []);
        HashSet<string> ignoredNames = ignored.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> friends = NormalizeUsernames(data.UserList ?? [])
            .Where(username => !ignoredNames.Contains(username))
            .ToList();
        List<Tuple<string, string>> notes = (data.UserNotes ?? [])
            .Where(pair => pair is not null && !string.IsNullOrWhiteSpace(pair.Item1))
            .GroupBy(pair => pair.Item1.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => Tuple.Create(group.Key, group.Last().Item2 ?? string.Empty))
            .OrderBy(pair => pair.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ImportedData(friends, ignored, NormalizeValues(data.Wishlist ?? []), notes);
    }

    /// <summary>Computes already-present counts without mutating canonical state.</summary>
    /// <param name="sourceName">The selected document's display name.</param>
    /// <param name="data">Normalized parsed data.</param>
    /// <returns>A detached review model.</returns>
    private SettingsImportPreview CreatePreview(string sourceName, ImportedData data)
    {
        IosUserListEntrySnapshot[] users = userListService.GetSnapshot().ToArray();
        HashSet<string> friends = users
            .Where(item => item.Role == Seeker.UserRole.Friend)
            .Select(item => item.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ignored = users
            .Where(item => item.Role == Seeker.UserRole.Ignored)
            .Select(item => item.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> wishlist = GetExistingWishlistTerms();
        HashSet<string> userNotes = UserMetadataService.UserNotes.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SettingsImportPreview(
            sourceName,
            data,
            data.UserList.Count,
            data.UserList.Count(friends.Contains),
            data.IgnoredBanned.Count,
            data.IgnoredBanned.Count(ignored.Contains),
            data.Wishlist.Count,
            data.Wishlist.Count(wishlist.Contains),
            data.UserNotes.Count,
            data.UserNotes.Count(pair => userNotes.Contains(pair.Item1)),
            friends,
            ignored,
            wishlist,
            userNotes);
    }

    /// <summary>Reads canonical and legacy wishlist terms for an accurate, nonmutating preview diff.</summary>
    /// <returns>Normalized existing wishlist terms; malformed canonical state contributes no terms.</returns>
    private HashSet<string> GetExistingWishlistTerms()
    {
        var terms = new HashSet<string>(
            keyValueStore.GetStringSet(LegacyWishlistKey) ?? [],
            StringComparer.OrdinalIgnoreCase);
        try
        {
            string? payload = keyValueStore.GetString(KeyConsts.M_SearchTabsState_Headers, string.Empty);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                foreach (SavedStateSearchTabHeader header in
                         SerializationHelper.RestoreSavedStateHeaderDictFromString(payload).Values)
                {
                    if (header is not null && !string.IsNullOrWhiteSpace(header.LastSearchTerm))
                    {
                        terms.Add(header.LastSearchTerm.Trim());
                    }
                }
            }
        }
        catch (Exception exception)
        {
            logger.FirebaseError("Could not read canonical wishlist terms while preparing an import preview.", exception);
        }

        return terms;
    }

    private SettingsImportCommit Apply(ImportedData data)
    {
        IosUserListRollbackSnapshot userListSnapshot = userListService.CaptureRollbackSnapshot();
        string[] previousFriendNames = userListSnapshot.Friends
            .Select(item => item.Username)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rollbackSnapshot = new ImportRollbackSnapshot(
            userListSnapshot,
            keyValueStore.GetString(KeyConsts.M_SearchTabsState_Headers),
            keyValueStore.GetString(KeyConsts.M_UserNotes),
            keyValueStore.GetStringSet(LegacyWishlistKey)?.ToArray());

        List<string> ignored = NormalizeUsernames(data.IgnoredBanned ?? []);
        var ignoredNames = ignored.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> friends = NormalizeUsernames(data.UserList ?? [])
            .Where(username => !ignoredNames.Contains(username) && !userListService.IsUserInIgnoreList(username))
            .ToList();
        List<string> wishlist = NormalizeValues(data.Wishlist ?? []);
        List<Tuple<string, string>> userNotes = (data.UserNotes ?? [])
            .Where(pair => pair is not null && !string.IsNullOrWhiteSpace(pair.Item1))
            .ToList();

        IReadOnlyCollection<string> legacyWishlist = keyValueStore.GetStringSet(LegacyWishlistKey) ?? [];
        PreparedImportedSettingsMerge preparedMetadata = ImportedSettingsPersistence.Prepare(
            keyValueStore,
            legacyWishlist.Concat(wishlist),
            userNotes);

        try
        {
            // Validate both metadata payloads before changing any canonical list. The rollback snapshot below also
            // covers later persistence failures so UIKit never observes a successful half-import.
            (int friendsAdded, int ignoredAdded) = userListService.ImportForTransaction(friends, ignored);
            ImportedSettingsMergeResult merged = ImportedSettingsPersistence.Apply(keyValueStore, preparedMetadata);
            keyValueStore.PutStringSet(LegacyWishlistKey, null);
            keyValueStore.Flush();
            foreach (AppDataRestoreFailure failure in appDataStateService.ReloadUserMetadata())
            {
                logger.FirebaseError($"Imported metadata reload failed for '{failure.Key}'.", failure.Exception);
            }

            userListService.PublishTransactionalChange();

            return new SettingsImportCommit(
                friendsAdded,
                ignoredAdded,
                merged.WishlistAddedCount,
                merged.UserNoteAddedCount,
                previousFriendNames);
        }
        catch (Exception commitException)
        {
            RestoreAfterFailedCommit(rollbackSnapshot, commitException);
            throw;
        }
    }

    /// <summary>Restores all live and persisted state captured before a failed multi-store commit.</summary>
    /// <param name="snapshot">The pre-commit state.</param>
    /// <param name="commitException">The failure that triggered rollback, used for correlated diagnostics.</param>
    private void RestoreAfterFailedCommit(ImportRollbackSnapshot snapshot, Exception commitException)
    {
        try
        {
            keyValueStore.PutString(KeyConsts.M_SearchTabsState_Headers, snapshot.WishlistPayload);
            keyValueStore.PutString(KeyConsts.M_UserNotes, snapshot.UserNotesPayload);
            keyValueStore.PutStringSet(LegacyWishlistKey, snapshot.LegacyWishlist);
            keyValueStore.Flush();
            foreach (AppDataRestoreFailure failure in appDataStateService.ReloadUserMetadata())
            {
                logger.FirebaseError($"Metadata rollback reload failed for '{failure.Key}'.", failure.Exception);
            }
        }
        catch (Exception rollbackException)
        {
            logger.FirebaseError(
                "Settings import failed and its metadata rollback could not be persisted.",
                new AggregateException(commitException, rollbackException));
        }

        try
        {
            userListService.RestoreRollbackSnapshot(snapshot.UserLists);
        }
        catch (Exception rollbackException)
        {
            logger.FirebaseError(
                "Settings import failed and the persisted user-list rollback also failed; live membership was restored.",
                new AggregateException(commitException, rollbackException));
        }
    }

    private static List<string> NormalizeUsernames(IEnumerable<string> usernames) =>
        NormalizeValues(usernames);

    private static List<string> NormalizeValues(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Holds the live and portable values needed to reverse an interrupted import commit.</summary>
    private sealed record ImportRollbackSnapshot(
        IosUserListRollbackSnapshot UserLists,
        string? WishlistPayload,
        string? UserNotesPayload,
        IReadOnlyCollection<string>? LegacyWishlist);

    private void CompletePicker(
        UIDocumentPickerDelegate sender,
        Action<SettingsImportResult?, Exception?> completion,
        SettingsImportResult? result,
        Exception? exception)
    {
        if (ReferenceEquals(activePickerDelegate, sender))
        {
            activePickerDelegate = null;
        }

        mainThreadRunner.RunOnUiThread(() => completion(result, exception));
    }

    /// <summary>Releases the picker delegate and delivers a preview result on the main thread.</summary>
    /// <param name="completion">The picker completion callback.</param>
    /// <param name="preview">The parsed preview, when successful.</param>
    /// <param name="exception">The parser failure, when unsuccessful.</param>
    private void CompletePreviewPicker(
        UIDocumentPickerDelegate sender,
        Action<SettingsImportPreview?, Exception?> completion,
        SettingsImportPreview? preview,
        Exception? exception)
    {
        if (ReferenceEquals(activePickerDelegate, sender))
        {
            activePickerDelegate = null;
        }

        mainThreadRunner.RunOnUiThread(() => completion(preview, exception));
    }

    private sealed class ImportPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly SettingsImportService owner;
        private readonly Action<SettingsImportResult?, Exception?> completion;

        public ImportPickerDelegate(
            SettingsImportService owner,
            Action<SettingsImportResult?, Exception?> completion)
        {
            this.owner = owner;
            this.completion = completion;
        }

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            NSUrl? url = urls.FirstOrDefault();
            if (url is null)
            {
                owner.CompletePicker(this, completion, null, new InvalidDataException("No document was selected."));
                return;
            }

            _ = ImportAsync(url);
        }

        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            owner.CompletePicker(this, completion, null, null);

        private async Task ImportAsync(NSUrl url)
        {
            try
            {
                SettingsImportResult result = await owner.ImportAsync(url);
                owner.CompletePicker(this, completion, result, null);
            }
            catch (Exception exception)
            {
                owner.CompletePicker(this, completion, null, exception);
            }
        }
    }

    /// <summary>Owns one review picker callback until selection, cancellation, or parsing completes.</summary>
    private sealed class PreviewPickerDelegate : UIDocumentPickerDelegate
    {
        private readonly SettingsImportService owner;
        private readonly Action<SettingsImportPreview?, Exception?> completion;
        private readonly CancellationToken cancellationToken;

        /// <summary>Creates a retained preview-picker delegate.</summary>
        /// <param name="owner">The import service.</param>
        /// <param name="completion">The caller's completion callback.</param>
        public PreviewPickerDelegate(
            SettingsImportService owner,
            Action<SettingsImportPreview?, Exception?> completion,
            CancellationToken cancellationToken)
        {
            this.owner = owner;
            this.completion = completion;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>Parses the first selected file for review.</summary>
        /// <param name="controller">The presenting picker.</param>
        /// <param name="urls">The copied document URLs.</param>
        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            NSUrl? url = urls.FirstOrDefault();
            if (url is null)
            {
                owner.CompletePreviewPicker(
                    this,
                    completion,
                    null,
                    new InvalidDataException("No document was selected."));
                return;
            }

            _ = ParseAsync(url);
        }

        /// <summary>Completes without a result when the person cancels the picker.</summary>
        /// <param name="controller">The canceled picker.</param>
        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            owner.CompletePreviewPicker(this, completion, null, null);

        /// <summary>Runs parsing and maps its completion back to the retained callback.</summary>
        /// <param name="url">The selected copied document URL.</param>
        private async Task ParseAsync(NSUrl url)
        {
            try
            {
                SettingsImportPreview preview = await owner.ParsePreviewAsync(url, cancellationToken);
                owner.CompletePreviewPicker(this, completion, preview, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                owner.CompletePreviewPicker(this, completion, null, null);
            }
            catch (Exception exception)
            {
                owner.CompletePreviewPicker(this, completion, null, exception);
            }
        }
    }
}

/// <summary>Reports the portable entity counts applied by one settings import.</summary>
/// <param name="FriendCount">The number of new friend names added.</param>
/// <param name="IgnoredCount">The number of new ignored names added.</param>
/// <param name="WishlistCount">The number of new wishlist entries added.</param>
/// <param name="UserNoteCount">The number of new user notes added.</param>
/// <param name="WatchReconciliationFailureCount">
/// The number of connected server watch operations that will retry after reconnect.
/// </param>
internal sealed record SettingsImportResult(
    int FriendCount,
    int IgnoredCount,
    int WishlistCount,
    int UserNoteCount,
    int WatchReconciliationFailureCount);

internal sealed record SettingsImportCommit(
    int FriendCount,
    int IgnoredCount,
    int WishlistCount,
    int UserNoteCount,
    IReadOnlyCollection<string> PreviousFriendNames);

/// <summary>Names a stable, selectable category in a settings-import preview.</summary>
internal enum SettingsImportCategory
{
    Friends,
    IgnoredUsers,
    Wishlist,
    UserNotes,
}

/// <summary>Describes the explicit none, mixed, or all state of a selectable import collection.</summary>
internal enum SettingsImportSelectionState
{
    None,
    Some,
    All,
}

/// <summary>
/// Holds an immutable-by-convention, case-insensitive selection of individual reviewed import entries.
/// </summary>
internal sealed class SettingsImportSelection
{
    private readonly HashSet<string> friends;
    private readonly HashSet<string> ignoredUsers;
    private readonly HashSet<string> wishlist;
    private readonly HashSet<string> userNotes;

    private SettingsImportSelection(
        IEnumerable<string> friends,
        IEnumerable<string> ignoredUsers,
        IEnumerable<string> wishlist,
        IEnumerable<string> userNotes)
    {
        this.friends = friends.ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.ignoredUsers = ignoredUsers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.wishlist = wishlist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.userNotes = userNotes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets an empty per-item selection.</summary>
    public static SettingsImportSelection Empty { get; } = new([], [], [], []);

    /// <summary>Creates a selection containing every item in a detached preview.</summary>
    /// <param name="preview">The preview whose exact normalized entries should be selected.</param>
    /// <returns>A detached selection containing all four supported categories.</returns>
    public static SettingsImportSelection All(SettingsImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new SettingsImportSelection(
            preview.ItemKeys(SettingsImportCategory.Friends),
            preview.ItemKeys(SettingsImportCategory.IgnoredUsers),
            preview.ItemKeys(SettingsImportCategory.Wishlist),
            preview.ItemKeys(SettingsImportCategory.UserNotes));
    }

    /// <summary>Creates the default review selection containing only entries that are not already present.</summary>
    /// <param name="preview">The preview whose new entries should be selected.</param>
    /// <returns>A detached selection that excludes explicit duplicate rows.</returns>
    public static SettingsImportSelection Selectable(SettingsImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new SettingsImportSelection(
            preview.SelectableItemKeys(SettingsImportCategory.Friends),
            preview.SelectableItemKeys(SettingsImportCategory.IgnoredUsers),
            preview.SelectableItemKeys(SettingsImportCategory.Wishlist),
            preview.SelectableItemKeys(SettingsImportCategory.UserNotes));
    }

    /// <summary>Gets whether at least one reviewed entry is selected.</summary>
    public bool Any => Count > 0;

    /// <summary>Gets the number of individually selected entries across every category.</summary>
    public int Count => friends.Count + ignoredUsers.Count + wishlist.Count + userNotes.Count;

    /// <summary>Gets whether an item is selected in a category using import normalization semantics.</summary>
    /// <param name="category">The item's import category.</param>
    /// <param name="key">The normalized value, or username key for a user note.</param>
    /// <returns><see langword="true"/> when the item will be committed.</returns>
    public bool Contains(SettingsImportCategory category, string key) => Values(category).Contains(key);

    /// <summary>Gets the number of selected items in one category.</summary>
    /// <param name="category">The import category.</param>
    /// <returns>The selected item count.</returns>
    public int CountFor(SettingsImportCategory category) => Values(category).Count;

    /// <summary>Returns a detached selection with one item selected or cleared.</summary>
    /// <param name="category">The item's category.</param>
    /// <param name="key">The normalized value, or username key for a user note.</param>
    /// <param name="selected">Whether the item should be included in the atomic commit.</param>
    /// <returns>A new selection; the current instance remains unchanged.</returns>
    public SettingsImportSelection Set(
        SettingsImportCategory category,
        string key,
        bool selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        SettingsImportSelection result = Clone();
        if (selected)
        {
            result.Values(category).Add(key);
        }
        else
        {
            result.Values(category).Remove(key);
        }

        return result;
    }

    /// <summary>Returns a detached selection with every supplied item in one category selected or cleared.</summary>
    /// <param name="category">The category to update.</param>
    /// <param name="keys">The normalized values represented by the visible review.</param>
    /// <param name="selected">Whether all supplied values should be included.</param>
    /// <returns>A new selection; the current instance remains unchanged.</returns>
    public SettingsImportSelection SetAll(
        SettingsImportCategory category,
        IEnumerable<string> keys,
        bool selected)
    {
        ArgumentNullException.ThrowIfNull(keys);
        SettingsImportSelection result = Clone();
        HashSet<string> values = result.Values(category);
        if (selected)
        {
            values.UnionWith(keys.Where(static key => !string.IsNullOrWhiteSpace(key)));
        }
        else
        {
            values.ExceptWith(keys);
        }

        return result;
    }

    /// <summary>Computes the explicit tri-state value for one category or the complete preview.</summary>
    /// <param name="category">The category to inspect, or <see langword="null"/> for all categories.</param>
    /// <param name="preview">The preview defining the complete available item set.</param>
    /// <returns>None, Some, or All according to the exact item counts.</returns>
    public SettingsImportSelectionState State(
        SettingsImportCategory? category,
        SettingsImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        int available = category is { } value
            ? preview.SelectableItemCount(value)
            : preview.TotalSelectableItemCount;
        int selected = category is { } selectedCategory ? CountFor(selectedCategory) : Count;
        return selected switch
        {
            0 => SettingsImportSelectionState.None,
            _ when available > 0 && selected >= available => SettingsImportSelectionState.All,
            _ => SettingsImportSelectionState.Some,
        };
    }

    private SettingsImportSelection Clone() => new(friends, ignoredUsers, wishlist, userNotes);

    private HashSet<string> Values(SettingsImportCategory category) => category switch
    {
        SettingsImportCategory.Friends => friends,
        SettingsImportCategory.IgnoredUsers => ignoredUsers,
        SettingsImportCategory.Wishlist => wishlist,
        SettingsImportCategory.UserNotes => userNotes,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}

/// <summary>Describes normalized import data and already-present counts before any mutation occurs.</summary>
/// <param name="SourceName">The selected document's display name.</param>
/// <param name="Data">The normalized portable parser result retained for commit.</param>
/// <param name="FriendCount">The number of friend entries in the file.</param>
/// <param name="FriendsAlreadyPresent">The number of friend entries already stored.</param>
/// <param name="IgnoredCount">The number of ignored entries in the file.</param>
/// <param name="IgnoredAlreadyPresent">The number of ignored entries already stored.</param>
/// <param name="WishlistCount">The number of wishlist entries in the file.</param>
/// <param name="WishlistAlreadyPresent">The number of wishlist entries already stored.</param>
/// <param name="UserNoteCount">The number of note entries in the file.</param>
/// <param name="UserNotesAlreadyPresent">The number of note keys already stored.</param>
/// <param name="ExistingFriends">The detached identities already stored as friends.</param>
/// <param name="ExistingIgnoredUsers">The detached identities already stored as ignored users.</param>
/// <param name="ExistingWishlist">The detached wishlist terms already stored.</param>
/// <param name="ExistingUserNotes">The detached usernames that already have a note.</param>
internal sealed record SettingsImportPreview(
    string SourceName,
    ImportedData Data,
    int FriendCount,
    int FriendsAlreadyPresent,
    int IgnoredCount,
    int IgnoredAlreadyPresent,
    int WishlistCount,
    int WishlistAlreadyPresent,
    int UserNoteCount,
    int UserNotesAlreadyPresent,
    IReadOnlySet<string> ExistingFriends,
    IReadOnlySet<string> ExistingIgnoredUsers,
    IReadOnlySet<string> ExistingWishlist,
    IReadOnlySet<string> ExistingUserNotes)
{
    /// <summary>Gets whether the selected file contains any supported entry.</summary>
    public bool HasContent => FriendCount + IgnoredCount + WishlistCount + UserNoteCount > 0;

    /// <summary>Gets the complete number of individually reviewable entries.</summary>
    public int TotalItemCount => FriendCount + IgnoredCount + WishlistCount + UserNoteCount;

    /// <summary>Gets the number of entries that can add new local data.</summary>
    public int TotalSelectableItemCount =>
        FriendCount - FriendsAlreadyPresent +
        IgnoredCount - IgnoredAlreadyPresent +
        WishlistCount - WishlistAlreadyPresent +
        UserNoteCount - UserNotesAlreadyPresent;

    /// <summary>Gets the number of normalized items in one stable import category.</summary>
    /// <param name="category">The requested category.</param>
    /// <returns>The category's reviewable item count.</returns>
    public int ItemCount(SettingsImportCategory category) => category switch
    {
        SettingsImportCategory.Friends => FriendCount,
        SettingsImportCategory.IgnoredUsers => IgnoredCount,
        SettingsImportCategory.Wishlist => WishlistCount,
        SettingsImportCategory.UserNotes => UserNoteCount,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>Returns normalized selection keys for one category without exposing user-note contents.</summary>
    /// <param name="category">The requested category.</param>
    /// <returns>Detached stable keys in their normalized display order.</returns>
    public IReadOnlyList<string> ItemKeys(SettingsImportCategory category) => category switch
    {
        SettingsImportCategory.Friends => Data.UserList.ToArray(),
        SettingsImportCategory.IgnoredUsers => Data.IgnoredBanned.ToArray(),
        SettingsImportCategory.Wishlist => Data.Wishlist.ToArray(),
        SettingsImportCategory.UserNotes => Data.UserNotes.Select(static note => note.Item1).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>Gets whether an exact normalized review item is already present in local state.</summary>
    /// <param name="category">The item's stable import category.</param>
    /// <param name="key">The normalized value, or username key for a note.</param>
    /// <returns><see langword="true"/> when committing the item cannot add new state.</returns>
    public bool IsAlreadyPresent(SettingsImportCategory category, string key) => category switch
    {
        SettingsImportCategory.Friends => ExistingFriends.Contains(key),
        SettingsImportCategory.IgnoredUsers => ExistingIgnoredUsers.Contains(key),
        SettingsImportCategory.Wishlist => ExistingWishlist.Contains(key),
        SettingsImportCategory.UserNotes => ExistingUserNotes.Contains(key),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>Gets the entries in one category that are not explicit duplicates.</summary>
    /// <param name="category">The requested category.</param>
    /// <returns>Detached normalized selection keys.</returns>
    public IReadOnlyList<string> SelectableItemKeys(SettingsImportCategory category) =>
        ItemKeys(category).Where(key => !IsAlreadyPresent(category, key)).ToArray();

    /// <summary>Gets the number of entries in one category that can add new state.</summary>
    /// <param name="category">The requested category.</param>
    /// <returns>The selectable item count.</returns>
    public int SelectableItemCount(SettingsImportCategory category) =>
        ItemCount(category) -
        (category switch
         {
             SettingsImportCategory.Friends => FriendsAlreadyPresent,
             SettingsImportCategory.IgnoredUsers => IgnoredAlreadyPresent,
             SettingsImportCategory.Wishlist => WishlistAlreadyPresent,
             SettingsImportCategory.UserNotes => UserNotesAlreadyPresent,
             _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
         });

    /// <summary>Builds a portable value containing only the categories selected for commit.</summary>
    /// <param name="selection">The reviewed category selection.</param>
    /// <returns>A detached filtered import value.</returns>
    public ImportedData CreateSelectedData(SettingsImportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new ImportedData(
            Data.UserList.Where(item => selection.Contains(SettingsImportCategory.Friends, item)).ToList(),
            Data.IgnoredBanned.Where(item => selection.Contains(SettingsImportCategory.IgnoredUsers, item)).ToList(),
            Data.Wishlist.Where(item => selection.Contains(SettingsImportCategory.Wishlist, item)).ToList(),
            Data.UserNotes
                .Where(item => selection.Contains(SettingsImportCategory.UserNotes, item.Item1))
                .ToList());
    }
}
