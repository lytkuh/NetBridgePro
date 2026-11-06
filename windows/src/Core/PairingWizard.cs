using NetBridge.Windows.Adb;

namespace NetBridge.Windows.Core;

/// <summary>
/// One-time activation wizard: starts the private adb server, waits for the
/// phone, and grants the Android app the permissions it cannot ask for
/// (WRITE_SECURE_SETTINGS + runtime grants). Optionally switches the phone
/// into "lockdown" (adb off) afterwards.
/// </summary>
public static class PairingWizard
{
    public class StepEventArgs : EventArgs
    {
        public string Message = "";
        public bool Ok = true;
    }

    public static event EventHandler<StepEventArgs>? Step;

    private static void Report(string msg, bool ok = true)
    {
        Logger.Info("Pair", msg);
        Step?.Invoke(null, new StepEventArgs { Message = msg, Ok = ok });
    }

    public static async Task<bool> RunAsync(CancellationToken ct)
    {
        Report("starting dedicated adb server (port " + Constants.AdbServerPort + ")…");
        if (!await AdbServerManager.EnsureServerAsync(ct).ConfigureAwait(false))
        {
            Report("adb server failed — install platform-tools from Settings first", false);
            return false;
        }
        Report("key fingerprint: " + AdbKey.Fingerprint());

        var adb = new AdbServerClient();
        Report("waiting for a device (30 s) — plug the cable and accept the prompt…");
        AdbServerClient.AdbDevice? dev = null;
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var devices = await adb.DevicesAsync(ct).ConfigureAwait(false);
                dev = devices.FirstOrDefault(d => d.State == "device")
                   ?? devices.FirstOrDefault(d => d.State == "unauthorized");
                if (dev != null && dev.State == "device") break;
                if (dev != null && dev.State == "unauthorized")
                    Report("device unauthorized — tap 'Allow' on the phone…", false);
            }
            catch { }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        if (dev == null || dev.State != "device")
        {
            Report("no authorized device found", false);
            return false;
        }
        Report($"device found: {dev.Model} ({dev.Serial})");

        var grants = new[]
        {
            "android.permission.WRITE_SECURE_SETTINGS",
            "android.permission.READ_PHONE_STATE",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.POST_NOTIFICATIONS"
        };
        foreach (var perm in grants)
        {
            var outp = await adb.ShellAsync(dev.Serial, $"pm grant {Constants.AndroidPackageId} {perm}", ct).ConfigureAwait(false);
            var rc = AdbServerClient.ExtractRc(outp, out var err);
            Report(rc == 0 ? $"granted {perm}" : $"grant {perm} failed: {err}", rc == 0);
        }

        // pull the shared token straight out of the phone app (debug build → run-as)
        Report("syncing shared token from the phone app…");
        if (await TokenSync.SyncAsync(dev.Serial, ct).ConfigureAwait(false))
        {
            Report("token synced — phone and PC authenticate automatically");
        }
        else
        {
            Report("token sync failed — copy the token from the phone app (Security tab) into Settings", false);
        }

        // reduce exposure: wireless debugging off, notification of the key stays scoped to this PC
        await adb.ShellAsync(dev.Serial, "settings put global adb_wifi_enabled 0", ct).ConfigureAwait(false);
        Report("wireless debugging disabled on phone");

        if (SettingsStore.Current.SecurityMode == "lockdown")
        {
            await adb.ShellAsync(dev.Serial, "settings put global adb_enabled 0", ct).ConfigureAwait(false);
            Report("LOCKDOWN: global adb disabled — re-enable from Developer options when needed");
        }
        else
        {
            Report("companion mode: USB debugging stays ON but only THIS PC's key is authorized");
            Report("tip: in Developer options tap 'Revoke USB debugging authorizations' first, then re-pair, so older PCs lose access");
        }

        Report("activation finished");
        return true;
    }
}
