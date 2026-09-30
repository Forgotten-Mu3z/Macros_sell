using EditInput.Core.Engine;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Core.Output;
using EditInput.Core.Profiles;

namespace EditInput.Core.Tests;

public sealed class FakeClock : IClock
{
    public long Now { get; set; } = 1_000_000;
    public long Frequency => 1_000_000; // microseconds
    public double Ms => Now / 1000.0;
}

/// <summary>Records every injected transition as e.g. "P↓" / "P↑", with the time it happened.</summary>
public sealed class RecordingBackend : IOutputBackend
{
    private readonly FakeClock _clock;
    public RecordingBackend(FakeClock clock) => _clock = clock;

    public List<(string Event, double AtMs)> Log { get; } = new();
    public HashSet<InputId> Down { get; } = new();
    public int NeutralizeCount { get; private set; }

    public bool CanHandle(DeviceKind kind) => kind != DeviceKind.None;
    public bool IsAvailable => true;

    public bool Send(InputId id, bool down)
    {
        if (down) Down.Add(id); else Down.Remove(id);
        Log.Add(($"{id.DisplayName}{(down ? "↓" : "↑")}", _clock.Ms));
        return true;
    }

    public void Neutralize() => NeutralizeCount++;

    public IEnumerable<string> Events => Log.Select(l => l.Event);
}

public sealed class FakeProbe : IPhysicalStateProbe
{
    public Dictionary<InputId, bool> State { get; } = new();
    public bool? IsPhysicallyDown(InputId id) => State.TryGetValue(id, out var v) ? v : null;
}

public sealed class EngineFixture
{
    public static readonly InputId E = InputId.Key('E');
    public static readonly InputId P = InputId.Key('P');
    public static readonly InputId R = InputId.Key('R');
    public static readonly InputId C = InputId.Key('C');
    public static readonly InputId F8 = InputId.Key(KeyNames.F8);
    public static readonly InputId F12 = InputId.Key(KeyNames.F12);

    public FakeClock Clock { get; } = new();
    public RecordingBackend Backend { get; }
    public OutputManager Output { get; }
    public MacroEngine Engine { get; }
    public Profile Profile { get; }
    public AppSettings Settings { get; } = new();

    public EngineFixture(Action<Profile>? configure = null, IPhysicalStateProbe? probe = null, bool enable = true)
    {
        Backend = new RecordingBackend(Clock);
        Output = new OutputManager(new[] { Backend }, NullLogger.Instance);
        Engine = new MacroEngine(Output, Clock, NullLogger.Instance, probe);
        // Scenario tests pin their own timings; defaults are covered separately in DefaultTimingTests.
        Profile = new Profile
        {
            ResetBind = R,
            ConfirmBind = C,
            SelectDelayMs = 0,
            ResetDelayMs = 10,
            TapDurationMs = 10,
            ConfirmDelayMs = 10,
        };
        configure?.Invoke(Profile);
        Engine.ApplyConfig(EngineConfig.From(Profile.Normalize(), Settings));
        if (enable) Engine.Enable("test");
    }

    public void Reconfigure(Action<Profile> change)
    {
        change(Profile);
        Engine.ApplyConfig(EngineConfig.From(Profile.Normalize(), Settings));
    }

    public void Down(InputId id) { Engine.HandleRaw(id, true); }
    public void Up(InputId id) { Engine.HandleRaw(id, false); }

    /// <summary>Advances fake time, running every scheduled step exactly at its deadline.</summary>
    public void Advance(double ms)
    {
        var target = Clock.Now + (long)(ms * 1000);
        while (Engine.NextDeadline is { } next && next <= target)
        {
            Clock.Now = Math.Max(Clock.Now, next);
            Engine.Tick();
        }
        Clock.Now = target;
        Engine.Tick();
    }

    public List<string> Events => Backend.Events.ToList();
    public bool NothingHeld => Backend.Down.Count == 0 && !Output.AnyHeld;
}
