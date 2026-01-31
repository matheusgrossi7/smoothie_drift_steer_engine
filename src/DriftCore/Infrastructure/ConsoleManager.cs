using System.Runtime.InteropServices;

namespace DriftCore.Infrastructure;

/// <summary>
/// Gerencia visibilidade do console no Windows.
/// </summary>
public static class ConsoleManager
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;

    public static void HideConsole()
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
            ShowWindow(handle, SW_HIDE);
    }

    public static void ShowTestModeBanner()
    {
        Console.WriteLine("=== MODO DE TESTE ATIVADO ===");
        Console.WriteLine("Logs de input serão exibidos no console.");
    }
}
