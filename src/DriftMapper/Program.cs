using System.ComponentModel;
using System.Text.Json;
using MadWizard.WinUSBNet;

namespace DriftMapper;

internal static class Program
{
    private const ushort VendorId = 0x045E;
    private const ushort ProductId = 0x0719;

    // GUID_DEVINTERFACE_USB_DEVICE
    private const string UsbDeviceInterfaceGuid = "{A5DCBF10-6530-11D2-901F-00C04FB951ED}";

    private const int WirelessInputReportLength = 29;

    public static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);

            if (options.AnalyzePath is not null)
            {
                Analyzer.Analyze(options.AnalyzePath);
                return 0;
            }

            Collector.Run(options);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Mapper] Cancelado.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mapper] ERRO: {ex.Message}");
            return 1;
        }
    }

    private sealed record CliOptions(
        string DeviceInterfaceGuid,
        int TimeoutMs,
        int MaxSamplesPerStep,
        int StepSeconds,
        int PrepSeconds,
        bool Manual,
        string OutputPath,
        string? AnalyzePath)
    {
        public static CliOptions Parse(string[] args)
        {
            string guid = string.Empty;
            int timeout = 20;
            int maxSamples = 6000;
            int seconds = 6;
            int prep = 3;
            bool manual = false;
            string outPath = $"drift_map_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string? analyzePath = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                static string Next(string[] arr, ref int idx)
                {
                    if (idx + 1 >= arr.Length) throw new ArgumentException($"Missing value for {arr[idx]}");
                    idx++;
                    return arr[idx];
                }

                if (a is "--guid" or "-g") guid = Next(args, ref i);
                else if (a is "--timeout" or "-t") timeout = int.Parse(Next(args, ref i));
                else if (a is "--max" or "-m") maxSamples = int.Parse(Next(args, ref i));
                else if (a is "--seconds" or "-s") seconds = int.Parse(Next(args, ref i));
                else if (a is "--prep") prep = int.Parse(Next(args, ref i));
                else if (a is "--manual") manual = true;
                else if (a is "--out" or "-o") outPath = Next(args, ref i);
                else if (a is "--analyze" or "-a") analyzePath = Next(args, ref i);
                else if (a is "--help" or "-h")
                {
                    PrintHelp();
                    Environment.Exit(0);
                }
            }

            timeout = Math.Clamp(timeout, 0, 5000);
            seconds = Math.Clamp(seconds, 1, 60);
            prep = Math.Clamp(prep, 0, 30);
            maxSamples = Math.Clamp(maxSamples, 200, 200_000);

            return new CliOptions(
                DeviceInterfaceGuid: NormalizeGuid(guid),
                TimeoutMs: timeout,
                MaxSamplesPerStep: maxSamples,
                StepSeconds: seconds,
                PrepSeconds: prep,
                Manual: manual,
                OutputPath: outPath,
                AnalyzePath: analyzePath);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("DriftMapper - coletor de reports brutos do Xbox 360 Wireless Receiver (WinUSB)\n");
            Console.WriteLine("Coletar:\n  dotnet run -c Debug --project src/DriftMapper -- --guid {GUID} --out map.json");
            Console.WriteLine("Analisar:\n  dotnet run -c Debug --project src/DriftMapper -- --analyze map.json");
            Console.WriteLine();
            Console.WriteLine("Opções:");
            Console.WriteLine("  --guid/-g     Device Interface GUID do WinUSB (opcional; se vazio usa GUID genérico USB)");
            Console.WriteLine("  --timeout/-t  Timeout do pipe (ms). Padrão: 20");
            Console.WriteLine("  --seconds/-s  Segundos por etapa. Padrão: 6");
            Console.WriteLine("  --prep        Segundos de contagem regressiva antes de cada etapa. Padrão: 3");
            Console.WriteLine("  --max/-m      Máx. amostras por etapa. Padrão: 6000");
            Console.WriteLine("  --out/-o      Caminho do JSON. Padrão: drift_map_YYYYMMDD_HHMMSS.json");
            Console.WriteLine("  --manual      Modo manual (pressione ENTER para iniciar cada etapa)");
        }

        private static string NormalizeGuid(string guid)
        {
            guid = (guid ?? string.Empty).Trim();
            if (guid.Length == 0) return string.Empty;
            if (!guid.StartsWith("{", StringComparison.Ordinal)) guid = "{" + guid;
            if (!guid.EndsWith("}", StringComparison.Ordinal)) guid += "}";
            return guid;
        }
    }

    private static class Collector
    {
        public static void Run(CliOptions options)
        {
            Console.WriteLine("[Mapper] DriftMapper (coleta guiada)");
            Console.WriteLine($"[Mapper] VID:PID={VendorId:X4}:{ProductId:X4} timeout={options.TimeoutMs}ms step={options.StepSeconds}s max={options.MaxSamplesPerStep}");
            Console.WriteLine($"[Mapper] mode={(options.Manual ? "manual" : "auto")} prep={options.PrepSeconds}s");

            using var reader = new RawReceiverReader(options.DeviceInterfaceGuid, options.TimeoutMs);
            reader.Open();

            var session = new MappingSession
            {
                CreatedUtc = DateTime.UtcNow,
                Vid = VendorId,
                Pid = ProductId,
                DeviceInterfaceGuid = string.IsNullOrWhiteSpace(options.DeviceInterfaceGuid) ? UsbDeviceInterfaceGuid : options.DeviceInterfaceGuid,
                TimeoutMs = options.TimeoutMs,
                Steps = new List<CaptureStep>()
            };

            var steps = StepCatalog.DefaultSteps();

            Console.WriteLine("\n[Mapper] Instruções:");
            Console.WriteLine("- Para cada etapa: faça a ação repetidamente durante a captura.");
            Console.WriteLine("- Você pode pressionar o botão várias vezes (a ideia é maioria/consistência).");
            Console.WriteLine("- Para abortar a qualquer momento: Ctrl+C.");
            Console.WriteLine();

            foreach (var step in steps)
            {
                Console.WriteLine($"\n=== {step.Name} ===");
                Console.WriteLine(step.Instruction);

                if (options.Manual)
                {
                    Console.WriteLine($"Pressione ENTER para iniciar ({options.StepSeconds}s)...");
                    _ = Console.ReadLine();
                }
                else
                {
                    Countdown(options.PrepSeconds);
                }

                var captured = CaptureOneStep(reader, step, options.StepSeconds, options.MaxSamplesPerStep);
                session.Steps.Add(captured);

                var quick = Analyzer.QuickStats(captured);
                Console.WriteLine($"[Mapper] Capturado: {quick.Accepted} amostras válidas | únicos={quick.UniqueFrames} | bytesMaisVariantes={string.Join(",", quick.TopChangingByteIndices)}");
            }

            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(options.OutputPath, json);
            Console.WriteLine($"\n[Mapper] OK. Arquivo salvo em: {Path.GetFullPath(options.OutputPath)}");
            Console.WriteLine("[Mapper] Próximo passo: rode com --analyze map.json e me mande o output.");
        }

        private static void Countdown(int seconds)
        {
            if (seconds <= 0) return;

            for (int i = seconds; i >= 1; i--)
            {
                Console.Write($"[Mapper] Começando em {i}...\r");
                Thread.Sleep(1000);
            }
            Console.Write(new string(' ', 40) + "\r");
        }

        private static CaptureStep CaptureOneStep(RawReceiverReader reader, StepDefinition step, int seconds, int maxSamples)
        {
            var captured = new CaptureStep
            {
                Name = step.Name,
                Instruction = step.Instruction,
                StartedUtc = DateTime.UtcNow,
                Samples = new List<RawSample>(capacity: Math.Min(2000, maxSamples))
            };

            var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            var token = tokenSource.Token;

            int accepted = 0;
            int totalReads = 0;
            var buf = new byte[64];

            // Capture loop
            while (!token.IsCancellationRequested && accepted < maxSamples)
            {
                totalReads++;
                var n = reader.TryRead(buf);
                if (n <= 0)
                    continue;

                if (n != WirelessInputReportLength)
                    continue;

                if (!LooksLikeWirelessInputHeader(buf, n))
                    continue;

                accepted++;
                captured.Samples.Add(new RawSample
                {
                    UtcTicks = DateTime.UtcNow.Ticks,
                    Hex = Convert.ToHexString(buf.AsSpan(0, n))
                });

                if ((accepted % 500) == 0)
                    Console.WriteLine($"[Mapper] ... {accepted} amostras");
            }

            captured.EndedUtc = DateTime.UtcNow;
            captured.TotalReads = totalReads;
            return captured;
        }

        private static bool LooksLikeWirelessInputHeader(byte[] report, int bytesRead)
        {
            if (bytesRead != WirelessInputReportLength)
                return false;

            if (report[0] != 0x00)
                return false;

            // controller slot 1..4
            if (report[1] is < 0x01 or > 0x04)
                return false;

            // 00 F0 00 13
            if (report[2] != 0x00 || report[3] != 0xF0 || report[4] != 0x00 || report[5] != 0x13)
                return false;

            return true;
        }
    }

    private sealed class RawReceiverReader : IDisposable
    {
        private readonly string _deviceInterfaceGuid;
        private readonly int _timeoutMs;
        private readonly byte[] _readBuffer = new byte[512];

        private USBDevice? _device;
        private USBInterface? _iface;
        private USBPipe? _inPipe;
        private int _readSize;

        public RawReceiverReader(string deviceInterfaceGuid, int timeoutMs)
        {
            _deviceInterfaceGuid = deviceInterfaceGuid;
            _timeoutMs = timeoutMs;
        }

        public void Open()
        {
            Close();

            USBDeviceInfo[] details;
            if (!string.IsNullOrWhiteSpace(_deviceInterfaceGuid))
                details = USBDevice.GetDevices(_deviceInterfaceGuid);
            else
                details = Array.Empty<USBDeviceInfo>();

            if (details.Length == 0)
                details = USBDevice.GetDevices(UsbDeviceInterfaceGuid);

            USBDeviceInfo? match = null;
            foreach (var info in details)
            {
                if (info.VID == VendorId && info.PID == ProductId)
                {
                    match = info;
                    break;
                }
            }

            if (match is null)
                throw new InvalidOperationException("Receiver (045E:0719) não encontrado via WinUSB. Verifique o driver e o GUID.");

            _device = new USBDevice(match);
            _iface = FindBestInterface(_device);
            if (_iface is null)
                throw new InvalidOperationException("Nenhuma interface WinUSB adequada foi encontrada.");

            _inPipe = _iface.InPipe;
            if (_inPipe is null)
                throw new InvalidOperationException("Interface selecionada não tem InPipe.");

            try { _readSize = Math.Clamp(_inPipe.MaximumPacketSize, 1, _readBuffer.Length); }
            catch { _readSize = 32; }

            try { _inPipe.Policy.PipeTransferTimeout = _timeoutMs; } catch { }
            try { _inPipe.Policy.AllowPartialReads = true; } catch { }

            Console.WriteLine($"[Mapper] Interface={_iface.Number} InPipe=0x{_inPipe.Address:X2} MaxPacket={_readSize}");
        }

        public int TryRead(byte[] destination)
        {
            if (_inPipe is null)
                return 0;

            try
            {
                var readSize = _readSize <= 0 ? Math.Min(_readBuffer.Length, 32) : _readSize;
                var n = _inPipe.Read(_readBuffer, 0, readSize);
                if (n <= 0) return 0;

                var copy = Math.Min(n, destination.Length);
                Buffer.BlockCopy(_readBuffer, 0, destination, 0, copy);
                return copy;
            }
            catch (Exception ex)
            {
                if (IsTimeout(ex))
                    return 0;

                throw;
            }
        }

        public void Dispose() => Close();

        private void Close()
        {
            try { _inPipe = null; } catch { }
            try { _iface = null; } catch { }
            try { _device?.Dispose(); } catch { }
            _device = null;
        }

        private static USBInterface? FindBestInterface(USBDevice device)
        {
            // Prefer interface #0 if it is not Audio.
            USBInterface? iface0 = null;
            try { iface0 = device.Interfaces[0]; } catch { }

            if (iface0 != null && iface0.BaseClass != USBBaseClass.Audio)
            {
                USBPipe? inPipe0 = null;
                try { inPipe0 = iface0.InPipe; } catch { }
                if (inPipe0 != null)
                    return iface0;
            }

            foreach (var iface in device.Interfaces)
            {
                if (iface.BaseClass == USBBaseClass.Audio)
                    continue;

                USBPipe? inPipe = null;
                try { inPipe = iface.InPipe; } catch { }
                if (inPipe != null)
                    return iface;
            }

            return null;
        }

        private static bool IsTimeout(Exception ex)
        {
            if (ex is TimeoutException)
                return true;

            for (Exception? cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is Win32Exception w32)
                {
                    if (w32.NativeErrorCode is 121 or 258)
                        return true;
                }
            }

            return false;
        }
    }

    private sealed class StepDefinition
    {
        public required string Name { get; init; }
        public required string Instruction { get; init; }
    }

    private static class StepCatalog
    {
        public static IReadOnlyList<StepDefinition> DefaultSteps() => new List<StepDefinition>
        {
            new() { Name = "REST", Instruction = "Solte tudo e não mexa no controle." },

            new() { Name = "A", Instruction = "Aperte o botão A repetidamente." },
            new() { Name = "B", Instruction = "Aperte o botão B repetidamente." },
            new() { Name = "X", Instruction = "Aperte o botão X repetidamente." },
            new() { Name = "Y", Instruction = "Aperte o botão Y repetidamente." },

            new() { Name = "LB", Instruction = "Aperte LB repetidamente." },
            new() { Name = "RB", Instruction = "Aperte RB repetidamente." },
            new() { Name = "BACK", Instruction = "Aperte BACK repetidamente." },
            new() { Name = "START", Instruction = "Aperte START repetidamente." },
            new() { Name = "L3", Instruction = "Clique no analógico esquerdo (L3) repetidamente." },
            new() { Name = "R3", Instruction = "Clique no analógico direito (R3) repetidamente." },

            new() { Name = "DPAD_UP", Instruction = "Aperte DPAD CIMA repetidamente." },
            new() { Name = "DPAD_RIGHT", Instruction = "Aperte DPAD DIREITA repetidamente." },
            new() { Name = "DPAD_DOWN", Instruction = "Aperte DPAD BAIXO repetidamente." },
            new() { Name = "DPAD_LEFT", Instruction = "Aperte DPAD ESQUERDA repetidamente." },

            new() { Name = "LT_PROGRESS", Instruction = "Aperte LT progressivamente (0->100%->0), repetindo." },
            new() { Name = "RT_PROGRESS", Instruction = "Aperte RT progressivamente (0->100%->0), repetindo." },

            new() { Name = "LX_POS", Instruction = "Empurre LX totalmente para a DIREITA, solte, repita." },
            new() { Name = "LX_NEG", Instruction = "Empurre LX totalmente para a ESQUERDA, solte, repita." },
            new() { Name = "LY_POS", Instruction = "Empurre LY totalmente para CIMA, solte, repita." },
            new() { Name = "LY_NEG", Instruction = "Empurre LY totalmente para BAIXO, solte, repita." },

            new() { Name = "RX_POS", Instruction = "Empurre RX totalmente para a DIREITA, solte, repita." },
            new() { Name = "RX_NEG", Instruction = "Empurre RX totalmente para a ESQUERDA, solte, repita." },
            new() { Name = "RY_POS", Instruction = "Empurre RY totalmente para CIMA, solte, repita." },
            new() { Name = "RY_NEG", Instruction = "Empurre RY totalmente para BAIXO, solte, repita." },
        };
    }

    private sealed class MappingSession
    {
        public DateTime CreatedUtc { get; set; }
        public ushort Vid { get; set; }
        public ushort Pid { get; set; }
        public string DeviceInterfaceGuid { get; set; } = string.Empty;
        public int TimeoutMs { get; set; }
        public List<CaptureStep> Steps { get; set; } = new();
    }

    private sealed class CaptureStep
    {
        public string Name { get; set; } = string.Empty;
        public string Instruction { get; set; } = string.Empty;
        public DateTime StartedUtc { get; set; }
        public DateTime EndedUtc { get; set; }
        public int TotalReads { get; set; }
        public List<RawSample> Samples { get; set; } = new();
    }

    private sealed class RawSample
    {
        public long UtcTicks { get; set; }
        public string Hex { get; set; } = string.Empty;
    }

    private static class Analyzer
    {
        public static void Analyze(string path)
        {
            var json = File.ReadAllText(path);
            var session = JsonSerializer.Deserialize<MappingSession>(json);
            if (session is null)
                throw new InvalidOperationException("JSON inválido.");

            Console.WriteLine($"[Analyze] Steps: {session.Steps.Count} | createdUtc={session.CreatedUtc:o}");

            var rest = session.Steps.FirstOrDefault(s => string.Equals(s.Name, "REST", StringComparison.OrdinalIgnoreCase));
            byte[]? restMedian = rest is null ? null : ComputeMedianBytes(rest);

            foreach (var step in session.Steps)
            {
                var stats = ComputeByteStats(step);

                Console.WriteLine($"\n=== {step.Name} ===");
                Console.WriteLine($"samples={step.Samples.Count} totalReads={step.TotalReads} duration={(step.EndedUtc - step.StartedUtc).TotalSeconds:F1}s");

                var top = stats.OrderByDescending(s => s.Range).ThenByDescending(s => s.Distinct).Take(10).ToArray();
                Console.WriteLine("Top bytes variando: idx:range distinct min max");
                foreach (var b in top)
                    Console.WriteLine($"  {b.Index,2}: {b.Range,4}  {b.Distinct,4}  {b.Min,3}  {b.Max,3}");

                if (restMedian is not null)
                {
                    Console.WriteLine("Delta vs REST (top 10): idx:absDelta");
                    var deltas = new List<(int idx, int d)>();
                    for (int i = 0; i < WirelessInputReportLength; i++)
                    {
                        var d = Math.Abs(stats[i].Median - restMedian[i]);
                        deltas.Add((i, d));
                    }

                    foreach (var (idx, d) in deltas.OrderByDescending(x => x.d).Take(10))
                        Console.WriteLine($"  {idx,2}: {d}");
                }
            }
        }

        public sealed record QuickSummary(int Accepted, int UniqueFrames, int[] TopChangingByteIndices);

        public static QuickSummary QuickStats(CaptureStep step)
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in step.Samples)
                unique.Add(s.Hex);

            var stats = ComputeByteStats(step);
            var top = stats.OrderByDescending(s => s.Range).ThenByDescending(s => s.Distinct).Take(6).Select(s => s.Index).ToArray();
            return new QuickSummary(step.Samples.Count, unique.Count, top);
        }

        private sealed class ByteStat
        {
            public int Index { get; init; }
            public int Min { get; set; } = 255;
            public int Max { get; set; } = 0;
            public int Distinct { get; set; }
            public int Range => Max - Min;
            public int Median { get; set; }
        }

        private static ByteStat[] ComputeByteStats(CaptureStep step)
        {
            var stats = new ByteStat[WirelessInputReportLength];
            for (int i = 0; i < stats.Length; i++)
                stats[i] = new ByteStat { Index = i };

            var distinctSets = new HashSet<byte>[WirelessInputReportLength];
            var allValues = new List<byte>[WirelessInputReportLength];
            for (int i = 0; i < WirelessInputReportLength; i++)
            {
                distinctSets[i] = new HashSet<byte>();
                allValues[i] = new List<byte>(Math.Min(2000, step.Samples.Count));
            }

            foreach (var sample in step.Samples)
            {
                if (!TryParseHex29(sample.Hex, out var bytes))
                    continue;

                for (int i = 0; i < WirelessInputReportLength; i++)
                {
                    var v = bytes[i];
                    if (v < stats[i].Min) stats[i].Min = v;
                    if (v > stats[i].Max) stats[i].Max = v;
                    distinctSets[i].Add(v);
                    allValues[i].Add(v);
                }
            }

            for (int i = 0; i < WirelessInputReportLength; i++)
            {
                stats[i].Distinct = distinctSets[i].Count;
                stats[i].Median = MedianByte(allValues[i]);
            }

            return stats;
        }

        private static byte[] ComputeMedianBytes(CaptureStep step)
        {
            var stats = ComputeByteStats(step);
            var median = new byte[WirelessInputReportLength];
            for (int i = 0; i < WirelessInputReportLength; i++)
                median[i] = (byte)Math.Clamp(stats[i].Median, 0, 255);
            return median;
        }

        private static int MedianByte(List<byte> values)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            return values[values.Count / 2];
        }

        private static bool TryParseHex29(string hex, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try
            {
                bytes = Convert.FromHexString(hex.Trim());
                return bytes.Length == WirelessInputReportLength;
            }
            catch
            {
                return false;
            }
        }
    }
}
