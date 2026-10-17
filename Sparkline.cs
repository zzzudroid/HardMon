using System.Windows;
using System.Windows.Media;

namespace HardMon;

// Мини-график в стиле Диспетчера задач: сетка, заливка и линия, шкала 0..100
public sealed class Sparkline : FrameworkElement
{
    public const int Capacity = 60;

    private readonly Queue<double> _values = new();
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1));

    public Color Accent { get; set; } = Colors.DeepSkyBlue;

    // Короткая подпись в левом верхнем углу (рисуется только в компактном режиме)
    public string Label { get; set; } = "";

    // Текущее значение в правом верхнем углу (тоже только в компактном режиме)
    public string ValueText { get; set; } = "";

    private bool _showLabel;
    public bool ShowLabel
    {
        get => _showLabel;
        set { _showLabel = value; InvalidateVisual(); }
    }

    public void Push(double? value)
    {
        _values.Enqueue(Math.Clamp(value ?? 0, 0, 100));
        while (_values.Count > Capacity)
            _values.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var border = new Pen(new SolidColorBrush(Accent), 1);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, new Rect(0, 0, w, h));

        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(h * i / 4) + 0.5;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }

        if (_values.Count > 1)
        {
            // Последнее значение прижато к правому краю, как в Диспетчере задач
            double step = w / (Capacity - 1);
            double x0 = w - (_values.Count - 1) * step;
            var points = _values.Select((v, i) => new Point(x0 + i * step, h - v / 100 * h)).ToList();

            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, h), true, true);
                ctx.PolyLineTo(points, true, true);
                ctx.LineTo(new Point(points[^1].X, h), true, true);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, Accent.R, Accent.G, Accent.B)), null, fill);

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Accent), 1.2), line);
        }

        if (_showLabel)
        {
            if (Label.Length > 0)
                DrawCaption(dc, Label, Color.FromArgb(220, 255, 255, 255), alignRight: false);
            if (ValueText.Length > 0)
                DrawCaption(dc, ValueText, Accent, alignRight: true);
        }

        dc.DrawRectangle(null, border, new Rect(0.5, 0.5, w - 1, h - 1));
    }

    // Подпись в верхнем углу с подложкой, чтобы читалась поверх графика
    private void DrawCaption(DrawingContext dc, string s, Color color, bool alignRight)
    {
        var text = new FormattedText(s, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            10, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double x = alignRight ? ActualWidth - text.Width - 4 : 4;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0x1E, 0x1E, 0x1E)), null, new Rect(x - 2, 2, text.Width + 4, text.Height));
        dc.DrawText(text, new Point(x, 2));
    }

    private static Pen Freeze(Pen p)
    {
        p.Freeze();
        return p;
    }
}
