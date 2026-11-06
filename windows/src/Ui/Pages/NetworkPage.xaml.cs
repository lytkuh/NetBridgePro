using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetBridge.Windows.Core;
using NetBridge.Windows.Net;

namespace NetBridge.Windows.Ui;

public partial class NetworkPage : Page, NetBridge.Windows.Core.ILocalizable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public NetworkPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            NetModeController.Instance.Timeline.CollectionChanged -= Timeline_Changed;
        };
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir;
        TitleText.Text = I18N.T("net_title");
        HintText.Text = I18N.T("net_hint");
        SimLabel.Text = I18N.T("net_sim");
        CurrentLabel.Text = I18N.T("net_current") + ":";
        RatLabel.Text = I18N.T("net_rat") + ":";
        LogTitle.Text = I18N.T("net_title") + " — log";
        SmartTitle.Text = I18N.T("net_smart");
        PolicyLabel.Text = I18N.T("net_policy") + ":";
        RsrpBadLabel.Text = I18N.T("net_rsrp_bad");
        RsrpGoodLabel.Text = I18N.T("net_rsrp_good");
        CooldownLabel.Text = I18N.T("net_cooldown");
        N5gAuto.Content = I18N.T("net_mode_auto5g");
        N5g4g.Content = I18N.T("net_mode_5g4g");
        N5gOnly.Content = I18N.T("net_mode_5g_only");
        N4gAuto.Content = I18N.T("net_mode_auto4g");
        N4gOnly.Content = I18N.T("net_mode_4g_only");
        N3gOnly.Content = I18N.T("net_mode_3g_only");
        N3g2g.Content = I18N.T("net_mode_3g2g");
        N2gOnly.Content = I18N.T("net_mode_2g_only");
        RawLabel.Text = I18N.T("set_raw_value");
        RawApplyBtn.Content = I18N.T("common_apply");
        SmartEnabled.Content = I18N.T("net_smart_on");
        PolStability.Content = I18N.T("net_pol_stability");
        PolSpeed.Content = I18N.T("net_pol_speed");
        PolBattery.Content = I18N.T("net_pol_battery");
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyTexts();

        SmartEnabled.IsChecked = SettingsStore.Current.SmartSwitcherEnabled;
        var cfg = SettingsStore.Current;
        RsrpBadBox.Text = cfg.RsrpBad.ToString();
        RsrpGoodBox.Text = cfg.RsrpGood.ToString();
        CooldownBox.Text = cfg.SwitchCooldownSec.ToString();
        (cfg.SwitcherPolicy switch
        {
            "speed" => PolSpeed,
            "battery" => PolBattery,
            _ => PolStability
        }).IsChecked = true;

        TimelineList.ItemsSource = NetModeController.Instance.Timeline;
        NetModeController.Instance.Timeline.CollectionChanged += Timeline_Changed;

        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    private void Timeline_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (TimelineList.Items.Count > 0)
                TimelineList.ScrollIntoView(TimelineList.Items[^1]);
        });
    }

    private void RefreshStatus()
    {
        var bridge = ConnectionManager.Instance.Bridge;
        var signal = SmartSwitcherHelperLastSignal();
        RatValue.Text = signal?.NetworkType ?? "—";
        SmartStatus.Text = SettingsStore.Current.SmartSwitcherEnabled
            ? SmartSwitcher.Instance.Status
            : "";

        _ = RefreshModeAsync(bridge);
    }

    private static Bridge.SignalData? SmartSwitcherHelperLastSignal()
    {
        // SmartSwitcher holds no public "last signal"; the bridge keeps it cached
        return ConnectionManager.Instance.Bridge.LastSignal;
    }

    private async Task RefreshModeAsync(Bridge.BridgeClient bridge)
    {
        if (!bridge.IsConnected) return;
        try
        {
            var subs = await bridge.GetSubscriptionsAsync(CancellationToken.None).ConfigureAwait(false);
            if (subs != null && subs.Count > 0)
            {
                var slot = Sim1.IsChecked == true ? 1 : 0;
                var sub = subs.FirstOrDefault(s => s.Slot == slot) ?? subs[0];
                _ = Dispatcher.BeginInvoke(() =>
                {
                    CurrentMode.Text = sub.PreferredMode?.ToString() ?? "—";
                });
            }
        }
        catch { }
    }

    private async void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string mode)
        {
            var slot = Sim1.IsChecked == true ? 1 : 0;
            Sim0.IsEnabled = Sim1.IsEnabled = false;
            await NetModeController.Instance.ApplyAsync(mode, slot).ConfigureAwait(false);
            _ = Dispatcher.BeginInvoke(() => Sim0.IsEnabled = Sim1.IsEnabled = true);
        }
    }

    private async void RawApply_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(RawValue.Text, out var v) && v is >= 0 and <= 33)
        {
            var slot = Sim1.IsChecked == true ? 1 : 0;
            await NetModeController.Instance.ApplyRawAsync(slot, v).ConfigureAwait(false);
        }
        else MessageBox.Show("0-33", "NetBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Smart_Changed(object sender, RoutedEventArgs e)
    {
        if (SmartEnabled.IsChecked == null) return;
        SettingsStore.Current.SmartSwitcherEnabled = SmartEnabled.IsChecked.Value;
        SettingsStore.Save();
    }

    private void Policy_Changed(object sender, RoutedEventArgs e)
    {
        if (PolStability.IsChecked == true) SettingsStore.Current.SwitcherPolicy = "stability";
        else if (PolSpeed.IsChecked == true) SettingsStore.Current.SwitcherPolicy = "speed";
        else if (PolBattery.IsChecked == true) SettingsStore.Current.SwitcherPolicy = "battery";
        SettingsStore.Save();
    }

    private void SaveSmart_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(RsrpBadBox.Text, out var bad)) SettingsStore.Current.RsrpBad = bad;
        if (int.TryParse(RsrpGoodBox.Text, out var good)) SettingsStore.Current.RsrpGood = good;
        if (int.TryParse(CooldownBox.Text, out var cd)) SettingsStore.Current.SwitchCooldownSec = Math.Max(30, cd);
        SettingsStore.Save();
        MessageBox.Show(I18N.T("net_applied"), "NetBridge", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
