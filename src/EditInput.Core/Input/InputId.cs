using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EditInput.Core.Input;

public enum DeviceKind : byte
{
    None = 0,
    Keyboard = 1,
    Mouse = 2,
    Controller = 3,
}

public enum MouseButton
{
    Left = 1,
    Right = 2,
    Middle = 3,
    X1 = 4,
    X2 = 5,
}

/// <summary>
/// Logical controller inputs. Values are bit positions used by <see cref="Controller.ControllerStateInterpreter"/>.
/// Paddles are not listed because XInput does not expose them (Elite/GameSir paddles are usually mapped onto a
/// standard button by the controller itself, which then works normally).
/// </summary>
public enum ControllerButton
{
    A = 0,
    B,
    X,
    Y,
    LB,
    RB,
    LT,
    RT,
    LS,
    RS,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    View,
    Menu,
    LStickUp,
    LStickDown,
    LStickLeft,
    LStickRight,
    RStickUp,
    RStickDown,
    RStickLeft,
    RStickRight,
}

/// <summary>
/// A single bindable input: a keyboard virtual-key, a mouse button or a controller button.
/// Serialized as readable strings such as "Key:E", "Mouse:X1", "Pad:RT" or "None".
/// </summary>
[JsonConverter(typeof(InputIdJsonConverter))]
public readonly record struct InputId(DeviceKind Kind, int Code)
{
    public static readonly InputId None = default;

    public bool IsNone => Kind == DeviceKind.None;

    public static InputId Key(int virtualKey) => new(DeviceKind.Keyboard, virtualKey);
    public static InputId Mouse(MouseButton button) => new(DeviceKind.Mouse, (int)button);
    public static InputId Pad(ControllerButton button) => new(DeviceKind.Controller, (int)button);

    public MouseButton MouseButton => (MouseButton)Code;
    public ControllerButton ControllerButton => (ControllerButton)Code;

    /// <summary>Short label for UI bind boxes, e.g. "E", "Mouse 4", "RT".</summary>
    public string DisplayName => Kind switch
    {
        DeviceKind.None => "Choose Bind",
        DeviceKind.Keyboard => KeyNames.GetDisplayName(Code),
        DeviceKind.Mouse => MouseButton switch
        {
            MouseButton.Left => "Left Click",
            MouseButton.Right => "Right Click",
            MouseButton.Middle => "Middle Click",
            MouseButton.X1 => "Mouse 4",
            MouseButton.X2 => "Mouse 5",
            _ => $"Mouse {Code}",
        },
        DeviceKind.Controller => ControllerNames.GetDisplayName(ControllerButton),
        _ => "?",
    };

    public string Serialize() => Kind switch
    {
        DeviceKind.None => "None",
        DeviceKind.Keyboard => "Key:" + KeyNames.GetId(Code),
        DeviceKind.Mouse => "Mouse:" + (Enum.IsDefined(MouseButton) ? MouseButton.ToString() : Code.ToString(CultureInfo.InvariantCulture)),
        DeviceKind.Controller => "Pad:" + (Enum.IsDefined(ControllerButton) ? ControllerButton.ToString() : Code.ToString(CultureInfo.InvariantCulture)),
        _ => "None",
    };

    public static bool TryParse(string? text, out InputId id)
    {
        id = None;
        if (string.IsNullOrWhiteSpace(text) || text.Equals("None", StringComparison.OrdinalIgnoreCase))
            return true;

        var sep = text.IndexOf(':');
        if (sep <= 0) return false;
        var prefix = text[..sep].Trim();
        var value = text[(sep + 1)..].Trim();

        switch (prefix.ToLowerInvariant())
        {
            case "key":
                if (KeyNames.TryParseId(value, out var vk)) { id = Key(vk); return true; }
                return false;
            case "mouse":
                if (Enum.TryParse<MouseButton>(value, true, out var mb) && Enum.IsDefined(mb)) { id = Mouse(mb); return true; }
                return false;
            case "pad":
                if (Enum.TryParse<ControllerButton>(value, true, out var cb) && Enum.IsDefined(cb)) { id = Pad(cb); return true; }
                return false;
            default:
                return false;
        }
    }

    public static InputId Parse(string? text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"Unrecognised input '{text}'.");

    public override string ToString() => Serialize();
}

public sealed class InputIdJsonConverter : JsonConverter<InputId>
{
    public override InputId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return InputId.None;
        var text = reader.GetString();
        // Unknown values degrade to "None" rather than making the whole profile file unreadable.
        return InputId.TryParse(text, out var id) ? id : InputId.None;
    }

    public override void Write(Utf8JsonWriter writer, InputId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Serialize());
}

public static class ControllerNames
{
    public static string GetDisplayName(ControllerButton b) => b switch
    {
        ControllerButton.DPadUp => "D-Pad Up",
        ControllerButton.DPadDown => "D-Pad Down",
        ControllerButton.DPadLeft => "D-Pad Left",
        ControllerButton.DPadRight => "D-Pad Right",
        ControllerButton.LS => "Left Stick Click",
        ControllerButton.RS => "Right Stick Click",
        ControllerButton.LStickUp => "L-Stick Up",
        ControllerButton.LStickDown => "L-Stick Down",
        ControllerButton.LStickLeft => "L-Stick Left",
        ControllerButton.LStickRight => "L-Stick Right",
        ControllerButton.RStickUp => "R-Stick Up",
        ControllerButton.RStickDown => "R-Stick Down",
        ControllerButton.RStickLeft => "R-Stick Left",
        ControllerButton.RStickRight => "R-Stick Right",
        _ => b.ToString(),
    };
}
