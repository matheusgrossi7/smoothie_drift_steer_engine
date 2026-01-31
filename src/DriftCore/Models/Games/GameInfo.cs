namespace DriftCore.Models;

/// <summary>
/// Telemetry connection information for a game.
/// </summary>
public sealed class GameInfo
{
    /// <summary>
    /// Game currently being monitored by the telemetry listener.
    /// </summary>
    public GameProfile Game { get; set; } = GameProfile.ForzaHorizon5;

    /// <summary>
    /// True when UDP telemetry packets are being received.
    /// </summary>
    public bool IsConnected { get; set; }
}
