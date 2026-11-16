using System.Text.Json;

namespace MchoseBattery;

internal sealed class AppSettings
{
    public int PollSeconds { get; set; } = 30;
    public bool StartWithWindows { get; set; }
    public List<CachedDevice> KnownDevices { get; set; } = new();

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MchoseBattery");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    settings.PollSeconds = Math.Clamp(settings.PollSeconds, 5, 3600);
                    return settings;
                }
            }
        }
        catch
        {
            // Corrupt settings are not fatal; fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Ignore write failures (read-only profile etc.).
        }
    }
}
