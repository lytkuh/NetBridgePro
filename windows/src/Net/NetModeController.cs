using System.Collections.ObjectModel;
using System.Windows;
using NetBridge.Windows.Adb;
using NetBridge.Windows.Bridge;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

/// <summary>
/// RAN mode controller. Writes preferred_network_mode through the bridge
/// (WRITE_SECURE_SETTINGS), verifies the resulting radio state, and when the
/// radio does not switch by itself, cycles airplane mode over the private adb
/// channel (companion mode). Values are OEM-dependent, so candidates are
/// tried in order until verification succeeds (see docs/NETWORK-CONTROL.md).
/// </summary>
public class NetModeController
{
    public static NetModeController Instance { get; } = new();

    public class ModeSpec
    {
        public string Key = "";
        public string[] Candidates = Array.Empty<string>();
        public string? ExpectedFamily;      // null = accept anything
        public bool ExpectNrActive;
        public bool ExpectNrInactive;
    }

    public static readonly Dictionary<string, ModeSpec> Modes = new()
    {
        ["5G_AUTO"]  = new ModeSpec { Key = "5G_AUTO",  Candidates = new[] { "26", "22", "33", "20", "9" }, ExpectedFamily = null,  ExpectNrInactive = false },
        ["5G_4G"]    = new ModeSpec { Key = "5G_4G",    Candidates = new[] { "18", "22", "26" },        ExpectedFamily = null },
        ["5G_ONLY"]  = new ModeSpec { Key = "5G_ONLY",  Candidates = new[] { "17" },                    ExpectedFamily = "5G", ExpectNrActive = true },
        ["4G_AUTO"]  = new ModeSpec { Key = "4G_AUTO",  Candidates = new[] { "9", "12" },               ExpectedFamily = null },
        ["4G_ONLY"]  = new ModeSpec { Key = "4G_ONLY",  Candidates = new[] { "11" },                    ExpectedFamily = "4G", ExpectNrInactive = true },
        ["3G_ONLY"]  = new ModeSpec { Key = "3G_ONLY",  Candidates = new[] { "2" },                     ExpectedFamily = "3G" },
        ["3G_2G"]    = new ModeSpec { Key = "3G_2G",    Candidates = new[] { "3", "0" },                ExpectedFamily = "3G" },
        ["2G_ONLY"]  = new ModeSpec { Key = "2G_ONLY",  Candidates = new[] { "1" },                     ExpectedFamily = "2G" },
        ["DEFAULT"]  = new ModeSpec { Key = "DEFAULT",  Candidates = new[] { "0" } },
    };

    public ObservableCollection<string> Timeline { get; } = new();

    public bool Busy { get; private set; }
    public string LastApplied { get; private set; } = "";

    private void Log(string line)
    {
        // Log() is called from background tasks (ApplyAsync, SmartSwitcher).
        // The Timeline is bound to the NetworkPage ListBox — WPF requires all
        // collection changes on the Dispatcher thread or the page crashes with
        // "CollectionView does not support changes from a different thread".
        var line2 = $"[{DateTime.Now:HH:mm:ss}] {line}";
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess())
        {
            Timeline.Insert(0, line2);
            while (Timeline.Count > 200) Timeline.RemoveAt(Timeline.Count - 1);
        }
        else
        {
            d.BeginInvoke(() =>
            {
                Timeline.Insert(0, line2);
                while (Timeline.Count > 200) Timeline.RemoveAt(Timeline.Count - 1);
            });
        }
        Logger.Info("NetMode", line);
    }

    /// <summary>Manual switch (also pauses the smart switcher for 10 minutes).</summary>
    public async Task<bool> ApplyAsync(string modeKey, int slot)
    {
        if (!Modes.TryGetValue(modeKey, out var spec))
        {
            Log($"unknown mode {modeKey}");
            return false;
        }
        Busy = true;
        SmartSwitcher.Instance.PauseManual();
        try
        {
            foreach (var candidate in spec.Candidates)
            {
                Log($"writing preferred_network_mode{slot + 1} = {candidate} ({modeKey})");
                var written = await BridgeWriteAsync(slot, candidate).ConfigureAwait(false);
                if (!written)
                {
                    Log("bridge write failed (auth/WRITE_SECURE_SETTINGS?)");
                    // last resort: adb settings put (companion mode)
                    written = await AdbWriteAsync(slot, candidate).ConfigureAwait(false);
                    if (!written) continue;
                }

                // give the modem a chance to switch by itself
                var applied = await VerifyAsync(spec, TimeSpan.FromSeconds(6)).ConfigureAwait(false);
                if (applied)
                {
                    Log($"OK — radio now matches {modeKey} (no airplane cycle needed)");
                    LastApplied = modeKey;
                    return true;
                }

                // airplane cycle over adb (companion mode only)
                if (await AirplaneCycleAsync().ConfigureAwait(false))
                {
                    applied = await VerifyAsync(spec, TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                    if (applied)
                    {
                        Log($"OK — {modeKey} applied after airplane cycle");
                        LastApplied = modeKey;
                        return true;
                    }
                }
                else
                {
                    Log("airplane cycle unavailable (lockdown mode?) — toggle airplane manually to apply");
                }
                Log($"candidate {candidate} did not take effect, trying next…");
            }
            Log($"FAILED to verify {modeKey} on this firmware (see docs/NETWORK-CONTROL.md)");
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task<bool> ApplyRawAsync(int slot, int value)
    {
        Busy = true;
        SmartSwitcher.Instance.PauseManual();
        try
        {
            var ok = await BridgeWriteAsync(slot, value.ToString()).ConfigureAwait(false)
                     || await AdbWriteAsync(slot, value.ToString()).ConfigureAwait(false);
            Log(ok ? $"raw value {value} written to slot {slot}" : "raw write failed");
            if (ok) LastApplied = $"raw:{value}";
            return ok;
        }
        finally { Busy = false; }
    }

    private static async Task<bool> BridgeWriteAsync(int slot, string value)
    {
        var bridge = ConnectionManager.Instance.Bridge;
        if (!bridge.IsConnected) return false;
        if (!int.TryParse(value, out var v)) return false;
        try { return await bridge.WritePreferredModeAsync(slot, v, CancellationToken.None).ConfigureAwait(false); }
        catch { return false; }
    }

    private static async Task<bool> AdbWriteAsync(int slot, string value)
    {
        var conn = ConnectionManager.Instance;
        if (conn.Serial == null) return false;
        var adb = new AdbServerClient();
        try
        {
            var outp = await adb.ShellAsync(conn.Serial,
                $"settings put global preferred_network_mode{slot + 1} {value}", CancellationToken.None).ConfigureAwait(false);
            AdbServerClient.ExtractRc(outp, out _);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("NetMode", "adb write failed: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> AirplaneCycleAsync()
    {
        var conn = ConnectionManager.Instance;
        if (conn.Serial == null) return false;
        var adb = new AdbServerClient();
        try
        {
            Log("airplane mode: ON");
            var o1 = await adb.ShellAsync(conn.Serial, "cmd connectivity airplane-mode enable", CancellationToken.None).ConfigureAwait(false);
            AdbServerClient.ExtractRc(o1, out _);
            await Task.Delay(3500).ConfigureAwait(false);
            Log("airplane mode: OFF");
            var o2 = await adb.ShellAsync(conn.Serial, "cmd connectivity airplane-mode disable", CancellationToken.None).ConfigureAwait(false);
            AdbServerClient.ExtractRc(o2, out _);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("NetMode", "airplane cycle failed: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> VerifyAsync(ModeSpec spec, TimeSpan window)
    {
        var bridge = ConnectionManager.Instance.Bridge;
        var deadline = DateTime.Now + window;
        while (DateTime.Now < deadline)
        {
            await Task.Delay(1200).ConfigureAwait(false);
            try
            {
                if (bridge.IsConnected)
                {
                    var s = await bridge.PollSignalAsync(CancellationToken.None).ConfigureAwait(false);
                    if (s == null) continue;

                    bool familyOk = spec.ExpectedFamily == null || s.Family == spec.ExpectedFamily;
                    bool nrOk = true;
                    if (spec.ExpectNrActive) nrOk = s.NrState is "CONNECTED" or "NOT_RESTRICTED";
                    if (spec.ExpectNrInactive) nrOk = s.NrState == "NONE";

                    if (familyOk && nrOk) return true;
                }
                else if (spec.ExpectedFamily == null)
                {
                    // no bridge: accept after write (verification impossible)
                    return true;
                }
            }
            catch { }
        }
        return false;
    }
}
