using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;

namespace DriftCore.Services.ForceFeedback;

public sealed class ForceFeedbackService : IDisposable
{
    private readonly object _gate = new();

    private uint _deviceId;
    private bool _started;

    private VJoyFfbNative.FfbGenCbDelegate? _callback;
    private bool _callbackRegistered;

    private GCHandle? _selfHandle;
    private double _latestNormalizedForce;
    private long _lastForceUpdateTicks;

    private long _lastSpikeLogTicks;

    public double LatestNormalizedForce
    {
        get
        {
            var last = Volatile.Read(ref _lastForceUpdateTicks);
            if (last == 0)
                return 0d;

            // If we stop receiving valid instantaneous force updates, don't keep applying
            // the last value forever.
            const double timeoutSeconds = 0.10;
            var ageTicks = Stopwatch.GetTimestamp() - last;
            if (ageTicks > (long)(Stopwatch.Frequency * timeoutSeconds))
                return 0d;

            return Volatile.Read(ref _latestNormalizedForce);
        }
    }

    public void EnsureStarted(uint deviceId)
    {
        lock (_gate)
        {
            deviceId = Math.Clamp(deviceId, 1u, 16u);

            if (_started && _deviceId == deviceId)
                return;

            StopInternal();

            _deviceId = deviceId;

            EnsureCallbackRegistered();

            try
            {
                // Best-effort. Some vJoy installs may not support FFB.
                var ok = VJoyFfbNative.FfbStart(_deviceId);
                _started = ok;

                if (ok)
                    Console.WriteLine($"[FFB] Started (vJoy DeviceId={_deviceId}).");
                else
                    Console.WriteLine($"[FFB] Failed to start (vJoy DeviceId={_deviceId}).");
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("[FFB] vJoyInterface.dll not found (FFB unavailable).");
            }
            catch (EntryPointNotFoundException)
            {
                Console.WriteLine("[FFB] vJoyInterface.dll has no FFB exports (FFB unavailable).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FFB] Start error: {ex.Message}");
            }
        }
    }

    private void EnsureCallbackRegistered()
    {
        if (_callbackRegistered)
            return;

        _callback = OnFfbPacket;
        if (!_selfHandle.HasValue)
            _selfHandle = GCHandle.Alloc(this);
        var userData = GCHandle.ToIntPtr(_selfHandle.Value);

        try
        {
            VJoyFfbNative.FfbRegisterGenCB(_callback, userData);
            _callbackRegistered = true;
            Console.WriteLine("[FFB] Callback registered.");
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[FFB] vJoyInterface.dll not found (FFB unavailable).");
        }
        catch (EntryPointNotFoundException)
        {
            Console.WriteLine("[FFB] vJoyInterface.dll has no FFB exports (FFB unavailable).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFB] Callback register error: {ex.Message}");
        }
    }

    private static void OnFfbPacket(IntPtr ffbDataPtr, IntPtr userData)
    {
        try
        {
            if (userData == IntPtr.Zero)
                return;

            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is not ForceFeedbackService service)
                return;

            if (VJoyFfbInterop.TryGetSignedNormalizedForce(ffbDataPtr, out var normalized, out var kind))
            {
                service.MaybeLogSpike(ffbDataPtr, normalized, kind);
                service.UpdateLatestForce(normalized);
            }
        }
        catch (Exception ex)
        {
            // Never throw across native callback boundary.
            Console.WriteLine($"[FFB] Callback error: {ex.Message}");
        }
    }

    private void MaybeLogSpike(IntPtr ffbDataPtr, double normalized, string kind)
    {
        if (Math.Abs(normalized) < 0.999)
            return;

        // Throttle to avoid flooding (native callback can be high frequency).
        var now = Stopwatch.GetTimestamp();
        var last = Volatile.Read(ref _lastSpikeLogTicks);
        if (now - last < Stopwatch.Frequency / 2)
            return;
        Volatile.Write(ref _lastSpikeLogTicks, now);

        byte reportId = 0;
        int len = 0;
        if (VJoyFfbInterop.TryCopyDataBytes(ffbDataPtr, out _, out var data) && data.Length > 0)
        {
            reportId = data[0];
            len = data.Length;
        }

        Console.WriteLine($"[FFB] Spike |norm|={Math.Abs(normalized):0.000} kind={kind} reportId=0x{reportId:X2} len={len}");
    }

    private void UpdateLatestForce(double normalized)
    {
        Volatile.Write(ref _lastForceUpdateTicks, Stopwatch.GetTimestamp());
        Volatile.Write(ref _latestNormalizedForce, normalized);
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        if (!_started)
            return;

        try
        {
            VJoyFfbNative.FfbStop(_deviceId);
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            _started = false;
        }
    }

    public void Dispose()
    {
        Stop();
        if (_selfHandle.HasValue)
        {
            _selfHandle.Value.Free();
            _selfHandle = null;
        }
    }
}

internal static class VJoyFfbNative
{
    private const string DllName = "vJoyInterface.dll";

    // vJoy uses Cdecl for its public API.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FfbGenCbDelegate(IntPtr ffbData, IntPtr userData);

    // Registers a generic force feedback callback.
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "FfbRegisterGenCB")]
    public static extern void FfbRegisterGenCB(FfbGenCbDelegate cb, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "FfbStart")]
    public static extern bool FfbStart(uint rID);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "FfbStop")]
    public static extern void FfbStop(uint rID);
}
