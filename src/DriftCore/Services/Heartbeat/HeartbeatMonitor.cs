using System.Diagnostics;
using System.Threading;

namespace DriftCore.Services.Heartbeat;

/// <summary>
/// Monitora heartbeat de forma leve (baseado em Stopwatch) para baixa latência.
/// </summary>
public sealed class HeartbeatMonitor
{
    private readonly bool _enabled;
    private readonly long _timeoutTicks;
    private long _lastHeartbeatTicks;

    public HeartbeatMonitor(bool enabled, TimeSpan timeout)
    {
        _enabled = enabled;
        _timeoutTicks = (long)(Stopwatch.Frequency * timeout.TotalSeconds);
        _lastHeartbeatTicks = Stopwatch.GetTimestamp();
    }

    public void Register()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, Stopwatch.GetTimestamp());
    }

    public bool IsExpired()
    {
        if (!_enabled)
            return false;

        long last = Volatile.Read(ref _lastHeartbeatTicks);
        long now = Stopwatch.GetTimestamp();

        return now - last > _timeoutTicks;
    }
}