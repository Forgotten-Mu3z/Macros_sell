namespace EditInput.Core.Input;

public enum EdgeKind
{
    None,
    Pressed,
    Released,
}

/// <summary>
/// Edge detector for physical inputs. Converts the raw stream (which includes keyboard auto-repeat downs)
/// into clean pressed/released transitions. Not thread-safe: owned by the engine thread.
/// </summary>
public sealed class InputStateTracker
{
    private readonly Dictionary<InputId, long> _downSince = new();
    private readonly Dictionary<InputId, long> _lastEvent = new();

    public bool IsDown(InputId id) => _downSince.ContainsKey(id);

    public IReadOnlyCollection<InputId> DownInputs => _downSince.Keys;

    /// <summary>
    /// Applies a raw event. <paramref name="staleAfterTicks"/> guards against a missed release (e.g. a key-up
    /// swallowed by a secure desktop): a repeated "down" after a long silence is treated as a fresh press.
    /// Windows keyboard auto-repeat never leaves gaps longer than ~1 s, so a 1.5 s threshold is safe.
    /// </summary>
    public EdgeKind Update(InputId id, bool down, long timestamp, long staleAfterTicks, out bool recoveredStaleState)
    {
        recoveredStaleState = false;
        _lastEvent.TryGetValue(id, out var last);
        _lastEvent[id] = timestamp;

        if (down)
        {
            if (_downSince.ContainsKey(id))
            {
                if (staleAfterTicks > 0 && timestamp - last > staleAfterTicks)
                {
                    recoveredStaleState = true;
                    _downSince[id] = timestamp;
                    return EdgeKind.Pressed;
                }
                return EdgeKind.None; // auto-repeat or duplicate
            }
            _downSince[id] = timestamp;
            return EdgeKind.Pressed;
        }

        return _downSince.Remove(id) ? EdgeKind.Released : EdgeKind.None;
    }

    public EdgeKind Update(InputId id, bool down, long timestamp) => Update(id, down, timestamp, 0, out _);

    /// <summary>Forget a single input without producing an edge.</summary>
    public void Forget(InputId id)
    {
        _downSince.Remove(id);
        _lastEvent.Remove(id);
    }

    public void ClearDevice(DeviceKind kind)
    {
        foreach (var id in _downSince.Keys.Where(k => k.Kind == kind).ToList())
            Forget(id);
    }

    public void Clear()
    {
        _downSince.Clear();
        _lastEvent.Clear();
    }
}
