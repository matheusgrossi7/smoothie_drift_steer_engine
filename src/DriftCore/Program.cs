using DriftCore.Configuration;
using DriftCore.Infrastructure;
using DriftCore.Models;
using DriftCore.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Registrar Engine
builder.Services.AddSingleton<DriftEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriftEngine>());

var app = builder.Build();

// Modo de teste
bool isTestMode = args.Contains("--test");

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
    app.Lifetime.StopApplication();
    StartForceExitTimer();
};

app.Run("http://localhost:5000");

void StartForceExitTimer()
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(EngineSettings.ShutdownTimeout);
        Console.WriteLine("[Shutdown] Timeout. Forçando encerramento...");
        Environment.Exit(0);
    });
}