using System.IO;
using EditInput.Core.Engine;
using EditInput.Core.Input;
using EditInput.Core.Logging;
using EditInput.Core.Output;
using EditInput.Core.Profiles;
using EditInput.Windows;
using EditInput.Windows.Input;
using EditInput.Windows.Output;

namespace EditInput.App.Services;

/// <summary>
/// Composition root. Creates every manager, wires them together and guarantees an orderly, input-safe
/// shutdown: hooks stop first (no new input), then the engine releases everything, then devices close.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly SemaphoreSlim _virtualPadGate = new(1, 1);
    private int _disposed;

    public AppServices()
    {
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortniteEditInput");
        Directory.CreateDirectory(DataDirectory);

        Logger = new Logger(Path.Combine(DataDirectory, "logs"));
        Settings = new SettingsManager(DataDirectory, Logger);
        Logger.DebugEnabled = Settings.Current.DebugLogging;
        Profiles = new ProfileManager(DataDirectory, Logger);

        VirtualPad = new ViGEmControllerOutput(Logger);
        Output = new OutputManager(new IOutputBackend[] { new SendInputBackend(), VirtualPad }, Logger);

        InputManager? input = null;
        var probe = new WindowsPhysicalStateProbe(() => input?.Controller);
        Engine = new MacroEngine(Output, StopwatchClock.Instance, Logger, probe);
        Host = new EngineHost(Engine, Output, StopwatchClock.Instance, Logger, new HighResolutionWaiter());

        input = new InputManager(Host, Logger);
        Input = input;
        Input.Controller.VirtualSlotProvider = () => VirtualPad.VirtualSlot;
        Input.Controller.Disconnected += info => Host.ControllerDisconnected(info.Label);
    }

    public string DataDirectory { get; }
    public Logger Logger { get; }
    public SettingsManager Settings { get; }
    public ProfileManager Profiles { get; }
    public ViGEmControllerOutput VirtualPad { get; }
    public OutputManager Output { get; }
    public MacroEngine Engine { get; }
    public EngineHost Host { get; }
    public InputManager Input { get; }

    public bool ControllerOutputAvailable { get; private set; }

    /// <summary>Completes once the ViGEmBus driver check has finished.</summary>
    public Task DriverProbe { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        Host.Start();
        Input.Start();
        // Probe the virtual controller driver off the UI thread.
        DriverProbe = Task.Run(() => ControllerOutputAvailable = VirtualPad.DriverAvailable);
        Logger.Info("App", $"Started. Data folder: {DataDirectory}");
    }

    /// <summary>Pushes a profile + settings to the engine and every input source.</summary>
    public void Apply(Profile profile, AppSettings settings, string reason)
    {
        var cfg = EngineConfig.From(profile, settings);
        Logger.DebugEnabled = settings.DebugLogging;
        Host.ApplyConfig(cfg, reason);
        Input.ApplyConfig(cfg, settings.IgnoreForeignInjectedInput);
        Input.Controller.Configure(profile.TriggerThresholdPercent, profile.StickDeadzonePercent, profile.ControllerSlot);
        Input.Controller.PollRateHz = settings.ControllerPollRateHz;

        var needsVirtualPad = cfg.OutputInputs().Any(o => o.Kind == DeviceKind.Controller && cfg.DeviceAllowed(o));
        _ = Task.Run(async () =>
        {
            await _virtualPadGate.WaitAsync().ConfigureAwait(false);
            try { VirtualPad.SetRequired(needsVirtualPad); }
            catch (Exception ex) { Logger.Error("App", "Virtual controller update failed", ex); }
            finally { _virtualPadGate.Release(); }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Logger.Info("App", "Shutting down");
        Host.ReleaseAllNow("Shutdown");
        Safe(() => Input.Dispose());
        Safe(() => Host.Dispose());      // releases everything again on the engine thread's way out
        Safe(() => VirtualPad.Dispose());
        Safe(() => Logger.Dispose());
    }

    private void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Logger.Error("App", "Shutdown step failed", ex); }
    }
}
