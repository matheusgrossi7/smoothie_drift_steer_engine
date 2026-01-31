using System.Threading;

namespace DriftCore.Services.InputProcessing;

/// <summary>
/// Processa steering com suavização e hold de ângulo.
/// </summary>
public sealed class InputProcessor
{
    private bool _smoothingEnabled;
    private int _smoothingValue;

    public void UpdateSmoothing(bool enabled, int value)
    {
        Volatile.Write(ref _smoothingEnabled, enabled);
        Volatile.Write(ref _smoothingValue, Math.Clamp(value, 0, 100));
    }

    public double ProcessSteering(double input)
    {
        return input;
    }


}
