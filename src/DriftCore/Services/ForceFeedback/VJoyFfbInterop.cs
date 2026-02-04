using System.Runtime.InteropServices;

namespace DriftCore.Services.ForceFeedback;

internal static class VJoyFfbInterop
{
    private const string DllName = "vJoyInterface.dll";

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFB_DATA
    {
        public uint size;
        public uint cmd;
        public IntPtr data;
    }

    // Layouts match the official vJoyInterfaceCS wrapper (Explicit + FieldOffset).
    // NOTE: Effect values are typically scaled such that +/-10000 ~= full scale.
    [StructLayout(LayoutKind.Explicit)]
    internal struct FFB_EFF_CONSTANT
    {
        [FieldOffset(0)]
        public byte EffectBlockIndex;

        [FieldOffset(4)]
        public short Magnitude;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct FFB_EFF_RAMP
    {
        [FieldOffset(0)]
        public byte EffectBlockIndex;

        [FieldOffset(4)]
        public short Start;

        [FieldOffset(8)]
        public short End;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct FFB_EFF_PERIOD
    {
        [FieldOffset(0)]
        public byte EffectBlockIndex;

        [FieldOffset(4)]
        public uint Magnitude;

        [FieldOffset(8)]
        public short Offset;

        [FieldOffset(12)]
        public uint Phase;

        [FieldOffset(16)]
        public uint Period;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct FFB_EFF_REPORT
    {
        [FieldOffset(0)]
        public byte EffectBlockIndex;

        // The official wrapper uses an enum here; we only need the field size.
        [FieldOffset(4)]
        public uint EffectType;

        [FieldOffset(8)]
        public ushort Duration;

        [FieldOffset(10)]
        public ushort TrigerRpt;

        [FieldOffset(12)]
        public ushort SamplePrd;

        [FieldOffset(14)]
        public byte Gain;

        [FieldOffset(15)]
        public byte TrigerBtn;

        // Treat as byte to avoid bool marshalling quirks in Explicit layouts.
        [FieldOffset(16)]
        public byte Polar;

        [FieldOffset(20)]
        public byte Direction;

        // Same offset as Direction when Polar==0
        [FieldOffset(20)]
        public byte DirX;

        [FieldOffset(21)]
        public byte DirY;
    }

    internal enum FFBPType : uint
    {
        PT_EFFREP = 1,
        PT_ENVREP = 2,
        PT_CONDREP = 3,
        PT_PRIDREP = 4,
        PT_CONSTREP = 5,
        PT_RAMPREP = 6,
        PT_CSTMREP = 7,
        PT_SMPLREP = 8,
        PT_EFOPREP = 10,
        PT_BLKFRREP = 11,
        PT_CTRLREP = 12,
        PT_GAINREP = 13,
        PT_SETCREP = 14,
        PT_NEWEFREP = 15,
        PT_BLKLDREP = 16,
        PT_POOLREP = 17,
    }

    // vJoy helpers return DWORD (0 == ERROR_SUCCESS).
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Ffb_h_Type")]
    private static extern uint Ffb_h_Type(IntPtr packet, ref FFBPType type);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Ffb_h_Eff_Report")]
    private static extern uint Ffb_h_Eff_Report(IntPtr packet, ref FFB_EFF_REPORT effect);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Ffb_h_Eff_Constant")]
    private static extern uint Ffb_h_Eff_Constant(IntPtr packet, ref FFB_EFF_CONSTANT constantEffect);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Ffb_h_Eff_Ramp")]
    private static extern uint Ffb_h_Eff_Ramp(IntPtr packet, ref FFB_EFF_RAMP rampEffect);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Ffb_h_Eff_Period")]
    private static extern uint Ffb_h_Eff_Period(IntPtr packet, ref FFB_EFF_PERIOD periodicEffect);

    public static bool TryGetPacketHeader(IntPtr ffbDataPtr, out FFB_DATA header)
    {
        header = default;
        if (ffbDataPtr == IntPtr.Zero)
            return false;

        try
        {
            header = Marshal.PtrToStructure<FFB_DATA>(ffbDataPtr);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryCopyDataBytes(IntPtr ffbDataPtr, out FFB_DATA header, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (!TryGetPacketHeader(ffbDataPtr, out header))
            return false;

        if (header.size == 0 || header.data == IntPtr.Zero)
            return false;

        // vJoy effects are small; clamp to protect against bogus pointers.
        var size = (int)Math.Clamp(header.size, 1u, 4096u);
        var tmp = new byte[size];

        try
        {
            Marshal.Copy(header.data, tmp, 0, size);
        }
        catch
        {
            return false;
        }

        data = tmp;
        return true;
    }

    public static bool TryGetSignedNormalizedForce(IntPtr ffbDataPtr, out double normalized, out string kind)
    {
        normalized = 0d;
        kind = "";

        if (ffbDataPtr == IntPtr.Zero)
            return false;

        try
        {
            var type = default(FFBPType);
            if (Ffb_h_Type(ffbDataPtr, ref type) != 0)
                return false;

            // IMPORTANT: Only decode the effect that matches the packet type.
            // Some vJoy builds can return success for helper calls even when the packet type
            // doesn't match, yielding garbage magnitudes (often saturating to 1.0).
            var dirSign = TryGetDirectionXSign(ffbDataPtr, out var directionKind);

            switch (type)
            {
                case FFBPType.PT_CONSTREP:
                    {
                        var constantEffect = default(FFB_EFF_CONSTANT);
                        if (Ffb_h_Eff_Constant(ffbDataPtr, ref constantEffect) != 0)
                            return false;

                        normalized = NormalizeMagnitudeWithSign(constantEffect.Magnitude, dirSign);
                        kind = directionKind is null ? "Constant" : $"Constant/{directionKind}";
                        return true;
                    }

                case FFBPType.PT_RAMPREP:
                    {
                        var ramp = default(FFB_EFF_RAMP);
                        if (Ffb_h_Eff_Ramp(ffbDataPtr, ref ramp) != 0)
                            return false;

                        var value = ramp.End != 0 ? ramp.End : ramp.Start;
                        normalized = NormalizeMagnitudeWithSign(value, dirSign);
                        kind = directionKind is null ? "Ramp" : $"Ramp/{directionKind}";
                        return true;
                    }
                default:
                    kind = type.ToString();
                    break;
            }

            // NOTE:
            // - PT_PRIDREP (SetPeriodic) and PT_CONDREP (SetCondition: Spring/Damper/Friction)
            //   are effect *parameter* updates, not instantaneous force samples.
            // - Treating their magnitude/coefficients as a direct force causes hard bias
            //   spikes (e.g. +1.0) and "ghost drift".
            // If we want periodic/condition effects later, we need to *synthesize* them
            // against time/position/velocity and effect enable state.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static double NormalizeMagnitudeWithSign(short signedMagnitude, double? directionSign)
    {
        var mag = Math.Clamp(Math.Abs((double)signedMagnitude) / 10000d, 0d, 1d);
        var sign = directionSign ?? (signedMagnitude < 0 ? -1d : 1d);
        return Math.Clamp(mag * sign, -1d, 1d);
    }

    private static double? TryGetDirectionXSign(IntPtr ffbDataPtr, out string? directionKind)
    {
        directionKind = null;

        var report = default(FFB_EFF_REPORT);
        if (Ffb_h_Eff_Report(ffbDataPtr, ref report) != 0)
            return null;

        if (report.Polar != 0)
        {
            // Map 0..255 -> 0..360 degrees (vJoy wrapper comment).
            var deg = report.Direction * (360.0 / 255.0);
            var x = Math.Cos(deg * (Math.PI / 180.0));
            directionKind = "Polar";
            return x >= 0 ? 1d : -1d;
        }

        // Cartesian: DirX is two's complement (signed byte).
        var dirX = unchecked((sbyte)report.DirX);
        directionKind = "Cartesian";
        if (dirX == 0)
            return null;
        return dirX > 0 ? 1d : -1d;
    }
}
