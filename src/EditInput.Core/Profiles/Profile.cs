using System.Text.Json.Serialization;
using EditInput.Core.Input;

namespace EditInput.Core.Profiles;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngineMode
{
    /// <summary>Edit → (Reset) → Select sequences.</summary>
    EditAutomation,
    /// <summary>Strict one input → one input remapping; every automation feature is off.</summary>
    SimpleRemap,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SelectMode
{
    HoldUntilEditReleased,
    TapOnce,
    Toggle,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoConfirmMode
{
    Off,
    ConfirmOnEditRelease,
    ConfirmAfterSelectRelease,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceMode
{
    KeyboardMouse,
    Controller,
    Hybrid,
}

public sealed class RemapEntry
{
    public InputId Source { get; set; }
    public InputId Target { get; set; }

    public RemapEntry Clone() => new() { Source = Source, Target = Target };
}

/// <summary>One user profile. Every value has a range enforced by <see cref="Normalize"/>.</summary>
public sealed class Profile
{
    public const int MaxDelayMs = 100;

    public string Name { get; set; } = "Default";
    public EngineMode Mode { get; set; } = EngineMode.EditAutomation;

    public InputId EditBind { get; set; } = InputId.Key('E');
    public InputId SelectBind { get; set; } = InputId.Key('P');
    public InputId ResetBind { get; set; } = InputId.None;
    public InputId ConfirmBind { get; set; } = InputId.None;

    public bool ResetBeforeSelect { get; set; }
    public SelectMode SelectMode { get; set; } = SelectMode.HoldUntilEditReleased;
    public AutoConfirmMode AutoConfirm { get; set; } = AutoConfirmMode.Off;

    /// <summary>Reset → Select delay (0–100 ms).</summary>
    public int ResetDelayMs { get; set; } = 10;
    /// <summary>Edit → Select delay (0–100 ms).</summary>
    public int SelectDelayMs { get; set; }
    /// <summary>How long tapped outputs (Reset, Confirm, Tap Once select) are held down (0–100 ms).</summary>
    public int TapDurationMs { get; set; } = 10;
    /// <summary>Gap between Select release and an automatic Confirm (0–100 ms).</summary>
    public int ConfirmDelayMs { get; set; } = 10;

    public int TriggerThresholdPercent { get; set; } = 50;
    public int StickDeadzonePercent { get; set; } = 10;
    public DeviceMode DeviceMode { get; set; } = DeviceMode.Hybrid;
    /// <summary>-1 = Auto, otherwise XInput slot 0–3.</summary>
    public int ControllerSlot { get; set; } = -1;

    public List<RemapEntry> Remaps { get; set; } = new();

    public Profile Clone()
    {
        var p = (Profile)MemberwiseClone();
        p.Remaps = Remaps.Select(r => r.Clone()).ToList();
        return p;
    }

    public Profile Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Profile" : Name.Trim();
        ResetDelayMs = Math.Clamp(ResetDelayMs, 0, MaxDelayMs);
        SelectDelayMs = Math.Clamp(SelectDelayMs, 0, MaxDelayMs);
        TapDurationMs = Math.Clamp(TapDurationMs, 0, MaxDelayMs);
        ConfirmDelayMs = Math.Clamp(ConfirmDelayMs, 0, MaxDelayMs);
        TriggerThresholdPercent = Math.Clamp(TriggerThresholdPercent, 5, 95);
        StickDeadzonePercent = Math.Clamp(StickDeadzonePercent, 0, 40);
        ControllerSlot = ControllerSlot is >= 0 and <= 3 ? ControllerSlot : -1;
        if (!Enum.IsDefined(Mode)) Mode = EngineMode.EditAutomation;
        if (!Enum.IsDefined(SelectMode)) SelectMode = SelectMode.HoldUntilEditReleased;
        if (!Enum.IsDefined(AutoConfirm)) AutoConfirm = AutoConfirmMode.Off;
        if (!Enum.IsDefined(DeviceMode)) DeviceMode = DeviceMode.Hybrid;
        Remaps ??= new();
        Remaps.RemoveAll(r => r is null);
        return this;
    }

    public bool ContentEquals(Profile other) =>
        System.Text.Json.JsonSerializer.Serialize(this, ProfileJson.Options) ==
        System.Text.Json.JsonSerializer.Serialize(other, ProfileJson.Options);

    public static IEnumerable<Profile> CreateDefaults()
    {
        yield return new Profile { Name = "Default" };
        yield return new Profile
        {
            Name = "Fortnite KBM",
            DeviceMode = DeviceMode.KeyboardMouse,
            ResetBind = InputId.Mouse(MouseButton.Right),
            ConfirmBind = InputId.Mouse(MouseButton.Left),
        };
        yield return new Profile
        {
            Name = "Fortnite Controller",
            DeviceMode = DeviceMode.Controller,
            EditBind = InputId.Pad(ControllerButton.LS),
            SelectBind = InputId.Pad(ControllerButton.RT),
            ResetBind = InputId.Pad(ControllerButton.RB),
            ConfirmBind = InputId.Pad(ControllerButton.LT),
        };
        yield return new Profile
        {
            Name = "Fast Edit",
            ResetBind = InputId.Mouse(MouseButton.Right),
            ResetBeforeSelect = true,
            ResetDelayMs = 5,
            TapDurationMs = 8,
        };
    }
}

public static class ProfileJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
