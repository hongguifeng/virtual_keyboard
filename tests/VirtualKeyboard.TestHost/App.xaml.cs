using System.Windows;

// WPF/WinForms 共存（T0.5b1）：别名压制 WinForms 隐式全局 using，消除与 WPF 类型的二义。
using Application = System.Windows.Application;

namespace VirtualKeyboard.TestHost;

/// <summary>
/// TestHost 入口（仅测试进程，不含产品功能代码；NFR-COMP-001 / 设计 §18.2）。
/// 无参数：同时启动 WPF 测试页与 WinForms 测试页（T0.5b1）——两个明确分开的独立窗口。
/// --selftest：启动后依次运行 WPF 页自检（31 项）与 WinForms 页自检（36 项，T0.5b1），
/// 控制台逐条打印 PASS/FAIL，以退出码 0（全部通过）/ 1（任一失败或异常）结束。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 2 && e.Args[0] == "--focus-worker-fixture")
        {
            Environment.Exit(FocusWorkerFixture.Run(e.Args[1]));
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--focus-probe")
        {
            if (e.Args.Length != 3 || !int.TryParse(e.Args[2], out int seconds) || seconds is < 1 or > 600)
            {
                Console.WriteLine("Expected --focus-probe <new-output.jsonl> <seconds:1..600>");
                Shutdown(2);
                return;
            }
            try
            {
                var result = VirtualKeyboard.Windows.FocusComparisonProbe.Run(e.Args[1], TimeSpan.FromSeconds(seconds));
                Console.WriteLine($"Rows={result.Rows}; DroppedEvents={result.DroppedEvents}; WorkerStopped={result.WorkerStopped}; WriteError={result.WriteError}");
                Shutdown(result.WriteError != 0 ? 2 : result.WorkerStopped ? 0 : 3);
            }
            catch (Exception error)
            {
                Console.WriteLine($"ProbeError={error.HResult}");
                Shutdown(2);
            }
            return;
        }

        var window = new MainWindow();
        window.Show();

        var winFormsPage = new WinFormsTestPage();
        winFormsPage.Show();

        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
        {
            window.UpdateLayout();
            try
            {
                int wpfCode = TestHostSelfTest.Run(window);
                int winFormsCode = WinFormsSelfTest.Run(winFormsPage);
                Console.WriteLine($"=== 自检汇总：WPF 页退出码 {wpfCode}，WinForms 页退出码 {winFormsCode} ===");
                Shutdown(wpfCode | winFormsCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 自检异常：{ex.Message}");
                Shutdown(1);
            }
        }
    }
}
