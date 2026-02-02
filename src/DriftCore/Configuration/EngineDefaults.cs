namespace DriftCore.Configuration;

/// <summary>
/// Defaults internos da engine (não configuráveis via appSettings).
/// </summary>
public static class EngineDefaults
{
    /// <summary>
    /// Indica se o monitoramento de heartbeat está ativo por padrão.
    /// Quando true, a engine entra em modo "monitorado":
    /// - é esperado que chamadas periódicas a <c>RegisterHeartbeat()</c> aconteçam;
    /// - se o tempo sem heartbeat ultrapassar <see cref="HeartbeatTimeout"/>,
    ///   a engine solicita shutdown forçado.
    /// Quando false, o heartbeat é ignorado e a engine não desliga por expiração.
    /// </summary>
    public const bool HeartbeatEnabled = false;
    /// <summary>
    /// Tempo máximo permitido sem receber heartbeat antes de considerar expiração.
    /// Usado somente quando <see cref="HeartbeatEnabled"/> está ativo.
    /// </summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);
    /// <summary>
    /// Tempo máximo de espera antes de forçar encerramento no shutdown.
    /// </summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    /// <summary>
    /// Timeout máximo para <c>StopAsync</c> do host antes de encerrar.
    /// </summary>
    public static readonly TimeSpan StopAsyncTimeout = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Intervalo entre tentativas de reconexão do driver vJoy.
    /// </summary>
    public static readonly TimeSpan DriverRetryInterval = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Delay aplicado quando o vJoy está desconectado, para evitar busy-loop.
    /// </summary>
    public static readonly TimeSpan DisconnectedDelay = TimeSpan.FromMilliseconds(200);
    /// <summary>
    /// A cada quantos frames o log de debug deve ser exibido no modo de teste.
    /// </summary>
    public const int DebugLogInterval = 50;
}
