using EditInput.Core.Controller;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Windows.Native;

namespace EditInput.Windows.Input;

public sealed record ControllerInfo(int Slot, string Name)
{
    public string Label => $"Controller {Slot + 1} – {Name}";
}

/// <summary>Latest controller view for the UI and the release watchdog. Immutable; swapped atomically.</summary>
public sealed record ControllerSnapshot(
    IReadOnlyList<ControllerInfo> Connected,
    ControllerInfo? Active,
    PadReading Reading,
    bool LiveReading)
{
    public static readonly ControllerSnapshot Empty = new(Array.Empty<ControllerInfo>(), null, default, false);
}

/// <summary>
/// Polls XInput on its own thread. Rate adapts to need: the configured rate (500–1000 Hz) only while a
/// controller input is bound, being captured or displayed; otherwise a cheap 4 Hz connection check.
/// Empty slots are scanned once per second because XInputGetState on an empty slot is comparatively slow.
///
/// The controller API is isolated behind this class so XInput can later be swapped for GameInput without
/// touching the engine: the engine only receives <see cref="InputId"/> edges.
/// </summary>
public sealed class ControllerInputManager : IDisposable
{
    private const string Cat = "Controller";

    private readonly Action<InputId, bool> _sink;
    private readonly ILogger _log;
    private readonly ControllerStateInterpreter _interpreter = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly bool[] _slotConnected = new bool[XInput.MaxControllers];
    private readonly string?[] _slotNames = new string?[XInput.MaxControllers];
    private volatile bool _stopping;
    private volatile ControllerSnapshot _snapshot = ControllerSnapshot.Empty;
    private volatile int _preferredSlot = -1;
    private volatile int _pollRateHz = 500;
    private volatile bool _bindsNeedController;
    private volatile bool _captureActive;
    private volatile bool _uiWantsLive;
    private int _configThreshold = 50, _configDeadzone = 10, _configVersion, _appliedVersion = -1;
    private int _activeSlot = -1;
    private uint _lastPacket;
    private bool _xinputMissing;

    public ControllerInputManager(Action<InputId, bool> sink, ILogger log)
    {
        _sink = sink;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "ControllerPoll", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Returns the XInput slot owned by our own virtual controller (never read as physical input).</summary>
    public Func<int>? VirtualSlotProvider { get; set; }

    public event Action<ControllerInfo>? Connected;
    public event Action<ControllerInfo>? Disconnected;

    public ControllerSnapshot Snapshot => _snapshot;

    public void Start() => _thread.Start();

    public void Configure(int triggerThresholdPercent, int deadzonePercent, int preferredSlot)
    {
        Volatile.Write(ref _configThreshold, triggerThresholdPercent);
        Volatile.Write(ref _configDeadzone, deadzonePercent);
        Interlocked.Increment(ref _configVersion);
        _preferredSlot = preferredSlot;
        _wake.Set();
    }

    public int PollRateHz
    {
        get => _pollRateHz;
        set { _pollRateHz = Math.Clamp(value, 125, 1000); _wake.Set(); }
    }

    public bool BindsNeedController
    {
        set { _bindsNeedController = value; _wake.Set(); }
    }

    public bool CaptureActive
    {
        set { _captureActive = value; _wake.Set(); }
    }

    public bool UiWantsLiveReading
    {
        set { _uiWantsLive = value; _wake.Set(); }
    }

    private bool HighRate => _bindsNeedController || _captureActive || _uiWantsLive;

    /// <summary>Physical state for the release watchdog; null when not being polled live.</summary>
    public bool? IsPhysicallyDown(ControllerButton b)
    {
        var s = _snapshot;
        if (!s.LiveReading || s.Active is null) return null;
        return s.Reading.IsPressed(b);
    }

    private void Run()
    {
        using var timer = new HighResolutionTimer();
        long lastScan = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (!_stopping)
        {
            try
            {
                if (_xinputMissing)
                {
                    _wake.WaitOne(5000);
                    continue;
                }

                if (_appliedVersion != Volatile.Read(ref _configVersion))
                {
                    _appliedVersion = Volatile.Read(ref _configVersion);
                    _interpreter.Configure(Volatile.Read(ref _configThreshold), Volatile.Read(ref _configDeadzone));
                }

                var now = sw.ElapsedMilliseconds;
                if (now - lastScan >= 1000 || lastScan == 0)
                {
                    lastScan = Math.Max(1, now);
                    ScanSlots();
                }

                SelectActiveSlot();
                var live = HighRate;
                if (_activeSlot >= 0) PollActive(live);
                else _snapshot = _snapshot with { Reading = default, LiveReading = false };

                var period = live && _activeSlot >= 0
                    ? TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _pollRateHz)
                    : TimeSpan.FromMilliseconds(250);
                timer.Wait(_wake, period);
            }
            catch (DllNotFoundException ex)
            {
                _xinputMissing = true;
                _log.Error(Cat, "XInput (xinput1_4.dll) is not available; controller support disabled", ex);
            }
            catch (Exception ex)
            {
                _log.Error(Cat, "Controller polling error", ex);
                Thread.Sleep(100);
            }
        }
    }

    private void ScanSlots()
    {
        var virtualSlot = VirtualSlotProvider?.Invoke() ?? -1;
        for (var slot = 0; slot < XInput.MaxControllers; slot++)
        {
            var connected = slot != virtualSlot && XInput.XInputGetState((uint)slot, out _) == XInput.ERROR_SUCCESS;
            if (connected == _slotConnected[slot]) continue;
            _slotConnected[slot] = connected;
            _slotNames[slot] = connected ? XInput.DescribeController((uint)slot) : null;
            _log.Debug(Cat, $"Slot {slot + 1}: {(connected ? "connected (" + _slotNames[slot] + ")" : "empty")}");
        }
        PublishList();
    }

    private void SelectActiveSlot()
    {
        var preferred = _preferredSlot;
        var virtualSlot = VirtualSlotProvider?.Invoke() ?? -1;
        int wanted;
        if (preferred is >= 0 and < XInput.MaxControllers)
            wanted = _slotConnected[preferred] && preferred != virtualSlot ? preferred : -1;
        else
            wanted = Array.FindIndex(_slotConnected, c => c);
        if (wanted == virtualSlot) wanted = -1;

        if (wanted == _activeSlot) return;

        if (_activeSlot >= 0) RaiseDisconnected(_activeSlot);
        _activeSlot = wanted;
        _interpreter.Reset();
        _lastPacket = 0;
        if (wanted >= 0)
        {
            var info = new ControllerInfo(wanted, _slotNames[wanted] ?? "XInput Controller");
            _log.Info(Cat, $"Controller Connected: {info.Label}");
            try { Connected?.Invoke(info); }
            catch (Exception ex) { _log.Error(Cat, "Connected handler failed", ex); }
        }
        PublishList();
    }

    private void PollActive(bool live)
    {
        var slot = _activeSlot;
        var result = XInput.XInputGetState((uint)slot, out var state);
        if (result != XInput.ERROR_SUCCESS)
        {
            // Immediate disconnect detection (don't wait for the 1 s scan).
            _slotConnected[slot] = false;
            _slotNames[slot] = null;
            RaiseDisconnected(slot);
            _activeSlot = -1;
            _interpreter.Reset();
            PublishList();
            return;
        }

        if (state.dwPacketNumber == _lastPacket && _snapshot.LiveReading == live) return;
        _lastPacket = state.dwPacketNumber;

        var g = state.Gamepad;
        var raw = new RawPadState(g.wButtons, g.bLeftTrigger, g.bRightTrigger, g.sThumbLX, g.sThumbLY, g.sThumbRX, g.sThumbRY);
        var reading = _interpreter.Interpret(raw, out var changed);

        if (changed != 0)
        {
            for (var bit = 0; bit < 32; bit++)
            {
                var mask = 1u << bit;
                if ((changed & mask) == 0) continue;
                _sink(InputId.Pad((ControllerButton)bit), (reading.Pressed & mask) != 0);
            }
        }

        _snapshot = _snapshot with { Reading = reading, LiveReading = live };
    }

    private void RaiseDisconnected(int slot)
    {
        var info = new ControllerInfo(slot, _slotNames[slot] ?? _snapshot.Active?.Name ?? "Controller");
        _log.Info(Cat, $"Controller Disconnected: {info.Label}");
        try { Disconnected?.Invoke(info); }
        catch (Exception ex) { _log.Error(Cat, "Disconnected handler failed", ex); }
    }

    private void PublishList()
    {
        var list = new List<ControllerInfo>();
        for (var i = 0; i < XInput.MaxControllers; i++)
            if (_slotConnected[i]) list.Add(new ControllerInfo(i, _slotNames[i] ?? "XInput Controller"));
        var active = _activeSlot >= 0 ? list.FirstOrDefault(c => c.Slot == _activeSlot) : null;
        _snapshot = _snapshot with
        {
            Connected = list,
            Active = active,
            Reading = active is null ? default : _snapshot.Reading,
            LiveReading = active is not null && _snapshot.LiveReading,
        };
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }
}
