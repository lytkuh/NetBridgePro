using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

using NetBridge.Windows.Core;

using System.IO;

namespace NetBridge.Windows.Net;

/// <summary>Session-level traffic counters + 1 s rate history for charts.</summary>
public class TrafficStats
{
    public static TrafficStats Instance { get; } = new();

    public long TotalRx { get; private set; }  // internet -> PC
    public long TotalTx { get; private set; }  // PC -> internet
    public long RateRx { get; private set; }
    public long RateTx { get; private set; }
    public int ActiveConnections => _records.Count(r => r.Active);

    public class Record
    {
        public long Id;
        public string Host = "";
        public int Port;
        public DateTime Started = DateTime.Now;
        public long Rx;
        public long Tx;
        public volatile bool Active = true;
    }

    private readonly ConcurrentQueue<Record> _records = new();
    private long _nextId = 1;
    private long _lastRx, _lastTx;

    public Record Begin(string host, int port)
    {
        var r = new Record { Id = Interlocked.Increment(ref _nextId), Host = host, Port = port };
        _records.Enqueue(r);
        while (_records.Count > 512 && _records.TryDequeue(out _)) { }
        return r;
    }

    public void Add(Record r, int bytes, bool rx)
    {
        if (rx) { TotalRx += bytes; r.Rx += bytes; }
        else { TotalTx += bytes; r.Tx += bytes; }
    }

    public void End(Record r) => r.Active = false;

    /// <summary>Called once per second by the UI/manager loop.</summary>
    public void Tick()
    {
        var drx = TotalRx - Interlocked.Read(ref _lastRx);
        var dtx = TotalTx - Interlocked.Read(ref _lastTx);
        RateRx = RateRx / 2 + drx / 2;
        RateTx = RateTx / 2 + dtx / 2;
        _lastRx = TotalRx;
        _lastTx = TotalTx;
    }
}

/// <summary>
/// PC-side local proxy (127.0.0.1). Accepts SOCKS5 and HTTP proxy from Windows
/// applications, applies rules (bypass domains, optional DoH), and forwards
/// through the phone's SOCKS5 endpoint over the active transport.
/// </summary>
public class LocalProxy
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private volatile string _phoneHost = "";
    private volatile int _phonePort;
    private volatile bool _ready;

    public static LocalProxy Instance { get; } = new();

    public bool Running { get; private set; }

    public void SetPhoneEndpoint(string host, int port)
    {
        _phoneHost = host;
        _phonePort = port;
        _ready = !string.IsNullOrEmpty(host);
        Logger.Info("LocalProxy", $"phone endpoint = {host}:{port}");
    }

    public void ClearPhoneEndpoint()
    {
        _ready = false;
    }

    private readonly object _startLock = new();

    /// <summary>Bind the loopback listener. Returns false (instead of throwing)
    /// when the port is taken — e.g. by a still-running older instance.</summary>
    public bool Start(int port)
    {
        lock (_startLock)
        {
            if (Running) return true;
            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start(64);
            }
            catch (SocketException ex)
            {
                Logger.Warn("LocalProxy", $"cannot bind 127.0.0.1:{port} - {ex.Message} (another NetBridge instance running?)");
                Running = false;
                return false;
            }
            Running = true;
            Logger.Info("LocalProxy", $"listening on 127.0.0.1:{port}");
            _ = AcceptLoopAsync(_cts.Token);
            return true;
        }
    }

    public void Stop()
    {
        Running = false;
        try { _listener?.Stop(); } catch { }
        _cts?.Cancel();
        Logger.Info("LocalProxy", "stopped");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveBufferSize = 256 * 1024;
            client.SendBufferSize = 256 * 1024;

            var stream = client.GetStream();
            // Peek first byte to detect protocol (SOCKS 0x04/0x05 vs HTTP ASCII)
            var first = new byte[1];
            var n = await stream.ReadAsync(first, ct).ConfigureAwait(false);
            if (n == 0) { client.Close(); return; }

            if (first[0] == 0x04 || first[0] == 0x05)
                await SocksAsync(client, stream, first[0], ct).ConfigureAwait(false);
            else
                await HttpAsync(client, stream, first[0], ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug("LocalProxy", "client: " + ex.Message);
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    // ==================== target resolution & policy ==================== //

    private static bool IsBypassed(string host)
    {
        foreach (var pattern in SettingsStore.Current.BypassDomains)
        {
            var p = pattern.Trim();
            if (p.Length == 0) continue;
            if (p.StartsWith("*."))
            {
                if (host.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (host.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private async Task<string?> ResolveHostAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out _)) return host; // already an IP
        var mode = SettingsStore.Current.DnsMode;
        if (mode == "remote") return null; // phone resolves it
        try
        {
            IPAddress[] ips = mode == "local-doh"
                ? await new DohClient(SettingsStore.Current.DohEndpoint).ResolveAsync(host, ct).ConfigureAwait(false)
                : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            var v4 = ips.FirstOrDefault(i => i.AddressFamily == AddressFamily.InterNetwork) ?? ips.FirstOrDefault();
            return v4?.ToString();
        }
        catch
        {
            return null; // fall back to remote resolution on the phone
        }
    }

    // ==================== upstream (phone) SOCKS5 client ==================== //

    private async Task<TcpClient> ConnectPhoneAsync(string targetHost, int targetPort, CancellationToken ct)
    {
        if (!_ready) throw new IOException("phone endpoint not ready");

        var phone = new TcpClient();
        phone.NoDelay = true;
        phone.ReceiveBufferSize = 256 * 1024;
        phone.SendBufferSize = 256 * 1024;
        await phone.ConnectAsync(_phoneHost, _phonePort, ct).ConfigureAwait(false);

        var s = phone.GetStream();
        var token = SettingsStore.Current.Token;

        // greeting: offer user/pass only (phone requires it when auth is on)
        await s.WriteAsync(new byte[] { 0x05, 0x01, 0x02 }, ct).ConfigureAwait(false);
        var resp = new byte[2];
        await ReadExactAsync(s, resp, ct).ConfigureAwait(false);
        if (resp[0] != 0x05) throw new IOException("bad socks version from phone");

        if (resp[1] == 0x02)
        {
            var user = Encoding.UTF8.GetBytes(Constants.ProxyUser);
            var pass = Encoding.UTF8.GetBytes(token);
            var auth = new byte[3 + user.Length + 1 + pass.Length];
            auth[0] = 0x01; auth[1] = (byte)user.Length;
            user.CopyTo(auth, 2);
            auth[2 + user.Length] = (byte)pass.Length;
            pass.CopyTo(auth, 3 + user.Length);
            await s.WriteAsync(auth, ct).ConfigureAwait(false);
            var authResp = new byte[2];
            await ReadExactAsync(s, authResp, ct).ConfigureAwait(false);
            if (authResp[1] != 0x00) { phone.Close(); throw new IOException("phone rejected credentials"); }
        }
        else if (resp[1] != 0x00)
        {
            phone.Close();
            throw new IOException("phone offered no supported auth method");
        }

        // CONNECT request — REMOTE DNS: send the hostname and let the PHONE
        // resolve it on its own network (its own DNS / VPN rules). Resolving on
        // the PC breaks when the PC's DNS is poisoned by VPN/proxy apps
        // (fake-DNS returns ::1 or unreachable addresses for the phone's
        // carrier) — the classic "connected but no internet".
        var useHost = targetHost;

        var req = BuildConnectRequest(useHost, targetPort);
        await s.WriteAsync(req, ct).ConfigureAwait(false);
        var head = new byte[4];
        await ReadExactAsync(s, head, ct).ConfigureAwait(false);
        if (head[1] != 0x00)
        {
            phone.Close();
            throw new IOException($"phone SOCKS error code {head[1]} for {targetHost}:{targetPort}");
        }
        // skip BND.ADDR/PORT
        if (head[3] == 0x03)
        {
            var lbuf = new byte[1];
            await ReadExactAsync(s, lbuf, ct).ConfigureAwait(false);
            var rest = new byte[lbuf[0] + 2]; // addr + port
            await ReadExactAsync(s, rest, ct).ConfigureAwait(false);
        }
        else if (head[3] == 0x01)
        {
            var rest = new byte[6]; // IPv4 + port
            await ReadExactAsync(s, rest, ct).ConfigureAwait(false);
        }
        else if (head[3] == 0x04)
        {
            var rest = new byte[18]; // IPv6 + port
            await ReadExactAsync(s, rest, ct).ConfigureAwait(false);
        }
        return phone;
    }

    private static byte[] BuildConnectRequest(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            var raw = ip.GetAddressBytes();
            var atyp = raw.Length == 4 ? (byte)0x01 : (byte)0x04;
            var req = new byte[4 + raw.Length + 2];
            req[0] = 5; req[1] = 1; req[2] = 0; req[3] = atyp;
            raw.CopyTo(req, 4);
            req[^2] = (byte)(port >> 8);
            req[^1] = (byte)(port & 0xFF);
            return req;
        }
        var hb = Encoding.UTF8.GetBytes(host);
        var reqd = new byte[4 + 1 + hb.Length + 2];
        reqd[0] = 5; reqd[1] = 1; reqd[2] = 0; reqd[3] = 0x03; reqd[4] = (byte)hb.Length;
        hb.CopyTo(reqd, 5);
        reqd[^2] = (byte)(port >> 8);
        reqd[^1] = (byte)(port & 0xFF);
        return reqd;
    }

    private static async Task ReadExactAsync(NetworkStream s, byte[] buf, CancellationToken ct)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(off, buf.Length - off), ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("stream ended");
            off += n;
        }
    }

    // ==================== SOCKS5 (server side) ==================== //

    private async Task SocksAsync(TcpClient client, NetworkStream stream, byte ver, CancellationToken ct)
    {
        if (ver != 0x05)
        {
            // SOCKS4/4a — minimal CONNECT support
            await Socks4Async(client, stream, ct).ConfigureAwait(false);
            return;
        }

        var mlen = new byte[1];
        await stream.ReadAsync(mlen, ct).ConfigureAwait(false);
        var methods = new byte[mlen[0]];
        await stream.ReadAsync(methods, ct).ConfigureAwait(false);
        await stream.WriteAsync(new byte[] { 0x05, 0x00 }, ct).ConfigureAwait(false); // NO-AUTH (loopback only)

        var head = new byte[4];
        await ReadExactAsync(stream, head, ct).ConfigureAwait(false);
        if (head[1] != 0x01) // CONNECT only
        {
            await stream.WriteAsync(new byte[] { 0x05, 0x07, 0, 1, 0, 0, 0, 0, 0, 0 }, ct).ConfigureAwait(false);
            return;
        }

        string host;
        if (head[3] == 0x01)
        {
            var ip = new byte[4];
            await stream.ReadAsync(ip, ct).ConfigureAwait(false);
            host = new IPAddress(ip).ToString();
        }
        else if (head[3] == 0x03)
        {
            var l = new byte[1];
            await stream.ReadAsync(l, ct).ConfigureAwait(false);
            var hb = new byte[l[0]];
            await stream.ReadAsync(hb, ct).ConfigureAwait(false);
            host = Encoding.UTF8.GetString(hb);
        }
        else if (head[3] == 0x04)
        {
            var ip = new byte[16];
            await stream.ReadAsync(ip, ct).ConfigureAwait(false);
            host = new IPAddress(ip).ToString();
        }
        else return;

        var pb = new byte[2];
        await stream.ReadAsync(pb, ct).ConfigureAwait(false);
        var port = (pb[0] << 8) | pb[1];

        await PumpThroughPhoneAsync(client, stream, host, port, ct, socksReply: (code) =>
            stream.WriteAsync(new byte[] { 0x05, code, 0, 1, 0, 0, 0, 0, 0, 0 }, ct)).ConfigureAwait(false);
    }

    private async Task Socks4Async(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[7];
        await stream.ReadAsync(head, ct).ConfigureAwait(false);
        var port = (head[1] << 8) | head[2];
        var ip = new byte[4] { head[3], head[4], head[5], head[6] };
        // userid until NUL
        var ub = new byte[1];
        while (true) { await stream.ReadAsync(ub, ct).ConfigureAwait(false); if (ub[0] == 0) break; }
        string host;
        if (ip[0] == 0 && ip[1] == 0 && ip[2] == 0 && ip[3] != 0)
        {
            // SOCKS4a domain
            var db = new List<byte>();
            while (true) { await stream.ReadAsync(ub, ct).ConfigureAwait(false); if (ub[0] == 0) break; db.Add(ub[0]); }
            host = Encoding.UTF8.GetString(db.ToArray());
        }
        else host = new IPAddress(ip).ToString();

        await PumpThroughPhoneAsync(client, stream, host, port, ct, socksReply: (code) =>
            stream.WriteAsync(new byte[] { 0, (byte)(code == 0 ? 0x5A : 0x5B), head[1], head[2], 0, 0, 0, 0 }, ct)).ConfigureAwait(false);
    }

    // ==================== HTTP proxy (server side) ==================== //

    private async Task HttpAsync(TcpClient client, NetworkStream stream, byte firstByte, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append((char)firstByte);
        var buf = new byte[1];
        while (sb.Length < 65536)
        {
            var n = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n == 0) return;
            sb.Append((char)buf[0]);
            var s = sb.ToString();
            if (s.EndsWith("\r\n\r\n") || s.EndsWith("\n\n")) break;
        }
        var head = sb.ToString();
        var lines = head.Replace("\r\n", "\n").Split('\n');
        var requestLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (requestLine == null) return;
        var parts = requestLine.Trim().Split(' ');
        if (parts.Length < 2) return;
        var method = parts[0].ToUpperInvariant();
        var target = parts[1];

        string host;
        int port;
        string? rewritePrefix = null;

        if (method == "CONNECT")
        {
            var hp = target.Split(':');
            host = hp[0];
            port = hp.Length > 1 && int.TryParse(hp[1], out var p1) ? p1 : 443;
        }
        else
        {
            // absolute-form
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                await WriteHttpErrorAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false);
                return;
            }
            host = uri.Host;
            port = uri.Port > 0 ? uri.Port : 80;
            rewritePrefix = (uri.PathAndQuery);
        }

        // Host header fallback for relative-form (rare, some legacy clients)
        if (method != "CONNECT" && string.IsNullOrEmpty(host))
        {
            var hostLine = lines.FirstOrDefault(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
            if (hostLine == null) { await WriteHttpErrorAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return; }
            var hv = hostLine.Substring(5).Trim();
            var hpp = hv.Split(':');
            host = hpp[0];
            port = hpp.Length > 1 && int.TryParse(hpp[1], out var p2) ? p2 : 80;
            rewritePrefix = parts[1];
        }

        try
        {
            if (method == "CONNECT")
            {
                var phone = await ConnectPhoneAsync(host, port, ct).ConfigureAwait(false);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), ct).ConfigureAwait(false);
                var rec = TrafficStats.Instance.Begin(host, port);
                await RelayAsync(stream, phone.GetStream(), rec, ct).ConfigureAwait(false);
            }
            else
            {
                var phone = await ConnectPhoneAsync(host, port, ct).ConfigureAwait(false);
                var upstream = phone.GetStream();

                // rebuild request head: strip hop-by-hop + proxy headers, force close
                var rsb = new StringBuilder();
                rsb.Append($"{method} {rewritePrefix ?? "/"} HTTP/1.1\r\n");
                foreach (var l in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    var lower = l.ToLowerInvariant();
                    if (lower.StartsWith("proxy-") || lower.StartsWith("connection:") ||
                        lower.StartsWith("keep-alive:") || lower.StartsWith("te:")) continue;
                    rsb.Append(l).Append("\r\n");
                }
                rsb.Append("Connection: close\r\n\r\n");
                var headBytes = Encoding.UTF8.GetBytes(rsb.ToString());
                await upstream.WriteAsync(headBytes, ct).ConfigureAwait(false);

                var rec = TrafficStats.Instance.Begin(host, port);
                TrafficStats.Instance.Add(rec, headBytes.Length, rx: false);
                await RelayAsync(stream, upstream, rec, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("LocalProxy", $"{method} {host}:{port} failed: {ex.Message}");
            try
            {
                if (method == "CONNECT")
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\n\r\n"), ct).ConfigureAwait(false);
                else
                    await WriteHttpErrorAsync(stream, 502, "Bad Gateway", ct).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static async Task WriteHttpErrorAsync(NetworkStream s, int code, string text, CancellationToken ct)
    {
        var body = Encoding.ASCII.GetBytes(text);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {text}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await s.WriteAsync(head, ct).ConfigureAwait(false);
        await s.WriteAsync(body, ct).ConfigureAwait(false);
    }

    // ==================== shared pump ==================== //

    private async Task PumpThroughPhoneAsync(
        TcpClient client, NetworkStream clientStream, string host, int port,
        CancellationToken ct, Func<byte, ValueTask> socksReply)
    {
        TcpClient? phone = null;
        try
        {
            if (IsBypassed(host))
            {
                // Direct from PC (bypass list)
                phone = new TcpClient();
                await phone.ConnectAsync(host, port, ct).ConfigureAwait(false);
                await socksReply(0).ConfigureAwait(false);
            }
            else
            {
                phone = await ConnectPhoneAsync(host, port, ct).ConfigureAwait(false);
                await socksReply(0).ConfigureAwait(false);
            }
            var rec = TrafficStats.Instance.Begin(host, port);
            await RelayAsync(clientStream, phone.GetStream(), rec, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug("LocalProxy", $"socks {host}:{port}: {ex.Message}");
            try { await socksReply(1).ConfigureAwait(false); } catch { }
            try { phone?.Close(); } catch { }
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    private async Task RelayAsync(NetworkStream a, NetworkStream b, TrafficStats.Record rec, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var t1 = PumpAsync(a, b, rec, rx: false, cts.Token);  // client -> internet
        var t2 = PumpAsync(b, a, rec, rx: true, cts.Token);   // internet -> client
        await Task.WhenAny(t1, t2).ConfigureAwait(false);
        cts.Cancel();
        try { await Task.WhenAll(t1, t2).ConfigureAwait(false); } catch { }
        TrafficStats.Instance.End(rec);
        try { a.Socket.Close(); b.Socket.Close(); } catch { }
    }

    private async Task PumpAsync(NetworkStream src, NetworkStream dst, TrafficStats.Record rec, bool rx, CancellationToken ct)
    {
        var limit = SettingsStore.Current.SpeedLimitBytesPerSec;
        var buf = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var n = await src.ReadAsync(buf, ct).ConfigureAwait(false);
                if (n <= 0) break;
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                TrafficStats.Instance.Add(rec, n, rx);
                if (limit > 0)
                {
                    // simple global limiter: proportional delay
                    var delayMs = (long)(n * 1000.0 / limit);
                    if (delayMs > 1) await Task.Delay((int)Math.Min(delayMs, 500), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }
}
