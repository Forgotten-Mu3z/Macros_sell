using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using EditInput.App.ViewModels;
using EditInput.App.Views;
using EditInput.Core.Engine;
using Forms = System.Windows.Forms;

namespace EditInput.App.Services;

/// <summary>Owns the main window and the tray icon (whose colour mirrors the engine status).</summary>
public sealed class UIManager : IDisposable
{
    private readonly AppServices _services;
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _tray;
    private readonly Dictionary<Color, Icon> _icons = new();
    private Color _currentColor = Color.Empty;

    public UIManager(AppServices services, MainViewModel vm, MainWindow window, Action exit)
    {
        _services = services;
        _window = window;

        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("Enable", null, (_, _) => services.Host.Enable("tray"));
        menu.Items.Add("Disable", null, (_, _) => services.Host.Disable("tray"));
        menu.Items.Add("Open Settings", null, (_, _) =>
        {
            vm.CurrentPage = Page.Settings;
            ShowWindow();
        });
        menu.Items.Add("Emergency Release", null, (_, _) => services.Host.EmergencyStop("tray"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _tray = new Forms.NotifyIcon
        {
            Text = "Fortnite Edit Input",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        UpdateTray(EngineSnapshot.Initial);
        vm.SnapshotChanged += UpdateTray;
    }

    public void ShowWindow() => _window.ShowAndActivate();

    public void HideToTray() => _window.Hide();

    private void UpdateTray(EngineSnapshot s)
    {
        var (color, text) = s.State switch
        {
            EngineState.EmergencyStopped => (Color.FromArgb(255, 77, 90), "EMERGENCY STOP"),
            EngineState.Error => (Color.FromArgb(255, 77, 90), "Error"),
            EngineState.Disabled => (Color.FromArgb(107, 114, 128), "Disabled"),
            _ => (Color.FromArgb(46, 204, 113), "Enabled"),
        };
        var tip = $"Fortnite Edit Input – {text}";
        if (s.ProfileName.Length > 0) tip += $" ({s.ProfileName})";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;

        if (color == _currentColor) return;
        _currentColor = color;
        if (!_icons.TryGetValue(color, out var icon))
        {
            icon = CreateIcon(color);
            _icons[color] = icon;
        }
        _tray.Icon = icon;
    }

    private static Icon CreateIcon(Color status)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(28, 32, 48));
            using var path = RoundedRect(new Rectangle(1, 1, 30, 30), 7);
            g.FillPath(bg, path);
            using var tile = new SolidBrush(Color.FromArgb(79, 140, 255));
            using var dim = new SolidBrush(Color.FromArgb(70, 78, 100));
            for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                g.FillRectangle(j == 0 || (i == 1 && j == 1) ? tile : dim, 6 + i * 7, 6 + j * 7, 5, 5);
            using var dot = new SolidBrush(status);
            using var ring = new Pen(Color.FromArgb(14, 16, 22), 2);
            g.FillEllipse(dot, 19, 19, 12, 12);
            g.DrawEllipse(ring, 19, 19, 12, 12);
        }

        var handle = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone(); // clone owns its own handle
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        foreach (var i in _icons.Values) i.Dispose();
        _icons.Clear();
    }
}
