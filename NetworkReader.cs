using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace HardMon;

// Режим выхода в интернет: напрямую, через VPN, или VPN поднят, но интернет идёт мимо него (split-tunnel)
public enum VpnState { Off, On, Split }

// Скорость приёма в Мбит/с: полезная нагрузка через VPN и напрямую (без двойного счёта)
public record NetReadings(double VpnMbps, double DirectMbps, VpnState Vpn, string? TunnelName)
{
    public double TotalMbps => VpnMbps + DirectMbps;
}

public sealed partial class NetworkReader
{
    // Туннельные адаптеры VPN-клиентов (OpenVPN Connect: DCO и TAP-Windows; WireGuard/AmneziaWG; Wintun)
    [GeneratedRegex("OpenVPN|TAP-Windows|Wintun|WireGuard|AmneziaWG", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelPattern();

    // Накладные расходы на каждый пакет туннеля, которые физический адаптер получает сверх
    // содержимого туннеля. Теория для OpenVPN AES-GCM: Ethernet 14 + IPv4 20 + UDP 8 + заголовок с тегом 24 = 66;
    // замер на OpenVPN Connect (TAP, закачка 100 МБ ×3) дал 60–95, в среднем 76 — берём замер
    public const int TunnelOverheadBytes = 76;

    // Адрес для проверки маршрута: через какой интерфейс уйдёт пакет в интернет
    private static readonly uint ProbeAddress = BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes());

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<ulong, (ulong Octets, ulong Packets)> _prev = new();
    private TimeSpan _prevTime;

    public NetReadings Read()
    {
        var rows = ReadTable();
        var now = _clock.Elapsed;
        double seconds = (now - _prevTime).TotalSeconds;
        bool first = _prev.Count == 0;
        _prevTime = now;

        double physBytes = 0, tunBytes = 0, tunPackets = 0;
        string? tunnelName = null;
        var tunnelIndexes = new HashSet<uint>();
        var seen = new HashSet<ulong>();
        uint? best = GetBestInterface(ProbeAddress, out uint bestIndex) == 0 ? bestIndex : null;

        foreach (var row in rows)
        {
            var kind = Classify(row, best);
            if (kind == Kind.Other)
                continue;

            ulong packets = row.InUcastPkts + row.InNUcastPkts;
            seen.Add(row.InterfaceLuid);
            bool known = _prev.TryGetValue(row.InterfaceLuid, out var prev);
            _prev[row.InterfaceLuid] = (row.InOctets, packets);
            if (kind == Kind.Tunnel)
            {
                tunnelIndexes.Add(row.InterfaceIndex);
                tunnelName ??= row.Description;
            }

            // Новый интерфейс или сброс счётчика (переподключение) — пропускаем один такт
            if (!known || row.InOctets < prev.Octets || packets < prev.Packets)
                continue;

            double bytes = row.InOctets - prev.Octets;
            if (kind == Kind.Physical)
                physBytes += bytes;
            else
            {
                tunBytes += bytes;
                tunPackets += packets - prev.Packets;
            }
        }

        // Отключённые адаптеры забываем, чтобы при возврате не посчитать накопленное за раз
        foreach (var luid in _prev.Keys.Where(k => !seen.Contains(k)).ToList())
            _prev.Remove(luid);

        var state = tunnelIndexes.Count == 0 ? VpnState.Off
            : best is uint b && tunnelIndexes.Contains(b) ? VpnState.On
            : VpnState.Split;

        if (first || seconds <= 0)
            return new NetReadings(0, 0, state, tunnelName);

        // Зашифрованный трафик туннеля физический адаптер тоже видит — вычитаем его вместе с оверхедом
        double direct = Math.Max(0, physBytes - tunBytes - tunPackets * TunnelOverheadBytes);
        const double toMbps = 8 / 1e6;
        return new NetReadings(tunBytes / seconds * toMbps, direct / seconds * toMbps, state, tunnelName);
    }

    private enum Kind { Other, Physical, Tunnel }

    // Физический — реальная сетевая карта (без фильтров NDIS и виртуальных Hyper-V/WSL/WAN Miniport).
    // Туннель — известный VPN-адаптер по имени, либо любой виртуальный адаптер (IF_TYPE_PROP_VIRTUAL: TAP, Wintun —
    // так выглядят sing-box/Clash/v2rayN в режиме TUN, WireSock и пр.), через который идёт маршрут в интернет.
    // Безымянные виртуальные адаптеры без маршрута (Tailscale, ZeroTier) не считаем, чтобы не гореть «VPN мимо»
    private static Kind Classify(in MIB_IF_ROW2 row, uint? bestIndex)
    {
        const int ifOperStatusUp = 1;
        const uint ifTypePropVirtual = 53;
        if (row.OperStatus != ifOperStatusUp)
            return Kind.Other;
        bool hardware = (row.InterfaceAndOperStatusFlags & 1) != 0;
        bool filter = (row.InterfaceAndOperStatusFlags & 2) != 0;
        if (filter)
            return Kind.Other;
        if (TunnelPattern().IsMatch(row.Description))
            return Kind.Tunnel;
        if (!hardware && row.Type == ifTypePropVirtual && row.InterfaceIndex == bestIndex)
            return Kind.Tunnel;
        return hardware ? Kind.Physical : Kind.Other;
    }

    private static List<MIB_IF_ROW2> ReadTable()
    {
        var list = new List<MIB_IF_ROW2>();
        if (GetIfTable2(out var table) != 0)
            return list;
        try
        {
            int count = Marshal.ReadInt32(table);
            int size = Marshal.SizeOf<MIB_IF_ROW2>();
            // Строки таблицы выровнены по 8 байт после поля NumEntries
            for (int i = 0; i < count; i++)
                list.Add(Marshal.PtrToStructure<MIB_IF_ROW2>(table + 8 + i * size));
        }
        finally
        {
            FreeMibTable(table);
        }
        return list;
    }

    // Текстовый дамп сетевых интерфейсов и скоростей за 1 с — для отладки (--dump).
    // Пишет все интерфейсы, включая отброшенные как Other: по нему видно, почему туннель не распознан
    public string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[Network] MIB_IF_ROW2 size = {Marshal.SizeOf<MIB_IF_ROW2>()} (ожидается 1352)");
        uint? best = GetBestInterface(ProbeAddress, out uint bestIndex) == 0 ? bestIndex : null;
        sb.AppendLine("  kind     #idx type tun media phys flags oper in-octets        description | alias");
        foreach (var row in ReadTable())
        {
            var kind = Classify(row, best);
            sb.AppendLine($"  {kind,-8} #{row.InterfaceIndex,-3} {row.Type,4} {row.TunnelType,3} {row.MediaType,5} {row.PhysicalMediumType,4} " +
                          $"0x{row.InterfaceAndOperStatusFlags:X2}  {row.OperStatus,4} {row.InOctets,-16} {row.Description} | {row.Alias}");
        }
        sb.AppendLine(best is uint b ? $"Маршрут до 1.1.1.1: интерфейс #{b}" : "Маршрут до 1.1.1.1: не определён");
        sb.AppendLine($"Системный прокси: {SystemProxy()}");
        Read();
        Thread.Sleep(1000);
        sb.AppendLine($"Network: {Read()}");
        return sb.ToString();
    }

    // Прокси из настроек Windows: VPN-клиенты в режиме прокси (Shadowsocks без TUN) своего адаптера не создают
    private static string SystemProxy()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        if (key is null)
            return "нет данных";
        bool enabled = key.GetValue("ProxyEnable") is int on && on != 0;
        return enabled ? $"включён, {key.GetValue("ProxyServer")}" : $"выключен; PAC: {key.GetValue("AutoConfigURL") ?? "нет"}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetIfTable2(out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);
}
