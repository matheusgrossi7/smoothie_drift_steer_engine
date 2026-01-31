using System.Text.Json.Serialization;

namespace DriftCore.Models;

/// <summary>
/// Profiles used to implement game-specific telemetry and physics handling.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameProfile
{
    ForzaHorizon5,

    ForzaHorizon6,

    ForzaMotorsport,
}
