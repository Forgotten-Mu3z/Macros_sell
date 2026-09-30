using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EditInput.App.Mvvm;
using EditInput.App.Services;
using EditInput.Core.Engine;
using EditInput.Core.Logging;
using EditInput.Core.Input;
using EditInput.Core.Profiles;
using EditInput.Core.Validation;

namespace EditInput.App.ViewModels;

public enum Page
{
    Edit,
    Remap,
    Controller,
    Debug,
    Settings,
}

public interface IDialogService
{
    string? Prompt(string title, string message, string initial);
    bool Confirm(string title, string message);
    /// <summary>true = save, false = discard, null = cancel.</summary>
    bool? AskSaveChanges(string profileName);
    void Info(string title, string message);
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _applyDebounce;
    private Profile _working = new();
    private Profile _saved = new();
    private Page _page = Page.Edit;
    private bool _dirty;
    private bool _capturing;
    private string _captureText = "";
    private string _captureTitle = "";
    private string _statusText = "DISABLED";
    private Brush _statusBrush = Brushes.Gray;
    private string _stateText = "DISABLED";
    private string? _banner;
    private EngineSnapshot? _lastSnapshot;
    private bool _windowVisible = true;
    private bool _suppressProfileSwitch;

    public MainViewModel(AppServices services, IDialogService dialogs, Action showWelcome)
    {
        _services = services;
        _dialogs = dialogs;
        _dispatcher = Dispatcher.CurrentDispatcher;

        Editor = new ProfileEditorViewModel(OnEditorChanged, BeginCapture);
        Settings = new SettingsViewModel(services, BeginCapture, () => { RefreshIssues(); ApplyNow("Settings changed"); }, showWelcome);
        Controller = new ControllerViewModel();
        Debug = new DebugViewModel(services.Logger);

        EnableCommand = new RelayCommand(() => _services.Host.Enable("Enable button"));
        DisableCommand = new RelayCommand(() => _services.Host.Disable("Disable button"));
        EmergencyStopCommand = new RelayCommand(() => _services.Host.EmergencyStop("button"));
        NavigateCommand = new RelayCommand(p => { if (p is Page pg) CurrentPage = pg; else if (Enum.TryParse<Page>(p?.ToString(), out var x)) CurrentPage = x; });
        NewProfileCommand = new RelayCommand(NewProfile);
        SaveProfileCommand = new RelayCommand(SaveProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile);
        RenameProfileCommand = new RelayCommand(RenameProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1);
        CancelCaptureCommand = new RelayCommand(() => _services.Host.CancelCapture());

        _applyDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _applyDebounce.Tick += (_, _) =>
        {
            _applyDebounce.Stop();
            ApplyNow("Profile edited");
        };

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _refreshTimer.Tick += (_, _) => Refresh();

        foreach (var n in services.Profiles.Names) Profiles.Add(n);
        services.Profiles.ListChanged += SyncProfileList;
    }

    // ───── child view-models ─────
    public ProfileEditorViewModel Editor { get; }
    public SettingsViewModel Settings { get; }
    public ControllerViewModel Controller { get; }
    public DebugViewModel Debug { get; }

    public ObservableCollection<string> Profiles { get; } = new();
    public ObservableCollection<BindIssue> Issues { get; } = new();

    // ───── commands ─────
    public ICommand EnableCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand EmergencyStopCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand NewProfileCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DuplicateProfileCommand { get; }
    public ICommand RenameProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand CancelCaptureCommand { get; }

    /// <summary>Raised when the engine snapshot changes (tray icon/tooltip).</summary>
    public event Action<EngineSnapshot>? SnapshotChanged;

    // ───── state ─────
    public Page CurrentPage
    {
        get => _page;
        set
        {
            if (!Set(ref _page, value)) return;
            UpdateLiveControllerDemand();
        }
    }

    public bool IsDirty { get => _dirty; private set { if (Set(ref _dirty, value)) OnPropertyChanged(nameof(ProfileTitle)); } }
    public string ProfileTitle => _working.Name + (_dirty ? "  •  unsaved" : "");

    public string? SelectedProfile
    {
        get => _working.Name;
        set
        {
            if (value is null || _suppressProfileSwitch || string.Equals(value, _working.Name, StringComparison.OrdinalIgnoreCase)) return;
            if (!ConfirmLeaveProfile())
            {
                // Revert the ComboBox after it finishes its own selection change.
                _dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedProfile)));
                return;
            }
            LoadProfile(value, "Profile Changed");
        }
    }

    public bool IsCapturing { get => _capturing; private set => Set(ref _capturing, value); }
    public string CaptureTitle { get => _captureTitle; private set => Set(ref _captureTitle, value); }
    public string CaptureText { get => _captureText; private set => Set(ref _captureText, value); }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
    public string? Banner { get => _banner; private set { if (Set(ref _banner, value)) OnPropertyChanged(nameof(HasBanner)); } }
    public bool HasBanner => _banner is not null;

    public bool WindowVisible
    {
        get => _windowVisible;
        set
        {
            if (_windowVisible == value) return;
            _windowVisible = value;
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(value ? 33 : 250);
            UpdateLiveControllerDemand();
        }
    }

    // ───── lifecycle ─────
    public void Initialize()
    {
        var settings = _services.Settings.Current;
        LoadProfile(settings.ActiveProfile, "Startup");
        if (settings.EnableOnLaunch) _services.Host.Enable("launch");
        _refreshTimer.Start();
        Refresh();
        _services.DriverProbe.ContinueWith(_ => _dispatcher.BeginInvoke(RefreshIssues), TaskScheduler.Default);
    }

    public static string StateName(EngineSnapshot s)
    {
        if (s.CaptureActive) return "CAPTURING BIND";
        return s.State switch
        {
            EngineState.Disabled => "DISABLED",
            EngineState.Idle => "READY",
            EngineState.EditPressed => "EDIT PRESSED",
            EngineState.Resetting => "RESETTING",
            EngineState.WaitingForSelect => "WAITING FOR SELECT",
            EngineState.Selecting => "SELECTING",
            EngineState.Releasing => "RELEASING",
            EngineState.RemapActive => "REMAPPING",
            EngineState.EmergencyStopped => "EMERGENCY STOP",
            EngineState.Error => "ERROR",
            _ => s.State.ToString().ToUpperInvariant(),
        };
    }

    private void Refresh()
    {
        var snap = _services.Host.Snapshot;
        if (snap != _lastSnapshot)
        {
            _lastSnapshot = snap;
            (StatusText, StatusBrush) = snap.State switch
            {
                EngineState.EmergencyStopped => ("EMERGENCY STOP", Res("RedBrush")),
                EngineState.Error => ("ERROR", Res("RedBrush")),
                EngineState.Disabled => ("DISABLED", Res("GrayBrush")),
                _ => ("ENABLED", Res("GreenBrush")),
            };
            StateText = StateName(snap);
            Banner = snap.State switch
            {
                EngineState.EmergencyStopped => "Emergency stop active – every generated input was released. Click ENABLE to start again.",
                EngineState.Error => $"The input engine stopped after an error: {snap.LastError}. All inputs were released. Click ENABLE to retry.",
                _ => null,
            };
            SnapshotChanged?.Invoke(snap);
        }

        if (!_windowVisible) return;
        Controller.Update(_services.Input.Controller.Snapshot, BoundInputs());
        Controller.VirtualPadStatus = _services.VirtualPad.Status;
        if (_page == Page.Debug || Settings.DebugLogging) Debug.Update(snap, Controller, _page == Page.Debug);
    }

    private IReadOnlyCollection<InputId> BoundInputs()
    {
        var s = _services.Settings.Current;
        var set = new HashSet<InputId> { s.ToggleBind, s.EmergencyStop };
        if (_working.Mode == EngineMode.EditAutomation)
        {
            set.Add(_working.EditBind);
            set.Add(_working.SelectBind);
            set.Add(_working.ResetBind);
            set.Add(_working.ConfirmBind);
        }
        else
        {
            foreach (var r in _working.Remaps) { set.Add(r.Source); set.Add(r.Target); }
        }
        return set;
    }

    private void UpdateLiveControllerDemand() =>
        _services.Input.Controller.UiWantsLiveReading = _windowVisible && _page is Page.Controller or Page.Debug;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // ───── profile editing ─────
    private void OnEditorChanged(bool immediate)
    {
        IsDirty = !_working.ContentEquals(_saved);
        RefreshIssues();
        if (immediate)
        {
            _applyDebounce.Stop();
            ApplyNow("Profile edited");
        }
        else
        {
            _applyDebounce.Stop();
            _applyDebounce.Start();
        }
    }

    private void ApplyNow(string reason) => _services.Apply(_working.Clone().Normalize(), _services.Settings.Current, reason);

    private void RefreshIssues()
    {
        Issues.Clear();
        foreach (var i in ConflictDetector.Analyze(_working, _services.Settings.Current, _services.ControllerOutputAvailable))
            Issues.Add(i);
        OnPropertyChanged(nameof(HasIssues));
    }

    public bool HasIssues => Issues.Count > 0;

    private void LoadProfile(string name, string reason)
    {
        _applyDebounce.Stop();
        _working = _services.Profiles.GetOrFirst(name);
        _saved = _working.Clone();
        Editor.Load(_working);
        _services.Settings.Update(s => s.ActiveProfile = _working.Name);
        IsDirty = false;
        _suppressProfileSwitch = true;
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(ProfileTitle));
        _suppressProfileSwitch = false;
        RefreshIssues();
        ApplyNow(reason); // releases every held input before the new binds take effect
        _services.Logger.Info("Profiles", $"Profile Changed → {_working.Name}");
    }

    /// <summary>Asks about unsaved changes. Returns false if the user cancelled.</summary>
    public bool ConfirmLeaveProfile()
    {
        if (!IsDirty) return true;
        var answer = _dialogs.AskSaveChanges(_working.Name);
        if (answer is null) return false;
        if (answer == true) SaveProfile();
        return true;
    }

    private void SyncProfileList()
    {
        var names = _services.Profiles.Names;
        _suppressProfileSwitch = true;
        try
        {
            foreach (var gone in Profiles.Where(p => !names.Contains(p)).ToList()) Profiles.Remove(gone);
            for (var i = 0; i < names.Count; i++)
            {
                if (i < Profiles.Count && Profiles[i] == names[i]) continue;
                if (Profiles.Contains(names[i])) Profiles.Move(Profiles.IndexOf(names[i]), i);
                else Profiles.Insert(i, names[i]);
            }
        }
        finally
        {
            _suppressProfileSwitch = false;
        }
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void SaveProfile()
    {
        _services.Profiles.Save(_working);
        _saved = _working.Clone();
        IsDirty = false;
        _services.Logger.Info("Profiles", $"Saved profile '{_working.Name}'");
    }

    private void NewProfile()
    {
        if (!ConfirmLeaveProfile()) return;
        var name = _dialogs.Prompt("New Profile", "Name for the new profile:", _services.Profiles.SuggestName("Custom"));
        if (name is null) return;
        try
        {
            var p = _services.Profiles.Create(name);
            LoadProfile(p.Name, "Profile Changed");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.Info("New Profile", ex.Message);
        }
    }

    private void DuplicateProfile()
    {
        var name = _dialogs.Prompt("Duplicate Profile", "Name for the copy (includes any unsaved changes):",
            _services.Profiles.SuggestName(_working.Name + " Copy"));
        if (name is null) return;
        name = name.Trim();
        if (name.Length == 0 || _services.Profiles.Contains(name))
        {
            _dialogs.Info("Duplicate Profile", name.Length == 0 ? "Profile name cannot be empty." : $"A profile named '{name}' already exists.");
            return;
        }
        var copy = _working.Clone();
        copy.Name = name;
        _services.Profiles.Save(copy); // the original keeps its last saved version
        LoadProfile(copy.Name, "Profile Changed");
    }

    private void RenameProfile()
    {
        var name = _dialogs.Prompt("Rename Profile", "New name:", _working.Name);
        if (name is null || name.Trim() == _working.Name) return;
        try
        {
            var old = _working.Name;
            _services.Profiles.Rename(old, name);
            _working.Name = name.Trim();
            _saved.Name = _working.Name;
            _services.Settings.Update(s => s.ActiveProfile = _working.Name);
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(ProfileTitle));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.Info("Rename Profile", ex.Message);
        }
    }

    private void DeleteProfile()
    {
        if (!_dialogs.Confirm("Delete Profile", $"Delete the profile '{_working.Name}'? This can't be undone.")) return;
        try
        {
            var name = _working.Name;
            _services.Profiles.Delete(name);
            IsDirty = false;
            LoadProfile(_services.Profiles.Names[0], "Profile Changed");
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.Info("Delete Profile", ex.Message);
        }
    }

    // ───── bind capture ─────
    private BindSlotViewModel? _captureSlot;

    private void BeginCapture(BindSlotViewModel slot)
    {
        if (IsCapturing) return;
        _captureSlot = slot;
        CaptureTitle = $"Set {slot.Label.ToLowerInvariant()} bind";
        CaptureText = "Press any key, mouse button, or controller button…";
        IsCapturing = true;
        _services.Host.BeginCapture(
            id => _dispatcher.BeginInvoke(() => CaptureText = $"{id.DisplayName} – release to confirm"),
            id => _dispatcher.BeginInvoke(() => CompleteCapture(id)),
            TimeSpan.FromSeconds(10));
    }

    private void CompleteCapture(InputId? captured)
    {
        IsCapturing = false;
        var slot = _captureSlot;
        _captureSlot = null;
        if (slot is null || captured is not { } id) return;

        var estop = _services.Settings.Current.EmergencyStop;
        if (slot.Role == BindRole.EmergencyStop && GeneratedInputs().Contains(id))
        {
            _dialogs.Info("Emergency Stop", $"{id.DisplayName} is an input this app generates, so it can't be the Emergency Stop. Choose a different key.");
            return;
        }
        if (slot.IsOutput && id == estop)
        {
            _dialogs.Info("Bind", $"{id.DisplayName} is your Emergency Stop key and can't be generated by the app. Choose a different input.");
            return;
        }
        slot.Value = id;
    }

    private HashSet<InputId> GeneratedInputs()
    {
        var set = new HashSet<InputId> { _working.SelectBind, _working.ResetBind, _working.ConfirmBind };
        foreach (var r in _working.Remaps) set.Add(r.Target);
        set.Remove(InputId.None);
        return set;
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _applyDebounce.Stop();
        _services.Profiles.ListChanged -= SyncProfileList;
    }
}
