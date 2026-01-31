namespace DriftCore.Services.InputProcessing;

/// <summary>
/// Processa o input do gamepad (camada para futura lógica de manter ângulo).
/// </summary>
public sealed class InputProcessor
{
    private bool _isSmoothingEnabled;
    private int _smoothingValue;

    /// <summary>
    /// Atualiza parâmetros de smoothness vindos da API.
    /// </summary>
    public void UpdateSmoothing(bool isEnabled, int smoothingValue)
    {
        _isSmoothingEnabled = isEnabled;
        _smoothingValue = smoothingValue;
    }

    /// <summary>
    /// Processa o steering já normalizado (-1 a 1).
    /// </summary>
    public double ProcessSteering(double steering)
    {
        // Placeholder: passthrough até implementar a lógica de manter ângulo.
        _ = _isSmoothingEnabled;
        _ = _smoothingValue;
        return steering;
    }
}
