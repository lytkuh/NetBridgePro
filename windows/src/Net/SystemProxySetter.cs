using Microsoft.Win32;
using System.Runtime.InteropServices;

using NetBridge.Windows.Core;

namespace NetBridge.Windows.Net;

/// <summary>
/// Sets/restores the per-user WinINET system proxy (same mechanism PdaNet
/// uses). Original values are backed up so Disconnect always restores them.
/// </summary>
public static class SystemProxySetter
{
    private const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static bool _applied;
    private static int _oldEnable;
    private static string _oldServer = "";
    private static string _oldOverride = "";

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    public static bool IsApplied => _applied;

    public static bool Apply(int localProxyPort)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettings, writable: true);
            if (key == null) return false;

            // backup current
            _oldEnable = key.GetValue("ProxyEnable") is int v ? v : 0;
            _oldServer = key.GetValue("ProxyServer") as string ?? "";
            _oldOverride = key.GetValue("ProxyOverride") as string ?? "";

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", $"127.0.0.1:{localProxyPort}", RegistryValueKind.String);
            key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.2?.*;172.30.*;172.31.*;192.168.*;<local>", RegistryValueKind.String);

            RefreshWinINET();
            _applied = true;
            Logger.Info("SysProxy", $"system proxy -> 127.0.0.1:{localProxyPort} (backup saved)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SysProxy", "apply failed: " + ex.Message);
            return false;
        }
    }

    public static void RestoreIfNeeded()
    {
        if (!_applied) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettings, writable: true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", _oldEnable, RegistryValueKind.DWord);
                if (string.IsNullOrEmpty(_oldServer)) key.DeleteValue("ProxyServer", false);
                else key.SetValue("ProxyServer", _oldServer, RegistryValueKind.String);
                if (string.IsNullOrEmpty(_oldOverride)) key.DeleteValue("ProxyOverride", false);
                else key.SetValue("ProxyOverride", _oldOverride, RegistryValueKind.String);
            }
            RefreshWinINET();
            _applied = false;
            Logger.Info("SysProxy", "system proxy restored");
        }
        catch (Exception ex)
        {
            Logger.Warn("SysProxy", "restore failed: " + ex.Message);
        }
    }

    private static void RefreshWinINET()
    {
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }
}
