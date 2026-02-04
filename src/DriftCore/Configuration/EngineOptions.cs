namespace DriftCore.Configuration;

/// <summary>
/// Engine configuration (appsettings.json).
/// Contains only user-tunable parameters.
/// </summary>
public sealed class EngineOptions
{
    /// <summary>
    /// XInput gamepad index (0-3).
    /// </summary>
    public int InputDeviceIndex { get; set; } = 0;

    /// <summary>
    /// vJoy device ID (1-16).
    /// </summary>
    public int VJoyDeviceId { get; set; } = 1;

    /// <summary>
    /// If true, LB becomes a digital clutch. Otherwise, uses the right stick Y axis.
    /// </summary>
    public bool UseLbAsClutch { get; set; } = false;

    /// <summary>
    /// Enables input smoothing.
    /// </summary>
    public bool SmoothingEnabled { get; set; } = true;

    /// <summary>
    /// Smoothing strength (0-100).
    /// </summary>
    public int SmoothingValue { get; set; } = 50;

    /// <summary>
    /// If true, reads the Xbox 360 Wireless Receiver via WinUSB (WinUSBNet) instead of XInput.
    /// Requires the device to be bound to WinUSB and a Device Interface GUID to be configured.
    /// </summary>
    public bool UseWinUsbReceiver { get; set; } = false;

    /// <summary>
    /// WinUSB Device Interface GUID (defined in the WinUSB .inf) used to enumerate the receiver.
    /// Example: "{BB9176E8-924F-4A7E-963A-6DC6A4E87FC2}".
    /// If empty, falls back to the generic USB device interface GUID and filters by VID/PID.
    /// </summary>
    public string WinUsbDeviceInterfaceGuid { get; set; } = "";

    /// <summary>
    /// Pipe read timeout (ms). 0 = no timeout.
    /// </summary>
    public int WinUsbReadTimeoutMs { get; set; } = 20;
}
