using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VirtualKeyboard.TestHost;

/// <summary>
/// TestHost WPF 测试页：提供 NFR-COMP-001 / 设计 §18.2 要求的六类控件区域，
/// 并实时显示当前键盘焦点与各控件按键计数。仅测试进程，不含任何产品功能代码。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>按键计数展示顺序（与 XAML 展示串格式一致）。</summary>
    internal static readonly string[] KeyOrder = { "normal", "readonly", "password", "multiline", "button" };

    private readonly Dictionary<string, int> _keyCounts = new();

    public MainWindow()
    {
        InitializeComponent();

        // 测试进程生命周期内事件随窗口销毁，无需显式解绑。
        // WPF 无全局静态焦点变化事件；对六类控件分别订阅焦点变化并刷新展示。
        foreach (var element in new UIElement[] { txtNormal, txtReadOnly, pwdPassword, txtMultiline, btnSample, areaBlank })
        {
            element.GotFocus += OnAnyFocusChanged;
            element.LostFocus += OnAnyFocusChanged;
        }
        UpdateFocusDisplay();
        UpdateKeyDisplay();
    }

    internal TextBox NormalTextBox => txtNormal;

    internal TextBox ReadOnlyTextBox => txtReadOnly;

    internal PasswordBox PasswordBox => pwdPassword;

    internal TextBox MultilineTextBox => txtMultiline;

    internal Button SampleButton => btnSample;

    internal Border BlankArea => areaBlank;

    internal TextBlock FocusDisplay => txtCurrentFocus;

    internal TextBlock KeyCountDisplay => txtKeyCounts;

    internal int TotalKeyCount => _keyCounts.Values.Sum();

    internal int KeyCount(string key) => _keyCounts.GetValueOrDefault(key);

    /// <summary>真实按键路径：五个可聚焦控件的 XAML PreviewKeyDown 绑定共用此处理器。</summary>
    private void OnControlKeyDown(object sender, KeyEventArgs e)
    {
        // e 仅由 XAML 事件绑定签名要求；计数只关心控件标识。
        _ = e;
        if (sender is FrameworkElement element)
        {
            RecordKey(ControlKey(element.Name));
        }
    }

    /// <summary>
    /// 按键计数与展示刷新。真实按键（OnControlKeyDown）与 selftest（--selftest）
    /// 共用此路径，保证两条路径的计数与展示行为一致。
    /// 只记录次数，不读取任何密码值（NFR-PRI）。
    /// </summary>
    internal void RecordKey(string key)
    {
        _keyCounts[key] = _keyCounts.GetValueOrDefault(key) + 1;
        UpdateKeyDisplay();
    }

    private void OnAnyFocusChanged(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateFocusDisplay();
    }

    /// <summary>焦点展示刷新（Keyboard.FocusChanged 回调与 selftest 共用）。</summary>
    internal void UpdateFocusDisplay()
    {
        var focused = Keyboard.FocusedElement as FrameworkElement;
        txtCurrentFocus.Text = focused is null
            ? "(none)"
            : $"{focused.Name} ({focused.GetType().Name})";
    }

    private void UpdateKeyDisplay()
    {
        var parts = string.Join("  ", KeyOrder.Select(k => $"{k}={_keyCounts.GetValueOrDefault(k)}"));
        txtKeyCounts.Text = $"{parts}（合计={TotalKeyCount}）";
    }

    private static string ControlKey(string controlName) => controlName switch
    {
        nameof(txtNormal) => "normal",
        nameof(txtReadOnly) => "readonly",
        nameof(pwdPassword) => "password",
        nameof(txtMultiline) => "multiline",
        nameof(btnSample) => "button",
        _ => "other",
    };
}
