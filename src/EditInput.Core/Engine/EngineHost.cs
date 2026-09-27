using System.Collections.Concurrent;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Core.Output;

namespace EditInput.Core.Engine;

/// <summary>Blocks the engine thread until signalled or until a deadline (clock ticks) passes.</summary>
public interface IPreciseWaiter : IDisposable
{
    void Wait(WaitHandle signal, long? deadline, IClock clock);
}

/// <summary>
/// Portable waiter: kernel wait for the bulk of the delay, short spin for the final ~1.5 ms.
/// On Windows the app uses a high-resolution waitable timer instead (see EditInput.Windows).
/// </summary>
public sealed class HybridWaiter : IPreciseWaiter
{
    public void Wait(WaitHandle signal, long? deadline, IClock clock)
    {
        if (deadline is null)
        {
            signal.WaitOne();
            return;
        }

        while (true)
        {
            var remainingMs = (deadline.Value - clock.Now) * 1000.0 / clock.Frequency;
            if (remainingMs <= 0) return;
            if (remainingMs > 2.0)
            {
                if (signal.WaitOne(TimeSpan.FromMilliseconds(remainingMs - 1.5))) return;
                continue;
            }
            if (signal.WaitOne(0)) return;
            Thread.SpinWait(64);
        }
    }

    public void Dispose() { }
}

/// <summary>
/// Owns the dedicated input worker thread. Hooks, the controller poller and the UI only ever *post* messages;
/// the <see cref="MacroEngine"/> is touched exclusively on this thread. The UI thread never blocks on it.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private const string Cat = "Host";

    private enum MsgKind { Input, Command }

    private readonly record struct Msg(MsgKind Kind, InputId Id, bool Down, Action<MacroEngine>? Command);

    private sealed class CaptureSession
    {
        public required Action<InputId> OnCaptured { get; init; }
        public required Action<InputId?> OnCompleted { get; init; }
        public InputStateTracker Tracker { get; } = new();
        public InputId Captured { get; set; }
        public bool WaitingForRelease { get; set; }
        public long Deadline { get; set; }
    }

    private readonly MacroEngine _engine;
    private readonly OutputManager _output;
    private readonly IClock _clock;
    private readonly ILogger _log;
    private readonly IPreciseWaiter _waiter;
    private readonly ConcurrentQueue<Msg> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private volatile bool _stopping;
    private volatile EngineSnapshot _snapshot = EngineSnapshot.Initial;
    private CaptureSession? _capture; // engine thread only
    private int _disposed;

    public EngineHost(MacroEngine engine, OutputManager output, IClock clock, ILogger log, IPreciseWaiter waiter)
    {
        _engine = engine;
        _output = output;
        _clock = clock;
        _log = log;
        _waiter = waiter;
        _engine.StateChanged += s => StateChanged?.Invoke(s);
        _thread = new Thread(Run) { IsBackground = true, Name = "InputEngine", Priority = ThreadPriority.Highest };
    }

    /// <summary>Latest engine snapshot; safe to read from any thread.</summary>
    public EngineSnapshot Snapshot => _snapshot;

    /// <summary>Raised on the engine thread. Subscribers must be fast and must marshal UI work.</summary>
    public event Action<EngineState>? StateChanged;

    /// <summary>Raised on the engine thread when bind capture starts/stops (hooks switch to forward-all).</summary>
    public event Action<bool>? CaptureModeChanged;

    public void Start() => _thread.Start();

    public bool IsEngineThread => Thread.CurrentThread == _thread;

    // ───── thread-safe API ─────

    public void PostInput(InputId id, bool down)
    {
        if (_stopping) return;
        _queue.Enqueue(new Msg(MsgKind.Input, id, down, null));
        Wake();
    }

    public void Post(Action<MacroEngine> command)
    {
        if (_stopping) return;
        _queue.Enqueue(new Msg(MsgKind.Command, default, false, command));
        Wake();
    }

    private void Wake()
    {
        try { _signal.Set(); }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    public void Enable(string reason) => Post(e => e.Enable(reason));
    public void Disable(string reason) => Post(e => e.Disable(reason));
    public void ApplyConfig(EngineConfig cfg, string reason = "Profile Changed") => Post(e => e.ApplyConfig(cfg, reason));
    public void ControllerDisconnected(string name) => Post(e => e.OnControllerDisconnected(name));
    public void SessionChanged(string reason) => Post(e => e.OnSessionChanged(reason));

    /// <summary>
    /// Releases everything immediately on the calling thread (no queue latency), then puts the engine into
    /// emergency stop. Safe from any thread, including crash handlers.
    /// </summary>
    public void EmergencyStop(string reason)
    {
        _output.AllowOutput = false; // refuse any press the engine thread might still be about to make
        ReleaseAllNow("Emergency Stop");
        Post(e => e.EmergencyStop(reason));
    }

    /// <summary>Centralised ReleaseAllInputs() that may be invoked from any thread.</summary>
    public void ReleaseAllNow(string reason)
    {
        try { _output.ReleaseAll(reason, _engine.Config.Select); }
        catch (Exception ex) { _log.Error(Cat, "ReleaseAll failed", ex); }
    }

    /// <summary>
    /// Captures the next physical input for a bind. Automation is suspended until the captured input has been
    /// released again, so the bind press itself can never trigger a macro. Escape cancels.
    /// </summary>
    public void BeginCapture(Action<InputId> onCaptured, Action<InputId?> onCompleted, TimeSpan timeout) =>
        Post(e =>
        {
            FinishCapture(null);
            e.SetSuspended(true);
            _capture = new CaptureSession
            {
                OnCaptured = onCaptured,
                OnCompleted = onCompleted,
                Deadline = _clock.Now + (long)(timeout.TotalSeconds * _clock.Frequency),
            };
            CaptureModeChanged?.Invoke(true);
        });

    public void CancelCapture() => Post(_ => FinishCapture(null));

    // ───── engine thread ─────

    private void Run()
    {
        _log.Info(Cat, "Input engine thread started");
        while (!_stopping)
        {
            try
            {
                while (!_stopping && _queue.TryDequeue(out var msg)) Process(msg);
                if (_stopping) break;
                _engine.Tick();
                CheckCaptureTimeout();
                Publish();

                long? deadline = _engine.NextDeadline;
                if (_capture is not null)
                    deadline = deadline is null ? _capture.Deadline : Math.Min(deadline.Value, _capture.Deadline);
                if (_queue.IsEmpty) _waiter.Wait(_signal, deadline, _clock);
            }
            catch (Exception ex)
            {
                HandleFault(ex);
            }
        }

        ReleaseAllNow("Shutdown");
        _log.Info(Cat, "Input engine thread stopped");
    }

    private void Process(in Msg msg)
    {
        if (msg.Kind == MsgKind.Command)
        {
            msg.Command!(_engine);
            return;
        }

        if (_capture is not null)
        {
            ProcessCapture(msg.Id, msg.Down);
            return;
        }
        _engine.HandleRaw(msg.Id, msg.Down);
    }

    private void ProcessCapture(InputId id, bool down)
    {
        var c = _capture!;
        var edge = c.Tracker.Update(id, down, _clock.Now);
        if (edge == EdgeKind.Pressed && !c.WaitingForRelease)
        {
            if (id == InputId.Key(KeyNames.Escape))
            {
                FinishCapture(null);
                return;
            }
            c.Captured = id;
            c.WaitingForRelease = true;
            c.Deadline = _clock.Now + 5 * _clock.Frequency;
            SafeInvoke(() => c.OnCaptured(id));
        }
        else if (edge == EdgeKind.Released && c.WaitingForRelease && id == c.Captured)
        {
            FinishCapture(c.Captured);
        }
    }

    private void CheckCaptureTimeout()
    {
        if (_capture is { } c && _clock.Now >= c.Deadline)
            FinishCapture(c.WaitingForRelease ? c.Captured : null);
    }

    private void FinishCapture(InputId? result)
    {
        var c = _capture;
        if (c is null) return;
        _capture = null;
        CaptureModeChanged?.Invoke(false);
        _engine.SetSuspended(false);
        SafeInvoke(() => c.OnCompleted(result));
    }

    private void Publish()
    {
        var cfg = _engine.Config;
        var snap = new EngineSnapshot(
            _engine.State,
            !cfg.Edit.IsNone && _engine.IsPhysicallyDown(cfg.Edit),
            _output.IsHeld(cfg.Select),
            _output.IsHeld(cfg.Reset),
            _output.IsHeld(cfg.Confirm),
            _capture is not null,
            _engine.LastError,
            cfg.ProfileName);
        if (snap != _snapshot) _snapshot = snap;
    }

    private void HandleFault(Exception ex)
    {
        _log.Error(Cat, "Unhandled error in input engine", ex);
        try { _engine.Fault(ex.Message); }
        catch (Exception inner)
        {
            _log.Error(Cat, "Fault handling failed", inner);
            ReleaseAllNow("Error");
        }
    }

    private void SafeInvoke(Action a)
    {
        try { a(); }
        catch (Exception ex) { _log.Error(Cat, "Capture callback failed", ex); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping = true;
        _signal.Set();
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(2)))
            _log.Warn(Cat, "Input engine thread did not stop in time");
        ReleaseAllNow("Shutdown");
        _output.AllowOutput = false;
        _waiter.Dispose();
        _signal.Dispose();
    }
}
