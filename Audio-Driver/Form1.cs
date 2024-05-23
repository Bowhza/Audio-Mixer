using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using NAudio.CoreAudioApi.Interfaces;

namespace Audio_Driver
{
    public partial class Form1 : Form
    {
        //Volume Slider Configuration
        static AppConfig Config = ConfigManager.LoadConfig();
        //Initialize the serial port with the config settings
        SerialPort Serial = new SerialPort(Config.COMPort, Config.BaudRate, Parity.None, 8, StopBits.One);
        //Selected Sessions
        Dictionary<int, AudioSessionControl> SelectedSessions = new Dictionary<int, AudioSessionControl>();
        //Unbound Sessions
        HashSet<AudioSessionControl> UnboundSessions = new HashSet<AudioSessionControl>();
        //Channels and their corresponding volume levels
        Dictionary<int, float> Channels = new Dictionary<int, float>();
        //Stores the textboxes
        List<TextBox> AppNames = null;

        static MMDeviceEnumerator DeviceEnumerator = new MMDeviceEnumerator();
        static MMDevice Device = DeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        static AudioSessionManager AudioSessionManager = Device.AudioSessionManager;

        public Form1()
        {
            InitializeChannels(Config.Applications.Count > 5 ? 5 : Config.Applications.Count);
            InitializeComponent();
            InitializeNotifyIcon();
            InitializeContextMenu();

            ScanProcessIDs(AudioSessionManager, Config.Applications);
            Config.Applications.ForEach(app => Trace.Write(app.ToString() + " | "));

            //Store the Slider Text boxes in a list for easy access
            AppNames = new List<TextBox>
            {
                Slider_1_Tbx,
                Slider_2_Tbx,
                Slider_3_Tbx,
                Slider_4_Tbx,
                Slider_5_Tbx
            };

            //Load config values into the UI textboxes
            for (int i = 0; i < Config.Applications.Count; i++)
            {
                AppNames[i].Text = Config.Applications[i];
            }
            Baud_Rate_Tbx.Text = Config.BaudRate.ToString();
            COM_Port_Tbx.Text = Config.COMPort;

            //Event listeners for serial data and audio sessions
            Serial.DataReceived += Serial_DataReceived;
            AudioSessionManager.OnSessionCreated += AudioSessionManager_OnSessionCreated;

            try
            {
                Serial.Open();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
                MessageBox.Show($"Failed to open COM port: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string SerialData = Serial.ReadLine();

                //Check if its the start of the JSON string
                if (SerialData?[0] == '{')
                {
                    //Deserialize the string into an object
                    SerialData Data = JsonConvert.DeserializeObject<SerialData>(SerialData);
                    //Set the channel to its corresponding percentage
                    Channels[Data.Channel] = Data.Percentage;
                    //Debug string display
                    Trace.WriteLine($"{Data.Channel} | {Data.Voltage}V | {Data.Percentage}%");

                    //Adjust the master volume
                    if (Config.Applications[Data.Channel] == "*")
                    {
                        //Invoke required for master volume adjust
                        Invoke(new Action(() =>
                        {
                            //Check if the received percentage data is the same
                            if (Device?.AudioEndpointVolume.MasterVolumeLevelScalar != Channels[Data.Channel] / 100.0f)
                            {
                                Device.AudioEndpointVolume.MasterVolumeLevelScalar = Channels[Data.Channel] / 100.0f;
                            }

                        }));
                    }

                    //Adjust volume for all unbound sessions
                    else if (Config.Applications[Data.Channel] == "-")
                    {
                        //Adjust the volume for each unbound session
                        foreach (var session in UnboundSessions)
                        {
                            session.SimpleAudioVolume.Volume = Channels[Data.Channel] / 100.0f;
                        }
                    }

                    //Adjusts the volume for the foreground window
                    else if (Config.Applications[Data.Channel] == "!")
                    {
                        Process ForegroundProcess = ActiveProcess.GetActiveProcess();
                        //Get the control for the foreground process, returns null if not found
                        AudioSessionControl Control = GetControlById(ForegroundProcess.Id, ForegroundProcess.ProcessName);
                        //Adjust the volume if the control is not null
                        if (Control != null)
                        {
                            Control.SimpleAudioVolume.Volume = Channels[Data.Channel] / 100.0f;
                        }
                    }

                    //Adjust the volume for all selected sessions
                    foreach (var Control in SelectedSessions)
                    {
                        if (Control.Value.SimpleAudioVolume.Volume != Channels[Control.Key] / 100.0f)
                        {
                            Control.Value.SimpleAudioVolume.Volume = Channels[Control.Key] / 100.0f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AudioSessionManager_OnSessionCreated(object sender, IAudioSessionControl newSession)
        {
            AudioSessionManager.RefreshSessions();
            newSession.GetDisplayName(out string displayName);
            Invoke(new Action(() => ScanProcessIDs(AudioSessionManager, Config.Applications)));
        }

        private void SaveConfig_Click(object sender, EventArgs e)
        {
            // Check if the baud rate entered is a valid integer
            if (!int.TryParse(Baud_Rate_Tbx.Text, out int baudRate))
            {
                MessageBox.Show("The baud rate value entered is not valid.", "Configuration", MessageBoxButtons.OK);
                Baud_Rate_Tbx.Text = Config.BaudRate.ToString();
                return;
            }

            else if(!COM_Port_Tbx.Text.StartsWith("COM"))
            {
                MessageBox.Show("The COM Port value entered is not valid.", "Configuration", MessageBoxButtons.OK);
                COM_Port_Tbx.Text = Config.COMPort;
                return;
            }

            try
            {
                //Save the text from each of the text boxes into the config object
                for (int i = 0; i < Config.Applications.Count; i++)
                {
                    Config.Applications[i] = AppNames[i].Text;
                }

                //Check if BaudRate or COMPort has changed
                if (baudRate != Config.BaudRate || COM_Port_Tbx.Text != Config.COMPort)
                {
                    //Close the existing serial port if open
                    if (Serial.IsOpen)
                    {
                        Serial.Close();
                    }

                    // Update the Config object
                    Config.BaudRate = baudRate;
                    Config.COMPort = COM_Port_Tbx.Text;

                    Serial.PortName = Config.COMPort;
                    Serial.BaudRate = baudRate;

                    //Open the new serial port
                    try
                    {
                        Serial.Open();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open COM port: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Trace.WriteLine(ex.ToString());
                        return;
                    }
                }
                //Save the object into the configuration file as JSON
                ConfigManager.SaveConfig(Config, out string message);
                //Rescan process IDs with the updated config
                ScanProcessIDs(AudioSessionManager, Config.Applications);
                //Display a message box with the save message
                MessageBox.Show(message, "Configuration", MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Scans all sessions int the AudioSessionManager and adds the Applications to their corresponding Lists.
        /// </summary>
        /// <param name="Manager">The AudioSessionManager</param>
        /// <param name="Applications">The list of selected application</param>
        private void ScanProcessIDs(AudioSessionManager Manager, List<string> Applications)
        {
            //Clear all Collections
            Sessions_List.Items.Clear();
            SelectedSessions.Clear();
            UnboundSessions.Clear();

            for (int i = 0; i < Manager.Sessions.Count; i++)
            {
                var session = Manager.Sessions[i];
                int ProcessID = (int)session.GetProcessID;
                string ProcessName = Process.GetProcessById(ProcessID).ProcessName;
                Trace.WriteLine($"{ProcessID} | {ProcessName}");
                Sessions_List.Items.Add(ProcessName);

                if (Applications.Contains(ProcessName))
                {
                    SelectedSessions[Applications.IndexOf(ProcessName)] = session;
                }
                else
                {
                    UnboundSessions.Add(session);
                }
            }
        }

        /// <summary>
        /// Attempts to find and return the AudioSessionControl for a specified process.
        /// </summary>
        /// <param name="ProcessID">Process ID</param>
        /// <param name="ProcessName">Process Name</param>
        /// <returns>AudioSessionControl object or Null if not found.</returns>
        private AudioSessionControl GetControlById(int ProcessID, string ProcessName)
        {
            for (int i = 0; i < AudioSessionManager.Sessions.Count; i++)
            {
                var session = AudioSessionManager.Sessions[i];
                // Skip system sounds
                if (session.GetProcessID == 0) continue;
                var process = Process.GetProcessById((int)session.GetProcessID);
                if (process.ProcessName.Equals(ProcessName) || process.Id == ProcessID)
                {
                    return session;
                }
            }
            return null;
        }

        /// <summary>
        /// Initializes the Channels Dictionary
        /// </summary>
        /// <param name="Count">Channel CouAppnt</param>
        private void InitializeChannels(int Count)
        {
            for (int i = 0; i < Count; i++)
            {
                Channels.Add(i, 100.0f);
            }
        }
    }
}