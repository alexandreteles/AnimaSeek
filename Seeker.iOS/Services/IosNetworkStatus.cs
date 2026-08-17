using CoreFoundation;
using Network;
using Seeker.Services;

namespace AnimaSeek.iOS.Services;

/// <summary>Tracks reachability and cost changes through Apple's Network framework.</summary>
internal sealed class IosNetworkStatus : INetworkStatus, IDisposable
{
    private readonly NWPathMonitor monitor = new();
    private readonly DispatchQueue queue = new("com.animaseek.app.network-monitor");
    private NetworkSnapshot snapshot = new(
        NWPathStatus.Invalid,
        IsExpensive: false,
        IsConstrained: false,
        UsesLocalNetwork: false,
        DateTimeOffset.MinValue);

    /// <summary>Starts monitoring the current default network path.</summary>
    public IosNetworkStatus()
    {
        monitor.SnapshotHandler = path =>
        {
            bool newUsesLocalNetwork =
                path.UsesInterfaceType(NWInterfaceType.Wifi) ||
                path.UsesInterfaceType(NWInterfaceType.Wired);
            NetworkSnapshot previous = Volatile.Read(ref snapshot);
            bool changed =
                path.Status != previous.Status ||
                path.IsExpensive != previous.IsExpensive ||
                path.IsConstrained != previous.IsConstrained ||
                newUsesLocalNetwork != previous.UsesLocalNetwork;
            Volatile.Write(ref snapshot, new NetworkSnapshot(
                path.Status,
                path.IsExpensive,
                path.IsConstrained,
                newUsesLocalNetwork,
                changed ? DateTimeOffset.UtcNow : previous.LastHandoff));
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };
        monitor.SetQueue(queue);
        monitor.Start();
    }

    /// <summary>Raised whenever reachability, metering, or constrained-network state changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Gets whether the active route is marked expensive or constrained.</summary>
    public bool IsMetered
    {
        get
        {
            NetworkSnapshot current = Volatile.Read(ref snapshot);
            return current.IsExpensive || current.IsConstrained;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Network framework classifies VPNs together with other virtual or unknown interfaces and does not expose a
    /// definitive process-route VPN flag. Returning false avoids treating an unrelated virtual route as a VPN.
    /// </remarks>
    public bool IsVpnActive => false;

    /// <summary>
    /// Gets whether the satisfied default route uses Wi-Fi or wired Ethernet and can therefore have a local gateway.
    /// </summary>
    public bool IsLocalNetworkAvailable
    {
        get
        {
            NetworkSnapshot current = Volatile.Read(ref snapshot);
            return current.Status == NWPathStatus.Satisfied && current.UsesLocalNetwork;
        }
    }

    /// <inheritdoc/>
    public bool DoWeHaveInternet() => Volatile.Read(ref snapshot).Status == NWPathStatus.Satisfied;

    /// <inheritdoc/>
    public bool HasHandoffOccuredRecently() =>
        DateTimeOffset.UtcNow - Volatile.Read(ref snapshot).LastHandoff < TimeSpan.FromSeconds(30);

    /// <summary>Stops the Network framework monitor and releases its native resources.</summary>
    public void Dispose()
    {
        monitor.Cancel();
        monitor.Dispose();
        queue.Dispose();
    }

    private sealed record NetworkSnapshot(
        NWPathStatus Status,
        bool IsExpensive,
        bool IsConstrained,
        bool UsesLocalNetwork,
        DateTimeOffset LastHandoff);
}
