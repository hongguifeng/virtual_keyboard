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
        if (e.Args.Length == 2 && e.Args[0] == "--focus-worker" && int.TryParse(e.Args[1], out int parentId) && parentId > 0)
        {
            int exitCode;
            try { exitCode = VirtualKeyboard.Windows.FocusWorkerHost.Run(parentId); }
            catch (Exception) { exitCode = 3; } // Headless boundary: never display a worker error dialog.
            Environment.Exit(exitCode);
            return;
        }
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
        window.StartAutomaticFocusObservation();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.Dispose();
            window.SaveCurrentConfiguration();
        }
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
