using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Services;
using System.Text.Json;

// Entry point for the Drift engine.
// Use `--test` to keep the console visible and print IO logs.
var isTestMode = args.Contains("--test", StringComparer.OrdinalIgnoreCase);

using var cts = new CancellationTokenSource();
var engineOptions = LoadOptions();
var engine = new DriftEngine(engineOptions, () => cts.Cancel());

if (OperatingSystem.IsWindows())
{
    if (isTestMode)
        ConsoleManager.ShowTestModeBanner();
    else
        ConsoleManager.HideConsole();
}

engine.SetTestMode(isTestMode);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;

    try
    {
        engine.ForceShutdown("Ctrl+C");
    }
    catch (ObjectDisposedException)
    {
        // Pode acontecer se o Ctrl+C chegar durante/apos o disposal do host.
    }
};

try
{
    await engine.RunAsync(cts.Token);
}
catch (TaskCanceledException)
{
    // Cancelamento normal.
    Environment.ExitCode = 0;
}
catch (OperationCanceledException)
{
    // Shutdown/cancel normal.
    Environment.ExitCode = 0;
}

EngineOptions LoadOptions()
{
    const string appSettingsFileName = "appsettings.json";
    var path = Path.Combine(AppContext.BaseDirectory, appSettingsFileName);
    if (!File.Exists(path))
        return new EngineOptions();

    try
    {
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return settings?.DriftEngine ?? new EngineOptions();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Config] Failed to read {appSettingsFileName}: {ex.Message}");
        return new EngineOptions();
    }
}

sealed class AppSettings
{
    public EngineOptions DriftEngine { get; set; } = new();
}

