using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using AudioMixer.Models;

namespace AudioMixer.Services;

/// <summary>
/// Owns the live <see cref="SerialPort"/> connection to the mixer, parsing the
/// newline-delimited JSON stream into strongly typed events. Raw frames are
/// buffered so partial reads never corrupt a packet.
/// </summary>
public sealed class SerialService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private SerialPort? _port;
    private readonly StringBuilder _buffer = new();
    private readonly object _lock = new();

    public event Action<MixerPacket>? PacketReceived;
    public event Action<DeviceIdentity>? IdentityReceived;
    public event Action<bool>? ConnectionChanged;

    public bool IsOpen => _port?.IsOpen ?? false;
    public string? PortName => _port?.PortName;

    /// <summary>Opens <paramref name="portName"/> at <paramref name="baudRate"/>. Returns an error message, or null on success.</summary>
    public string? Connect(string portName, int baudRate)
    {
        Disconnect();
        try
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                NewLine = "\n",
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true, // required so some boards (and the 16U2) start sending
            };
            _port.DataReceived += OnDataReceived;
            _port.Open();
            lock (_lock) _buffer.Clear();
            ConnectionChanged?.Invoke(true);

            // Ask the device to identify itself (also confirms it is really ours)
            // and to report every fader's current position right away.
            SendIdentifyQuery();
            RequestSync();
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] Connect('{portName}') failed: {ex.Message}");
            _port = null;
            ConnectionChanged?.Invoke(false);
            return ex.Message;
        }
    }

    public void Disconnect()
    {
        if (_port == null)
            return;

        try
        {
            _port.DataReceived -= OnDataReceived;
            if (_port.IsOpen)
                _port.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] Disconnect failed: {ex.Message}");
        }
        finally
        {
            _port.Dispose();
            _port = null;
            ConnectionChanged?.Invoke(false);
        }
    }

    public void SendIdentifyQuery()
    {
        try
        {
            _port?.Write("ID?\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] SendIdentifyQuery failed: {ex.Message}");
        }
    }

    /// <summary>Asks the device to resend every fader's current position.</summary>
    public void RequestSync()
    {
        try
        {
            _port?.Write("SYNC\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] RequestSync failed: {ex.Message}");
        }
    }

    /// <summary>Sets and persists the number of faders on the device (EEPROM).</summary>
    public void SetFaderCount(int count)
    {
        try
        {
            _port?.Write($"FADERS={count}\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] SetFaderCount failed: {ex.Message}");
        }
    }

    /// <summary>Sets and persists the device's display name on the device (EEPROM).</summary>
    public void SetDeviceName(string name)
    {
        try
        {
            _port?.Write($"NAME={name}\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] SetDeviceName failed: {ex.Message}");
        }
    }

    /// <summary>Sets and persists the idle-jitter (hysteresis) threshold on the device (EEPROM).</summary>
    public void SetHysteresis(int threshold)
    {
        try
        {
            _port?.Write($"HYST={threshold}\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SerialService] SetHysteresis failed: {ex.Message}");
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string chunk = _port!.ReadExisting();
            string[] lines;
            lock (_lock)
            {
                _buffer.Append(chunk);
                string all = _buffer.ToString();
                int lastNewline = all.LastIndexOf('\n');
                if (lastNewline < 0)
                    return;

                lines = all[..lastNewline].Split('\n');
                _buffer.Clear();
                _buffer.Append(all[(lastNewline + 1)..]);
            }

            foreach (string raw in lines)
                ParseLine(raw.Trim());
        }
        catch (Exception ex)
        {
            // A disconnect mid-read surfaces here, so drop the connection cleanly.
            Debug.WriteLine($"[SerialService] Read failed: {ex.Message}");
            Disconnect();
        }
    }

    private void ParseLine(string line)
    {
        if (line.Length == 0 || line[0] != '{')
            return;

        try
        {
            if (line.Contains("\"Device\"", StringComparison.OrdinalIgnoreCase))
            {
                var identity = JsonSerializer.Deserialize<DeviceIdentity>(line, JsonOptions);
                if (identity?.Device != null)
                    IdentityReceived?.Invoke(identity);
            }
            else if (line.Contains("\"Channel\"", StringComparison.OrdinalIgnoreCase))
            {
                var packet = JsonSerializer.Deserialize<MixerPacket>(line, JsonOptions);
                if (packet != null)
                    PacketReceived?.Invoke(packet);
            }
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[SerialService] Bad JSON '{line}': {ex.Message}");
        }
    }

    public void Dispose() => Disconnect();
}
