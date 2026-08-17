using Foundation;
using Seeker.Services;
using System.IO;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Persists portable application preferences in the app's standard <see cref="NSUserDefaults"/> domain.
/// </summary>
internal sealed class UserDefaultsKeyValueStore : IKeyValueStore
{
    private readonly NSUserDefaults defaults;
    private readonly Func<bool> synchronize;

    /// <summary>
    /// Creates a key-value store over the supplied defaults domain.
    /// </summary>
    /// <param name="defaults">The defaults domain to read and update.</param>
    /// <exception cref="ArgumentNullException"><paramref name="defaults"/> is <see langword="null"/>.</exception>
    public UserDefaultsKeyValueStore(NSUserDefaults defaults)
        : this(defaults, () => defaults.Synchronize())
    {
    }

    /// <summary>Creates a store with an injectable synchronization boundary for deterministic durability tests.</summary>
    /// <param name="defaults">The defaults domain to read and update.</param>
    /// <param name="synchronize">Returns whether staged writes reached the defaults backing store.</param>
    internal UserDefaultsKeyValueStore(NSUserDefaults defaults, Func<bool> synchronize)
    {
        this.defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        this.synchronize = synchronize ?? throw new ArgumentNullException(nameof(synchronize));
    }

    /// <inheritdoc/>
    public bool GetBoolean(string key, bool defaultValue) =>
        defaults[RequireKey(key)] is null ? defaultValue : defaults.BoolForKey(key);

    /// <inheritdoc/>
    public int GetInt(string key, int defaultValue) =>
        defaults[RequireKey(key)] is null ? defaultValue : checked((int)defaults.IntForKey(key));

    /// <inheritdoc/>
    public long GetLong(string key, long defaultValue) =>
        defaults[RequireKey(key)] is null ? defaultValue : defaults.IntForKey(key);

    /// <inheritdoc/>
    public string? GetString(string key, string? defaultValue = null) =>
        defaults.StringForKey(RequireKey(key)) ?? defaultValue;

    /// <inheritdoc/>
    public IReadOnlyCollection<string>? GetStringSet(
        string key,
        IReadOnlyCollection<string>? defaultValue = null)
    {
        string[]? values = defaults.StringArrayForKey(RequireKey(key));
        return values is null
            ? defaultValue
            : values.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc/>
    public void PutBoolean(string key, bool value) => defaults.SetBool(value, RequireKey(key));

    /// <inheritdoc/>
    public void PutInt(string key, int value) => defaults.SetInt(value, RequireKey(key));

    /// <inheritdoc/>
    public void PutLong(string key, long value) => defaults.SetInt(checked((nint)value), RequireKey(key));

    /// <inheritdoc/>
    public void PutString(string key, string? value)
    {
        key = RequireKey(key);
        if (value is null)
        {
            defaults.RemoveObject(key);
            return;
        }

        defaults.SetString(value, key);
    }

    /// <inheritdoc/>
    public void PutStringSet(string key, IReadOnlyCollection<string>? value)
    {
        key = RequireKey(key);
        if (value is null)
        {
            defaults.RemoveObject(key);
            return;
        }

        // The public .NET indexer maps directly to NSUserDefaults objectForKey:/setObject:forKey:. The raw binding
        // methods are protected in .NET 10, so KVC must not be used as a substitute.
        defaults[key] = NSArray.FromStrings(value.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <inheritdoc/>
    public void Flush()
    {
        if (!synchronize())
        {
            throw new IOException("NSUserDefaults failed to synchronize its backing store.");
        }
    }

    private static string RequireKey(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("A preference key cannot be empty or whitespace.", nameof(key))
            : key;
}
