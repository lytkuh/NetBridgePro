using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

/// <summary>
/// Minimal RFC 8484 DoH client (DNS wireformat over POST) with an in-memory
/// TTL cache. Parses A / AAAA / CNAME answers; falls back to the system
/// resolver on any failure.
/// </summary>
public class DohClient
{
    private class CacheEntry
    {
        public IPAddress[] Addresses = Array.Empty<IPAddress>();
        public DateTime ExpiresUtc;
    }

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly object _lock = new();

    public DohClient(string endpoint)
    {
        _endpoint = endpoint;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
    }

    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return new[] { literal };

        lock (_lock)
        {
            if (_cache.TryGetValue(host.ToLowerInvariant(), out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
                return hit.Addresses;
        }

        try
        {
            var query = BuildQuery(host, 1); // A
            var content = new ByteArrayContent(query);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            using var resp = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var answer = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var (ips, ttl) = ParseAnswer(answer);

            if (ips.Length == 0)
            {
                // try AAAA
                var query6 = BuildQuery(host, 28);
                var content6 = new ByteArrayContent(query6);
                content6.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
                using var resp6 = await _http.PostAsync(_endpoint, content6, ct).ConfigureAwait(false);
                var answer6 = await resp6.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                (ips, ttl) = ParseAnswer(answer6);
            }

            if (ips.Length > 0)
            {
                lock (_lock)
                {
                    _cache[host.ToLowerInvariant()] = new CacheEntry
                    {
                        Addresses = ips,
                        ExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(ttl, 30, 600))
                    };
                }
                return ips;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("DoH", "resolve failed for " + host + ": " + ex.Message);
        }

        // Fallback: system DNS
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            if (addrs.Length > 0) return addrs;
        }
        catch { }
        throw new SocketException((int)SocketError.HostNotFound);
    }

    // ---------------- DNS wireformat ---------------- //

    private static byte[] BuildQuery(string name, ushort type)
    {
        var ms = new MemoryStream();
        // header: id=0, flags=0x0100 (RD), qd=1
        WriteU16(ms, 0x0000); WriteU16(ms, 0x0100); WriteU16(ms, 1);
        WriteU16(ms, 0); WriteU16(ms, 0); WriteU16(ms, 0);
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var b = Encoding.UTF8.GetBytes(label);
            ms.WriteByte((byte)b.Length);
            ms.Write(b);
        }
        ms.WriteByte(0);
        WriteU16(ms, type);
        WriteU16(ms, 1); // IN
        return ms.ToArray();
    }

    private static (IPAddress[], uint) ParseAnswer(byte[] dns)
    {
        var ips = new List<IPAddress>();
        uint minTtl = 60;
        if (dns.Length < 12) return (ips.ToArray(), minTtl);
        int qd = BinaryPrimitives.ReadUInt16BigEndian(dns.AsSpan(4));
        int an = BinaryPrimitives.ReadUInt16BigEndian(dns.AsSpan(6));
        var off = 12;
        for (var i = 0; i < qd && off < dns.Length; i++) { SkipName(dns, ref off); off += 4; }
        for (var i = 0; i < an && off < dns.Length; i++)
        {
            SkipName(dns, ref off);
            if (off + 10 > dns.Length) break;
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(dns.AsSpan(off));
            ushort rdlen = BinaryPrimitives.ReadUInt16BigEndian(dns.AsSpan(off + 8));
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(dns.AsSpan(off + 4));
            off += 10;
            if (off + rdlen > dns.Length) break;
            if (type == 1 && rdlen == 4) ips.Add(new IPAddress(dns.AsSpan(off, 4).ToArray()));
            else if (type == 28 && rdlen == 16) ips.Add(new IPAddress(dns.AsSpan(off, 16).ToArray()));
            minTtl = Math.Min(minTtl, Math.Max(ttl, 30));
            off += rdlen;
        }
        return (ips.ToArray(), minTtl);
    }

    private static void SkipName(byte[] dns, ref int off)
    {
        while (off < dns.Length)
        {
            var len = dns[off];
            if (len == 0) { off += 1; return; }
            if ((len & 0xC0) == 0xC0) { off += 2; return; }
            off += 1 + len;
        }
    }

    private static void WriteU16(MemoryStream ms, ushort v)
    {
        ms.WriteByte((byte)(v >> 8));
        ms.WriteByte((byte)(v & 0xFF));
    }
}
