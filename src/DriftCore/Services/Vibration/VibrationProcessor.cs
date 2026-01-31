using DriftCore.Services.Input;

namespace DriftCore.Services.Vibration;

/// <summary>
/// Processa e repassa vibração do jogo para o controle físico.
/// </summary>
public class VibrationProcessor
{
    private readonly bool _testMode;

    public VibrationProcessor(bool testMode = false)
    {
        _testMode = testMode;
    }

    /// <summary>
    /// Processa a vibração recebida do jogo e envia para o controle físico.
    /// </summary>
    public void Process(int gamepadIndex, byte largeMotor, byte smallMotor)
    {
        if (gamepadIndex < 0 || gamepadIndex > 3)
            return;

        // Amplifica e converte de 0-255 para 0-65535
        ushort largeOut = CalculateOutput(largeMotor);
        ushort smallOut = CalculateOutput(smallMotor);

        GamepadReader.SetVibration(gamepadIndex, largeOut, smallOut);

        if (_testMode && (largeMotor > 0 || smallMotor > 0))
        {
            Console.WriteLine($"[Vibration] In: L={largeMotor} S={smallMotor} | Out: L={largeOut} S={smallOut}");
        }
    }

    private static ushort CalculateOutput(byte input)
    {
        const int boost = 5;
        // 257 converte 0-255 para 0-65535 (257 * 255 = 65535)
        int result = input * 257 * boost;
        return (ushort)Math.Min(result, ushort.MaxValue);
    }
}
