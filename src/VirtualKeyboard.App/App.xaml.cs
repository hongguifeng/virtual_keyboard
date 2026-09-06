using System.Configuration;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace VirtualKeyboard.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF owns the application lifetime; OnExit disposes the tray resource.")]
public partial class App : Application
{
    private TrayIconController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray = new TrayIconController(window);
        window.ShowCurrentKeyboard();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (MainWindow is IDisposable disposable) disposable.Dispose();
        base.OnExit(e);
    }
}
