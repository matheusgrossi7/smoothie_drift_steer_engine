namespace DriftCore.Configuration;

/// <summary>
/// Engine internal defaults (not configurable via appsettings).
/// </summary>
public static class EngineDefaults
{
    /// <summary>
    /// Enables the heartbeat monitor by default.
    /// When true, the engine runs in a "monitored" mode:
    /// - periodic calls to <c>RegisterHeartbeat()</c> are expected;
    /// - if the time since the last heartbeat exceeds <see cref="HeartbeatTimeout"/>,
    ///   the engine requests a forced shutdown.
    /// When false, heartbeat is ignored and expiration will never trigger shutdown.
    /// </summary>
    public const bool HeartbeatEnabled = false;
    /// <summary>
    /// Maximum time allowed without receiving a heartbeat before considering it expired.
    /// Used only when <see cref="HeartbeatEnabled"/> is enabled.
    /// </summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);
    /// <summary>
    /// Maximum time to wait before forcing termination during shutdown.
    /// </summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    /// <summary>
    /// Maximum time to wait for host <c>StopAsync</c> before terminating.
    /// </summary>
    public static readonly TimeSpan StopAsyncTimeout = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Interval between vJoy reconnection attempts.
    /// </summary>
    public static readonly TimeSpan DriverRetryInterval = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Delay applied when vJoy is disconnected to avoid a busy loop.
    /// </summary>
    public static readonly TimeSpan DisconnectedDelay = TimeSpan.FromMilliseconds(200);
    /// <summary>
    /// How many frames between debug log lines in test mode.
    /// </summary>
    public const int DebugLogInterval = 50;
}
