using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HardMon;

public partial class MainWindow : Window
{
    // Плитка: график + заголовок + значение + подпись
    private sealed class Tile
    {
        public required Sparkline Chart;
        public required TextBlock Value;
        public required TextBlock Detail;
        public required FrameworkElement TextPanel;
        public required FrameworkElement Row;
    }

    private readonly List<Tile> _tiles = new();

    // Разделитель между группами ЦП и GPU
    private readonly Border _separator = new() { Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) };

    private readonly Settings _settings = Settings.Load();
    private readonly CancellationTokenSource _cts = new();
    private HardwareReader? _reader;

    private readonly Tile _cpuLoad, _cpuTemp, _gpuTemp, _gpuLoad, _ram, _vram;

    public MainWindow()
    {
        InitializeComponent();

        _cpuLoad = AddTile("Загрузка ЦП", "ЦП", Color.FromRgb(0x11, 0x91, 0xD8));
        _cpuTemp = AddTile("Темп. ЦП", "ЦП°", Color.FromRgb(0x4D, 0xD0, 0xE1));
        _ram = AddTile("Память", "ОЗУ", Color.FromRgb(0xB3, 0x88, 0xFF));
        Tiles.Children.Add(_separator);
        _gpuLoad = AddTile("Загрузка GPU", "GPU", Color.FromRgb(0x9C, 0xCC, 0x65));
        _gpuTemp = AddTile("Темп. GPU", "GPU°", Color.FromRgb(0xFF, 0x70, 0x43));
        _vram = AddTile("Видеопамять", "VRAM", Color.FromRgb(0xF0, 0x62, 0x92));

        Topmost = _settings.Topmost;
        Opacity = _settings.Opacity;
        ApplyLayout(_settings.Compact, _settings.Horizontal);
        ApplyScale(_settings.Scale);
        _tray.ScaleRequested += SetScale;
        _tray.ToggleCompact += () => SetLayout(!_settings.Compact, _settings.Horizontal);
        _tray.ToggleHorizontal += () => SetLayout(_settings.Compact, !_settings.Horizontal);

        _tray.ToggleVisibility += () => SetWidgetVisible(!IsVisible);
        _tray.ToggleTopmost += () => SetTopmost(!Topmost);
        _tray.ToggleAutostart += () => SetAutostart(!Autostart.IsEnabled());
        _tray.Exit += Close;
        SyncMenus();

        Loaded += (_, _) =>
        {
            PlaceOnScreen();
            Task.Run(() => PollLoop(_cts.Token));
            if (_settings.Hidden)
                SetWidgetVisible(false);
            else
                SyncMenus();
        };
    }

    private readonly TrayIcon _tray = new();


    private void SyncMenus()
    {
        bool autostart = Autostart.IsEnabled();
        TopmostItem.IsChecked = Topmost;
        AutostartItem.IsChecked = autostart;
        CompactItem.IsChecked = _settings.Compact;
        HorizontalItem.IsChecked = _settings.Horizontal;
        HorizontalItem.IsEnabled = _settings.Compact;
        foreach (MenuItem item in ScaleMenu.Items)
            item.IsChecked = Math.Abs(double.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture) - _settings.Scale) < 0.01;
        _tray.SetState(IsVisible, Topmost, autostart, _settings.Compact, _settings.Horizontal, _settings.Scale);
    }

    private void SetWidgetVisible(bool visible)
    {
        if (visible)
        {
            Show();
            Activate();
        }
        else
        {
            Hide();
        }
        _settings.Hidden = !visible;
        _settings.Save();
        SyncMenus();
    }

    private void SetTopmost(bool topmost)
    {
        Topmost = topmost;
        _settings.Topmost = topmost;
        _settings.Save();
        SyncMenus();
    }

    private void SetAutostart(bool enable)
    {
        Autostart.Set(enable);
        SyncMenus();
    }

    // Позиция из настроек, но не за пределами рабочей области (по умолчанию — правый верхний угол)
    private void PlaceOnScreen()
    {
        var area = SystemParameters.VirtualScreenWidth > 0
            ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                       SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
            : SystemParameters.WorkArea;
        var work = SystemParameters.WorkArea;
        double left = _settings.Left ?? work.Right - ActualWidth - 16;
        double top = _settings.Top ?? work.Top + 40;
        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
    }

    private Tile AddTile(string title, string shortLabel, Color accent)
    {
        var chart = new Sparkline { Width = 70, Height = 46, Accent = accent, Label = shortLabel, Margin = new Thickness(0, 0, 10, 0) };
        var titleText = new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13 };
        var value = new TextBlock { Text = "…", Foreground = new SolidColorBrush(accent), FontSize = 15, FontWeight = FontWeights.SemiBold };
        var detail = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), FontSize = 11 };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 };
        text.Children.Add(titleText);
        text.Children.Add(value);
        text.Children.Add(detail);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(chart);
        row.Children.Add(text);
        Tiles.Children.Add(row);

        var tile = new Tile { Chart = chart, Value = value, Detail = detail, TextPanel = text, Row = row };
        _tiles.Add(tile);
        return tile;
    }

    // Компактный режим: только графики с короткими подписями, без текстовой колонки;
    // горизонтальный вид — графики в строку (только в компактном режиме)
    private void ApplyLayout(bool compact, bool horizontal)
    {
        horizontal &= compact;
        foreach (var t in _tiles)
        {
            t.TextPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            t.Chart.ShowLabel = compact;
            t.Chart.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 10, 0);
            t.Row.Margin = horizontal ? new Thickness(3, 0, 3, 0) : new Thickness(0, 4, 0, 4);
        }
        Tiles.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        _separator.Width = horizontal ? 1 : double.NaN;
        _separator.Height = horizontal ? double.NaN : 1;
        _separator.Margin = horizontal ? new Thickness(4, 0, 4, 0) : new Thickness(0, 4, 0, 4);
        Root.Padding = compact ? new Thickness(horizontal ? 4 : 6, 6, horizontal ? 4 : 6, 6) : new Thickness(8, 6, 10, 6);
    }

    private void SetLayout(bool compact, bool horizontal)
    {
        KeepRightEdge(() => ApplyLayout(compact, horizontal));
        _settings.Compact = compact;
        _settings.Horizontal = horizontal;
        _settings.Save();
        SyncMenus();
    }

    // Масштаб всего виджета (векторная отрисовка — графики остаются чёткими)
    private void ApplyScale(double scale) => Root.LayoutTransform = new ScaleTransform(scale, scale);

    private void SetScale(double scale)
    {
        KeepRightEdge(() => ApplyScale(scale));
        _settings.Scale = scale;
        _settings.Save();
        SyncMenus();
    }

    // Сохраняем правый край окна на месте, чтобы виджет не «уезжал» от края экрана
    private void KeepRightEdge(Action change)
    {
        double right = Left + ActualWidth;
        change();
        UpdateLayout();
        Left = right - ActualWidth;
        _settings.Left = Left;
    }

    private void OnScaleClick(object sender, RoutedEventArgs e) =>
        SetScale(double.Parse((string)((MenuItem)sender).Tag, System.Globalization.CultureInfo.InvariantCulture));

    private void OnCompactClick(object sender, RoutedEventArgs e) => SetLayout(CompactItem.IsChecked, _settings.Horizontal);

    private void OnHorizontalClick(object sender, RoutedEventArgs e) => SetLayout(_settings.Compact, HorizontalItem.IsChecked);

    private async Task PollLoop(CancellationToken ct)
    {
        try
        {
            _reader = new HardwareReader();
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => _cpuTemp.Detail.Text = "Ошибка: " + ex.Message);
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            Readings r;
            try { r = _reader.Read(); }
            catch { await Task.Delay(1000, ct).ContinueWith(_ => { }); continue; }

            await Dispatcher.InvokeAsync(() => Render(r));
            await Task.Delay(1000, ct).ContinueWith(_ => { });
        }
    }

    private void Render(Readings r)
    {
        // Числа для компактного режима (правый верхний угол графика)
        _cpuLoad.Chart.ValueText = Fmt(r.CpuLoad, "%");
        _cpuTemp.Chart.ValueText = Fmt(r.CpuTemp, "°");
        _ram.Chart.ValueText = Fmt(r.RamLoad, "%");
        _gpuLoad.Chart.ValueText = Fmt(r.GpuLoad, "%");
        _gpuTemp.Chart.ValueText = Fmt(r.GpuTemp, "°");
        _vram.Chart.ValueText = Fmt(r.VramLoad, "%");

        _cpuLoad.Chart.Push(r.CpuLoad);
        _cpuLoad.Value.Text = r.CpuLoad is { } cl ? $"{cl:0}%" : "—";
        _cpuLoad.Detail.Text = r.CpuLoad is null ? "нет данных" : "";

        SetTemp(_cpuTemp, r.CpuTemp, "нет доступа к датчику");
        SetTemp(_gpuTemp, r.GpuTemp, "GPU не найден или спит");

        _gpuLoad.Chart.Push(r.GpuLoad);
        _gpuLoad.Value.Text = r.GpuLoad is { } gl ? $"{gl:0}%" : "—";
        _gpuLoad.Detail.Text = r.GpuLoad is null ? "нет данных" : "";

        _ram.Chart.Push(r.RamLoad);
        _ram.Value.Text = $"{r.RamLoad:0}%";
        _ram.Detail.Text = $"{r.RamUsedGb:0.0} / {r.RamTotalGb:0.0} ГБ";

        _vram.Chart.Push(r.VramLoad);
        _vram.Value.Text = r.VramLoad is { } vl ? $"{vl:0}%" : "—";
        _vram.Detail.Text = r.VramUsedGb is { } vu ? $"{vu:0.0} / {r.VramTotalGb:0.0} ГБ" : "нет данных";

        _tray.SetTooltip(
            $"ЦП: {Fmt(r.CpuLoad, "%")}, {Fmt(r.CpuTemp, " °C")}\n" +
            $"Память: {r.RamLoad:0}%\n" +
            $"GPU: {Fmt(r.GpuLoad, "%")}, {Fmt(r.GpuTemp, " °C")}\n" +
            $"Видеопамять: {Fmt(r.VramLoad, "%")}");
    }

    private static string Fmt(float? v, string unit) => v is { } x ? $"{x:0}{unit}" : "—";

    private static void SetTemp(Tile tile, float? temp, string missingHint)
    {
        // Шкала графика температуры — 0..100 °C
        tile.Chart.Push(temp);
        if (temp is not { } t)
        {
            tile.Value.Text = "—";
            tile.Detail.Text = missingHint;
            return;
        }
        tile.Value.Text = $"{t:0} °C";
        tile.Detail.Text = t >= 90 ? "горячо!" : t >= 80 ? "тепло" : "";
        tile.Detail.Foreground = t >= 90 ? Brushes.OrangeRed : t >= 80 ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => SetWidgetVisible(false);

    private void OnTopmostClick(object sender, RoutedEventArgs e) => SetTopmost(TopmostItem.IsChecked);

    private void OnOpacityClick(object sender, RoutedEventArgs e)
    {
        Opacity = double.Parse((string)((MenuItem)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        _settings.Opacity = Opacity;
        _settings.Save();
    }

    private void OnAutostartClick(object sender, RoutedEventArgs e) => SetAutostart(AutostartItem.IsChecked);

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _tray.Dispose();
        _reader?.Dispose();
        base.OnClosed(e);
    }
}
