using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VirtualKeyboard.TestHost;

/// <summary>
/// TestHost WPF 页自检（--selftest，在 UI 线程执行）：
/// 1) 六类控件区域均存在且关键属性正确（只读/AcceptsReturn/Focusable=false）；
/// 2) 程序化设置键盘焦点后 Keyboard.FocusedElement 正确、焦点展示随之刷新（真实 WPF 焦点语义）；
/// 3) 不可聚焦空白区不取得键盘焦点；
/// 4) 按键计数（与真实按键共用的 RecordKey 路径）逐控件与合计正确、展示串即时刷新。
/// 全部通过返回 0，任一失败返回 1；控制台逐条打印 PASS/FAIL 作为自动证据。
/// </summary>
internal static class TestHostSelfTest
{
    private static int _failed;

    public static int Run(MainWindow window)
    {
        _failed = 0;
        Console.WriteLine("=== TestHost WPF 页自检（--selftest）开始 ===");

        Check(window.NormalTextBox is TextBox, "普通 TextBox 存在（类型 TextBox）");
        Check(window.ReadOnlyTextBox is TextBox { IsReadOnly: true }, "只读 TextBox 存在且 IsReadOnly=true");
        Check(window.PasswordBox is PasswordBox, "PasswordBox 存在");
        Check(window.MultilineTextBox is TextBox { AcceptsReturn: true }, "多行编辑框存在且 AcceptsReturn=true");
        Check(window.SampleButton is Button, "Button 存在");
        Check(window.BlankArea is FrameworkElement { Focusable: false }, "不可聚焦空白区存在且 Focusable=false");

        foreach (var control in new FrameworkElement[] { window.NormalTextBox, window.ReadOnlyTextBox, window.PasswordBox, window.MultilineTextBox, window.SampleButton })
        {
            Check(control is { IsEnabled: true }, $"{control.Name} 可用（IsEnabled=true）");
        }

        // 真实 WPF 焦点语义：程序化设置焦点 → FocusedElement 生效 + 焦点展示刷新。
        FocusCheck(window, window.NormalTextBox);
        FocusCheck(window, window.ReadOnlyTextBox);
        FocusCheck(window, window.PasswordBox);
        FocusCheck(window, window.MultilineTextBox);
        FocusCheck(window, window.SampleButton);

        // 不可聚焦空白区：设置焦点请求后不得取得键盘焦点。
        Keyboard.Focus(window.BlankArea);
        Check(Keyboard.FocusedElement != window.BlankArea, "不可聚焦空白区不取得键盘焦点");

        // 按键计数：初始为 0；经与真实按键共用的 RecordKey 路径逐控件累加。
        Check(window.TotalKeyCount == 0, "初始按键计数合计为 0");
        foreach (var key in MainWindow.KeyOrder)
        {
            window.RecordKey(key);
        }

        foreach (var key in MainWindow.KeyOrder)
        {
            Check(window.KeyCount(key) == 1, $"{key} 计数=1");
        }

        Check(window.TotalKeyCount == 5, "合计计数=5");
        Check(window.KeyCountDisplay.Text.Contains("normal=1") && window.KeyCountDisplay.Text.Contains("password=1")
              && window.KeyCountDisplay.Text.Contains("合计=5"), "按键计数展示串即时刷新");
        window.RecordKey("normal");
        Check(window.KeyCount("normal") == 2 && window.TotalKeyCount == 6, "重复按键累加正确");

        var result = _failed == 0 ? "PASS（全部通过）" : $"FAIL（失败 {_failed} 项）";
        Console.WriteLine($"=== 自检结束：{result} ===");
        return _failed == 0 ? 0 : 1;
    }

    private static void FocusCheck(MainWindow window, FrameworkElement control)
    {
        Keyboard.Focus(control);
        Check(ReferenceEquals(Keyboard.FocusedElement, control), $"键盘焦点可落在 {control.Name}");
        Check(window.FocusDisplay.Text.Contains(control.Name), $"焦点展示刷新为 {control.Name}");
    }

    private static void Check(bool condition, string description)
    {
        if (condition)
        {
            Console.WriteLine($"[PASS] {description}");
        }
        else
        {
            Console.WriteLine($"[FAIL] {description}");
            _failed++;
        }
    }
}
