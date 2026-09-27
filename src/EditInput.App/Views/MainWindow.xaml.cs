using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EditInput.App.ViewModels;

namespace EditInput.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Func<bool> _minimizeToTray;

    public MainWindow(MainViewModel vm, Func<bool> minimizeToTray)
    {
        InitializeComponent();
        _vm = vm;
        _minimizeToTray = minimizeToTray;
        DataContext = vm;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        IsVisibleChanged += (_, _) => _vm.WindowVisible = IsVisible && WindowState != WindowState.Minimized;
        StateChanged += OnStateChanged;
    }

    /// <summary>Set by the app when the user chose Exit (tray/menu) so closing doesn't just hide.</summary>
    public bool ExitRequested { get; set; }

    public event Action? ExitApplicationRequested;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        _vm.WindowVisible = IsVisible && WindowState != WindowState.Minimized;
        if (WindowState == WindowState.Minimized && _minimizeToTray()) Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (ExitRequested) return;
        // Closing the window exits the app; the app asks about unsaved changes and releases inputs first.
        e.Cancel = true;
        ExitApplicationRequested?.Invoke();
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}

internal static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)   // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));        // pre-20H1 value
        }
        catch
        {
            // purely cosmetic
        }
    }
}
