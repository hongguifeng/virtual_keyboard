using System.Configuration;
using System.Data;
using System.Windows;

namespace VirtualKeyboard.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.ShowAt(40, 40, 220, 116);
    }
}
