namespace DriftCore.Infrastructure;

/// <summary>
/// Gerencia shutdown gracioso e forçado da aplicação.
/// </summary>
public sealed class ShutdownManager
{
    private readonly Action _stopApplication;
    private readonly Func<TimeSpan> _shutdownTimeout;
    private int _isShuttingDown;

    public bool IsShuttingDown => Volatile.Read(ref _isShuttingDown) != 0;

    public ShutdownManager(Action stopApplication, Func<TimeSpan> shutdownTimeout)
    {
        _stopApplication = stopApplication;
        _shutdownTimeout = shutdownTimeout;
    }

    /// <summary>
    /// Inicia shutdown gracioso.
    /// </summary>
    public void RequestShutdown(string reason)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0) return;

        Console.WriteLine($"[Shutdown] {reason}");

        _stopApplication();
    }

    /// <summary>
    /// Inicia shutdown com timeout forçado.
    /// </summary>
    public void RequestForcedShutdown(string reason)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0) return;

        Console.WriteLine($"[Shutdown] {reason}");
        StartForceExitTimer();

        _stopApplication();
    }

    private void StartForceExitTimer()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(_shutdownTimeout());
            Console.WriteLine("[Shutdown] Timeout. Forçando encerramento...");
            Environment.Exit(0);
        });
    }
}
