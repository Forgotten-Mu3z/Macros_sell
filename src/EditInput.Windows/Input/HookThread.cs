using System.Collections.Concurrent;
using EditInput.Core.Logging;
using EditInput.Windows.Native;

namespace EditInput.Windows.Input;

/// <summary>
/// Dedicated thread with a Win32 message loop. Low-level hooks are called on the thread that installed them,
/// so hook callbacks never run on (or wait for) the UI thread.
/// </summary>
public sealed class HookThread : IDisposable
{
    private const uint WM_RUN_WORK = NativeMethods.WM_APP + 1;

    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly ILogger _log;
    private readonly Thread _thread;
    private uint _threadId;
    private int _disposed;

    public HookThread(ILogger log)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "InputHooks", Priority = ThreadPriority.Highest };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public bool IsHookThread => Thread.CurrentThread == _thread;

    /// <summary>Runs <paramref name="action"/> on the hook thread (inline if already there).</summary>
    public void Invoke(Action action)
    {
        if (IsHookThread)
        {
            action();
            return;
        }
        _work.Enqueue(action);
        NativeMethods.PostThreadMessage(_threadId, WM_RUN_WORK, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Runs <paramref name="action"/> on the hook thread and waits for it (bounded).</summary>
    public bool InvokeAndWait(Action action, TimeSpan timeout)
    {
        if (IsHookThread)
        {
            action();
            return true;
        }
        using var done = new ManualResetEventSlim();
        Invoke(() =>
        {
            try { action(); }
            finally { done.Set(); }
        });
        return done.Wait(timeout);
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // force message queue creation
        _ready.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_RUN_WORK) Drain();
        }
        Drain();
    }

    private void Drain()
    {
        while (_work.TryDequeue(out var a))
        {
            try { a(); }
            catch (Exception ex) { _log.Error("Hooks", "Hook thread work item failed", ex); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
