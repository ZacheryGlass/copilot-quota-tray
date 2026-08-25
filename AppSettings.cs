using System.Text.Json;

namespace WorkdayProgress;

internal sealed record AppSettings(
    bool WeekdaysOnly,
    bool ShowActualUsage = false)
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CopilotPace",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return Default();
            }

            string json = File.ReadAllText(SettingsPath);

            AppSettings? settings =
                JsonSerializer.Deserialize<AppSettings>(json);

            return settings ?? Default();
        }
        catch
        {
            return Default();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // The toggles still work for the current session.
        }
    }

    private static AppSettings Default()
    {
        return new AppSettings(
            WeekdaysOnly: true,
            ShowActualUsage: false);
    }
}
