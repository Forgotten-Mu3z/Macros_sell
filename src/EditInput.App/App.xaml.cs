using System.Windows;
using System.Windows.Threading;
using EditInput.App.Services;
using EditInput.App.ViewModels;
using EditInput.App.Views;
using EditInput.Core.Logging;
using Microsoft.Win32;

namespace EditInput.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\FortniteEditInput.SingleInstance";
    private const string ShowEventName = @"Local\FortniteEditInput.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppServices? _services;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private UIManager? _ui;
    private int _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two instances would both inject inputs. Hand over to the running one instead.
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* old instance is exiting */ }
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _services?.Host.ReleaseAllNow("Process exit");
        DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _services?.Logger.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            _services = new AppServices();
            _services.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fortnite Edit Input could not start:\n\n{ex.Message}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            _services?.Dispose();
            Shutdown(1);
            return;
        }

        var settings = _services.Settings.Current;
        _vm = new MainViewModel(_services, new DialogService(), ShowWelcome);
        _window = new MainWindow(_vm, () => _services.Settings.Current.MinimizeToTray);
        _window.ExitApplicationRequested += ExitApplication;
        MainWindow = _window;
        _ui = new UIManager(_services, _vm, _window, ExitApplication);
        _vm.Initialize();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SessionEnding += (_, _) => _services.Host.ReleaseAllNow("Windows session ending");

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _ui?.ShowWindow()), null, Timeout.Infinite, false);

        var firstRun = _services.Settings.IsFirstRun;
        var startHidden = !firstRun && settings.StartMinimized;
        if (startHidden && settings.MinimizeToTray)
        {
            _vm.WindowVisible = false; // tray only
        }
        else
        {
            if (startHidden) _window.WindowState = WindowState.Minimized;
            _window.Show();
        }

        if (firstRun) Dispatcher.BeginInvoke(ShowWelcome, DispatcherPriority.ApplicationIdle);
    }

    private void ShowWelcome()
    {
        if (_window is null || _services is null) return;
        if (!_window.IsVisible) _ui?.ShowWindow();
        var w = new WelcomeWindow { Owner = _window };
        w.ShowDialog();
        _services.Settings.Update(s => s.FirstRunCompleted = true);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // Lock/unlock/remote switches can swallow key-ups: release and forget physical state.
        _services?.Host.SessionChanged($"Windows session {e.Reason}");
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            _services?.Host.SessionChanged($"Power {e.Mode}");
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A UI bug must never leave a key held: release first, then report and keep running.
        _services?.Host.ReleaseAllNow("UI exception");
        _services?.Logger.Error("App", "Unhandled UI exception", e.Exception);
        e.Handled = true;
        try
        {
            MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nAll generated inputs were released. Details are in the log.",
                "Fortnite Edit Input", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* dispatcher may be shutting down */ }
    }

    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _services?.Host.ReleaseAllNow("Fatal exception");
            _services?.Logger.Error("App", "Fatal unhandled exception", e.ExceptionObject as Exception);
        }
        catch { /* last chance */ }
    }

    private void ExitApplication()
    {
        if (_vm is not null && !_vm.ConfirmLeaveProfile()) return;
        if (Interlocked.Exchange(ref _exiting, 1) != 0) return;

        _services?.Host.ReleaseAllNow("Exit");
        if (_window is not null)
        {
            _window.ExitRequested = true;
            _window.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _ui?.Dispose();
        _vm?.Dispose();
        _services?.Dispose(); // stops hooks, releases every generated input, closes devices
        try { _instanceMutex?.ReleaseMutex(); } catch { /* not owned */ }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
