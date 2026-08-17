using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Seeker.Helpers
{
    /// <summary>
    /// Converts arbitrary diagnostic input into a privacy-preserving event signature.
    /// </summary>
    /// <remarks>
    /// The formatter deliberately never copies caller-supplied message text into its output. Usernames, search terms,
    /// chat content, remote paths, local paths, addresses, and exception messages are therefore unable to enter an
    /// exported diagnostic file even when a legacy caller embeds them in an unstructured logging string.
    /// </remarks>
    public static class PrivacySafeDiagnosticFormatter
    {
        private const int SignatureBytes = 8;
        private static readonly byte[] SignatureKey = CreateSignatureKey();

        /// <summary>Creates a process-local, keyed signature for one arbitrary diagnostic payload.</summary>
        /// <param name="message">Potentially sensitive diagnostic text.</param>
        /// <returns>A fixed-width uppercase hexadecimal signature containing no source text.</returns>
        public static string CreateSignature(string? message)
        {
            byte[] payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
            using HMACSHA256 algorithm = new(SignatureKey);
            byte[] hash = algorithm.ComputeHash(payload);
            return string.Concat(hash.Take(SignatureBytes).Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }

        /// <summary>Formats a new privacy-safe diagnostic line.</summary>
        /// <param name="timestamp">The event timestamp.</param>
        /// <param name="level">A fixed severity label.</param>
        /// <param name="message">Potentially sensitive diagnostic text used only to derive a signature.</param>
        /// <param name="exceptionType">An optional exception type; its message and stack values are never copied.</param>
        /// <returns>A line containing only timestamp, normalized severity, signature, and optional type name.</returns>
        public static string Format(
            DateTimeOffset timestamp,
            string? level,
            string? message,
            Type? exceptionType = null)
        {
            string safeLevel = NormalizeLevel(level);
            string type = exceptionType is null
                ? string.Empty
                : $" exception={NormalizeTypeName(exceptionType.FullName ?? exceptionType.Name)}";
            return $"{timestamp:O} [{safeLevel}] event={CreateSignature(message)}{type}";
        }

        /// <summary>Replaces a legacy free-text log line with a signature-only migrated entry.</summary>
        /// <param name="line">A potentially sensitive line written by an older application version.</param>
        /// <returns>A new line that cannot contain any source substring.</returns>
        public static string RedactLegacyLine(string? line)
        {
            string source = line ?? string.Empty;
            int separator = source.IndexOf(' ');
            DateTimeOffset timestamp = separator > 0 &&
                DateTimeOffset.TryParseExact(
                    source[..separator],
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.UnixEpoch;
            return Format(timestamp, "MIGRATED", source);
        }

        private static string NormalizeLevel(string? level) => level?.Trim().ToUpperInvariant() switch
        {
            "DEBUG" => "DEBUG",
            "INFO" => "INFO",
            "WARN" => "WARN",
            "ERROR" => "ERROR",
            "MIGRATED" => "MIGRATED",
            _ => "EVENT",
        };

        private static string NormalizeTypeName(string value)
        {
            string normalized = new(value
                .Where(character => IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`')
                .Take(160)
                .ToArray());
            return string.IsNullOrEmpty(normalized) ? "Exception" : normalized;
        }

        private static byte[] CreateSignatureKey()
        {
            byte[] key = new byte[32];
            using RandomNumberGenerator generator = RandomNumberGenerator.Create();
            generator.GetBytes(key);
            return key;
        }

        private static bool IsAsciiLetterOrDigit(char value) =>
            value is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
