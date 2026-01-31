using System.Diagnostics;
using System.Threading;
using DriftCore.Configuration;

namespace DriftCore.Services.Heartbeat;

/// <summary>
/// Monitora heartbeat com timer de alta precisão.
/// </summary>
public sealed class HeartbeatMonitor
{
    private readonly bool _enabled;
    private readonly long _timeoutTicks;
    private long _lastTicks;

    public HeartbeatMonitor() : this(EngineSettings.HeartbeatEnabled, EngineSettings.HeartbeatTimeout) { }

    public HeartbeatMonitor(bool enabled, TimeSpan timeout)
    {
        _enabled = enabled;
        _timeoutTicks = (long)(Stopwatch.Frequency * timeout.TotalSeconds);
        _lastTicks = Stopwatch.GetTimestamp();
    }

    public void Register()
    {
        Interlocked.Exchange(ref _lastTicks, Stopwatch.GetTimestamp());
    }

    public bool IsExpired()
    {
        if (!_enabled) return false;

        long last = Volatile.Read(ref _lastTicks);
        return Stopwatch.GetTimestamp() - last > _timeoutTicks;
    }
}