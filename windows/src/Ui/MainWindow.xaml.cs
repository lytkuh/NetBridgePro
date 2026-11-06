using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetBridge.Windows.Core;
using NetBridge.Windows.Net;

namespace NetBridge.Windows.Ui;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Page> _pages = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>When set before Show(), the window opens on this tab instead of Dashboard.</summary>
    public string? StartTab;

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage();

        _pages["dashboard"] = new DashboardPage();
        _pages["signal"] = new SignalPage();
        _pages["network"] = new NetworkPage();
        _pages["apps"] = new AppsPage();
        _pages["speed"] = new SpeedTestPage();
        _pages["settings"] = new SettingsPage();
        _pages["logs"] = new LogsPage();
        _pages["about"] = new AboutPage();
        foreach (var p in _pages.Values) p.FlowDirection = I18N.Dir;

        PageHost.Content = _pages["dashboard"];

        if (StartTab != null)
        {
            var rb = StartTab switch
            {
                "signal" => NavSignal,
                "network" => NavNetwork,
                "apps" => NavApps,
                "speed" => NavSpeed,
                "settings" => NavSettings,
                "logs" => NavLogs,
                "about" => NavAbout,
                _ => NavDashboard
            };
            rb.IsChecked = true; // fires Nav_Checked → hosts the page
        }

        _statusTimer.Tick += (_, _) => RefreshStatusPills();
        _statusTimer.Start();

        // Start local proxy + auto-connect in background
        Loaded += (_, _) =>
        {
            if (!LocalProxy.Instance.Running)
                LocalProxy.Instance.Start(SettingsStore.Current.LocalProxyPort);

            if (SettingsStore.Current.AutoConnect)
                ConnectionManager.Instance.SetAutoConnect(true);
        };
    }

    private void ApplyLanguage()
    {
        FlowDirection = I18N.Dir;
        // WPF does NOT inherit FlowDirection from the Window into Pages hosted
        // inside a Frame — set it explicitly on the host and every page.
        PageHost.FlowDirection = I18N.Dir;
        foreach (var p in _pages.Values)
        {
            p.FlowDirection = I18N.Dir;
            try { if (p is ILocalizable loc) loc.ApplyTexts(); }
            catch (Exception ex) { Core.Logger.Error("UI", "ApplyTexts failed: " + ex.Message); }
        }
        SubTitle.Text = I18N.CurrentLang == "fa" ? "تترینگ حرفه‌ای + کنترل دکل" : "Pro tethering + RAN control";

        // Direct references — the visual tree is NOT walkable inside the ctor
        // (the window template is applied only at Show), so FindVisualChildren
        // finds nothing there and the menu stayed Persian after a restart.
        NavDashboard.Content = I18N.T("nav_dashboard");
        NavSignal.Content = I18N.T("nav_signal");
        NavNetwork.Content = I18N.T("nav_network");
        NavApps.Content = I18N.T("nav_apps");
        NavSpeed.Content = I18N.T("nav_speed");
        NavSettings.Content = I18N.T("nav_settings");
        NavLogs.Content = I18N.T("nav_logs");
        NavAbout.Content = I18N.T("nav_about");
    }

    /// <summary>Live language switch — no process restart needed. Called from the Settings page.
    /// The whole window is rebuilt exactly like a fresh start (the deterministic path),
    /// while proxy/bridge singletons keep running — the connection never drops.</summary>
    public void SwitchLanguage(string lang)
    {
        I18N.Init(lang);

        // carry over the visible tab and window geometry for a seamless swap
        var current = "dashboard";
        foreach (var kv in _pages)
            if (ReferenceEquals(kv.Value, PageHost.Content)) { current = kv.Key; break; }

        var fresh = new MainWindow { StartTab = current };
        fresh.Left = Left; fresh.Top = Top;
        fresh.Width = Width; fresh.Height = Height;
        fresh.WindowState = WindowState;
        Application.Current.MainWindow = fresh;
        fresh.Show();
        Close();
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || PageHost == null) return;
        if (_pages.TryGetValue(rb.Tag?.ToString() ?? "", out var page))
            PageHost.Content = page;
    }

    private void RefreshStatusPills()
    {
        var st = ConnectionManager.Instance.State;
        StatusPill.Text = st.Connected ? I18N.T("conn_connected") : I18N.T("conn_disconnected");
        DotStatus.Fill = st.Connected
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        TransportPill.Text = (st.Connected ? I18N.T(st.Transport) : I18N.T("tr_none"))
            + (st.Connected ? $"  •  {st.Endpoint}" : "");
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dep) where T : DependencyObject
    {
        if (dep == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = VisualTreeHelper.GetChild(dep, i);
            if (child is T t) yield return t;
            foreach (var inner in FindVisualChildren<T>(child)) yield return inner;
        }
    }
}
