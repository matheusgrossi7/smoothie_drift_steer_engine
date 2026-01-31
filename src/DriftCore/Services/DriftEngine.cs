using System.Diagnostics;
using System.Threading;
using DriftCore.Models;
using DriftCore.Services.Heartbeat;
using DriftCore.Services.Input;
using DriftCore.Services.InputProcessing;
using DriftCore.Services.Status;
using DriftCore.Services.VirtualController;
using DriftCore.Services.Vibration;

namespace DriftCore.Services;

/// <summary>
/// Motor principal do Drift. Orquestra leitura de input, processamento e saída.
/// </summary>
public class DriftEngine : BackgroundService
{
    // === Dependências ===
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly EngineStatusProvider _statusProvider;
    private readonly HeartbeatMonitor _heartbeat;
    private readonly InputProcessor _inputProcessor;

    // === Configuração ===
    private DriftConfig _config = new();
    private bool _testMode;
    private volatile bool _isShuttingDown;

    // === Heartbeat (Desativado até Flutter estar pronto) ===
    private const bool HEARTBEAT_ENABLED = false;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);

    // === Componentes ===
    private VirtualControllerManager? _virtualController;
    private VibrationProcessor? _vibrationProcessor;

    // === Retry do Driver ===
    private long _lastDriverRetryTicks;
    private static readonly TimeSpan DriverRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly long DriverRetryIntervalTicks = (long)(Stopwatch.Frequency * DriverRetryInterval.TotalSeconds);

    private static readonly TimeSpan DisconnectedDelay = TimeSpan.FromMilliseconds(200);

    // === Jogos Implementados ===
    private static readonly List<GameProfile> ImplementedGames = new();

    public DriftEngine(IHostApplicationLifetime appLifetime)
    {
        _appLifetime = appLifetime;
        _statusProvider = new EngineStatusProvider(ImplementedGames);
        _heartbeat = new HeartbeatMonitor(HEARTBEAT_ENABLED, HeartbeatTimeout);
        _inputProcessor = new InputProcessor();
    }

    #region Ciclo de Vida

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Engine] Iniciando...");

        _vibrationProcessor = new VibrationProcessor(_testMode);

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
            {
                // Verifica timeout do heartbeat (se ativado)
                if (_heartbeat.IsExpired())
                {
                    Console.WriteLine("[Engine] Heartbeat expirado. Encerrando aplicação...");
                    _appLifetime.StopApplication();
                    break;
                }

                EnsureDriverConnected();

                if (_virtualController?.IsConnected != true)
                {
                    await Task.Delay(DisconnectedDelay, stoppingToken);
                    continue;
                }

                ProcessFrame();
                LogDebug();

                Thread.Yield();
            }
        }
        catch (OperationCanceledException) { /* Shutdown normal */ }
        finally
        {
            Cleanup();
            Console.WriteLine("[Engine] Encerrado.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[Engine] Parando...");
        _isShuttingDown = true;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try { await base.StopAsync(cts.Token); }
        catch (OperationCanceledException) { Console.WriteLine("[Engine] Timeout na parada."); }
    }

    public override void Dispose()
    {
        _isShuttingDown = true;
        base.Dispose();
    }

    #endregion

    #region Loop Principal

    private void ProcessFrame()
    {
        var config = Volatile.Read(ref _config);
        int gamepadIndex = (int)config.SelectedInputDevice;
        var input = GamepadReader.Read(gamepadIndex);

        if (!input.IsConnected)
        {
            _virtualController?.SendState(VirtualControllerState.Empty);
            return;
        }

        // Processa input (futuro: manter ângulo)
        double processedSteering = _inputProcessor.ProcessSteering(input.Steering);

        // Converte para o range do controle virtual
        short steeringOutput = (short)(processedSteering * 32767);

        // Cria estado com steering processado, resto é passthrough
        var state = VirtualControllerState.FromGamepad(input.Gamepad, steeringOutput);

        _virtualController?.SendState(state);
    }

    private void EnsureDriverConnected()
    {
        if (_virtualController?.IsConnected == true)
            return;

        long nowTicks = Stopwatch.GetTimestamp();
        if (nowTicks - _lastDriverRetryTicks < DriverRetryIntervalTicks)
            return;

        _lastDriverRetryTicks = nowTicks;

        if (_isShuttingDown)
            return;

        _virtualController?.Dispose();
        _virtualController = new VirtualControllerManager();

        if (_virtualController.Initialize())
        {
            _virtualController.VibrationReceived += OnVibrationReceived;
        }
    }

    private void Cleanup()
    {
        _virtualController?.Dispose();
        _virtualController = null;
    }

    #endregion

    #region Vibração

    private void OnVibrationReceived(object? sender, VibrationEventArgs e)
    {
        var config = Volatile.Read(ref _config);
        int gamepadIndex = (int)config.SelectedInputDevice;
        _vibrationProcessor?.Process(gamepadIndex, e.LargeMotor, e.SmallMotor);
    }

    #endregion

    #region API Pública

    public void SetTestMode(bool isTest) => _testMode = isTest;

    public void RegisterHeartbeat() => _heartbeat.Register();

    public void Shutdown()
    {
        Console.WriteLine("[Engine] Shutdown solicitado.");
        _isShuttingDown = true;
        if (!_appLifetime.ApplicationStopping.IsCancellationRequested)
            _appLifetime.StopApplication();
    }

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

        _debugCounter++;
        if (_debugCounter < 50) return;
        _debugCounter = 0;

        var config = Volatile.Read(ref _config);
        int gamepadIndex = (int)config.SelectedInputDevice;
        var input = GamepadReader.Read(gamepadIndex);

        Console.WriteLine($"[IO] Gamepad{gamepadIndex} | Connected: {input.IsConnected} | Steering: {input.Steering:F2}");
    }

    #endregion
}
