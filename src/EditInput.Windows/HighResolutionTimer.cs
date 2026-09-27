using EditInput.Core.Engine;
using EditInput.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace EditInput.Windows;

/// <summary>
/// Wraps a Windows 10 1803+ high-resolution waitable timer (~0.5 ms accuracy without raising the global
/// timer resolution). Falls back to timeBeginPeriod(1) when unavailable. One instance per thread.
/// </summary>
public sealed class HighResolutionTimer : IDisposable
{
    private readonly SafeWaitHandle? _handle;
    private readonly TimerWaitHandle? _waitHandle;
    private readonly bool _raisedPeriod;

    public HighResolutionTimer()
    {
        try
        {
            var h = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null,
                NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
            if (!h.IsInvalid)
            {
                _handle = h;
                _waitHandle = new TimerWaitHandle(h);
                return;
            }
            h.Dispose();
        }
        catch (EntryPointNotFoundException) { }

        // Older Windows: fall back to 1 ms scheduler granularity for the lifetime of this object.
        _raisedPeriod = NativeMethods.timeBeginPeriod(1) == 0;
    }

    public bool IsHighResolution => _handle is not null;

    /// <summary>Waits until <paramref name="signal"/> is set or <paramref name="duration"/> elapses.</summary>
    public bool Wait(WaitHandle? signal, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return signal?.WaitOne(0) ?? false;

        if (_handle is not null && _waitHandle is not null)
        {
            var due = -Math.Max(1L, duration.Ticks); // relative, 100 ns units
            if (NativeMethods.SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                if (signal is null)
                {
                    _waitHandle.WaitOne();
                    return false;
                }
                return WaitHandle.WaitAny(new[] { signal, _waitHandle }) == 0;
            }
        }

        var ms = Math.Max(1, (int)Math.Round(duration.TotalMilliseconds));
        if (signal is null)
        {
            Thread.Sleep(ms);
            return false;
        }
        return signal.WaitOne(ms);
    }

    public void Dispose()
    {
        _waitHandle?.Dispose();
        _handle?.Dispose();
        if (_raisedPeriod) NativeMethods.timeEndPeriod(1);
    }

    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle h)
        {
            // The SafeWaitHandle is owned (and disposed) by HighResolutionTimer.
            SafeWaitHandle = new SafeWaitHandle(h.DangerousGetHandle(), ownsHandle: false);
        }
    }
}

/// <summary>Engine waiter backed by <see cref="HighResolutionTimer"/>, spinning only for the last ~0.3 ms.</summary>
public sealed class HighResolutionWaiter : IPreciseWaiter
{
    private readonly HighResolutionTimer _timer = new();
    private const double SpinThresholdMs = 0.3;

    public void Wait(WaitHandle signal, long? deadline, IClock clock)
    {
        if (deadline is null)
        {
            signal.WaitOne();
            return;
        }

        while (true)
        {
            var remainingMs = (deadline.Value - clock.Now) * 1000.0 / clock.Frequency;
            if (remainingMs <= 0) return;
            if (remainingMs > SpinThresholdMs + (_timer.IsHighResolution ? 0.2 : 1.2))
            {
                var sleepMs = remainingMs - (_timer.IsHighResolution ? SpinThresholdMs : 1.2);
                if (_timer.Wait(signal, TimeSpan.FromMilliseconds(sleepMs))) return;
                continue;
            }
            if (signal.WaitOne(0)) return;
            Thread.SpinWait(32);
        }
    }

    public void Dispose() => _timer.Dispose();
}
