using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Vortice.XInput;

namespace DriftCore.Services.VirtualController;

/// <summary>
/// Gerencia o controle virtual Xbox 360 via ViGEm.
/// </summary>
public sealed class VirtualControllerManager : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _disposed;

    public bool IsConnected => _controller != null && !_disposed;

    public event EventHandler<VibrationEventArgs>? VibrationReceived;

    public bool Initialize()
    {
        try
        {
            Console.WriteLine("[VirtualController] Conectando ao ViGEm...");
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.FeedbackReceived += OnFeedbackReceived;
            _controller.Connect();
            Console.WriteLine("[VirtualController] Conectado!");
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            Console.WriteLine("[ERRO] Driver ViGEmBus não encontrado.");
            return false;
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[ERRO] DLL ViGEm não encontrada.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] Falha ViGEm: {ex.Message}");
            return false;
        }
    }

    public void SendState(VirtualControllerState state)
    {
        if (_controller == null || _disposed) return;

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftStickX);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftStickY);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, state.RightStickX);
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, state.RightStickY);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
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
        if (_disposed) return;
        _disposed = true;

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
            Console.WriteLine($"[VirtualController] Erro: {ex.Message}");
        }

        try
        {
            _client?.Dispose();
            _client = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualController] Erro cliente: {ex.Message}");
        }

        Console.WriteLine("[VirtualController] Desconectado.");
    }
}

public sealed class VibrationEventArgs : EventArgs
{
    public byte LargeMotor { get; }
    public byte SmallMotor { get; }

    public VibrationEventArgs(byte large, byte small)
    {
        LargeMotor = large;
        SmallMotor = small;
    }
}
