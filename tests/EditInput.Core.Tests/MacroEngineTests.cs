using EditInput.Core.Engine;
using EditInput.Core.Input;
using EditInput.Core.Profiles;
using static EditInput.Core.Tests.EngineFixture;

namespace EditInput.Core.Tests;

public class MacroEngineTests
{
    [Fact]
    public void Hold_EditDown_PressesSelect_EditUp_ReleasesSelect()
    {
        var f = new EngineFixture();
        f.Down(E);
        Assert.Equal(new[] { "P↓" }, f.Events);
        Assert.Equal(EngineState.Selecting, f.Engine.State);

        f.Up(E);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        Assert.Equal(EngineState.Idle, f.Engine.State);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void ResetBeforeSelect_RunsResetTapThenDelayThenSelect()
    {
        var f = new EngineFixture(p => { p.ResetBeforeSelect = true; p.ResetDelayMs = 10; p.TapDurationMs = 10; });
        var t0 = f.Clock.Ms;

        f.Down(E);
        Assert.Equal(new[] { "R↓" }, f.Events);
        Assert.Equal(EngineState.Resetting, f.Engine.State);

        f.Advance(10);
        Assert.Equal(new[] { "R↓", "R↑" }, f.Events);
        Assert.Equal(EngineState.WaitingForSelect, f.Engine.State);

        f.Advance(10);
        Assert.Equal(new[] { "R↓", "R↑", "P↓" }, f.Events);
        Assert.Equal(EngineState.Selecting, f.Engine.State);
        Assert.Equal(t0 + 10, f.Backend.Log[1].AtMs, 3);
        Assert.Equal(t0 + 20, f.Backend.Log[2].AtMs, 3);

        f.Advance(200);
        f.Up(E);
        Assert.Equal(new[] { "R↓", "R↑", "P↓", "P↑" }, f.Events);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void ResetBeforeSelect_ResetsOnlyOnce_WhileEditHeldWithAutoRepeat()
    {
        var f = new EngineFixture(p => p.ResetBeforeSelect = true);
        f.Down(E);
        for (var i = 0; i < 30; i++)
        {
            f.Advance(33);
            f.Down(E); // keyboard auto-repeat
        }
        f.Up(E);
        Assert.Equal(1, f.Events.Count(e => e == "R↓"));
        Assert.Equal(1, f.Events.Count(e => e == "P↓"));
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void ZeroDelays_ResetStillReleasedBeforeSelect()
    {
        var f = new EngineFixture(p => { p.ResetBeforeSelect = true; p.ResetDelayMs = 0; p.TapDurationMs = 0; });
        f.Down(E);
        Assert.Equal(new[] { "R↓", "R↑", "P↓" }, f.Events);
        f.Up(E);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void SelectDelay_DelaysSelectDown()
    {
        var f = new EngineFixture(p => p.SelectDelayMs = 15);
        var t0 = f.Clock.Ms;
        f.Down(E);
        Assert.Empty(f.Events);
        Assert.Equal(EngineState.EditPressed, f.Engine.State);
        f.Advance(14);
        Assert.Empty(f.Events);
        f.Advance(1);
        Assert.Equal(new[] { "P↓" }, f.Events);
        Assert.Equal(t0 + 15, f.Backend.Log[0].AtMs, 3);
    }

    [Fact]
    public void RapidTap_BeforeSelectStarts_NeverLeavesAnythingHeld()
    {
        var f = new EngineFixture(p => { p.ResetBeforeSelect = true; p.SelectDelayMs = 0; p.TapDurationMs = 10; });
        f.Down(E);
        f.Advance(3);
        f.Up(E); // mid-reset
        f.Advance(100);
        Assert.Equal(new[] { "R↓", "R↑" }, f.Events);
        Assert.True(f.NothingHeld);
        Assert.Equal(EngineState.Idle, f.Engine.State);
    }

    [Fact]
    public void RapidTap_NoDelays_NoStuckSelect()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Up(E);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void RapidTap_DuringSelectDelay_NoSelectAtAll()
    {
        var f = new EngineFixture(p => p.SelectDelayMs = 20);
        f.Down(E);
        f.Advance(5);
        f.Up(E);
        f.Advance(100);
        Assert.Empty(f.Events);
    }

    [Fact]
    public void MultipleRapidPresses_NoDuplicateSelectDown()
    {
        var f = new EngineFixture(p => { p.ResetBeforeSelect = true; p.TapDurationMs = 2; p.ResetDelayMs = 3; });
        var rnd = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            f.Down(E);
            f.Advance(rnd.Next(0, 12));
            if (rnd.Next(3) == 0) f.Down(E); // stray repeat
            f.Up(E);
            f.Advance(rnd.Next(0, 4));
        }
        f.Advance(100);

        var held = new HashSet<string>();
        foreach (var e in f.Events)
        {
            var key = e[..^1];
            if (e.EndsWith('↓')) Assert.True(held.Add(key), $"duplicate down for {key}");
            else Assert.True(held.Remove(key), $"release without press for {key}");
        }
        Assert.Empty(held);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void KeyboardAutoRepeat_DoesNotDuplicateSelect()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Down(E);
        f.Down(E);
        Assert.Equal(new[] { "P↓" }, f.Events);
    }

    [Fact]
    public void DisableWhileSelecting_ReleasesEverything()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Engine.Disable("test");
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        Assert.Equal(EngineState.Disabled, f.Engine.State);
        f.Up(E);
        f.Down(E);
        Assert.Equal(2, f.Events.Count);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void ToggleHotkey_DisablesAndReenables()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Down(F8);
        f.Up(F8);
        Assert.Equal(EngineState.Disabled, f.Engine.State);
        Assert.True(f.NothingHeld);
        f.Down(F8);
        Assert.Equal(EngineState.Idle, f.Engine.State);
    }

    [Fact]
    public void EmergencyStopWhileSelecting_ReleasesAndRequiresManualEnable()
    {
        var f = new EngineFixture(p => { p.ResetBeforeSelect = true; p.TapDurationMs = 20; });
        f.Down(E);                       // Reset held
        f.Down(F12);
        Assert.True(f.NothingHeld);
        Assert.Equal(EngineState.EmergencyStopped, f.Engine.State);

        f.Up(F12);
        f.Down(F8);                      // toggle must NOT leave emergency stop
        f.Up(F8);
        Assert.Equal(EngineState.EmergencyStopped, f.Engine.State);
        f.Advance(100);
        f.Up(E);
        f.Down(E);
        Assert.Equal(new[] { "R↓", "R↑" }, f.Events);
        Assert.False(f.Output.Press(P)); // hard output gate

        f.Up(E);
        f.Engine.Enable("button");
        f.Down(E);
        Assert.Contains("R↓", f.Events.Skip(2));
    }

    [Fact]
    public void EmergencyStop_WorksEvenWhenDisabled()
    {
        var f = new EngineFixture(enable: false);
        f.Down(F12);
        Assert.Equal(EngineState.EmergencyStopped, f.Engine.State);
    }

    [Fact]
    public void ControllerDisconnectWhileSelecting_ReleasesAndRequiresFreshPress()
    {
        var edit = InputId.Pad(ControllerButton.LS);
        var select = InputId.Pad(ControllerButton.RT);
        var f = new EngineFixture(p => { p.EditBind = edit; p.SelectBind = select; });
        f.Down(edit);
        Assert.Equal(new[] { "RT↓" }, f.Events);

        f.Engine.OnControllerDisconnected("pad");
        Assert.Equal(new[] { "RT↓", "RT↑" }, f.Events);
        Assert.True(f.NothingHeld);
        Assert.Equal(EngineState.Idle, f.Engine.State);

        f.Up(edit);                    // stale release after reconnect: ignored
        Assert.Equal(2, f.Events.Count);
        f.Down(edit);                  // fresh press starts a new activation
        Assert.Equal("RT↓", f.Events[^1]);
    }

    [Fact]
    public void ControllerDisconnect_KeyboardEditStillHeld_DoesNotResume()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Engine.OnControllerDisconnected("pad");
        Assert.True(f.NothingHeld);
        f.Down(E); // auto-repeat, not a fresh press
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
    }

    [Fact]
    public void ProfileSwitchWhileSelecting_ReleasesEverything()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Reconfigure(p => p.SelectBind = InputId.Key('Q'));
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        Assert.True(f.NothingHeld);
        f.Up(E);
        Assert.Equal(2, f.Events.Count);
    }

    [Fact]
    public void SessionChange_ReleasesAndForgetsPhysicalState()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Engine.OnSessionChanged("lock");
        Assert.True(f.NothingHeld);
        f.Down(E); // after unlock the tracker was cleared, so this is a fresh press
        Assert.Equal(new[] { "P↓", "P↑", "P↓" }, f.Events);
    }

    [Fact]
    public void EnableWhileEditHeld_RequiresFreshPress()
    {
        var f = new EngineFixture(enable: false);
        f.Down(E);
        f.Engine.Enable("test");
        f.Down(E); // repeat
        Assert.Empty(f.Events);
        f.Up(E);
        f.Down(E);
        Assert.Equal(new[] { "P↓" }, f.Events);
    }

    [Fact]
    public void TapOnce_TapsSelect_ThenNothingOnRelease()
    {
        var f = new EngineFixture(p => { p.SelectMode = SelectMode.TapOnce; p.TapDurationMs = 12; });
        f.Down(E);
        Assert.Equal(new[] { "P↓" }, f.Events);
        f.Advance(12);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        f.Up(E);
        Assert.Equal(2, f.Events.Count);
        Assert.Equal(EngineState.Idle, f.Engine.State);
    }

    [Fact]
    public void TapOnce_EditReleasedDuringTap_ReleasesSelectImmediately()
    {
        var f = new EngineFixture(p => { p.SelectMode = SelectMode.TapOnce; p.TapDurationMs = 50; });
        f.Down(E);
        f.Advance(5);
        f.Up(E);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        f.Advance(100);
        Assert.Equal(2, f.Events.Count);
    }

    [Fact]
    public void Toggle_SecondEditPressReleasesSelect()
    {
        var f = new EngineFixture(p => p.SelectMode = SelectMode.Toggle);
        f.Down(E);
        f.Up(E);
        Assert.Equal(new[] { "P↓" }, f.Events);
        Assert.Equal(EngineState.Selecting, f.Engine.State);
        f.Down(E);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        f.Up(E);
        Assert.Equal(EngineState.Idle, f.Engine.State);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void Toggle_EmergencyStopReleasesLatchedSelect()
    {
        var f = new EngineFixture(p => p.SelectMode = SelectMode.Toggle);
        f.Down(E);
        f.Up(E);
        f.Down(F12);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void AutoConfirmOff_NeverSendsConfirm()
    {
        var f = new EngineFixture(p => p.AutoConfirm = AutoConfirmMode.Off);
        for (var i = 0; i < 5; i++) { f.Down(E); f.Advance(30); f.Up(E); f.Advance(30); }
        Assert.DoesNotContain(f.Events, e => e.StartsWith('C'));
    }

    [Fact]
    public void AutoConfirmOnEditRelease_SelectUpThenConfirmTap()
    {
        var f = new EngineFixture(p => { p.AutoConfirm = AutoConfirmMode.ConfirmOnEditRelease; p.ConfirmDelayMs = 10; p.TapDurationMs = 10; });
        f.Down(E);
        f.Advance(50);
        f.Up(E);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        Assert.Equal(EngineState.Releasing, f.Engine.State);
        f.Advance(10);
        Assert.Equal(new[] { "P↓", "P↑", "C↓" }, f.Events);
        f.Advance(10);
        Assert.Equal(new[] { "P↓", "P↑", "C↓", "C↑" }, f.Events);
        Assert.Equal(EngineState.Idle, f.Engine.State);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void AutoConfirmAfterSelectRelease_TapOnce_ConfirmsAfterTap()
    {
        var f = new EngineFixture(p =>
        {
            p.SelectMode = SelectMode.TapOnce;
            p.AutoConfirm = AutoConfirmMode.ConfirmAfterSelectRelease;
            p.ConfirmDelayMs = 0;
            p.TapDurationMs = 5;
        });
        f.Down(E);
        f.Advance(5);
        Assert.Equal(new[] { "P↓", "P↑", "C↓" }, f.Events);
        f.Up(E); // confirm still in flight
        Assert.Equal(EngineState.Releasing, f.Engine.State);
        f.Advance(5);
        Assert.Equal(new[] { "P↓", "P↑", "C↓", "C↑" }, f.Events);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void AutoConfirm_NoConfirmWhenEditReleasedBeforeSelect()
    {
        var f = new EngineFixture(p => { p.AutoConfirm = AutoConfirmMode.ConfirmAfterSelectRelease; p.SelectDelayMs = 20; });
        f.Down(E);
        f.Up(E);
        f.Advance(100);
        Assert.Empty(f.Events);
    }

    [Fact]
    public void NewEditPressDuringPendingConfirm_LeavesNothingStuck()
    {
        var f = new EngineFixture(p => { p.AutoConfirm = AutoConfirmMode.ConfirmOnEditRelease; p.ConfirmDelayMs = 0; p.TapDurationMs = 20; });
        f.Down(E);
        f.Up(E);            // P↑, C↓ (held for 20ms)
        f.Advance(5);
        f.Down(E);          // new activation while C is held
        Assert.Equal(new[] { "P↓", "P↑", "C↓", "C↑", "P↓" }, f.Events);
        f.Up(E);
        f.Advance(50);
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void Watchdog_ReleasesSelect_WhenEditReleaseWasMissed()
    {
        var probe = new FakeProbe();
        var f = new EngineFixture(probe: probe);
        probe.State[E] = true;
        f.Down(E);
        f.Advance(200);
        Assert.Equal(new[] { "P↓" }, f.Events);

        probe.State[E] = false; // the key-up never reached the hook
        f.Advance(200);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        Assert.Equal(EngineState.Idle, f.Engine.State);

        probe.State[E] = true;  // next real press works normally
        f.Down(E);
        Assert.Equal("P↓", f.Events[^1]);
    }

    [Fact]
    public void Watchdog_IsIdleWhenNothingActive()
    {
        var probe = new FakeProbe();
        var f = new EngineFixture(probe: probe);
        Assert.Null(f.Engine.NextDeadline);
    }

    [Fact]
    public void StaleDown_AfterMissedRelease_IsTreatedAsFreshPress()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Advance(3000); // release lost, no probe available
        f.Down(E);
        Assert.Equal(new[] { "P↓", "P↑", "P↓" }, f.Events);
    }

    [Fact]
    public void DeviceMode_KeyboardMouse_IgnoresControllerEdit()
    {
        var pad = InputId.Pad(ControllerButton.A);
        var f = new EngineFixture(p => { p.EditBind = pad; p.DeviceMode = DeviceMode.KeyboardMouse; });
        f.Down(pad);
        Assert.Empty(f.Events);
    }

    [Fact]
    public void Hybrid_ControllerEdit_KeyboardSelect()
    {
        var pad = InputId.Pad(ControllerButton.LB);
        var f = new EngineFixture(p => { p.EditBind = pad; p.DeviceMode = DeviceMode.Hybrid; });
        f.Down(pad);
        f.Up(pad);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
    }

    [Fact]
    public void SuspendedForCapture_DoesNothing_AndReleases()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Engine.SetSuspended(true);
        Assert.True(f.NothingHeld);
        f.Up(E);
        f.Down(E);
        Assert.Equal(new[] { "P↓", "P↑" }, f.Events);
        f.Engine.SetSuspended(false);
        f.Up(E);
        f.Down(E);
        Assert.Equal("P↓", f.Events[^1]);
    }

    [Fact]
    public void Fault_ReleasesAndEntersError()
    {
        var f = new EngineFixture();
        f.Down(E);
        f.Engine.Fault("boom");
        Assert.True(f.NothingHeld);
        Assert.Equal(EngineState.Error, f.Engine.State);
        f.Engine.Enable("user");
        Assert.Equal(EngineState.Idle, f.Engine.State);
        Assert.Null(f.Engine.LastError);
    }
}

public class RemapTests
{
    private static readonly InputId A = InputId.Key('A');
    private static readonly InputId B = InputId.Key('B');
    private static readonly InputId X = InputId.Mouse(MouseButton.X1);
    private static readonly InputId Y = InputId.Key('Y');

    private static EngineFixture Create() => new(p =>
    {
        p.Mode = EngineMode.SimpleRemap;
        p.Remaps = new() { new() { Source = A, Target = B }, new() { Source = X, Target = Y } };
    });

    [Fact]
    public void OneToOne_PressAndRelease()
    {
        var f = Create();
        f.Down(A);
        Assert.Equal(EngineState.RemapActive, f.Engine.State);
        f.Down(A);
        f.Up(A);
        f.Down(X);
        f.Up(X);
        Assert.Equal(new[] { "B↓", "B↑", "Y↓", "Y↑" }, f.Events);
        Assert.Equal(EngineState.Idle, f.Engine.State);
    }

    [Fact]
    public void RemapMode_DisablesEditAutomation()
    {
        var f = Create();
        f.Down(E);
        Assert.Empty(f.Events);
    }

    [Fact]
    public void DisableWhileRemapHeld_Releases()
    {
        var f = Create();
        f.Down(A);
        f.Engine.Disable("t");
        Assert.True(f.NothingHeld);
    }

    [Fact]
    public void DuplicateSources_FirstWins()
    {
        var f = new EngineFixture(p =>
        {
            p.Mode = EngineMode.SimpleRemap;
            p.Remaps = new() { new() { Source = A, Target = B }, new() { Source = A, Target = Y } };
        });
        f.Down(A);
        f.Up(A);
        Assert.Equal(new[] { "B↓", "B↑" }, f.Events);
    }

    [Fact]
    public void BlockedInputs_OnlyKeyboardAndMouseSources()
    {
        var pad = InputId.Pad(ControllerButton.A);
        var cfg = EngineConfig.From(new Profile
        {
            Mode = EngineMode.SimpleRemap,
            Remaps = new() { new() { Source = A, Target = B }, new() { Source = pad, Target = Y } },
        }, new AppSettings());
        Assert.Contains(A, cfg.BlockedInputs());
        Assert.DoesNotContain(pad, cfg.BlockedInputs());
        Assert.Contains(pad, cfg.WatchedInputs());
    }
}
