using Common;

namespace AnimaSeek.iOS.UI.Components;

/// <summary>Provides concise access to the shared English catalog for UIKit presentation code.</summary>
internal static class AppStrings
{
    /// <summary>Gets a localized value from the shared application catalog.</summary>
    /// <param name="key">The stable catalog key.</param>
    /// <returns>The catalog-backed visible text.</returns>
    public static string Get(string key) => StringResources.Get(key);

    /// <summary>Formats a localized value from the shared application catalog.</summary>
    /// <param name="key">The stable catalog key.</param>
    /// <param name="arguments">The culture-aware replacement values.</param>
    /// <returns>The formatted catalog-backed visible text.</returns>
    public static string Format(string key, params object[] arguments) =>
        StringResources.Format(key, arguments);
}
