using System.Runtime.InteropServices;

namespace DriftCore.Infrastructure;

/// <summary>
/// Manages console window visibility on Windows.
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
        Console.WriteLine("=== TEST MODE ENABLED ===");
        Console.WriteLine("Input logs will be printed to the console.");
    }
}
