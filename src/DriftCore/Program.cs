using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Services;
using System.Text.Json;

// Modo de teste
bool isTestMode = args.Contains("--test");

var cts = new CancellationTokenSource();
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
}
catch (OperationCanceledException)
{
    // Shutdown/cancel normal.
}

EngineOptions LoadOptions()
{
    var path = Path.Combine(AppContext.BaseDirectory, "appSettings.json");
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
        Console.WriteLine($"[Config] Falha ao ler appSettings.json: {ex.Message}");
        return new EngineOptions();
    }
}

sealed class AppSettings
{
    public EngineOptions DriftEngine { get; set; } = new();
}

