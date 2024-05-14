using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio_Driver
{
    internal static class ConfigManager
    {
        private const string ConfigFilePath = "config.json";

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
                Console.WriteLine($"Error loading config: {ex.Message}");
            }

            // Return default config if file doesn't exist or there's an error
            return new AppConfig { BaudRate = 9600, COMPort = "COM3", Applications = new List<string>() };
        }
    }
}
