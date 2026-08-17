namespace AnimaSeek.iOS;

/// <summary>Pure eligibility policy for opportunistic authentication that must honor explicit reconnect suppression.</summary>
internal static class BackgroundConnectionEligibilityPolicy
{
    /// <summary>Returns whether a background operation may start or continue toward <c>ConnectAsync</c>.</summary>
    public static bool CanConnect(
        bool disposed,
        bool automaticReconnectBlocked,
        bool hasCredentials,
        bool authenticationGenerationMatches) =>
        !disposed &&
        !automaticReconnectBlocked &&
        hasCredentials &&
        authenticationGenerationMatches;
}
