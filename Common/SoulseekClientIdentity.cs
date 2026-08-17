namespace Seeker;

/// <summary>Defines the Soulseek protocol identity assigned to AnimaSeek.</summary>
public static class SoulseekClientIdentity
{
    /// <summary>
    /// Gets the user-facing notice required for AnimaSeek's modified vendored protocol library.
    /// </summary>
    /// <remarks>
    /// Keep this notice visible and prominent when replacing the temporary iOS root view, and retain an enduring
    /// legal-notices route in the completed interface.
    /// </remarks>
    public const string ModifiedLibraryNotice =
        "This is a modified version of Soulseek.NET.  It is not maintained by, endorsed by, or affiliated with " +
        "the Soulseek.NET project or its author(s).";

    /// <summary>
    /// Gets the AnimaSeek-specific minor version sent during Soulseek login.
    /// </summary>
    /// <remarks>
    /// This value identifies the client implementation and is intentionally independent of the
    /// application version shown to users. Keep it synchronized across every shipping platform.
    /// </remarks>
    public const int MinorVersion = 793;
}
