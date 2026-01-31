using DriftCore.Models;
using DriftCore.Services.Input;
using DriftCore.Services.VirtualController;
using DriftCore.Services.Vibration;

namespace DriftCore.Services;

/// <summary>
/// Motor principal do Drift. Orquestra leitura de input, processamento e saída.
/// </summary>
public class DriftEngine : BackgroundService
{
    // === Configuração ===
    private DriftConfig _config = new();
    private bool _testMode;
    private volatile bool _isShuttingDown;

    // === Componentes ===
    private VirtualControllerManager? _virtualController;
    private VibrationProcessor? _vibrationProcessor;

    // === Retry do Driver ===
    private DateTime _lastDriverRetry = DateTime.MinValue;
    private static readonly TimeSpan DriverRetryInterval = TimeSpan.FromSeconds(5);

    // === Status (para API) ===
    private DateTime _lastHeartbeat = DateTime.Now;
    private static readonly List<GameProfile> ImplementedGames = new();

    #region Ciclo de Vida

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Engine] Iniciando...");

        _vibrationProcessor = new VibrationProcessor(_testMode);
        int debugCounter = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
            {
                EnsureDriverConnected();

                if (_virtualController?.IsConnected != true)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                ProcessFrame();
                LogDebug(ref debugCounter);

                await Task.Yield();
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
        int gamepadIndex = (int)_config.SelectedInputDevice;
        var input = GamepadReader.Read(gamepadIndex);

        if (!input.IsConnected)
        {
            _virtualController!.SendState(VirtualControllerState.Empty);
            return;
        }

        // TODO: Aqui entra o processamento de física/drift no futuro
        double processedSteering = input.Steering;

        // Converte para o range do controle virtual
        short steeringOutput = (short)(processedSteering * 32767);

        // Cria estado com steering processado, resto é passthrough
        var state = VirtualControllerState.FromGamepad(input.Gamepad, steeringOutput);

        _virtualController!.SendState(state);
    }

    private void EnsureDriverConnected()
    {
        if (_virtualController?.IsConnected == true)
            return;

        if (DateTime.UtcNow - _lastDriverRetry < DriverRetryInterval)
            return;

        _lastDriverRetry = DateTime.UtcNow;

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
        int gamepadIndex = (int)_config.SelectedInputDevice;
        _vibrationProcessor?.Process(gamepadIndex, e.LargeMotor, e.SmallMotor);
    }

    #endregion

    #region API Pública

    public void SetTestMode(bool isTest) => _testMode = isTest;

    public void RegisterHeartbeat() => _lastHeartbeat = DateTime.Now;

    public void Shutdown()
    {
        Console.WriteLine("[Engine] Shutdown solicitado.");
        _isShuttingDown = true;
    }

    public void UpdateConfig(DriftConfig newConfig)
    {
        _config = newConfig;
        if (_testMode)
            Console.WriteLine($"[Config] Input: {_config.SelectedInputDevice}");
    }

    public EngineStatus GetStatus()
    {
        var inputs = Enumerable.Range(0, 4)
            .Select(i => new DeviceInfo
            {
                Name = $"Xbox Controller {i + 1}",
                Type = (InputDeviceType)i,
                IsConnected = GamepadReader.IsConnected(i)
            })
            .ToList();

        var selectedInput = inputs.FirstOrDefault(x => x.Type == _config.SelectedInputDevice)
            ?? new DeviceInfo { Name = "None", IsConnected = false };

        return new EngineStatus
        {
            AvailableInputList = inputs,
            SelectedInput = selectedInput,
            ImplementedGames = ImplementedGames,
            NotImplementedGames = Enum.GetValues<GameProfile>().Except(ImplementedGames).ToList(),
            SelectedGame = new GameInfo
            {
                Game = _config.SelectedGame,
                IsConnected = false // Fase 1: Sem telemetria
            }
        };
    }

    #endregion

    #region Debug

    private void LogDebug(ref int counter)
    {
        if (!_testMode) return;

        counter++;
        if (counter < 50) return;
        counter = 0;

        int gamepadIndex = (int)_config.SelectedInputDevice;
        var input = GamepadReader.Read(gamepadIndex);

        Console.WriteLine($"[IO] Gamepad{gamepadIndex} | Connected: {input.IsConnected} | Steering: {input.Steering:F2}");
    }

    #endregion
}
