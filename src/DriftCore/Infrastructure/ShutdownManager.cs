using DriftCore.Configuration;

namespace DriftCore.Infrastructure;

/// <summary>
/// Gerencia shutdown gracioso e forçado da aplicação.
/// </summary>
public sealed class ShutdownManager
{
    private readonly IHostApplicationLifetime _lifetime;
    private volatile bool _isShuttingDown;

    public bool IsShuttingDown => _isShuttingDown;

    public ShutdownManager(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    /// <summary>
    /// Inicia shutdown gracioso.
    /// </summary>
    public void RequestShutdown(string reason)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        Console.WriteLine($"[Shutdown] {reason}");

        if (!_lifetime.ApplicationStopping.IsCancellationRequested)
            _lifetime.StopApplication();
    }

    /// <summary>
    /// Inicia shutdown com timeout forçado.
    /// </summary>
    public void RequestForcedShutdown(string reason)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        Console.WriteLine($"[Shutdown] {reason}");
        StartForceExitTimer();

        if (!_lifetime.ApplicationStopping.IsCancellationRequested)
            _lifetime.StopApplication();
    }

    private void StartForceExitTimer()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(EngineSettings.ShutdownTimeout);
            Console.WriteLine("[Shutdown] Timeout. Forçando encerramento...");
            Environment.Exit(0);
        });
    }
}
