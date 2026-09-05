using System.Windows;

namespace VirtualKeyboard.TestHost;

/// <summary>
/// TestHost 入口（仅测试进程，不含产品功能代码；NFR-COMP-001 / 设计 §18.2）。
/// 无参数：启动 WPF 测试页（六类控件区域 + 焦点/按键计数展示）。
/// --selftest：启动后运行页面自检，控制台逐条打印 PASS/FAIL，以退出码 0（全部通过）/ 1（失败或异常）结束。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        window.Show();

        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
        {
            window.UpdateLayout();
            try
            {
                Shutdown(TestHostSelfTest.Run(window));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 自检异常：{ex.Message}");
                Shutdown(1);
            }
        }
    }
}
