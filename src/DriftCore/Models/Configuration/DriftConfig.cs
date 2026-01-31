namespace DriftCore.Models;

/// <summary>
/// Application configuration for drift steering and telemetry.
/// </summary>
public sealed class DriftConfig
{
    public GameProfile SelectedGame { get; set; } = GameProfile.ForzaHorizon5;

    /// <summary>
    /// Selected input device used as the steering source.
    /// </summary>
    public InputDeviceType SelectedInputDevice { get; set; } = InputDeviceType.Gamepad0;

    /// <summary>
    /// Enables or disables steering smoothing.
    /// </summary>
    public bool IsSmoothingEnabled { get; set; } = true;

    /// <summary>
    /// Smoothing factor for the steering filter.
    /// Range: 0 (raw) to 100 (max smoothness).
    /// </summary>
    public int SmoothingValue { get; set; } = 50;
}
