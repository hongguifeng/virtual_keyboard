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
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = SingleInstanceCoordinator.CreateDefault(() =>
            Dispatcher.BeginInvoke(() => { if (MainWindow is MainWindow window) window.OpenSettingsWindow(); }));
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.NotifyPrimary();
            Shutdown();
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray = new TrayIconController(window);
        window.ShowCurrentKeyboard();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        if (MainWindow is IDisposable disposable) disposable.Dispose();
        base.OnExit(e);
    }
}
