namespace DriftCore.Configuration;

/// <summary>
/// Configurações estáticas da engine (não mudam em runtime).
/// </summary>
public static class EngineSettings
{
    // === Heartbeat ===
    public const bool HeartbeatEnabled = false;
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);

    // === Shutdown ===
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan StopAsyncTimeout = TimeSpan.FromSeconds(5);

    // === Driver Retry ===
    public static readonly TimeSpan DriverRetryInterval = TimeSpan.FromSeconds(5);

    // === Loop ===
    public static readonly TimeSpan DisconnectedDelay = TimeSpan.FromMilliseconds(200);

    // === Vibração ===
    public const int VibrationBoost = 5;

    // === Debug ===
    public const int DebugLogInterval = 50;
}
