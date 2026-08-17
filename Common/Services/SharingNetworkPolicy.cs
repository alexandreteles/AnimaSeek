using System;

namespace Seeker.Services
{
    /// <summary>Evaluates whether the current network route satisfies the user's upload-sharing restrictions.</summary>
    public static class SharingNetworkPolicy
    {
        /// <summary>Determines whether a peer upload may be admitted on the current route.</summary>
        /// <param name="networkStatus">The current portable route status.</param>
        /// <param name="allowUploadsOnMetered">
        /// <see langword="true"/> to permit expensive or constrained routes.
        /// </param>
        /// <param name="requireVpnForSharing">
        /// <see langword="true"/> to require positive VPN identification rather than assuming an unknown route is
        /// protected.
        /// </param>
        /// <returns><see langword="true"/> when every enabled restriction is satisfied.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="networkStatus"/> is <see langword="null"/>.</exception>
        public static bool PermitsUpload(
            INetworkStatus networkStatus,
            bool allowUploadsOnMetered,
            bool requireVpnForSharing)
        {
            if (networkStatus is null)
            {
                throw new ArgumentNullException(nameof(networkStatus));
            }

            return (allowUploadsOnMetered || !networkStatus.IsMetered) &&
                (!requireVpnForSharing || networkStatus.IsVpnActive);
        }
    }
}
