using System.Collections.Immutable;
using System.Diagnostics;
using EditInput.Core.Input;
using EditInput.Core.Profiles;

namespace EditInput.Core.Engine;

public enum EngineState
{
    Disabled,
    Idle,
    EditPressed,
    Resetting,
    WaitingForSelect,
    Selecting,
    Releasing,
    RemapActive,
    EmergencyStopped,
    Error,
}

/// <summary>Monotonic high-resolution time source.</summary>
public interface IClock
{
    long Now { get; }
    long Frequency { get; }
}

public sealed class StopwatchClock : IClock
{
    public static readonly StopwatchClock Instance = new();
    public long Now => Stopwatch.GetTimestamp();
    public long Frequency => Stopwatch.Frequency;
}

/// <summary>
/// Optional secondary source of truth for "is this physical input really still held?". Used by the engine's
/// watchdog so a key-up that the hook never saw (focus change to an elevated window, hook timeout...) can't
/// leave Select held. Returns null when the state is unknown.
/// </summary>
public interface IPhysicalStateProbe
{
    bool? IsPhysicallyDown(InputId id);
}

/// <summary>Immutable snapshot of everything the engine needs, built from a profile + global settings.</summary>
public sealed record EngineConfig
{
    public EngineMode Mode { get; init; } = EngineMode.EditAutomation;
    public InputId Edit { get; init; }
    public InputId Select { get; init; }
    public InputId Reset { get; init; }
    public InputId Confirm { get; init; }
    public InputId EmergencyStop { get; init; } = InputId.Key(KeyNames.F12);
    public InputId Toggle { get; init; } = InputId.Key(KeyNames.F8);
    public bool ResetBeforeSelect { get; init; }
    public SelectMode SelectMode { get; init; }
    public AutoConfirmMode AutoConfirm { get; init; }
    public int ResetDelayMs { get; init; }
    public int SelectDelayMs { get; init; }
    public int TapDurationMs { get; init; }
    public int ConfirmDelayMs { get; init; }
    public DeviceMode DeviceMode { get; init; } = DeviceMode.Hybrid;
    public ImmutableArray<RemapEntry> Remaps { get; init; } = ImmutableArray<RemapEntry>.Empty;
    public string ProfileName { get; init; } = "";

    public static EngineConfig From(Profile p, AppSettings s)
    {
        // Strict 1:1: the first mapping for a source wins; mappings with a missing side are dropped.
        var remaps = p.Remaps
            .Where(r => !r.Source.IsNone && !r.Target.IsNone && r.Source != r.Target)
            .GroupBy(r => r.Source).Select(g => g.First().Clone())
            .ToImmutableArray();

        return new EngineConfig
        {
            Mode = p.Mode,
            Edit = p.EditBind,
            Select = p.SelectBind,
            Reset = p.ResetBind,
            Confirm = p.ConfirmBind,
            EmergencyStop = s.EmergencyStop,
            Toggle = s.ToggleBind,
            ResetBeforeSelect = p.ResetBeforeSelect,
            SelectMode = p.SelectMode,
            AutoConfirm = p.AutoConfirm,
            ResetDelayMs = p.ResetDelayMs,
            SelectDelayMs = p.SelectDelayMs,
            TapDurationMs = p.TapDurationMs,
            ConfirmDelayMs = p.ConfirmDelayMs,
            DeviceMode = p.DeviceMode,
            Remaps = remaps,
            ProfileName = p.Name,
        };
    }

    public bool DeviceAllowed(InputId id) => id.Kind switch
    {
        DeviceKind.None => false,
        DeviceKind.Controller => DeviceMode != DeviceMode.KeyboardMouse,
        _ => DeviceMode != DeviceMode.Controller,
    };

    /// <summary>Physical inputs the engine must observe (everything else is never forwarded or logged).</summary>
    public ImmutableHashSet<InputId> WatchedInputs()
    {
        var set = ImmutableHashSet.CreateBuilder<InputId>();
        void Add(InputId id) { if (!id.IsNone) set.Add(id); }
        Add(EmergencyStop);
        Add(Toggle);
        if (Mode == EngineMode.EditAutomation)
        {
            if (DeviceAllowed(Edit)) Add(Edit);
        }
        else
        {
            foreach (var r in Remaps)
                if (DeviceAllowed(r.Source)) Add(r.Source);
        }
        return set.ToImmutable();
    }

    /// <summary>Keyboard/mouse remap sources are swallowed so only the target reaches other apps.</summary>
    public ImmutableHashSet<InputId> BlockedInputs() =>
        Mode == EngineMode.SimpleRemap
            ? Remaps.Select(r => r.Source)
                .Where(s => s.Kind is DeviceKind.Keyboard or DeviceKind.Mouse && DeviceAllowed(s))
                .ToImmutableHashSet()
            : ImmutableHashSet<InputId>.Empty;

    /// <summary>Every input this configuration may inject.</summary>
    public IEnumerable<InputId> OutputInputs()
    {
        if (Mode == EngineMode.SimpleRemap)
        {
            foreach (var r in Remaps) yield return r.Target;
            yield break;
        }
        if (!Select.IsNone) yield return Select;
        if (ResetBeforeSelect && !Reset.IsNone) yield return Reset;
        if (AutoConfirm != AutoConfirmMode.Off && !Confirm.IsNone) yield return Confirm;
    }
}

/// <summary>Read-only view of the engine published for the UI and tray.</summary>
public sealed record EngineSnapshot(
    EngineState State,
    bool EditDown,
    bool SelectHeld,
    bool ResetHeld,
    bool ConfirmHeld,
    bool CaptureActive,
    string? LastError,
    string ProfileName)
{
    public static readonly EngineSnapshot Initial = new(EngineState.Disabled, false, false, false, false, false, null, "");

    public bool IsEnabled => State is not (EngineState.Disabled or EngineState.EmergencyStopped or EngineState.Error);
}
