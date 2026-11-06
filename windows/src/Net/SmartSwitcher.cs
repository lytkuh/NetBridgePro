using NetBridge.Windows.Bridge;
using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

/// <summary>
/// Hysteresis-based automatic RAN mode switcher driven by the live signal feed.
///
///   stability : drops from 5G to 4G-only after a sustained weak-NR period,
///               returns to 5G auto once the signal is healthy again
///   speed     : keeps 5G auto; returns to 5G from a manual 4G-only lock when
///               conditions are good
///   battery   : switches to 4G auto when the radio is struggling (less
///               scanning / re-registering)
///
/// All decisions respect cooldown + dwell timers and pause after manual
/// changes (10 min).
/// </summary>
public class SmartSwitcher
{
    public static SmartSwitcher Instance { get; } = new();

    private DateTime? _nrWeakSince;
    private DateTime? _nrGoodSince;
    private DateTime _lastSwitch = DateTime.MinValue;
    private DateTime _dwellSince = DateTime.MinValue;
    private DateTime _manualPauseUntil = DateTime.MinValue;
    private string _currentMode = "5G_AUTO";
    private string _status = "idle";

    public string Status
    {
        get => _status;
        private set { _status = value; StatusChanged?.Invoke(value); }
    }

    public event Action<string>? StatusChanged;

    public string CurrentMode => _currentMode;

    public void PauseManual()
    {
        _manualPauseUntil = DateTime.Now.AddMinutes(10);
        _dwellSince = DateTime.Now;
        Status = "paused (manual)";
    }

    public void Feed(SignalData s)
    {
        if (!SettingsStore.Current.SmartSwitcherEnabled) return;
        if (NetModeController.Instance.Busy) return;
        if (DateTime.Now < _manualPauseUntil) return;
        if ((DateTime.Now - _lastSwitch).TotalSeconds < SettingsStore.Current.SwitchCooldownSec) return;
        if ((DateTime.Now - _dwellSince).TotalSeconds < SettingsStore.Current.MinDwellSec) return;

        var cfg = SettingsStore.Current;
        var nrActive = s.NrState is "CONNECTED" or "NOT_RESTRICTED";
        var rsrp = s.Rsrp;
        var sinr = s.Sinr;

        try { Task.Run(() => DecideAsync(s, cfg, nrActive, rsrp, sinr)).ConfigureAwait(false); }
        catch { }
    }

    private async Task DecideAsync(SignalData s, SettingsModel cfg, bool nrActive, int? rsrp, int? sinr)
    {
        var now = DateTime.Now;

        switch (cfg.SwitcherPolicy)
        {
            case "stability":
                if (nrActive && rsrp.HasValue && rsrp.Value < cfg.RsrpBad)
                {
                    _nrWeakSince ??= now;
                    _nrGoodSince = null;
                    if ((now - _nrWeakSince.Value).TotalSeconds >= cfg.NrDropGraceSec)
                    {
                        Status = $"weak NR (RSRP {rsrp}) for {cfg.NrDropGraceSec}s → 4G only";
                        await SwitchToAsync("4G_ONLY").ConfigureAwait(false);
                        _nrWeakSince = null;
                    }
                    else Status = $"watching weak NR ({(int)(cfg.NrDropGraceSec - (now - _nrWeakSince.Value).TotalSeconds)}s)";
                }
                else if (_currentMode == "4G_ONLY" && rsrp.HasValue && rsrp.Value > cfg.RsrpGood)
                {
                    _nrGoodSince ??= now;
                    if ((now - _nrGoodSince.Value).TotalSeconds >= 60)
                    {
                        Status = "signal healthy again → 5G auto";
                        await SwitchToAsync("5G_AUTO").ConfigureAwait(false);
                        _nrGoodSince = null;
                    }
                    else Status = "waiting to restore 5G…";
                }
                else
                {
                    _nrWeakSince = null;
                    Status = nrActive ? "stable on NR" : "stable on LTE";
                }
                break;

            case "speed":
                if (_currentMode == "4G_ONLY" && rsrp.HasValue && rsrp.Value > cfg.RsrpGood &&
                    (!sinr.HasValue || sinr.Value > cfg.SinrGood))
                {
                    Status = "good conditions → back to 5G auto";
                    await SwitchToAsync("5G_AUTO").ConfigureAwait(false);
                }
                else Status = nrActive ? "5G active" : "on LTE (5G unavailable)";
                break;

            case "battery":
                if (rsrp.HasValue && rsrp.Value < cfg.RsrpBad && sinr.HasValue && sinr.Value < 0 && _currentMode != "4G_AUTO")
                {
                    Status = "weak radio → 4G auto (less scanning)";
                    await SwitchToAsync("4G_AUTO").ConfigureAwait(false);
                }
                else if (_currentMode == "4G_AUTO" && rsrp.HasValue && rsrp.Value > cfg.RsrpGood)
                {
                    Status = "radio healthy → restore 5G auto";
                    await SwitchToAsync("5G_AUTO").ConfigureAwait(false);
                }
                else Status = "battery policy watching";
                break;
        }
    }

    private async Task SwitchToAsync(string mode)
    {
        _lastSwitch = DateTime.Now;
        _dwellSince = DateTime.Now;
        Logger.Info("Switcher", $"policy switch → {mode}");
        var ok = await NetModeController.Instance.ApplyAsync(mode, 0).ConfigureAwait(false);
        if (ok)
        {
            _currentMode = mode;
            Status += " [applied]";
        }
        else
        {
            Status += " [failed]";
        }
    }
}
