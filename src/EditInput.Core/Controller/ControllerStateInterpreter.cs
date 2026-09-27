using EditInput.Core.Input;

namespace EditInput.Core.Controller;

/// <summary>Raw gamepad report in XInput layout (independent of the actual controller API).</summary>
public readonly record struct RawPadState(ushort Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY)
{
    public const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008,
        Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080,
        LeftShoulder = 0x0100, RightShoulder = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
}

/// <summary>Result of interpreting one report: logical pressed buttons plus analog readouts for the UI.</summary>
public readonly record struct PadReading(uint Pressed, float LeftTrigger, float RightTrigger,
    float LeftX, float LeftY, float RightX, float RightY)
{
    public bool IsPressed(ControllerButton b) => (Pressed & (1u << (int)b)) != 0;
}

/// <summary>
/// Converts raw reports into logical button states with trigger threshold + hysteresis and a radial stick
/// deadzone. Keeps previous state so edges can be computed. Not thread-safe (owned by the poll thread).
/// </summary>
public sealed class ControllerStateInterpreter
{
    /// <summary>Absolute hysteresis applied below the trigger threshold to stop flicker.</summary>
    public const float TriggerHysteresis = 0.04f;

    /// <summary>Stick direction activation, as a fraction of the post-deadzone range.</summary>
    public const float StickActivate = 0.5f, StickRelease = 0.4f;

    private uint _pressed;

    public float TriggerThreshold { get; private set; } = 0.5f;
    public float StickDeadzone { get; private set; } = 0.1f;

    public uint Pressed => _pressed;

    public void Configure(int triggerThresholdPercent, int deadzonePercent)
    {
        TriggerThreshold = Math.Clamp(triggerThresholdPercent, 5, 95) / 100f;
        StickDeadzone = Math.Clamp(deadzonePercent, 0, 40) / 100f;
    }

    public void Reset() => _pressed = 0;

    /// <summary>Interprets a report and returns the bitmask of buttons whose state changed.</summary>
    public PadReading Interpret(in RawPadState raw, out uint changed)
    {
        var prev = _pressed;
        uint now = 0;

        void Set(ControllerButton b, bool on) { if (on) now |= 1u << (int)b; }
        bool Was(ControllerButton b) => (prev & (1u << (int)b)) != 0;

        Set(ControllerButton.A, (raw.Buttons & RawPadState.A) != 0);
        Set(ControllerButton.B, (raw.Buttons & RawPadState.B) != 0);
        Set(ControllerButton.X, (raw.Buttons & RawPadState.X) != 0);
        Set(ControllerButton.Y, (raw.Buttons & RawPadState.Y) != 0);
        Set(ControllerButton.LB, (raw.Buttons & RawPadState.LeftShoulder) != 0);
        Set(ControllerButton.RB, (raw.Buttons & RawPadState.RightShoulder) != 0);
        Set(ControllerButton.LS, (raw.Buttons & RawPadState.LeftThumb) != 0);
        Set(ControllerButton.RS, (raw.Buttons & RawPadState.RightThumb) != 0);
        Set(ControllerButton.DPadUp, (raw.Buttons & RawPadState.DPadUp) != 0);
        Set(ControllerButton.DPadDown, (raw.Buttons & RawPadState.DPadDown) != 0);
        Set(ControllerButton.DPadLeft, (raw.Buttons & RawPadState.DPadLeft) != 0);
        Set(ControllerButton.DPadRight, (raw.Buttons & RawPadState.DPadRight) != 0);
        Set(ControllerButton.View, (raw.Buttons & RawPadState.Back) != 0);
        Set(ControllerButton.Menu, (raw.Buttons & RawPadState.Start) != 0);

        var lt = raw.LeftTrigger / 255f;
        var rt = raw.RightTrigger / 255f;
        Set(ControllerButton.LT, TriggerPressed(lt, Was(ControllerButton.LT)));
        Set(ControllerButton.RT, TriggerPressed(rt, Was(ControllerButton.RT)));

        var (lx, ly) = ApplyDeadzone(raw.LeftX, raw.LeftY);
        var (rx, ry) = ApplyDeadzone(raw.RightX, raw.RightY);
        Set(ControllerButton.LStickUp, Dir(ly, Was(ControllerButton.LStickUp)));
        Set(ControllerButton.LStickDown, Dir(-ly, Was(ControllerButton.LStickDown)));
        Set(ControllerButton.LStickRight, Dir(lx, Was(ControllerButton.LStickRight)));
        Set(ControllerButton.LStickLeft, Dir(-lx, Was(ControllerButton.LStickLeft)));
        Set(ControllerButton.RStickUp, Dir(ry, Was(ControllerButton.RStickUp)));
        Set(ControllerButton.RStickDown, Dir(-ry, Was(ControllerButton.RStickDown)));
        Set(ControllerButton.RStickRight, Dir(rx, Was(ControllerButton.RStickRight)));
        Set(ControllerButton.RStickLeft, Dir(-rx, Was(ControllerButton.RStickLeft)));

        _pressed = now;
        changed = prev ^ now;
        return new PadReading(now, lt, rt, lx, ly, rx, ry);
    }

    private bool TriggerPressed(float value, bool wasPressed)
    {
        if (wasPressed)
            return value >= Math.Max(0.01f, TriggerThreshold - TriggerHysteresis);
        return value >= TriggerThreshold;
    }

    private static bool Dir(float component, bool wasPressed) =>
        wasPressed ? component > StickRelease : component > StickActivate;

    /// <summary>Radial deadzone, rescaled so output magnitude runs 0..1 outside the deadzone.</summary>
    private (float X, float Y) ApplyDeadzone(short rawX, short rawY)
    {
        var x = Math.Max(-1f, rawX / 32767f);
        var y = Math.Max(-1f, rawY / 32767f);
        var mag = MathF.Sqrt(x * x + y * y);
        if (mag <= StickDeadzone || mag <= 0f) return (0f, 0f);
        var scaled = Math.Min(1f, (mag - StickDeadzone) / (1f - StickDeadzone));
        return (x / mag * scaled, y / mag * scaled);
    }
}
