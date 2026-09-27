using System.Collections.ObjectModel;
using System.Windows.Input;
using EditInput.App.Mvvm;
using EditInput.Core.Profiles;

namespace EditInput.App.ViewModels;

public sealed record Option<T>(T Value, string Display)
{
    public override string ToString() => Display;
}

public sealed class RemapEntryViewModel : ObservableObject
{
    public RemapEntryViewModel(RemapEntry entry, Action<BindSlotViewModel> capture, Action changed, Action<RemapEntryViewModel> remove)
    {
        Entry = entry;
        Source = new BindSlotViewModel("FROM", BindRole.RemapSource, () => entry.Source, v => { entry.Source = v; changed(); }, capture);
        Target = new BindSlotViewModel("TO", BindRole.RemapTarget, () => entry.Target, v => { entry.Target = v; changed(); }, capture);
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public RemapEntry Entry { get; }
    public BindSlotViewModel Source { get; }
    public BindSlotViewModel Target { get; }
    public ICommand RemoveCommand { get; }
}

/// <summary>Edits the working copy of the active profile. Every change is reported to the owner, which
/// applies it live to the engine (debounced) and tracks unsaved changes.</summary>
public sealed class ProfileEditorViewModel : ObservableObject
{
    private readonly Action<bool> _changed; // arg: structural change that should apply immediately
    private readonly Action<BindSlotViewModel> _capture;
    private Profile _p = new();

    public ProfileEditorViewModel(Action<bool> changed, Action<BindSlotViewModel> capture)
    {
        _changed = changed;
        _capture = capture;
        Edit = new BindSlotViewModel("EDIT", BindRole.Edit, () => _p.EditBind, v => { _p.EditBind = v; Changed(true); }, capture);
        Select = new BindSlotViewModel("SELECT", BindRole.Select, () => _p.SelectBind, v => { _p.SelectBind = v; Changed(true); }, capture);
        Reset = new BindSlotViewModel("RESET", BindRole.Reset, () => _p.ResetBind, v => { _p.ResetBind = v; Changed(true); }, capture);
        Confirm = new BindSlotViewModel("CONFIRM", BindRole.Confirm, () => _p.ConfirmBind, v => { _p.ConfirmBind = v; Changed(true); }, capture);
        AddRemapCommand = new RelayCommand(AddRemap);
    }

    public Profile Profile => _p;

    public BindSlotViewModel Edit { get; }
    public BindSlotViewModel Select { get; }
    public BindSlotViewModel Reset { get; }
    public BindSlotViewModel Confirm { get; }
    public IEnumerable<BindSlotViewModel> ProfileBinds => new[] { Edit, Select, Reset, Confirm };

    public ObservableCollection<RemapEntryViewModel> Remaps { get; } = new();
    public ICommand AddRemapCommand { get; }

    public IReadOnlyList<Option<SelectMode>> SelectModes { get; } = new[]
    {
        new Option<SelectMode>(SelectMode.HoldUntilEditReleased, "Hold Until Edit Released"),
        new Option<SelectMode>(SelectMode.TapOnce, "Tap Once"),
        new Option<SelectMode>(SelectMode.Toggle, "Toggle"),
    };

    public IReadOnlyList<Option<AutoConfirmMode>> ConfirmModes { get; } = new[]
    {
        new Option<AutoConfirmMode>(AutoConfirmMode.Off, "Off"),
        new Option<AutoConfirmMode>(AutoConfirmMode.ConfirmOnEditRelease, "Confirm On Edit Release"),
        new Option<AutoConfirmMode>(AutoConfirmMode.ConfirmAfterSelectRelease, "Confirm After Select Release"),
    };

    public IReadOnlyList<Option<DeviceMode>> DeviceModes { get; } = new[]
    {
        new Option<DeviceMode>(DeviceMode.KeyboardMouse, "Keyboard & Mouse"),
        new Option<DeviceMode>(DeviceMode.Controller, "Controller"),
        new Option<DeviceMode>(DeviceMode.Hybrid, "Hybrid"),
    };

    public IReadOnlyList<Option<EngineMode>> EngineModes { get; } = new[]
    {
        new Option<EngineMode>(EngineMode.EditAutomation, "Edit Automation"),
        new Option<EngineMode>(EngineMode.SimpleRemap, "Simple Remap (1:1, no automation)"),
    };

    public void Load(Profile profile)
    {
        _p = profile;
        Remaps.Clear();
        foreach (var r in _p.Remaps) Remaps.Add(CreateRemapVm(r));
        foreach (var b in ProfileBinds) b.Refresh();
        OnPropertyChanged(string.Empty); // refresh everything
    }

    public EngineMode Mode
    {
        get => _p.Mode;
        set { if (_p.Mode == value) return; _p.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAutomation)); OnPropertyChanged(nameof(IsRemap)); Changed(true); }
    }

    public bool IsAutomation => _p.Mode == EngineMode.EditAutomation;

    public bool IsRemap
    {
        get => _p.Mode == EngineMode.SimpleRemap;
        set => Mode = value ? EngineMode.SimpleRemap : EngineMode.EditAutomation;
    }

    public bool ResetBeforeSelect
    {
        get => _p.ResetBeforeSelect;
        set { if (_p.ResetBeforeSelect == value) return; _p.ResetBeforeSelect = value; OnPropertyChanged(); Changed(true); }
    }

    public SelectMode SelectMode
    {
        get => _p.SelectMode;
        set { if (_p.SelectMode == value) return; _p.SelectMode = value; OnPropertyChanged(); Changed(true); }
    }

    public AutoConfirmMode AutoConfirm
    {
        get => _p.AutoConfirm;
        set { if (_p.AutoConfirm == value) return; _p.AutoConfirm = value; OnPropertyChanged(); Changed(true); }
    }

    public DeviceMode DeviceMode
    {
        get => _p.DeviceMode;
        set { if (_p.DeviceMode == value) return; _p.DeviceMode = value; OnPropertyChanged(); Changed(true); }
    }

    public int ResetDelayMs
    {
        get => _p.ResetDelayMs;
        set => SetInt(v => _p.ResetDelayMs = v, _p.ResetDelayMs, value, 0, Profile.MaxDelayMs);
    }

    public int SelectDelayMs
    {
        get => _p.SelectDelayMs;
        set => SetInt(v => _p.SelectDelayMs = v, _p.SelectDelayMs, value, 0, Profile.MaxDelayMs);
    }

    public int TapDurationMs
    {
        get => _p.TapDurationMs;
        set => SetInt(v => _p.TapDurationMs = v, _p.TapDurationMs, value, 0, Profile.MaxDelayMs);
    }

    public int ConfirmDelayMs
    {
        get => _p.ConfirmDelayMs;
        set => SetInt(v => _p.ConfirmDelayMs = v, _p.ConfirmDelayMs, value, 0, Profile.MaxDelayMs);
    }

    public int TriggerThresholdPercent
    {
        get => _p.TriggerThresholdPercent;
        set => SetInt(v => _p.TriggerThresholdPercent = v, _p.TriggerThresholdPercent, value, 5, 95);
    }

    public int StickDeadzonePercent
    {
        get => _p.StickDeadzonePercent;
        set => SetInt(v => _p.StickDeadzonePercent = v, _p.StickDeadzonePercent, value, 0, 40);
    }

    public int ControllerSlot
    {
        get => _p.ControllerSlot;
        set { if (_p.ControllerSlot == value) return; _p.ControllerSlot = value; OnPropertyChanged(); Changed(true); }
    }

    private void SetInt(Action<int> assign, int current, int value, int min, int max, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        value = Math.Clamp(value, min, max);
        if (current == value) return;
        assign(value);
        OnPropertyChanged(name);
        Changed(false);
    }

    private void AddRemap()
    {
        var entry = new RemapEntry();
        _p.Remaps.Add(entry);
        Remaps.Add(CreateRemapVm(entry));
        Changed(true);
    }

    private RemapEntryViewModel CreateRemapVm(RemapEntry e) =>
        new(e, _capture, () => Changed(true), vm =>
        {
            _p.Remaps.Remove(vm.Entry);
            Remaps.Remove(vm);
            Changed(true);
        });

    private void Changed(bool immediate) => _changed(immediate);
}
