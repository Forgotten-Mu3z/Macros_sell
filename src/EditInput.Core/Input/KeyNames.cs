using System.Globalization;

namespace EditInput.Core.Input;

/// <summary>
/// Windows virtual-key codes with stable serialization ids and friendly display names.
/// Unknown codes are serialized as "VK_0x??" so no key is ever unrepresentable.
/// </summary>
public static class KeyNames
{
    public const int Escape = 0x1B;
    public const int F8 = 0x77;
    public const int F12 = 0x7B;

    private static readonly Dictionary<int, (string Id, string Display)> ByCode = new();
    private static readonly Dictionary<string, int> ById = new(StringComparer.OrdinalIgnoreCase);

    static KeyNames()
    {
        for (var c = 'A'; c <= 'Z'; c++) Add(c, c.ToString(), c.ToString());
        for (var d = 0; d <= 9; d++) Add(0x30 + d, "D" + d, d.ToString(CultureInfo.InvariantCulture));
        for (var f = 1; f <= 24; f++) Add(0x6F + f, "F" + f, "F" + f);
        for (var n = 0; n <= 9; n++) Add(0x60 + n, "NumPad" + n, "Num " + n);

        Add(0x08, "Backspace", "Backspace");
        Add(0x09, "Tab", "Tab");
        Add(0x0C, "Clear", "Clear");
        Add(0x0D, "Enter", "Enter");
        Add(0x10, "Shift", "Shift");
        Add(0x11, "Ctrl", "Ctrl");
        Add(0x12, "Alt", "Alt");
        Add(0x13, "Pause", "Pause");
        Add(0x14, "CapsLock", "Caps Lock");
        Add(Escape, "Escape", "Esc");
        Add(0x20, "Space", "Space");
        Add(0x21, "PageUp", "Page Up");
        Add(0x22, "PageDown", "Page Down");
        Add(0x23, "End", "End");
        Add(0x24, "Home", "Home");
        Add(0x25, "Left", "Left Arrow");
        Add(0x26, "Up", "Up Arrow");
        Add(0x27, "Right", "Right Arrow");
        Add(0x28, "Down", "Down Arrow");
        Add(0x2C, "PrintScreen", "Print Screen");
        Add(0x2D, "Insert", "Insert");
        Add(0x2E, "Delete", "Delete");
        Add(0x5B, "LWin", "Left Win");
        Add(0x5C, "RWin", "Right Win");
        Add(0x5D, "Apps", "Menu Key");
        Add(0x6A, "Multiply", "Num *");
        Add(0x6B, "Add", "Num +");
        Add(0x6C, "Separator", "Num Sep");
        Add(0x6D, "Subtract", "Num -");
        Add(0x6E, "Decimal", "Num .");
        Add(0x6F, "Divide", "Num /");
        Add(0x90, "NumLock", "Num Lock");
        Add(0x91, "ScrollLock", "Scroll Lock");
        Add(0xA0, "LShift", "Left Shift");
        Add(0xA1, "RShift", "Right Shift");
        Add(0xA2, "LCtrl", "Left Ctrl");
        Add(0xA3, "RCtrl", "Right Ctrl");
        Add(0xA4, "LAlt", "Left Alt");
        Add(0xA5, "RAlt", "Right Alt");
        Add(0xBA, "Semicolon", ";");
        Add(0xBB, "Equals", "=");
        Add(0xBC, "Comma", ",");
        Add(0xBD, "Minus", "-");
        Add(0xBE, "Period", ".");
        Add(0xBF, "Slash", "/");
        Add(0xC0, "Backtick", "`");
        Add(0xDB, "LBracket", "[");
        Add(0xDC, "Backslash", "\\");
        Add(0xDD, "RBracket", "]");
        Add(0xDE, "Quote", "'");
        Add(0xE2, "Oem102", "< >");
    }

    private static void Add(int code, string id, string display)
    {
        ByCode[code] = (id, display);
        ById[id] = code;
    }

    public static string GetId(int vk) =>
        ByCode.TryGetValue(vk, out var e) ? e.Id : "VK_0x" + vk.ToString("X2", CultureInfo.InvariantCulture);

    public static string GetDisplayName(int vk) =>
        ByCode.TryGetValue(vk, out var e) ? e.Display : "Key 0x" + vk.ToString("X2", CultureInfo.InvariantCulture);

    public static bool TryParseId(string id, out int vk)
    {
        if (ById.TryGetValue(id, out vk)) return true;
        if (id.StartsWith("VK_0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(id.AsSpan(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) &&
            vk is > 0 and < 0xFF)
            return true;
        vk = 0;
        return false;
    }

    /// <summary>Keys whose scan code needs the extended-key flag when injected.</summary>
    public static bool IsExtendedKey(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or // nav + arrows
        0x2D or 0x2E or 0x2C or                                           // insert, delete, print screen
        0x5B or 0x5C or 0x5D or                                           // win keys, apps
        0x6F or 0x90 or                                                   // num divide, num lock
        0xA3 or 0xA5;                                                     // right ctrl, right alt
}
