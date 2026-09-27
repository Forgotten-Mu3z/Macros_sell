using System.Diagnostics;
using System.Windows.Input;
using EditInput.App.Mvvm;
using EditInput.App.Services;
using EditInput.Core.Logging;
using EditInput.Core.Profiles;
using EditInput.Windows;

namespace EditInput.App.ViewModels;

/// <summary>Global settings (settings.json). Changes are saved immediately.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _reapply;

    public SettingsViewModel(AppServices services, Action<BindSlotViewModel> capture, Action reapply, Action showWelcome)
    {
        _services = services;
        _reapply = reapply;
        Toggle = new BindSlotViewModel("ENABLE / DISABLE", BindRole.Toggle, () => S.ToggleBind,
            v => Update(s => s.ToggleBind = v, true), capture);
        EmergencyStop = new BindSlotViewModel("EMERGENCY STOP", BindRole.EmergencyStop, () => S.EmergencyStop,
            v => Update(s => s.EmergencyStop = v, true), capture, allowClear: false);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(_services.DataDirectory));
        OpenLogFolderCommand = new RelayCommand(() => OpenFolder(_services.Logger.Directory ?? _services.DataDirectory));
        ShowWelcomeCommand = new RelayCommand(showWelcome);
    }

    private AppSettings S => _services.Settings.Current;

    public BindSlotViewModel Toggle { get; }
    public BindSlotViewModel EmergencyStop { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand ShowWelcomeCommand { get; }

    public string DataDirectory => _services.DataDirectory;
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set
        {
            try
            {
                StartupRegistration.Set(value, Environment.ProcessPath ?? "");
                Update(s => s.StartWithWindows = value, false);
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Settings", "Could not change Start With Windows", ex);
                OnPropertyChanged();
            }
        }
    }

    public bool StartMinimized
    {
        get => S.StartMinimized;
        set => Update(s => s.StartMinimized = value, false);
    }

    public bool MinimizeToTray
    {
        get => S.MinimizeToTray;
        set => Update(s => s.MinimizeToTray = value, false);
    }

    public bool EnableOnLaunch
    {
        get => S.EnableOnLaunch;
        set => Update(s => s.EnableOnLaunch = value, false);
    }

    public bool DebugLogging
    {
        get => S.DebugLogging;
        set => Update(s => s.DebugLogging = value, true);
    }

    public bool IgnoreForeignInjectedInput
    {
        get => S.IgnoreForeignInjectedInput;
        set => Update(s => s.IgnoreForeignInjectedInput = value, true);
    }

    public int ControllerPollRateHz
    {
        get => S.ControllerPollRateHz;
        set => Update(s => s.ControllerPollRateHz = value, true);
    }

    public IReadOnlyList<Option<int>> PollRates { get; } = new[]
    {
        new Option<int>(250, "250 Hz (lowest CPU)"),
        new Option<int>(500, "500 Hz (recommended)"),
        new Option<int>(1000, "1000 Hz (lowest latency)"),
    };

    public void RefreshAll()
    {
        Toggle.Refresh();
        EmergencyStop.Refresh();
        OnPropertyChanged(string.Empty);
    }

    private void Update(Action<AppSettings> change, bool reapply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _services.Settings.Update(change);
        OnPropertyChanged(name);
        if (reapply) _reapply();
    }

    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _services.Logger.Error("Settings", "Could not open folder", ex); }
    }
}
