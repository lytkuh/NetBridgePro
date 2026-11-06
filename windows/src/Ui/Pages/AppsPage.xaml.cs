using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NetBridge.Windows.Core;
using NetBridge.Windows.Firewall;

namespace NetBridge.Windows.Ui;

/// <summary>Row wrapper so the DataGrid checkbox can reach the app path.</summary>
public class AppRow : INotifyPropertyChanged
{
    private bool _blocked;
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool Blocked
    {
        get => _blocked;
        set { _blocked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Blocked))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class AppsPage : Page, NetBridge.Windows.Core.ILocalizable
{
    public List<AppRow> Apps { get; } = new();

    public AppsPage()
    {
        InitializeComponent();
        DataContext = this;
        ApplyTexts();
        AppsGrid.ItemsSource = Apps;
        Loaded += (_, _) => Reload();
    }

    public void ApplyTexts()
    {
        FlowDirection = I18N.Dir;
        TitleText.Text = I18N.T("apps_title");
        HintText.Text = I18N.T("apps_hint");
        RefreshBtn.Content = I18N.T("apps_refresh");
        ElevText.Text = AppFirewall.IsElevated
            ? I18N.T("apps_elev_ok")
            : I18N.T("apps_elev_warn");
    }

    private void Reload()
    {
        AppFirewall.RefreshRunningApps();
        Apps.Clear();
        foreach (var a in AppFirewall.ListedApps)
            Apps.Add(new AppRow { Name = a.Name, Path = a.Path, Blocked = a.Blocked });
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Block_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox cb) return;
        if (cb.DataContext is not AppRow row) return;

        if (!AppFirewall.SetBlocked(row.Path, row.Blocked))
        {
            MessageBox.Show(I18N.T("apps_hint"), "NetBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
            Reload();
        }
    }
}
