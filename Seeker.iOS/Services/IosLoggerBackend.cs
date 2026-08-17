using CoreFoundation;
using Seeker.Helpers;
using AppleOSLog = CoreFoundation.OSLog;
using AppleOSLogLevel = CoreFoundation.OSLogLevel;

namespace AnimaSeek.iOS.Services;

/// <summary>
/// Writes diagnostics to the Apple unified console stream and to a rotating, Files-visible log file.
/// </summary>
internal sealed class IosLoggerBackend : ILoggerBackend
{
    private const long MaximumLogLength = 1_048_576;
    private readonly Lock sync = new();
    private readonly AppleOSLog osLog = new("com.animaseek.app", "application");

    /// <summary>Creates a logger whose text log lives in the app's Documents folder.</summary>
    /// <param name="documentsPath">The Files-visible Documents path.</param>
    public IosLoggerBackend(string documentsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsPath);
        Directory.CreateDirectory(documentsPath);
        LogPath = Path.Combine(documentsPath, "AnimaSeek.log");
        MigrateLegacyLogIfNeeded(LogPath);
        MigrateLegacyLogIfNeeded(LogPath + ".1");
    }

    /// <summary>Gets the log path users can export through Files or the share sheet.</summary>
    public string LogPath { get; }

    /// <summary>Checks that every exportable line follows the signature-only privacy contract.</summary>
    /// <returns><see langword="true"/> only for a present, non-empty, wholly privacy-safe log.</returns>
    public bool IsLogPrivacySafe()
    {
        lock (sync)
        {
            try
            {
                return File.Exists(LogPath) &&
                    new FileInfo(LogPath).Length > 0 &&
                    File.ReadLines(LogPath).All(IsPrivacySafeLine);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public void Debug(string msg) => Write("DEBUG", msg);

    /// <inheritdoc/>
    public void Firebase(string msg) => Write("WARN", msg);

    /// <inheritdoc/>
    public void FirebaseError(string msg, Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Write("ERROR", msg, e.GetType());
    }

    /// <inheritdoc/>
    public void InfoFirebase(string msg) => Write("INFO", msg);

    private void Write(string level, string message, Type? exceptionType = null)
    {
        string line = PrivacySafeDiagnosticFormatter.Format(
            DateTimeOffset.Now,
            level,
            message,
            exceptionType);

        osLog.Log(level switch
        {
            "DEBUG" => AppleOSLogLevel.Debug,
            "INFO" => AppleOSLogLevel.Info,
            "ERROR" => AppleOSLogLevel.Error,
            _ => AppleOSLogLevel.Default,
        }, line);

        lock (sync)
        {
            RotateIfNeeded();
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogLength)
        {
            return;
        }

        string archivedPath = LogPath + ".1";
        File.Move(LogPath, archivedPath, overwrite: true);
    }

    /// <summary>Rewrites legacy free-text diagnostics before any file can be shared from the upgraded app.</summary>
    /// <param name="path">The current or rotated app-owned diagnostic file.</param>
    private static void MigrateLegacyLogIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            // A file that cannot be inspected must never be offered by the in-app export flow.
            TryDelete(path);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(path);
            return;
        }

        if (lines.All(IsPrivacySafeLine))
        {
            return;
        }

        string pendingPath = path + ".privacy-pending";
        try
        {
            File.WriteAllLines(
                pendingPath,
                lines.Select(PrivacySafeDiagnosticFormatter.RedactLegacyLine));
            File.Move(pendingPath, path, overwrite: true);
        }
        catch (IOException)
        {
            // Never leave a legacy free-text file available to the in-app export flow after a failed migration.
            TryDelete(path);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(path);
        }
        finally
        {
            TryDelete(pendingPath);
        }
    }

    private static bool IsPrivacySafeLine(string line)
    {
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 3 or > 4 ||
            !DateTimeOffset.TryParseExact(
                fields[0],
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out _) ||
            fields[1] is not ("[DEBUG]" or "[INFO]" or "[WARN]" or "[ERROR]" or "[MIGRATED]" or "[EVENT]") ||
            !fields[2].StartsWith("event=", StringComparison.Ordinal) ||
            fields[2].Length != 22 ||
            fields[2][6..].Any(character => !IsAsciiHexDigit(character)))
        {
            return false;
        }

        return fields.Length == 3 ||
            fields[3].StartsWith("exception=", StringComparison.Ordinal) &&
            fields[3].Length > 10 &&
            fields[3][10..].All(character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`');
    }

    private static bool IsAsciiHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'F';

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

}
