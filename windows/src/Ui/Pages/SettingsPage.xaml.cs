using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetBridge.Windows.Adb;
using NetBridge.Windows.Core;
using NetBridge.Windows.Net;

namespace NetBridge.Windows.Ui;

public partial class SettingsPage : Page, NetBridge.Windows.Core.ILocalizable
{
    public SettingsPage()
    {
        InitializeComponent();
        ApplyTexts();
        LoadValues();

        AdbServerManager.OnDownloadProgress += msg => Dispatcher.BeginInvoke(() => AdbProgress.Text = msg);
        PairingWizard.Step += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            PairLog.Items.Add((e.Ok ? "  " : "  ⚠ ") + e.Message);
            if (PairLog.Items.Count > 0) PairLog.ScrollIntoView(PairLog.Items[^1]);
        });

        Loaded += (_, _) => _ = RefreshAdbStatusAsync();
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir;
        GenTitle.Text = I18N.T("set_general");
        LangLabel.Text = I18N.T("set_language") + ":";
        AutoStartChk.Content = I18N.T("set_autostart");
        SysProxyChk.Content = I18N.T("set_sysproxy");
        NcmChk.Content = I18N.T("set_ncm");
        SecTitle.Text = I18N.T("set_security");
        ModeCompanion.Content = I18N.T("set_mode_companion");
        ModeLockdown.Content = I18N.T("set_mode_lockdown");
        SecHint.Text = I18N.T("set_security_hint");
        TokenLabel.Text = I18N.T("set_token") + ":";
        CopyBtn.Content = I18N.T("set_copy");
        RegenBtn.Content = I18N.T("set_regenerate");
        PairTitle.Text = I18N.T("set_pairing_title");
        PairHint.Text = I18N.T("set_pairing_hint");
        PairBtn.Content = I18N.T("set_pairing_btn");
        AdbTitle.Text = I18N.T("set_adb_title");
        AdbStatusLabel.Text = I18N.T("set_adb_status") + ":";
        AdbInstallBtn.Content = I18N.T("set_adb_install");
        KeyLabel.Text = I18N.T("set_adb_key") + ":";
        DnsTitle.Text = I18N.T("set_dns_title") + " / " + I18N.T("set_ports_title");
        DnsRemote.Content = I18N.T("set_dns_phone");
        DnsDoh.Content = I18N.T("set_dns_doh");
        DnsSystem.Content = I18N.T("set_dns_system");
        DohLabel.Text = I18N.T("set_doh_provider") + ":";
        LpPortLabel.Text = I18N.T("set_localproxy_port");
        LimitLabel.Text = I18N.T("set_limit");
        DataDirLabel.Text = I18N.T("set_data_dir");
        BypassLabel.Text = I18N.T("set_bypass") + ":";
        SaveBtn.Content = I18N.T("set_apply");
    }

    private void LoadValues()
    {
        var cfg = SettingsStore.Current;
        (cfg.Language == "en" ? LangEn : LangFa).IsChecked = true;
        (cfg.SecurityMode == "lockdown" ? ModeLockdown : ModeCompanion).IsChecked = true;
        TokenBox.Text = cfg.Token;
        AutoStartChk.IsChecked = cfg.AutoStartWithWindows;
        SysProxyChk.IsChecked = cfg.SetSystemProxy;
        NcmChk.IsChecked = cfg.PreferNcmTethering;
        (cfg.DnsMode switch
        {
            "local-doh" => DnsDoh,
            "system" => DnsSystem,
            _ => DnsRemote
        }).IsChecked = true;
        foreach (var item in DohBox.Items.Cast<ComboBoxItem>())
            if ((string)item.Tag == cfg.DohEndpoint) { DohBox.SelectedItem = item; break; }
        LpPortBox.Text = cfg.LocalProxyPort.ToString();
        LimitBox.Text = (cfg.SpeedLimitBytesPerSec / 1024).ToString();
        BypassBox.Text = string.Join(Environment.NewLine, cfg.BypassDomains);
        DataDirVal.Text = Constants.ProgramDataDir;
    }

    private async Task RefreshAdbStatusAsync()
    {
        var up = await new Adb.AdbServerClient().PingAsync().ConfigureAwait(false);
        _ = Dispatcher.BeginInvoke(() =>
        {
            AdbStatus.Text = up
                ? $"running on :{Constants.AdbServerPort}  ({(AdbServerManager.ResolvedAdbPath ?? AdbServerManager.FindAdb() ?? "?")})"
                : "stopped";
            KeyFp.Text = AdbKey.Fingerprint();
        });
    }

    // ---------------- handlers ---------------- //

    private void Lang_Changed(object sender, RoutedEventArgs e)
    {
        if (LangFa.IsChecked == null) return;
        var lang = LangFa.IsChecked == true ? "fa" : "en";
        if (SettingsStore.Current.Language == lang) return;
        SettingsStore.Current.Language = lang;
        SettingsStore.Save();
        // apply immediately across the whole window — no restart needed
        (Window.GetWindow(this) as MainWindow)?.SwitchLanguage(lang);
    }

    private void SecMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ModeLockdown.IsChecked == null) return;
        SettingsStore.Current.SecurityMode = ModeLockdown.IsChecked == true ? "lockdown" : "companion";
        SettingsStore.Save();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoStartChk.IsChecked == null) return;
        var enabled = AutoStartChk.IsChecked.Value;
        SettingsStore.Current.AutoStartWithWindows = enabled;
        SettingsStore.Save();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enabled)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) key.SetValue("NetBridgePro", $"\"{exe}\"");
            }
            else key.DeleteValue("NetBridgePro", false);
        }
        catch (Exception ex) { Logger.Warn("Settings", "autostart: " + ex.Message); }
    }

    private void SysProxy_Changed(object sender, RoutedEventArgs e)
    {
        if (SysProxyChk.IsChecked == null) return;
        SettingsStore.Current.SetSystemProxy = SysProxyChk.IsChecked.Value;
        SettingsStore.Save();
    }

    private void Ncm_Changed(object sender, RoutedEventArgs e)
    {
        if (NcmChk.IsChecked == null) return;
        SettingsStore.Current.PreferNcmTethering = NcmChk.IsChecked.Value;
        SettingsStore.Save();
    }

    private void Dns_Changed(object sender, RoutedEventArgs e)
    {
        if (DnsRemote.IsChecked == null) return;
        SettingsStore.Current.DnsMode = DnsDoh.IsChecked == true ? "local-doh"
            : DnsSystem.IsChecked == true ? "system" : "remote";
        SettingsStore.Save();
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(SettingsStore.Current.Token); } catch { }
    }

    private void TokenBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tok = TokenBox.Text.Trim().ToLowerInvariant();
        if (tok == SettingsStore.Current.Token) return;
        SettingsStore.Current.Token = tok;
        SettingsStore.Save();
        Logger.Info("Settings", "token updated manually");
    }

    /// <summary>The phone owns the shared secret — pull it over adb (debug builds allow run-as).</summary>
    private async void RegenToken_Click(object sender, RoutedEventArgs e)
    {
        RegenBtn.IsEnabled = false;
        var ok = false;
        var msg = "";
        try
        {
            if (!await AdbServerManager.EnsureServerAsync().ConfigureAwait(false))
            {
                msg = I18N.T("set_sync_no_adb");
            }
            else
            {
                var adb = new AdbServerClient();
                var devices = await adb.DevicesAsync().ConfigureAwait(false);
                var dev = devices.FirstOrDefault(d => d.State == "device");
                if (dev == null)
                {
                    msg = I18N.T("set_sync_no_device");
                }
                else
                {
                    ok = await TokenSync.SyncAsync(dev.Serial, CancellationToken.None).ConfigureAwait(false);
                    msg = ok ? I18N.T("set_sync_ok") : I18N.T("set_sync_fail");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Settings", "token sync: " + ex.Message);
            msg = I18N.T("set_sync_fail");
        }
        finally
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                TokenBox.Text = SettingsStore.Current.Token;
                RegenBtn.IsEnabled = true;
                MessageBox.Show(msg, "NetBridge", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            });
        }
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        PairBtn.IsEnabled = false;
        PairBar.Visibility = Visibility.Visible;
        PairLog.Items.Clear();
        try
        {
            await PairingWizard.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Pair", ex.Message);
        }
        finally
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                PairBtn.IsEnabled = true;
                PairBar.Visibility = Visibility.Collapsed;
            });
        }
    }

    private async void AdbInstall_Click(object sender, RoutedEventArgs e)
    {
        AdbInstallBtn.IsEnabled = false;
        var ok = await AdbServerManager.DownloadPlatformToolsAsync().ConfigureAwait(false);
        _ = Dispatcher.BeginInvoke(() =>
        {
            AdbInstallBtn.IsEnabled = true;
            AdbProgress.Text = ok ? "✓" : "failed";
        });
        if (ok) await RefreshAdbStatusAsync().ConfigureAwait(false);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var cfg = SettingsStore.Current;
        if (int.TryParse(LpPortBox.Text, out var lp) && lp is >= 1024 and <= 65535) cfg.LocalProxyPort = lp;
        if (long.TryParse(LimitBox.Text, out var lim) && lim >= 0) cfg.SpeedLimitBytesPerSec = lim * 1024;
        cfg.BypassDomains = BypassBox.Text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (DohBox.SelectedItem is ComboBoxItem it) cfg.DohEndpoint = (string)it.Tag;
        SettingsStore.Save();

        // apply live
        var restarted = false;
        if (LocalProxy.Instance.Running)
        {
            LocalProxy.Instance.Stop();
            restarted = LocalProxy.Instance.Start(cfg.LocalProxyPort);
        }
        if (restarted)
            MessageBox.Show(I18N.T("net_applied"), "NetBridge", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(
                $"Port {cfg.LocalProxyPort} is busy - close the other NetBridge instance or choose another port, then Apply again.",
                "NetBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
