using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Adb;

/// <summary>
/// Manages a *private* adb server instance:
///   - dedicated port 5038 (never touches the system 5037 server)
///   - dedicated key via ADB_VENDOR_KEYS (ProgramData\NetBridge\keys\adbkey)
///   - optional automatic download of Google platform-tools
/// </summary>
public static class AdbServerManager
{
    private const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private static readonly object Lock = new();

    public static string? ResolvedAdbPath { get; private set; }
    public static string DownloadProgress { get; private set; } = "";

    public static event Action<string>? OnDownloadProgress;

    // ---------------- Path resolution ---------------- //

    public static string? FindAdb()
    {
        // 1. user override
        if (!string.IsNullOrWhiteSpace(SettingsStore.Current.AdbPath) && File.Exists(SettingsStore.Current.AdbPath))
            return SettingsStore.Current.AdbPath;

        // 2. bundled (auto-downloaded) platform-tools
        var bundled = Path.Combine(Constants.ToolsDir, "platform-tools", "adb.exe");
        if (File.Exists(bundled)) return bundled;

        // 2.5 portable platform-tools shipped beside the exe (ZIP / setup layout)
        var portable = Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
        if (File.Exists(portable)) return portable;

        // 3. PATH
        try
        {
            var psi = new ProcessStartInfo("where", "adb")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var first = p.StandardOutput.ReadLine()?.Trim();
                p.WaitForExit(3000);
                if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
            }
        }
        catch { }

        // 4. default Android SDK location
        var sdk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android", "Sdk", "platform-tools", "adb.exe");
        return File.Exists(sdk) ? sdk : null;
    }

    // ---------------- Server lifecycle ---------------- //

    /// <summary>Ensures our private server is running. Safe to call repeatedly.</summary>
    public static async Task<bool> EnsureServerAsync(CancellationToken ct = default)
    {
        var adb = FindAdb();
        if (adb == null)
        {
            Logger.Warn("Adb", "adb.exe not found — install platform-tools from Settings page");
            return false;
        }
        ResolvedAdbPath = adb;

        var client = new AdbServerClient();
        if (await client.PingAsync(ct).ConfigureAwait(false))
        {
            Logger.Debug("Adb", $"private adb server already up on :{Constants.AdbServerPort}");
            return true;
        }

        // Start with our key + port. adb creates the key pair on first run.
        Constants.EnsureDirs();
        var psi = new ProcessStartInfo(adb, $"-P {Constants.AdbServerPort} start-server")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        psi.EnvironmentVariables["ADB_VENDOR_KEYS"] = AdbKey.KeyFile;
        psi.EnvironmentVariables["ANDROID_ADB_SERVER_PORT"] = Constants.AdbServerPort.ToString();

        try
        {
            using var p = Process.Start(psi);
            if (p != null)
            {
                _ = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Adb", "failed to start server: " + ex.Message);
            return false;
        }

        // allow server bootstrap
        for (var i = 0; i < 20; i++)
        {
            if (await client.PingAsync(ct).ConfigureAwait(false))
            {
                Logger.Info("Adb", $"private adb server started (port={Constants.AdbServerPort}, key={AdbKey.Fingerprint()})");
                return true;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return false;
    }

    public static void ShutdownServer()
    {
        try
        {
            var adb = ResolvedAdbPath ?? FindAdb();
            if (adb == null) return;
            var psi = new ProcessStartInfo(adb, $"-P {Constants.AdbServerPort} kill-server")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["ANDROID_ADB_SERVER_PORT"] = Constants.AdbServerPort.ToString();
            using var p = Process.Start(psi);
            p?.WaitForExit(4000);
            Logger.Info("Adb", "private adb server stopped");
        }
        catch { }
    }

    // ---------------- platform-tools download ---------------- //

    public static bool IsPlatformToolsInstalled() =>
        File.Exists(Path.Combine(Constants.ToolsDir, "platform-tools", "adb.exe"));

    public static async Task<bool> DownloadPlatformToolsAsync(CancellationToken ct = default)
    {
        try
        {
            Constants.EnsureDirs();
            var zipPath = Path.Combine(Path.GetTempPath(), "platform-tools.zip");
            Report($"Downloading {PlatformToolsUrl} …");

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var resp = await http.GetAsync(PlatformToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;

            await using (var fs = File.Create(zipPath))
            await using (var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                var buf = new byte[1 << 16];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    read += n;
                    if (total > 0)
                        Report($"{read / 1048576.0:F1} / {total / 1048576.0:F1} MB");
                }
            }

            Report("Extracting…");
            var extractDir = Constants.ToolsDir;
            if (Directory.Exists(extractDir))
            {
                try { Directory.Delete(extractDir, true); } catch { }
            }
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { }

            var ok = IsPlatformToolsInstalled();
            Report(ok ? "platform-tools installed." : "Extraction failed — adb.exe missing");
            return ok;
        }
        catch (Exception ex)
        {
            Report("Download failed: " + ex.Message);
            return false;
        }
    }

    private static void Report(string msg)
    {
        DownloadProgress = msg;
        Logger.Info("Adb", msg);
        OnDownloadProgress?.Invoke(msg);
    }
}
