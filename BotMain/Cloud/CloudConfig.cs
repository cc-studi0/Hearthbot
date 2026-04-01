using System;
using System.IO;
using System.Text.Json;

namespace BotMain.Cloud
{
    public class CloudConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DeviceToken { get; set; } = string.Empty;

        public bool IsEnabled => !string.IsNullOrWhiteSpace(ServerUrl);

        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloud.json");

        public static CloudConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<CloudConfig>(File.ReadAllText(ConfigPath))
                           ?? new CloudConfig();
            }
            catch { }
            return new CloudConfig();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        public void EnsureDeviceId()
        {
            if (!string.IsNullOrEmpty(DeviceId)) return;

            DeviceId = $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}";
            if (string.IsNullOrEmpty(DisplayName))
                DisplayName = Environment.MachineName;
            Save();
        }
    }
}
