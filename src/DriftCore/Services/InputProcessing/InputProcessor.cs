using System;
using System.Diagnostics;
using System.Threading;
using DriftCore.Configuration;

namespace DriftCore.Services.InputProcessing;

/// <summary>Integrates steering position from driver torque + force feedback (FFB).</summary>
public sealed class InputProcessor
{
    private double _deadzone = 0.25;
    private double _inertia = 0.2;
    private double _damping = 12.0;
    private double _driverTorqueGain = 16.0;
    private double _feedbackTorqueGain = 15.0;
    private double _maxDtSeconds = 0.05;

    private bool _smoothingEnabled;
    private int _smoothingValue;

    private double _steeringPosition;
    private double _velocity;
    private double _driverTorque;
    private double _feedbackForce;
    private long _lastTimestamp;

    public void UpdateSmoothing(bool enabled, int value)
    {
        Volatile.Write(ref _smoothingEnabled, enabled);
        Volatile.Write(ref _smoothingValue, Math.Clamp(value, 0, 100));
    }

    public void UpdatePhysics(EngineOptions.SteeringPhysicsOptions options)
    {
        // Assumes options are already normalized/clamped by DriftEngine.
        _deadzone = options.Deadzone;
        _inertia = options.Inertia;
        _damping = options.Damping;
        _driverTorqueGain = options.DriverTorqueGain;
        _feedbackTorqueGain = options.FeedbackTorqueGain;
        _maxDtSeconds = options.MaxDtSeconds;
    }

    /// <summary>
    /// Sets the external force feedback (FFB) value coming from the game/vJoy.
    /// Expected to be normalized to -1.0..1.0.
    /// </summary>
    public void SetFeedbackForce(double force)
    {
        force = Math.Clamp(force, -1.0, 1.0);
        Volatile.Write(ref _feedbackForce, force);
    }

    public double ProcessSteering(double input)
    {
        input = ApplyDeadzoneAndRemap(Math.Clamp(input, -1.0, 1.0));

        if (!TryGetDeltaSeconds(out var dtSeconds))
        {
            return _steeringPosition;
        }

        UpdateDriverTorque(input, dtSeconds);
        IntegrateSteering(dtSeconds);
        ApplyBumpStops();

        return _steeringPosition;
    }

    private double ApplyDeadzoneAndRemap(double input)
    {
        var absInput = Math.Abs(input);
        if (absInput <= _deadzone)
        {
            return 0.0;
        }

        var scaled = (absInput - _deadzone) / (1.0 - _deadzone);
        return Math.Sign(input) * Math.Clamp(scaled, 0.0, 1.0);
    }

    private bool TryGetDeltaSeconds(out double dtSeconds)
    {
        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref _lastTimestamp);

        if (last == 0)
        {
            Volatile.Write(ref _lastTimestamp, now);
            dtSeconds = 0;
            return false;
        }

        Volatile.Write(ref _lastTimestamp, now);

        var deltaTicks = now - last;
        if (deltaTicks <= 0)
        {
            dtSeconds = 0;
            return false;
        }

        dtSeconds = deltaTicks / (double)Stopwatch.Frequency;
        if (dtSeconds > _maxDtSeconds) dtSeconds = _maxDtSeconds;
        return true;
    }

    private void UpdateDriverTorque(double input, double dtSeconds)
    {
        var desiredDriverTorque = input * _driverTorqueGain;
        if (!Volatile.Read(ref _smoothingEnabled))
        {
            _driverTorque = desiredDriverTorque;
            return;
        }

        var smoothingValue = Volatile.Read(ref _smoothingValue); // 0..100
        var t = smoothingValue / 100.0;
        var maxTorqueRate = Lerp(250.0, 8.0, t);
        _driverTorque = MoveTowards(_driverTorque, desiredDriverTorque, maxTorqueRate * dtSeconds);
    }

    private void IntegrateSteering(double dtSeconds)
    {
        var feedbackTorque = Volatile.Read(ref _feedbackForce) * _feedbackTorqueGain;

        var inertia = Math.Max(1e-6, _inertia);
        var damping = Math.Max(0.0, _damping);
        var acceleration = (_driverTorque + feedbackTorque - (_velocity * damping)) / inertia;

        _velocity += acceleration * dtSeconds;
        _steeringPosition += _velocity * dtSeconds;
    }

    private void ApplyBumpStops()
    {
        if (_steeringPosition > 1.0)
        {
            _steeringPosition = 1.0;
            if (_velocity > 0.0) _velocity = 0.0;
            return;
        }

        if (_steeringPosition < -1.0)
        {
            _steeringPosition = -1.0;
            if (_velocity < 0.0) _velocity = 0.0;
        }
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * Math.Clamp(t, 0.0, 1.0));

    private static double MoveTowards(double current, double target, double maxDelta)
    {
        var delta = target - current;
        if (Math.Abs(delta) <= maxDelta) return target;
        return current + (Math.Sign(delta) * maxDelta);
    }

    /// <summary>
    /// Resets the processor state (useful on reconnects).
    /// </summary>
    public void Reset()
    {
        _steeringPosition = 0;
        _velocity = 0;
        _driverTorque = 0;
        Volatile.Write(ref _feedbackForce, 0.0);
        Volatile.Write(ref _lastTimestamp, 0);
    }
}
