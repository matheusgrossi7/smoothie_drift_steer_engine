using DriftCore.Configuration;
using DriftCore.Services.Input;

namespace DriftCore.Services.Vibration;

/// <summary>
/// Processa e repassa vibração do jogo para o controle físico.
/// </summary>
public sealed class VibrationProcessor
{
    private readonly bool _testMode;

    public VibrationProcessor(bool testMode = false)
    {
        _testMode = testMode;
    }

    public void Process(int gamepadIndex, byte largeMotor, byte smallMotor)
    {
        if (gamepadIndex is < 0 or > 3) return;

        ushort largeOut = Amplify(largeMotor);
        ushort smallOut = Amplify(smallMotor);

        GamepadReader.SetVibration(gamepadIndex, largeOut, smallOut);

        if (_testMode && (largeMotor > 0 || smallMotor > 0))
            Console.WriteLine($"[Vibration] In: L={largeMotor} S={smallMotor} | Out: L={largeOut} S={smallOut}");
    }

    private static ushort Amplify(byte input)
    {
        // 257 * 255 = 65535 (conversão exata 8-bit -> 16-bit)
        int result = input * 257 * EngineSettings.VibrationBoost;
        return (ushort)Math.Min(result, ushort.MaxValue);
    }
}
