using System.Net;
using System.Text;
using Common;
using Seeker;
using Seeker.Helpers;
using Seeker.Services;
using Soulseek;

namespace AnimaSeek.iOS.Services;

/// <summary>Describes the locally stored profile presented by the Account screen and peer-info responder.</summary>
/// <param name="Username">The connected or most recently authenticated user name.</param>
/// <param name="Biography">The public profile biography.</param>
/// <param name="ImageBytes">Detached encoded image bytes, or <see langword="null"/> when no image is configured.</param>
/// <param name="ImageName">The original display name retained for the Account screen.</param>
internal sealed record IosAccountProfileSnapshot(
    string Username,
    string Biography,
    byte[]? ImageBytes,
    string? ImageName);

/// <summary>Captures every profile representation required to roll back an interrupted durable commit.</summary>
/// <param name="PersistedBiography">The biography previously stored in the key-value store.</param>
/// <param name="PersistedImageName">The image name previously stored in the key-value store.</param>
/// <param name="PreferenceBiography">The prior process-local biography mirror.</param>
/// <param name="PreferenceImageName">The prior process-local image-name mirror.</param>
/// <param name="ImageBytes">The exact prior image file bytes, or <see langword="null"/> when absent.</param>
internal sealed record IosAccountProfileRollbackSnapshot(
    string PersistedBiography,
    string? PersistedImageName,
    string PreferenceBiography,
    string PreferenceImageName,
    byte[]? ImageBytes);

/// <summary>
/// Owns iOS account profile persistence, safe peer-info responses, account commands, and portable data export.
/// </summary>
/// <remarks>
/// Profile image bytes are private app data in Application Support. Replacement is committed through an atomic rename
/// before the key-value pointer changes, so a failed write cannot discard the previously published profile image.
/// </remarks>
internal sealed class IosAccountService
{
    /// <summary>Maximum encoded profile-image size accepted from Files.</summary>
    public const int MaximumImageBytes = 5 * 1024 * 1024;

    private const string ProfileDirectoryName = "AccountProfile";
    private const string StoredImageFileName = "profile-image";
    private readonly AppSession session;
    private readonly IKeyValueStore keyValueStore;
    private readonly IosFileSystemService fileSystem;
    private readonly IosUserListService userLists;
    private readonly IosWishlistService wishlists;
    private readonly PortableAppDataStateService appDataStateService;
    private readonly ILoggerBackend logger;
    private readonly SemaphoreSlim profileGate = new(1, 1);
    private readonly SemaphoreSlim exportGate = new(1, 1);

    /// <summary>Creates the account facade over process-lifetime portable and iOS services.</summary>
    /// <param name="session">The authenticated Soulseek session.</param>
    /// <param name="keyValueStore">The durable preference store.</param>
    /// <param name="fileSystem">The sandbox path provider.</param>
    /// <param name="userLists">The canonical friend and ignore lists.</param>
    /// <param name="wishlists">The canonical wishlist service.</param>
    /// <param name="appDataStateService">The source of portable user metadata.</param>
    /// <param name="logger">The privacy-safe diagnostics sink.</param>
    public IosAccountService(
        AppSession session,
        IKeyValueStore keyValueStore,
        IosFileSystemService fileSystem,
        IosUserListService userLists,
        IosWishlistService wishlists,
        PortableAppDataStateService appDataStateService,
        ILoggerBackend logger)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.userLists = userLists ?? throw new ArgumentNullException(nameof(userLists));
        this.wishlists = wishlists ?? throw new ArgumentNullException(nameof(wishlists));
        this.appDataStateService = appDataStateService ?? throw new ArgumentNullException(nameof(appDataStateService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Clears stored credentials and ends the session. Local work is unaffected, so this stays available
    /// while the account is disconnected.
    /// </summary>
    public void SignOut() => session.Logout();

    /// <summary>Raised when the session's connection state changes; may arrive on any thread.</summary>
    public event EventHandler? SessionStateChanged
    {
        add => session.StateChanged += value;
        remove => session.StateChanged -= value;
    }

    /// <summary>Gets whether the session currently holds an authenticated server connection.</summary>
    public bool IsConnected => session.ConnectionState == SessionConnectionState.Connected;

    /// <summary>Loads a detached own-profile snapshot without exposing an internal sandbox path.</summary>
    /// <returns>The current biography, optional image bytes/name, and presentation-safe user name.</returns>
    public async Task<IosAccountProfileSnapshot> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        await profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return CreateSnapshot();
        }
        finally
        {
            profileGate.Release();
        }
    }

    /// <summary>Atomically replaces the biography and optional profile image.</summary>
    /// <param name="biography">Public biography text; <see langword="null"/> is normalized to an empty string.</param>
    /// <param name="imageBytes">Encoded image bytes, or <see langword="null"/> to clear the current image.</param>
    /// <param name="imageName">The original image display name when bytes are supplied.</param>
    /// <param name="cancellationToken">Cancels before durable mutation starts.</param>
    /// <returns>The newly committed detached profile snapshot.</returns>
    /// <exception cref="InvalidDataException">The image is empty, exceeds 5 MB, or omits a usable display name.</exception>
    public async Task<IosAccountProfileSnapshot> SaveProfileAsync(
        string? biography,
        byte[]? imageBytes,
        string? imageName,
        CancellationToken cancellationToken = default)
    {
        byte[]? detachedImage = ValidateAndCopyImage(imageBytes, imageName);
        string normalizedBiography = biography?.Trim() ?? string.Empty;
        string? safeDisplayName = detachedImage is null ? null : SanitizeDisplayName(imageName!);

        await profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string profileDirectory = ProfileDirectory;
            string destinationPath = StoredImagePath;
            string? stagedPath = null;
            var rollback = new IosAccountProfileRollbackSnapshot(
                keyValueStore.GetString(KeyConsts.M_UserInfoBio, PreferencesState.UserInfoBio) ?? string.Empty,
                keyValueStore.GetString(KeyConsts.M_UserInfoPicture, null),
                PreferencesState.UserInfoBio,
                PreferencesState.UserInfoPictureName,
                System.IO.File.Exists(destinationPath)
                    ? await System.IO.File.ReadAllBytesAsync(destinationPath, cancellationToken)
                    : null);
            try
            {
                if (detachedImage is not null)
                {
                    System.IO.Directory.CreateDirectory(profileDirectory);
                    stagedPath = Path.Combine(profileDirectory, $".{Guid.NewGuid():N}.pending");
                    await System.IO.File.WriteAllBytesAsync(stagedPath, detachedImage, cancellationToken)
                        .ConfigureAwait(false);
                    System.IO.File.Move(stagedPath, destinationPath, overwrite: true);
                    stagedPath = null;
                }
                else if (System.IO.File.Exists(destinationPath))
                {
                    System.IO.File.Delete(destinationPath);
                }

                PreferencesState.UserInfoBio = normalizedBiography;
                PreferencesState.UserInfoPictureName = safeDisplayName ?? string.Empty;
                keyValueStore.PutString(KeyConsts.M_UserInfoBio, normalizedBiography);
                keyValueStore.PutString(KeyConsts.M_UserInfoPicture, safeDisplayName);
                keyValueStore.Flush();
            }
            catch (Exception commitException)
            {
                try
                {
                    await RestoreProfileAsync(rollback, destinationPath).ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    logger.FirebaseError("Account profile rollback failed after an incomplete commit.", rollbackException);
                    throw new AggregateException(
                        "The account profile update failed and its previous state could not be completely restored.",
                        commitException,
                        rollbackException);
                }

                throw;
            }
            finally
            {
                if (stagedPath is not null && System.IO.File.Exists(stagedPath))
                {
                    System.IO.File.Delete(stagedPath);
                }
            }

            return new IosAccountProfileSnapshot(
                session.Username ?? string.Empty,
                normalizedBiography,
                detachedImage?.ToArray(),
                safeDisplayName);
        }
        finally
        {
            profileGate.Release();
        }
    }

    /// <summary>Restores the exact pre-commit file, durable values, and process-local preference mirrors.</summary>
    /// <param name="snapshot">The state captured immediately before mutation began.</param>
    /// <param name="destinationPath">The fixed Application Support image destination.</param>
    /// <remarks>
    /// Rollback deliberately ignores the caller's cancellation request because the durable transaction has already
    /// started. Image restoration uses another same-directory rename so observers never read a partial image file.
    /// </remarks>
    private async Task RestoreProfileAsync(
        IosAccountProfileRollbackSnapshot snapshot,
        string destinationPath)
    {
        string? stagedRollbackPath = null;
        try
        {
            if (snapshot.ImageBytes is { } previousImage)
            {
                System.IO.Directory.CreateDirectory(ProfileDirectory);
                stagedRollbackPath = Path.Combine(ProfileDirectory, $".{Guid.NewGuid():N}.rollback");
                await System.IO.File.WriteAllBytesAsync(stagedRollbackPath, previousImage, CancellationToken.None)
                    .ConfigureAwait(false);
                System.IO.File.Move(stagedRollbackPath, destinationPath, overwrite: true);
                stagedRollbackPath = null;
            }
            else if (System.IO.File.Exists(destinationPath))
            {
                System.IO.File.Delete(destinationPath);
            }

            PreferencesState.UserInfoBio = snapshot.PreferenceBiography;
            PreferencesState.UserInfoPictureName = snapshot.PreferenceImageName;
            keyValueStore.PutString(KeyConsts.M_UserInfoBio, snapshot.PersistedBiography);
            keyValueStore.PutString(KeyConsts.M_UserInfoPicture, snapshot.PersistedImageName);
            keyValueStore.Flush();
        }
        finally
        {
            if (stagedRollbackPath is not null && System.IO.File.Exists(stagedRollbackPath))
            {
                System.IO.File.Delete(stagedRollbackPath);
            }
        }
    }

    /// <summary>Returns own profile information to a requesting peer unless that peer is ignored.</summary>
    /// <param name="username">The requesting peer's Soulseek user name.</param>
    /// <param name="endpoint">The validated peer endpoint supplied by Soulseek.NET.</param>
    /// <returns>A privacy-safe user-info response with sharing availability and a detached optional picture.</returns>
    public async Task<UserInfo> ResolveUserInfoAsync(string username, IPEndPoint endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (userLists.IsUserInIgnoreList(username))
        {
            return new UserInfo(string.Empty, 0, 0, false);
        }

        IosAccountProfileSnapshot profile = await GetProfileAsync().ConfigureAwait(false);
        bool sharingEnabled = PreferencesState.SharingOn;
        return new UserInfo(
            profile.Biography,
            sharingEnabled ? 1 : 0,
            queueLength: 0,
            hasFreeUploadSlot: sharingEnabled,
            profile.ImageBytes?.ToArray());
    }

    /// <summary>Changes the authenticated account password through the session's serialized account command.</summary>
    /// <param name="newPassword">The new non-empty password.</param>
    /// <param name="cancellationToken">Cancels the server request.</param>
    public Task ChangePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        return session.ChangePasswordAsync(newPassword, cancellationToken);
    }

    /// <summary>Gets the server-reported number of remaining privilege seconds.</summary>
    /// <param name="cancellationToken">Cancels the server request.</param>
    /// <returns>Remaining seconds, clamped to zero.</returns>
    public async Task<int> GetPrivilegesAsync(CancellationToken cancellationToken = default) =>
        Math.Max(0, await session.GetPrivilegesAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Gives whole privilege days to another Soulseek user through the session facade.</summary>
    /// <param name="username">The recipient user name.</param>
    /// <param name="days">The positive whole-day amount.</param>
    /// <param name="cancellationToken">Cancels the server request.</param>
    public Task GrantPrivilegesAsync(
        string username,
        int days,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        return session.GrantPrivilegesAsync(username, days, cancellationToken);
    }

    /// <summary>Creates a source-generated JSON export in the Files-visible Documents directory.</summary>
    /// <param name="cancellationToken">Cancels snapshot construction or file creation.</param>
    /// <returns>The absolute, shareable export file path.</returns>
    public async Task<string> ExportPortableDataAsync(CancellationToken cancellationToken = default)
    {
        await exportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SeekerImportExportData export = CreatePortableExport();
                string payload = SerializationHelper.SerializeToString(export);
                string path = AllocateExportPath();
                string pendingPath = path + ".pending";
                try
                {
                    System.IO.File.WriteAllText(
                        pendingPath,
                        payload,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    System.IO.File.Move(pendingPath, path);
                    return path;
                }
                finally
                {
                    if (System.IO.File.Exists(pendingPath))
                    {
                        System.IO.File.Delete(pendingPath);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.FirebaseError("Portable account data export failed.", exception);
            throw;
        }
        finally
        {
            exportGate.Release();
        }
    }

    /// <summary>Builds an export from detached canonical snapshots without serializing credentials or device paths.</summary>
    /// <returns>A portable Seeker import/export value.</returns>
    private SeekerImportExportData CreatePortableExport()
    {
        IosUserListEntrySnapshot[] users = userLists.GetSnapshot().ToArray();
        PortableAppDataState metadata = appDataStateService.RestoreAll().State;
        return new SeekerImportExportData
        {
            Userlist = users
                .Where(entry => entry.Role == Seeker.UserRole.Friend)
                .Select(entry => entry.Username)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BanIgnoreList = users
                .Where(entry => entry.Role == Seeker.UserRole.Ignored)
                .Select(entry => entry.Username)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Wishlist = wishlists.GetSnapshot()
                .Select(entry => entry.Query)
                .Where(query => !string.IsNullOrWhiteSpace(query))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(query => query, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UserNotes = metadata.UserNotes
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new KeyValueEl { Key = pair.Key, Value = pair.Value ?? string.Empty })
                .ToList(),
        };
    }

    /// <summary>Creates a detached snapshot while the profile mutation gate is held.</summary>
    /// <returns>The current coherent profile state.</returns>
    private IosAccountProfileSnapshot CreateSnapshot()
    {
        string? imageName = keyValueStore.GetString(KeyConsts.M_UserInfoPicture, null);
        byte[]? image = imageName is not null && System.IO.File.Exists(StoredImagePath)
            ? System.IO.File.ReadAllBytes(StoredImagePath)
            : null;
        if (image is { Length: > MaximumImageBytes })
        {
            logger.Firebase("Stored account profile image exceeds the safety limit and was omitted.");
            image = null;
        }

        return new IosAccountProfileSnapshot(
            session.Username ?? string.Empty,
            keyValueStore.GetString(KeyConsts.M_UserInfoBio, PreferencesState.UserInfoBio) ?? string.Empty,
            image?.ToArray(),
            image is null ? null : imageName);
    }

    /// <summary>Validates and detaches optional encoded image bytes before profile mutation.</summary>
    /// <param name="bytes">Candidate encoded image bytes.</param>
    /// <param name="name">Candidate display name.</param>
    /// <returns>A detached image buffer, or <see langword="null"/> for a cleared image.</returns>
    private static byte[]? ValidateAndCopyImage(byte[]? bytes, string? name)
    {
        if (bytes is null)
        {
            return null;
        }

        if (bytes.Length == 0)
        {
            throw new InvalidDataException("The selected profile image is empty.");
        }

        if (bytes.Length > MaximumImageBytes)
        {
            throw new InvalidDataException("The selected profile image exceeds the 5 MB limit.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("The selected profile image has no file name.");
        }

        return bytes.ToArray();
    }

    /// <summary>Removes path information and bounds a selected image's display name.</summary>
    /// <param name="name">The source file name or path.</param>
    /// <returns>A safe, non-empty display name.</returns>
    private static string SanitizeDisplayName(string name)
    {
        string safeName = Path.GetFileName(name).Trim();
        if (safeName.Length == 0)
        {
            throw new InvalidDataException("The selected profile image has no file name.");
        }

        return safeName.Length <= 255 ? safeName : safeName[^255..];
    }

    /// <summary>Allocates a collision-free timestamped export path in Documents.</summary>
    /// <returns>An absolute path that does not currently exist.</returns>
    private string AllocateExportPath()
    {
        string baseName = $"AnimaSeek-Data-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        return Enumerable.Range(0, int.MaxValue)
            .Select(index => Path.Combine(
                fileSystem.DocumentsPath,
                index == 0 ? $"{baseName}.json" : $"{baseName}-{index}.json"))
            .First(path =>
                !System.IO.File.Exists(path) &&
                !System.IO.File.Exists(path + ".pending"));
    }

    /// <summary>Gets the private directory that owns the durable profile image.</summary>
    private string ProfileDirectory => Path.Combine(fileSystem.ApplicationSupportPath, ProfileDirectoryName);

    /// <summary>Gets the fixed atomic-replacement target for the current profile image.</summary>
    private string StoredImagePath => Path.Combine(ProfileDirectory, StoredImageFileName);
}
