using System.Diagnostics;
using DriftCore.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Vortice.XInput;

namespace DriftCore.Services;

public class DriftEngine : BackgroundService
{
    // --- Estado Interno ---
    private DriftConfig _config = new();
    private bool _isTestMode = false;
    private DateTime _lastHeartbeat = DateTime.Now;
    private DateTime _lastDriverRetry = DateTime.MinValue;
    private static readonly TimeSpan _driverRetryInterval = TimeSpan.FromSeconds(5);
    private volatile bool _isShuttingDown = false;

    // Listas Estáticas (Jogos Implementados - Vazio por enquanto na Fase 1)
    private static readonly List<GameProfile> _implementedGames = new();

    // --- Componentes de Driver ---
    private ViGEmClient? _vigem;
    private IXbox360Controller? _virtualController;

    public DriftEngine()
    {
        // A inicialização pesada fica no ExecuteAsync para controle de fluxo
    }

    // --- Inicialização e Setup do Driver ---

    private bool InitializeDriver()
    {
        try
        {
            Console.WriteLine("[Engine] Conectando ao Driver ViGEm...");
            _vigem = new ViGEmClient(); // Pode lançar exceção se driver não existir
            _virtualController = _vigem.CreateXbox360Controller();

            // Evento de Vibração (Feedback Loop)
            _virtualController.FeedbackReceived += OnFeedbackReceived;

            _virtualController.Connect();
            Console.WriteLine("[Engine] Controle Virtual Criado com Sucesso!");
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            Console.WriteLine("[ERRO CRÍTICO] Driver ViGEmBus não encontrado no sistema.");
            return false;
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[ERRO CRÍTICO] DLL do cliente ViGEm não encontrada.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO CRÍTICO] Falha genérica ao iniciar driver: {ex.Message}");
            return false;
        }
    }



    // --- Métodos Públicos (API) ---

    public void SetTestMode(bool isTest) => _isTestMode = isTest;
    public void RegisterHeartbeat() => _lastHeartbeat = DateTime.Now;

    public void UpdateConfig(DriftConfig newConfig)
    {
        _config = newConfig;
        if (_isTestMode) Console.WriteLine($"[Config] Input Alterado para: {_config.SelectedInputDevice}");
    }

    public void Shutdown()
    {
        Console.WriteLine("[Engine] Shutdown() chamado.");
        _isShuttingDown = true;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[Engine] Recebido sinal de parada...");
        _isShuttingDown = true;

        // Timeout de 5 segundos para parada
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await base.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Engine] Timeout na parada, forçando encerramento.");
        }

        Console.WriteLine("[Engine] Parada concluída.");
    }

    public override void Dispose()
    {
        _isShuttingDown = true;
        // CleanupDriver é chamado no finally do ExecuteAsync
        base.Dispose();
    }

    public EngineStatus GetStatus()
    {
        var status = new EngineStatus();

        // 1. Inputs Disponíveis
        var inputs = new List<DeviceInfo>();

        for (int i = 0; i < 4; i++)
        {
            // Cast seguro para uint (0 a 3)
            bool connected = XInput.GetState((uint)i, out var st);
            inputs.Add(new DeviceInfo
            {
                Name = $"Xbox Controller {i + 1}",
                // Enum Gamepad0(1) + i
                Type = (InputDeviceType)((int)InputDeviceType.Gamepad0 + i),
                IsConnected = connected
            });
        }
        status.AvailableInputList = inputs;

        // Listas de Jogos
        status.ImplementedGames = _implementedGames;
        status.NotImplementedGames = Enum.GetValues<GameProfile>()
                                         .Except(_implementedGames)
                                         .ToList();

        // 2. Input Selecionado
        var active = inputs.FirstOrDefault(x => x.Type == _config.SelectedInputDevice);
        status.SelectedInput = active ?? new DeviceInfo { Name = "None", IsConnected = false };

        // 3. Status do Jogo (Mockup fase 1)
        status.SelectedGame = new GameInfo
        {
            Game = _config.SelectedGame,
            IsConnected = false // Fase 1: Sem telemetria ainda
        };

        return status;
    }

    // --- Loop Principal (Worker) ---

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[Engine] Loop de Processamento Iniciado.");
        int debugCounter = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_isShuttingDown)
            {

                // Se o driver falhou, apenas espera (API Mode Only)
                if (_virtualController is null)
                {
                    if (DateTime.UtcNow - _lastDriverRetry >= _driverRetryInterval)
                    {
                        _lastDriverRetry = DateTime.UtcNow;
                        if (!_isShuttingDown)
                        {
                            InitializeDriver();
                        }
                    }
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                // 2. Leitura de Input
                double steeringInput = 0;
                bool inputDetected = false;
                State gamepadState = new State();
                int gamepadIndex = (int)_config.SelectedInputDevice;

                if (gamepadIndex >= 0 && gamepadIndex <= 3 && XInput.GetState((uint)gamepadIndex, out gamepadState))
                {
                    // -32768..32767 -> -1.0..1.0
                    steeringInput = gamepadState.Gamepad.LeftThumbX / 32768.0;

                    // Deadzone simples
                    if (Math.Abs(steeringInput) < 0.15) steeringInput = 0;

                    inputDetected = true;
                }

                // 3. Processamento (Passthrough na Fase 1)
                // Aqui entra a física depois. Hoje é: Input -> Output direto.
                try
                {
                    short outputSteering = (short)(steeringInput * 32767);
                    _virtualController.SetAxisValue(Xbox360Axis.LeftThumbX, outputSteering);

                    // 4. Mapeamento de Botões
                    if (inputDetected)
                    {
                        ApplyFullPassthrough(gamepadState.Gamepad);
                    }

                    // Envia comando para o Windows
                    _virtualController.SubmitReport();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engine] Erro ao enviar relatório: {ex.Message}");
                    CleanupDriver();
                }

                // 5. Debug (Modo Teste)
                if (_isTestMode)
                {
                    debugCounter++;
                    if (debugCounter % 50 == 0) // ~500ms
                    {
                        Console.WriteLine($"[IO] Input: {_config.SelectedInputDevice} | Detected: {inputDetected} | In: {steeringInput:F2} | Out: {steeringInput:F2}");
                        debugCounter = 0;
                    }
                }

                try
                {
                    await Task.Delay(0, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelamento normal via Ctrl+C
        }
        finally
        {
            Console.WriteLine("[Engine] Loop de Processamento Encerrado.");
            CleanupDriver();
        }
    }

    private void ApplyFullPassthrough(Gamepad gp)
    {
        if (_virtualController == null) return;

        // Eixos
        _virtualController.SetAxisValue(Xbox360Axis.LeftThumbY, gp.LeftThumbY);
        _virtualController.SetAxisValue(Xbox360Axis.RightThumbX, gp.RightThumbX);
        _virtualController.SetAxisValue(Xbox360Axis.RightThumbY, gp.RightThumbY);
        _virtualController.SetSliderValue(Xbox360Slider.LeftTrigger, gp.LeftTrigger);
        _virtualController.SetSliderValue(Xbox360Slider.RightTrigger, gp.RightTrigger);

        // Botões
        _virtualController.SetButtonState(Xbox360Button.A, (gp.Buttons & GamepadButtons.A) != 0);
        _virtualController.SetButtonState(Xbox360Button.B, (gp.Buttons & GamepadButtons.B) != 0);
        _virtualController.SetButtonState(Xbox360Button.X, (gp.Buttons & GamepadButtons.X) != 0);
        _virtualController.SetButtonState(Xbox360Button.Y, (gp.Buttons & GamepadButtons.Y) != 0);
        _virtualController.SetButtonState(Xbox360Button.LeftShoulder, (gp.Buttons & GamepadButtons.LeftShoulder) != 0);
        _virtualController.SetButtonState(Xbox360Button.RightShoulder, (gp.Buttons & GamepadButtons.RightShoulder) != 0);
        _virtualController.SetButtonState(Xbox360Button.Back, (gp.Buttons & GamepadButtons.Back) != 0);
        _virtualController.SetButtonState(Xbox360Button.Start, (gp.Buttons & GamepadButtons.Start) != 0);
        _virtualController.SetButtonState(Xbox360Button.LeftThumb, (gp.Buttons & GamepadButtons.LeftThumb) != 0);
        _virtualController.SetButtonState(Xbox360Button.RightThumb, (gp.Buttons & GamepadButtons.RightThumb) != 0);

        // D-Pad
        _virtualController.SetButtonState(Xbox360Button.Up, (gp.Buttons & GamepadButtons.DPadUp) != 0);
        _virtualController.SetButtonState(Xbox360Button.Down, (gp.Buttons & GamepadButtons.DPadDown) != 0);
        _virtualController.SetButtonState(Xbox360Button.Left, (gp.Buttons & GamepadButtons.DPadLeft) != 0);
        _virtualController.SetButtonState(Xbox360Button.Right, (gp.Buttons & GamepadButtons.DPadRight) != 0);
    }

    private void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        int index = (int)_config.SelectedInputDevice;
        if (index >= 0 && index <= 3)
        {
            ushort boost = 4;
            // Passthrough 1:1 - conversão de 0-255 para 0-65535 * boost
            ushort largeOut = (ushort)(e.LargeMotor * 257 * boost);
            ushort smallOut = (ushort)(e.SmallMotor * 257 * boost);

            var vibration = new Vibration(largeOut, smallOut);
            XInput.SetVibration((uint)index, vibration);

            if (_isTestMode && (e.LargeMotor > 0 || e.SmallMotor > 0))
            {
                Console.WriteLine($"[Vibration] In: L={e.LargeMotor} S={e.SmallMotor} | Out: L={largeOut} S={smallOut} | Idx={index}");
            }
        }
    }

    private void CleanupDriver()
    {
        Console.WriteLine("[Engine] Limpando recursos do driver...");

        var controller = _virtualController;
        _virtualController = null;

        try
        {
            if (controller != null)
            {
                controller.FeedbackReceived -= OnFeedbackReceived;
                controller.Disconnect();
                Console.WriteLine("[Engine] Controle virtual desconectado.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Engine] Erro ao desconectar controle virtual: {ex.Message}");
        }

        var vigem = _vigem;
        _vigem = null;

        try
        {
            vigem?.Dispose();
            Console.WriteLine("[Engine] ViGEm liberado.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Engine] Erro ao liberar ViGEm: {ex.Message}");
        }

        Console.WriteLine("[Engine] Cleanup concluído.");
    }
}