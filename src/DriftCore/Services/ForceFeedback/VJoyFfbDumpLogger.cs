using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace DriftCore.Services.ForceFeedback;

internal sealed class VJoyFfbDumpLogger : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private readonly string _path;
    private readonly string _latestPath;
    private long _lines;

    public VJoyFfbDumpLogger()
    {
        var root = GetProjectRootDirectory();
        _path = CreateSessionPath(root);
        _latestPath = System.IO.Path.Combine(root, "ffb_dump_latest.log");
        EnsureFileCreated();
    }

    public string Path => _path;

    public void TryLogPacket(IntPtr ffbDataPtr)
    {
        if (VJoyFfbInterop.TryCopyDataBytes(ffbDataPtr, out var header, out var data))
        {
            var meta = $"Cmd=0x{header.cmd:X8} Size={header.size} DataPtr=0x{header.data.ToInt64():X}";
            var line = BuildLine(meta, data);
            WriteLine(line);
            return;
        }

        // Fallback: dump a small peek of the raw pointer, just in case.
        if (TryPeekBytes(ffbDataPtr, 64, out var peek))
        {
            var line = BuildLine("Unparsed=peek64", peek);
            WriteLine(line);
            return;
        }

        WriteLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " | Unparsed=unreadable");
    }

    private static bool TryPeekBytes(IntPtr ptr, int count, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (ptr == IntPtr.Zero)
            return false;

        count = Math.Clamp(count, 1, 512);
        var tmp = new byte[count];

        try
        {
            for (int i = 0; i < count; i++)
                tmp[i] = Marshal.ReadByte(ptr, i);
        }
        catch
        {
            return false;
        }

        bytes = tmp;
        return true;
    }

    private string BuildLine(string prefix, byte[] payload)
    {
        var sb = new StringBuilder(payload.Length * 2 + 64);
        sb.Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        sb.Append(" | ");
        sb.Append(prefix);
        sb.Append(" | Len=");
        sb.Append(payload.Length);
        sb.Append(" | Raw=");
        for (int i = 0; i < payload.Length; i++)
            sb.Append(payload[i].ToString("X2"));
        return sb.ToString();
    }

    private void WriteLine(string line)
    {
        lock (_gate)
        {
            _writer ??= CreateWriter(_path);
            _writer.WriteLine(line);
            _lines++;
        }
    }

    private void EnsureFileCreated()
    {
        lock (_gate)
        {
            if (_writer != null)
                return;

            _writer = CreateWriter(_path);
            _writer.WriteLine($"# vJoy FFB dump session");
            _writer.WriteLine($"# UtcStart={DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
            _writer.WriteLine($"# ProcessId={Process.GetCurrentProcess().Id}");
            _writer.WriteLine($"# BaseDir={AppContext.BaseDirectory}");
            _writer.WriteLine($"# Latest={_latestPath}");
            _writer.Flush();

            try
            {
                File.WriteAllText(_latestPath, $"# Latest dump file\n{_path}\n", Encoding.UTF8);
            }
            catch
            {
                // Best-effort.
            }

            Console.WriteLine($"[FFB] Dump file: {_path}");
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            var fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetFileName(path));
            var stream = new FileStream(fallback, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }
    }

    private static string CreateSessionPath(string root)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var name = $"ffb_dump_{stamp}.log";
        return System.IO.Path.Combine(root, name);
    }

    private static string GetProjectRootDirectory()
    {
        try
        {
            // Prefer current directory if launched from repo root.
            var cwd = Directory.GetCurrentDirectory();
            if (LooksLikeProjectRoot(cwd))
                return cwd;

            // Otherwise walk up from the binary folder.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (LooksLikeProjectRoot(dir.FullName))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return cwd;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static bool LooksLikeProjectRoot(string directory)
    {
        try
        {
            if (File.Exists(System.IO.Path.Combine(directory, "SmoothieDriftEngine.sln")))
                return true;
            if (Directory.Exists(System.IO.Path.Combine(directory, ".git")))
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_writer != null)
            {
                _writer.WriteLine($"# UtcEnd={DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
                _writer.WriteLine($"# Lines={_lines}");
                _writer.Flush();
            }
            _writer?.Dispose();
            _writer = null;
        }
    }
}
