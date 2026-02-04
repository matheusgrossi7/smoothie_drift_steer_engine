using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace DriftCore.Services.ForceFeedback;

internal static class FfbReportParser
{
    // vJoy passes a pointer to an internal FFB_DATA. Different builds/exports can expose
    // different layouts, so we try a few common size-prefix encodings before falling back to a peek.
    public static string DescribeFfbData(IntPtr ffbDataPtr)
    {
        if (!TryReadPayload(ffbDataPtr, out var payload, out var layout))
            return "<unreadable>";

        return $"{DescribePayload(payload)} Layout={layout}";
    }

    public static bool TryGetNormalizedForce(IntPtr ffbDataPtr, out double normalized)
    {
        normalized = 0d;

        if (!TryReadPayload(ffbDataPtr, out var payload))
            return false;

        return TryGetNormalizedForce(payload, out normalized);
    }

    public static bool TryReadPayload(IntPtr ffbDataPtr, out byte[] payload)
    {
        return TryReadPayload(ffbDataPtr, out payload, out _);
    }

    public static bool TryReadPayload(IntPtr ffbDataPtr, out byte[] payload, out string layout)
    {
        payload = Array.Empty<byte>();
        layout = "";

        if (ffbDataPtr == IntPtr.Zero)
            return false;

        if (TryReadWithInt32Size(ffbDataPtr, out payload))
        {
            layout = "i32+4";
            return true;
        }

        if (TryReadWithByteSize(ffbDataPtr, out payload))
        {
            layout = "u8+1";
            return true;
        }

        if (TryReadWithUInt16Size(ffbDataPtr, out payload))
        {
            layout = "u16+2";
            return true;
        }

        return false;
    }

    public static bool TryPeekBytes(IntPtr ffbDataPtr, int count, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (ffbDataPtr == IntPtr.Zero)
            return false;

        count = Math.Clamp(count, 1, 512);
        var tmp = new byte[count];

        try
        {
            for (int i = 0; i < count; i++)
                tmp[i] = Marshal.ReadByte(ffbDataPtr, i);
        }
        catch
        {
            return false;
        }

        bytes = tmp;
        return true;
    }

    private static bool TryReadWithInt32Size(IntPtr ffbDataPtr, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        int size;
        try
        {
            size = Marshal.ReadInt32(ffbDataPtr);
        }
        catch
        {
            return false;
        }

        if (size <= 0 || size > 512)
            return false;

        payload = new byte[size];
        try
        {
            Marshal.Copy(IntPtr.Add(ffbDataPtr, 4), payload, 0, size);
        }
        catch
        {
            payload = Array.Empty<byte>();
            return false;
        }

        return true;
    }

    private static bool TryReadWithByteSize(IntPtr ffbDataPtr, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        int size;
        try
        {
            size = Marshal.ReadByte(ffbDataPtr);
        }
        catch
        {
            return false;
        }

        if (size <= 0 || size > 512)
            return false;

        payload = new byte[size];
        try
        {
            Marshal.Copy(IntPtr.Add(ffbDataPtr, 1), payload, 0, size);
        }
        catch
        {
            payload = Array.Empty<byte>();
            return false;
        }

        return true;
    }

    private static bool TryReadWithUInt16Size(IntPtr ffbDataPtr, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        int size;
        try
        {
            size = Marshal.ReadInt16(ffbDataPtr);
        }
        catch
        {
            return false;
        }

        if (size <= 0 || size > 512)
            return false;

        payload = new byte[size];
        try
        {
            Marshal.Copy(IntPtr.Add(ffbDataPtr, 2), payload, 0, size);
        }
        catch
        {
            payload = Array.Empty<byte>();
            return false;
        }

        return true;
    }

    private static string DescribePayload(ReadOnlySpan<byte> p)
    {
        if (p.Length == 0)
            return "<empty>";

        var reportId = p[0];
        var name = ((FfbReportId)reportId).ToString();

        var sb = new StringBuilder();
        sb.Append($"ReportId=0x{reportId:X2}({name})");

        // Most PID reports are little-endian.
        // This is a best-effort parser based on the common USB HID PID layouts.
        switch ((FfbReportId)reportId)
        {
            case FfbReportId.DeviceControl:
                if (p.Length >= 2)
                {
                    var ctrl = p[1];
                    sb.Append($" Control={DescribeDeviceControl(ctrl)}(0x{ctrl:X2})");
                }
                break;

            case FfbReportId.DeviceGain:
                if (p.Length >= 2)
                {
                    var gain = p[1];
                    sb.Append($" Gain={gain} (0..255)");
                }
                break;

            case FfbReportId.SetEffect:
                DescribeSetEffect(p, sb);
                break;

            case FfbReportId.SetEnvelope:
                DescribeSetEnvelope(p, sb);
                break;

            case FfbReportId.SetCondition:
                DescribeSetCondition(p, sb);
                break;

            case FfbReportId.SetPeriodic:
                DescribeSetPeriodic(p, sb);
                break;

            case FfbReportId.SetConstantForce:
                DescribeSetConstantForce(p, sb);
                break;

            case FfbReportId.SetRampForce:
                DescribeSetRampForce(p, sb);
                break;

            case FfbReportId.EffectOperation:
                DescribeEffectOperation(p, sb);
                break;

            case FfbReportId.PidBlockLoad:
                DescribePidBlockLoad(p, sb);
                break;

            case FfbReportId.PidPool:
                DescribePidPool(p, sb);
                break;

            default:
                break;
        }

        sb.Append($" Len={p.Length}");
        sb.Append($" Raw={ToHex(p)}");
        return sb.ToString();
    }

    private static bool TryGetNormalizedForce(ReadOnlySpan<byte> p, out double normalized)
    {
        normalized = 0d;

        if (p.Length == 0)
            return false;

        short raw;
        switch ((FfbReportId)p[0])
        {
            case FfbReportId.SetConstantForce:
                if (p.Length < 4) return false;
                raw = ReadI16(p, 2);
                break;

            case FfbReportId.SetRampForce:
                if (p.Length < 4) return false;
                var start = ReadI16(p, 2);
                var end = p.Length >= 6 ? ReadI16(p, 4) : start;
                raw = end != 0 ? end : start;
                break;

            case FfbReportId.SetPeriodic:
                if (p.Length < 4) return false;
                raw = ReadI16(p, 2);
                break;

            case FfbReportId.SetCondition:
                if (p.Length < 9) return false;
                var pos = ReadI16(p, 5);
                var neg = ReadI16(p, 7);
                raw = Math.Abs(pos) >= Math.Abs(neg) ? pos : neg;
                break;

            default:
                return false;
        }

        normalized = NormalizePidValue(raw);
        return true;
    }

    private static void DescribeSetEffect(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        // Common layout (best-effort):
        // [0]=ReportId
        // [1]=EffectId
        // [2]=EffectType
        // [3..4]=Duration (ushort)
        // [5..6]=TriggerRepeatInterval (ushort)
        // [7..8]=SamplePeriod (ushort)
        // [9]=Gain
        // [10]=TriggerButton
        // [11]=AxesEnable
        // [12]=DirectionEnable
        // [13..]=Direction data + StartDelay etc (varies)
        if (p.Length < 3)
            return;

        var effectId = p[1];
        var effectType = p[2];
        sb.Append($" EffectId={effectId}");
        sb.Append($" Type={DescribeEffectType(effectType)}(0x{effectType:X2})");

        if (p.Length >= 5)
            sb.Append($" Duration={ReadU16(p, 3)}ms");
        if (p.Length >= 7)
            sb.Append($" TrigRepeat={ReadU16(p, 5)}ms");
        if (p.Length >= 9)
            sb.Append($" SamplePeriod={ReadU16(p, 7)}ms");
        if (p.Length >= 10)
            sb.Append($" Gain={p[9]}");
        if (p.Length >= 11)
            sb.Append($" TriggerBtn={p[10]}");
        if (p.Length >= 12)
            sb.Append($" Axes=0x{p[11]:X2}");
        if (p.Length >= 13)
            sb.Append($" DirEn=0x{p[12]:X2}");
    }

    private static void DescribeSetEnvelope(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        // [1]=EffectId, [2..]=attack/fade levels & times
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 4) sb.Append($" AttackLevel={ReadU16(p, 2)}");
        if (p.Length >= 6) sb.Append($" FadeLevel={ReadU16(p, 4)}");
        if (p.Length >= 8) sb.Append($" AttackTime={ReadU16(p, 6)}ms");
        if (p.Length >= 10) sb.Append($" FadeTime={ReadU16(p, 8)}ms");
    }

    private static void DescribeSetCondition(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        // Typical: [1]=EffectId, [2]=ParamBlockOffset
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 3) sb.Append($" ParamOfs={p[2]}");
        if (p.Length >= 5) sb.Append($" Center={ReadI16(p, 3)}");
        if (p.Length >= 7) sb.Append($" PosCoeff={ReadI16(p, 5)}");
        if (p.Length >= 9) sb.Append($" NegCoeff={ReadI16(p, 7)}");
        if (p.Length >= 11) sb.Append($" PosSat={ReadU16(p, 9)}");
        if (p.Length >= 13) sb.Append($" NegSat={ReadU16(p, 11)}");
        if (p.Length >= 15) sb.Append($" Deadband={ReadU16(p, 13)}");
    }

    private static void DescribeSetPeriodic(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 4) sb.Append($" Mag={ReadI16(p, 2)}");
        if (p.Length >= 6) sb.Append($" Offset={ReadI16(p, 4)}");
        if (p.Length >= 8) sb.Append($" Phase={ReadU16(p, 6)}");
        if (p.Length >= 10) sb.Append($" Period={ReadU16(p, 8)}ms");
    }

    private static void DescribeSetConstantForce(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 4) sb.Append($" Mag={ReadI16(p, 2)}");
    }

    private static void DescribeSetRampForce(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 4) sb.Append($" Start={ReadI16(p, 2)}");
        if (p.Length >= 6) sb.Append($" End={ReadI16(p, 4)}");
    }

    private static void DescribeEffectOperation(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 3)
        {
            var op = p[2];
            sb.Append($" Op={DescribeEffectOp(op)}(0x{op:X2})");
        }
        if (p.Length >= 4)
            sb.Append($" LoopCount={p[3]}");
    }

    private static void DescribePidBlockLoad(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        // Best-effort: [1]=EffectId, [2]=LoadStatus, [3..4]=RamPoolAvail
        if (p.Length < 2)
            return;

        sb.Append($" EffectId={p[1]}");
        if (p.Length >= 3) sb.Append($" Status=0x{p[2]:X2}");
        if (p.Length >= 5) sb.Append($" RamAvail={ReadU16(p, 3)}");
    }

    private static void DescribePidPool(ReadOnlySpan<byte> p, StringBuilder sb)
    {
        // Best-effort: [1..2]=RamPoolSize, [3]=MaxEffects, [4]=MemMgmt
        if (p.Length >= 3) sb.Append($" RamPoolSize={ReadU16(p, 1)}");
        if (p.Length >= 4) sb.Append($" MaxEffects={p[3]}");
        if (p.Length >= 5) sb.Append($" MemMgmt=0x{p[4]:X2}");
    }

    private static string DescribeEffectType(byte t) => t switch
    {
        0x01 => "Constant",
        0x02 => "Ramp",
        0x03 => "Square",
        0x04 => "Sine",
        0x05 => "Triangle",
        0x06 => "SawtoothUp",
        0x07 => "SawtoothDown",
        0x08 => "Spring",
        0x09 => "Damper",
        0x0A => "Inertia",
        0x0B => "Friction",
        0x0C => "Custom",
        _ => "Unknown"
    };

    private static string DescribeEffectOp(byte op) => op switch
    {
        0x01 => "Start",
        0x02 => "StartSolo",
        0x03 => "Stop",
        _ => "Unknown"
    };

    private static string DescribeDeviceControl(byte ctrl) => ctrl switch
    {
        0x01 => "EnableActuators",
        0x02 => "DisableActuators",
        0x03 => "StopAllEffects",
        0x04 => "Reset",
        0x05 => "Pause",
        0x06 => "Continue",
        _ => "Unknown"
    };

    private static ushort ReadU16(ReadOnlySpan<byte> p, int ofs)
    {
        if (ofs + 2 > p.Length) return 0;
        return BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(ofs, 2));
    }

    private static short ReadI16(ReadOnlySpan<byte> p, int ofs)
    {
        if (ofs + 2 > p.Length) return 0;
        return BinaryPrimitives.ReadInt16LittleEndian(p.Slice(ofs, 2));
    }

    private static double NormalizePidValue(short raw)
    {
        if (raw == short.MinValue)
            return -1d;

        const double max = 10000d;
        var value = raw / max;
        return Math.Clamp(value, -1d, 1d);
    }

    private static string ToHex(ReadOnlySpan<byte> data)
    {
        // keep log compact
        const int max = 32;
        var len = Math.Min(data.Length, max);
        var sb = new StringBuilder(len * 2 + 8);
        for (int i = 0; i < len; i++)
            sb.Append(data[i].ToString("X2"));
        if (data.Length > max)
            sb.Append("…");
        return sb.ToString();
    }

    private enum FfbReportId : byte
    {
        SetEffect = 0x01,
        SetEnvelope = 0x02,
        SetCondition = 0x03,
        SetPeriodic = 0x04,
        SetConstantForce = 0x05,
        SetRampForce = 0x06,
        // 0x07..0x0C custom/other
        EffectOperation = 0x0A,
        DeviceControl = 0x0B,
        DeviceGain = 0x0C,
        // Common pool/status reports (may differ by implementation)
        PidPool = 0x0D,
        PidBlockLoad = 0x0E,
    }
}
