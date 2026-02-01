using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Models;
using DriftCore.Services;
using Microsoft.AspNetCore.Mvc;

// Modo de teste
bool isTestMode = args.Contains("--test");

var builder = WebApplication.CreateBuilder(args);

// Evita conflito de porta em execuções locais repetidas (especialmente em --test)
builder.WebHost.UseUrls(isTestMode ? "http://localhost:0" : "http://localhost:5000");

// Registrar Engine
builder.Services.AddSingleton<DriftEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriftEngine>());

var app = builder.Build();

if (isTestMode)
    ConsoleManager.ShowTestModeBanner();
else
    ConsoleManager.HideConsole();

// API Endpoints
app.MapGet("/api/heartbeat", (DriftEngine engine) =>
{
    engine.RegisterHeartbeat();
    return Results.Ok("Alive");
});

app.MapPost("/api/config", ([FromBody] DriftConfig config, DriftEngine engine) =>
{
    engine.UpdateConfig(config);
    return Results.Ok();
});

app.MapGet("/api/status", (DriftEngine engine) => Results.Ok(engine.GetStatus()));

// Inicialização
var engine = app.Services.GetRequiredService<DriftEngine>();
engine.SetTestMode(isTestMode);

// Shutdown handlers
app.Lifetime.ApplicationStopping.Register(engine.Shutdown);
app.Lifetime.ApplicationStopped.Register(() => Environment.Exit(0));

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;

    try
    {
        app.Lifetime.StopApplication();
    }
    catch (ObjectDisposedException)
    {
        // Pode acontecer se o Ctrl+C chegar durante/apos o disposal do host.
    }

    StartForceExitTimer();
};

try
{
    app.Run();
}
catch (TaskCanceledException)
{
    // Pode ocorrer se o host for cancelado durante o bind/start.
}
catch (OperationCanceledException)
{
    // Shutdown/cancel normal.
}

void StartForceExitTimer()
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(EngineSettings.ShutdownTimeout);
        Console.WriteLine("[Shutdown] Timeout. Forçando encerramento...");
        Environment.Exit(0);
    });
}