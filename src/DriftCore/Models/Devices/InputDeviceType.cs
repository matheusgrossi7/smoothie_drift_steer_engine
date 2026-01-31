using System.Text.Json.Serialization;

namespace DriftCore.Models;

/// <summary>
/// Identifies supported input sources and XInput slots.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputDeviceType
{
    /// <summary>
    /// XInput controller at index 0.
    /// </summary>
    Gamepad0,

    /// <summary>
    /// XInput controller at index 1.
    /// </summary>
    Gamepad1,

    /// <summary>
    /// XInput controller at index 2.
    /// </summary>
    Gamepad2,

    /// <summary>
    /// XInput controller at index 3.
    /// </summary>
    Gamepad3,
}
