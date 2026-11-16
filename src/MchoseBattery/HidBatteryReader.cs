using System.Runtime.InteropServices;
using System.Text;

namespace MchoseBattery;

/// <summary>
/// Talks to MCHOSE mice that implement the "MC" protocol.
///
/// Protocol (reverse engineered from MCHOSE HUB / MCHOSE HUB Watcher):
///   Vendor HID collection (usage page 0xFF01), report ID 0x4D, 64-byte reports.
///   Packet layout: [0x4D][flags=1][business=1][dataLen][cmdLo][cmdHi][bizCode][sequence][data...][xor]
///   XOR covers bytes [2 .. 7+dataLen] inclusive.
///   "Get device info" command is 0x0900 with empty data.
///   Reply comes back as a 64-byte input report on the same report ID:
///     [0x4D][flags][flags][0x10][cmdLo][cmdHi][..][payload...]
///   Payload starts at index 7 (report-ID excluded), and is:
///     [0..1] vid LE, [2..3] pid LE, [4] maxConfigNum, [5] macroSaveType,
///     [6..7] shareMacroSize LE, [8..9] macroUnitSize LE,
///     [10] connectStatus (1 = 2.4G), [11] chargeStatus (1 = charging),
///     [12] batteryLevel, [13] gameMode.
///   With the report ID at index 0 of a raw ReadFile buffer, battery lands at buf[20],
///   connectStatus at buf[18] and chargeStatus at buf[19].
/// </summary>
internal static class HidBatteryReader
{
    // Hint used during discovery; it is not required for a cached device to match.
    private const ushort KnownVendorId = 0x3837;
    private const byte DefaultReportId = 0x4D;
    private const ushort DefaultCommand = 0x0900;

    public sealed record Reading(int Percent, int ConnectStatus, int ChargeStatus, DateTime Time, string Product)
    {
        public bool IsWireless => ConnectStatus != 0;
        public bool IsCharging => ChargeStatus == 1;
    }

    public sealed record ProbeResult(Reading? Reading, CachedDevice? Device);

    private sealed class HidInterface
    {
        public string Path = "";
        public ushort Vid;
        public ushort Pid;
        public string Product = "";
        public ushort UsagePage;
        public ushort Usage;
        public ushort InputReportLength;
        public ushort OutputReportLength;
    }

    /// <summary>
    /// Tries the cached identities first, then falls back to auto-discovery.
    /// </summary>
    /// <param name="cache">Previously discovered devices, newest first is preferred.</param>
    /// <param name="forceDiscovery">Skip the cache and rediscover (used by "重新识别设备").</param>
    public static ProbeResult Probe(IReadOnlyList<CachedDevice> cache, int timeoutMs = 2500, bool forceDiscovery = false)
    {
        var interfaces = Enumerate(openCaps: true);

        if (!forceDiscovery)
        {
            foreach (var cached in cache.OrderByDescending(c => c.LastSeenUtc))
            {
                var iface = interfaces.FirstOrDefault(i => Matches(i, cached));
                if (iface == null) continue;

                var reading = QueryWithRetry(iface, cached.ReportId, cached.Command, timeoutMs);
                if (reading != null)
                {
                    cached.LastSeenUtc = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(iface.Product)) cached.Product = iface.Product;
                    return new ProbeResult(reading, cached);
                }

                // The cached interface is still plugged in but did not answer; the
                // mouse is most likely asleep. Do not poke unrelated devices.
                return new ProbeResult(null, null);
            }
        }

        foreach (var iface in DiscoveryCandidates(interfaces))
        {
            var reading = QueryWithRetry(iface, DefaultReportId, DefaultCommand, timeoutMs);
            if (reading == null) continue;

            return new ProbeResult(reading, new CachedDevice
            {
                Vid = iface.Vid,
                Pid = iface.Pid,
                UsagePage = iface.UsagePage,
                Usage = iface.Usage,
                Product = iface.Product,
                ReportId = DefaultReportId,
                Command = DefaultCommand,
                LastSeenUtc = DateTime.UtcNow
            });
        }

        return new ProbeResult(null, null);
    }

    private static Reading? QueryWithRetry(HidInterface iface, byte reportId, ushort command, int timeoutMs)
    {
        // The first query after the mouse wakes up can be missed; retry once.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var reading = Query(iface, reportId, command, timeoutMs);
            if (reading != null) return reading;
            Thread.Sleep(120);
        }
        return null;
    }

    private static bool Matches(HidInterface iface, CachedDevice device) =>
        iface.Vid == device.Vid &&
        iface.Pid == device.Pid &&
        iface.UsagePage == device.UsagePage &&
        iface.Usage == device.Usage;

    /// <summary>
    /// Discovery only touches vendor-defined collections (usage page >= 0xFF00)
    /// that look like a MCHOSE device, so ordinary keyboards/mice and other
    /// vendors' vendor pages are left alone.
    /// </summary>
    private static IEnumerable<HidInterface> DiscoveryCandidates(List<HidInterface> interfaces)
    {
        return interfaces
            .Where(i => i.UsagePage >= 0xFF00)
            .Where(i => i.InputReportLength >= 64 && i.OutputReportLength >= 64)
            .Where(i => i.Vid == KnownVendorId || LooksLikeMchose(i.Product))
            .OrderByDescending(i => i.Vid == KnownVendorId)
            .ThenByDescending(i => i.Product.Contains("V7", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.Product.Contains("MCHOSE", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeMchose(string product)
    {
        if (string.IsNullOrWhiteSpace(product)) return false;
        return product.Contains("MCHOSE", StringComparison.OrdinalIgnoreCase)
            || product.Contains("V7", StringComparison.OrdinalIgnoreCase)
            || product.Contains("2.4G", StringComparison.OrdinalIgnoreCase);
    }

    private static Reading? Query(HidInterface iface, byte reportId, ushort command, int timeoutMs)
    {
        IntPtr writeHandle = IntPtr.Zero;
        IntPtr readHandle = IntPtr.Zero;
        IntPtr readEvent = IntPtr.Zero;

        try
        {
            writeHandle = Native.CreateFile(iface.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (writeHandle == Native.INVALID_HANDLE_VALUE) return null;

            readHandle = Native.CreateFile(iface.Path,
                Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (readHandle == Native.INVALID_HANDLE_VALUE) return null;

            readEvent = Native.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (readEvent == IntPtr.Zero) return null;

            DrainPending(readHandle, readEvent);

            if (!WriteDeviceInfoQuery(writeHandle, reportId, command)) return null;

            var deadline = Environment.TickCount64 + timeoutMs;
            var buffer = new byte[Math.Max((int)iface.InputReportLength, 64)];

            while (Environment.TickCount64 < deadline)
            {
                int remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
                int got = ReadReport(readHandle, readEvent, buffer, remaining, out bool timedOut);
                if (timedOut) break;
                if (got <= 0) break;

                if (got >= 21 &&
                    buffer[0] == reportId &&
                    buffer[1] == 0x01 &&
                    buffer[4] == (byte)(command & 0xFF) &&
                    buffer[5] == (byte)(command >> 8) &&
                    buffer[8] == (byte)(iface.Vid & 0xFF) &&
                    buffer[9] == (byte)(iface.Vid >> 8))
                {
                    int connectStatus = buffer[18];
                    int chargeStatus = buffer[19];
                    int percent = buffer[20];
                    if (percent > 100) percent = 100;
                    return new Reading(percent, connectStatus, chargeStatus, DateTime.Now, iface.Product);
                }
            }
            return null;
        }
        finally
        {
            if (readHandle != IntPtr.Zero && readHandle != Native.INVALID_HANDLE_VALUE)
                Native.CancelIoEx(readHandle, IntPtr.Zero);
            if (readEvent != IntPtr.Zero) Native.CloseHandle(readEvent);
            if (readHandle != IntPtr.Zero && readHandle != Native.INVALID_HANDLE_VALUE) Native.CloseHandle(readHandle);
            if (writeHandle != IntPtr.Zero && writeHandle != Native.INVALID_HANDLE_VALUE) Native.CloseHandle(writeHandle);
        }
    }

    private static bool WriteDeviceInfoQuery(IntPtr handle, byte reportId, ushort command)
    {
        var packet = new byte[64];
        packet[0] = reportId;
        packet[1] = 0x01;                 // flags: request with checksum
        packet[2] = 0x01;                 // business type
        packet[3] = 0x00;                 // payload length
        packet[4] = (byte)(command & 0xFF);
        packet[5] = (byte)(command >> 8);
        packet[6] = 0x00;                 // business code
        packet[7] = 0x00;                 // sequence
        packet[8] = XorSum(packet, 2, 7); // checksum

        return Native.WriteFile(handle, packet, packet.Length, out _, IntPtr.Zero);
    }

    private static byte XorSum(byte[] data, int from, int to)
    {
        byte value = 0;
        for (int i = from; i <= to; i++) value ^= data[i];
        return value;
    }

    private static void DrainPending(IntPtr readHandle, IntPtr readEvent)
    {
        var buffer = new byte[64];
        while (true)
        {
            int got = ReadReport(readHandle, readEvent, buffer, 0, out bool timedOut);
            if (timedOut || got <= 0) return;
        }
    }

    private static int ReadReport(IntPtr readHandle, IntPtr readEvent, byte[] buffer, int timeoutMs, out bool timedOut)
    {
        timedOut = false;
        Native.ResetEvent(readEvent);

        var overlapped = new Native.OVERLAPPED { hEvent = readEvent };
        bool ok = Native.ReadFile(readHandle, buffer, buffer.Length, IntPtr.Zero, ref overlapped);
        if (!ok)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != Native.ERROR_IO_PENDING) return -1;

            uint wait = Native.WaitForSingleObject(readEvent, (uint)timeoutMs);
            if (wait == Native.WAIT_TIMEOUT)
            {
                Native.CancelIoEx(readHandle, IntPtr.Zero);
                Native.WaitForSingleObject(readEvent, 200);
                timedOut = true;
                return -1;
            }
            if (wait != Native.WAIT_OBJECT_0) return -1;
        }

        if (!Native.GetOverlappedResult(readHandle, ref overlapped, out int transferred, false))
            return -1;
        return transferred;
    }

    private static List<HidInterface> Enumerate(bool openCaps)
    {
        var result = new List<HidInterface>();
        Native.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = Native.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == Native.INVALID_HANDLE_VALUE) return result;

        try
        {
            for (uint index = 0; ; index++)
            {
                var did = new Native.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>()
                };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref did)) break;

                Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, required, out _, IntPtr.Zero))
                        continue;

                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? "";
                    var iface = openCaps ? ReadCaps(path) : new HidInterface { Path = path };
                    if (iface != null) result.Add(iface);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static HidInterface? ReadCaps(string path)
    {
        IntPtr handle = Native.CreateFile(path, 0,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == Native.INVALID_HANDLE_VALUE) return null;

        try
        {
            var attributes = new Native.HIDD_ATTRIBUTES
            {
                Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>()
            };
            if (!Native.HidD_GetAttributes(handle, ref attributes)) return null;

            var info = new HidInterface
            {
                Path = path,
                Vid = attributes.VendorID,
                Pid = attributes.ProductID
            };

            var nameBuffer = new byte[512];
            if (Native.HidD_GetProductString(handle, nameBuffer, nameBuffer.Length))
                info.Product = Encoding.Unicode.GetString(nameBuffer).TrimEnd('\0');

            if (Native.HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                try
                {
                    var caps = new Native.HIDP_CAPS();
                    if (Native.HidP_GetCaps(preparsed, ref caps) == Native.HIDP_STATUS_SUCCESS)
                    {
                        info.UsagePage = caps.UsagePage;
                        info.Usage = caps.Usage;
                        info.InputReportLength = caps.InputReportByteLength;
                        info.OutputReportLength = caps.OutputReportByteLength;
                    }
                }
                finally
                {
                    Native.HidD_FreePreparsedData(preparsed);
                }
            }

            return info;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}
