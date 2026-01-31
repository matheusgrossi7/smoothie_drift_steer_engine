using Vortice.XInput;

namespace DriftCore.Services.VirtualController;

/// <summary>
/// Estado do controle virtual a ser enviado para o jogo.
/// </summary>
public struct VirtualControllerState
{
    public short LeftStickX;
    public short LeftStickY;
    public short RightStickX;
    public short RightStickY;
    public byte LeftTrigger;
    public byte RightTrigger;
    public GamepadButtons Buttons;

    /// <summary>
    /// Cria um estado a partir de um Gamepad físico (passthrough).
    /// </summary>
    public static VirtualControllerState FromGamepad(Gamepad gamepad, short? overrideLeftStickX = null)
    {
        return new VirtualControllerState
        {
            LeftStickX = overrideLeftStickX ?? gamepad.LeftThumbX,
            LeftStickY = gamepad.LeftThumbY,
            RightStickX = gamepad.RightThumbX,
            RightStickY = gamepad.RightThumbY,
            LeftTrigger = gamepad.LeftTrigger,
            RightTrigger = gamepad.RightTrigger,
            Buttons = gamepad.Buttons
        };
    }

    /// <summary>
    /// Estado vazio (nenhum input).
    /// </summary>
    public static VirtualControllerState Empty => new();
}
