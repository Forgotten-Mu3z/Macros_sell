using System.Collections.Frozen;
using System.ComponentModel;
using System.Runtime.InteropServices;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Windows.Native;

namespace EditInput.Windows.Input;

/// <summary>Marker placed in dwExtraInfo of every event this app injects, so hooks can ignore our own output.</summary>
public static class InjectionSignature
{
    public const nuint Value = 0x45444954; // "EDIT"
}

/// <summary>Immutable filter the hooks consult. Swapped atomically; hooks never lock.</summary>
public sealed class HookPolicy
{
    public static readonly HookPolicy Empty = new(FrozenSet<InputId>.Empty, FrozenSet<InputId>.Empty, false, false, false);

    public HookPolicy(FrozenSet<InputId> watched, FrozenSet<InputId> blocked, bool forwardAll, bool swallowKeyboard, bool ignoreForeignInjected)
    {
        Watched = watched;
        Blocked = blocked;
        ForwardAll = forwardAll;
        SwallowKeyboard = swallowKeyboard;
        IgnoreForeignInjected = ignoreForeignInjected;
    }

    /// <summary>Inputs forwarded to the engine. Nothing else is ever forwarded or logged (no keylogging).</summary>
    public FrozenSet<InputId> Watched { get; }
    /// <summary>Inputs swallowed so other apps don't see them (Simple Remap sources while enabled).</summary>
    public FrozenSet<InputId> Blocked { get; }
    /// <summary>Bind capture: forward every key/button.</summary>
    public bool ForwardAll { get; }
    /// <summary>Bind capture: keep keystrokes away from the focused window.</summary>
    public bool SwallowKeyboard { get; }
    public bool IgnoreForeignInjected { get; }

    public bool NeedsMouseHook => ForwardAll || Watched.Any(w => w.Kind == DeviceKind.Mouse);
}

/// <summary>WH_KEYBOARD_LL listener. The callback does a set lookup and a queue post, nothing more.</summary>
public sealed class KeyboardInputManager : IDisposable
{
    private readonly HookThread _thread;
    private readonly Action<InputId, bool> _sink;
    private readonly ILogger _log;
    private readonly NativeMethods.LowLevelProc _proc; // must stay referenced while the hook is installed
    private readonly HashSet<int> _blockedDown = new(); // hook thread only
    private volatile HookPolicy _policy = HookPolicy.Empty;
    private IntPtr _hook;

    public KeyboardInputManager(HookThread thread, Action<InputId, bool> sink, ILogger log)
    {
        _thread = thread;
        _sink = sink;
        _log = log;
        _proc = HookProc;
    }

    public HookPolicy Policy
    {
        get => _policy;
        set => _policy = value;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install() => _thread.InvokeAndWait(() =>
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _log.Error("Keyboard", "Keyboard hook failed", new Win32Exception(Marshal.GetLastWin32Error()));
        else
            _log.Info("Keyboard", "Keyboard hook installed");
    }, TimeSpan.FromSeconds(3));

    public void Uninstall() => _thread.InvokeAndWait(() =>
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _blockedDown.Clear();
    }, TimeSpan.FromSeconds(3));

    private unsafe IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            try
            {
                var kb = (NativeMethods.KBDLLHOOKSTRUCT*)lParam;
                if (kb->dwExtraInfo != InjectionSignature.Value && kb->vkCode != NativeMethods.VK_PACKET)
                {
                    var msg = (int)wParam;
                    var down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                    var up = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
                    if (down || up)
                    {
                        var policy = _policy;
                        var injected = (kb->flags & NativeMethods.LLKHF_INJECTED) != 0;
                        if (!(injected && policy.IgnoreForeignInjected))
                        {
                            var vk = (int)kb->vkCode;
                            var id = InputId.Key(vk);
                            if (policy.ForwardAll || policy.Watched.Contains(id)) _sink(id, down);

                            if (down && (policy.SwallowKeyboard || policy.Blocked.Contains(id)))
                            {
                                _blockedDown.Add(vk);
                                return 1;
                            }
                            // Only swallow a release whose press we swallowed, so a policy change can never
                            // leave another app with a key it saw go down but never up.
                            if (up && _blockedDown.Remove(vk)) return 1;
                        }
                    }
                }
            }
            catch
            {
                // Never let an exception escape into the OS hook chain.
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}

/// <summary>
/// WH_MOUSE_LL listener, installed only while a mouse button is actually bound (or during capture) because
/// the OS calls it for every mouse move. Moves are passed straight through without touching the payload.
/// </summary>
public sealed class MouseInputManager : IDisposable
{
    private readonly HookThread _thread;
    private readonly Action<InputId, bool> _sink;
    private readonly ILogger _log;
    private readonly NativeMethods.LowLevelProc _proc;
    private readonly HashSet<int> _blockedDown = new();
    private volatile HookPolicy _policy = HookPolicy.Empty;
    private IntPtr _hook;

    public MouseInputManager(HookThread thread, Action<InputId, bool> sink, ILogger log)
    {
        _thread = thread;
        _sink = sink;
        _log = log;
        _proc = HookProc;
    }

    public HookPolicy Policy
    {
        get => _policy;
        set
        {
            _policy = value;
            if (value.NeedsMouseHook) Install();
            else Uninstall();
        }
    }

    private void Install() => _thread.Invoke(() =>
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _log.Error("Mouse", "Mouse hook failed", new Win32Exception(Marshal.GetLastWin32Error()));
        else
            _log.Debug("Mouse", "Mouse hook installed");
    });

    private void Uninstall() => _thread.Invoke(() =>
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _blockedDown.Clear();
        _log.Debug("Mouse", "Mouse hook removed");
    });

    private unsafe IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION)
        {
            var msg = (int)wParam;
            MouseButton button;
            bool down;
            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN: button = MouseButton.Left; down = true; break;
                case NativeMethods.WM_LBUTTONUP: button = MouseButton.Left; down = false; break;
                case NativeMethods.WM_RBUTTONDOWN: button = MouseButton.Right; down = true; break;
                case NativeMethods.WM_RBUTTONUP: button = MouseButton.Right; down = false; break;
                case NativeMethods.WM_MBUTTONDOWN: button = MouseButton.Middle; down = true; break;
                case NativeMethods.WM_MBUTTONUP: button = MouseButton.Middle; down = false; break;
                case NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP:
                    down = msg == NativeMethods.WM_XBUTTONDOWN;
                    var data = ((NativeMethods.MSLLHOOKSTRUCT*)lParam)->mouseData >> 16;
                    button = data == NativeMethods.XBUTTON2 ? MouseButton.X2 : MouseButton.X1;
                    break;
                default:
                    return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam); // moves, wheel
            }

            try
            {
                var ms = (NativeMethods.MSLLHOOKSTRUCT*)lParam;
                if (ms->dwExtraInfo != InjectionSignature.Value)
                {
                    var policy = _policy;
                    var injected = (ms->flags & NativeMethods.LLMHF_INJECTED) != 0;
                    if (!(injected && policy.IgnoreForeignInjected))
                    {
                        var id = InputId.Mouse(button);
                        if (policy.ForwardAll || policy.Watched.Contains(id)) _sink(id, down);
                        if (down && policy.Blocked.Contains(id))
                        {
                            _blockedDown.Add((int)button);
                            return 1;
                        }
                        if (!down && _blockedDown.Remove((int)button)) return 1;
                    }
                }
            }
            catch
            {
                // never throw into the hook chain
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose() => _thread.InvokeAndWait(() =>
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }, TimeSpan.FromSeconds(3));
}
