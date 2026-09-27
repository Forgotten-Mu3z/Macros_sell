using System.Runtime.InteropServices;
using EditInput.Core.Input;
using EditInput.Core.Output;
using EditInput.Windows.Input;
using EditInput.Windows.Native;

namespace EditInput.Windows.Output;

/// <summary>
/// Keyboard + mouse injection through SendInput. Keys are sent as hardware scan codes (what games read via
/// raw input / DirectInput) and every event carries <see cref="InjectionSignature"/> so our own hooks ignore it.
/// </summary>
public sealed class SendInputBackend : IOutputBackend
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();
    private readonly NativeMethods.INPUT[] _buffer = new NativeMethods.INPUT[1];

    public bool CanHandle(DeviceKind kind) => kind is DeviceKind.Keyboard or DeviceKind.Mouse;

    public bool IsAvailable => true;

    // Callers (OutputManager) serialise access, so the shared buffer is safe.
    public bool Send(InputId id, bool down)
    {
        _buffer[0] = id.Kind == DeviceKind.Keyboard ? Keyboard(id.Code, down) : Mouse(id.MouseButton, down);
        return NativeMethods.SendInput(1, _buffer, InputSize) == 1;
    }

    public void Neutralize()
    {
        // Nothing to reset: keyboard/mouse state only changes through Send, which OutputManager tracks.
    }

    private static NativeMethods.INPUT Keyboard(int vk, bool down)
    {
        var scan = NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC_EX);
        var flags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP;
        ushort wScan = 0;
        ushort wVk = (ushort)vk;

        if (scan != 0)
        {
            flags |= NativeMethods.KEYEVENTF_SCANCODE;
            if ((scan & 0xFF00) is 0xE000 or 0xE100 || KeyNames.IsExtendedKey(vk)) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            wScan = (ushort)(scan & 0xFF);
            wVk = 0;
        }
        else if (KeyNames.IsExtendedKey(vk))
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = wVk,
                    wScan = wScan,
                    dwFlags = flags,
                    dwExtraInfo = InjectionSignature.Value,
                },
            },
        };
    }

    private static NativeMethods.INPUT Mouse(MouseButton button, bool down)
    {
        uint flags, data = 0;
        switch (button)
        {
            case MouseButton.Left: flags = down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP; break;
            case MouseButton.Right: flags = down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP; break;
            case MouseButton.Middle: flags = down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP; break;
            case MouseButton.X1:
                flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                data = NativeMethods.XBUTTON1;
                break;
            case MouseButton.X2:
                flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                data = NativeMethods.XBUTTON2;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button");
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT { mouseData = data, dwFlags = flags, dwExtraInfo = InjectionSignature.Value },
            },
        };
    }
}
