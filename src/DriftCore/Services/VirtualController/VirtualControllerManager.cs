using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Vortice.XInput;

namespace DriftCore.Services.VirtualController;

/// <summary>
/// Gerencia o controle virtual Xbox 360 via ViGEm.
/// </summary>
public class VirtualControllerManager : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _isDisposed;

    public bool IsConnected => _controller != null;

    public event EventHandler<VibrationEventArgs>? VibrationReceived;

    /// <summary>
    /// Inicializa a conexão com o driver ViGEm e cria o controle virtual.
    /// </summary>
    public bool Initialize()
    {
        try
        {
            Console.WriteLine("[VirtualController] Conectando ao Driver ViGEm...");

            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.FeedbackReceived += OnFeedbackReceived;
            _controller.Connect();

            Console.WriteLine("[VirtualController] Controle Virtual Criado com Sucesso!");
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            Console.WriteLine("[ERRO] Driver ViGEmBus não encontrado no sistema.");
            return false;
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[ERRO] DLL do cliente ViGEm não encontrada.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] Falha ao iniciar driver: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Envia o estado atual para o jogo.
    /// </summary>
    public void SendState(VirtualControllerState state)
    {
        if (_controller == null) return;

        // Eixos analógicos
        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftStickX);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftStickY);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, state.RightStickX);
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, state.RightStickY);

        // Gatilhos
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);

        // Botões
        SetButtons(state.Buttons);

        _controller.SubmitReport();
    }

    private void SetButtons(GamepadButtons buttons)
    {
        if (_controller == null) return;

        _controller.SetButtonState(Xbox360Button.A, buttons.HasFlag(GamepadButtons.A));
        _controller.SetButtonState(Xbox360Button.B, buttons.HasFlag(GamepadButtons.B));
        _controller.SetButtonState(Xbox360Button.X, buttons.HasFlag(GamepadButtons.X));
        _controller.SetButtonState(Xbox360Button.Y, buttons.HasFlag(GamepadButtons.Y));

        _controller.SetButtonState(Xbox360Button.LeftShoulder, buttons.HasFlag(GamepadButtons.LeftShoulder));
        _controller.SetButtonState(Xbox360Button.RightShoulder, buttons.HasFlag(GamepadButtons.RightShoulder));

        _controller.SetButtonState(Xbox360Button.Back, buttons.HasFlag(GamepadButtons.Back));
        _controller.SetButtonState(Xbox360Button.Start, buttons.HasFlag(GamepadButtons.Start));

        _controller.SetButtonState(Xbox360Button.LeftThumb, buttons.HasFlag(GamepadButtons.LeftThumb));
        _controller.SetButtonState(Xbox360Button.RightThumb, buttons.HasFlag(GamepadButtons.RightThumb));

        _controller.SetButtonState(Xbox360Button.Up, buttons.HasFlag(GamepadButtons.DPadUp));
        _controller.SetButtonState(Xbox360Button.Down, buttons.HasFlag(GamepadButtons.DPadDown));
        _controller.SetButtonState(Xbox360Button.Left, buttons.HasFlag(GamepadButtons.DPadLeft));
        _controller.SetButtonState(Xbox360Button.Right, buttons.HasFlag(GamepadButtons.DPadRight));
    }

    private void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        VibrationReceived?.Invoke(this, new VibrationEventArgs(e.LargeMotor, e.SmallMotor));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Console.WriteLine("[VirtualController] Desconectando...");

        try
        {
            if (_controller != null)
            {
                _controller.FeedbackReceived -= OnFeedbackReceived;
                _controller.Disconnect();
                _controller = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualController] Erro ao desconectar: {ex.Message}");
        }

        try
        {
            _client?.Dispose();
            _client = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualController] Erro ao liberar cliente: {ex.Message}");
        }

        Console.WriteLine("[VirtualController] Desconectado.");
    }
}

/// <summary>
/// Argumentos do evento de vibração.
/// </summary>
public class VibrationEventArgs : EventArgs
{
    public byte LargeMotor { get; }
    public byte SmallMotor { get; }

    public VibrationEventArgs(byte largeMotor, byte smallMotor)
    {
        LargeMotor = largeMotor;
        SmallMotor = smallMotor;
    }
}
