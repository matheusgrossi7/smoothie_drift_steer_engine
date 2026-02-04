using System.Reflection;
using System.Text;
using System.ComponentModel;
using MadWizard.WinUSBNet;

if (args.Length >= 2 && string.Equals(args[0], "--monitor", StringComparison.OrdinalIgnoreCase))
{
    var guid = args[1];
    var dump = args.Any(a => string.Equals(a, "--dump", StringComparison.OrdinalIgnoreCase));
    var rawio = args.Any(a => string.Equals(a, "--rawio", StringComparison.OrdinalIgnoreCase));
    MonitorReceiver(guid, dump, rawio);
    return;
}

static string FormatType(Type t)
{
    if (t.IsByRef)
    {
        return FormatType(t.GetElementType()!) + "&";
    }

    if (!t.IsGenericType)
    {
        return t.FullName ?? t.Name;
    }

    var genericTypeDefinitionName = (t.GetGenericTypeDefinition().FullName ?? t.Name);
    var tickIndex = genericTypeDefinitionName.IndexOf('`');
    if (tickIndex >= 0)
    {
        genericTypeDefinitionName = genericTypeDefinitionName.Substring(0, tickIndex);
    }

    var args = t.GetGenericArguments();
    return genericTypeDefinitionName + "<" + string.Join(", ", args.Select(FormatType)) + ">";
}

static string FormatMethod(MethodInfo m)
{
    var sb = new StringBuilder();

    sb.Append(FormatType(m.ReturnType));
    sb.Append(' ');
    sb.Append(m.DeclaringType?.FullName ?? "<unknown>");
    sb.Append('.');
    sb.Append(m.Name);

    if (m.IsGenericMethodDefinition)
    {
        sb.Append('<');
        sb.Append(string.Join(", ", m.GetGenericArguments().Select(a => a.Name)));
        sb.Append('>');
    }

    sb.Append('(');
    sb.Append(string.Join(", ", m.GetParameters().Select(p =>
    {
        var modifier = p.IsOut ? "out " : (p.ParameterType.IsByRef ? "ref " : "");
        return modifier + FormatType(p.ParameterType) + " " + p.Name;
    })));
    sb.Append(')');

    return sb.ToString();
}

static void DumpMethods(Type t, Func<MethodInfo, bool> predicate)
{
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .Where(predicate)
        .OrderBy(m => m.Name)
        .ThenBy(m => m.GetParameters().Length))
    {
        Console.WriteLine(FormatMethod(m));
    }
}

static void DumpProperties(Type t, Func<PropertyInfo, bool> predicate)
{
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        .Where(predicate)
        .OrderBy(p => p.Name))
    {
        Console.WriteLine($"{FormatType(p.PropertyType)} {t.FullName}.{p.Name}");
    }
}

Console.WriteLine("WinUSBNet assembly: " + typeof(USBDevice).Assembly.FullName);
Console.WriteLine();

Console.WriteLine("=== USBPipe (read-related) ===");
DumpMethods(typeof(USBPipe), m =>
    m.Name is "Read" or "BeginRead" or "EndRead" || m.Name.StartsWith("Read", StringComparison.Ordinal));
Console.WriteLine();

Console.WriteLine("=== USBPipe (properties that look like transfer/length/timeout) ===");
DumpProperties(typeof(USBPipe), p =>
    p.Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
    p.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase) ||
    p.Name.Contains("Length", StringComparison.OrdinalIgnoreCase) ||
    p.Name.Contains("Bytes", StringComparison.OrdinalIgnoreCase));
Console.WriteLine();

Console.WriteLine("=== USBPipe (all public properties) ===");
DumpProperties(typeof(USBPipe), _ => true);
Console.WriteLine();

Console.WriteLine("=== USBPipePolicy (timeout + partial read behavior) ===");
DumpProperties(typeof(USBPipePolicy), _ => true);

Console.WriteLine();
Console.WriteLine("=== USBDevice.Interfaces type ===");
var ifacesProp = typeof(USBDevice).GetProperty("Interfaces", BindingFlags.Public | BindingFlags.Instance);
Console.WriteLine(ifacesProp == null ? "<not found>" : FormatType(ifacesProp.PropertyType) + " " + typeof(USBDevice).FullName + ".Interfaces");

Console.WriteLine();
Console.WriteLine("=== USBInterface (public properties) ===");
DumpProperties(typeof(USBInterface), _ => true);

Console.WriteLine();
Console.WriteLine("=== USBInterfaceCollection? (Interfaces property underlying type methods) ===");
if (ifacesProp != null)
{
    var t = ifacesProp.PropertyType;
    DumpMethods(t, m => m.Name is "get_Item" or "get_Count" or "GetEnumerator" || m.Name.Contains("Item") || m.Name.Contains("Count"));
}

static void MonitorReceiver(string guid, bool dump, bool rawio)
{
    guid = guid.Trim();
    if (!guid.StartsWith("{", StringComparison.Ordinal)) guid = "{" + guid;
    if (!guid.EndsWith("}", StringComparison.Ordinal)) guid = guid + "}";

    Console.WriteLine("Monitoring devices for GUID: " + guid);
    var devices = USBDevice.GetDevices(guid);
    Console.WriteLine("Found devices: " + (devices?.Length ?? 0));
    if (devices == null || devices.Length == 0) return;

    for (int i = 0; i < devices.Length; i++)
    {
        var d = devices[i];
        Console.WriteLine($"[{i}] VID=0x{d.VID:X4} PID=0x{d.PID:X4} Path={d.DevicePath}");
    }

    var info = devices[0];
    using var dev = new USBDevice(info);

    Console.WriteLine();
    Console.WriteLine("Interfaces:");
    foreach (var iface in dev.Interfaces)
    {
        Console.WriteLine($"- If#{iface.Number} BaseClass={iface.BaseClass} SubClass={iface.SubClass} Protocol={iface.Protocol}");
        try
        {
            var inPipe = iface.InPipe;
            Console.WriteLine($"  InPipe: Addr=0x{inPipe.Address:X2} MaxPacket={inPipe.MaximumPacketSize}");
        }
        catch
        {
            Console.WriteLine("  InPipe: <none>");
        }

        try
        {
            var outPipe = iface.OutPipe;
            Console.WriteLine($"  OutPipe: Addr=0x{outPipe.Address:X2} MaxPacket={outPipe.MaximumPacketSize}");
        }
        catch
        {
            Console.WriteLine("  OutPipe: <none>");
        }

        try
        {
            Console.WriteLine("  Pipes:");
            foreach (var pipe in iface.Pipes)
            {
                Console.WriteLine($"   - {(pipe.IsIn ? "IN" : "OUT")} Addr=0x{pipe.Address:X2} MaxPacket={pipe.MaximumPacketSize}");
            }
        }
        catch
        {
            Console.WriteLine("  Pipes: <unavailable>");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Probing IN pipes for 5 seconds...");
    Console.WriteLine($"Policy: RawIO={(rawio ? "on" : "off")}");

    var end = DateTime.UtcNow.AddSeconds(5);

    var dumpsLeft = dump ? 20 : 0;

    while (DateTime.UtcNow < end)
    {
        foreach (var iface in dev.Interfaces)
        {
            USBPipe? pipeIn = null;
            try { pipeIn = iface.InPipe; } catch { pipeIn = null; }
            if (pipeIn == null) continue;

            // Read with the endpoint's max packet size (commonly required for interrupt pipes).
            var buf = new byte[Math.Max(1, pipeIn.MaximumPacketSize)];

            try { pipeIn.Policy.PipeTransferTimeout = 10; } catch { }
            try { pipeIn.Policy.AllowPartialReads = true; } catch { }
            try { pipeIn.Policy.AutoClearStall = true; } catch { }
            try { pipeIn.Policy.IgnoreShortPackets = true; } catch { }
            try { pipeIn.Policy.ShortPacketTerminate = true; } catch { }
            if (rawio)
            {
                // RawIO can change how the underlying stack handles transfers; may help on some endpoints.
                try { pipeIn.Policy.RawIO = true; } catch { }
            }

            int read;
            try { read = pipeIn.Read(buf, 0, buf.Length); }
            catch (Exception ex)
            {
                // Keep it compact; we only need to know if *any* interface reads.
                Console.WriteLine($"If#{iface.Number} IN 0x{pipeIn.Address:X2}: EX {ex.GetType().Name} HR=0x{ex.HResult:X8} {ex.Message}");
                PrintExceptionChain(ex);
                continue;
            }

            if (read <= 0) continue;

            var b0 = buf[0];
            var b1 = buf[1];
            var b2 = buf[2];
            var b3 = buf[3];

            Console.WriteLine($"If#{iface.Number} IN 0x{pipeIn.Address:X2}: bytes={read} hdr={b0:X2} {b1:X2} {b2:X2} {b3:X2} b1Is00={(b1 == 0x00 ? "yes" : "no")}");

            if (dump && dumpsLeft > 0)
            {
                dumpsLeft--;
                Console.Write("  data: ");
                for (int i = 0; i < read; i++)
                {
                    if (i != 0) Console.Write(' ');
                    Console.Write(buf[i].ToString("X2"));
                }
                Console.WriteLine();
            }
        }
    }
}

static void PrintExceptionChain(Exception ex)
{
    var depth = 0;
    Exception? cur = ex;
    while (cur != null && depth < 6)
    {
        var prefix = depth == 0 ? "  chain:" : "        ";

        if (cur is Win32Exception w32)
        {
            Console.WriteLine($"{prefix} {cur.GetType().Name} HR=0x{cur.HResult:X8} NativeErrorCode={w32.NativeErrorCode} {cur.Message}");
        }
        else
        {
            Console.WriteLine($"{prefix} {cur.GetType().Name} HR=0x{cur.HResult:X8} {cur.Message}");
        }

        cur = cur.InnerException;
        depth++;
    }
}
