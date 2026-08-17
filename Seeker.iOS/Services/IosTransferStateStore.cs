using Common;
using Seeker.Services;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Persists transfer-manager snapshots as atomic files in Application Support.
/// </summary>
/// <remarks>
/// Transfer snapshots can grow far beyond the small-value use case of <c>NSUserDefaults</c>. This store keeps new
/// checkpoints in private files while retaining a one-way fallback to the Android-compatible preference keys used by
/// earlier AnimaSeek builds.
/// </remarks>
internal sealed class IosTransferStateStore
{
    private const string DownloadsFileName = "downloads.json";
    private const string UploadsFileName = "uploads.json";
    private readonly Lock sync = new();
    private readonly string directory;
    private readonly IKeyValueStore legacyStore;

    /// <summary>Creates a store rooted in the supplied Application Support directory.</summary>
    /// <param name="applicationSupportPath">The app-private Application Support directory.</param>
    /// <param name="legacyStore">The preference store containing transfer snapshots from earlier builds.</param>
    public IosTransferStateStore(string applicationSupportPath, IKeyValueStore legacyStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationSupportPath);
        this.legacyStore = legacyStore ?? throw new ArgumentNullException(nameof(legacyStore));
        directory = Path.Combine(Path.GetFullPath(applicationSupportPath), "Transfers");
    }

    /// <summary>Loads the current download snapshot and its legacy predecessor.</summary>
    /// <returns>
    /// The file-backed current snapshot when present; otherwise the two legacy preference payloads expected by
    /// <see cref="Seeker.TransferPersistence.RestoreDownloadTransferItems(string, string)"/>.
    /// </returns>
    public (string Current, string Legacy) LoadDownloads()
    {
        lock (sync)
        {
            return Load(DownloadsFileName, KeyConsts.M_TransferList, KeyConsts.M_TransferList_v2);
        }
    }

    /// <summary>Loads the current upload snapshot and its legacy predecessor.</summary>
    /// <returns>
    /// The file-backed current snapshot when present; otherwise the two legacy preference payloads expected by
    /// <see cref="Seeker.TransferPersistence.RestoreUploadTransferItems(string, string)"/>.
    /// </returns>
    public (string Current, string Legacy) LoadUploads()
    {
        lock (sync)
        {
            return Load(UploadsFileName, KeyConsts.M_TransferListUpload, KeyConsts.M_TransferListUpload_v2);
        }
    }

    /// <summary>
    /// Saves both current snapshots with an atomic replacement for each file, then removes the superseded
    /// preference payloads.
    /// </summary>
    /// <param name="downloads">The source-generated JSON download-manager snapshot.</param>
    /// <param name="uploads">The source-generated JSON upload-manager snapshot.</param>
    /// <exception cref="ArgumentNullException">Either snapshot is <see langword="null"/>.</exception>
    /// <remarks>
    /// The two files are replaced sequentially; the key-value abstraction does not provide a transaction spanning
    /// both snapshots. Each individual file is nevertheless never exposed in a partially written state.
    /// </remarks>
    /// <exception cref="IOException">The private directory or an individual atomic file replacement cannot be written.</exception>
    public void Save(string downloads, string uploads)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(uploads);
        lock (sync)
        {
            Directory.CreateDirectory(directory);

            WriteAtomically(Resolve(DownloadsFileName), downloads);
            WriteAtomically(Resolve(UploadsFileName), uploads);

            legacyStore.PutString(KeyConsts.M_TransferList, null);
            legacyStore.PutString(KeyConsts.M_TransferList_v2, null);
            legacyStore.PutString(KeyConsts.M_TransferListUpload, null);
            legacyStore.PutString(KeyConsts.M_TransferListUpload_v2, null);
            legacyStore.Flush();
        }
    }

    private (string Current, string Legacy) Load(string fileName, string currentKey, string legacyKey)
    {
        string path = Resolve(fileName);
        if (File.Exists(path))
        {
            return (File.ReadAllText(path), string.Empty);
        }

        return (
            legacyStore.GetString(currentKey, string.Empty) ?? string.Empty,
            legacyStore.GetString(legacyKey, string.Empty) ?? string.Empty);
    }

    private string Resolve(string fileName) => Path.Combine(directory, fileName);

    private static void WriteAtomically(string destination, string contents)
    {
        string temporary = destination + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(
                           stream,
                           new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                           bufferSize: 4096,
                           leaveOpen: true))
                {
                    writer.Write(contents);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
