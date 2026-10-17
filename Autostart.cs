using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace HardMon;

// Автозапуск с правами администратора через Планировщик задач (без UAC при входе)
public static class Autostart
{
    private const string TaskName = "HardMon";

    public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Set(bool enable)
    {
        if (!enable)
            return Schtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

        // XML нужен, чтобы снять умолчания Планировщика: «не запускать от батареи» и лимит 72 часа
        var user = WindowsIdentity.GetCurrent().Name;
        var exe = Environment.ProcessPath!;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command></Exec>
              </Actions>
            </Task>
            """;
        var path = Path.Combine(Path.GetTempPath(), "HardMon-task.xml");
        File.WriteAllText(path, xml, System.Text.Encoding.Unicode);
        try
        {
            return Schtasks($"/Create /TN \"{TaskName}\" /XML \"{path}\" /F") == 0;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            p.WaitForExit(10000);
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
