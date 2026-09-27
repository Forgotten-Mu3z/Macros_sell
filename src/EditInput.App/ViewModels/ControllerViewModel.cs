using System.Collections.ObjectModel;
using EditInput.App.Mvvm;
using EditInput.Core.Controller;
using EditInput.Core.Input;
using EditInput.Windows.Input;

namespace EditInput.App.ViewModels;

public sealed class PadButtonVisual : ObservableObject
{
    private bool _pressed;
    private bool _bound;

    public PadButtonVisual(ControllerButton button, string label, double x, double y, double w, double h, bool round)
    {
        Button = button;
        Label = label;
        X = x;
        Y = y;
        W = w;
        H = h;
        Corner = round ? Math.Min(w, h) / 2 : 6;
    }

    public ControllerButton Button { get; }
    public string Label { get; }
    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }
    public double Corner { get; }

    public bool IsPressed { get => _pressed; set => Set(ref _pressed, value); }
    public bool IsBound { get => _bound; set => Set(ref _bound, value); }
}

public sealed class ControllerViewModel : ObservableObject
{
    private const double StickTravel = 22;
    private string _status = "No Controller Detected";
    private bool _connected;
    private float _lt, _rt;
    private double _lx, _ly, _rx, _ry;
    private string _virtualPadStatus = "";
    private string _slotSignature = "";
    private bool _live;

    public ControllerViewModel()
    {
        Buttons = new ObservableCollection<PadButtonVisual>
        {
            new(ControllerButton.LB, "LB", 78, 44, 92, 24, false),
            new(ControllerButton.RB, "RB", 390, 44, 92, 24, false),
            new(ControllerButton.View, "View", 232, 116, 40, 22, true),
            new(ControllerButton.Menu, "Menu", 288, 116, 40, 22, true),
            new(ControllerButton.Y, "Y", 403, 88, 34, 34, true),
            new(ControllerButton.X, "X", 366, 124, 34, 34, true),
            new(ControllerButton.B, "B", 440, 124, 34, 34, true),
            new(ControllerButton.A, "A", 403, 160, 34, 34, true),
            new(ControllerButton.DPadUp, "▲", 198, 182, 28, 26, false),
            new(ControllerButton.DPadDown, "▼", 198, 236, 28, 26, false),
            new(ControllerButton.DPadLeft, "◀", 170, 209, 28, 26, false),
            new(ControllerButton.DPadRight, "▶", 226, 209, 28, 26, false),
            new(ControllerButton.LS, "LS", 124, 108, 56, 56, true),
            new(ControllerButton.RS, "RS", 322, 190, 56, 56, true),
        };
    }

    public ObservableCollection<PadButtonVisual> Buttons { get; }
    /// <summary>Fixed items (updated in place) so the ComboBox selection is never lost when names change.</summary>
    public IReadOnlyList<SlotOption> SlotOptions { get; } = new[]
    {
        new SlotOption(-1, "Auto (first connected)"),
        new SlotOption(0, "Controller 1"),
        new SlotOption(1, "Controller 2"),
        new SlotOption(2, "Controller 3"),
        new SlotOption(3, "Controller 4"),
    };

    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsConnected { get => _connected; private set => Set(ref _connected, value); }
    public bool IsLive { get => _live; private set => Set(ref _live, value); }
    public string VirtualPadStatus { get => _virtualPadStatus; set => Set(ref _virtualPadStatus, value); }

    public float LeftTrigger { get => _lt; private set { if (Set(ref _lt, value)) OnPropertyChanged(nameof(LeftTriggerText)); } }
    public float RightTrigger { get => _rt; private set { if (Set(ref _rt, value)) OnPropertyChanged(nameof(RightTriggerText)); } }
    public string LeftTriggerText => $"{Math.Round(_lt * 100)}%";
    public string RightTriggerText => $"{Math.Round(_rt * 100)}%";
    public bool LtPressed { get => _ltPressed; private set => Set(ref _ltPressed, value); }
    public bool RtPressed { get => _rtPressed; private set => Set(ref _rtPressed, value); }
    private bool _ltPressed, _rtPressed;

    // Dot positions (canvas coordinates) for the stick visualisers.
    public double LeftDotX { get => _lx; private set => Set(ref _lx, value); }
    public double LeftDotY { get => _ly; private set => Set(ref _ly, value); }
    public double RightDotX { get => _rx; private set => Set(ref _rx, value); }
    public double RightDotY { get => _ry; private set => Set(ref _ry, value); }

    public void Update(ControllerSnapshot snap, IReadOnlyCollection<InputId> boundInputs)
    {
        IsConnected = snap.Active is not null;
        Status = snap.Active is { } a ? $"{a.Name} Connected" : "No Controller Detected";
        IsLive = snap.LiveReading;

        var sig = string.Join("|", snap.Connected.Select(c => c.Label));
        if (sig != _slotSignature)
        {
            _slotSignature = sig;
            RebuildSlots(snap.Connected);
        }

        var r = snap.Reading;
        foreach (var b in Buttons)
        {
            b.IsPressed = r.IsPressed(b.Button);
            b.IsBound = boundInputs.Contains(InputId.Pad(b.Button));
        }
        LeftTrigger = r.LeftTrigger;
        RightTrigger = r.RightTrigger;
        LtPressed = r.IsPressed(ControllerButton.LT);
        RtPressed = r.IsPressed(ControllerButton.RT);
        LeftDotX = 152 - 8 + r.LeftX * StickTravel;
        LeftDotY = 136 - 8 - r.LeftY * StickTravel;
        RightDotX = 350 - 8 + r.RightX * StickTravel;
        RightDotY = 218 - 8 - r.RightY * StickTravel;
    }

    private void RebuildSlots(IReadOnlyList<ControllerInfo> connected)
    {
        for (var i = 0; i < 4; i++)
        {
            var c = connected.FirstOrDefault(x => x.Slot == i);
            SlotOptions[i + 1].Display = c is null ? $"Controller {i + 1} (not connected)" : c.Label;
        }
    }
}

public sealed class SlotOption : ObservableObject
{
    private string _display;

    public SlotOption(int value, string display)
    {
        Value = value;
        _display = display;
    }

    public int Value { get; }
    public string Display { get => _display; set => Set(ref _display, value); }

    public override string ToString() => _display;
}
