using System.Windows;
using System.Windows.Media;

namespace HardMon;

// Мини-график в стиле Диспетчера задач: сетка, заливка и линия, шкала 0..100
// (или автошкала по максимуму окна); опционально вторая серия поверх первой и значок состояния (например, «VPN»)
public sealed class Sparkline : FrameworkElement
{
    public const int Capacity = 60;

    private readonly Queue<double> _values = new();
    private readonly Queue<double> _values2 = new();
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1));

    public Color Accent { get; set; } = Colors.DeepSkyBlue;

    // Цвет второй серии (часть первой, рисуется от низа); null — серии нет
    public Color? Accent2 { get; set; }

    // Шкала по максимуму значений в окне, но не меньше MinScale
    public bool AutoScale { get; set; }
    public double MinScale { get; set; } = 1;

    // Значок в левом нижнем углу: светится цветом BadgeColor, когда BadgeLit, иначе тусклый
    public string Badge { get; set; } = "";
    public Color BadgeColor { get; set; } = Colors.LimeGreen;

    private bool _badgeLit;
    public bool BadgeLit
    {
        get => _badgeLit;
        set { _badgeLit = value; InvalidateVisual(); }
    }

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

    public void Push(double? value, double? second = null)
    {
        double max = AutoScale ? double.MaxValue : 100;
        _values.Enqueue(Math.Clamp(value ?? 0, 0, max));
        while (_values.Count > Capacity)
            _values.Dequeue();
        if (Accent2 != null)
        {
            _values2.Enqueue(Math.Clamp(second ?? 0, 0, max));
            while (_values2.Count > Capacity)
                _values2.Dequeue();
        }
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

        double scale = AutoScale ? Math.Max(MinScale, _values.DefaultIfEmpty(0).Max()) : 100;
        DrawSeries(dc, _values, Accent, scale);
        if (Accent2 is { } accent2)
            DrawSeries(dc, _values2, accent2, scale);

        if (Badge.Length > 0)
            DrawBadge(dc);

        if (_showLabel)
        {
            if (Label.Length > 0)
                DrawCaption(dc, Label, Color.FromArgb(220, 255, 255, 255), alignRight: false);
            if (ValueText.Length > 0)
                DrawCaption(dc, ValueText, Accent, alignRight: true);
        }

        dc.DrawRectangle(null, border, new Rect(0.5, 0.5, w - 1, h - 1));
    }

    private void DrawSeries(DrawingContext dc, Queue<double> values, Color color, double scale)
    {
        if (values.Count < 2)
            return;
        double w = ActualWidth, h = ActualHeight;
        // Последнее значение прижато к правому краю, как в Диспетчере задач
        double step = w / (Capacity - 1);
        double x0 = w - (values.Count - 1) * step;
        var points = values.Select((v, i) => new Point(x0 + i * step, h - v / scale * h)).ToList();

        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, h), true, true);
            ctx.PolyLineTo(points, true, true);
            ctx.LineTo(new Point(points[^1].X, h), true, true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B)), null, fill);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.2), line);
    }

    // Горящий значок — неон: светлая сердцевина и ореол из нескольких полупрозрачных обводок
    // цветом BadgeColor; погасший — тусклый серый текст без свечения
    private void DrawBadge(DrawingContext dc)
    {
        var text = new FormattedText(Badge, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            9, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var origin = new Point(5, ActualHeight - text.Height - 3);
        if (!_badgeLit)
        {
            text.SetForegroundBrush(new SolidColorBrush(Color.FromArgb(110, 0x9E, 0x9E, 0x9E)));
            dc.DrawText(text, origin);
            return;
        }

        var c = BadgeColor;
        var glyphs = text.BuildGeometry(origin);
        foreach (var (width, alpha) in new[] { (2.0, 70), (1.2, 170) })
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)alpha, c.R, c.G, c.B)), width) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, glyphs);
        }
        // Сердцевина трубки — цвет, высветленный к белому
        var core = Color.FromRgb((byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2));
        dc.DrawGeometry(new SolidColorBrush(core), null, glyphs);
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
