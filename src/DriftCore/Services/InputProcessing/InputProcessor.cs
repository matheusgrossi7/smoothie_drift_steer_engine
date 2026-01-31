using System.Threading;

namespace DriftCore.Services.InputProcessing;

/// <summary>
/// Processa steering com suavização e hold de ângulo.
/// </summary>
public sealed class InputProcessor
{
    private const double HoldThreshold = 0.05;
    private const double MinAlpha = 0.02;

    private bool _smoothingEnabled;
    private int _smoothingValue;
    private double _lastOutput;

    public void UpdateSmoothing(bool enabled, int value)
    {
        Volatile.Write(ref _smoothingEnabled, enabled);
        Volatile.Write(ref _smoothingValue, Math.Clamp(value, 0, 100));
    }

    public double ProcessSteering(double input)
    {
        bool enabled = Volatile.Read(ref _smoothingEnabled);
        int value = Volatile.Read(ref _smoothingValue);

        if (!enabled)
        {
            _lastOutput = Clamp(input);
            return _lastOutput;
        }

        double clamped = Clamp(input);

        // Hold: se soltou o stick, mantém último ângulo
        if (Math.Abs(clamped) <= HoldThreshold)
            clamped = _lastOutput;

        // Filtro exponencial
        double alpha = CalculateAlpha(value);
        _lastOutput = _lastOutput + alpha * (clamped - _lastOutput);
        _lastOutput = Clamp(_lastOutput);

        return _lastOutput;
    }

    private static double CalculateAlpha(int smoothingValue)
    {
        // 0 = resposta imediata, 100 = máxima suavização
        double t = smoothingValue / 100.0;
        return Math.Clamp(1.0 - t, MinAlpha, 1.0);
    }

    private static double Clamp(double v) => v > 1.0 ? 1.0 : v < -1.0 ? -1.0 : v;
}
