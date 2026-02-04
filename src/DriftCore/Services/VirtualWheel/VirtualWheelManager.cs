using System.Reflection;
using System.Runtime.InteropServices;

namespace DriftCore.Services.VirtualWheel;

/// <summary>
/// Manages a virtual wheel via vJoy (DirectInput).
/// </summary>
public sealed class VirtualWheelManager : IDisposable
{
    private bool _disposed;
    private readonly uint _deviceId;
    private bool _acquired;

    public bool IsConnected => _acquired && !_disposed;

    public VirtualWheelManager(uint deviceId = 1)
    {
        _deviceId = deviceId;
    }

    public bool Initialize()
    {
        try
        {
            Console.WriteLine($"[VirtualWheel] Initializing vJoy (DeviceId={_deviceId})...");

            if (!VJoyNative.vJoyEnabled())
            {
                Console.WriteLine("[ERROR] vJoy is not enabled. Install/enable the vJoy driver.");
                return false;
            }

            var status = VJoyNative.GetVJDStatus(_deviceId);
            if (status is VjdStat.VJD_STAT_MISS)
            {
                Console.WriteLine("[ERROR] vJoy device not found (VJD_STAT_MISS). Configure a device in vJoyConf.");
                return false;
            }

            if (status is VjdStat.VJD_STAT_BUSY)
            {
                Console.WriteLine("[ERROR] vJoy device is busy (VJD_STAT_BUSY).");
                return false;
            }

            if (!VJoyNative.AcquireVJD(_deviceId))
            {
                Console.WriteLine("[ERROR] Failed to acquire vJoy device.");
                return false;
            }

            _acquired = true;
            VJoyNative.ResetVJD(_deviceId);
            Console.WriteLine("[VirtualWheel] Connected!");
            return true;
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[ERROR] vJoyInterface.dll not found. Verify vJoy installation and x64 architecture.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] vJoy failure: {ex.Message}");
            return false;
        }
    }

    public void SendState(VirtualWheelState state)
    {
        if (!_acquired || _disposed) return;

        // Axes
        VJoyNative.SetAxis(state.SteeringX, _deviceId, HidUsage.HID_USAGE_X);
        VJoyNative.SetAxis(state.BrakeY, _deviceId, HidUsage.HID_USAGE_Y);
        VJoyNative.SetAxis(state.ThrottleZ, _deviceId, HidUsage.HID_USAGE_Z);
        VJoyNative.SetAxis(state.Rx, _deviceId, HidUsage.HID_USAGE_RX);

        // Continuous POV (1)
        VJoyNative.SetContPov(state.Pov1, _deviceId, 1);

        // Buttons (1..32)
        SetButtons(state.Buttons);
    }

    private void SetButtons(WheelButtons buttons)
    {
        uint mask = (uint)buttons;
        for (uint i = 0; i < 32; i++)
        {
            bool pressed = (mask & (1u << (int)i)) != 0;
            VJoyNative.SetBtn(pressed, _deviceId, (byte)(i + 1));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.WriteLine("[VirtualWheel] Disconnecting...");

        try
        {
            if (_acquired)
            {
                VJoyNative.ResetVJD(_deviceId);
                VJoyNative.RelinquishVJD(_deviceId);
                _acquired = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualWheel] Error: {ex.Message}");
        }

        Console.WriteLine("[VirtualWheel] Disconnected.");
    }
}

internal enum VjdStat : int
{
    VJD_STAT_OWN = 0,
    VJD_STAT_FREE = 1,
    VJD_STAT_BUSY = 2,
    VJD_STAT_MISS = 3,
    VJD_STAT_UNKN = 4,
}

internal static class HidUsage
{
    // HID usage IDs (Generic Desktop)
    public const uint HID_USAGE_X = 0x30;
    public const uint HID_USAGE_Y = 0x31;
    public const uint HID_USAGE_Z = 0x32;
    public const uint HID_USAGE_RX = 0x33;
}

internal static class VJoyNative
{
    private const string DllName = "vJoyInterface.dll";

    static VJoyNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(VJoyNative).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        // 1) Local (next to executable)
        var local = Path.Combine(AppContext.BaseDirectory, DllName);
        if (File.Exists(local))
            return NativeLibrary.Load(local);

        // 2) Common vJoy install locations
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[]
        {
            Path.Combine(programFiles, "vJoy", "x64", DllName),
            Path.Combine(programFilesX86, "vJoy", "x64", DllName),
            Path.Combine(programFiles, "vJoy", DllName),
            Path.Combine(programFilesX86, "vJoy", DllName),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return NativeLibrary.Load(path);
        }

        return IntPtr.Zero;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool vJoyEnabled();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool AcquireVJD(uint rID);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void RelinquishVJD(uint rID);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool ResetVJD(uint rID);

    [DllImport(DllName, EntryPoint = "GetVJDStatus", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetVJDStatusRaw(uint rID);

    public static VjdStat GetVJDStatus(uint rID) => (VjdStat)GetVJDStatusRaw(rID);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool SetAxis(int value, uint rID, uint axis);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool SetBtn(bool value, uint rID, byte nBtn);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool SetContPov(int value, uint rID, byte nPov);
}
