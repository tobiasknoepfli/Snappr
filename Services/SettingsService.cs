using System;
using System.IO;
using System.Text.Json;

namespace Snappr.Services
{
    public class AppSettings
    {
        public string LastSourceFolder { get; set; } = string.Empty;
        public System.Collections.Generic.HashSet<string> SensitiveImagePaths { get; set; } = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Snappr",
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(settings);
                File.WriteAllText(SettingsPath, json);

            }
            catch { }
        }
    }
}
