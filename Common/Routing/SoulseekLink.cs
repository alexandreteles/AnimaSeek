using System;
using System.Linq;

namespace Seeker.Routing
{
    /// <summary>Identifies whether a Soulseek link addresses one file or one folder.</summary>
    public enum SoulseekLinkKind
    {
        /// <summary>The link addresses a single file.</summary>
        File = 0,

        /// <summary>The link addresses a folder and its contents.</summary>
        Folder = 1,
    }

    /// <summary>
    /// Represents a validated <c>slsk://</c> link without depending on a platform URL type.
    /// </summary>
    /// <param name="Username">The remote Soulseek username.</param>
    /// <param name="Path">The decoded remote path, normalized to backslash separators.</param>
    /// <param name="DirectoryPath">The folder to open before acting on the link.</param>
    /// <param name="Kind">Whether the address represents a file or folder.</param>
    public sealed class SoulseekLink
    {
        /// <summary>Initializes one validated Soulseek target.</summary>
        /// <param name="username">The remote Soulseek username.</param>
        /// <param name="path">The normalized remote path.</param>
        /// <param name="directoryPath">The folder to open before acting on the target.</param>
        /// <param name="kind">Whether the target is a file or folder.</param>
        public SoulseekLink(
            string username,
            string path,
            string directoryPath,
            SoulseekLinkKind kind)
        {
            Username = username;
            Path = path;
            DirectoryPath = directoryPath;
            Kind = kind;
        }

        /// <summary>Gets the remote Soulseek username.</summary>
        public string Username { get; }

        /// <summary>Gets the decoded remote path, normalized to backslash separators.</summary>
        public string Path { get; }

        /// <summary>Gets the folder to open before acting on the link.</summary>
        public string DirectoryPath { get; }

        /// <summary>Gets whether the address represents a file or folder.</summary>
        public SoulseekLinkKind Kind { get; }

        /// <summary>Gets whether the link identifies a single file.</summary>
        public bool IsFile => Kind == SoulseekLinkKind.File;

        /// <summary>Creates the canonical encoded representation of this link.</summary>
        /// <returns>A <c>slsk://</c> URL suitable for copying or sharing.</returns>
        public override string ToString()
        {
            string forwardPath = Path.Replace('\\', '/');
            string encodedUsername = Uri.EscapeDataString(Username);
            string encodedPath = string.Join(
                "/",
                forwardPath.Split('/').Select(Uri.EscapeDataString));
            string directorySuffix = Kind == SoulseekLinkKind.Folder ? "/" : string.Empty;
            return $"slsk://{encodedUsername}/{encodedPath}{directorySuffix}";
        }
    }

    /// <summary>Parses and validates the username-and-path form supported by Soulseek clients.</summary>
    public static class SoulseekLinkParser
    {
        private const int MaximumLinkLength = 16_384;

        /// <summary>
        /// Attempts to parse a <c>slsk://username/path</c> link.
        /// </summary>
        /// <param name="value">The absolute URL text supplied by a message, pasteboard, or scene.</param>
        /// <param name="link">The normalized link when parsing succeeds.</param>
        /// <returns><see langword="true"/> when the URL is a safe supported Soulseek link.</returns>
        /// <remarks>
        /// Search and profile URLs are intentionally unsupported. Empty usernames or paths, control characters,
        /// traversal segments, query strings, fragments, credentials, ports, and encoded separators inside a path
        /// segment are rejected rather than guessed at.
        /// </remarks>
        public static bool TryParse(string? value, out SoulseekLink? link)
        {
            link = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLinkLength)
            {
                return false;
            }

            bool isFolder = value.EndsWith("/", StringComparison.Ordinal);
            int rawPathSeparator = value.IndexOf('/', "slsk://".Length);
            if (rawPathSeparator < 0 || !HasSafeRawPath(value[(rawPathSeparator + 1)..].TrimEnd('/')))
            {
                return false;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
                !string.Equals(uri.Scheme, "slsk", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !uri.IsDefaultPort)
            {
                return false;
            }

            string username;
            string decodedPath;
            try
            {
                username = Uri.UnescapeDataString(uri.Host);
                string escapedPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
                decodedPath = string.Join(
                    "\\",
                    escapedPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.UnescapeDataString));
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!IsSafeValue(username) || !IsSafePath(decodedPath))
            {
                return false;
            }

            string[] pathSegments = decodedPath.Split('\\');
            string directoryPath = isFolder
                ? decodedPath
                : string.Join("\\", pathSegments.Take(pathSegments.Length - 1));
            link = new SoulseekLink(
                username,
                decodedPath,
                directoryPath,
                isFolder ? SoulseekLinkKind.Folder : SoulseekLinkKind.File);
            return true;
        }

        /// <summary>Parses a supported Soulseek link or throws a descriptive format exception.</summary>
        /// <param name="value">The absolute Soulseek URL text.</param>
        /// <returns>The validated normalized link.</returns>
        /// <exception cref="FormatException"><paramref name="value"/> is malformed, unsafe, or unsupported.</exception>
        public static SoulseekLink Parse(string value) =>
            TryParse(value, out SoulseekLink? link)
                ? link!
                : throw new FormatException("The Soulseek link must contain a username and a safe file or folder path.");

        private static bool IsSafeValue(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value is not "." and not ".." &&
            value.All(character => !char.IsControl(character) && character is not '/' and not '\\');

        private static bool IsSafePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value[0] == '\\')
            {
                return false;
            }

            string[] segments = value.Split('\\');
            return segments.All(segment =>
                IsSafeValue(segment) &&
                !segment.Contains(':', StringComparison.Ordinal));
        }

        private static bool HasSafeRawPath(string escapedPath)
        {
            if (string.IsNullOrWhiteSpace(escapedPath))
            {
                return false;
            }

            try
            {
                string[] segments = escapedPath.Split('/');
                return segments.All(segment =>
                {
                    string decoded = Uri.UnescapeDataString(segment);
                    return IsSafeValue(decoded) && !decoded.Contains(':', StringComparison.Ordinal);
                });
            }
            catch (UriFormatException)
            {
                return false;
            }
        }
    }
}
