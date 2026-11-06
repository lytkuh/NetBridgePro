using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Bridge;

/// <summary>Live signal sample mirrored from the phone (SignalSample.kt).</summary>
public class SignalData
{
    public long TimestampMs { get; set; }
    public string NetworkType { get; set; } = "UNKNOWN";
    public string NrState { get; set; } = "NONE";
    public int? Rsrp { get; set; }
    public int? Rsrq { get; set; }
    public int? Rssi { get; set; }
    public int? Sinr { get; set; }
    public int? Cqi { get; set; }
    public string Band { get; set; } = "";
    public int? Pci { get; set; }
    public string Tac { get; set; } = "";
    public string CellId { get; set; } = "";
    public string Operator { get; set; } = "";
    public bool IsDataConnected { get; set; }
    public string Family { get; set; } = "UNKNOWN";

    public static SignalData FromJson(JsonElement el)
    {
        int? I(string n) => el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        string S(string n) => el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        return new SignalData
        {
            TimestampMs = el.TryGetProperty("timestampMs", out var t) ? t.GetInt64() : 0,
            NetworkType = S("networkType"),
            NrState = S("nrState"),
            Rsrp = I("rsrp"), Rsrq = I("rsrq"), Rssi = I("rssi"), Sinr = I("sinr"), Cqi = I("cqi"),
            Band = S("band"), Pci = I("pci"), Tac = S("tac"), CellId = S("cellId"),
            Operator = S("operator"),
            IsDataConnected = el.TryGetProperty("isDataConnected", out var d) && d.GetBoolean(),
            Family = S("family")
        };
    }
}

public class SubscriptionInfo
{
    public int SubId { get; set; }
    public int Slot { get; set; }
    public string Carrier { get; set; } = "";
    public int? PreferredMode { get; set; }
    public string NetworkType { get; set; } = "";
}

/// <summary>
/// JSON-lines client for the phone control bridge with HMAC-SHA256
/// challenge/response auth (mirror of BridgeServer.kt).
/// </summary>
public class BridgeClient : IDisposable
{
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly object _writeLock = new();
    private long _nextId = 1;
    private readonly Dictionary<long, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly CancellationTokenSource _cts = new();

    public bool IsConnected { get; private set; }
    public string Endpoint { get; private set; } = "";
    public SignalData? LastSignal { get; private set; }

    public event Action<bool>? ConnectionChanged;
    public event Action<SignalData>? SignalReceived;

    private static string HmacHex(string secret, string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        Dispose(false);
        Endpoint = $"{host}:{port}";
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
        tcp.NoDelay = true;
        _tcp = tcp;
        var stream = tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        IsConnected = true;
        ConnectionChanged?.Invoke(true);

        _ = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);

        // ---- HMAC handshake ----
        var nonce = Guid.NewGuid().ToString("N");
        var authResp = await RequestAsync("auth", new { nonce }, ct).ConfigureAwait(false)
            ?? throw new IOException("bridge: no auth response");
        var serverNonce = authResp.GetProperty("nonce").GetString() ?? "";
        var proof = HmacHex(SettingsStore.Current.Token, nonce + "|" + serverNonce);
        var verified = await RequestAsync("auth.verify", new { nonce, proof }, ct).ConfigureAwait(false)
            ?? throw new IOException("bridge: no verify response");
        Logger.Info("Bridge", $"authenticated to {Endpoint}");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                HandleLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Debug("Bridge", "reader ended: " + ex.Message);
        }
        finally
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(false);
        }
    }

    private void HandleLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();
            if (root.TryGetProperty("id", out var idEl))
            {
                var id = idEl.GetInt64();
                TaskCompletionSource<JsonElement?>? tcs;
                lock (_pending)
                {
                    if (_pending.TryGetValue(id, out tcs)) _pending.Remove(id);
                }
                tcs?.TrySetResult(root.TryGetProperty("result", out var res) ? res.Clone() : (JsonElement?)null);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("Bridge", "bad line: " + ex.Message);
        }
    }

    public async Task<JsonElement?> RequestAsync(string cmd, object? args, CancellationToken ct)
    {
        if (_writer == null) throw new IOException("bridge not connected");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;

        var payload = args == null
            ? JsonSerializer.Serialize(new { id, cmd })
            : JsonSerializer.Serialize(new { id, cmd, args });

        lock (_writeLock)
        {
            _writer.WriteLine(payload);
        }

        try
        {
            var task = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(20), ct)).ConfigureAwait(false);
            if (task != tcs.Task) return null;
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_pending) _pending.Remove(id);
        }
    }

    // ---------------- Typed helpers ---------------- //

    public async Task<(int? Battery, double Temp, bool AdbOn, bool Wss)?> GetDeviceInfoAsync(CancellationToken ct)
    {
        var r = await RequestAsync("device.info", null, ct).ConfigureAwait(false);
        if (r == null) return null;
        var v = r.Value;
        return (
            v.TryGetProperty("batteryLevel", out var b) ? b.GetInt32() : null,
            v.TryGetProperty("batteryTempC", out var t) ? t.GetDouble() : -1,
            v.TryGetProperty("adbEnabled", out var a) && a.GetBoolean(),
            v.TryGetProperty("writeSecureSettings", out var w) && w.GetBoolean()
        );
    }

    public async Task<SignalData?> PollSignalAsync(CancellationToken ct)
    {
        var r = await RequestAsync("net.type", null, ct).ConfigureAwait(false);
        if (r == null || r.Value.ValueKind != JsonValueKind.Object) return null;
        var s = SignalData.FromJson(r.Value);
        LastSignal = s;
        SignalReceived?.Invoke(s);
        return s;
    }

    public async Task<List<SubscriptionInfo>?> GetSubscriptionsAsync(CancellationToken ct)
    {
        var r = await RequestAsync("net.info", null, ct).ConfigureAwait(false);
        if (r == null) return null;
        var list = new List<SubscriptionInfo>();
        if (r.Value.TryGetProperty("subscriptions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                list.Add(new SubscriptionInfo
                {
                    SubId = el.TryGetProperty("subId", out var sid) ? sid.GetInt32() : -1,
                    Slot = el.TryGetProperty("slot", out var sl) ? sl.GetInt32() : 0,
                    Carrier = el.TryGetProperty("carrier", out var c) ? c.GetString() ?? "" : "",
                    PreferredMode = el.TryGetProperty("preferredMode", out var pm) && pm.ValueKind == JsonValueKind.Number ? pm.GetInt32() : null,
                    NetworkType = el.TryGetProperty("networkType", out var nt) ? nt.GetString() ?? "" : ""
                });
            }
        }
        return list;
    }

    public async Task<bool> WritePreferredModeAsync(int slot, int value, CancellationToken ct)
    {
        var r = await RequestAsync("net.mode.write", new { slot, value }, ct).ConfigureAwait(false);
        return r != null;
    }

    public async Task<int?> ReadPreferredModeAsync(int slot, CancellationToken ct)
    {
        var r = await RequestAsync("net.mode.read", new { slot }, ct).ConfigureAwait(false);
        if (r == null) return null;
        return r.Value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }

    public async Task<bool> IsUsbTetheredAsync(CancellationToken ct)
    {
        var r = await RequestAsync("tether.status", null, ct).ConfigureAwait(false);
        if (r == null) return false;
        return r.Value.TryGetProperty("usbTethered", out var v) && v.GetBoolean();
    }

    public async Task<(long Rx, long Tx, int Active)?> GetProxyStatsAsync(CancellationToken ct)
    {
        var r = await RequestAsync("proxy.stats", null, ct).ConfigureAwait(false);
        if (r == null) return null;
        return (
            r.Value.TryGetProperty("rxBytes", out var rx) ? rx.GetInt64() : 0,
            r.Value.TryGetProperty("txBytes", out var tx) ? tx.GetInt64() : 0,
            r.Value.TryGetProperty("activeConnections", out var ac) ? ac.GetInt32() : 0
        );
    }

    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }

    private void Dispose(bool disposing)
    {
        try { _cts.Cancel(); } catch { }
        try { _tcp?.Close(); } catch { }
        _tcp = null; _reader = null; _writer = null;
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(false);
        }
        lock (_pending)
        {
            foreach (var tcs in _pending.Values) tcs.TrySetCanceled();
            _pending.Clear();
        }
        if (disposing && !_cts.IsCancellationRequested) { }
    }
}
