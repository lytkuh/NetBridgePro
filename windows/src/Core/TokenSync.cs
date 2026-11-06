using System.Text.RegularExpressions;

using NetBridge.Windows.Adb;

namespace NetBridge.Windows.Core;

/// <summary>
/// The Android app owns the shared secret (Security tab on the phone).
/// This helper pulls it over adb with `run-as` (works for debug builds),
/// so the PC and phone always agree — no manual typing needed.
/// When the pull is impossible (non-debuggable phone / no device), the
/// manually-typed token in Settings is used as fallback.
/// </summary>
public static class TokenSync
{
    private static readonly Regex TokenRx =
        new("name=\"token\">([0-9a-fA-F]{16,})<", RegexOptions.Compiled);

    /// <summary>Reads the phone token from shared_prefs/netbridge.xml via run-as.</summary>
    public static async Task<string?> PullFromPhoneAsync(string serial, CancellationToken ct)
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
                // run-as works only on debuggable builds — the public APK is a
                // release build, so Android refuses it. Manual copy is the way.
                Logger.Warn("Token",
                    "could not read phone token (run-as is unavailable for release builds) — " +
                    "copy it manually: phone app → Security tab → Copy → PC Settings → Pairing token");
                return null;
            }
            return m.Groups[1].Value.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Logger.Warn("Token", "pull failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Overwrites the PC token with the phone's (the phone is source of truth).</summary>
    public static async Task<bool> SyncAsync(string serial, CancellationToken ct)
    {
        var tok = await PullFromPhoneAsync(serial, ct).ConfigureAwait(false);
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
