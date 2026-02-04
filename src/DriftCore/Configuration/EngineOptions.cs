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

    /// <summary>
    /// Se true, usa o receiver Xbox 360 Wireless via WinUSB (WinUSBNet) ao invés de XInput.
    /// Requer que o dispositivo esteja com driver WinUSB e um Device Interface GUID configurado.
    /// </summary>
    public bool UseWinUsbReceiver { get; set; } = false;

    /// <summary>
    /// Device Interface GUID (definido no .inf do WinUSB) para enumerar o receiver.
    /// Ex: "{BB9176E8-924F-4A7E-963A-6DC6A4E87FC2}".
    /// Se vazio, a engine cai para o GUID genérico de dispositivos USB e filtra por VID/PID.
    /// </summary>
    public string WinUsbDeviceInterfaceGuid { get; set; } = "";

    /// <summary>
    /// Timeout de leitura do pipe (ms). 0 = sem timeout.
    /// </summary>
    public int WinUsbReadTimeoutMs { get; set; } = 20;

}
