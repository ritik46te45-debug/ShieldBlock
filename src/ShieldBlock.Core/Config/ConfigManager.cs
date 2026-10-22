using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Config
{
    public class ConfigManager
    {
        private readonly string _serviceSettingsPath;
        private readonly string _userSettingsPath;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ConfigManager(string? servicePath = null, string? userPath = null)
        {
            _serviceSettingsPath = servicePath ?? Constants.ServiceSettingsPath;
            _userSettingsPath = userPath ?? Constants.UserSettingsPath;

            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            var sDir = Path.GetDirectoryName(_serviceSettingsPath);
            if (!string.IsNullOrEmpty(sDir) && !Directory.Exists(sDir))
            {
                Directory.CreateDirectory(sDir);
            }

            var uDir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(uDir) && !Directory.Exists(uDir))
            {
                Directory.CreateDirectory(uDir);
            }
        }

        public ServiceSettings LoadServiceSettings()
        {
            try
            {
                if (File.Exists(_serviceSettingsPath))
                {
                    var json = File.ReadAllText(_serviceSettingsPath, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<ServiceSettings>(json, JsonOptions);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Error loading service settings: {ex.Message}");
            }

            var defaults = new ServiceSettings();
            SaveServiceSettings(defaults);
            return defaults;
        }

        public void SaveServiceSettings(ServiceSettings settings)
        {
            try
            {
                EnsureDirectories();
                settings.LastUpdatedUtc = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var tempFile = _serviceSettingsPath + ".tmp";
                File.WriteAllText(tempFile, json, Encoding.UTF8);
                File.Move(tempFile, _serviceSettingsPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Error saving service settings: {ex.Message}");
            }
        }

        public UserSettings LoadUserSettings()
        {
            try
            {
                if (File.Exists(_userSettingsPath))
                {
                    var json = File.ReadAllText(_userSettingsPath, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Error loading user settings: {ex.Message}");
            }

            var defaults = new UserSettings();
            SaveUserSettings(defaults);
            return defaults;
        }

        public void SaveUserSettings(UserSettings settings)
        {
            try
            {
                EnsureDirectories();
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var tempFile = _userSettingsPath + ".tmp";
                File.WriteAllText(tempFile, json, Encoding.UTF8);
                File.Move(tempFile, _userSettingsPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] Error saving user settings: {ex.Message}");
            }
        }
    }
}
