using System;
using System.Diagnostics;
using System.Threading;

namespace DriftCore.Services.InputProcessing;

/// <summary>
/// Processa steering com suavização e hold de ângulo.
/// </summary>
public sealed class InputProcessor
{
    // Deadzone único aplicado ao input (mantido centralizado aqui).
    private const double Deadzone = 0.25;
    private const double SpeedAdjust = 0.5;
    private const double MaxUnitsPerSecond = 1.5;

    private bool _smoothingEnabled;
    private int _smoothingValue;

    private double _steeringPosition;
    private double _smoothedInput;
    private long _lastTimestamp;

    public void UpdateSmoothing(bool enabled, int value)
    {
        Volatile.Write(ref _smoothingEnabled, enabled);
        Volatile.Write(ref _smoothingValue, Math.Clamp(value, 0, 100));

        if (!enabled)
        {
            _smoothedInput = 0;
        }
    }

    public double ProcessSteering(double input)
    {
        // Input é velocidade (não posição): -1..0 vira para esquerda, 0..1 para direita.
        // Ao soltar e voltar para 0, para de mover e mantém o ângulo atual.

        input = Math.Clamp(input, -1.0, 1.0);

        // Deadzone para evitar drift perto do zero.
        var absInput = Math.Abs(input);
        if (absInput <= Deadzone)
        {
            input = 0.0;
        }
        else
        {
            // Remapeia para manter resposta linear fora da deadzone.
            var scaled = (absInput - Deadzone) / (1.0 - Deadzone);
            input = Math.Sign(input) * Math.Clamp(scaled, 0.0, 1.0);
        }

        // Suavização opcional do input (passthrough por enquanto).
        if (Volatile.Read(ref _smoothingEnabled))
        {
            _ = Volatile.Read(ref _smoothingValue);
            // Intencionalmente sem aplicar smoothing no input neste momento.
        }

        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref _lastTimestamp);
        if (last == 0)
        {
            Volatile.Write(ref _lastTimestamp, now);
            return _steeringPosition;
        }

        Volatile.Write(ref _lastTimestamp, now);

        var deltaTicks = now - last;
        if (deltaTicks <= 0)
        {
            return _steeringPosition;
        }

        var dtSeconds = deltaTicks / (double)Stopwatch.Frequency;

        // Ajuste global de velocidade do giro (0..1).
        var speedAdjust = Math.Clamp(SpeedAdjust, 0.0, 1.0);

        var newPosition = _steeringPosition + (input * MaxUnitsPerSecond * speedAdjust * dtSeconds);
        _steeringPosition = Math.Clamp(newPosition, -1.0, 1.0);

        // Output linear: posição normalizada atual do volante.
        return _steeringPosition;
    }

    /// <summary>
    /// Reseta o estado do processador (útil em reconexões).
    /// </summary>
    public void Reset()
    {
        _steeringPosition = 0;
        _smoothedInput = 0;
        Volatile.Write(ref _lastTimestamp, 0);
    }
}
