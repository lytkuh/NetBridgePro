using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

/// <summary>
/// Full-path speed test: runs HTTP transfers THROUGH the local proxy so the
/// measured number is the real end-to-end tunnel throughput
/// (PC → USB → phone → RAN → internet). Cloudflare endpoints by default.
/// </summary>
public class SpeedTest
{
    public class Result
    {
        public double DownloadMbps;
        public double UploadMbps;
        public double PingMs;
        public double JitterMs;
        public DateTime Time = DateTime.Now;
    }

    public class Progress
    {
        public string Phase = "";
        public double Percent;
        public double LiveMbps;
    }

    private CancellationTokenSource? _cts;

    public bool Running { get; private set; }
    public event Action<Progress>? OnProgress;
    public event Action<Result>? OnFinished;

    private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=";
    private const string UpUrl = "https://speed.cloudflare.com/__up";

    public void Cancel() => _cts?.Cancel();

    public async Task RunAsync(int phaseSeconds)
    {
        if (Running) return;
        Running = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var result = new Result();

        try
        {
            // ---- ping / jitter ----
            Report("ping", 0, 0);
            var pings = new List<double>();
            using (var pingClient = ClientThroughTunnel())
            {
                for (var i = 0; i < 5 && !ct.IsCancellationRequested; i++)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using var resp = await pingClient.GetAsync(DownUrl + "1", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        sw.Stop();
                        pings.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    catch { }
                    Report("ping", (i + 1) * 20.0 / 2, 0);
                }
            }
            if (pings.Count > 0)
            {
                result.PingMs = pings.Min();
                if (pings.Count > 1)
                {
                    var avg = pings.Average();
                    result.JitterMs = pings.Select(p => Math.Abs(p - avg)).Average();
                }
            }

            // ---- download ----
            Report("download", 0, 0);
            var downBytes = await TimedTransferAsync(ct, phaseSeconds, async (client, token, onChunk) =>
            {
                using var resp = await client.GetAsync(DownUrl + (50L * 1024 * 1024), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                await using var stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var buf = new byte[128 * 1024];
                while (true)
                {
                    var n = await stream.ReadAsync(buf, token).ConfigureAwait(false);
                    if (n <= 0) break;
                    onChunk(n);
                }
            }).ConfigureAwait(false);
            result.DownloadMbps = downBytes * 8.0 / (phaseSeconds * 1_000_000.0);

            // ---- upload ----
            Report("upload", 0, 0);
            var upBytes = await TimedTransferAsync(ct, phaseSeconds, async (client, token, onChunk) =>
            {
                var payload = new byte[256 * 1024];
                new Random(42).NextBytes(payload);
                using var content = new ByteArrayContent(payload);
                using var resp = await client.PostAsync(UpUrl, content, token).ConfigureAwait(false);
                onChunk(payload.Length);
            }).ConfigureAwait(false);
            result.UploadMbps = upBytes * 8.0 / (phaseSeconds * 1_000_000.0);

            OnFinished?.Invoke(result);
            Logger.Info("SpeedTest", $"down={result.DownloadMbps:F1} up={result.UploadMbps:F1} ping={result.PingMs:F0}ms jitter={result.JitterMs:F1}ms");
        }
        catch (OperationCanceledException)
        {
            Logger.Info("SpeedTest", "cancelled");
        }
        catch (Exception ex)
        {
            Logger.Error("SpeedTest", ex.Message);
            OnFinished?.Invoke(result);
        }
        finally
        {
            Running = false;
        }
    }

    private static HttpClient ClientThroughTunnel()
    {
        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{SettingsStore.Current.LocalProxyPort}"),
            UseProxy = true,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
    }

    private async Task<long> TimedTransferAsync(
        CancellationToken ct, int phaseSeconds,
        Func<HttpClient, CancellationToken, Action<int>, Task> transfer)
    {
        long total = 0;
        var sw = Stopwatch.StartNew();
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phaseCts.CancelAfter(TimeSpan.FromSeconds(phaseSeconds + 2));
        try
        {
            await transfer(ClientThroughTunnel(), phaseCts.Token, n =>
            {
                Interlocked.Add(ref total, n);
                var elapsed = sw.Elapsed.TotalSeconds;
                if (elapsed > 0.5)
                    Report(PhaseTag(), Math.Min(100, elapsed * 100.0 / phaseSeconds),
                           Interlocked.Read(ref total) * 8.0 / (elapsed * 1_000_000.0));
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* phase timer */ }
        sw.Stop();
        // use actual elapsed (bounded by phase window)
        var secs = Math.Max(0.5, Math.Min(sw.Elapsed.TotalSeconds, phaseSeconds + 2));
        return (long)(Interlocked.Read(ref total) * Math.Min(1.0, phaseSeconds / secs));
    }

    private string _phase = "";
    private string PhaseTag() => _phase;

    private void Report(string phase, double percent, double liveMbps)
    {
        _phase = phase;
        OnProgress?.Invoke(new Progress { Phase = phase, Percent = percent, LiveMbps = liveMbps });
    }
}
