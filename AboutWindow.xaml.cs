using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace HardMon;

// Окно «О программе»: версия и ссылка на репозиторий
public partial class AboutWindow : Window
{
    public const string RepoUrl = "https://github.com/zzzudroid/HardMon";

    private static AboutWindow? _current;

    private AboutWindow()
    {
        InitializeComponent();
        // InformationalVersion содержит и хеш коммита: 1.2.1+e68aaec…
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = info.IndexOf('+');
        VersionText.Text = plus < 0 ? $"Версия {info}" : $"Версия {info[..plus]} ({info[(plus + 1)..Math.Min(info.Length, plus + 8)]})";
        RepoText.Text = RepoUrl.Replace("https://", "");
        RepoLink.ToolTip = RepoUrl;
    }

    // Одно окно на всё приложение: повторный вызов выводит уже открытое на передний план
    public static void ShowSingle()
    {
        if (_current is null)
        {
            _current = new AboutWindow();
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }
        _current.Activate();
    }

    // Через explorer.exe: виджет работает с правами админа, а браузер должен открыться обычным
    private void OnRepoClick(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", RepoUrl);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
