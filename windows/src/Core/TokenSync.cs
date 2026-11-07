using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using NetBridge.Windows.Adb;

namespace NetBridge.Windows.Core;

/// <summary>
/// The Android app owns the shared secret (Security tab on the phone).
/// Release builds cannot be read via `run-as`, so the primary flow opens a
/// short "pairing window" on the phone through an adb broadcast and then asks
/// the bridge's pre-auth `pair.token` command through an adb-forwarded socket.
/// The run-as path stays as fallback for debug builds.
/// </summary>
public static class TokenSync
{
    private static readonly Regex TokenRx =
        new("name=\"token\">([0-9a-fA-F]{16,})<", RegexOptions.Compiled);

    /// <summary>Reads the phone token: adb broadcast → forwarded pair.token.</summary>
    public static async Task<string?> PullFromPhoneAsync(string serial, CancellationToken ct)
    {
        var adb = new AdbServerClient();

        // 1) open the pairing window on the phone (proof of physical/adb access)
        try
        {
            await adb.ShellAsync(serial,
                $"am broadcast -a pro.netbridge.android.PAIR -n {Constants.AndroidPackageId}/.bridge.PairingReceiver",
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn("Token", "pair broadcast failed: " + ex.Message);
        }
        await Task.Delay(900, ct).ConfigureAwait(false); // let the receiver run

        // 2) forward a local port to the phone bridge and ask for the token
        try
        {
            await adb.ForwardAsync(serial, Constants.AdbForwardBridge, Constants.BridgePort, ct)
                .ConfigureAwait(false);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", Constants.AdbForwardBridge, ct).ConfigureAwait(false);
            tcp.NoDelay = true;
            var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            await writer.WriteLineAsync("{\"id\":1,\"cmd\":\"pair.token\"}").ConfigureAwait(false);

            // bounded read — the window reply is instant when it works
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(6));
            var line = await reader.ReadLineAsync(wait.Token).ConfigureAwait(false);
            if (line == null)
            {
                Logger.Warn("Token", "pair.token: no response — is 'sharing' running on the phone?");
                return null;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();
            if (root.TryGetProperty("result", out var res) && res.TryGetProperty("token", out var t))
                return t.GetString()?.ToLowerInvariant();

            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown response";
            Logger.Warn("Token", $"pair.token rejected: {err}");
            return null;
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("Token", "pair.token timed out");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn("Token", "pair.token fetch failed: " + ex.Message);
        }

        return null;
    }

    /// <summary>Fallback for debug builds: read shared_prefs via run-as.</summary>
    private static async Task<string?> PullViaRunAsAsync(string serial, CancellationToken ct)
    {
        try
        {
            var adb = new AdbServerClient();
            var outp = await adb.ShellAsync(serial,
                $"run-as {Constants.AndroidPackageId} cat shared_prefs/netbridge.xml", ct).ConfigureAwait(false);
            AdbServerClient.ExtractRc(outp, out var clean);
            var m = TokenRx.Match(clean);
            if (!m.Success)
            {
                Logger.Warn("Token",
                    "could not read phone token (run-as is unavailable for release builds) — " +
                    "copy it manually: phone app → Security tab → Copy → PC Settings → Pairing token");
                return null;
            }
            return m.Groups[1].Value.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Logger.Warn("Token", "run-as pull failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Overwrites the PC token with the phone's (the phone is source of truth).</summary>
    public static async Task<bool> SyncAsync(string serial, CancellationToken ct)
    {
        var tok = await PullFromPhoneAsync(serial, ct).ConfigureAwait(false)
                  ?? await PullViaRunAsAsync(serial, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(tok)) return false;
        if (SettingsStore.Current.Token != tok)
        {
            SettingsStore.Current.Token = tok;
            SettingsStore.Save();
            Logger.Info("Token", "token synced from phone");
        }
        return true;
    }
}
