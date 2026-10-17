using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace HardMon;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --dump: записать все датчики в sensors.txt рядом с exe и выйти
        if (e.Args.Contains("--dump"))
        {
            using var reader = new HardwareReader();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sensors.txt"), reader.Dump() + new NetworkReader().Dump());
            Shutdown();
            return;
        }

        // --autostart on|off: настроить задачу автозапуска и выйти (вызывать с правами админа)
        int ai = Array.IndexOf(e.Args, "--autostart");
        if (ai >= 0 && ai + 1 < e.Args.Length)
        {
            Shutdown(Autostart.Set(e.Args[ai + 1] == "on") ? 0 : 1);
            return;
        }

        // Температура CPU требует прав администратора — перезапускаемся с повышением
        if (!IsAdmin() && !e.Args.Contains("--no-elevate"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
                Shutdown();
                return;
            }
            catch
            {
                // Пользователь отказал в UAC — работаем без температуры CPU
            }
        }

        // Только одна копия виджета
        _mutex = new Mutex(true, @"Global\HardMon.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private Mutex? _mutex;

    private static bool IsAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
