using System.Collections.ObjectModel;
using System.Windows.Input;
using EditInput.App.Mvvm;
using EditInput.Core.Engine;
using EditInput.Core.Logging;

namespace EditInput.App.ViewModels;

/// <summary>Live input visualiser + recent log lines.</summary>
public sealed class DebugViewModel : ObservableObject
{
    private readonly Logger _logger;
    private long _lastSeq;
    private string _edit = "UP", _select = "UP", _reset = "UP", _confirm = "UP", _controller = "DISCONNECTED",
        _rt = "0%", _lt = "0%", _state = "DISABLED";
    private bool _editDown, _selectDown, _resetDown, _confirmDown;

    public DebugViewModel(Logger logger)
    {
        _logger = logger;
        ClearCommand = new RelayCommand(() =>
        {
            _logger.ClearRecent();
            Lines.Clear();
        });
    }

    public ObservableCollection<string> Lines { get; } = new();
    public ICommand ClearCommand { get; }

    public string Edit { get => _edit; private set => Set(ref _edit, value); }
    public string Select { get => _select; private set => Set(ref _select, value); }
    public string Reset { get => _reset; private set => Set(ref _reset, value); }
    public string Confirm { get => _confirm; private set => Set(ref _confirm, value); }
    public bool EditDown { get => _editDown; private set => Set(ref _editDown, value); }
    public bool SelectDown { get => _selectDown; private set => Set(ref _selectDown, value); }
    public bool ResetDown { get => _resetDown; private set => Set(ref _resetDown, value); }
    public bool ConfirmDown { get => _confirmDown; private set => Set(ref _confirmDown, value); }
    public string Controller { get => _controller; private set => Set(ref _controller, value); }
    public string RightTrigger { get => _rt; private set => Set(ref _rt, value); }
    public string LeftTrigger { get => _lt; private set => Set(ref _lt, value); }
    public string AppState { get => _state; private set => Set(ref _state, value); }

    public void Update(EngineSnapshot s, ControllerViewModel pad, bool pullLogs)
    {
        EditDown = s.EditDown;
        SelectDown = s.SelectHeld;
        ResetDown = s.ResetHeld;
        ConfirmDown = s.ConfirmHeld;
        Edit = s.EditDown ? "DOWN" : "UP";
        Select = s.SelectHeld ? "DOWN" : "UP";
        Reset = s.ResetHeld ? "DOWN" : "UP";
        Confirm = s.ConfirmHeld ? "DOWN" : "UP";
        Controller = pad.IsConnected ? "CONNECTED" : "DISCONNECTED";
        RightTrigger = pad.IsLive ? pad.RightTriggerText : "–";
        LeftTrigger = pad.IsLive ? pad.LeftTriggerText : "–";
        AppState = MainViewModel.StateName(s);

        if (!pullLogs) return;
        var recent = _logger.GetRecent();
        if (recent.Count == 0 || recent[^1].Sequence == _lastSeq) return;
        foreach (var e in recent.Where(e => e.Sequence > _lastSeq)) Lines.Add(e.Text);
        _lastSeq = recent[^1].Sequence;
        while (Lines.Count > 400) Lines.RemoveAt(0);
    }
}
