using System.Collections.Frozen;
using EditInput.Core.Engine;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Windows.Native;

namespace EditInput.Windows.Input;

/// <summary>
/// Aggregates keyboard, mouse and controller sources and forwards only the configured control inputs to the
/// engine. Owns the hook policy (what to watch, what to block, capture mode). Devices are independent, so
/// Hybrid setups (keyboard Edit + controller Select, or the reverse) need no special handling.
/// </summary>
public sealed class InputManager : IDisposable
{
    private readonly EngineHost _host;
    private readonly ILogger _log;
    private readonly HookThread _hookThread;
    private readonly object _gate = new();
    private EngineConfig _config = new();
    private bool _captureActive;
    private bool _blockingActive;
    private bool _ignoreForeignInjected;
    private volatile FrozenSet<InputId> _watchedControllerInputs = FrozenSet<InputId>.Empty;
    private volatile bool _forwardAllController;

    public InputManager(EngineHost host, ILogger log)
    {
        _host = host;
        _log = log;
        _hookThread = new HookThread(log);
        Keyboard = new KeyboardInputManager(_hookThread, Forward, log);
        Mouse = new MouseInputManager(_hookThread, Forward, log);
        Controller = new ControllerInputManager(OnControllerEdge, log);

        _host.CaptureModeChanged += active =>
        {
            lock (_gate) _captureActive = active;
            Controller.CaptureActive = active;
            RebuildPolicy();
        };
        _host.StateChanged += state =>
        {
            var blocking = state is not (EngineState.Disabled or EngineState.EmergencyStopped or EngineState.Error);
            lock (_gate)
            {
                if (_blockingActive == blocking) return;
                _blockingActive = blocking;
            }
            RebuildPolicy();
        };
    }

    public KeyboardInputManager Keyboard { get; }
    public MouseInputManager Mouse { get; }
    public ControllerInputManager Controller { get; }

    public void Start()
    {
        Keyboard.Install();
        Controller.Start();
        RebuildPolicy();
    }

    public void ApplyConfig(EngineConfig config, bool ignoreForeignInjected)
    {
        lock (_gate)
        {
            _config = config;
            _ignoreForeignInjected = ignoreForeignInjected;
        }
        RebuildPolicy();
    }

    private void RebuildPolicy()
    {
        HookPolicy policy;
        FrozenSet<InputId> padWatched;
        bool capture;
        lock (_gate)
        {
            var watched = _config.WatchedInputs();
            var blocked = _blockingActive ? _config.BlockedInputs() : System.Collections.Immutable.ImmutableHashSet<InputId>.Empty;
            capture = _captureActive;
            policy = new HookPolicy(
                watched.ToFrozenSet(),
                blocked.ToFrozenSet(),
                forwardAll: capture,
                swallowKeyboard: capture,
                ignoreForeignInjected: _ignoreForeignInjected);
            padWatched = watched.Where(w => w.Kind == DeviceKind.Controller).ToFrozenSet();
        }

        Keyboard.Policy = policy;
        Mouse.Policy = policy;
        _watchedControllerInputs = padWatched;
        _forwardAllController = capture;
        Controller.BindsNeedController = padWatched.Count > 0;
    }

    private void Forward(InputId id, bool down) => _host.PostInput(id, down);

    private void OnControllerEdge(InputId id, bool down)
    {
        if (_forwardAllController || _watchedControllerInputs.Contains(id))
            _host.PostInput(id, down);
    }

    public void Dispose()
    {
        try { Mouse.Dispose(); } catch (Exception ex) { _log.Error("Input", "Mouse hook dispose failed", ex); }
        try { Keyboard.Dispose(); } catch (Exception ex) { _log.Error("Input", "Keyboard hook dispose failed", ex); }
        try { Controller.Dispose(); } catch (Exception ex) { _log.Error("Input", "Controller dispose failed", ex); }
        _hookThread.Dispose();
    }
}

/// <summary>Watchdog probe: asks Windows / the poller whether a physical input is really still held.</summary>
public sealed class WindowsPhysicalStateProbe : IPhysicalStateProbe
{
    private readonly Func<ControllerInputManager?> _controller;

    public WindowsPhysicalStateProbe(Func<ControllerInputManager?> controller) => _controller = controller;

    public bool? IsPhysicallyDown(InputId id)
    {
        switch (id.Kind)
        {
            case DeviceKind.Keyboard:
                return (NativeMethods.GetAsyncKeyState(id.Code) & 0x8000) != 0;
            case DeviceKind.Mouse:
                var vk = id.MouseButton switch
                {
                    MouseButton.Left => 0x01,
                    MouseButton.Right => 0x02,
                    MouseButton.Middle => 0x04,
                    MouseButton.X1 => 0x05,
                    MouseButton.X2 => 0x06,
                    _ => 0,
                };
                if (vk == 0) return null;
                // With swapped buttons the async state and hook messages disagree on left/right; don't guess.
                if (vk is 0x01 or 0x02 && NativeMethods.GetSystemMetrics(NativeMethods.SM_SWAPBUTTON) != 0) return null;
                return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
            case DeviceKind.Controller:
                return _controller()?.IsPhysicallyDown(id.ControllerButton);
            default:
                return null;
        }
    }
}
