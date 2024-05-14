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
        //Load the stored settings
        List<string> Applications = Config.Applications;

        //Selected Sessions
        Dictionary<int, AudioSessionControl> SelectedSessions = new Dictionary<int, AudioSessionControl>();
        //Unbound Sessions
        HashSet<AudioSessionControl> UnboundSessions = new HashSet<AudioSessionControl>();
        //Channels and their corresponding volume levels
        Dictionary<int, float> Channels = new Dictionary<int, float>();
        //Stores the textboxes
        List<TextBox> AppNames = null;

        static MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
        static MMDevice device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        static AudioSessionManager audioSessionManager = device.AudioSessionManager;

        public Form1()
        {
            InitializeChannels(Config.Applications.Count > 5 ? 5 : Config.Applications.Count);
            InitializeComponent();
    
            ScanProcessIDs(audioSessionManager, Applications);
            Trace.WriteLine($"{Config.COMPort} | {Config.BaudRate}");
            Config.Applications.ForEach(app => Trace.Write(app.ToString() + " | "));

            try
            {
                Serial.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

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
            audioSessionManager.OnSessionCreated += AudioSessionManager_OnSessionCreated;
        }

        private void AudioSessionManager_OnSessionCreated(object sender, IAudioSessionControl newSession)
        {
            audioSessionManager.RefreshSessions();
            newSession.GetDisplayName(out string displayName);
            Invoke(new Action(() => ScanProcessIDs(audioSessionManager, Applications)));
        }

        private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
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

                try
                {
                    //Adjust the master volume
                    if (Applications[Data.Channel] == "*")
                    {
                        //Invoke required for master volume adjust
                        Invoke(new Action(() =>
                        {
                            //Check if the received percentage data is the same
                            if (device?.AudioEndpointVolume.MasterVolumeLevelScalar != Channels[Data.Channel] / 100.0f)
                            {
                                device.AudioEndpointVolume.MasterVolumeLevelScalar = Channels[Data.Channel] / 100.0f;
                            }

                        }));
                    }
                    //Adjust volume for all unbound sessions
                    else if (Applications[Data.Channel] == "-")
                    {
                        //Adjust the volume for each unbound session
                        foreach (var session in UnboundSessions)
                        {
                            session.SimpleAudioVolume.Volume = Channels[Data.Channel] / 100.0f;
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
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                }
            }
        }

        /// <summary>
        /// Scans all sessions int the AudioSessionManager and adds the Applications to their corresponding Lists.
        /// </summary>
        /// <param name="Manager">The AudioSessionManager</param>
        /// <param name="Applications">The list of selected application</param>
        private void ScanProcessIDs(AudioSessionManager Manager, List<string> Applications)
        {
            Sessions_List.Items.Clear();
            for (int i = 0; i < Manager.Sessions.Count; i++)
            {
                var session = Manager.Sessions[i];
                int ProcessID = (int)session.GetProcessID;
                string ProcessName = Process.GetProcessById(ProcessID).ProcessName;
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

        private void SaveConfig_Click(object sender, EventArgs e)
        {
            //Check if the baudrate enter is a valid integer
            if(!int.TryParse(Baud_Rate_Tbx.Text, out int BaudRate))
            {
                MessageBox.Show("The baud rate value entered is not valid.", "Configuration", MessageBoxButtons.OK);
                Baud_Rate_Tbx.Text = Config.BaudRate.ToString();
                return;
            }

            try
            {
                //Save the text from each of the text boxes into the config object
                for (int i = 0; i < Config.Applications.Count; i++)
                {
                    Config.Applications[i] = AppNames[i].Text;
                }
                //Save the BaudRate and COM Port entered
                Config.BaudRate = BaudRate;
                Config.COMPort = COM_Port_Tbx.Text;

                //Save the object into the configuration file as JSON
                ConfigManager.SaveConfig(Config, out string Message);
                //Display a message box with the message.
                MessageBox.Show(Message, "Configuration", MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }
    }
}