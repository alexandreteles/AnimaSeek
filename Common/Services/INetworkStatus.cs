namespace Seeker.Services
{
    /// <summary>Exposes the portable reachability and route properties used by session and sharing policy.</summary>
    public interface INetworkStatus
    {
        /// <summary>Gets whether the current route is expensive or constrained.</summary>
        bool IsMetered { get; }

        /// <summary>
        /// Gets whether the implementation can positively identify the current application route as VPN-backed.
        /// </summary>
        /// <remarks>
        /// Implementations must return <see langword="false"/> when the platform cannot distinguish a VPN from
        /// another virtual or unknown interface. This makes a require-VPN sharing policy fail closed.
        /// </remarks>
        bool IsVpnActive { get; }

        /// <summary>Gets whether the current application route can reach the internet.</summary>
        bool DoWeHaveInternet();

        /// <summary>Gets whether the observed default route changed recently.</summary>
        bool HasHandoffOccuredRecently();
    }
}
