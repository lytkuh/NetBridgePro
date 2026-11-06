using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NetBridge.Windows.Ui.Controls;

/// <summary>Lightweight live line chart (no external charting dependency).</summary>
public partial class SpeedChart : UserControl
{
    private readonly Queue<double> _series = new();
    private readonly Brush _lineBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
    private readonly Brush _fillBrush = new SolidColorBrush(Color.FromArgb(36, 0x22, 0xD3, 0xEE));
    private readonly Brush _gridBrush = new SolidColorBrush(Color.FromArgb(28, 0x8B, 0x94, 0xA7));
    private readonly Brush _labelBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0xA7));

    public string Unit { get; set; } = "Mbps";
    public int Capacity { get; set; } = 120;

    public SpeedChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
    }

    public void Push(double value)
    {
        _series.Enqueue(value);
        while (_series.Count > Capacity) _series.Dequeue();
        Render();
    }

    public void Clear()
    {
        _series.Clear();
        Render();
    }

    private void Render()
    {
        Plot.Children.Clear();
        var w = Math.Max(1, Plot.ActualWidth);
        var h = Math.Max(1, Plot.ActualHeight);

        // grid: 4 horizontal lines
        for (var i = 1; i <= 4; i++)
        {
            var y = h * i / 5.0;
            var line = new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = _gridBrush, StrokeThickness = 1 };
            Plot.Children.Add(line);
            var lbl = new TextBlock
            {
                Text = FormatValue(MaxValue * (5 - i) / 5.0),
                FontSize = 9.5,
                Foreground = _labelBrush,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Canvas.SetTop(lbl, y - 7);
            Canvas.SetLeft(lbl, 0);
            Plot.Children.Add(lbl);
        }

        if (_series.Count < 2) return;

        var max = Math.Max(MaxValue, 1e-6);
        var pts = _series.ToList();
        var step = w / (Capacity - 1);
        var startIdx = Capacity - pts.Count;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            Point Map(int i, double v) => new((startIdx + i) * step, h - (v / max) * (h - 8) - 2);

            var first = Map(0, pts[0]);
            ctx.BeginFigure(first, isFilled: true, isClosed: false);
            var poly = new List<Point> { first };
            for (var i = 1; i < pts.Count; i++) poly.Add(Map(i, pts[i]));
            foreach (var p in poly) ctx.LineTo(p, true, false);

            // fill polygon (down to baseline)
            var fill = new List<Point>(poly)
            {
                new(poly[^1].X, h), new(poly[0].X, h)
            };
            ctx.BeginFigure(fill[0], isFilled: true, isClosed: true);
            for (var i = 1; i < fill.Count; i++) ctx.LineTo(fill[i], true, false);
        }
        geometry.Freeze();

        Plot.Children.Add(new Path { Data = geometry, Stroke = _lineBrush, StrokeThickness = 1.8 });
    }

    private double MaxValue => _series.Count == 0 ? 1 : Math.Max(1, _series.Max() * 1.25);

    private string FormatValue(double v) =>
        v >= 1_000_000 ? $"{v / 1_000_000:F1} {Unit}" :
        v >= 1000 ? $"{v / 1000:F1} K{Unit}" :
        $"{v:F1} {Unit}";
}
