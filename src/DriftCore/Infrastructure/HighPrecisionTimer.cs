using System.Diagnostics;
using DriftCore.Configuration;

namespace DriftCore.Infrastructure;

/// <summary>
/// Timer de alta precisão baseado em Stopwatch para hot paths.
/// </summary>
public sealed class HighPrecisionTimer
{
    private readonly long _intervalTicks;
    private long _lastTicks;

    public HighPrecisionTimer(TimeSpan interval)
    {
        _intervalTicks = (long)(Stopwatch.Frequency * interval.TotalSeconds);
        _lastTicks = 0;
    }

    /// <summary>
    /// Retorna true se o intervalo passou desde a última chamada que retornou true.
    /// </summary>
    public bool TryElapse()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastTicks < _intervalTicks)
            return false;

        _lastTicks = now;
        return true;
    }

    /// <summary>
    /// Reseta o timer.
    /// </summary>
    public void Reset()
    {
        _lastTicks = Stopwatch.GetTimestamp();
    }
}
