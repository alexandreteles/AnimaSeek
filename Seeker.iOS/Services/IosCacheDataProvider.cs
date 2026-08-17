using Foundation;
using Seeker.Serialization;
using Seeker.Services;

namespace AnimaSeek.iOS.Services;

/// <summary>Stores the portable share-index cache in Application Support.</summary>
internal sealed class IosCacheDataProvider : ICacheDataProvider
{
    private readonly string cacheDirectory;
    private readonly IKeyValueStore keyValueStore;

    /// <summary>Creates the provider and resolves its private cache directory.</summary>
    /// <param name="keyValueStore">The store used for the cached file count.</param>
    public IosCacheDataProvider(IKeyValueStore keyValueStore)
    {
        this.keyValueStore = keyValueStore ?? throw new ArgumentNullException(nameof(keyValueStore));
        string applicationSupport = NSSearchPath.GetDirectories(
            NSSearchPathDirectory.ApplicationSupportDirectory,
            NSSearchPathDomain.User,
            expandTilde: true).First();
        cacheDirectory = Path.Combine(applicationSupport, "ShareIndex");
    }

    /// <inheritdoc/>
    public bool CacheExists() => Directory.Exists(cacheDirectory);

    /// <inheritdoc/>
    public void EnsureCacheExists() => Directory.CreateDirectory(cacheDirectory);

    /// <inheritdoc/>
    public Stream OpenRead(string filename)
    {
        string path = Resolve(filename);
        return File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null!;
    }

    /// <inheritdoc/>
    public void Write(string filename, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCacheExists();
        File.WriteAllBytes(Resolve(filename), data);
    }

    /// <inheritdoc/>
    public int GetCachedFileCount() => keyValueStore.GetInt(Common.KeyConsts.M_CACHE_nonHiddenFileCount_v3, -1);

    /// <inheritdoc/>
    public void SaveCachedFileCount(int count)
    {
        keyValueStore.PutInt(Common.KeyConsts.M_CACHE_nonHiddenFileCount_v3, count);
        keyValueStore.Flush();
    }

    private string Resolve(string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        string leaf = Path.GetFileName(filename);
        return string.IsNullOrWhiteSpace(leaf)
            ? throw new ArgumentException("A cache filename must contain a leaf name.", nameof(filename))
            : Path.Combine(cacheDirectory, leaf);
    }
}
