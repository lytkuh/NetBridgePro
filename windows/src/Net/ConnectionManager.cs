using System.Diagnostics;
using System.Net;
using System.Net.Http;

using NetBridge.Windows.Adb;
using NetBridge.Windows.Bridge;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

/// <summary>Observable connection state for the UI.</summary>
public class ConnectionState : Core.ObservableBase
{
    private bool _connected;
    private string _transport = "tr_none";
    private string _endpoint = "—";
    private string _statusText = "conn_disconnected";
    private bool _tetherUp;
    private bool _bridgeAuthed;
    private int? _battery = -1;
    private double _temp = -1;
    private string _phoneModel = "";
    private string _adbSerial = "";
    private long _phoneRx, _phoneTx;

    public bool Connected { get => _connected; set => Set(ref _connected, value); }
    public string Transport { get => _transport; set => Set(ref _transport, value); }
    public string Endpoint { get => _endpoint; set => Set(ref _endpoint, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public bool TetherUp { get => _tetherUp; set => Set(ref _tetherUp, value); }
    public bool BridgeAuthed { get => _bridgeAuthed; set => Set(ref _bridgeAuthed, value); }
    public int? Battery { get => _battery; set => Set(ref _battery, value); }
    public double Temp { get => _temp; set => Set(ref _temp, value); }
    public string PhoneModel { get => _phoneModel; set => Set(ref _phoneModel, value); }
    public string AdbSerial { get => _adbSerial; set => Set(ref _adbSerial, value); }
    public long PhoneRx { get => _phoneRx; set => Set(ref _phoneRx, value); }
    public long PhoneTx { get => _phoneTx; set => Set(ref _phoneTx, value); }
}

/// <summary>
/// Orchestrates the whole connection lifecycle:
///   adb (private server) → enable NCM tethering → probe proxy →
///   fallback adb-forward tunnel → bridge auth → system proxy →
///   signal polling + auto-reconnect.
/// </summary>
public class ConnectionManager
{
    public static ConnectionManager Instance { get; } = new();

    public ConnectionState State { get; } = new();
    public BridgeClient Bridge { get; } = new();

    private readonly AdbServerClient _adb = new();
    private string? _serial;
    private CancellationTokenSource? _lifetimeCts;
    private Timer? _autoTimer;
    private bool _connecting;
    private bool _grantsDone;          // zero-touch pm-grant runs once per session
    private TransportKind _active = TransportKind.None;

    public event Action<SignalData>? Signal;

    private ConnectionManager()
    {
        Bridge.ConnectionChanged += authed =>
        {
            State.BridgeAuthed = authed;
            if (!authed && State.Connected)
            {
                Logger.Warn("Conn", "bridge lost — scheduling reconnect");
            }
        };
        Bridge.SignalReceived += s =>
        {
            Signal?.Invoke(s);
            SmartSwitcher.Instance.Feed(s);
        };
    }

    // ---------------- public API ---------------- //

    public async Task ConnectAsync()
    {
        if (_connecting) return;
        _connecting = true;
        State.StatusText = "conn_connecting";
        try
        {
            await ConnectCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Conn", "connect failed: " + ex.Message);
            State.StatusText = "conn_disconnected";
        }
        finally
        {
            _connecting = false;
        }
    }

    public void Disconnect()
    {
        Logger.Info("Conn", "disconnect requested");
        Bridge.Dispose();
        LocalProxy.Instance.ClearPhoneEndpoint();
        SystemProxySetter.RestoreIfNeeded();
        _active = TransportKind.None;
        State.Connected = false;
        State.Transport = "tr_none";
        State.Endpoint = "—";
        State.StatusText = "conn_disconnected";
    }

    public void SetAutoConnect(bool enabled)
    {
        SettingsStore.Current.AutoConnect = enabled;
        SettingsStore.Save();
        if (enabled)
        {
            _autoTimer ??= new Timer(async _ =>
            {
                if (!_connecting && !State.Connected && SettingsStore.Current.AutoConnect)
                {
                    try { await ConnectAsync().ConfigureAwait(false); } catch { }
                }
            }, null, 5000, 7000);
        }
    }

    public void Shutdown()
    {
        try { _autoTimer?.Dispose(); } catch { }
        try { Bridge.Dispose(); } catch { }
    }

    // ---------------- core sequence ---------------- //

    private async Task ConnectCoreAsync()
    {
        _lifetimeCts = new CancellationTokenSource();
        var ct = _lifetimeCts.Token;
        var security = SettingsStore.Current.SecurityMode;

        // 1) adb (companion mode only) — installed automatically if missing
        if (security == "companion")
        {
            var adbUp = await AdbServerManager.EnsureServerAsync(ct).ConfigureAwait(false);
            if (!adbUp && AdbServerManager.FindAdb() == null)
            {
                // zero-touch: adb not present — fetch platform-tools automatically (needs internet once)
                Logger.Info("Conn", "adb not found — auto-installing platform-tools");
                State.StatusText = "conn_connecting";
                if (await AdbServerManager.DownloadPlatformToolsAsync(ct).ConfigureAwait(false))
                    adbUp = await AdbServerManager.EnsureServerAsync(ct).ConfigureAwait(false);
            }
            if (adbUp)
            {
                var devices = await _adb.DevicesAsync(ct).ConfigureAwait(false);
                var dev = devices.FirstOrDefault(d => d.State == "device")
                       ?? devices.FirstOrDefault(d => d.State == "unauthorized");
                if (dev != null)
                {
                    _serial = dev.Serial;
                    State.AdbSerial = dev.Serial;
                    State.PhoneModel = dev.Model;
                    if (dev.State == "unauthorized")
                    {
                        Logger.Warn("Conn", "device unauthorized — accept the USB debugging dialog on the phone");
                    }
                    else if (dev.State == "device")
                    {
                        // zero-touch: keep the token in sync + silently grant phone-app permissions
                        await TokenSync.SyncAsync(dev.Serial, ct).ConfigureAwait(false);
                        if (!_grantsDone)
                        {
                            await AutoGrantAsync(ct).ConfigureAwait(false);
                            _grantsDone = true;
                        }
                    }
                }
            }
        }

        // 2) USB tethering via shell (best path)
        if (_serial != null && SettingsStore.Current.PreferNcmTethering && !TetherMonitor.IsUsbTetherUp())
        {
            Logger.Info("Conn", "enabling USB tethering (ncm) via adb shell");
            var outp = await _adb.ShellAsync(_serial, "svc usb setFunctions ncm 2>/dev/null || svc usb setFunctions rndis", ct).ConfigureAwait(false);
            AdbServerClient.ExtractRc(outp, out _);
            // wait for the interface
            for (var i = 0; i < 16 && !TetherMonitor.IsUsbTetherUp(); i++)
                await Task.Delay(500, ct).ConfigureAwait(false);
        }
        State.TetherUp = TetherMonitor.IsUsbTetherUp();

        // 3) discover proxy endpoint
        var probe = await TetherMonitor.FindPhoneProxyAsync(ct).ConfigureAwait(false);

        if (probe == null && _serial != null && security == "companion")
        {
            // 4) fallback: adb forward tunnel (works even without tethering)
            Logger.Info("Conn", "no direct endpoint — falling back to ADB tunnel");
            try
            {
                await _adb.ForwardAsync(_serial, Constants.AdbForwardData, Constants.ProxyPort, ct).ConfigureAwait(false);
                await _adb.ForwardAsync(_serial, Constants.AdbForwardBridge, Constants.BridgePort, ct).ConfigureAwait(false);
                if (await TetherMonitor.ProbeAsync("127.0.0.1", Constants.AdbForwardData).ConfigureAwait(false))
                {
                    probe = new TetherMonitor.ProbeResult
                    {
                        Kind = TransportKind.AdbTunnel,
                        Host = "127.0.0.1",
                        Port = Constants.AdbForwardData,
                        AdapterName = "adb"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Conn", "adb tunnel failed: " + ex.Message);
            }
        }

        if (probe == null)
        {
            Logger.Warn("Conn", "phone proxy not reachable on any transport");
            State.StatusText = "conn_disconnected";
            return;
        }

        // 5) activate data plane + bridge
        _active = probe.Kind;
        State.Transport = probe.Kind switch
        {
            TransportKind.UsbNcm => "tr_ncm",
            TransportKind.AdbTunnel => "tr_adb",
            TransportKind.WifiHotspot => "tr_wifi",
            TransportKind.BluetoothPan => "tr_bt",
            _ => "tr_none"
        };
        State.Endpoint = $"{probe.Host}:{probe.Port}";
        LocalProxy.Instance.SetPhoneEndpoint(probe.Host, probe.Port);

        var bridgeHost = probe.Kind == TransportKind.AdbTunnel ? "127.0.0.1" : probe.Host;
        var bridgePort = probe.Kind == TransportKind.AdbTunnel ? Constants.AdbForwardBridge : Constants.BridgePort;
        try
        {
            using var bc = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await Bridge.ConnectAsync(bridgeHost, bridgePort, bc.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn("Conn", "bridge connect failed: " + ex.Message);
            if (ex.Message.Contains("no verify") || ex.Message.Contains("no auth"))
                Logger.Warn("Conn",
                    "pairing token mismatch or phone bridge not responding — " +
                    "phone app: Security tab → Copy token → PC: Settings → Pairing token; " +
                    "or plug the USB cable and press 'Run: activate & grant permissions' then 'Sync from phone'. " +
                    "Also restart 'sharing' on the phone once after an app update.");
        }

        // 6) phone info
        if (Bridge.IsConnected)
        {
            try
            {
                var info = await Bridge.GetDeviceInfoAsync(ct).ConfigureAwait(false);
                if (info != null)
                {
                    State.Battery = info.Value.Battery;
                    State.Temp = info.Value.Temp;
                }
            }
            catch { }
        }

        // 7) system proxy
        if (SettingsStore.Current.SetSystemProxy)
        {
            if (!LocalProxy.Instance.Running)
                LocalProxy.Instance.Start(SettingsStore.Current.LocalProxyPort);
            SystemProxySetter.Apply(SettingsStore.Current.LocalProxyPort);
        }
        else if (!LocalProxy.Instance.Running)
        {
            LocalProxy.Instance.Start(SettingsStore.Current.LocalProxyPort);
        }

        State.Connected = true;
        State.StatusText = "conn_connected";
        Logger.Info("Conn", $"connected via {State.Transport} ({State.Endpoint})");

        // 8) end-to-end internet verification + one-shot self-heal.
        // TCP "connected" says nothing about the data plane — a token mismatch
        // makes the phone proxy close every SOCKS session ("stream ended").
        // This verifies a REAL request through the whole chain and, on failure
        // with a USB device attached, re-syncs the token and verifies again.
        _ = VerifyInternetLoopAsync(ct);

        // 9) loops
        _ = MonitorLoopAsync(ct);
    }

    /// <summary>True when a 204/2xx comes back through the full tunnel.</summary>
    public static async Task<bool> VerifyInternetAsync(int localProxyPort, CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{localProxyPort}"),
                UseProxy = true,
                AllowAutoRedirect = false
            };
            using var http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            using var resp = await http.GetAsync("http://www.gstatic.com/generate_204", ct).ConfigureAwait(false);
            return (int)resp.StatusCode is 204 or >= 200 and < 300;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // our own timeout
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task VerifyInternetLoopAsync(CancellationToken ct)
    {
        var port = SettingsStore.Current.LocalProxyPort;
        await Task.Delay(2500, ct).ConfigureAwait(false);
        if (!State.Connected) return;

        if (await VerifyInternetAsync(port, ct).ConfigureAwait(false))
        {
            Logger.Info("Conn", "INTERNET OK — verified end-to-end through the tunnel");
            return;
        }
        Logger.Warn("Conn", "internet check FAILED through the tunnel — trying to self-heal (token re-sync)…");

        // self-heal: if a USB device is attached, re-pull the phone token
        try
        {
            var serial = _serial;
            if (serial != null)
            {
                await TokenSync.SyncAsync(serial, ct).ConfigureAwait(false);
            }
            else
            {
                Logger.Warn("Conn",
                    "no USB device for token re-sync — paste the phone token manually " +
                    "(phone: Security → Copy → PC: Settings → Pairing token), or connect the cable");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Conn", "token re-sync failed: " + ex.Message);
        }

        await Task.Delay(2000, ct).ConfigureAwait(false);
        if (!State.Connected) return;

        if (await VerifyInternetAsync(port, ct).ConfigureAwait(false))
        {
            Logger.Info("Conn", "INTERNET OK after token re-sync — self-heal successful");
        }
        else
        {
            Logger.Warn("Conn",
                "still no internet — checklist: 1) phone mobile data ON, 2) sharing running on the phone " +
                "(stop/start once), 3) token pasted correctly, 4) phone VPN off");
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        var sec = 0;
        while (!ct.IsCancellationRequested && State.Connected)
        {
            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
                sec++;
                TrafficStats.Instance.Tick();

                if (Bridge.IsConnected && sec % 2 == 0)
                {
                    await Bridge.PollSignalAsync(ct).ConfigureAwait(false);
                    var st = await Bridge.GetProxyStatsAsync(ct).ConfigureAwait(false);
                    if (st != null)
                    {
                        State.PhoneRx = st.Value.Rx;
                        State.PhoneTx = st.Value.Tx;
                    }
                }

                if (sec % 10 == 0)
                {
                    State.TetherUp = TetherMonitor.IsUsbTetherUp();
                    // liveness: endpoint still answers?
                    var host = _active == TransportKind.AdbTunnel ? "127.0.0.1" : State.Endpoint.Split(':')[0];
                    var port = int.Parse(State.Endpoint.Split(':')[1]);
                    if (!await TetherMonitor.ProbeAsync(host, port, 1200).ConfigureAwait(false))
                    {
                        Logger.Warn("Conn", "endpoint lost — disconnecting");
                        Disconnect();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.Debug("Conn", "monitor: " + ex.Message);
            }
        }
    }

    // ---------------- adb helpers for other modules ---------------- //

    /// <summary>
    /// Zero-touch: silently grants the phone app the permissions it cannot ask for
    /// itself (WRITE_SECURE_SETTINGS etc.). `pm grant` is idempotent — safe to re-run.
    /// </summary>
    private async Task AutoGrantAsync(CancellationToken ct)
    {
        if (_serial == null) return;
        string[] grants =
        {
            "android.permission.WRITE_SECURE_SETTINGS",
            "android.permission.READ_PHONE_STATE",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.POST_NOTIFICATIONS"
        };
        foreach (var perm in grants)
            await _adb.ShellAsync(_serial, $"pm grant {Constants.AndroidPackageId} {perm}", ct).ConfigureAwait(false);
        Logger.Info("Conn", "phone-app permissions ensured (zero-touch)");
    }

    public async Task<bool> RunShellAsync(string command)
    {
        if (_serial == null) return false;
        try
        {
            var outp = await _adb.ShellAsync(_serial, command, CancellationToken.None).ConfigureAwait(false);
            var rc = AdbServerClient.ExtractRc(outp, out var clean);
            Logger.Debug("Shell", $"{command} -> rc={rc} {clean}");
            return rc == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Shell", command + " failed: " + ex.Message);
            return false;
        }
    }

    public string? Serial => _serial;
}
