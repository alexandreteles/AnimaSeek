using System.Collections.Generic;

namespace Seeker.Services
{
    /// <summary>
    /// Provides platform-neutral access to the small primitive value set used by application preferences.
    /// </summary>
    /// <remarks>
    /// Implementations may stage writes, but reads made after a write must observe that write. Call
    /// <see cref="Flush"/> at a persistence boundary to ensure all preceding writes have been committed.
    /// Keys are case-sensitive.
    /// </remarks>
    public interface IKeyValueStore
    {
        /// <summary>
        /// Gets a Boolean value, or <paramref name="defaultValue"/> when <paramref name="key"/> is absent.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="defaultValue">The value returned when the key is absent.</param>
        /// <returns>The stored value or <paramref name="defaultValue"/>.</returns>
        bool GetBoolean(string key, bool defaultValue);

        /// <summary>
        /// Gets a 32-bit integer value, or <paramref name="defaultValue"/> when <paramref name="key"/> is absent.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="defaultValue">The value returned when the key is absent.</param>
        /// <returns>The stored value or <paramref name="defaultValue"/>.</returns>
        int GetInt(string key, int defaultValue);

        /// <summary>
        /// Gets a 64-bit integer value, or <paramref name="defaultValue"/> when <paramref name="key"/> is absent.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="defaultValue">The value returned when the key is absent.</param>
        /// <returns>The stored value or <paramref name="defaultValue"/>.</returns>
        long GetLong(string key, long defaultValue);

        /// <summary>
        /// Gets a string value, or <paramref name="defaultValue"/> when <paramref name="key"/> is absent.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="defaultValue">The value returned when the key is absent.</param>
        /// <returns>The stored value or <paramref name="defaultValue"/>.</returns>
        string? GetString(string key, string? defaultValue = null);

        /// <summary>
        /// Gets an unordered snapshot of distinct strings, or <paramref name="defaultValue"/> when the key is absent.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="defaultValue">The value returned when the key is absent.</param>
        /// <returns>
        /// A detached, unordered snapshot of the stored set, or <paramref name="defaultValue"/>. Callers must not
        /// rely on enumeration order.
        /// </returns>
        IReadOnlyCollection<string>? GetStringSet(
            string key,
            IReadOnlyCollection<string>? defaultValue = null);

        /// <summary>
        /// Stores a Boolean value under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="value">The value to store.</param>
        void PutBoolean(string key, bool value);

        /// <summary>
        /// Stores a 32-bit integer value under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="value">The value to store.</param>
        void PutInt(string key, int value);

        /// <summary>
        /// Stores a 64-bit integer value under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="value">The value to store.</param>
        void PutLong(string key, long value);

        /// <summary>
        /// Stores a string under <paramref name="key"/>, or removes the key when <paramref name="value"/> is
        /// <see langword="null"/>.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="value">The value to store, or <see langword="null"/> to remove the key.</param>
        void PutString(string key, string? value);

        /// <summary>
        /// Stores an unordered set of strings under <paramref name="key"/>, or removes the key when
        /// <paramref name="value"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="key">The non-empty preference key.</param>
        /// <param name="value">
        /// The values to store, or <see langword="null"/> to remove the key. Duplicate values must be discarded
        /// using ordinal string equality.
        /// </param>
        void PutStringSet(string key, IReadOnlyCollection<string>? value);

        /// <summary>
        /// Synchronously commits all writes made before this call returns.
        /// </summary>
        /// <remarks>
        /// Implementations whose writes are already durable may implement this method as a no-op.
        /// </remarks>
        void Flush();
    }
}
