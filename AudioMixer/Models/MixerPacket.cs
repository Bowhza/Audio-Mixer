using System.Text.Json.Serialization;

namespace AudioMixer.Models;

/// <summary>
/// A single fader reading streamed by the firmware, e.g.
/// <c>{"Channel":0,"Voltage":2.50,"Percentage":50.0}</c>.
/// </summary>
public class MixerPacket
{
    public int Channel { get; set; }
    public float Voltage { get; set; }
    public float Percentage { get; set; }
}

/// <summary>
/// The handshake reply sent in response to <c>ID?</c>, e.g.
/// <c>{"Device":"AudioMixer","Protocol":1,"Faders":5,"Firmware":"2.0.0"}</c>.
/// </summary>
public class DeviceIdentity
{
    /// <summary>User-editable friendly name (shown in the UI).</summary>
    public string? Device { get; set; }

    /// <summary>Constant product signature used for auto-detection (never renamed).</summary>
    public string? Signature { get; set; }

    public int Protocol { get; set; }
    public int Faders { get; set; }

    /// <summary>Idle-jitter filter threshold in ADC counts.</summary>
    public int Hysteresis { get; set; }

    public string? Firmware { get; set; }
}
