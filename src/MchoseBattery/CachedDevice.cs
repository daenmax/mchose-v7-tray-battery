using System.Text.Json.Serialization;

namespace MchoseBattery;

/// <summary>
/// Identity of a device that answered the MCHOSE MC protocol. Persisted so the
/// next launch can go straight to the right HID interface instead of probing.
/// </summary>
internal sealed class CachedDevice
{
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public ushort UsagePage { get; set; }
    public ushort Usage { get; set; }
    public string Product { get; set; } = "";
    public byte ReportId { get; set; } = 0x4D;
    public ushort Command { get; set; } = 0x0900;
    public DateTime LastSeenUtc { get; set; }

    /// <summary>
    /// VID/PID/usage page are stable across replugs; the device path is not
    /// (it changes with the USB port), so it is deliberately not part of the key.
    /// </summary>
    public bool Matches(CachedDevice other) =>
        Vid == other.Vid &&
        Pid == other.Pid &&
        UsagePage == other.UsagePage &&
        Usage == other.Usage;

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Product) ? $"设备 {Vid:X4}:{Pid:X4}" : Product;
}
