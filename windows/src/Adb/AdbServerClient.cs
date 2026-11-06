using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Adb;

/// <summary>
/// Minimal adb *server* protocol client. Talks to a private adb server instance
/// on 127.0.0.1:5038 (never the system one on 5037) so our key and forwards
/// stay isolated — the phone sees exactly one authorized host key.
/// </summary>
public class AdbServerClient
{
    private readonly string _host;
    private readonly int _port;

    public AdbServerClient(int port = Constants.AdbServerPort)
    {
        _host = "127.0.0.1";
        _port = port;
    }

    public class AdbDevice
    {
        public string Serial { get; init; } = "";
        public string State { get; init; } = "";
        public string Model { get; init; } = "";
        public override string ToString() => $"{Serial} [{State}] {Model}";
    }

    // ---------------- Low-level framing ---------------- //

    private async Task<NetworkStream> ConnectAsync(CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        tcp.ReceiveTimeout = 15000;
        tcp.SendTimeout = 15000;
        return tcp.GetStream();
    }

    private static async Task SendServiceAsync(Stream s, string service, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(service);
        var head = payload.Length.ToString("x4");
        var buf = new byte[4 + payload.Length];
        Encoding.ASCII.GetBytes(head, 0, head.Length, buf, 0);
        payload.CopyTo(buf, 4);
        await s.WriteAsync(buf, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads OKAY/FAIL; on FAIL throws with the server message.</summary>
    private static async Task ReadStatusAsync(Stream s, CancellationToken ct)
    {
        var status = new byte[4];
        await ReadExactAsync(s, status, ct).ConfigureAwait(false);
        var str = Encoding.ASCII.GetString(status);
        if (str == "OKAY") return;
        if (str == "FAIL")
        {
            var lenBuf = new byte[4];
            await ReadExactAsync(s, lenBuf, ct).ConfigureAwait(false);
            var len = ParseHex(lenBuf);
            var msg = new byte[len];
            await ReadExactAsync(s, msg, ct).ConfigureAwait(false);
            throw new AdbException("adb: " + Encoding.UTF8.GetString(msg));
        }
        throw new AdbException("adb: bad status '" + str + "'");
    }

    private static async Task<byte[]> ReadLengthPrefixedAsync(Stream s, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        await ReadExactAsync(s, lenBuf, ct).ConfigureAwait(false);
        var len = ParseHex(lenBuf);
        var payload = new byte[len];
        await ReadExactAsync(s, payload, ct).ConfigureAwait(false);
        return payload;
    }

    private static async Task ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(off, buf.Length - off), ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("adb connection closed");
            off += n;
        }
    }

    private static int ParseHex(byte[] hex)
    {
        var str = Encoding.ASCII.GetString(hex);
        return int.TryParse(str, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? v : throw new AdbException("bad hex length '" + str + "'");
    }

    private class AdbException : Exception
    {
        public AdbException(string m) : base(m) { }
    }

    // ---------------- Services ---------------- //

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(2500);
            await tcp.ConnectAsync(_host, _port, timeoutCts.Token).ConfigureAwait(false);
            var s = tcp.GetStream();
            await SendServiceAsync(s, "host:version", timeoutCts.Token).ConfigureAwait(false);
            await ReadStatusAsync(s, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }

    public async Task<List<AdbDevice>> DevicesAsync(CancellationToken ct = default)
    {
        var list = new List<AdbDevice>();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        var s = tcp.GetStream();
        await SendServiceAsync(s, "host:devices-l", ct).ConfigureAwait(false);
        await ReadStatusAsync(s, ct).ConfigureAwait(false);
        var data = await ReadLengthPrefixedAsync(s, ct).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(data);

        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var model = "";
            for (var i = 3; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("model:"))
                    model = parts[i].Substring(6).Replace('_', ' ');
            }
            list.Add(new AdbDevice { Serial = parts[0], State = parts[1], Model = model });
        }
        return list;
    }

    /// <summary>Runs a shell command on the device; returns stdout with \r stripped.</summary>
    public async Task<string> ShellAsync(string serial, string command, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        tcp.ReceiveTimeout = 30000;
        await tcp.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        var s = tcp.GetStream();

        await SendServiceAsync(s, $"host:transport:{serial}", ct).ConfigureAwait(false);
        await ReadStatusAsync(s, ct).ConfigureAwait(false);

        // Append RC marker so we can detect failures (shell v1 has no exit code)
        var marked = command.Contains("__RC__") ? command : $"({command}); echo __RC__=$?";
        await SendServiceAsync(s, "shell:" + marked, ct).ConfigureAwait(false);
        await ReadStatusAsync(s, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        var buf = new byte[8192];
        while (true)
        {
            var n = await s.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n <= 0) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
        }
        var output = sb.ToString().Replace("\r", "");
        return output;
    }

    /// <summary>Extracts the exit code injected by ShellAsync (best effort).</summary>
    public static int ExtractRc(string shellOutput, out string cleanOutput)
    {
        var idx = shellOutput.LastIndexOf("__RC__=", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var tail = shellOutput[(idx + 7)..].Trim();
            var end = tail.IndexOfAny(new[] { '\n', '\r', ' ' });
            var rcStr = end < 0 ? tail : tail[..end];
            cleanOutput = shellOutput[..idx].TrimEnd();
            return int.TryParse(rcStr, out var rc) ? rc : 0;
        }
        cleanOutput = shellOutput.TrimEnd();
        return 0;
    }

    public async Task<bool> ForwardAsync(string serial, int localPort, int remotePort, CancellationToken ct = default)
    {
        // Remove any previous forward for this port, then add (idempotent)
        try
        {
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
                var s = tcp.GetStream();
                await SendServiceAsync(s, $"host-serial:{serial}:killforward:tcp:{localPort}", ct).ConfigureAwait(false);
                await ReadStatusAsync(s, ct).ConfigureAwait(false);
            }
        }
        catch { /* no existing forward */ }

        using var tcp2 = new TcpClient();
        await tcp2.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        var s2 = tcp2.GetStream();
        await SendServiceAsync(s2, $"host-serial:{serial}:forward:tcp:{localPort};tcp:{remotePort}", ct).ConfigureAwait(false);
        await ReadStatusAsync(s2, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<string> TrackDeviceModelAsync(string serial, CancellationToken ct = default)
    {
        var outp = await ShellAsync(serial, "getprop ro.product.model", ct).ConfigureAwait(false);
        AdbServerClient.ExtractRc(outp, out var clean);
        return clean.Trim();
    }
}

/// <summary>Key management is delegated to adb itself (ADB_VENDOR_KEYS): the server
/// generates/loads keys under ProgramData\NetBridge\keys so we never reimplement
/// the Android RSAPublicKey wire format.</summary>
public static class AdbKey
{
    public static string KeyFile => Path.Combine(Constants.KeyDir, "adbkey");

    public static bool Exists => File.Exists(KeyFile);

    /// <summary>SHA-1 style fingerprint of the public key for display/pairing.</summary>
    public static string Fingerprint()
    {
        try
        {
            var pub = KeyFile + ".pub";
            if (!File.Exists(pub)) return "(adb will generate on first start)";
            var b64 = File.ReadAllText(pub).Trim().Split(' ')[0];
            var bytes = Convert.FromBase64String(b64);
            var hash = SHA1.HashData(bytes);
            return string.Join(":", hash.Select(b => b.ToString("x2")));
        }
        catch { return "?"; }
    }
}
