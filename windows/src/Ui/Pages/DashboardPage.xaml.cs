using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetBridge.Windows.Bridge;
using NetBridge.Windows.Core;
using NetBridge.Windows.Net;

namespace NetBridge.Windows.Ui;

public partial class DashboardPage : Page, NetBridge.Windows.Core.ILocalizable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static SignalData? _lastSignal;

    static DashboardPage()
    {
        ConnectionManager.Instance.Signal += s => _lastSignal = s;
    }

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { ApplyTexts(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => Refresh();

        ChkAuto.IsChecked = SettingsStore.Current.AutoConnect;
        ApplyTexts();
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir;
        ConnTitle.Text = I18N.T("conn_status");
        EndpointLabel.Text = I18N.T("conn_endpoint") + ": ";
        PhoneLabel.Text = I18N.T("dash_phone");
        BatteryLabel.Text = I18N.T("dash_battery");
        TetherLabel.Text = I18N.T("dash_tether");
        BtnConnect.Content = I18N.T("conn_connect");
        ChkAuto.Content = I18N.T("conn_autoconnect");
        DownTitle.Text = I18N.T("speed_down");
        UpTitle.Text = I18N.T("speed_up");
        TotDownLabel.Text = I18N.T("speed_total_down");
        TotUpLabel.Text = I18N.T("speed_total_up");
        ConnsLabel.Text = I18N.T("conn_active_conns");
        ProxyLabel.Text = I18N.T("dash_proxy");
        SignalTitle.Text = I18N.T("signal_type");
        QuickTitle.Text = I18N.T("nav_network");
        Q5gAuto.Content = I18N.T("net_mode_auto5g");
        Q5gOnly.Content = I18N.T("net_mode_5g_only");
        Q4gOnly.Content = I18N.T("net_mode_4g_only");
        Q4gAuto.Content = I18N.T("net_mode_auto4g");
        Q3gOnly.Content = I18N.T("net_mode_3g_only");
        Q2gOnly.Content = I18N.T("net_mode_2g_only");
        QuickHint.Text = I18N.T("dash_quick_hint");
    }

    private void Refresh()
    {
        var st = ConnectionManager.Instance.State;
        var stats = TrafficStats.Instance;

        ConnState.Text = st.Connected ? I18N.T("conn_connected") : I18N.T("conn_disconnected");
        Dot.Fill = st.Connected
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        TransportText.Text = st.Connected ? I18N.T(st.Transport) : I18N.T("tr_none");
        EndpointText.Text = st.Connected ? st.Endpoint : "—";
        PhoneText.Text = string.IsNullOrEmpty(st.PhoneModel) ? "—" : st.PhoneModel;
        BatteryText.Text = st.Battery is >= 0 ? $"{st.Battery}%  ({st.Temp:F0}°C)" : "—";
        TetherText.Text = st.TetherUp ? I18N.T("common_on") : I18N.T("common_off");

        BtnConnect.Content = st.Connected ? I18N.T("conn_disconnect") : I18N.T("conn_connect");

        DownValue.Text = FmtRate(stats.RateRx);
        UpValue.Text = FmtRate(stats.RateTx);
        ChartDown.Push(stats.RateRx * 8.0 / 1_000_000.0);
        ChartUp.Push(stats.RateTx * 8.0 / 1_000_000.0);

        TotDown.Text = FmtBytes(stats.TotalRx);
        TotUp.Text = FmtBytes(stats.TotalTx);
        ConnsCount.Text = stats.ActiveConnections.ToString();
        ProxyAddr.Text = $"127.0.0.1:{SettingsStore.Current.LocalProxyPort}";

        var s = _lastSignal;
        if (s != null)
        {
            RatBadge.Text = s.NetworkType + (s.NrState is "CONNECTED" or "NOT_RESTRICTED" ? " +NR" : "");
            var parts = new List<string>();
            if (s.Rsrp.HasValue) parts.Add($"RSRP {s.Rsrp} dBm");
            if (s.Rsrq.HasValue) parts.Add($"RSRQ {s.Rsrq} dB");
            if (s.Sinr.HasValue) parts.Add($"SINR {s.Sinr} dB");
            if (!string.IsNullOrEmpty(s.Band)) parts.Add(I18N.T("signal_band") + " " + s.Band);
            SignalDetail.Text = string.Join("   ", parts);
        }
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        var mgr = ConnectionManager.Instance;
        if (mgr.State.Connected) mgr.Disconnect();
        else _ = mgr.ConnectAsync();
    }

    private void ChkAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkAuto.IsChecked == null) return;
        ConnectionManager.Instance.SetAutoConnect(ChkAuto.IsChecked.Value);
    }

    private void QuickLock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string mode)
            _ = NetModeController.Instance.ApplyAsync(mode, 0);
    }

    private static string FmtBytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / 1073741824.0:F2} GB",
        >= 1L << 20 => $"{b / 1048576.0:F1} MB",
        >= 1L << 10 => $"{b / 1024.0:F1} KB",
        _ => $"{b} B"
    };

    private static string FmtRate(long bps) => bps switch
    {
        >= 1_000_000 => $"{bps * 8.0 / 1_000_000.0:F1} Mbps",
        >= 1000 => $"{bps * 8.0 / 1000.0:F0} Kbps",
        _ => $"{bps * 8} bps"
    };
}
