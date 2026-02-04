using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace DriftCore.Services.VirtualWheel;

/// <summary>
/// Manages a virtual DualShock 4 controller via ViGEmBus.
/// </summary>
public sealed class VirtualWheelManager : IDisposable
{
    private bool _disposed;
    private ViGEmClient? _client;
    private IDualShock4Controller? _controller;
    private bool _connected;

    public bool IsConnected => _connected && !_disposed;

    public bool Initialize()
    {
        try
        {
            _client ??= new ViGEmClient();

            if (_controller == null)
            {
                _controller = _client.CreateDualShock4Controller();
                _controller.AutoSubmitReport = false;
            }

            if (!_connected)
            {
                Console.WriteLine("[VirtualWheel] Connecting ViGEm DS4...");
                _controller.Connect();
                _connected = true;
            }

            Console.WriteLine("[VirtualWheel] Connected (DS4).");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ViGEm failure: {ex.Message}");
            return false;
        }
    }

    public void SendState(VirtualWheelState state)
    {
        if (_disposed || !_connected || _controller == null) return;

        // Axes
        _controller.SetAxisValue(DualShock4Axis.LeftThumbX, AxisToByte(state.SteeringX));
        _controller.SetAxisValue(DualShock4Axis.LeftThumbY, 128);

        _controller.SetAxisValue(DualShock4Axis.RightThumbX, AxisToByte(state.Rx));
        _controller.SetAxisValue(DualShock4Axis.RightThumbY, 128);

        // Triggers (independent axes)
        _controller.SetSliderValue(DualShock4Slider.LeftTrigger, AxisToByte(state.BrakeY));
        _controller.SetSliderValue(DualShock4Slider.RightTrigger, AxisToByte(state.ThrottleZ));

        // D-Pad
        _controller.SetDPadDirection(MapDpad(state.Pov1));

        // Buttons
        ApplyButtons(state.Buttons);

        _controller.SubmitReport();
    }

    private void ApplyButtons(WheelButtons buttons)
    {
        _controller?.SetButtonState(DualShock4Button.Cross, buttons.HasFlag(WheelButtons.Button1));
        _controller?.SetButtonState(DualShock4Button.Circle, buttons.HasFlag(WheelButtons.Button2));
        _controller?.SetButtonState(DualShock4Button.Square, buttons.HasFlag(WheelButtons.Button3));
        _controller?.SetButtonState(DualShock4Button.Triangle, buttons.HasFlag(WheelButtons.Button4));

        _controller?.SetButtonState(DualShock4Button.ShoulderLeft, buttons.HasFlag(WheelButtons.Button5));
        _controller?.SetButtonState(DualShock4Button.ShoulderRight, buttons.HasFlag(WheelButtons.Button6));

        _controller?.SetButtonState(DualShock4Button.Share, buttons.HasFlag(WheelButtons.Button7));
        _controller?.SetButtonState(DualShock4Button.Options, buttons.HasFlag(WheelButtons.Button8));

        _controller?.SetButtonState(DualShock4Button.ThumbLeft, buttons.HasFlag(WheelButtons.Button9));
        _controller?.SetButtonState(DualShock4Button.ThumbRight, buttons.HasFlag(WheelButtons.Button10));
    }

    private static DualShock4DPadDirection MapDpad(int pov)
    {
        if (pov < 0) return DualShock4DPadDirection.None;

        var angle = pov / 100.0;
        var sector = ((int)Math.Round(angle / 45.0)) % 8;

        return sector switch
        {
            0 => DualShock4DPadDirection.North,
            1 => DualShock4DPadDirection.Northeast,
            2 => DualShock4DPadDirection.East,
            3 => DualShock4DPadDirection.Southeast,
            4 => DualShock4DPadDirection.South,
            5 => DualShock4DPadDirection.Southwest,
            6 => DualShock4DPadDirection.West,
            7 => DualShock4DPadDirection.Northwest,
            _ => DualShock4DPadDirection.None
        };
    }

    private static byte AxisToByte(int axis)
    {
        var clamped = Math.Clamp(axis, 0, 32768);
        return (byte)Math.Clamp((int)Math.Round(clamped * (255.0 / 32768.0)), 0, 255);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.WriteLine("[VirtualWheel] Disconnecting...");

        try
        {
            if (_controller != null)
            {
                if (_connected)
                    _controller.Disconnect();

                _controller.Dispose();
                _controller = null;
                _connected = false;
            }

            _client?.Dispose();
            _client = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualWheel] Error: {ex.Message}");
        }

        Console.WriteLine("[VirtualWheel] Disconnected.");
    }
}
