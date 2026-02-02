using System;
using System.Diagnostics;
using System.Threading;

namespace DriftCore.Infrastructure;

/// <summary>
/// Timer de alta precisão baseado em Stopwatch para hot paths.
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
    /// Retorna true se o intervalo passou desde a última chamada que retornou true.
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
    /// Atualiza o intervalo de disparo do timer.
    /// </summary>
    public void UpdateInterval(TimeSpan interval)
    {
        var ticks = (long)(Stopwatch.Frequency * Math.Max(interval.TotalSeconds, 0));
        Volatile.Write(ref _intervalTicks, ticks);
        Volatile.Write(ref _lastTicks, 0);
    }

    /// <summary>
    /// Reseta o timer.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref _lastTicks, Stopwatch.GetTimestamp());
    }
}
