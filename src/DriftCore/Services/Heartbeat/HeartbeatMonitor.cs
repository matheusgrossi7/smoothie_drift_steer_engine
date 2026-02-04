using System.Diagnostics;
using System.Threading;

namespace DriftCore.Services.Heartbeat;

/// <summary>
/// Monitors a periodic heartbeat using a high-precision timer.
/// </summary>
public sealed class HeartbeatMonitor
{
    private long _timeoutTicks;
    private int _enabled;
    private long _lastTicks;

    public HeartbeatMonitor()
    {
        UpdateSettings(false, TimeSpan.FromSeconds(10));
        _lastTicks = Stopwatch.GetTimestamp();
    }

    public void UpdateSettings(bool enabled, TimeSpan timeout)
    {
        Volatile.Write(ref _enabled, enabled ? 1 : 0);

        if (enabled)
        {
            Interlocked.Exchange(ref _lastTicks, Stopwatch.GetTimestamp());
        }

        var seconds = Math.Max(timeout.TotalSeconds, 0);
        var ticks = (long)(Stopwatch.Frequency * seconds);
        Volatile.Write(ref _timeoutTicks, ticks);
    }

    public void Register()
    {
        Interlocked.Exchange(ref _lastTicks, Stopwatch.GetTimestamp());
    }

    public bool IsExpired()
    {
        if (Volatile.Read(ref _enabled) == 0) return false;

        long last = Volatile.Read(ref _lastTicks);
        long timeout = Volatile.Read(ref _timeoutTicks);
        return Stopwatch.GetTimestamp() - last > timeout;
    }
}