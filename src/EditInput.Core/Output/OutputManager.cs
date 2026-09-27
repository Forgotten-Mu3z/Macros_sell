using EditInput.Core.Input;
using EditInput.Core.Logging;

namespace EditInput.Core.Output;

/// <summary>A device-specific injector (SendInput, virtual controller, test recorder...).</summary>
public interface IOutputBackend
{
    bool CanHandle(DeviceKind kind);

    /// <summary>False when the backend cannot currently inject (e.g. no virtual controller driver).</summary>
    bool IsAvailable { get; }

    /// <summary>Injects a single transition. Returns false if the OS rejected it.</summary>
    bool Send(InputId id, bool down);

    /// <summary>Returns the device to a neutral state (all buttons up / triggers zero). Must be idempotent.</summary>
    void Neutralize();
}

public interface IOutputSink
{
    bool Press(InputId id);
    bool Release(InputId id);
    void ReleaseAll(string reason, InputId releaseFirst = default);
    bool IsHeld(InputId id);
    bool AnyHeld { get; }

    /// <summary>Hard gate: while false every press is refused. Releases are always allowed.</summary>
    bool AllowOutput { get; set; }
}

/// <summary>
/// The single owner of every injected input. It tracks what is held so nothing can be pressed twice or left
/// down, and exposes one centralised <see cref="ReleaseAll"/> used by every exit path (disable, emergency
/// stop, profile change, disconnect, crash, shutdown). All members are thread-safe.
/// </summary>
public sealed class OutputManager : IOutputSink
{
    private readonly object _gate = new();
    private readonly List<InputId> _held = new(); // in press order
    private readonly IReadOnlyList<IOutputBackend> _backends;
    private readonly ILogger _log;
    private volatile bool _allowOutput = true;

    public OutputManager(IEnumerable<IOutputBackend> backends, ILogger log)
    {
        _backends = backends.ToList();
        _log = log;
    }

    /// <summary>Raised (under no lock) after an injected transition succeeded.</summary>
    public event Action<InputId, bool>? OutputChanged;

    /// <summary>
    /// Hard gate: while false, <see cref="Press"/> is refused regardless of what the engine asks for.
    /// Set to false on disable / emergency stop as a second line of defence.
    /// </summary>
    public bool AllowOutput
    {
        get => _allowOutput;
        set => _allowOutput = value;
    }

    public bool AnyHeld
    {
        get { lock (_gate) return _held.Count > 0; }
    }

    public IReadOnlyList<InputId> HeldSnapshot()
    {
        lock (_gate) return _held.ToList();
    }

    public bool IsHeld(InputId id)
    {
        lock (_gate) return _held.Contains(id);
    }

    public bool CanOutput(InputId id) =>
        !id.IsNone && _backends.Any(b => b.CanHandle(id.Kind) && b.IsAvailable);

    public bool Press(InputId id)
    {
        if (id.IsNone) return false;
        lock (_gate)
        {
            if (!_allowOutput) return false;
            if (_held.Contains(id)) return false; // never send a duplicate down

            var backend = Find(id);
            if (backend is null)
            {
                _log.Warn("Output", $"No available output device for {id.DisplayName}; press skipped.");
                return false;
            }

            bool ok;
            try { ok = backend.Send(id, true); }
            catch (Exception ex)
            {
                _log.Error("Output", $"Press {id.DisplayName} failed", ex);
                ok = false;
            }

            if (!ok)
            {
                _log.Warn("Output", $"OS rejected press of {id.DisplayName}.");
                return false;
            }
            _held.Add(id);
        }
        Raise(id, true);
        return true;
    }

    public bool Release(InputId id)
    {
        lock (_gate)
        {
            if (!_held.Remove(id)) return false;
            SendUp(id);
        }
        Raise(id, false);
        return true;
    }

    /// <summary>
    /// Releases every held input. <paramref name="releaseFirst"/> (usually Select) goes up before the rest to
    /// avoid accidental held selections; remaining inputs are released newest-first. Afterwards each backend
    /// is neutralised, so even state we failed to track is cleared.
    /// </summary>
    public void ReleaseAll(string reason, InputId releaseFirst = default)
    {
        List<InputId> released;
        lock (_gate)
        {
            released = new List<InputId>(_held.Count);
            if (!releaseFirst.IsNone && _held.Remove(releaseFirst))
            {
                SendUp(releaseFirst);
                released.Add(releaseFirst);
            }
            for (var i = _held.Count - 1; i >= 0; i--)
            {
                SendUp(_held[i]);
                released.Add(_held[i]);
            }
            _held.Clear();

            foreach (var b in _backends)
            {
                try { b.Neutralize(); }
                catch (Exception ex) { _log.Error("Output", "Neutralize failed", ex); }
            }
        }

        if (released.Count > 0)
            _log.Debug("Output", $"ReleaseAllInputs ({reason}): {string.Join(", ", released.Select(r => r.DisplayName))}");
        foreach (var id in released) Raise(id, false);
    }

    private void SendUp(InputId id)
    {
        // Release is attempted even while AllowOutput is false: releasing is always safe.
        var backend = _backends.FirstOrDefault(b => b.CanHandle(id.Kind));
        if (backend is null) return;
        try
        {
            if (!backend.Send(id, false))
                _log.Warn("Output", $"OS rejected release of {id.DisplayName}.");
        }
        catch (Exception ex)
        {
            _log.Error("Output", $"Release {id.DisplayName} failed", ex);
        }
    }

    private IOutputBackend? Find(InputId id) =>
        _backends.FirstOrDefault(b => b.CanHandle(id.Kind) && b.IsAvailable);

    private void Raise(InputId id, bool down)
    {
        try { OutputChanged?.Invoke(id, down); }
        catch (Exception ex) { _log.Error("Output", "OutputChanged handler failed", ex); }
    }
}
