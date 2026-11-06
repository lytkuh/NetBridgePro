using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetBridge.Windows.Core;

namespace NetBridge.Windows.Ui;

public partial class LogsPage : Page, NetBridge.Windows.Core.ILocalizable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private int _minLevel = 1; // INFO

    public LogsPage()
    {
        InitializeComponent();
        ApplyTexts();

        Loaded += (_, _) => { _timer.Start(); Refresh(); };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => Refresh();
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir;
        TitleText.Text = I18N.T("log_title");
        CopyBtn.Content = I18N.T("log_copy");
        ClearBtn.Content = I18N.T("log_clear");
        AutoScroll.Content = I18N.T("log_autoscroll");
    }

    private void Level_Changed(object sender, SelectionChangedEventArgs e)
    {
        _minLevel = LevelBox.SelectedIndex switch
        {
            0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 1
        };
    }

    private void Refresh()
    {
        var lines = Logger.Recent(800)
            .Where(l => LevelOf(l) >= _minLevel);
        LogBox.Text = string.Join(Environment.NewLine, lines);
        if (AutoScroll.IsChecked == true)
        {
            LogBox.CaretIndex = LogBox.Text.Length;
            LogBox.ScrollToEnd();
        }
    }

    private static int LevelOf(string line)
    {
        // format: "MM-dd HH:mm:ss.fff L/TAG: msg" -> level is 3rd space-separated token
        var parts = line.Split(' ');
        var lChar = parts.Length > 2 && parts[2].Length > 0 ? parts[2][0] : 'I';
        return lChar switch
        {
            'D' => 0,
            'I' => 1,
            'W' => 2,
            'E' => 3,
            _ => 1
        };
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(LogBox.Text); } catch { }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => LogBox.Clear();
}
