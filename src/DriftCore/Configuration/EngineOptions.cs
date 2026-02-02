namespace DriftCore.Configuration;

/// <summary>
/// Configuração da engine (appSettings.json).
/// Mantém apenas parâmetros ajustáveis pelo usuário.
/// </summary>
public sealed class EngineOptions
{
    /// <summary>
    /// Índice do gamepad XInput (0-3).
    /// </summary>
    public int InputDeviceIndex { get; set; } = 0;

    /// <summary>
    /// ID do dispositivo vJoy (1-16).
    /// </summary>
    public int VJoyDeviceId { get; set; } = 1;

    /// <summary>
    /// Se true, LB vira embreagem. Caso contrário, usa o eixo Y do analógico direito.
    /// </summary>
    public bool UseLbAsClutch { get; set; } = false;

    /// <summary>
    /// Ativa suavização do input.
    /// </summary>
    public bool SmoothingEnabled { get; set; } = true;

    /// <summary>
    /// Intensidade da suavização (0-100).
    /// </summary>
    public int SmoothingValue { get; set; } = 50;

}
