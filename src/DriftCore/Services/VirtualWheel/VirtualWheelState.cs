using Vortice.XInput;

namespace DriftCore.Services.VirtualWheel;

/// <summary>
/// Estado imutável do volante virtual (vJoy) a ser enviado para o jogo.
/// </summary>
/// <remarks>
/// vJoy usa, por padrão, eixos no intervalo 0..32768.
/// Steering aqui é X (0=esquerda, 16384=centro, 32768=direita).
/// </remarks>
public readonly struct VirtualWheelState
{
    public int SteeringX { get; }
    public int BrakeY { get; }
    public int ThrottleZ { get; }
    public int ClutchRx { get; }

    /// <summary>
    /// POV contínuo em centésimos de grau (0..35900) ou -1 para neutro.
    /// </summary>
    public int Pov1 { get; }

    public WheelButtons Buttons { get; }

    private VirtualWheelState(int steeringX, int brakeY, int throttleZ, int clutchRx, int pov1, WheelButtons buttons)
    {
        SteeringX = ClampAxis(steeringX);
        BrakeY = ClampAxis(brakeY);
        ThrottleZ = ClampAxis(throttleZ);
        ClutchRx = ClampAxis(clutchRx);
        Pov1 = pov1;
        Buttons = buttons;
    }

    public static VirtualWheelState FromGamepad(Gamepad gamepad, double steeringNormalized, bool useLbAsClutch)
    {
        // Steering: -1..1 -> 0..32768
        var steeringX = AxisFromSignedNormalized(steeringNormalized);

        // Pedais: 0..255 -> 0..32768
        var brakeY = AxisFromUnsignedByte(gamepad.LeftTrigger);
        var throttleZ = AxisFromUnsignedByte(gamepad.RightTrigger);

        // Embreagem (clutch):
        // - Se UseLbAsClutch=true, LB vira embreagem digital (0/100%).
        // - Caso contrário, usa analógico direito (eixo Y) apenas metade superior.
        var clutchRx = useLbAsClutch
            ? AxisFromDigitalButton(gamepad.Buttons.HasFlag(GamepadButtons.LeftShoulder))
            : AxisFromRightStickUpHalf(gamepad.RightThumbY);

        // D-Pad -> POV contínuo (graus * 100)
        int pov1 = PovFromDpad(gamepad.Buttons);

        var buttons = WheelButtonMapper.FromGamepad(gamepad, useLbAsClutch);
        return new VirtualWheelState(steeringX, brakeY, throttleZ, clutchRx, pov1, buttons);
    }

    public static VirtualWheelState Empty => new(16384, 0, 0, 0, -1, WheelButtons.None);

    private static int ClampAxis(int value) => Math.Clamp(value, 0, 32768);

    private static int AxisFromSignedNormalized(double value)
    {
        value = Math.Clamp(value, -1.0, 1.0);
        // [-1..1] => [0..32768]
        return ClampAxis((int)Math.Round((value + 1.0) * 16384.0));
    }

    private static int AxisFromUnsignedByte(byte value)
    {
        // [0..255] => [0..32768]
        return ClampAxis((int)Math.Round(value * (32768.0 / 255.0)));
    }

    private static int AxisFromRightStickUpHalf(short rightThumbY)
    {
        // rightThumbY: [-32768..32767] (up is positive)
        // Normalize to [0..1] for the "up" direction only.
        double up = rightThumbY / 32767.0;
        up = Math.Clamp(up, 0.0, 1.0);

        // Only the upper half of the travel is used.
        const double threshold = 0.5;
        if (up <= threshold) return 0;

        double scaled = (up - threshold) / (1.0 - threshold); // 0..1
        return ClampAxis((int)Math.Round(scaled * 32768.0));
    }

    private static int AxisFromDigitalButton(bool pressed) => pressed ? 32768 : 0;

    private static int PovFromDpad(GamepadButtons buttons)
    {
        bool up = buttons.HasFlag(GamepadButtons.DPadUp);
        bool down = buttons.HasFlag(GamepadButtons.DPadDown);
        bool left = buttons.HasFlag(GamepadButtons.DPadLeft);
        bool right = buttons.HasFlag(GamepadButtons.DPadRight);

        if (!up && !down && !left && !right) return -1;

        // vJoy POV contínuo: 0=up, 9000=right, 18000=down, 27000=left
        if (up && right) return 4500;
        if (right && down) return 13500;
        if (down && left) return 22500;
        if (left && up) return 31500;
        if (up) return 0;
        if (right) return 9000;
        if (down) return 18000;
        return 27000;
    }
}

[Flags]
public enum WheelButtons : uint
{
    None = 0,

    Button1 = 1u << 0,
    Button2 = 1u << 1,
    Button3 = 1u << 2,
    Button4 = 1u << 3,
    Button5 = 1u << 4,
    Button6 = 1u << 5,
    Button7 = 1u << 6,
    Button8 = 1u << 7,
    Button9 = 1u << 8,
    Button10 = 1u << 9,
    Button11 = 1u << 10,
    Button12 = 1u << 11,

    Button13 = 1u << 12,
    Button14 = 1u << 13,
    Button15 = 1u << 14,
    Button16 = 1u << 15,

    // Reservado: até 32 botões aqui, vJoy suporta bem mais.
}

internal static class WheelButtonMapper
{
    public static WheelButtons FromGamepad(Gamepad gamepad, bool useLbAsClutch)
    {
        WheelButtons result = WheelButtons.None;

        var buttons = gamepad.Buttons;

        // 1:1 (controle -> vJoy buttons)
        // ABXY
        if (buttons.HasFlag(GamepadButtons.A)) result |= WheelButtons.Button1;
        if (buttons.HasFlag(GamepadButtons.B)) result |= WheelButtons.Button2;
        if (buttons.HasFlag(GamepadButtons.X)) result |= WheelButtons.Button3;
        if (buttons.HasFlag(GamepadButtons.Y)) result |= WheelButtons.Button4;

        // Shoulder
        if (!useLbAsClutch && buttons.HasFlag(GamepadButtons.LeftShoulder)) result |= WheelButtons.Button5;
        if (buttons.HasFlag(GamepadButtons.RightShoulder)) result |= WheelButtons.Button6;

        // Menu/View
        if (buttons.HasFlag(GamepadButtons.Back)) result |= WheelButtons.Button7;
        if (buttons.HasFlag(GamepadButtons.Start)) result |= WheelButtons.Button8;

        // Stick press (L3/R3)
        if (buttons.HasFlag(GamepadButtons.LeftThumb)) result |= WheelButtons.Button9;
        if (buttons.HasFlag(GamepadButtons.RightThumb)) result |= WheelButtons.Button10;

        // D-Pad (também é enviado como POV em VirtualWheelState)
        if (buttons.HasFlag(GamepadButtons.DPadUp)) result |= WheelButtons.Button11;
        if (buttons.HasFlag(GamepadButtons.DPadRight)) result |= WheelButtons.Button12;
        if (buttons.HasFlag(GamepadButtons.DPadDown)) result |= WheelButtons.Button13;
        if (buttons.HasFlag(GamepadButtons.DPadLeft)) result |= WheelButtons.Button14;

        return result;
    }
}
