using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace Common
{
    /// <summary>
    /// Provides platform-neutral access to the English application string catalog.
    /// </summary>
    /// <remarks>
    /// String identifiers intentionally remain compatible with the Android resource names so
    /// portable code and the iOS UI can migrate incrementally. Resource lookup is ordinal and
    /// culture-neutral; formatting uses the caller's current culture for argument rendering.
    /// </remarks>
    public static class StringResources
    {
        private const string ResourceBaseName = "Common.StringResources";

        private static readonly ResourceManager ResourceManager = new ResourceManager(
            ResourceBaseName,
            typeof(StringResources).Assembly);

        private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishStrings =
            new Lazy<IReadOnlyDictionary<string, string>>(LoadEnglishStrings);

        /// <summary>
        /// Gets the number of entries in the English string catalog.
        /// </summary>
        public static int Count => EnglishStrings.Value.Count;

        /// <summary>
        /// Gets a string by its Android-compatible resource identifier.
        /// </summary>
        /// <param name="key">The case-sensitive resource identifier.</param>
        /// <returns>The English resource value.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="key"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when the catalog does not contain <paramref name="key"/>.
        /// </exception>
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A string resource key is required.", nameof(key));
            }

            return EnglishStrings.Value.TryGetValue(key, out string value)
                ? value
                : throw new KeyNotFoundException($"String resource '{key}' was not found.");
        }

        /// <summary>
        /// Gets a string for a portable <see cref="StringKey"/> value.
        /// </summary>
        /// <param name="key">The portable resource key.</param>
        /// <returns>The English resource value.</returns>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when the catalog does not contain the enum member's name.
        /// </exception>
        public static string Get(StringKey key) => Get(key.ToString());

        /// <summary>
        /// Attempts to get a string by its Android-compatible resource identifier.
        /// </summary>
        /// <param name="key">The case-sensitive resource identifier.</param>
        /// <param name="value">
        /// The English resource value when found; otherwise, <see cref="string.Empty"/>.
        /// </param>
        /// <returns><see langword="true"/> when the key exists; otherwise, <see langword="false"/>.</returns>
        public static bool TryGet(string key, out string value)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                EnglishStrings.Value.TryGetValue(key, out string resourceValue))
            {
                value = resourceValue;
                return true;
            }

            value = string.Empty;
            return false;
        }

        /// <summary>
        /// Formats a string resource using the current culture.
        /// </summary>
        /// <param name="key">The case-sensitive resource identifier.</param>
        /// <param name="arguments">Values to substitute into composite format placeholders.</param>
        /// <returns>The formatted English resource value.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="key"/> is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="FormatException">
        /// Thrown when the format string and <paramref name="arguments"/> are incompatible.
        /// </exception>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when the catalog does not contain <paramref name="key"/>.
        /// </exception>
        public static string Format(string key, params object[] arguments) =>
            string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

        /// <summary>
        /// Formats a portable string resource using the current culture.
        /// </summary>
        /// <param name="key">The portable resource key.</param>
        /// <param name="arguments">Values to substitute into composite format placeholders.</param>
        /// <returns>The formatted English resource value.</returns>
        /// <exception cref="FormatException">
        /// Thrown when the format string and <paramref name="arguments"/> are incompatible.
        /// </exception>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when the catalog does not contain the enum member's name.
        /// </exception>
        public static string Format(StringKey key, params object[] arguments) =>
            Format(key.ToString(), arguments);

        private static IReadOnlyDictionary<string, string> LoadEnglishStrings()
        {
            ResourceSet resourceSet = ResourceManager.GetResourceSet(
                CultureInfo.InvariantCulture,
                true,
                false) ?? throw new InvalidOperationException(
                    $"Embedded string resource '{ResourceBaseName}' could not be loaded.");

            Dictionary<string, string> entries = resourceSet
                .Cast<DictionaryEntry>()
                .ToDictionary(
                    entry => (string)entry.Key,
                    entry => (string)entry.Value,
                    StringComparer.Ordinal);

            return new ReadOnlyDictionary<string, string>(entries);
        }
    }
}
