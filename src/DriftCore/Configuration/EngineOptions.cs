namespace DriftCore.Configuration;

/// <summary>
/// Engine configuration (appsettings.json).
/// Contains only user-tunable parameters.
/// </summary>
public sealed class EngineOptions
{
    public sealed class SteeringPhysicsOptions
    {
        public sealed class SoftLockOptions
        {
            /// <summary>
            /// Enables a spring-like end stop near the steering limits.
            /// This avoids hitting the hard limit (vJoy clipping) by applying a strong counter torque.
            /// </summary>
            public bool Enabled { get; set; } = true;

            /// <summary>
            /// Where the soft lock starts, in normalized steering position (0..1).
            /// Example: 0.92 means the last 8% of travel is the soft lock zone.
            /// </summary>
            public double Start { get; set; } = 0.92;

            /// <summary>
            /// Spring stiffness (torque units) at full penetration (|pos| == 1.0).
            /// Higher values push back harder.
            /// </summary>
            public double Stiffness { get; set; } = 35.0;

            /// <summary>
            /// Extra damping applied only while moving further into the stop (outward velocity).
            /// </summary>
            public double Damping { get; set; } = 6.0;

            /// <summary>
            /// Safety clamp for the internal integrator to prevent runaway on extreme inputs.
            /// Allows some overshoot beyond 1.0 while still remaining stable.
            /// </summary>
            public double MaxOvershoot { get; set; } = 0.03;

            /// <summary>
            /// Output clamp limit sent to vJoy (<= 1.0). Using slightly less than 1.0 avoids
            /// the vJoy axis sticking at the hard maximum.
            /// </summary>
            public double OutputLimit { get; set; } = 0.999;
        }

        public double Deadzone { get; set; } = 0.25;

        // Higher gains / lower inertia => faster steering. Damping prevents runaway velocity.
        public double Inertia { get; set; } = 0.2;
        public double Damping { get; set; } = 12.0;
        public double DriverTorqueGain { get; set; } = 16.0;
        public double FeedbackTorqueGain { get; set; } = 15.0;

        public double MaxDtSeconds { get; set; } = 0.05;

        /// <summary>
        /// Soft end-stop (soft lock) settings.
        /// </summary>
        public SoftLockOptions SoftLock { get; set; } = new();
    }

    /// <summary>
    /// XInput gamepad index (0-3).
    /// </summary>
    public int InputDeviceIndex { get; set; } = 0;

    /// <summary>
    /// vJoy device ID (1-16).
    /// </summary>
    public int VJoyDeviceId { get; set; } = 1;

    /// <summary>
    /// Enables input smoothing.
    /// </summary>
    public bool SmoothingEnabled { get; set; } = true;

    /// <summary>
    /// Smoothing strength (0-100).
    /// </summary>
    public int SmoothingValue { get; set; } = 50;

    /// <summary>
    /// Steering physics model tunables.
    /// </summary>
    public SteeringPhysicsOptions SteeringPhysics { get; set; } = new();

    /// <summary>
    /// If true, reads the Xbox 360 Wireless Receiver via WinUSB (WinUSBNet) instead of XInput.
    /// Requires the device to be bound to WinUSB and a Device Interface GUID to be configured.
    /// </summary>
    public bool UseWinUsbReceiver { get; set; } = false;

    /// <summary>
    /// WinUSB Device Interface GUID (defined in the WinUSB .inf) used to enumerate the receiver.
    /// Example: "{BB9176E8-924F-4A7E-963A-6DC6A4E87FC2}".
    /// If empty, falls back to the generic USB device interface GUID and filters by VID/PID.
    /// </summary>
    public string WinUsbDeviceInterfaceGuid { get; set; } = "";

    /// <summary>
    /// Pipe read timeout (ms). 0 = no timeout.
    /// </summary>
    public int WinUsbReadTimeoutMs { get; set; } = 20;
}
