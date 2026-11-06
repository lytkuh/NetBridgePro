using System.Text.Json;
using System.Text.Json.Serialization;

using System.IO;

namespace NetBridge.Windows.Core;

/// <summary>Persistent app settings (JSON in ProgramData).</summary>
public class SettingsModel
{
    // Pairing
    public string Token { get; set; } = "";
    public string Language { get; set; } = "fa";

    // Connection
    public bool AutoConnect { get; set; } = true;
    public bool AutoStartWithWindows { get; set; } = false;
    public bool PreferNcmTethering { get; set; } = true;
    public bool SetSystemProxy { get; set; } = true;

    // Security mode: "companion" keeps adb on (full features), "lockdown" turns it off
    public string SecurityMode { get; set; } = "companion";

    // DNS
    public string DnsMode { get; set; } = "remote";   // remote | local-doh | system
    public string DohEndpoint { get; set; } = Constants.DoHCloudflare;

    // Local proxy
    public int LocalProxyPort { get; set; } = Constants.LocalProxyPort;
    public long SpeedLimitBytesPerSec { get; set; } = 0; // 0 = unlimited
    public List<string> BypassDomains { get; set; } = new()
    {
        "*.lan", "*.local", "localhost", "127.0.0.1", "*.microsoft.com", "*.windowsupdate.com"
    };

    // Smart switcher
    public bool SmartSwitcherEnabled { get; set; } = false;
    public string SwitcherPolicy { get; set; } = "stability"; // stability | speed | battery
    public int RsrpBad { get; set; } = -118;
    public int RsrpGood { get; set; } = -105;
    public int SinrGood { get; set; } = 13;
    public int SwitchCooldownSec { get; set; } = 180;
    public int MinDwellSec { get; set; } = 120;
    public int NrDropGraceSec { get; set; } = 45;

    // ADB
    public string AdbPath { get; set; } = ""; // empty = auto (bundled platform-tools or PATH)

    // Speed test
    public int SpeedTestSeconds { get; set; } = 10;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static SettingsModel Current { get; private set; } = new();

    public static void Load()
    {
        Constants.EnsureDirs();
        try
        {
            if (File.Exists(Constants.SettingsFile))
            {
                Current = JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(Constants.SettingsFile), JsonOpts) ?? new SettingsModel();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Settings", "failed to load settings: " + ex.Message);
            Current = new SettingsModel();
        }

        // First-run token
        if (string.IsNullOrWhiteSpace(Current.Token) || Current.Token.Length < 16)
        {
            Current.Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 16);
            Save();
        }
    }

    public static void Save()
    {
        try
        {
            Constants.EnsureDirs();
            File.WriteAllText(Constants.SettingsFile, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Warn("Settings", "failed to save settings: " + ex.Message);
        }
    }
}
