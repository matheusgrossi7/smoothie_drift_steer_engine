using Vortice.XInput;

namespace DriftCore.Services.Input;

/// <summary>
/// Lê o estado de gamepads XInput conectados ao sistema.
/// </summary>
public static class GamepadReader
{
    private const int MaxGamepadIndex = 3;

    public static GamepadReadResult Read(int index)
    {
        if (!IsValidIndex(index)) return GamepadReadResult.Disconnected;
        if (!XInput.GetState((uint)index, out var state)) return GamepadReadResult.Disconnected;

        // Deadzone é aplicada no InputProcessor para manter a lógica centralizada.
        double steering = NormalizeAxis(state.Gamepad.LeftThumbX);

        return new GamepadReadResult(
            isConnected: true,
            steering: steering,
            gamepad: state.Gamepad
        );
    }

    public static bool IsConnected(int index)
    {
        return IsValidIndex(index) && XInput.GetState((uint)index, out _);
    }

    public static void SetVibration(int index, ushort largeMotor, ushort smallMotor)
    {
        if (!IsValidIndex(index)) return;
        XInput.SetVibration((uint)index, new Vortice.XInput.Vibration(largeMotor, smallMotor));
    }

    private static bool IsValidIndex(int index) => index >= 0 && index <= MaxGamepadIndex;
    private static double NormalizeAxis(short value)
    {
        // XInput usa [-32768..32767]. Normaliza para [-1..1] sem assimetria.
        double normalized = value < 0 ? value / 32768.0 : value / 32767.0;
        return Math.Clamp(normalized, -1.0, 1.0);
    }
}

/// <summary>
/// Resultado imutável da leitura de um gamepad.
/// </summary>
public readonly struct GamepadReadResult
{
    public bool IsConnected { get; }
    public double Steering { get; }
    public Gamepad Gamepad { get; }

    public GamepadReadResult(bool isConnected, double steering, Gamepad gamepad)
    {
        IsConnected = isConnected;
        Steering = steering;
        Gamepad = gamepad;
    }

    public static GamepadReadResult Disconnected => new(false, 0, default);
}
