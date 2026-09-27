using System.Windows;

namespace EditInput.App.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;
}
