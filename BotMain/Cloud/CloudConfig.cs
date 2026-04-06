using System;
using System.IO;
using System.Text.Json;

namespace BotMain.Cloud
{
    public class CloudConfig
    {
        public string ServerUrl { get; set; } = "http://70.39.201.9:5000";
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
            // 用机器名作为固定 DeviceId，重装/重部署不会产生重复设备
            var stableId = Environment.MachineName;
            if (DeviceId != stableId)
            {
                DeviceId = stableId;
                if (string.IsNullOrEmpty(DisplayName))
                    DisplayName = stableId;
                Save();
            }
        }
    }
}
