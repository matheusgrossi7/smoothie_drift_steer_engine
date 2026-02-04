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

    private bool _softLockEnabled = true;
    private double _softLockStart = 0.92;
    private double _softLockStiffness = 35.0;
    private double _softLockDamping = 6.0;
    private double _softLockMaxOvershoot = 0.03;
    private double _softLockOutputLimit = 0.999;

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

        _softLockEnabled = options.SoftLock.Enabled;
        _softLockStart = options.SoftLock.Start;
        _softLockStiffness = options.SoftLock.Stiffness;
        _softLockDamping = options.SoftLock.Damping;
        _softLockMaxOvershoot = options.SoftLock.MaxOvershoot;
        _softLockOutputLimit = options.SoftLock.OutputLimit;
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
        ApplySoftLockSafetyClamp();

        return ApplyOutputLimit(_steeringPosition);
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
        if (smoothingValue <= 0)
        {
            _driverTorque = desiredDriverTorque;
            return;
        }

        var maxTorqueRate = ComputeMaxTorqueRate(smoothingValue);
        _driverTorque = MoveTowards(_driverTorque, desiredDriverTorque, maxTorqueRate * dtSeconds);
    }

    private void IntegrateSteering(double dtSeconds)
    {
        var feedbackTorque = Volatile.Read(ref _feedbackForce) * _feedbackTorqueGain;

        var softLockTorque = ComputeSoftLockTorque();

        var inertia = Math.Max(1e-6, _inertia);
        var damping = Math.Max(0.0, _damping);
        var acceleration = (_driverTorque + feedbackTorque + softLockTorque - (_velocity * damping)) / inertia;

        _velocity += acceleration * dtSeconds;
        _steeringPosition += _velocity * dtSeconds;
    }

    private double ComputeSoftLockTorque()
    {
        if (!_softLockEnabled)
            return 0.0;

        var start = Math.Clamp(_softLockStart, 0.0, 1.0);
        var absPos = Math.Abs(_steeringPosition);
        if (absPos <= start)
            return 0.0;

        var range = Math.Max(1e-6, 1.0 - start);
        var penetration = (absPos - start) / range; // 0..(>1 if overshoot)

        var direction = Math.Sign(_steeringPosition);
        if (direction == 0)
            return 0.0;

        // Spring: pushes back towards center.
        var spring = -direction * (_softLockStiffness * penetration);

        // Damping: only while moving further into the stop.
        var outwardVelocity = direction * _velocity; // >0 when moving outward
        var damper = outwardVelocity > 0.0 ? (-direction * (_softLockDamping * outwardVelocity)) : 0.0;

        return spring + damper;
    }

    private void ApplySoftLockSafetyClamp()
    {
        if (!_softLockEnabled)
        {
            // Keep a safety clamp even when soft lock is disabled.
            _steeringPosition = Math.Clamp(_steeringPosition, -1.0, 1.0);
            if (_steeringPosition >= 1.0 && _velocity > 0.0) _velocity = 0.0;
            if (_steeringPosition <= -1.0 && _velocity < 0.0) _velocity = 0.0;
            return;
        }

        var maxOvershoot = Math.Max(0.0, _softLockMaxOvershoot);
        var maxPos = 1.0 + maxOvershoot;

        if (_steeringPosition > maxPos)
        {
            _steeringPosition = maxPos;
            if (_velocity > 0.0) _velocity = 0.0;
            return;
        }

        if (_steeringPosition < -maxPos)
        {
            _steeringPosition = -maxPos;
            if (_velocity < 0.0) _velocity = 0.0;
        }
    }

    private double ApplyOutputLimit(double position)
    {
        var limit = Math.Clamp(_softLockOutputLimit, 0.1, 1.0);
        return Math.Clamp(position, -limit, limit);
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * Math.Clamp(t, 0.0, 1.0));

    private static double ComputeMaxTorqueRate(int smoothingValue)
    {
        // Map 0..100 -> maxRate..minRate with a progressive curve (more effect at higher values).
        // Units: torque-units per second.
        const double maxRate = 350.0;
        const double minRate = 3.0;

        var t = Math.Clamp(smoothingValue, 0, 100) / 100.0;
        t = Math.Pow(t, 1.8);

        // Exponential (geometric) interpolation for a wider, more perceptible range.
        return maxRate * Math.Pow(minRate / maxRate, t);
    }

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
