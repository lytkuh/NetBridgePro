using System.Windows.Controls;
using System.Windows.Threading;
using NetBridge.Windows.Bridge;
using NetBridge.Windows.Core;
using NetBridge.Windows.Net;

namespace NetBridge.Windows.Ui;

public partial class SignalPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static SignalData? _last;

    static SignalPage()
    {
        ConnectionManager.Instance.Signal += s => _last = s;
    }

    public SignalPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var s = _last;
        if (s == null)
        {
            RatBig.Text = "—";
            NrBadge.Text = I18N.T("signal_nodata");
            return;
        }

        RatBig.Text = s.NetworkType;
        NrBadge.Text = s.NrState switch
        {
            "CONNECTED" => "NR CONNECTED (NSA/SA active)",
            "NOT_RESTRICTED" => "NR detected (NSA)",
            "RESTRICTED" => "NR restricted",
            _ => "LTE only"
        };
        OperatorText.Text = string.IsNullOrEmpty(s.Operator) ? "" : s.Operator;

        if (s.Rsrp.HasValue)
        {
            ValRsrp.Text = $"{s.Rsrp} dBm";
            BarRsrp.Value = s.Rsrp.Value;
            RsrpChart.Push(s.Rsrp.Value + 140); // normalize 0..96 for the chart
        }
        if (s.Sinr.HasValue) { ValSinr.Text = $"{s.Sinr} dB"; BarSinr.Value = s.Sinr.Value; }
        if (s.Rsrq.HasValue) { ValRsrq.Text = $"{s.Rsrq} dB"; BarRsrq.Value = s.Rsrq.Value; }
        if (s.Rssi.HasValue) { ValRssi.Text = $"{s.Rssi} dBm"; BarRssi.Value = s.Rssi.Value; }

        BandVal.Text = string.IsNullOrEmpty(s.Band) ? "—" : s.Band;
        PciVal.Text = s.Pci?.ToString() ?? "—";
        TacVal.Text = string.IsNullOrEmpty(s.Tac) ? "—" : s.Tac;
        CidVal.Text = string.IsNullOrEmpty(s.CellId) ? "—" : s.CellId;
        CqiVal.Text = s.Cqi?.ToString() ?? "—";
        DataVal.Text = s.IsDataConnected ? "connected" : "idle";
    }
}
