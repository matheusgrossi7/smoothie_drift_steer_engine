namespace DriftCore.Models;

/// <summary>
/// Describes a physical input device detected by the engine.
/// </summary>
public sealed class DeviceInfo
{
    /// <summary>
    /// Friendly name for UI display (e.g., "Xbox Controller 1", "Keyboard").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The device type (e.g., Gamepad0, Keyboard).
    /// </summary>
    public InputDeviceType Type { get; set; } = InputDeviceType.Gamepad0;

    /// <summary>
    /// Indicates whether the device is currently connected.
    /// </summary>
    public bool IsConnected { get; set; }
}
