using Seeker.Helpers;

namespace Seeker.Services
{
    public class AndroidNetworkStatus : INetworkStatus
    {
        /// <inheritdoc/>
        public bool IsMetered => !NetworkStateService.CurrentConnectionIsUnmetered;

        /// <inheritdoc/>
        public bool IsVpnActive => NetworkStateService.CurrentConnectionIsVpn;

        public bool DoWeHaveInternet()
        {
            return ConnectionReceiver.DoWeHaveInternet();
        }

        public bool HasHandoffOccuredRecently()
        {
            return NetworkHandoffDetector.HasHandoffOccuredRecently();
        }
    }
}
