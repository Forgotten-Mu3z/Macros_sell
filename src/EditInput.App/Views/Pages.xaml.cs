using System.Windows;
using System.Windows.Controls;
using EditInput.App.ViewModels;

namespace EditInput.App.Views;

public partial class EditPage : UserControl
{
    public EditPage() => InitializeComponent();

    private void OnUseAutomation(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Editor.IsRemap = false;
    }
}

public partial class RemapPage : UserControl
{
    public RemapPage() => InitializeComponent();
}

public partial class ControllerPage : UserControl
{
    public ControllerPage() => InitializeComponent();
}

public partial class DebugPage : UserControl
{
    public DebugPage() => InitializeComponent();
}

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}
