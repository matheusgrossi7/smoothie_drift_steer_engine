using Vortice.XInput;

namespace DriftCore.Services.VirtualWheel;

/// <summary>
/// Immutable virtual wheel state (vJoy) to be sent to the game.
/// </summary>
/// <remarks>
/// vJoy axes typically use the 0..32768 range.
/// Steering uses X (0=left, 16384=center, 32768=right).
/// </remarks>
public readonly struct VirtualWheelState
{
    public int SteeringX { get; }
    public int BrakeY { get; }
    public int ThrottleZ { get; }
    public int Rx { get; }

    /// <summary>
    /// Continuous POV hat in hundredths of a degree (0..35900), or -1 for neutral.
    /// </summary>
    public int Pov1 { get; }

    public WheelButtons Buttons { get; }

    private VirtualWheelState(int steeringX, int brakeY, int throttleZ, int rx, int pov1, WheelButtons buttons)
    {
        SteeringX = ClampAxis(steeringX);
        BrakeY = ClampAxis(brakeY);
        ThrottleZ = ClampAxis(throttleZ);
        Rx = ClampAxis(rx);
        Pov1 = pov1;
        Buttons = buttons;
    }

    public static VirtualWheelState FromGamepad(Gamepad gamepad, double steeringNormalized)
    {
        // Steering: -1..1 -> 0..32768
        var steeringX = AxisFromSignedNormalized(steeringNormalized);

        // Pedais: 0..255 -> 0..32768
        var brakeY = AxisFromUnsignedByte(gamepad.LeftTrigger);
        var throttleZ = AxisFromUnsignedByte(gamepad.RightTrigger);

        // vJoy Rx: maps to the right stick X axis (horizontal), with deadzone.
        const double rxDeadzone = 0.4;
        var rx = AxisFromSignedShortWithDeadzone(gamepad.RightThumbX, rxDeadzone);

        // D-Pad -> continuous POV (degrees * 100)
        int pov1 = PovFromDpad(gamepad.Buttons);

        var buttons = WheelButtonMapper.FromGamepad(gamepad);
        return new VirtualWheelState(steeringX, brakeY, throttleZ, rx, pov1, buttons);
    }

    public static VirtualWheelState Empty => new(16384, 0, 0, 16384, -1, WheelButtons.None);

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

    private static int AxisFromSignedShort(short value)
    {
        // value: [-32768..32767] => normalized [-1..1] => [0..32768]
        return AxisFromSignedNormalized(value / 32767.0);
    }

    private static int AxisFromSignedShortWithDeadzone(short value, double deadzone)
    {
        deadzone = Math.Clamp(deadzone, 0.0, 0.99);

        double normalized = value / 32767.0;
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        double abs = Math.Abs(normalized);
        if (abs <= deadzone) return 16384;

        // Re-scale remaining range so output still reaches full travel.
        double scaled = (abs - deadzone) / (1.0 - deadzone);
        scaled = Math.Clamp(scaled, 0.0, 1.0);
        double signed = Math.CopySign(scaled, normalized);
        return AxisFromSignedNormalized(signed);
    }

    private static int PovFromDpad(GamepadButtons buttons)
    {
        bool up = buttons.HasFlag(GamepadButtons.DPadUp);
        bool down = buttons.HasFlag(GamepadButtons.DPadDown);
        bool left = buttons.HasFlag(GamepadButtons.DPadLeft);
        bool right = buttons.HasFlag(GamepadButtons.DPadRight);

        if (!up && !down && !left && !right) return -1;

        // vJoy continuous POV: 0=up, 9000=right, 18000=down, 27000=left
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

    // Reserved: define up to 32 buttons here (vJoy supports many more).
}

internal static class WheelButtonMapper
{
    public static WheelButtons FromGamepad(Gamepad gamepad)
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
        if (buttons.HasFlag(GamepadButtons.LeftShoulder)) result |= WheelButtons.Button5;
        if (buttons.HasFlag(GamepadButtons.RightShoulder)) result |= WheelButtons.Button6;

        // Menu/View
        if (buttons.HasFlag(GamepadButtons.Back)) result |= WheelButtons.Button7;
        if (buttons.HasFlag(GamepadButtons.Start)) result |= WheelButtons.Button8;

        // Stick press (L3/R3)
        if (buttons.HasFlag(GamepadButtons.LeftThumb)) result |= WheelButtons.Button9;
        if (buttons.HasFlag(GamepadButtons.RightThumb)) result |= WheelButtons.Button10;

        // D-Pad (also sent as POV in VirtualWheelState)
        if (buttons.HasFlag(GamepadButtons.DPadUp)) result |= WheelButtons.Button11;
        if (buttons.HasFlag(GamepadButtons.DPadRight)) result |= WheelButtons.Button12;
        if (buttons.HasFlag(GamepadButtons.DPadDown)) result |= WheelButtons.Button13;
        if (buttons.HasFlag(GamepadButtons.DPadLeft)) result |= WheelButtons.Button14;

        return result;
    }
}
