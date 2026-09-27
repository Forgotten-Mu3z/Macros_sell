using EditInput.Core.Input;

namespace EditInput.Core.Profiles;

/// <summary>Global (profile-independent) settings, stored in settings.json.</summary>
public sealed class AppSettings
{
    public const int MinPollRateHz = 125, MaxPollRateHz = 1000;

    public string ActiveProfile { get; set; } = "Default";
    public InputId EmergencyStop { get; set; } = InputId.Key(KeyNames.F12);
    public InputId ToggleBind { get; set; } = InputId.Key(KeyNames.F8);

    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool EnableOnLaunch { get; set; } = true;
    public bool DebugLogging { get; set; }
    public bool ShowDebugPanel { get; set; }
    public bool FirstRunCompleted { get; set; }

    /// <summary>When true, input injected by *other* software (e.g. Steam Input keyboard mapping) is ignored.</summary>
    public bool IgnoreForeignInjectedInput { get; set; }

    public int ControllerPollRateHz { get; set; } = 500;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public AppSettings Normalize()
    {
        ActiveProfile = string.IsNullOrWhiteSpace(ActiveProfile) ? "Default" : ActiveProfile;
        if (EmergencyStop.IsNone) EmergencyStop = InputId.Key(KeyNames.F12);
        ControllerPollRateHz = Math.Clamp(ControllerPollRateHz, MinPollRateHz, MaxPollRateHz);
        return this;
    }
}
