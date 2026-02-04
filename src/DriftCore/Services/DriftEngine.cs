using System.Threading;
using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Services.ForceFeedback;
using DriftCore.Services.Heartbeat;
using DriftCore.Services.Input;
using DriftCore.Services.InputProcessing;
using DriftCore.Services.VirtualWheel;

namespace DriftCore.Services;

/// <summary>
/// Drift engine main loop. Orchestrates input reads, processing, and output.
/// </summary>
public sealed class DriftEngine
{
    private readonly ShutdownManager _shutdown;
    private readonly HeartbeatMonitor _heartbeat;
    private readonly InputProcessor _inputProcessor;
    private readonly HighPrecisionTimer _driverRetryTimer;

    private EngineOptions _config = new();
    private bool _testMode;
    private VirtualWheelManager? _virtualWheel;
    private uint _activeVJoyDeviceId;

    private readonly ForceFeedbackService _forceFeedback;

    private XboxWirelessUsbDriver? _usbDriver;
    private string _activeUsbGuid = string.Empty;
    private int _activeUsbTimeoutMs;

    private DebugSnapshot _lastDebug;

    public DriftEngine(EngineOptions options, Action stopApplication)
    {
        _shutdown = new ShutdownManager(stopApplication, () => EngineDefaults.ShutdownTimeout);
        _heartbeat = new HeartbeatMonitor();
        _inputProcessor = new InputProcessor();
        _driverRetryTimer = new HighPrecisionTimer(EngineDefaults.DriverRetryInterval);
        _forceFeedback = new ForceFeedbackService();

        ApplyConfig(options);
    }

    #region Lifecycle

    public Task RunAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Engine] Starting...");

        try
        {
            RunMainLoop(stoppingToken);
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        finally
        {
            Cleanup();
            Console.WriteLine("[Engine] Stopped.");
        }

        return Task.CompletedTask;
    }

    private void RunMainLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_shutdown.IsShuttingDown)
        {
            if (_heartbeat.IsExpired())
            {
                _shutdown.RequestForcedShutdown("Heartbeat expired");
                break;
            }

            TryConnectDriver();

            if (_virtualWheel?.IsConnected != true)
            {
                if (token.WaitHandle.WaitOne(EngineDefaults.DisconnectedDelay))
                    break;
                continue;
            }

            ProcessFrame();
            LogDebug();
        }
    }

    #endregion

    #region Main Loop

    private void ProcessFrame()
    {
        var config = Volatile.Read(ref _config);
        var input = ReadInput(config);

        _lastDebug = DebugSnapshot.From(config, input);

        // Update external force feedback before integrating steering.
        _inputProcessor.SetFeedbackForce(_forceFeedback.LatestNormalizedForce);

        if (!input.IsConnected)
        {
            _inputProcessor.Reset();
            _virtualWheel?.SendState(VirtualWheelState.Empty);
            return;
        }

        double processed = _inputProcessor.ProcessSteering(input.Steering);
        _lastDebug = _lastDebug.WithProcessed(processed);
        var state = VirtualWheelState.FromGamepad(input.Gamepad, processed);

        _virtualWheel?.SendState(state);
    }

    private GamepadReadResult ReadInput(EngineOptions config)
    {
        if (config.UseWinUsbReceiver)
        {
            EnsureUsbDriver(config);

            if (_usbDriver != null && _usbDriver.TryGetLatest(out var usbResult))
                return usbResult;

            return GamepadReadResult.Disconnected;
        }

        return GamepadReader.Read(config.InputDeviceIndex);
    }

    private void EnsureUsbDriver(EngineOptions config)
    {
        var guid = config.WinUsbDeviceInterfaceGuid ?? string.Empty;
        var timeout = Math.Max(0, config.WinUsbReadTimeoutMs);

        // Recria driver se GUID/timeout mudarem.
        if (_usbDriver != null && string.Equals(_activeUsbGuid, guid, StringComparison.Ordinal) && _activeUsbTimeoutMs == timeout)
            return;

        _usbDriver?.Dispose();
        _usbDriver = null;

        _activeUsbGuid = guid;
        _activeUsbTimeoutMs = timeout;

        // GUID may be empty: the driver will fall back to the generic USB device interface GUID
        // and filter by VID/PID.
        _usbDriver = new XboxWirelessUsbDriver(guid, timeout);
        _usbDriver.Start();
    }

    private void TryConnectDriver()
    {
        if (_virtualWheel?.IsConnected == true) return;
        if (!_driverRetryTimer.TryElapse()) return;
        if (_shutdown.IsShuttingDown) return;

        var config = Volatile.Read(ref _config);
        var desiredDeviceId = (uint)Math.Clamp(config.VJoyDeviceId, 1, 16);

        if (_virtualWheel == null || _activeVJoyDeviceId != desiredDeviceId)
        {
            _virtualWheel?.Dispose();
            _virtualWheel = new VirtualWheelManager(desiredDeviceId);
            _activeVJoyDeviceId = desiredDeviceId;
        }

        if (_virtualWheel.Initialize())
        {
            // Start FFB listener after vJoy device is acquired.
            _forceFeedback.EnsureStarted(_activeVJoyDeviceId);
        }
    }

    private void Cleanup()
    {
        _virtualWheel?.Dispose();
        _virtualWheel = null;

        _forceFeedback.Dispose();

        _usbDriver?.Dispose();
        _usbDriver = null;
    }

    #endregion

    #region Public API

    public void SetTestMode(bool isTest) => _testMode = isTest;

    public void RegisterHeartbeat() => _heartbeat.Register();

    public void Shutdown() => _shutdown.RequestShutdown("Shutdown requested");

    public void ForceShutdown(string reason) => _shutdown.RequestForcedShutdown(reason);

    #endregion

    #region Debug

    private int _debugCounter;

    private void LogDebug()
    {
        if (!_testMode) return;

        var interval = EngineDefaults.DebugLogInterval;
        if (interval <= 0) return;
        if (++_debugCounter < interval) return;

        _debugCounter = 0;
        var d = _lastDebug;
        var ffb = _forceFeedback.LatestNormalizedForce;
        Console.WriteLine($"[IO] {d.Source} | Connected: {d.IsConnected} | Raw: {d.Raw:F3} | FFB: {ffb:F3} | Processed: {d.Processed:F3}");
    }

    private readonly record struct DebugSnapshot(string Source, bool IsConnected, double Raw, double Processed)
    {
        public static DebugSnapshot From(EngineOptions config, GamepadReadResult input)
        {
            var src = config.UseWinUsbReceiver ? "WinUSB" : $"XInput{config.InputDeviceIndex}";
            return new DebugSnapshot(src, input.IsConnected, input.Steering, 0d);
        }

        public DebugSnapshot WithProcessed(double processed) => this with { Processed = processed };
    }

    private void ApplyConfig(EngineOptions config)
    {
        var normalized = NormalizeOptions(config);

        Volatile.Write(ref _config, normalized);
        _inputProcessor.UpdateSmoothing(normalized.SmoothingEnabled, normalized.SmoothingValue);
        _heartbeat.UpdateSettings(EngineDefaults.HeartbeatEnabled, EngineDefaults.HeartbeatTimeout);
        _driverRetryTimer.UpdateInterval(EngineDefaults.DriverRetryInterval);

        if (_testMode)
        {
            Console.WriteLine($"[Config] Input={normalized.InputDeviceIndex} vJoy={normalized.VJoyDeviceId} Smooth={(normalized.SmoothingEnabled ? normalized.SmoothingValue : 0)}");
        }
    }

    private static EngineOptions NormalizeOptions(EngineOptions input)
    {
        return new EngineOptions
        {
            InputDeviceIndex = Math.Clamp(input.InputDeviceIndex, 0, 3),
            VJoyDeviceId = Math.Clamp(input.VJoyDeviceId, 1, 16),

            SmoothingEnabled = input.SmoothingEnabled,
            SmoothingValue = Math.Clamp(input.SmoothingValue, 0, 100),

            UseWinUsbReceiver = input.UseWinUsbReceiver,
            WinUsbDeviceInterfaceGuid = input.WinUsbDeviceInterfaceGuid ?? string.Empty,
            WinUsbReadTimeoutMs = Math.Max(0, input.WinUsbReadTimeoutMs)
        };
    }

    #endregion
}
