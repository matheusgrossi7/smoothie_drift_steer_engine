using System;
using System.Diagnostics;
using System.Threading;

namespace DriftCore.Services.InputProcessing;

/// <summary>
/// Processa steering com suavização e hold de ângulo.
/// </summary>
public sealed class InputProcessor
{
    private bool _smoothingEnabled;
    private int _smoothingValue;

    private double _steeringPosition;
    private long _lastTimestamp;

    public void UpdateSmoothing(bool enabled, int value)
    {
        Volatile.Write(ref _smoothingEnabled, enabled);
        Volatile.Write(ref _smoothingValue, Math.Clamp(value, 0, 100));
    }

    public double ProcessSteering(double input)
    {
        // Input é velocidade (não posição): -1..0 vira para esquerda, 0..1 para direita.
        // Ao soltar e voltar para 0, para de mover e mantém o ângulo atual.

        input = Math.Clamp(input, -1.0, 1.0);

        // Deadzone para evitar drift perto do zero.
        const double deadzone = 0.25;
        var absInput = Math.Abs(input);
        if (absInput <= deadzone)
        {
            input = 0.0;
        }
        else
        {
            // Remapeia para manter resposta linear fora da deadzone.
            var scaled = (absInput - deadzone) / (1.0 - deadzone);
            input = Math.Sign(input) * Math.Clamp(scaled, 0.0, 1.0);
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

        // Variável local (0..1) para ajustar a velocidade global de giro.
        var speedAdjust = 0.1;
        speedAdjust = Math.Clamp(speedAdjust, 0.0, 1.0);

        // Taxa máxima de mudança do output (normalizado) por segundo, com input=±1.
        const double maxUnitsPerSecond = 1.5;

        var newPosition = _steeringPosition + (input * maxUnitsPerSecond * speedAdjust * dtSeconds);
        _steeringPosition = Math.Clamp(newPosition, -1.0, 1.0);

        // TODO: Ignorar deadzone FH5: output -0.25..0.25 ele seta como 0, para controle

        // Output linear: posição normalizada atual do volante.
        return _steeringPosition;
    }


}
