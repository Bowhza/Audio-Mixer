using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Audio_Driver
{
    /// <summary>
    /// Manages interactions with the configuration file.
    /// </summary>
    internal static class ConfigManager
    {
        private const string ConfigFilePath = "config.json";

        /// <summary>
        /// Saves the modified config to the config file
        /// </summary>
        /// <param name="Config">App Config Object</param>
        public static bool SaveConfig(AppConfig Config, out string Message)
        {
            try
            {
                string JSONData = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, JSONData);
                Message = "Successfully saved the configuration.";
                return true;
            }
            catch (Exception ex)
            {
                Message = $"Error saving config: {ex.Message}";
                Trace.WriteLine($"Error saving config: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Loads the configuration file JSON into and AppConfig object.
        /// </summary>
        /// <returns>App Configuration Object</returns>
        public static AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    return JsonConvert.DeserializeObject<AppConfig>(json);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error loading config: {ex.Message}");
            }

            // Return default config if file doesn't exist or there's an error
            return new AppConfig { BaudRate = 9600, COMPort = "COM3", Applications = new List<string>() };
        }
    }
}
