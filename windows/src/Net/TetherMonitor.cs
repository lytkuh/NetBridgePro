using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

public enum TransportKind
{
    None,
    UsbNcm,       // USB tethering interface (fastest)
    AdbTunnel,    // adb forward (no tethering needed)
    WifiHotspot,  // phone hotspot
    BluetoothPan  // emergency slow path
}

/// <summary>
/// Discovers the phone's proxy endpoint on any transport by probing candidate
/// gateway IPs for the authenticated SOCKS5 port. This avoids fragile
/// adapter-name matching: whoever answers with the right port IS the phone.
/// </summary>
public static class TetherMonitor
{
    public class ProbeResult
    {
        public TransportKind Kind;
        public string Host = "";
        public int Port;
        public string AdapterName = "";
    }

    public static bool IsUsbTetherAdapter(NetworkInterface ni)
    {
        try
        {
            if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet) return false;
            var d = ni.Description.ToLowerInvariant();
            var n = ni.Name.ToLowerInvariant();
            return d.Contains("ncm") || d.Contains("rndis") || d.Contains("remote ndis")
                || n.Contains("ncm") || n.Contains("rndis");
        }
        catch { return false; }
    }

    public static bool IsBluetoothPanAdapter(NetworkInterface ni)
    {
        try
        {
            var d = ni.Description.ToLowerInvariant();
            return ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                   (d.Contains("bluetooth") || d.Contains("bluetooth device (personal area network)"));
        }
        catch { return false; }
    }

    private static IEnumerable<string> GatewayCandidates(NetworkInterface ni)
    {
        var ips = new List<string>();
        try
        {
            foreach (var gw in ni.GetIPProperties().GatewayAddresses)
            {
                if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any))
                    ips.Add(gw.Address.ToString());
            }
        }
        catch { }
        return ips;
    }

    private static IEnumerable<NetworkInterface> ActiveAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        }
        catch { return Enumerable.Empty<NetworkInterface>(); }
    }

    /// <summary>Probe: TCP connect to the phone proxy port (59110). TCP success is
    /// enough for discovery; full auth happens when LocalProxy uses it.</summary>
    public static async Task<bool> ProbeAsync(string host, int port, int timeoutMs = 1500)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            var connect = tcp.ConnectAsync(host, port);
            var finished = await Task.WhenAny(connect, Task.Delay(timeoutMs, cts.Token)).ConfigureAwait(false);
            if (finished != connect) return false;
            await connect.ConfigureAwait(false); // propagate
            return tcp.Connected;
        }
        catch { return false; }
    }

    /// <summary>Search every transport for a reachable phone proxy. Order: USB-NCM,
    /// Wi-Fi hotspot, Bluetooth PAN.</summary>
    public static async Task<ProbeResult?> FindPhoneProxyAsync(CancellationToken ct)
    {
        var port = Constants.ProxyPort;

        foreach (var ni in ActiveAdapters())
        {
            TransportKind kind;
            if (IsUsbTetherAdapter(ni)) kind = TransportKind.UsbNcm;
            else if (IsBluetoothPanAdapter(ni)) kind = TransportKind.BluetoothPan;
            else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) kind = TransportKind.WifiHotspot;
            else continue;

            foreach (var gw in GatewayCandidates(ni))
            {
                ct.ThrowIfCancellationRequested();
                if (await ProbeAsync(gw, port).ConfigureAwait(false))
                {
                    return new ProbeResult
                    {
                        Kind = kind,
                        Host = gw,
                        Port = port,
                        AdapterName = ni.Name
                    };
                }
            }
        }
        return null;
    }

    /// <summary>USB tether interface adapter, if present (for status display).</summary>
    public static NetworkInterface? GetUsbTetherAdapter()
    {
        return ActiveAdapters().FirstOrDefault(IsUsbTetherAdapter);
    }

    public static bool IsUsbTetherUp() => GetUsbTetherAdapter() != null;
}
