using System.Windows;
using System.Windows.Controls;

namespace EditInput.App.Views;

/// <summary>Small dark dialog used for prompts, confirmations and notices.</summary>
public partial class DialogWindow : Window
{
    private DialogWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    public string? Result { get; private set; }

    private void AddButton(string text, string result, bool isDefault = false, bool isCancel = false, string? style = null)
    {
        var b = new Button { Content = text, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
        if (style is not null) b.Style = (Style)FindResource(style);
        b.Click += (_, _) =>
        {
            Result = result;
            DialogResult = !isCancel;
        };
        Buttons.Children.Add(b);
    }

    private static Window? ActiveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ??
        (Application.Current?.MainWindow is { IsVisible: true } m ? m : null);

    private string? Run()
    {
        var owner = ActiveOwner();
        if (owner is not null && owner != this) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowDialog();
        return Result;
    }

    public static string? Prompt(string title, string message, string initial)
    {
        var d = new DialogWindow(title, message);
        d.Input.Visibility = Visibility.Visible;
        d.Input.Text = initial;
        d.AddButton("Cancel", "cancel", isCancel: true);
        d.AddButton("OK", "ok", isDefault: true, style: "PrimaryButton");
        d.Loaded += (_, _) => { d.Input.Focus(); d.Input.SelectAll(); };
        return d.Run() == "ok" ? d.Input.Text : null;
    }

    public static bool Confirm(string title, string message)
    {
        var d = new DialogWindow(title, message);
        d.AddButton("Cancel", "cancel", isCancel: true);
        d.AddButton("Delete", "ok", isDefault: true, style: "DangerButton");
        return d.Run() == "ok";
    }

    public static bool? AskSave(string profile)
    {
        var d = new DialogWindow("Unsaved Changes", $"Save changes to the profile '{profile}'?");
        d.AddButton("Cancel", "cancel", isCancel: true);
        d.AddButton("Don't Save", "discard");
        d.AddButton("Save", "save", isDefault: true, style: "PrimaryButton");
        return d.Run() switch
        {
            "save" => true,
            "discard" => false,
            _ => null,
        };
    }

    public static void Info(string title, string message)
    {
        var d = new DialogWindow(title, message);
        d.AddButton("OK", "ok", isDefault: true, isCancel: false, style: "PrimaryButton");
        d.Run();
    }
}

public sealed class DialogService : ViewModels.IDialogService
{
    public string? Prompt(string title, string message, string initial) => DialogWindow.Prompt(title, message, initial);
    public bool Confirm(string title, string message) => DialogWindow.Confirm(title, message);
    public bool? AskSaveChanges(string profileName) => DialogWindow.AskSave(profileName);
    public void Info(string title, string message) => DialogWindow.Info(title, message);
}
