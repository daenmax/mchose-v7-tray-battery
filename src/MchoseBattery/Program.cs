using System.Text;
using System.Diagnostics;

namespace MchoseBattery;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--dump-icon")
        {
            BatteryIconRenderer.DumpPreview(args[1], args.Length >= 3 ? int.Parse(args[2]) : 75);
            return;
        }

        if (args.Length >= 1 && args[0] == "--probe")
        {
            string output = args.Length >= 2 ? args[1] : "probe-result.txt";
            var report = new StringBuilder();
            try
            {
                var settings = AppSettings.Load();
                bool force = args.Contains("--force");
                var stopwatch = Stopwatch.StartNew();
                var result = HidBatteryReader.Probe(settings.KnownDevices, forceDiscovery: force);
                stopwatch.Stop();
                report.AppendLine($"elapsed: {stopwatch.ElapsedMilliseconds} ms (cache={settings.KnownDevices.Count}, force={force})");
                if (result.Reading == null)
                {
                    report.AppendLine("no reading");
                    report.AppendLine("discovered device: " + (result.Device == null ? "none" : Describe(result.Device)));
                }
                else
                {
                    report.AppendLine("reading: " + result.Reading.Percent + "% connect=" + result.Reading.ConnectStatus
                        + " charge=" + result.Reading.ChargeStatus + " product=" + result.Reading.Product);
                    report.AppendLine("discovered device: " + (result.Device == null ? "none" : Describe(result.Device)));
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("EXCEPTION: " + ex);
            }
            File.WriteAllText(output, report.ToString());
            return;
        }

        using var mutex = new Mutex(true, @"Local\MchoseBatteryV7_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }

    private static string Describe(CachedDevice device) =>
        $"vid={device.Vid:X4} pid={device.Pid:X4} usagePage={device.UsagePage:X4} usage={device.Usage:X4} "
        + $"reportId=0x{device.ReportId:X2} cmd=0x{device.Command:X4} product='{device.Product}'";
}
