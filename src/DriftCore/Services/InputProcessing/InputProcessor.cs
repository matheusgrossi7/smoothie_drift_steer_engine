using System;
using System.Diagnostics;
using System.Threading;

namespace DriftCore.Services.InputProcessing;

/// <summary>
/// Processes steering input with optional smoothing and angle hold.
/// </summary>
public sealed class InputProcessor
{
    // Single deadzone applied to the input (kept centralized here).
    private const double Deadzone = 0.25;
    private const double SpeedAdjust = 0.5;
    private const double MaxUnitsPerSecond = 1.5;

    private bool _smoothingEnabled;
    private int _smoothingValue;

    private double _steeringPosition;
    private double _smoothedInput;
    private long _lastTimestamp;

    public void UpdateSmoothing(bool enabled, int value)
    {
        Volatile.Write(ref _smoothingEnabled, enabled);
        Volatile.Write(ref _smoothingValue, Math.Clamp(value, 0, 100));

        if (!enabled)
        {
            _smoothedInput = 0;
        }
    }

    public double ProcessSteering(double input)
    {
        // Input represents angular velocity (not position):
        // -1..0 turns left, 0..1 turns right.
        // When the stick returns to 0, the wheel stops moving and holds the current angle.

        input = Math.Clamp(input, -1.0, 1.0);

        // Deadzone to avoid drift around zero.
        var absInput = Math.Abs(input);
        if (absInput <= Deadzone)
        {
            input = 0.0;
        }
        else
        {
            // Remap to keep a linear response outside the deadzone.
            var scaled = (absInput - Deadzone) / (1.0 - Deadzone);
            input = Math.Sign(input) * Math.Clamp(scaled, 0.0, 1.0);
        }

        // Optional input smoothing (simple low-pass filter).
        if (Volatile.Read(ref _smoothingEnabled))
        {
            var smoothingValue = Volatile.Read(ref _smoothingValue); // 0..100

            // 0 => no smoothing (alpha=1), 100 => strong smoothing (alpha~0.05)
            var t = smoothingValue / 100.0;
            var alpha = Math.Clamp(1.0 - (t * 0.95), 0.05, 1.0);

            _smoothedInput += (input - _smoothedInput) * alpha;
            input = _smoothedInput;
        }
        else
        {
            _smoothedInput = input;
        }

        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref _lastTimestamp);
        if (last == 0)
        {
            Volatile.Write(ref _lastTimestamp, now);
            return _steeringPosition;
        }

        Volatile.Write(ref _lastTimestamp, now);

        var deltaTicks = now - last;
        if (deltaTicks <= 0)
        {
            return _steeringPosition;
        }

        var dtSeconds = deltaTicks / (double)Stopwatch.Frequency;

        // Global steering speed adjust (0..1).
        var speedAdjust = Math.Clamp(SpeedAdjust, 0.0, 1.0);

        var newPosition = _steeringPosition + (input * MaxUnitsPerSecond * speedAdjust * dtSeconds);
        _steeringPosition = Math.Clamp(newPosition, -1.0, 1.0);

        // Linear output: current normalized wheel position.
        return _steeringPosition;
    }

    /// <summary>
    /// Resets the processor state (useful on reconnects).
    /// </summary>
    public void Reset()
    {
        _steeringPosition = 0;
        _smoothedInput = 0;
        Volatile.Write(ref _lastTimestamp, 0);
    }
}
