using System.Windows.Input;
using EditInput.App.Mvvm;
using EditInput.Core.Input;

namespace EditInput.App.ViewModels;

public enum BindRole
{
    Edit,
    Select,
    Reset,
    Confirm,
    Toggle,
    EmergencyStop,
    RemapSource,
    RemapTarget,
}

/// <summary>One clickable bind box. Reading/writing goes through delegates so it works for profile and global binds.</summary>
public sealed class BindSlotViewModel : ObservableObject
{
    private readonly Func<InputId> _get;
    private readonly Action<InputId> _set;

    public BindSlotViewModel(string label, BindRole role, Func<InputId> get, Action<InputId> set,
        Action<BindSlotViewModel> beginCapture, bool allowClear = true)
    {
        Label = label;
        Role = role;
        _get = get;
        _set = set;
        AllowClear = allowClear;
        CaptureCommand = new RelayCommand(() => beginCapture(this));
        ClearCommand = new RelayCommand(() => Value = InputId.None, () => AllowClear && !Value.IsNone);
    }

    public string Label { get; }
    public BindRole Role { get; }
    public bool AllowClear { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ClearCommand { get; }

    /// <summary>True for binds the app generates (they must never equal the emergency stop).</summary>
    public bool IsOutput => Role is BindRole.Select or BindRole.Reset or BindRole.Confirm or BindRole.RemapTarget;

    public InputId Value
    {
        get => _get();
        set
        {
            if (_get() == value) return;
            _set(value);
            Refresh();
        }
    }

    public string Display => Value.DisplayName;

    public bool IsEmpty => Value.IsNone;

    /// <summary>Segoe MDL2 glyph for the device kind.</summary>
    public string DeviceGlyph => Value.Kind switch
    {
        DeviceKind.Keyboard => "",
        DeviceKind.Mouse => "",
        DeviceKind.Controller => "",
        _ => "",
    };

    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(DeviceGlyph));
    }
}
