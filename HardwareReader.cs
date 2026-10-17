using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace HardMon;

// Снимок показаний; null — датчик недоступен
public record Readings(
    float? CpuLoad,
    float? CpuTemp,
    float? GpuTemp,
    float? GpuLoad,
    float RamLoad, float RamUsedGb, float RamTotalGb,
    float? VramLoad, float? VramUsedGb, float? VramTotalGb);

public sealed class HardwareReader : IDisposable
{
    private readonly Computer _computer;

    public HardwareReader()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();
    }

    public Readings Read()
    {
        foreach (var hw in _computer.Hardware)
            UpdateRecursive(hw);

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Дискретная видеокарта: NVIDIA или AMD (встройку Intel пропускаем)
        var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
               ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd);

        float? cpuLoad = cpu == null ? null : Find(cpu, SensorType.Load, "CPU Total");
        float? cpuTemp = cpu == null ? null : CpuTemperature(cpu);
        float? gpuTemp = gpu == null ? null : Find(gpu, SensorType.Temperature, "GPU Core")
                                             ?? MaxOf(gpu, SensorType.Temperature);
        float? gpuLoad = gpu == null ? null : Find(gpu, SensorType.Load, "GPU Core");

        float? vramUsed = null, vramTotal = null, vramLoad = null;
        if (gpu != null)
        {
            // Значения SmallData — в мегабайтах
            var used = Find(gpu, SensorType.SmallData, "GPU Memory Used");
            var total = Find(gpu, SensorType.SmallData, "GPU Memory Total");
            if (used is > 0 && total is > 0)
            {
                vramUsed = used / 1024f;
                vramTotal = total / 1024f;
                vramLoad = used / total * 100f;
            }
        }

        var (ramLoad, ramUsed, ramTotal) = RamStatus();
        return new Readings(cpuLoad, cpuTemp, gpuTemp, gpuLoad, ramLoad, ramUsed, ramTotal, vramLoad, vramUsed, vramTotal);
    }

    // Приоритет: CPU Package → Core Max → максимум по всем температурам
    private static float? CpuTemperature(IHardware cpu) =>
        Find(cpu, SensorType.Temperature, "CPU Package")
        ?? Find(cpu, SensorType.Temperature, "Core Max")
        ?? MaxOf(cpu, SensorType.Temperature);

    private static IEnumerable<ISensor> AllSensors(IHardware hw) =>
        hw.Sensors.Concat(hw.SubHardware.SelectMany(AllSensors));

    private static float? Find(IHardware hw, SensorType type, string name) =>
        AllSensors(hw).FirstOrDefault(s => s.SensorType == type && s.Name == name && s.Value.HasValue)?.Value;

    private static float? MaxOf(IHardware hw, SensorType type)
    {
        var values = AllSensors(hw).Where(s => s.SensorType == type && s.Value is > 0).Select(s => s.Value!.Value).ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    private static void UpdateRecursive(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            UpdateRecursive(sub);
    }

    // ОЗУ берём напрямую из WinAPI — надёжнее и без драйвера
    private static (float load, float usedGb, float totalGb) RamStatus()
    {
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref ms))
            return (0, 0, 0);
        float total = ms.ullTotalPhys / 1073741824f;
        float used = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824f;
        return (used / total * 100f, used, total);
    }

    // Текстовый дамп всех датчиков — для отладки (--dump)
    public string Dump()
    {
        foreach (var hw in _computer.Hardware)
            UpdateRecursive(hw);
        var sb = new StringBuilder();
        void Walk(IHardware hw, string indent)
        {
            sb.AppendLine($"{indent}[{hw.HardwareType}] {hw.Name}");
            foreach (var s in hw.Sensors)
                sb.AppendLine($"{indent}  {s.SensorType,-12} {s.Name,-32} {s.Value}");
            foreach (var sub in hw.SubHardware)
                Walk(sub, indent + "  ");
        }
        foreach (var hw in _computer.Hardware)
            Walk(hw, "");
        sb.AppendLine();
        sb.AppendLine($"Readings: {Read()}");
        return sb.ToString();
    }

    public void Dispose() => _computer.Close();

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
