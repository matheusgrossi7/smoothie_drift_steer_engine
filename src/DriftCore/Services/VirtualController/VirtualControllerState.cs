using Vortice.XInput;

namespace DriftCore.Services.VirtualController;

/// <summary>
/// Estado imutável do controle virtual a ser enviado para o jogo.
/// </summary>
public readonly struct VirtualControllerState
{
    public short LeftStickX { get; }
    public short LeftStickY { get; }
    public short RightStickX { get; }
    public short RightStickY { get; }
    public byte LeftTrigger { get; }
    public byte RightTrigger { get; }
    public GamepadButtons Buttons { get; }

    private VirtualControllerState(
        short leftStickX, short leftStickY,
        short rightStickX, short rightStickY,
        byte leftTrigger, byte rightTrigger,
        GamepadButtons buttons)
    {
        LeftStickX = leftStickX;
        LeftStickY = leftStickY;
        RightStickX = rightStickX;
        RightStickY = rightStickY;
        LeftTrigger = leftTrigger;
        RightTrigger = rightTrigger;
        Buttons = buttons;
    }

    public static VirtualControllerState FromGamepad(Gamepad gamepad, short? overrideLeftStickX = null)
    {
        return new VirtualControllerState(
            overrideLeftStickX ?? gamepad.LeftThumbX,
            gamepad.LeftThumbY,
            gamepad.RightThumbX,
            gamepad.RightThumbY,
            gamepad.LeftTrigger,
            gamepad.RightTrigger,
            gamepad.Buttons
        );
    }

    public static VirtualControllerState Empty => new(0, 0, 0, 0, 0, 0, 0);
}
