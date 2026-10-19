using System.Windows;
using Forms = System.Windows.Forms;

namespace HardMon;

// Иконка в трее: левый клик — показать/скрыть виджет, правый — меню, подсказка — текущие значения
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _showItem, _compactItem, _horizontalItem, _topmostItem, _autostartItem;

    public event Action? ToggleVisibility;
    public event Action? ToggleCompact;
    public event Action? ToggleHorizontal;
    public event Action<double>? ScaleRequested;

    private readonly (double Scale, Forms.ToolStripMenuItem Item)[] _scaleItems;
    public event Action? ToggleTopmost;
    public event Action? ToggleAutostart;
    public event Action? About;
    public event Action? Exit;

    public TrayIcon()
    {
        _showItem = new Forms.ToolStripMenuItem("Показать виджет", null, (_, _) => ToggleVisibility?.Invoke());
        _compactItem = new Forms.ToolStripMenuItem("Компактный режим", null, (_, _) => ToggleCompact?.Invoke());
        _horizontalItem = new Forms.ToolStripMenuItem("Горизонтально", null, (_, _) => ToggleHorizontal?.Invoke());
        _topmostItem = new Forms.ToolStripMenuItem("Поверх всех окон", null, (_, _) => ToggleTopmost?.Invoke());
        _autostartItem = new Forms.ToolStripMenuItem("Запускать при входе в Windows", null, (_, _) => ToggleAutostart?.Invoke());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(_compactItem);
        menu.Items.Add(_horizontalItem);

        var scaleMenu = new Forms.ToolStripMenuItem("Размер");
        _scaleItems = new[] { 1.0, 1.5, 2.0 }
            .Select(s => (s, new Forms.ToolStripMenuItem($"{s * 100:0}%", null, (_, _) => ScaleRequested?.Invoke(s))))
            .ToArray();
        foreach (var (_, item) in _scaleItems)
            scaleMenu.DropDownItems.Add(item);
        menu.Items.Add(scaleMenu);
        menu.Items.Add(_topmostItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("О программе…", null, (_, _) => About?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Выход", null, (_, _) => Exit?.Invoke()));

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "HardMon",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                ToggleVisibility?.Invoke();
        };
    }

    public void SetState(bool visible, bool topmost, bool autostart, bool compact, bool horizontal, double scale)
    {
        foreach (var (s, item) in _scaleItems)
            item.Checked = Math.Abs(s - scale) < 0.01;
        _showItem.Text = visible ? "Скрыть виджет" : "Показать виджет";
        _compactItem.Checked = compact;
        _horizontalItem.Checked = horizontal;
        _horizontalItem.Enabled = compact;
        _topmostItem.Checked = topmost;
        _autostartItem.Checked = autostart;
    }

    // Подсказка ограничена 127 символами
    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
