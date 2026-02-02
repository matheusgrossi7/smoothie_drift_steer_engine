using System.Threading;
using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Services.Heartbeat;
using DriftCore.Services.Input;
using DriftCore.Services.InputProcessing;
using DriftCore.Services.VirtualWheel;

namespace DriftCore.Services;

/// <summary>
/// Motor principal do Drift. Orquestra leitura de input, processamento e saída.
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

    public DriftEngine(EngineOptions options, Action stopApplication)
    {
        _shutdown = new ShutdownManager(stopApplication, () => EngineDefaults.ShutdownTimeout);
        _heartbeat = new HeartbeatMonitor();
        _inputProcessor = new InputProcessor();
        _driverRetryTimer = new HighPrecisionTimer(EngineDefaults.DriverRetryInterval);

        ApplyConfig(options);
    }

    #region Lifecycle

    public Task RunAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Engine] Iniciando...");

        try
        {
            RunMainLoop(stoppingToken);
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        finally
        {
            Cleanup();
            Console.WriteLine("[Engine] Encerrado.");
        }

        return Task.CompletedTask;
    }

    private void RunMainLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_shutdown.IsShuttingDown)
        {
            if (_heartbeat.IsExpired())
            {
                _shutdown.RequestForcedShutdown("Heartbeat expirado");
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
        var input = GamepadReader.Read(config.InputDeviceIndex);

        if (!input.IsConnected)
        {
            _inputProcessor.Reset();
            _virtualWheel?.SendState(VirtualWheelState.Empty);
            return;
        }

        double processed = _inputProcessor.ProcessSteering(input.Steering);
        var state = VirtualWheelState.FromGamepad(input.Gamepad, processed, config.UseLbAsClutch);

        _virtualWheel?.SendState(state);
    }

    private void TryConnectDriver()
    {
        if (_virtualWheel?.IsConnected == true) return;
        if (!_driverRetryTimer.TryElapse()) return;
        if (_shutdown.IsShuttingDown) return;

        var config = Volatile.Read(ref _config);
        var desiredDeviceId = (uint)Math.Clamp(config.VJoyDeviceId, 1, 16);

        if (_virtualWheel != null && _activeVJoyDeviceId != desiredDeviceId)
        {
            _virtualWheel.Dispose();
            _virtualWheel = null;
        }

        _virtualWheel?.Dispose();
        _virtualWheel = new VirtualWheelManager(desiredDeviceId);
        _activeVJoyDeviceId = desiredDeviceId;

        _virtualWheel.Initialize();
    }

    private void Cleanup()
    {
        _virtualWheel?.Dispose();
        _virtualWheel = null;
    }

    #endregion

    #region Public API

    public void SetTestMode(bool isTest) => _testMode = isTest;

    public void RegisterHeartbeat() => _heartbeat.Register();

    public void Shutdown() => _shutdown.RequestShutdown("Shutdown solicitado");

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
        var config = Volatile.Read(ref _config);
        var input = GamepadReader.Read(config.InputDeviceIndex);
        double processed = _inputProcessor.ProcessSteering(input.Steering);

        Console.WriteLine($"[IO] Gamepad{config.InputDeviceIndex} | Connected: {input.IsConnected} | Raw: {input.Steering:F3} | Processed: {processed:F3} | vJoy: {_virtualWheel?.IsConnected == true}");
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
            UseLbAsClutch = input.UseLbAsClutch,

            SmoothingEnabled = input.SmoothingEnabled,
            SmoothingValue = Math.Clamp(input.SmoothingValue, 0, 100)
        };
    }

    #endregion
}
