using Android.Content;
using Seeker.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker
{
    /// <summary>
    /// Provides the portable <see cref="IKeyValueStore"/> contract over Android shared preferences.
    /// </summary>
    /// <remarks>
    /// Mutations use synchronous shared-preference commits, so successful calls are durable and immediately
    /// observable. Consequently, <see cref="Flush"/> is a no-op.
    /// </remarks>
    public sealed class AndroidSharedPreferencesKeyValueStore : IKeyValueStore
    {
        private readonly ISharedPreferences preferences;

        /// <summary>Creates an adapter over an Android shared-preferences instance.</summary>
        /// <param name="preferences">The preferences instance to read and update.</param>
        /// <exception cref="ArgumentNullException"><paramref name="preferences"/> is <see langword="null"/>.</exception>
        public AndroidSharedPreferencesKeyValueStore(ISharedPreferences preferences)
        {
            this.preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        }

        /// <inheritdoc/>
        public bool GetBoolean(string key, bool defaultValue) =>
            preferences.GetBoolean(RequireKey(key), defaultValue);

        /// <inheritdoc/>
        public int GetInt(string key, int defaultValue) => preferences.GetInt(RequireKey(key), defaultValue);

        /// <inheritdoc/>
        public long GetLong(string key, long defaultValue) => preferences.GetLong(RequireKey(key), defaultValue);

        /// <inheritdoc/>
        public string GetString(string key, string defaultValue = null) =>
            preferences.GetString(RequireKey(key), defaultValue) ?? defaultValue;

        /// <inheritdoc/>
        public IReadOnlyCollection<string> GetStringSet(
            string key,
            IReadOnlyCollection<string> defaultValue = null)
        {
            ICollection<string> values = preferences.GetStringSet(RequireKey(key), null);
            return values == null
                ? defaultValue
                : values.Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <inheritdoc/>
        public void PutBoolean(string key, bool value) =>
            Commit(editor => editor.PutBoolean(RequireKey(key), value));

        /// <inheritdoc/>
        public void PutInt(string key, int value) => Commit(editor => editor.PutInt(RequireKey(key), value));

        /// <inheritdoc/>
        public void PutLong(string key, long value) => Commit(editor => editor.PutLong(RequireKey(key), value));

        /// <inheritdoc/>
        public void PutString(string key, string value)
        {
            key = RequireKey(key);
            Commit(editor => value == null ? editor.Remove(key) : editor.PutString(key, value));
        }

        /// <inheritdoc/>
        public void PutStringSet(string key, IReadOnlyCollection<string> value)
        {
            key = RequireKey(key);
            Commit(
                editor => value == null
                    ? editor.Remove(key)
                    : editor.PutStringSet(key, value.Distinct(StringComparer.Ordinal).ToArray()));
        }

        /// <inheritdoc/>
        public void Flush()
        {
            // Every mutation is committed synchronously.
        }

        private void Commit(Func<ISharedPreferencesEditor, ISharedPreferencesEditor> edit)
        {
            using ISharedPreferencesEditor editor = preferences.Edit();
            if (!edit(editor).Commit())
            {
                throw new InvalidOperationException("Android failed to commit the preference update.");
            }
        }

        private static string RequireKey(string key) =>
            string.IsNullOrWhiteSpace(key)
                ? throw new ArgumentException("A preference key cannot be empty or whitespace.", nameof(key))
                : key;
    }
}
