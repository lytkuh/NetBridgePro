using System.IO;

namespace NetBridge.Windows.Core;

/// <summary>Shared constants — keep in sync with the Android app (Prefs.kt) and docs/PROTOCOL.md.</summary>
public static class Constants
{
    public const string AndroidPackageId = "pro.netbridge.android";

    public const int ProxyPort = 59110;     // phone-side multiprotocol proxy
    public const int BridgePort = 59123;    // phone-side JSON bridge
    public const int LocalProxyPort = 59120;// PC-side loopback proxy for system apps
    public const int AdbServerPort = 5038;  // private adb server instance
    public const int AdbForwardData = 59121;// local endpoint -> phone 59110
    public const int AdbForwardBridge = 59122; // local endpoint -> phone 59123

    public const string ProxyUser = "netbridge";

    public const string DoHCloudflare = "https://cloudflare-dns.com/dns-query";
    public const string DoHGoogle = "https://dns.google/dns-query";
    public const string DoHAdguard = "https://dns.adguard-dns.com/dns-query";

    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetBridge");

    public static string LogDir => Path.Combine(ProgramDataDir, "logs");
    public static string KeyDir => Path.Combine(ProgramDataDir, "keys");
    public static string ToolsDir => Path.Combine(ProgramDataDir, "platform-tools");
    public static string SettingsFile => Path.Combine(ProgramDataDir, "settings.json");
    public static string FirewallRulesFile => Path.Combine(ProgramDataDir, "firewall-rules.json");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(ProgramDataDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(KeyDir);
    }
}
