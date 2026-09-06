using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Targeting;
using Xunit;

namespace VirtualKeyboard.IntegrationTests;

/// <summary>
/// 主窗口与设置窗口的打开路径（bug 回归）：
/// WPF 规定从未 Show 过的 Window 不能被设为 Owner（否则 InvalidOperationException）。
/// 应用刚启动时主窗口尚未显示（键盘从未弹出），此时从托盘右键菜单打开设置窗口曾直接崩溃，
/// 必须等键盘弹出过一次后才可用；修复后：
///   - 主窗口从未显示 → 设置窗口作为独立顶层窗口打开（无 Owner）；
///   - 主窗口已显示过 → 设置窗口 Owner = 主窗口（原行为不变）。
/// </summary>
public sealed class MainWindowSettingsTests
{
    [Fact]
    public void OpenSettingsWindowBeforeMainWindowEverShownOpensWindowWithoutOwner()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var main = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            Assert.False(main.IsLoaded); // 模拟托盘菜单场景：主窗口从未显示

            var settings = OpenAndCaptureSettingsWindow(main);

            // 从未 Show 的主窗口不能当 Owner（修复前这行 Owner = this 抛 InvalidOperationException）
            Assert.Null(settings.Owner);
            Assert.False(settings.IsVisible); // 模态窗口已被回调关闭
        });
    }

    [Fact]
    public void OpenSettingsWindowAfterMainWindowShownAssignsMainAsOwner()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var main = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            main.Show();
            try
            {
                var settings = OpenAndCaptureSettingsWindow(main);
                Assert.Same(main, settings.Owner); // 主窗口显示过 → Owner 行为保持不变
            }
            finally
            {
                main.Close();
            }
        });
    }

    /// <summary>
    /// 调用 OpenSettingsWindow（内部 ShowDialog 阻塞当前线程），
    /// 通过预先投递到同一 Dispatcher 的回调在模态循环内关闭并捕获 SettingsWindow。
    /// 不用 Application 枚举窗口（测试进程不创建 WPF Application），
    /// 而是用 user32 EnumWindows + HwndSource 找到属于当前线程的 SettingsWindow。
    /// </summary>
    private static SettingsWindow OpenAndCaptureSettingsWindow(MainWindow main)
    {
        SettingsWindow? captured = null;
        main.Dispatcher.BeginInvoke(new Action(() =>
        {
            captured = new SettingsWindowFinder(main.Dispatcher).Find();
            Assert.NotNull(captured);
            captured!.Close();
        }));

        main.OpenSettingsWindow(); // ShowDialog 的嵌套消息循环处理上面的回调后返回
        return captured!;
    }

    /// <summary>EnumWindows 回调持有类（限定只找当前线程的 SettingsWindow；本地变量在方法结束前保持活动，委托不会被 GC）。</summary>
    private sealed class SettingsWindowFinder
    {
        private readonly Dispatcher _owner;
        private SettingsWindow? _result;

        public SettingsWindowFinder(Dispatcher owner) => _owner = owner;

        public SettingsWindow Find()
        {
            uint thisThread = GetCurrentThreadId();
            var proc = new EnumWindowsProc((hwnd, _) =>
            {
                if (GetWindowThreadProcessId(hwnd, out uint processId) != thisThread) return true;
                if (HwndSource.FromHwnd(hwnd)?.RootVisual is SettingsWindow settings
                    && ReferenceEquals(settings.Dispatcher, _owner))
                {
                    _result = settings;
                    return false;
                }
                return true;
            });
            EnumWindows(proc, IntPtr.Zero);
            return _result!;
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.MainWindowSettingsTests.{Guid.NewGuid():N}");
        public Fixture()
        {
            ConfigurationFile = Path.Combine(_root, "config.json");
            Repository = new(new(ConfigurationFile, Path.Combine(_root, "recovery")));
        }
        public string ConfigurationFile { get; }
        public ConfigurationRepository Repository { get; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class UnusedCapture : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => TargetCaptureResult.Failure(TargetCaptureStatus.NoForegroundWindow);
    }
}
