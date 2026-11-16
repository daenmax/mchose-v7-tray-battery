using System.Runtime.InteropServices;
using System.Text;

namespace hidprobe;

internal static class Native
{
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x01;
    public const uint FILE_SHARE_WRITE = 0x02;
    public const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsagePage;
        public ushort LinkUsage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ushort[] RangeOrNot;
    }

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(byte reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort length, IntPtr ppData);

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES attrs);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr ppData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr ppData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr ppData, ref HIDP_CAPS caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr h, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetManufacturerString(IntPtr h, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(IntPtr h, byte[] buffer, int bufferLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr hFile, byte[] buffer, int nBytesToRead, out int nBytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr hFile, byte[] buffer, int nBytesToWrite, out int nBytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer,
        int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);
}

internal sealed class HidDeviceInfo
{
    public string Path;
    public ushort Vid;
    public ushort Pid;
    public ushort Version;
    public string Product = "";
    public string Manufacturer = "";
    public ushort UsagePage;
    public ushort Usage;
    public ushort InputReportLen;
    public ushort OutputReportLen;
    public ushort FeatureReportLen;
}

internal static class Program
{
    private const ushort TargetVid = 0x3837;

    private static List<HidDeviceInfo> Enumerate()
    {
        var result = new List<HidDeviceInfo>();
        Native.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = Native.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
            return result;

        try
        {
            uint index = 0;
            while (true)
            {
                var did = new Native.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>();
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref did))
                    break;
                index++;

                Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref did, detail, required, out _, IntPtr.Zero))
                        continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));

                    IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                        IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == new IntPtr(-1)) continue;
                    try
                    {
                        var attrs = new Native.HIDD_ATTRIBUTES();
                        attrs.Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>();
                        if (!Native.HidD_GetAttributes(h, ref attrs)) continue;

                        var info = new HidDeviceInfo
                        {
                            Path = path,
                            Vid = attrs.VendorID,
                            Pid = attrs.ProductID,
                            Version = attrs.VersionNumber
                        };

                        var buf = new byte[512];
                        if (Native.HidD_GetProductString(h, buf, buf.Length))
                            info.Product = Encoding.Unicode.GetString(buf).TrimEnd('\0');
                        if (Native.HidD_GetManufacturerString(h, buf, buf.Length))
                            info.Manufacturer = Encoding.Unicode.GetString(buf).TrimEnd('\0');

                        if (Native.HidD_GetPreparsedData(h, out IntPtr prep))
                        {
                            try
                            {
                                var caps = new Native.HIDP_CAPS();
                                if (Native.HidP_GetCaps(prep, ref caps) == 0x00110000)
                                {
                                    info.UsagePage = caps.UsagePage;
                                    info.Usage = caps.Usage;
                                    info.InputReportLen = caps.InputReportByteLength;
                                    info.OutputReportLen = caps.OutputReportByteLength;
                                    info.FeatureReportLen = caps.FeatureReportByteLength;
                                }
                            }
                            finally
                            {
                                Native.HidD_FreePreparsedData(prep);
                            }
                        }
                        result.Add(info);
                    }
                    finally
                    {
                        Native.CloseHandle(h);
                    }
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

    private static string Hex(byte[] d, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len; i++) sb.Append(d[i].ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    private static void PrintList(List<HidDeviceInfo> list, ushort? filterVid)
    {
        foreach (var d in list)
        {
            if (filterVid.HasValue && d.Vid != filterVid.Value) continue;
            Console.WriteLine($"VID={d.Vid:X4} PID={d.Pid:X4} MFR='{d.Manufacturer}' PRODUCT='{d.Product}'");
            Console.WriteLine($"   usagePage={d.UsagePage:X4} usage={d.Usage:X4} in={d.InputReportLen} out={d.OutputReportLen} feature={d.FeatureReportLen}");
            Console.WriteLine($"   {d.Path}");
        }
    }

    private static int Open(string path)
    {
        IntPtr h = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        return h == new IntPtr(-1) ? -1 : (int)h.ToInt64();
    }

    private static void Listen(HidDeviceInfo d, int seconds)
    {
        if (d.InputReportLen == 0)
        {
            Console.WriteLine($"  [skip] {d.Path}: no input reports");
            return;
        }
        IntPtr h = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == new IntPtr(-1))
        {
            Console.WriteLine($"  [fail] open {d.Path} err={Marshal.GetLastWin32Error()}");
            return;
        }
        var thread = new Thread(() =>
        {
            var buf = new byte[Math.Max((int)d.InputReportLen, 64)];
            var start = DateTime.Now;
            while (true)
            {
                if (!Native.ReadFile(h, buf, buf.Length, out int read, IntPtr.Zero))
                    break;
                if (read > 0)
                {
                    Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] +{(DateTime.Now - start).TotalSeconds:F1}s len={read}: {Hex(buf, read)}");
                }
            }
        }) { IsBackground = true };
        thread.Start();
        Thread.Sleep(seconds * 1000);
        Native.CloseHandle(h);
        Console.WriteLine($"  (listened {seconds}s on usagePage={d.UsagePage:X4} usage={d.Usage:X4})");
    }

    private static void SendFeatureAndRead(HidDeviceInfo d, byte reportId, byte[] payload, int readLen)
    {
        IntPtr h = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == new IntPtr(-1))
        {
            Console.WriteLine($"  [fail] open {d.Path} err={Marshal.GetLastWin32Error()}");
            return;
        }
        try
        {
            int len = d.FeatureReportLen > 0 ? d.FeatureReportLen : 65;
            var send = new byte[len];
            send[0] = reportId;
            Array.Copy(payload, 0, send, 1, Math.Min(payload.Length, len - 1));
            bool ok = Native.HidD_SetFeature(h, send, len);
            Console.WriteLine($"  SetFeature id=0x{reportId:X2} ok={ok} err={Marshal.GetLastWin32Error()}");

            var recv = new byte[len];
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                Thread.Sleep(120);
                Array.Clear(recv, 0, recv.Length);
                recv[0] = reportId;
                ok = Native.HidD_GetFeature(h, recv, len);
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"  GetFeature#{attempt} id=0x{reportId:X2} ok={ok} err={err} data: {Hex(recv, Math.Min(len, 32))}");
                if (ok) break;
            }
        }
        finally
        {
            Native.CloseHandle(h);
        }
    }

    public static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        var all = Enumerate();

        switch (mode)
        {
            case "list":
                PrintList(all, null);
                return 0;

            case "v7":
                PrintList(all, TargetVid);
                return 0;

            case "listen":
            {
                int secs = args.Length > 1 ? int.Parse(args[1]) : 15;
                ushort? page = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : null;
                foreach (var d in all.Where(x => x.Vid == TargetVid && x.InputReportLen > 0 &&
                                                 (!page.HasValue || x.UsagePage == page.Value)))
                {
                    Console.WriteLine($"--- listening {d.Product} pid={d.Pid:X4} usagePage={d.UsagePage:X4} usage={d.Usage:X4}");
                    Listen(d, secs);
                }
                return 0;
            }

            case "setfeature":
            {
                // setfeature <usagePage hex> <reportId hex> <payload hex...>
                ushort up = Convert.ToUInt16(args[1], 16);
                byte rid = Convert.ToByte(args[2], 16);
                var payload = args.Skip(3).Select(x => Convert.ToByte(x, 16)).ToArray();
                foreach (var d in all.Where(x => x.Vid == TargetVid && x.UsagePage == up))
                {
                    Console.WriteLine($"--- {d.Product} pid={d.Pid:X4} usagePage={d.UsagePage:X4} featureLen={d.FeatureReportLen}");
                    SendFeatureAndRead(d, rid, payload, 0);
                }
                return 0;
            }

            case "sim":
            {
                // sim <usagePage> <reportId> <secs> <payload bytes...>
                ushort up = Convert.ToUInt16(args[1], 16);
                byte rid = Convert.ToByte(args[2], 16);
                int secs = int.Parse(args[3]);
                var payload = args.Skip(4).Select(x => Convert.ToByte(x, 16)).ToArray();
                foreach (var d in all.Where(x => x.Vid == TargetVid && x.UsagePage == up))
                {
                    Console.WriteLine($"--- {d.Product} pid={d.Pid:X4} usagePage={d.UsagePage:X4} in={d.InputReportLen} out={d.OutputReportLen}");
                    IntPtr hRead = Native.CreateFile(d.Path, Native.GENERIC_READ,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    IntPtr hWrite = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (hRead == new IntPtr(-1) || hWrite == new IntPtr(-1))
                    { Console.WriteLine($"open fail {Marshal.GetLastWin32Error()}"); continue; }

                    var thread = new Thread(() =>
                    {
                        var buf = new byte[Math.Max((int)d.InputReportLen, 64)];
                        while (true)
                        {
                            if (!Native.ReadFile(hRead, buf, buf.Length, out int read, IntPtr.Zero)) break;
                            if (read > 0)
                            {
                                Console.WriteLine($"  IN len={read}: {Hex(buf, read)}");
                                Console.Out.Flush();
                            }
                        }
                    }) { IsBackground = true };
                    thread.Start();
                    Thread.Sleep(200);

                    int outLen = d.OutputReportLen > 0 ? d.OutputReportLen : 65;
                    var pkt = new byte[outLen];
                    pkt[0] = rid;
                    Array.Copy(payload, 0, pkt, 1, Math.Min(payload.Length, outLen - 1));
                    bool ok = Native.WriteFile(hWrite, pkt, outLen, out int written, IntPtr.Zero);
                    Console.WriteLine($"  WriteFile rid=0x{rid:X2} ok={ok} written={written} err={Marshal.GetLastWin32Error()}");
                    Console.Out.Flush();
                    Thread.Sleep(secs * 1000);
                    Console.Out.Flush();
                    Environment.Exit(0);
                }
                return 0;
            }

            case "sl":
            {
                // sl <usagePage> <reportId> <secs> <payload hex...>
                ushort up = Convert.ToUInt16(args[1], 16);
                byte rid = Convert.ToByte(args[2], 16);
                int secs = int.Parse(args[3]);
                var payload = args.Skip(4).Select(x => Convert.ToByte(x, 16)).ToArray();
                foreach (var d in all.Where(x => x.Vid == TargetVid && x.UsagePage == up))
                {
                    Console.WriteLine($"--- {d.Product} pid={d.Pid:X4} usagePage={d.UsagePage:X4} in={d.InputReportLen} feature={d.FeatureReportLen}");
                    IntPtr hRead = Native.CreateFile(d.Path, Native.GENERIC_READ,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    IntPtr hWrite = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (hRead == new IntPtr(-1) || hWrite == new IntPtr(-1)) { Console.WriteLine($"open fail {Marshal.GetLastWin32Error()}"); continue; }
                    var thread = new Thread(() =>
                    {
                        var buf = new byte[Math.Max((int)d.InputReportLen, 64)];
                        var start = DateTime.Now;
                        while (true)
                        {
                            if (!Native.ReadFile(hRead, buf, buf.Length, out int read, IntPtr.Zero)) break;
                            if (read > 0)
                            {
                                Console.WriteLine($"  IN [{DateTime.Now:HH:mm:ss.fff}] +{(DateTime.Now - start).TotalMilliseconds:F0}ms len={read}: {Hex(buf, read)}");
                                Console.Out.Flush();
                            }
                        }
                    }) { IsBackground = true };
                    thread.Start();
                    Thread.Sleep(200);
                    int len = d.FeatureReportLen > 0 ? d.FeatureReportLen : 65;
                    var send = new byte[len];
                    send[0] = rid;
                    Array.Copy(payload, 0, send, 1, Math.Min(payload.Length, len - 1));
                    bool ok = Native.HidD_SetFeature(hWrite, send, len);
                    Console.WriteLine($"  SetFeature rid=0x{rid:X2} ok={ok} err={Marshal.GetLastWin32Error()}");
                    Console.Out.Flush();
                    Thread.Sleep(secs * 1000);
                    Console.Out.Flush();
                    Environment.Exit(0);
                }
                return 0;
            }

            case "getall":
            {
                foreach (var d in all.Where(x => x.Vid == TargetVid && x.UsagePage == 0xFF01))
                {
                    IntPtr h = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == new IntPtr(-1)) { Console.WriteLine($"open fail {Marshal.GetLastWin32Error()}"); continue; }
                    try
                    {
                        for (int rid = 0; rid <= 32; rid++)
                        {
                            foreach (int len in new[] { (int)d.FeatureReportLen, (int)d.FeatureReportLen + 1 })
                            {
                                var buf = new byte[len];
                                buf[0] = (byte)rid;
                                bool ok = Native.HidD_GetFeature(h, buf, len);
                                int err = Marshal.GetLastWin32Error();
                                if (ok) Console.WriteLine($"  GetFeature rid=0x{rid:X2} len={len} OK: {Hex(buf, Math.Min(len, 32))}");
                                else if (err != 87) Console.WriteLine($"  GetFeature rid=0x{rid:X2} len={len} fail err={err}");
                            }
                        }
                    }
                    finally { Native.CloseHandle(h); }
                }
                return 0;
            }

            case "fcaps":
            {
                ushort fpage = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0xFF01;
                foreach (var d in all.Where(x => x.Vid == TargetVid && (fpage == 0 || x.UsagePage == fpage)))
                {
                    IntPtr h = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == new IntPtr(-1)) { Console.WriteLine($"open fail {Marshal.GetLastWin32Error()}"); continue; }
                    try
                    {
                        if (!Native.HidD_GetPreparsedData(h, out IntPtr prep)) { Console.WriteLine("preparsed fail"); continue; }
                        try
                        {
                            foreach (var (name, type) in new[] { ("Input", (byte)0), ("Output", (byte)1), ("Feature", (byte)2) })
                            {
                                var caps = new Native.HIDP_VALUE_CAPS[128];
                                ushort len = (ushort)caps.Length;
                                int st = Native.HidP_GetValueCaps(type, caps, ref len, prep);
                                Console.WriteLine($"--- {name} valueCaps status=0x{st:X8} count={len}");
                                for (int i = 0; i < len; i++)
                                {
                                    var c = caps[i];
                                    Console.WriteLine($"    usagePage={c.UsagePage:X4} rid=0x{c.ReportID:X2} bitSize={c.BitSize} count={c.ReportCount} isRange={c.IsRange} usageMin={c.RangeOrNot[0]:X4} usageMax={c.RangeOrNot[1]:X4}");
                                }
                            }
                        }
                        finally { Native.HidD_FreePreparsedData(prep); }
                    }
                    finally { Native.CloseHandle(h); }
                }
                return 0;
            }

            case "desc":
            {
                ushort up = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0xFF01;
                foreach (var d in all.Where(x => x.Vid == TargetVid && x.UsagePage == up))
                {
                    IntPtr h = Native.CreateFile(d.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == new IntPtr(-1)) { Console.WriteLine($"open fail {Marshal.GetLastWin32Error()}"); continue; }
                    try
                    {
                        var buf = new byte[4096];
                        // IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0190
                        bool ok = Native.DeviceIoControl(h, 0x000B0190, IntPtr.Zero, 0, buf, buf.Length, out int got, IntPtr.Zero);
                        Console.WriteLine($"--- descriptor pid={d.Pid:X4} usagePage={d.UsagePage:X4} ok={ok} bytes={got} err={Marshal.GetLastWin32Error()}");
                        if (ok) Console.WriteLine(Hex(buf, got));
                    }
                    finally { Native.CloseHandle(h); }
                }
                return 0;
            }

            default:
                Console.WriteLine("usage: hidprobe [list|v7|listen <secs>|setfeature <usagePageHex> <reportIdHex> <payload...>]");
                return 1;
        }
    }
}
