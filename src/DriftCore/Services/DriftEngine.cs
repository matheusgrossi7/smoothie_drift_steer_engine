using System.Threading;
using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Models;
using DriftCore.Services.Heartbeat;
using DriftCore.Services.Input;
using DriftCore.Services.InputProcessing;
using DriftCore.Services.Status;
using DriftCore.Services.VirtualController;

namespace DriftCore.Services;

/// <summary>
/// Motor principal do Drift. Orquestra leitura de input, processamento e saída.
/// </summary>
public sealed class DriftEngine : BackgroundService
{
    private readonly ShutdownManager _shutdown;
    private readonly HeartbeatMonitor _heartbeat;
    private readonly EngineStatusProvider _statusProvider;
    private readonly InputProcessor _inputProcessor;
    private readonly HighPrecisionTimer _driverRetryTimer;

    private DriftConfig _config = new();
    private bool _testMode;
    private VirtualWheelManager? _virtualWheel;
    private uint _activeVJoyDeviceId;

    private static readonly List<GameProfile> ImplementedGames = new();

    public DriftEngine(IHostApplicationLifetime lifetime)
    {
        _shutdown = new ShutdownManager(lifetime);
        _heartbeat = new HeartbeatMonitor();
        _statusProvider = new EngineStatusProvider(ImplementedGames);
        _inputProcessor = new InputProcessor();
        _driverRetryTimer = new HighPrecisionTimer(EngineSettings.DriverRetryInterval);
    }

    #region Lifecycle

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Engine] Iniciando...");

        try
        {
            await RunMainLoop(stoppingToken);
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        finally
        {
            Cleanup();
            Console.WriteLine("[Engine] Encerrado.");
        }
    }

    private async Task RunMainLoop(CancellationToken token)
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
                await Task.Delay(EngineSettings.DisconnectedDelay, token);
                continue;
            }

            ProcessFrame();
            LogDebug();

            Thread.Yield();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[Engine] Parando...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(EngineSettings.StopAsyncTimeout);

        try
        {
            await base.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Engine] Timeout na parada.");
        }
    }

    #endregion

    #region Main Loop

    private void ProcessFrame()
    {
        var config = Volatile.Read(ref _config);
        int gamepadIndex = (int)config.SelectedInputDevice;
        var input = GamepadReader.Read(gamepadIndex);

        if (!input.IsConnected)
        {
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

    public void UpdateConfig(DriftConfig newConfig)
    {
        Volatile.Write(ref _config, newConfig);
        _inputProcessor.UpdateSmoothing(newConfig.IsSmoothingEnabled, newConfig.SmoothingValue);

        if (_testMode)
            Console.WriteLine($"[Config] Input: {newConfig.SelectedInputDevice}");
    }

    public EngineStatus GetStatus()
    {
        var config = Volatile.Read(ref _config);
        return _statusProvider.BuildStatus(config.SelectedInputDevice, config.SelectedGame);
    }

    #endregion

    #region Debug

    private int _debugCounter;

    private void LogDebug()
    {
        if (!_testMode) return;
        if (++_debugCounter < EngineSettings.DebugLogInterval) return;

        _debugCounter = 0;
        var config = Volatile.Read(ref _config);
        var input = GamepadReader.Read((int)config.SelectedInputDevice);
        double processed = _inputProcessor.ProcessSteering(input.Steering);

        Console.WriteLine($"[IO] Gamepad{(int)config.SelectedInputDevice} | Connected: {input.IsConnected} | Raw: {input.Steering:F3} | Processed: {processed:F3}");
    }

    #endregion
}
