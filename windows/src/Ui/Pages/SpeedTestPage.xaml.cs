using System.Windows;
using System.Windows.Controls;
using NetBridge.Windows.Core;
using NetBridge.Windows.Net;

namespace NetBridge.Windows.Ui;

public partial class SpeedTestPage : Page, NetBridge.Windows.Core.ILocalizable
{
    private readonly SpeedTest _test = new();

    public SpeedTestPage()
    {
        InitializeComponent();
        ApplyTexts();
        SecondsBox.Text = SettingsStore.Current.SpeedTestSeconds.ToString();

        _test.OnProgress += p =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                PhaseText.Text = p.Phase switch
                {
                    "ping" => I18N.T("speed_ping") + "…",
                    "download" => I18N.T("speed_download") + $"  {p.LiveMbps:F1} Mbps",
                    "upload" => I18N.T("speed_upload") + $"  {p.LiveMbps:F1} Mbps",
                    _ => ""
                };
                PhaseBar.Value = p.Percent;
                if (p.Phase is "download" or "upload") LiveChart.Push(p.LiveMbps);
            });
        };

        _test.OnFinished += r =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                DownVal.Text = $"{r.DownloadMbps:F1} Mbps";
                UpVal.Text = $"{r.UploadMbps:F1} Mbps";
                PingVal.Text = $"{r.PingMs:F0} ms";
                JitterVal.Text = $"{r.JitterMs:F1} ms";
                RunBtn.IsEnabled = true;
                CancelBtn.IsEnabled = false;
                PhaseText.Text = "";
                PhaseBar.Value = 0;
            });
        };
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir;
        TitleText.Text = I18N.T("speed_title");
        HintText.Text = I18N.T("speed_hint");
        DurLabel.Text = I18N.T("speed_duration") + ":";
        RunBtn.Content = I18N.T("speed_run");
        CancelBtn.Content = I18N.T("speed_cancel");
        DownLabel.Text = I18N.T("speed_download");
        UpLabel.Text = I18N.T("speed_upload");
        PingLabel.Text = I18N.T("speed_ping");
        JitterLabel.Text = I18N.T("speed_jitter");
        ClearBtn.Content = I18N.T("speed_clear");
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var seconds = int.TryParse(SecondsBox.Text, out var s) && s is >= 3 and <= 60 ? s : 10;
        SettingsStore.Current.SpeedTestSeconds = seconds;
        SettingsStore.Save();
        LiveChart.Clear();
        RunBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        _ = _test.RunAsync(seconds);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _test.Cancel();

    private void Clear_Click(object sender, RoutedEventArgs e) => LiveChart.Clear();
}
