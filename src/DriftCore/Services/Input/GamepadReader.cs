using Vortice.XInput;

namespace DriftCore.Services.Input;

/// <summary>
/// Lê o estado de gamepads XInput conectados ao sistema.
/// </summary>
public static class GamepadReader
{
    private const double DEADZONE = 0.15;

    /// <summary>
    /// Lê o estado completo de um gamepad.
    /// </summary>
    public static GamepadReadResult Read(int gamepadIndex)
    {
        if (gamepadIndex < 0 || gamepadIndex > 3)
            return GamepadReadResult.NotConnected;

        if (!XInput.GetState((uint)gamepadIndex, out var state))
            return GamepadReadResult.NotConnected;

        double steering = NormalizeAxis(state.Gamepad.LeftThumbX);

        return new GamepadReadResult
        {
            IsConnected = true,
            Steering = ApplyDeadzone(steering),
            Gamepad = state.Gamepad
        };
    }

    /// <summary>
    /// Verifica se um gamepad está conectado.
    /// </summary>
    public static bool IsConnected(int gamepadIndex)
    {
        if (gamepadIndex < 0 || gamepadIndex > 3)
            return false;

        return XInput.GetState((uint)gamepadIndex, out _);
    }

    /// <summary>
    /// Envia vibração para um gamepad.
    /// </summary>
    public static void SetVibration(int gamepadIndex, ushort largeMotor, ushort smallMotor)
    {
        if (gamepadIndex < 0 || gamepadIndex > 3)
            return;

        var vibration = new Vortice.XInput.Vibration(largeMotor, smallMotor);
        XInput.SetVibration((uint)gamepadIndex, vibration);
    }

    private static double NormalizeAxis(short value) => value / 32768.0;

    private static double ApplyDeadzone(double value) =>
        Math.Abs(value) < DEADZONE ? 0 : value;
}

/// <summary>
/// Resultado da leitura de um gamepad.
/// </summary>
public struct GamepadReadResult
{
    public bool IsConnected;
    public double Steering;
    public Gamepad Gamepad;

    public static GamepadReadResult NotConnected => new() { IsConnected = false };
}
