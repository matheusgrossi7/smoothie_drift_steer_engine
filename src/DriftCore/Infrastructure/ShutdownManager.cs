namespace DriftCore.Infrastructure;

/// <summary>
/// Coordinates graceful and forced application shutdown.
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
    /// Requests a graceful shutdown.
    /// </summary>
    public void RequestShutdown(string reason)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0) return;

        Console.WriteLine($"[Shutdown] {reason}");

        _stopApplication();
    }

    /// <summary>
    /// Requests shutdown and schedules a forced exit after the configured timeout.
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
            Console.WriteLine("[Shutdown] Timeout reached. Forcing exit...");
            Environment.Exit(0);
        });
    }
}
