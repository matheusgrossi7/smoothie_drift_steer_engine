using System;
using System.ComponentModel;
using System.Threading;
using MadWizard.WinUSBNet;
using Vortice.XInput;

namespace DriftCore.Services.Input;

/// <summary>
/// Driver WinUSB (WinUSBNet) para o Xbox 360 Wireless Receiver (VID:045E PID:0719).
/// 
/// Este receiver multiplexa input, bateria/status, conexão/handshake e áudio no mesmo pipe.
/// A estratégia aqui é filtrar agressivamente pacotes e só aceitar reports de input.
/// </summary>
public sealed class XboxWirelessUsbDriver : IDisposable
{
    public const ushort VendorId = 0x045E;
    public const ushort ProductId = 0x0719;

    // GUID_DEVINTERFACE_USB_DEVICE
    // Lets us enumerate *all* USB devices and then filter by VID/PID, which is a useful fallback
    // when a custom WinUSB interface GUID is missing/changed on the machine.
    private const string UsbDeviceInterfaceGuid = "{A5DCBF10-6530-11D2-901F-00C04FB951ED}";

    private const int ExpectedInputReportLength = 29;

    private readonly string _deviceInterfaceGuid;
    private readonly int _readTimeoutMs;

    private readonly byte[] _buffer;

    private USBDevice? _device;
    private USBInterface? _iface;
    private USBPipe? _inPipe;
    private int _readSize;

    private Thread? _thread;
    private CancellationTokenSource? _cts;

    private volatile bool _isRunning;
    private volatile bool _isConnected;

    private readonly bool _dumpEnabled;
    private int _dumpRemaining;

    private readonly int _forcedOffset;
    private int _lockedOffset;
    private readonly int[] _offsetVotes;

    private Gamepad _lastGamepad;
    private double _lastSteering;
    private long _lastPacketTicks;

    // Aggressive filtering strategy:
    // The wireless receiver multiplexes non-input traffic (status/battery/voice) on the same IN pipe.
    // For real controller state we only accept the known 29-byte "input" report that starts with:
    //   00 <pad:1..4> 00 F0 00 13 ...
    // The exact start of the XInput-like state payload is learned and locked-on (typically offset 6).
    //
    // Then, to suppress single-frame glitches, we debounce by requiring two identical decodes
    // before publishing the state to the engine.
    private const int WirelessInputReportLength = 29;
    private const int WirelessStateOffsetMin = 6;
    private const int WirelessStateOffsetMax = WirelessInputReportLength - 12; // inclusive

    // NOTE: The wireless receiver payload layout varies from our initial assumptions.
    // We therefore learn the most plausible XInput-like payload offset and lock onto it.

    // Debounce: require two consecutive identical decoded states before publishing.
    private bool _hasPending;
    private ushort _pendingButtons;
    private byte _pendingLeftTrigger;
    private byte _pendingRightTrigger;
    private short _pendingLeftThumbX;
    private short _pendingLeftThumbY;
    private short _pendingRightThumbX;
    private short _pendingRightThumbY;
    private double _pendingSteering;

    private long _statReads;
    private long _statTimeouts;
    private long _statErrors;
    private long _statFiltered;
    private long _statParsed;
    private int _statLastBytes;
    private int _statLastB1;
    private int _statLastIf;
    private int _statLastInAddr;
    private int _statPayloadOffset;
    private int _statLastButtons;
    private int _statLastLX;
    private int _statLastLY;
    private int _statLastRX;
    private int _statLastRY;
    private int _statLastLT;
    private int _statLastRT;

    public readonly struct UsbDriverStats
    {
        public long Reads { get; }
        public long Timeouts { get; }
        public long Errors { get; }
        public long Filtered { get; }
        public long Parsed { get; }
        public int LastBytes { get; }
        public byte LastB1 { get; }
        public int LastInterfaceNumber { get; }
        public byte LastInPipeAddress { get; }
        public int PayloadOffset { get; }
        public ushort LastButtons { get; }
        public byte LastLeftTrigger { get; }
        public byte LastRightTrigger { get; }
        public short LastLeftThumbX { get; }
        public short LastLeftThumbY { get; }
        public short LastRightThumbX { get; }
        public short LastRightThumbY { get; }

        public UsbDriverStats(long reads, long timeouts, long errors, long filtered, long parsed, int lastBytes, int lastB1, int lastInterfaceNumber, int lastInAddr, int payloadOffset,
            int lastButtons, int lastLt, int lastRt, int lastLx, int lastLy, int lastRx, int lastRy)
        {
            Reads = reads;
            Timeouts = timeouts;
            Errors = errors;
            Filtered = filtered;
            Parsed = parsed;
            LastBytes = lastBytes;
            LastB1 = (byte)lastB1;
            LastInterfaceNumber = lastInterfaceNumber;
            LastInPipeAddress = (byte)lastInAddr;
            PayloadOffset = payloadOffset;

            LastButtons = (ushort)lastButtons;
            LastLeftTrigger = (byte)lastLt;
            LastRightTrigger = (byte)lastRt;
            LastLeftThumbX = (short)lastLx;
            LastLeftThumbY = (short)lastLy;
            LastRightThumbX = (short)lastRx;
            LastRightThumbY = (short)lastRy;
        }
    }

    public UsbDriverStats GetStats()
    {
        return new UsbDriverStats(
            reads: Interlocked.Read(ref _statReads),
            timeouts: Interlocked.Read(ref _statTimeouts),
            errors: Interlocked.Read(ref _statErrors),
            filtered: Interlocked.Read(ref _statFiltered),
            parsed: Interlocked.Read(ref _statParsed),
            lastBytes: Volatile.Read(ref _statLastBytes),
            lastB1: Volatile.Read(ref _statLastB1),
            lastInterfaceNumber: Volatile.Read(ref _statLastIf),
            lastInAddr: Volatile.Read(ref _statLastInAddr),
            payloadOffset: Volatile.Read(ref _statPayloadOffset),
            lastButtons: Volatile.Read(ref _statLastButtons),
            lastLt: Volatile.Read(ref _statLastLT),
            lastRt: Volatile.Read(ref _statLastRT),
            lastLx: Volatile.Read(ref _statLastLX),
            lastLy: Volatile.Read(ref _statLastLY),
            lastRx: Volatile.Read(ref _statLastRX),
            lastRy: Volatile.Read(ref _statLastRY)
        );
    }

    public XboxWirelessUsbDriver(string deviceInterfaceGuid, int readTimeoutMs = 20, int readBufferSize = 512)
    {
        _deviceInterfaceGuid = NormalizeGuid(deviceInterfaceGuid);
        _readTimeoutMs = Math.Max(0, readTimeoutMs);
        _buffer = new byte[Math.Max(ExpectedInputReportLength, readBufferSize)];

        _dumpEnabled = string.Equals(Environment.GetEnvironmentVariable("DRIFT_USB_DUMP"), "1", StringComparison.OrdinalIgnoreCase);
        var dumpCount = ParseOptionalIntEnv("DRIFT_USB_DUMP_COUNT");
        _dumpRemaining = _dumpEnabled ? Math.Clamp(dumpCount > 0 ? dumpCount : 5, 1, 200) : 0;

        _forcedOffset = ParseOptionalIntEnv("DRIFT_USB_OFFSET");
        _lockedOffset = _forcedOffset >= WirelessStateOffsetMin && _forcedOffset <= WirelessStateOffsetMax ? _forcedOffset : -1;
        _offsetVotes = new int[WirelessStateOffsetMax + 1];
    }

    public bool IsRunning => _isRunning;
    public bool IsConnected => _isConnected;

    public void Start()
    {
        if (_isRunning) return;

        if (_dumpEnabled)
            Console.Error.WriteLine("[USB] DRIFT_USB_DUMP enabled (will print a few input reports)");

        if (_dumpEnabled && _lockedOffset >= 0)
            Console.Error.WriteLine($"[USB] DRIFT_USB_OFFSET forced/locked: {_lockedOffset}");

        _cts = new CancellationTokenSource();
        _thread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "XboxWirelessUsbDriver.ReadLoop"
        };

        _isRunning = true;
        _thread.Start(_cts.Token);
    }

    public bool TryGetLatest(out GamepadReadResult result)
    {
        // IMPORTANT: For WinUSB, reads can legitimately time out when no new packets arrive.
        // That must NOT be treated as a disconnect, otherwise the engine flickers and resets vJoy.
        // We consider the device connected as long as we have an open pipe.
        if (!_isConnected)
        {
            result = GamepadReadResult.Disconnected;
            return false;
        }

        // Copy snapshot (struct copies are atomic enough for this use).
        var gamepad = _lastGamepad;
        var steering = _lastSteering;

        result = new GamepadReadResult(isConnected: true, steering: steering, gamepad: gamepad);
        return true;
    }

    public void Dispose()
    {
        Stop();
    }

    public void Stop()
    {
        if (!_isRunning) return;

        try
        {
            _cts?.Cancel();
        }
        catch { /* ignore */ }

        try
        {
            if (_thread != null && _thread.IsAlive)
                _thread.Join(millisecondsTimeout: 500);
        }
        catch { /* ignore */ }

        _cts?.Dispose();
        _cts = null;
        _thread = null;

        CloseDevice();

        _isConnected = false;
        _isRunning = false;
    }

    private void ReadLoop(object? state)
    {
        var token = state is CancellationToken ct ? ct : CancellationToken.None;

        while (!token.IsCancellationRequested)
        {
            if (_inPipe == null)
            {
                TryOpenDevice();

                // Backoff curto para não busy-loop em máquina sem driver/dispositivo.
                if (_inPipe == null)
                    Sleep(token, 250);

                continue;
            }

            int bytesRead;
            try
            {
                // WinUSB interrupt pipes can be picky about requested length;
                // reading the endpoint's max packet size is the safest default.
                var readSize = _readSize <= 0 ? Math.Min(_buffer.Length, 32) : _readSize;
                bytesRead = _inPipe.Read(_buffer, 0, readSize);
            }
            catch (Exception ex)
            {
                // Importante: com PipeTransferTimeout configurado, "sem dados ainda" pode vir como timeout.
                // Timeout não é desconexão e não deve causar CloseDevice, senão o estado fica piscando.
                if (IsTimeout(ex))
                {
                    Interlocked.Increment(ref _statTimeouts);
                    continue;
                }

                Interlocked.Increment(ref _statErrors);
                MarkDisconnected();
                CloseDevice();
                Sleep(token, 250);
                continue;
            }

            if (bytesRead <= 0)
                continue;

            Interlocked.Increment(ref _statReads);
            Volatile.Write(ref _statLastBytes, bytesRead);
            Volatile.Write(ref _statLastB1, bytesRead > 1 ? _buffer[1] : -1);
            Volatile.Write(ref _statLastIf, _iface?.Number ?? -1);
            Volatile.Write(ref _statLastInAddr, _inPipe?.Address ?? -1);

            // Anti-flicker #1: aceite apenas o tamanho de report de input que conhecemos (29 bytes).
            // Isso descarta a maior parte de voz/status/handshake sem nem tentar parse.
            if (bytesRead != WirelessInputReportLength)
            {
                Interlocked.Increment(ref _statFiltered);
                continue;
            }

            if (_dumpEnabled && _dumpRemaining > 0 && LooksLikeWirelessInputHeader(_buffer, bytesRead))
            {
                _dumpRemaining--;
                Console.Error.WriteLine($"[USB] input-report len={bytesRead} hex={Convert.ToHexString(_buffer.AsSpan(0, bytesRead))}");
            }

            // Parse (sem alocação, bitwise, little-endian).
            if (!TryParseAndDebounce(_buffer, bytesRead, out var gamepad, out var steering, out var usedOffset))
            {
                Interlocked.Increment(ref _statFiltered);
                continue;
            }

            Volatile.Write(ref _statPayloadOffset, usedOffset);

            _lastGamepad = gamepad;
            _lastSteering = steering;
            Volatile.Write(ref _lastPacketTicks, DateTime.UtcNow.Ticks);
            _isConnected = true;
            Interlocked.Increment(ref _statParsed);
        }

        _isConnected = false;
        _isRunning = false;
    }

    private void TryOpenDevice()
    {
        try
        {
            CloseDevice();

            // Prefer a configured WinUSB interface GUID, but fall back to the generic USB device
            // interface GUID when the configured GUID isn't present (common when driver binding changes).
            USBDeviceInfo[] details;
            var configuredGuid = string.IsNullOrWhiteSpace(_deviceInterfaceGuid) ? string.Empty : _deviceInterfaceGuid;
            if (!string.IsNullOrWhiteSpace(configuredGuid))
            {
                details = USBDevice.GetDevices(configuredGuid);
            }
            else
            {
                details = Array.Empty<USBDeviceInfo>();
            }

            if (details.Length == 0)
            {
                details = USBDevice.GetDevices(UsbDeviceInterfaceGuid);
            }

            if (details.Length == 0)
            {
                MarkDisconnected();
                return;
            }

            USBDeviceInfo? match = null;
            for (int i = 0; i < details.Length; i++)
            {
                var info = details[i];
                if (info.VID == VendorId && info.PID == ProductId)
                {
                    match = info;
                    break;
                }
            }

            if (match == null)
            {
                MarkDisconnected();
                return;
            }

            _device = new USBDevice(match);

            // Heurística: preferir interface VendorSpecific com IN pipe.
            _iface = FindBestInterface(_device);
            if (_iface == null)
            {
                MarkDisconnected();
                CloseDevice();
                return;
            }

            _inPipe = _iface.InPipe;
            if (_inPipe == null)
            {
                MarkDisconnected();
                CloseDevice();
                return;
            }

            try
            {
                _readSize = Math.Clamp(_inPipe.MaximumPacketSize, 1, _buffer.Length);
            }
            catch
            {
                _readSize = Math.Min(_buffer.Length, 32);
            }

            // Timeout (ms) – evita bloqueio permanente do loop.
            try
            {
                _inPipe.Policy.PipeTransferTimeout = _readTimeoutMs;
            }
            catch
            {
                // Algumas stacks/drivers podem não suportar policy. Mantém sem timeout.
            }

            try
            {
                _inPipe.Policy.AllowPartialReads = true;
            }
            catch
            {
                // Ignore.
            }

            // Consider connected as soon as the WinUSB pipe is open.
            // Input freshness is independent (steering/gamepad only update on valid input packets).
            _isConnected = true;
        }
        catch
        {
            MarkDisconnected();
            CloseDevice();
        }
    }

    private static USBInterface? FindBestInterface(USBDevice device)
    {
        // Receiver é um dispositivo composto (ex.: IA_01 / IA_02) e pode expor interfaces de áudio/headset.
        // A interface de INPUT é a #0. Priorizar ela explicitamente é a forma mais segura de evitar flicker.

        USBInterface? iface0 = null;
        try
        {
            // Em WinUSBNet, o indexador usa o "interface number".
            iface0 = device.Interfaces[0];
        }
        catch
        {
            iface0 = null;
        }

        if (iface0 != null && iface0.BaseClass != USBBaseClass.Audio)
        {
            USBPipe? inPipe0 = null;
            try { inPipe0 = iface0.InPipe; } catch { inPipe0 = null; }

            // Interface #0 é preferida, mas tentamos achar uma interface que realmente devolva bytes.
            if (inPipe0 != null)
            {
                var probe0 = ProbePipe(inPipe0);
                if (probe0 == PipeProbeResult.HasData)
                    return iface0;

                // Timeout = "sem dados ainda"; mantém como fallback caso nenhuma interface entregue bytes.
                if (probe0 == PipeProbeResult.Timeout)
                {
                    // Continue searching other interfaces for actual traffic.
                }
            }
        }

        // Fallback: menor risco possível.
        // Ignora explicitamente Audio e qualquer interface sem InPipe.
        USBInterface? firstNonAudioWithInPipe = null;
        USBInterface? firstTimeoutWithInPipe = iface0 != null && iface0.BaseClass != USBBaseClass.Audio ? iface0 : null;
        foreach (var iface in device.Interfaces)
        {
            if (iface.BaseClass == USBBaseClass.Audio)
                continue;

            USBPipe? inPipe = null;
            try { inPipe = iface.InPipe; } catch { inPipe = null; }
            if (inPipe == null)
                continue;

            if (firstNonAudioWithInPipe == null)
                firstNonAudioWithInPipe = iface;

            var probe = ProbePipe(inPipe);
            if (probe == PipeProbeResult.HasData)
                return iface;

            if (probe == PipeProbeResult.Timeout && firstTimeoutWithInPipe == null)
                firstTimeoutWithInPipe = iface;

            // Se encontrarmos a própria interface #0 por enumeração, também serve.
            if (iface.Number == 0)
                return iface;
        }

        return firstTimeoutWithInPipe ?? firstNonAudioWithInPipe;
    }

    private enum PipeProbeResult
    {
        HasData,
        Timeout,
        Error
    }

    private static PipeProbeResult ProbePipe(USBPipe pipe)
    {
        // Best-effort probe:
        // - HasData: pipe returned bytes quickly
        // - Timeout: pipe didn't have data within the short timeout (not an error)
        // - Error: pipe failed immediately with a non-timeout error
        var buf = new byte[Math.Max(1, Math.Min(pipe.MaximumPacketSize, 32))];
        try { pipe.Policy.PipeTransferTimeout = 10; } catch { }
        try { pipe.Policy.AllowPartialReads = true; } catch { }

        try
        {
            var n = pipe.Read(buf, 0, buf.Length);
            return n > 0 ? PipeProbeResult.HasData : PipeProbeResult.Timeout;
        }
        catch (Exception ex)
        {
            return IsTimeout(ex) ? PipeProbeResult.Timeout : PipeProbeResult.Error;
        }
    }

    private void CloseDevice()
    {
        try { _inPipe = null; } catch { /* ignore */ }
        try { _iface = null; } catch { /* ignore */ }

        try
        {
            _device?.Dispose();
        }
        catch { /* ignore */ }

        _device = null;
    }

    private void MarkDisconnected()
    {
        _isConnected = false;
        _lastGamepad = default;
        _lastSteering = 0;
        Volatile.Write(ref _lastPacketTicks, 0);

        _hasPending = false;
        Volatile.Write(ref _statPayloadOffset, -1);
    }

    // Kept for potential future diagnostics/telemetry.
    // The engine should not use packet freshness as a connectivity signal.

    private static void Sleep(CancellationToken token, int ms)
    {
        try { token.WaitHandle.WaitOne(ms); } catch { /* ignore */ }
    }

    private static bool IsTimeout(Exception ex)
    {
        if (ex is TimeoutException)
            return true;

        // WinUSBNet can wrap the real Win32 timeout multiple levels deep:
        // USBException -> APIException -> Win32Exception(NativeErrorCode=121)
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is Win32Exception w32)
            {
                // ERROR_SEM_TIMEOUT (121) and WAIT_TIMEOUT (258)
                if (w32.NativeErrorCode is 121 or 258)
                    return true;
            }
        }

        return false;
    }

    private bool TryParseAndDebounce(byte[] report, int bytesRead, out Gamepad gamepad, out double steering, out int usedOffset)
    {
        usedOffset = -1;

        if (!TryParseWirelessInputReport(report, bytesRead, out var buttons, out var lt, out var rt, out var lx, out var ly, out var rx, out var ry, out var payloadOffset))
        {
            gamepad = default;
            steering = 0;
            return false;
        }

        usedOffset = payloadOffset;
        steering = NormalizeAxis(lx);

        // Debounce: require exact repetition once.
        if (_hasPending &&
            buttons == _pendingButtons && lt == _pendingLeftTrigger && rt == _pendingRightTrigger &&
            lx == _pendingLeftThumbX && ly == _pendingLeftThumbY && rx == _pendingRightThumbX && ry == _pendingRightThumbY)
        {
            _hasPending = false;
            gamepad = BuildGamepad(buttons, lt, rt, lx, ly, rx, ry);
            PublishLastDecoded(buttons, lt, rt, lx, ly, rx, ry);
            return true;
        }

        _hasPending = true;
        _pendingButtons = buttons;
        _pendingLeftTrigger = lt;
        _pendingRightTrigger = rt;
        _pendingLeftThumbX = lx;
        _pendingLeftThumbY = ly;
        _pendingRightThumbX = rx;
        _pendingRightThumbY = ry;
        _pendingSteering = steering;

        gamepad = default;
        steering = 0;
        return false;
    }

    private bool TryParseWirelessInputReport(byte[] report, int bytesRead,
        out ushort buttons, out byte leftTrigger, out byte rightTrigger,
        out short leftThumbX, out short leftThumbY, out short rightThumbX, out short rightThumbY,
        out int payloadOffset)
    {
        buttons = 0;
        leftTrigger = 0;
        rightTrigger = 0;
        leftThumbX = 0;
        leftThumbY = 0;
        rightThumbX = 0;
        rightThumbY = 0;
        payloadOffset = -1;

        if (bytesRead != WirelessInputReportLength)
            return false;

        if (!LooksLikeWirelessInputHeader(report, bytesRead))
            return false;

        // If an offset was forced (or previously locked), use it exclusively.
        if (_lockedOffset >= WirelessStateOffsetMin && _lockedOffset <= WirelessStateOffsetMax)
        {
            payloadOffset = _lockedOffset;
            return TryReadXInputLikeAt(report, bytesRead, payloadOffset,
                out buttons, out leftTrigger, out rightTrigger, out leftThumbX, out leftThumbY, out rightThumbX, out rightThumbY);
        }

        // Otherwise: scan candidates and vote for the most plausible.
        var bestOffset = -1;
        var bestScore = int.MinValue;
        ushort bestButtons = 0;
        byte bestLt = 0;
        byte bestRt = 0;
        short bestLx = 0;
        short bestLy = 0;
        short bestRx = 0;
        short bestRy = 0;

        for (int off = WirelessStateOffsetMin; off <= WirelessStateOffsetMax; off++)
        {
            if (!TryReadXInputLikeAt(report, bytesRead, off, out var b, out var lt, out var rt, out var lx, out var ly, out var rx, out var ry))
                continue;

            var score = ScoreXInputLikeDecode(b, lt, rt, lx, ly, rx, ry);
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = off;
                bestButtons = b;
                bestLt = lt;
                bestRt = rt;
                bestLx = lx;
                bestLy = ly;
                bestRx = rx;
                bestRy = ry;
            }
        }

        if (bestOffset < 0)
            return false;

        // Vote and lock-on if this offset consistently wins.
        _offsetVotes[bestOffset]++;
        var secondBestVotes = 0;
        for (int off = WirelessStateOffsetMin; off <= WirelessStateOffsetMax; off++)
        {
            if (off == bestOffset) continue;
            var v = _offsetVotes[off];
            if (v > secondBestVotes) secondBestVotes = v;
        }

        // Lock quickly, but require a margin to avoid flapping.
        if (_offsetVotes[bestOffset] >= 6 && _offsetVotes[bestOffset] - secondBestVotes >= 3)
        {
            _lockedOffset = bestOffset;
            if (_dumpEnabled)
                Console.Error.WriteLine($"[USB] Locked wireless payload offset: {_lockedOffset}");
        }

        payloadOffset = bestOffset;
        buttons = bestButtons;
        leftTrigger = bestLt;
        rightTrigger = bestRt;
        leftThumbX = bestLx;
        leftThumbY = bestLy;
        rightThumbX = bestRx;
        rightThumbY = bestRy;
        return true;
    }

    private static int ScoreXInputLikeDecode(ushort buttons, byte lt, byte rt, short lx, short ly, short rx, short ry)
    {
        // Heuristic scoring tuned for "controller at rest" plausibility.
        // Higher score = more likely correct payload alignment.
        var score = 0;

        // Triggers at rest are usually close to 0, not ~128.
        score += lt <= 20 ? 40 : 0;
        score += rt <= 20 ? 40 : 0;
        score -= (lt >= 120 && lt <= 136) ? 30 : 0;
        score -= (rt >= 120 && rt <= 136) ? 30 : 0;

        // Avoid obvious extreme pegging.
        score -= (lx is short.MinValue or short.MaxValue) ? 80 : 0;
        score -= (ly is short.MinValue or short.MaxValue) ? 80 : 0;
        score -= (rx is short.MinValue or short.MaxValue) ? 60 : 0;
        score -= (ry is short.MinValue or short.MaxValue) ? 60 : 0;

        // Prefer smaller magnitudes (rest near center). Use cheap abs.
        static int Abs(short v) => v < 0 ? -v : v;
        score -= Abs(lx) / 512;
        score -= Abs(ly) / 512;
        score -= Abs(rx) / 1024;
        score -= Abs(ry) / 1024;

        // Prefer no buttons pressed (rest).
        score += buttons == 0 ? 25 : 0;

        return score;
    }

    private static int ParseOptionalIntEnv(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return -1;

        return int.TryParse(raw.Trim(), out var v) ? v : -1;
    }

    private static bool LooksLikeWirelessInputHeader(byte[] report, int bytesRead)
    {
        if (bytesRead != WirelessInputReportLength)
            return false;

        if (report[0] != 0x00)
            return false;

        // Wireless receiver packets include a controller slot/index in byte 1.
        // In practice this is 1..4 for real controller traffic.
        if (report[1] is < 0x01 or > 0x04)
            return false;

        if (report[2] != 0x00 || report[3] != 0xF0 || report[4] != 0x00 || report[5] != 0x13)
            return false;

        return true;
    }

    private static bool TryReadXInputLikeAt(byte[] report, int bytesRead, int buttonsOffset,
        out ushort buttons, out byte leftTrigger, out byte rightTrigger,
        out short leftThumbX, out short leftThumbY, out short rightThumbX, out short rightThumbY)
    {
        buttons = 0;
        leftTrigger = 0;
        rightTrigger = 0;
        leftThumbX = 0;
        leftThumbY = 0;
        rightThumbX = 0;
        rightThumbY = 0;

        // Need at least 12 bytes from buttonsOffset.
        if (buttonsOffset < 0 || buttonsOffset + 12 > bytesRead)
            return false;

        // Decode.
        buttons = (ushort)(report[buttonsOffset] | (report[buttonsOffset + 1] << 8));
        leftTrigger = report[buttonsOffset + 2];
        rightTrigger = report[buttonsOffset + 3];
        leftThumbX = (short)(report[buttonsOffset + 4] | (report[buttonsOffset + 5] << 8));
        leftThumbY = (short)(report[buttonsOffset + 6] | (report[buttonsOffset + 7] << 8));
        rightThumbX = (short)(report[buttonsOffset + 8] | (report[buttonsOffset + 9] << 8));
        rightThumbY = (short)(report[buttonsOffset + 10] | (report[buttonsOffset + 11] << 8));

        // Aggressive plausibility checks.
        const ushort ValidButtonsMask = 0xF7FF;
        const ushort DpadUp = 0x0001;
        const ushort DpadDown = 0x0002;
        const ushort DpadLeft = 0x0004;
        const ushort DpadRight = 0x0008;

        if ((buttons & ~ValidButtonsMask) != 0)
            return false;

        if (((buttons & DpadUp) != 0 && (buttons & DpadDown) != 0) ||
            ((buttons & DpadLeft) != 0 && (buttons & DpadRight) != 0))
        {
            return false;
        }

        return true;
    }

    private static Gamepad BuildGamepad(ushort buttons, byte leftTrigger, byte rightTrigger, short lx, short ly, short rx, short ry)
    {
        return new Gamepad
        {
            Buttons = (GamepadButtons)buttons,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            LeftThumbX = lx,
            LeftThumbY = ly,
            RightThumbX = rx,
            RightThumbY = ry
        };
    }

    private void PublishLastDecoded(ushort buttons, byte lt, byte rt, short lx, short ly, short rx, short ry)
    {
        Volatile.Write(ref _statLastButtons, buttons);
        Volatile.Write(ref _statLastLT, lt);
        Volatile.Write(ref _statLastRT, rt);
        Volatile.Write(ref _statLastLX, lx);
        Volatile.Write(ref _statLastLY, ly);
        Volatile.Write(ref _statLastRX, rx);
        Volatile.Write(ref _statLastRY, ry);
    }

    private static double NormalizeAxis(short value)
    {
        double normalized = value < 0 ? value / 32768.0 : value / 32767.0;
        return Math.Clamp(normalized, -1.0, 1.0);
    }

    private static string NormalizeGuid(string guid)
    {
        guid = (guid ?? string.Empty).Trim();
        if (guid.Length == 0) return string.Empty;

        // Aceita formatos com/sem chaves.
        if (!guid.StartsWith("{", StringComparison.Ordinal))
            guid = "{" + guid;
        if (!guid.EndsWith("}", StringComparison.Ordinal))
            guid = guid + "}";

        return guid;
    }
}
