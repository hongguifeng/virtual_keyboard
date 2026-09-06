using System.Windows;
using System.Windows.Input;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

/// <summary>最小 NoActivate 键盘窗口；T1.1 不包含目标捕获或输入注入。</summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly OverlayWindowAdapter _overlay;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _overlay = new OverlayWindowAdapter(this);
    }

    internal void ShowAt(int x, int y, int width, int height) => _overlay.ShowAt(x, y, width, height);

    private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = sender;
        _ = e;
        // DragMove 会激活 WPF 窗口；物理像素拖动在 T3.6 通过适配器实现。
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _overlay.Close();
    }

    private void OnKeyAClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        // SendInput 与 TargetSession 属于 T1.2/T1.3。
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _overlay.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
