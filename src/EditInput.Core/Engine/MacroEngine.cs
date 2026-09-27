using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Core.Output;
using EditInput.Core.Profiles;

namespace EditInput.Core.Engine;

/// <summary>
/// The edit state machine. Single-threaded by design: every method must be called from the engine thread
/// (see <see cref="EngineHost"/>), which removes all races between input edges, timers and commands.
///
/// Timed steps are never "sleeps": the engine records one pending step with a deadline and the host calls
/// <see cref="Tick"/> when it is due. Only one timed step can be pending, which makes duplicate Select/Reset
/// actions structurally impossible. Every path that leaves an active state goes through
/// <see cref="IOutputSink.ReleaseAll"/>.
/// </summary>
public sealed class MacroEngine
{
    private const string Cat = "Engine";
    private const double WatchdogIntervalMs = 25;
    private const double WatchdogGraceMs = 60;
    private const double StaleStateMs = 1500;

    private enum Step
    {
        None,
        AfterSelectDelay,
        ResetUp,
        StartSelect,
        TapSelectUp,
        ConfirmDown,
        ConfirmUp,
    }

    private readonly IOutputSink _out;
    private readonly IClock _clock;
    private readonly ILogger _log;
    private readonly IPhysicalStateProbe? _probe;
    private readonly InputStateTracker _tracker = new();
    private readonly Dictionary<InputId, long> _lastEdgeAt = new();
    private readonly Dictionary<InputId, int> _watchdogMisses = new();

    private EngineConfig _cfg = new();
    private EngineState _state = EngineState.Disabled;
    private Step _pending;
    private long _pendingAt;
    private long _nextWatchdogAt = long.MaxValue;
    private bool _toggleLatched;
    private bool _confirmIssued;
    private bool _suspended;

    public MacroEngine(IOutputSink output, IClock clock, ILogger log, IPhysicalStateProbe? probe = null)
    {
        _out = output;
        _clock = clock;
        _log = log;
        _probe = probe;
    }

    public event Action<EngineState>? StateChanged;

    public EngineState State => _state;
    public EngineConfig Config => _cfg;
    public string? LastError { get; private set; }
    public bool IsSuspended => _suspended;

    public bool IsEnabled => _state is not (EngineState.Disabled or EngineState.EmergencyStopped or EngineState.Error);

    public bool IsPhysicallyDown(InputId id) => _tracker.IsDown(id);

    /// <summary>Earliest timestamp at which <see cref="Tick"/> has work to do, or null if nothing is scheduled.</summary>
    public long? NextDeadline
    {
        get
        {
            var d = Math.Min(_pending != Step.None ? _pendingAt : long.MaxValue, _nextWatchdogAt);
            return d == long.MaxValue ? null : d;
        }
    }

    // ───────────────────────────── commands ─────────────────────────────

    public void ApplyConfig(EngineConfig cfg, string reason = "Profile Changed")
    {
        CancelAndRelease(reason);
        _cfg = cfg;
        if (IsEnabled) SetState(EngineState.Idle);
        _log.Debug(Cat, $"{reason}: '{cfg.ProfileName}' ({cfg.Mode})");
        UpdateWatchdog();
    }

    public void Enable(string reason)
    {
        if (IsEnabled) return;
        LastError = null;
        ReconcileTracker();
        _out.AllowOutput = true;
        SetState(EngineState.Idle);
        _log.Info(Cat, $"Enabled ({reason})");
    }

    public void Disable(string reason)
    {
        if (_state == EngineState.Disabled) return;
        if (_state is EngineState.EmergencyStopped or EngineState.Error)
        {
            // Already released and gated. Staying here keeps "only the Enable button restarts" true:
            // moving to Disabled would let the toggle hotkey re-enable after an emergency stop.
            CancelAndRelease(reason);
            return;
        }
        CancelAndRelease(reason);
        _out.AllowOutput = false;
        SetState(EngineState.Disabled);
        _log.Info(Cat, $"Disabled ({reason})");
    }

    public void EmergencyStop(string reason = "hotkey")
    {
        _out.ReleaseAll("Emergency Stop", _cfg.Select);
        _out.AllowOutput = false;
        CancelAndRelease("Emergency Stop");
        SetState(EngineState.EmergencyStopped);
        _log.Warn(Cat, $"EMERGENCY STOP ({reason}) - all generated inputs released; click Enable to resume.");
    }

    public void Fault(string message)
    {
        _out.ReleaseAll("Error", _cfg.Select);
        _out.AllowOutput = false;
        CancelAndRelease("Error");
        LastError = message;
        SetState(EngineState.Error);
        _log.Error(Cat, $"Engine stopped after an error: {message}");
    }

    /// <summary>Suspends automation (e.g. while capturing a bind). Inputs are still tracked for edges.</summary>
    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        if (suspended)
        {
            CancelAndRelease("Bind capture");
            if (IsEnabled) SetState(EngineState.Idle);
        }
        UpdateWatchdog();
    }

    public void OnControllerDisconnected(string name)
    {
        _log.Info("Controller", $"Controller Disconnected ({name})");
        _tracker.ClearDevice(DeviceKind.Controller);
        CancelAndRelease("Controller Disconnected");
        if (IsEnabled) SetState(EngineState.Idle);
        UpdateWatchdog();
    }

    /// <summary>Lock/unlock, suspend, logoff: hooks can miss releases, so forget all physical state.</summary>
    public void OnSessionChanged(string reason)
    {
        _tracker.Clear();
        CancelAndRelease(reason);
        if (IsEnabled) SetState(EngineState.Idle);
        _log.Info(Cat, $"Session change ({reason}) - inputs released");
        UpdateWatchdog();
    }

    // ───────────────────────────── input ─────────────────────────────

    /// <summary>Feeds one raw physical event (auto-repeat included). Only real transitions do anything.</summary>
    public void HandleRaw(InputId id, bool down)
    {
        var now = _clock.Now;
        var edge = _tracker.Update(id, down, now, MsToTicks(StaleStateMs), out var recovered);
        if (recovered)
        {
            // We missed a release earlier (the OS never delivered it). Finish the old activation first.
            _log.Warn(Cat, $"Recovered missed release of {id.DisplayName}");
            HandleEdge(id, false, now);
        }
        if (edge == EdgeKind.None) return;
        HandleEdge(id, edge == EdgeKind.Pressed, now);
    }

    private void HandleEdge(InputId id, bool down, long now)
    {
        _lastEdgeAt[id] = now;
        _watchdogMisses.Remove(id);

        if (id == _cfg.EmergencyStop)
        {
            if (down) EmergencyStop();
            return;
        }

        if (id == _cfg.Toggle)
        {
            if (!down) return;
            if (_state is EngineState.EmergencyStopped or EngineState.Error)
                _log.Info(Cat, "Toggle hotkey ignored: click Enable to leave emergency stop.");
            else if (IsEnabled) Disable("toggle hotkey");
            else Enable("toggle hotkey");
            return;
        }

        if (!IsEnabled || _suspended || !_cfg.DeviceAllowed(id))
        {
            UpdateWatchdog();
            return;
        }

        if (_cfg.Mode == EngineMode.SimpleRemap)
        {
            HandleRemap(id, down);
        }
        else if (id == _cfg.Edit)
        {
            _log.Debug(Cat, $"Edit {(down ? "Down" : "Up")} ({id.DisplayName})");
            if (down) OnEditDown();
            else OnEditUp();
        }

        UpdateWatchdog();
    }

    /// <summary>Runs due timed steps and the release watchdog. Call whenever <see cref="NextDeadline"/> passes.</summary>
    public void Tick()
    {
        var now = _clock.Now;
        var guard = 0;
        while (_pending != Step.None && now >= _pendingAt && guard++ < 16)
        {
            var step = _pending;
            _pending = Step.None;
            Execute(step);
        }

        if (now >= _nextWatchdogAt) RunWatchdog(now);
        UpdateWatchdog();
    }

    // ───────────────────────────── edit automation ─────────────────────────────

    private void OnEditDown()
    {
        switch (_state)
        {
            case EngineState.Idle:
                BeginActivation();
                break;

            case EngineState.Releasing:
                // A confirm is still in flight: finish cleanly and start the new activation right away.
                CancelAndRelease("new Edit press");
                SetState(EngineState.Idle);
                BeginActivation();
                break;

            case EngineState.Selecting when _cfg.SelectMode == SelectMode.Toggle && _toggleLatched:
                ToggleOff();
                break;

            case EngineState.EditPressed or EngineState.Resetting or EngineState.WaitingForSelect
                when _cfg.SelectMode == SelectMode.Toggle:
                CancelAndRelease("toggle cancelled before select");
                SetState(EngineState.Idle);
                break;

            default:
                _log.Debug(Cat, $"Edit press ignored in state {_state}");
                break;
        }
    }

    private void OnEditUp()
    {
        // Toggle: releasing Edit never releases Select; the next Edit press does.
        if (_cfg.SelectMode == SelectMode.Toggle) return;

        switch (_state)
        {
            case EngineState.EditPressed or EngineState.Resetting or EngineState.WaitingForSelect:
                // Rapid tap: Select never started. Release whatever the sequence was holding (Reset).
                CancelAndRelease("Edit released before Select");
                SetState(EngineState.Idle);
                break;

            case EngineState.Selecting:
                if (_pending == Step.TapSelectUp)
                {
                    _pending = Step.None;
                    Execute(Step.TapSelectUp);
                }

                var selectWasHeld = _out.IsHeld(_cfg.Select);
                if (selectWasHeld) ReleaseLogged(_cfg.Select, "Select Up");

                var confirmInFlight = _pending is Step.ConfirmDown or Step.ConfirmUp;
                if (!confirmInFlight) _out.ReleaseAll("Edit released");

                if (_cfg.AutoConfirm == AutoConfirmMode.ConfirmOnEditRelease ||
                    (_cfg.AutoConfirm == AutoConfirmMode.ConfirmAfterSelectRelease && selectWasHeld))
                    BeginConfirm();

                if (_pending is Step.ConfirmDown or Step.ConfirmUp) SetState(EngineState.Releasing);
                else FinishToIdle("Edit released");
                break;
        }
    }

    private void BeginActivation()
    {
        _confirmIssued = false;
        _toggleLatched = false;
        SetState(EngineState.EditPressed);
        Schedule(Step.AfterSelectDelay, _cfg.SelectDelayMs);
    }

    private void Execute(Step step)
    {
        switch (step)
        {
            case Step.AfterSelectDelay:
                if (_cfg.ResetBeforeSelect && !_cfg.Reset.IsNone)
                {
                    // Reset happens exactly once per activation: this step is only ever scheduled by BeginActivation.
                    PressLogged(_cfg.Reset, "Reset Down");
                    SetState(EngineState.Resetting);
                    Schedule(Step.ResetUp, _cfg.TapDurationMs);
                }
                else
                {
                    Execute(Step.StartSelect);
                }
                break;

            case Step.ResetUp:
                ReleaseLogged(_cfg.Reset, "Reset Up");
                SetState(EngineState.WaitingForSelect);
                Schedule(Step.StartSelect, _cfg.ResetDelayMs);
                break;

            case Step.StartSelect:
                SetState(EngineState.Selecting);
                if (_cfg.Select.IsNone)
                {
                    _log.Warn(Cat, "Select bind is not set; nothing to hold.");
                    break;
                }
                PressLogged(_cfg.Select, "Select Down");
                if (_cfg.SelectMode == SelectMode.Toggle) _toggleLatched = true;
                else if (_cfg.SelectMode == SelectMode.TapOnce) Schedule(Step.TapSelectUp, _cfg.TapDurationMs);
                break;

            case Step.TapSelectUp:
                ReleaseLogged(_cfg.Select, "Select Up");
                if (_cfg.AutoConfirm == AutoConfirmMode.ConfirmAfterSelectRelease) BeginConfirm();
                break;

            case Step.ConfirmDown:
                PressLogged(_cfg.Confirm, "Confirm Down");
                Schedule(Step.ConfirmUp, _cfg.TapDurationMs);
                break;

            case Step.ConfirmUp:
                ReleaseLogged(_cfg.Confirm, "Confirm Up");
                if (_state == EngineState.Releasing) FinishToIdle("Confirm complete");
                break;
        }
    }

    private void ToggleOff()
    {
        _toggleLatched = false;
        if (_out.IsHeld(_cfg.Select)) ReleaseLogged(_cfg.Select, "Select Up");
        _pending = Step.None;
        _out.ReleaseAll("Toggle off");
        if (_cfg.AutoConfirm != AutoConfirmMode.Off) BeginConfirm();
        if (_pending is Step.ConfirmDown or Step.ConfirmUp) SetState(EngineState.Releasing);
        else FinishToIdle("Toggle off");
    }

    private void BeginConfirm()
    {
        if (_confirmIssued || _cfg.AutoConfirm == AutoConfirmMode.Off || _cfg.Confirm.IsNone) return;
        _confirmIssued = true;
        Schedule(Step.ConfirmDown, _cfg.ConfirmDelayMs);
    }

    private void FinishToIdle(string reason)
    {
        _pending = Step.None;
        _toggleLatched = false;
        _out.ReleaseAll(reason, _cfg.Select);
        SetState(IsEnabled ? EngineState.Idle : _state);
    }

    // ───────────────────────────── remap ─────────────────────────────

    private void HandleRemap(InputId source, bool down)
    {
        foreach (var r in _cfg.Remaps)
        {
            if (r.Source != source) continue;
            if (down) PressLogged(r.Target, $"Remap {source.DisplayName} → Down");
            else ReleaseLogged(r.Target, $"Remap {source.DisplayName} → Up");
            break; // strict one-to-one
        }
        SetState(_out.AnyHeld ? EngineState.RemapActive : EngineState.Idle);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private void Schedule(Step step, int delayMs)
    {
        if (delayMs <= 0)
        {
            Execute(step);
            return;
        }
        _pending = step;
        _pendingAt = _clock.Now + MsToTicks(delayMs);
    }

    private void CancelAndRelease(string reason)
    {
        _pending = Step.None;
        _toggleLatched = false;
        _out.ReleaseAll(reason, _cfg.Select);
    }

    private void PressLogged(InputId id, string what)
    {
        if (_out.Press(id)) _log.Debug(Cat, $"{what} ({id.DisplayName})");
    }

    private void ReleaseLogged(InputId id, string what)
    {
        if (_out.Release(id)) _log.Debug(Cat, $"{what} ({id.DisplayName})");
    }

    private void SetState(EngineState s)
    {
        if (_state == s) return;
        _state = s;
        _log.Debug(Cat, $"State → {s}");
        StateChanged?.Invoke(s);
    }

    private long MsToTicks(double ms) => (long)(ms * _clock.Frequency / 1000.0);

    private void ReconcileTracker()
    {
        if (_probe is null) return;
        foreach (var id in _tracker.DownInputs.ToList())
            if (_probe.IsPhysicallyDown(id) == false) _tracker.Forget(id);
    }

    // ───────────────────────────── release watchdog ─────────────────────────────

    /// <summary>Inputs whose release we are waiting for and which the probe can verify.</summary>
    private IEnumerable<InputId> WatchdogCandidates()
    {
        if (_probe is null || !IsEnabled || _suspended) yield break;

        if (_cfg.Mode == EngineMode.EditAutomation)
        {
            var active = _state is EngineState.EditPressed or EngineState.Resetting or EngineState.WaitingForSelect or EngineState.Selecting;
            // In Toggle mode Select is intentionally latched after Edit is released.
            if (active && _cfg.SelectMode != SelectMode.Toggle && _tracker.IsDown(_cfg.Edit) &&
                !_cfg.OutputInputs().Contains(_cfg.Edit))
                yield return _cfg.Edit;
        }
        else if (_state == EngineState.RemapActive)
        {
            var blocked = _cfg.BlockedInputs();
            var targets = _cfg.Remaps.Select(r => r.Target).ToHashSet();
            foreach (var r in _cfg.Remaps)
            {
                // Swallowed inputs never reach the OS key state, so they can't be verified by polling.
                if (_tracker.IsDown(r.Source) && !blocked.Contains(r.Source) && !targets.Contains(r.Source))
                    yield return r.Source;
            }
        }
    }

    private void UpdateWatchdog()
    {
        var now = _clock.Now;
        var next = long.MaxValue;
        foreach (var id in WatchdogCandidates())
        {
            _lastEdgeAt.TryGetValue(id, out var edgeAt);
            next = Math.Min(next, Math.Max(edgeAt + MsToTicks(WatchdogGraceMs), now + MsToTicks(WatchdogIntervalMs)));
        }

        if (next == long.MaxValue)
            _nextWatchdogAt = long.MaxValue;
        else if (_nextWatchdogAt == long.MaxValue || _nextWatchdogAt <= now || _nextWatchdogAt > next)
            _nextWatchdogAt = next; // otherwise keep the earlier, still-pending check
    }

    private void RunWatchdog(long now)
    {
        _nextWatchdogAt = long.MaxValue;
        foreach (var id in WatchdogCandidates().ToList())
        {
            _lastEdgeAt.TryGetValue(id, out var edgeAt);
            if (now - edgeAt < MsToTicks(WatchdogGraceMs)) continue;

            if (_probe!.IsPhysicallyDown(id) == false)
            {
                var misses = _watchdogMisses.GetValueOrDefault(id) + 1;
                _watchdogMisses[id] = misses;
                if (misses >= 2) // two consecutive polls: avoids racing the hook-before-keystate ordering
                {
                    _log.Warn(Cat, $"Watchdog: {id.DisplayName} is no longer held but its release was never received - releasing.");
                    _tracker.Update(id, false, now);
                    HandleEdge(id, false, now);
                }
            }
            else
            {
                _watchdogMisses.Remove(id);
            }
        }
    }
}
