using System.Runtime.InteropServices;
using DriftCore.Models;
using DriftCore.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Registrar o Engine como Serviço Único ---
// Ele vai rodar em background enquanto a API atende requisições
builder.Services.AddSingleton<DriftEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriftEngine>());

var app = builder.Build();

// --- 2. Controle de Visibilidade (Invisível vs Teste) ---
// Se rodar "dotnet run --test", mostra o console. Se não, esconde.
bool isTestMode = args.Contains("--test");

if (!isTestMode)
{
    // Código nativo do Windows para esconder a janela do Console
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    const int SW_HIDE = 0;

    var handle = GetConsoleWindow();
    if (handle != IntPtr.Zero) ShowWindow(handle, SW_HIDE);
}
else
{
    Console.WriteLine("=== MODO DE TESTE ATIVADO ===");
    Console.WriteLine("Logs de input serão exibidos no console.");
}

// --- 3. Endpoints da API (Comunicação com Flutter) ---

// Heartbeat: O Flutter chama isso a cada 10s para manter a engine viva.
app.MapGet("/api/heartbeat", (DriftEngine engine) =>
{
    engine.RegisterHeartbeat();
    return Results.Ok("Alive");
});

// Config: O Flutter manda as configurações (Input, Jogo, etc)
app.MapPost("/api/config", ([FromBody] DriftConfig config, DriftEngine engine) =>
{
    engine.UpdateConfig(config);
    return Results.Ok();
});

// Status: O Flutter pede o estado atual (Inputs, Jogo Conectado, etc)
app.MapGet("/api/status", (DriftEngine engine) =>
{
    return Results.Ok(engine.GetStatus());
});

// Inicializa o Engine no modo teste se a flag estiver presente
var engine = app.Services.GetRequiredService<DriftEngine>();
engine.SetTestMode(isTestMode);

// Desligamento limpo ao encerrar a aplicação (inclui Ctrl+C)
app.Lifetime.ApplicationStopping.Register(engine.Shutdown);

// Fallback: garante finalização total do processo
app.Lifetime.ApplicationStopped.Register(() =>
{
    Environment.Exit(0);
});

// Garantia de Ctrl+C mesmo em modo teste
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    app.Lifetime.StopApplication();

    // Timeout forçado: se não encerrar em 3s, mata o processo
    Task.Run(async () =>
    {
        await Task.Delay(3000);
        Console.WriteLine("[Engine] Timeout de shutdown. Forçando encerramento...");
        Environment.Exit(0);
    });
};

app.Run("http://localhost:5000");