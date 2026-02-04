using System;
using System.Diagnostics;
using System.Threading;

namespace DriftCore.Infrastructure;

/// <summary>
/// High-precision timer based on <see cref="Stopwatch"/> for hot paths.
/// </summary>
public sealed class HighPrecisionTimer
{
    private long _intervalTicks;
    private long _lastTicks;

    public HighPrecisionTimer(TimeSpan interval)
    {
        _intervalTicks = (long)(Stopwatch.Frequency * interval.TotalSeconds);
        _lastTicks = 0;
    }

    /// <summary>
    /// Returns true if the interval has elapsed since the last call that returned true.
    /// </summary>
    public bool TryElapse()
    {
        long now = Stopwatch.GetTimestamp();
        long last = Volatile.Read(ref _lastTicks);
        long interval = Volatile.Read(ref _intervalTicks);

        if (now - last < interval)
            return false;

        Volatile.Write(ref _lastTicks, now);
        return true;
    }

    /// <summary>
    /// Updates the timer interval.
    /// </summary>
    public void UpdateInterval(TimeSpan interval)
    {
        var ticks = (long)(Stopwatch.Frequency * Math.Max(interval.TotalSeconds, 0));
        Volatile.Write(ref _intervalTicks, ticks);
        Volatile.Write(ref _lastTicks, 0);
    }

    /// <summary>
    /// Resets the timer.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref _lastTicks, Stopwatch.GetTimestamp());
    }
}
