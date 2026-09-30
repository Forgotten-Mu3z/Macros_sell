using System.Text.Json;
using EditInput.Core.Controller;
using EditInput.Core.Engine;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Core.Output;
using EditInput.Core.Profiles;
using EditInput.Core.Validation;

namespace EditInput.Core.Tests;

public class OutputManagerTests
{
    private static (OutputManager, RecordingBackend) Create()
    {
        var b = new RecordingBackend(new FakeClock());
        return (new OutputManager(new[] { b }, NullLogger.Instance), b);
    }

    [Fact]
    public void DuplicatePress_IsIgnored()
    {
        var (o, b) = Create();
        Assert.True(o.Press(InputId.Key('P')));
        Assert.False(o.Press(InputId.Key('P')));
        Assert.Single(b.Log);
    }

    [Fact]
    public void ReleaseAll_ReleasesPriorityFirst_ThenNewestFirst()
    {
        var (o, b) = Create();
        o.Press(InputId.Key('A'));
        o.Press(InputId.Key('P'));
        o.Press(InputId.Key('C'));
        b.Log.Clear();
        o.ReleaseAll("t", InputId.Key('P'));
        Assert.Equal(new[] { "P↑", "C↑", "A↑" }, b.Events);
        Assert.False(o.AnyHeld);
        Assert.Equal(1, b.NeutralizeCount);
    }

    [Fact]
    public void AllowOutputFalse_BlocksPress_ButReleaseWorks()
    {
        var (o, b) = Create();
        o.Press(InputId.Key('A'));
        o.AllowOutput = false;
        Assert.False(o.Press(InputId.Key('B')));
        Assert.True(o.Release(InputId.Key('A')));
        Assert.Equal(new[] { "A↓", "A↑" }, b.Events);
    }

    [Fact]
    public void Release_OfUnheldInput_SendsNothing()
    {
        var (o, b) = Create();
        Assert.False(o.Release(InputId.Key('A')));
        Assert.Empty(b.Log);
    }

    [Fact]
    public void ConcurrentPressRelease_IsConsistent()
    {
        var (o, b) = Create();
        var ids = Enumerable.Range(0, 8).Select(i => InputId.Key('A' + i)).ToArray();
        Parallel.For(0, 4000, i =>
        {
            var id = ids[i % ids.Length];
            if (i % 3 == 0) o.ReleaseAll("t");
            else if (i % 2 == 0) o.Press(id);
            else o.Release(id);
        });
        o.ReleaseAll("final");
        Assert.False(o.AnyHeld);
        Assert.Empty(b.Down);
    }
}

public class ControllerInterpreterTests
{
    [Fact]
    public void Trigger_UsesThresholdWithHysteresis()
    {
        var i = new ControllerStateInterpreter();
        i.Configure(50, 10);
        byte At(float f) => (byte)Math.Round(f * 255);

        var r = i.Interpret(new RawPadState(0, 0, At(0.49f), 0, 0, 0, 0), out _);
        Assert.False(r.IsPressed(ControllerButton.RT));
        r = i.Interpret(new RawPadState(0, 0, At(0.51f), 0, 0, 0, 0), out var changed);
        Assert.True(r.IsPressed(ControllerButton.RT));
        Assert.NotEqual(0u, changed);
        // Just below the threshold: still pressed thanks to hysteresis (no flicker).
        r = i.Interpret(new RawPadState(0, 0, At(0.48f), 0, 0, 0, 0), out changed);
        Assert.True(r.IsPressed(ControllerButton.RT));
        Assert.Equal(0u, changed);
        r = i.Interpret(new RawPadState(0, 0, At(0.40f), 0, 0, 0, 0), out _);
        Assert.False(r.IsPressed(ControllerButton.RT));
    }

    [Fact]
    public void Buttons_MapFromXInputBits()
    {
        var i = new ControllerStateInterpreter();
        var r = i.Interpret(new RawPadState((ushort)(RawPadState.A | RawPadState.RightShoulder | RawPadState.Back), 0, 0, 0, 0, 0, 0), out _);
        Assert.True(r.IsPressed(ControllerButton.A));
        Assert.True(r.IsPressed(ControllerButton.RB));
        Assert.True(r.IsPressed(ControllerButton.View));
        Assert.False(r.IsPressed(ControllerButton.B));
    }

    [Fact]
    public void Stick_DeadzoneSuppressesSmallMovement()
    {
        var i = new ControllerStateInterpreter();
        i.Configure(50, 20);
        var r = i.Interpret(new RawPadState(0, 0, 0, 0, 6000, 0, 0), out _); // ~18%
        Assert.Equal(0f, r.LeftY);
        Assert.False(r.IsPressed(ControllerButton.LStickUp));
        r = i.Interpret(new RawPadState(0, 0, 0, 0, 32767, 0, 0), out _);
        Assert.True(r.IsPressed(ControllerButton.LStickUp));
        r = i.Interpret(new RawPadState(0, 0, 0, -32768, 0, 0, 0), out _);
        Assert.True(r.IsPressed(ControllerButton.LStickLeft));
        Assert.False(r.IsPressed(ControllerButton.LStickUp));
    }

    [Fact]
    public void Configure_ClampsRanges()
    {
        var i = new ControllerStateInterpreter();
        i.Configure(1, 90);
        Assert.Equal(0.05f, i.TriggerThreshold, 3);
        Assert.Equal(0.40f, i.StickDeadzone, 3);
    }
}

public class InputIdTests
{
    [Theory]
    [InlineData("Key:E")]
    [InlineData("Key:F12")]
    [InlineData("Key:LShift")]
    [InlineData("Key:VK_0xE9")]
    [InlineData("Mouse:X1")]
    [InlineData("Pad:RT")]
    [InlineData("Pad:LStickLeft")]
    [InlineData("None")]
    public void Serialize_RoundTrips(string text)
    {
        var id = InputId.Parse(text);
        Assert.Equal(text, id.Serialize());
    }

    [Fact]
    public void Json_RoundTrip_AndUnknownBecomesNone()
    {
        var p = new Profile { ResetBind = InputId.Mouse(MouseButton.Right) };
        var json = JsonSerializer.Serialize(p, ProfileJson.Options);
        Assert.Contains("\"Key:E\"", json);
        var back = JsonSerializer.Deserialize<Profile>(json, ProfileJson.Options)!;
        Assert.True(p.ContentEquals(back));

        var bad = JsonSerializer.Deserialize<Profile>("{\"editBind\":\"Banana:7\"}", ProfileJson.Options)!;
        Assert.True(bad.EditBind.IsNone);
    }

    [Fact]
    public void DisplayNames_AreFriendly()
    {
        Assert.Equal("E", InputId.Key('E').DisplayName);
        Assert.Equal("Mouse 4", InputId.Mouse(MouseButton.X1).DisplayName);
        Assert.Equal("Left Stick Click", InputId.Pad(ControllerButton.LS).DisplayName);
    }
}

public class PersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "editinput-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Profiles_SeedDefaults_AndPersistOperations()
    {
        var m = new ProfileManager(_dir, NullLogger.Instance);
        Assert.Contains("Default", m.Names);
        Assert.Contains("Fortnite KBM", m.Names);

        var p = m.Create("Mine");
        p.ResetDelayMs = 42;
        m.Save(p);
        m.Duplicate("Mine", "Mine Copy");
        m.Rename("Mine Copy", "Other");
        m.Delete("Default");

        var reloaded = new ProfileManager(_dir, NullLogger.Instance);
        Assert.Equal(42, reloaded.Get("Mine")!.ResetDelayMs);
        Assert.Equal(42, reloaded.Get("other")!.ResetDelayMs);
        Assert.False(reloaded.Contains("Default"));
        Assert.Throws<ArgumentException>(() => reloaded.Create("MINE"));
    }

    [Fact]
    public void Profiles_CannotDeleteLast()
    {
        var m = new ProfileManager(_dir, NullLogger.Instance);
        foreach (var n in m.Names.Skip(1)) m.Delete(n);
        Assert.Throws<InvalidOperationException>(() => m.Delete(m.Names[0]));
    }

    [Fact]
    public void Profile_Normalize_ClampsValues()
    {
        var p = new Profile { ResetDelayMs = 500, SelectDelayMs = -4, TriggerThresholdPercent = 99, StickDeadzonePercent = 70 }.Normalize();
        Assert.Equal(100, p.ResetDelayMs);
        Assert.Equal(0, p.SelectDelayMs);
        Assert.Equal(95, p.TriggerThresholdPercent);
        Assert.Equal(40, p.StickDeadzonePercent);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        var s = new SettingsManager(_dir, NullLogger.Instance);
        Assert.Equal(InputId.Key(KeyNames.F12), s.Current.EmergencyStop);
        Assert.True(Directory.GetFiles(_dir, "settings.json.corrupt-*").Length == 1);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var s = new SettingsManager(_dir, NullLogger.Instance);
        Assert.True(s.IsFirstRun);
        s.Update(x => { x.ActiveProfile = "Fast Edit"; x.FirstRunCompleted = true; x.ToggleBind = InputId.Key('K'); });
        var again = new SettingsManager(_dir, NullLogger.Instance);
        Assert.False(again.IsFirstRun);
        Assert.Equal("Fast Edit", again.Current.ActiveProfile);
        Assert.Equal(InputId.Key('K'), again.Current.ToggleBind);
    }
}

public class ConflictDetectorTests
{
    [Fact]
    public void SameEditAndSelect_Warns()
    {
        var p = new Profile { SelectBind = InputId.Key('E') };
        var issues = ConflictDetector.Analyze(p, new AppSettings(), true);
        Assert.Contains(issues, i => i.Message.StartsWith("Edit and Select are using the same input"));
    }

    [Fact]
    public void EmergencyStopAsGeneratedOutput_IsError()
    {
        var p = new Profile { SelectBind = InputId.Key(KeyNames.F12) };
        var issues = ConflictDetector.Analyze(p, new AppSettings(), true);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("Emergency Stop"));
    }

    [Fact]
    public void ControllerOutputWithoutDriver_Warns()
    {
        var p = new Profile { SelectBind = InputId.Pad(ControllerButton.RT) };
        Assert.Contains(ConflictDetector.Analyze(p, new AppSettings(), false), i => i.Message.Contains("ViGEmBus"));
        Assert.DoesNotContain(ConflictDetector.Analyze(p, new AppSettings(), true), i => i.Message.Contains("ViGEmBus"));
    }

    [Fact]
    public void DefaultProfile_HasNoWarnings()
    {
        var issues = ConflictDetector.Analyze(new Profile(), new AppSettings(), true);
        Assert.DoesNotContain(issues, i => i.Severity >= IssueSeverity.Warning);
    }

    [Fact]
    public void ToggleAndEdit_Conflict()
    {
        var issues = ConflictDetector.Analyze(new Profile { EditBind = InputId.Key(KeyNames.F8) }, new AppSettings(), true);
        Assert.Contains(issues, i => i.Message.Contains("Edit and Enable/Disable"));
    }
}

public class EngineHostTests
{
    [Fact]
    public void RealThread_HoldAndRelease_AndDisposeReleasesEverything()
    {
        var clock = StopwatchClock.Instance;
        var backend = new ThreadSafeBackend();
        var output = new OutputManager(new[] { backend }, NullLogger.Instance);
        var engine = new MacroEngine(output, clock, NullLogger.Instance);
        using var host = new EngineHost(engine, output, clock, NullLogger.Instance, new HybridWaiter());
        host.Start();
        host.ApplyConfig(EngineConfig.From(new Profile { ResetBind = InputId.Key('R'), ResetBeforeSelect = true, ResetDelayMs = 5 }, new AppSettings()));
        host.Enable("test");

        host.PostInput(InputId.Key('E'), true);
        Assert.True(SpinWait.SpinUntil(() => backend.IsDown(InputId.Key('P')), 2000));
        Assert.Equal(new[] { "R↓", "R↑", "P↓" }, backend.Events());

        host.PostInput(InputId.Key('E'), false);
        Assert.True(SpinWait.SpinUntil(() => !backend.IsDown(InputId.Key('P')), 2000));

        host.PostInput(InputId.Key('E'), true);
        Assert.True(SpinWait.SpinUntil(() => backend.IsDown(InputId.Key('P')), 2000));
        host.Dispose();
        Assert.False(backend.IsDown(InputId.Key('P')));
    }

    [Fact]
    public void Capture_ReturnsInputAfterRelease_AndSuspendsAutomation()
    {
        var clock = StopwatchClock.Instance;
        var backend = new ThreadSafeBackend();
        var output = new OutputManager(new[] { backend }, NullLogger.Instance);
        var engine = new MacroEngine(output, clock, NullLogger.Instance);
        using var host = new EngineHost(engine, output, clock, NullLogger.Instance, new HybridWaiter());
        host.Start();
        host.ApplyConfig(EngineConfig.From(new Profile(), new AppSettings()));
        host.Enable("test");

        InputId? result = null;
        var captured = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        host.BeginCapture(_ => captured.Set(), r => { result = r; done.Set(); }, TimeSpan.FromSeconds(5));
        host.PostInput(InputId.Key('E'), true); // would normally trigger Select
        Assert.True(captured.Wait(2000));
        Assert.False(done.IsSet);               // waits for release
        host.PostInput(InputId.Key('E'), false);
        Assert.True(done.Wait(2000));
        Assert.Equal(InputId.Key('E'), result);
        Assert.Empty(backend.Events());
    }

    [Fact]
    public void Capture_EscapeCancels()
    {
        var clock = StopwatchClock.Instance;
        var output = new OutputManager(new[] { new ThreadSafeBackend() }, NullLogger.Instance);
        using var host = new EngineHost(new MacroEngine(output, clock, NullLogger.Instance), output, clock, NullLogger.Instance, new HybridWaiter());
        host.Start();
        var done = new ManualResetEventSlim();
        InputId? result = InputId.Key('Z');
        host.BeginCapture(_ => { }, r => { result = r; done.Set(); }, TimeSpan.FromSeconds(5));
        host.PostInput(InputId.Key(KeyNames.Escape), true);
        Assert.True(done.Wait(2000));
        Assert.Null(result);
    }

    private sealed class ThreadSafeBackend : IOutputBackend
    {
        private readonly object _g = new();
        private readonly List<string> _events = new();
        private readonly HashSet<InputId> _down = new();
        public bool CanHandle(DeviceKind kind) => true;
        public bool IsAvailable => true;
        public bool Send(InputId id, bool down)
        {
            lock (_g)
            {
                if (down) _down.Add(id); else _down.Remove(id);
                _events.Add(id.DisplayName + (down ? "↓" : "↑"));
            }
            return true;
        }
        public void Neutralize() { }
        public bool IsDown(InputId id) { lock (_g) return _down.Contains(id); }
        public string[] Events() { lock (_g) return _events.ToArray(); }
    }
}

public class ProfileMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "editinput-mig-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Version1File_OldDefaultsBecomeOneMs_CustomValuesKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "profiles.json"), """
        {
          "version": 1,
          "profiles": [
            { "name": "Default", "resetDelayMs": 10, "selectDelayMs": 0, "tapDurationMs": 10, "confirmDelayMs": 10 },
            { "name": "Tuned",   "resetDelayMs": 23, "selectDelayMs": 4, "tapDurationMs": 15, "confirmDelayMs": 30 }
          ]
        }
        """);

        var m = new ProfileManager(_dir, NullLogger.Instance);
        var d = m.Get("Default")!;
        Assert.Equal((1, 1, 1, 1), (d.ResetDelayMs, d.SelectDelayMs, d.TapDurationMs, d.ConfirmDelayMs));
        var t = m.Get("Tuned")!;
        Assert.Equal((23, 4, 15, 30), (t.ResetDelayMs, t.SelectDelayMs, t.TapDurationMs, t.ConfirmDelayMs));

        // Migration runs once: a user can go back to 0 ms select delay and it sticks.
        d.SelectDelayMs = 0;
        m.Save(d);
        Assert.Equal(0, new ProfileManager(_dir, NullLogger.Instance).Get("Default")!.SelectDelayMs);
    }
}
