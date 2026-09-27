using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Core.Output;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace EditInput.Windows.Output;

/// <summary>
/// Optional controller output through a virtual Xbox 360 pad (ViGEmBus driver). The virtual pad is only
/// plugged in while the active profile actually generates controller inputs, and its XInput slot is
/// reported to the poller so our own output is never read back as physical input.
/// </summary>
public sealed class ViGEmControllerOutput : IOutputBackend, IDisposable
{
    private const string Cat = "VirtualPad";

    private readonly ILogger _log;
    private readonly object _gate = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;
    private volatile int _userIndex = -1;
    private volatile bool _padConnected;
    private volatile string _status = "Checking for ViGEmBus driver…";
    private bool _driverChecked;

    public ViGEmControllerOutput(ILogger log) => _log = log;

    /// <summary>True when the ViGEmBus driver is installed (checked lazily, once).</summary>
    public bool DriverAvailable
    {
        get
        {
            lock (_gate)
            {
                EnsureClient();
                return _client is not null;
            }
        }
    }

    /// <summary>Human-readable state. Lock-free so the UI never waits on a slow driver call.</summary>
    public string Status => _status;

    /// <summary>XInput slot of the virtual pad, or -1.</summary>
    public int VirtualSlot => _userIndex;

    public bool CanHandle(DeviceKind kind) => kind == DeviceKind.Controller;

    public bool IsAvailable => _padConnected;

    /// <summary>Plugs the virtual pad in or out. Call from a background thread (connect can take a moment).</summary>
    public void SetRequired(bool required)
    {
        lock (_gate)
        {
            if (required)
            {
                if (_pad is not null) return;
                EnsureClient();
                if (_client is null) return;
                try
                {
                    var pad = _client.CreateXbox360Controller();
                    pad.AutoSubmitReport = false;
                    pad.Connect();
                    _pad = pad;
                    _padConnected = true;
                    _status = "Virtual controller active";
                    _log.Info(Cat, "Virtual Xbox controller connected");
                }
                catch (Exception ex)
                {
                    _log.Error(Cat, "Could not create virtual controller", ex);
                    _pad = null;
                    return;
                }
            }
            else
            {
                DisconnectPad();
                return;
            }
        }

        // The slot is assigned asynchronously by the XInput stack; poll briefly outside the lock.
        for (var i = 0; i < 40 && _userIndex < 0; i++)
        {
            lock (_gate)
            {
                if (_pad is null) return;
                try { _userIndex = _pad.UserIndex; }
                catch { /* not reported yet */ }
            }
            if (_userIndex < 0) Thread.Sleep(50);
        }
        if (_userIndex >= 0 && _padConnected) _status = $"Virtual controller active (slot {_userIndex + 1})";
    }

    public bool Send(InputId id, bool down)
    {
        lock (_gate)
        {
            if (_pad is null) return false;
            var b = id.ControllerButton;
            switch (b)
            {
                case ControllerButton.LT: _pad.SetSliderValue(Xbox360Slider.LeftTrigger, down ? (byte)255 : (byte)0); break;
                case ControllerButton.RT: _pad.SetSliderValue(Xbox360Slider.RightTrigger, down ? (byte)255 : (byte)0); break;
                case ControllerButton.LStickUp: _pad.SetAxisValue(Xbox360Axis.LeftThumbY, down ? short.MaxValue : (short)0); break;
                case ControllerButton.LStickDown: _pad.SetAxisValue(Xbox360Axis.LeftThumbY, down ? short.MinValue : (short)0); break;
                case ControllerButton.LStickLeft: _pad.SetAxisValue(Xbox360Axis.LeftThumbX, down ? short.MinValue : (short)0); break;
                case ControllerButton.LStickRight: _pad.SetAxisValue(Xbox360Axis.LeftThumbX, down ? short.MaxValue : (short)0); break;
                case ControllerButton.RStickUp: _pad.SetAxisValue(Xbox360Axis.RightThumbY, down ? short.MaxValue : (short)0); break;
                case ControllerButton.RStickDown: _pad.SetAxisValue(Xbox360Axis.RightThumbY, down ? short.MinValue : (short)0); break;
                case ControllerButton.RStickLeft: _pad.SetAxisValue(Xbox360Axis.RightThumbX, down ? short.MinValue : (short)0); break;
                case ControllerButton.RStickRight: _pad.SetAxisValue(Xbox360Axis.RightThumbX, down ? short.MaxValue : (short)0); break;
                default:
                    var button = MapButton(b);
                    if (button is null) return false;
                    _pad.SetButtonState(button, down);
                    break;
            }
            _pad.SubmitReport();
            return true;
        }
    }

    public void Neutralize()
    {
        lock (_gate)
        {
            if (_pad is null) return;
            try
            {
                _pad.ResetReport();
                _pad.SubmitReport();
            }
            catch (Exception ex)
            {
                _log.Error(Cat, "Could not neutralise virtual controller", ex);
            }
        }
    }

    private static Xbox360Button? MapButton(ControllerButton b) => b switch
    {
        ControllerButton.A => Xbox360Button.A,
        ControllerButton.B => Xbox360Button.B,
        ControllerButton.X => Xbox360Button.X,
        ControllerButton.Y => Xbox360Button.Y,
        ControllerButton.LB => Xbox360Button.LeftShoulder,
        ControllerButton.RB => Xbox360Button.RightShoulder,
        ControllerButton.LS => Xbox360Button.LeftThumb,
        ControllerButton.RS => Xbox360Button.RightThumb,
        ControllerButton.DPadUp => Xbox360Button.Up,
        ControllerButton.DPadDown => Xbox360Button.Down,
        ControllerButton.DPadLeft => Xbox360Button.Left,
        ControllerButton.DPadRight => Xbox360Button.Right,
        ControllerButton.View => Xbox360Button.Back,
        ControllerButton.Menu => Xbox360Button.Start,
        _ => null,
    };

    private void EnsureClient()
    {
        if (_client is not null || _driverChecked) return;
        _driverChecked = true;
        try
        {
            _client = new ViGEmClient();
            _status = "Virtual controller idle (plugged in only when a profile needs it)";
            _log.Info(Cat, "ViGEmBus driver found");
        }
        catch (Exception ex)
        {
            _client = null;
            _status = "ViGEmBus not installed – controller output unavailable";
            _log.Info(Cat, $"ViGEmBus driver not available ({ex.GetType().Name}); controller output disabled");
        }
    }

    private void DisconnectPad()
    {
        if (_pad is null) return;
        try
        {
            _pad.ResetReport();
            _pad.SubmitReport();
            _pad.Disconnect();
        }
        catch (Exception ex)
        {
            _log.Error(Cat, "Virtual controller disconnect failed", ex);
        }
        _pad = null;
        _padConnected = false;
        _userIndex = -1;
        if (_client is not null) _status = "Virtual controller idle (plugged in only when a profile needs it)";
        _log.Info(Cat, "Virtual Xbox controller disconnected");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisconnectPad();
            _client?.Dispose();
            _client = null;
        }
    }
}
