using System.IO;
using System.Text.Json;

namespace HardMon;

// Настройки окна в %AppData%\HardMon\settings.json
public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public bool Hidden { get; set; }
    public bool Compact { get; set; }
    public bool Horizontal { get; set; }
    public double Scale { get; set; } = 1.0;

    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HardMon", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // Битый файл — начинаем с умолчаний
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Не критично
        }
    }
}
