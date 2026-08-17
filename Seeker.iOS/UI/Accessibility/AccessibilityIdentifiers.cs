using System.Security.Cryptography;
using System.Text;

namespace AnimaSeek.iOS.UI.Accessibility;

/// <summary>
/// Defines stable, nonlocalized automation identifiers without exposing credentials, usernames, messages, or paths.
/// </summary>
internal static class AccessibilityIdentifiers
{
    /// <summary>Identifies the root tab bar.</summary>
    public const string AppTabBar = "app.tabs";

    /// <summary>Identifies the Home root screen.</summary>
    public const string HomeScreen = "home.screen";

    /// <summary>Identifies Home's username field.</summary>
    public const string HomeUsername = "home.login.username";

    /// <summary>Identifies Home's password field.</summary>
    public const string HomePassword = "home.login.password";

    /// <summary>Identifies Home's password-visibility control.</summary>
    public const string HomePasswordVisibility = "home.login.password-visibility";

    /// <summary>Identifies Home's inline credential error.</summary>
    public const string HomeLoginError = "home.login.error";

    /// <summary>Identifies Home's sign-in command.</summary>
    public const string HomeLoginSubmit = "home.login.submit";

    /// <summary>Identifies Home's reconnect command.</summary>
    public const string HomeReconnect = "home.connection.reconnect";

    /// <summary>Identifies Home's connection-state summary.</summary>
    public const string HomeConnectionStatus = "home.connection.status";

    /// <summary>Identifies Home's local sharing summary independently from connection status.</summary>
    public const string HomeSharingStatus = "home.sharing.status";

    /// <summary>Identifies Home's message destination.</summary>
    public const string HomeMessages = "home.destination.messages";

    /// <summary>Identifies Home's chatroom destination.</summary>
    public const string HomeChatrooms = "home.destination.chatrooms";

    /// <summary>Identifies Home's saved-users destination.</summary>
    public const string HomeUsers = "home.destination.users";

    /// <summary>Identifies Home's navigation-bar Settings action, which reaches Account, About, and Legal Notices.</summary>
    public const string HomeSettings = "home.destination.settings";

    /// <summary>Identifies the About screen.</summary>
    public const string AboutScreen = "about.screen";

    /// <summary>Identifies About's source-code link.</summary>
    public const string AboutSource = "about.source";

    /// <summary>Identifies About's permanent legal destination.</summary>
    public const string AboutLegal = "about.legal";

    /// <summary>Identifies the Legal Notices screen.</summary>
    public const string LegalScreen = "legal.screen";

    /// <summary>Identifies the exact modified-library notice on the permanent legal screen.</summary>
    public const string LegalLibraryNotice = "legal.modified-library-notice";

    /// <summary>Identifies a legal document using a privacy-safe stable suffix.</summary>
    /// <param name="documentIdentity">The internal document identity.</param>
    /// <returns>An automation identifier that does not include the supplied value.</returns>
    public static string LegalDocument(string documentIdentity) =>
        $"legal.document.{Opaque(documentIdentity)}";

    /// <summary>Identifies a route destination using a privacy-safe stable suffix.</summary>
    /// <param name="routeIdentity">The route's internal identity.</param>
    /// <returns>An automation identifier that does not include route payload text.</returns>
    public static string Route(string routeIdentity) => $"route.{Opaque(routeIdentity)}";

    /// <summary>Creates a deterministic short token without retaining sensitive source text.</summary>
    /// <param name="value">The source identity.</param>
    /// <returns>A lowercase, 16-character SHA-256 prefix.</returns>
    public static string Opaque(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}
