namespace DriftCore.Models;

/// <summary>
/// Aggregated runtime status snapshot exposed by the engine.
/// </summary>
public sealed class EngineStatus
{
    /// <summary>
    /// Currently selected input device.
    /// </summary>
    public DeviceInfo SelectedInput { get; set; } = new();

    /// <summary>
    /// Current telemetry connection info for the selected game.
    /// </summary>
    public GameInfo SelectedGame { get; set; } = new();

    /// <summary>
    /// All detected input devices available to the engine.
    /// </summary>
    public List<DeviceInfo> AvailableInputList { get; set; } = new();

    /// <summary>
    /// Games with telemetry decoding already implemented.
    /// </summary>
    public List<GameProfile> ImplementedGames { get; set; } = new();

    /// <summary>
    /// Games detected but not yet implemented.
    /// </summary>
    public List<GameProfile> NotImplementedGames { get; set; } = new();
}
