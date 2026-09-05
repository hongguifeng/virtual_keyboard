// T0.5b1: WinForms 控件页（NFR-COMP-001 / 设计 §18.3）。
// 注意：本文件只使用 WinForms 类型，且一律使用完全限定名（项目同时引用 WPF，
// 简单名 TextBox/Button/Control 等在两个框架间冲突，别名/全限定是 NFR-COMP-001 的既定解法）。
// 页面对应 Win32 页的 6 个目标 + 一个独立 WinForms 容器：
//   1. 只读 TextBox（ReadOnly）
//   2. 普通可编辑 TextBox
//   3. 多行 TextBox（Multiline + ScrollBars）
//   4. 密码 TextBox（UseSystemPasswordChar，只测能力不测内容）
//   5. 可点击 Button（Click 计数）
//   6. 不可聚焦的 Label
// 键与 Enter/Leave 焦点事件被计数，供 WinFormsSelfTest 断言（NFR-COMP-001 / NFR-PRI-001）。
namespace VirtualKeyboard.TestHost;

/// <summary>
/// T0.5b1: WinForms 控件页（NFR-COMP-001 / 设计 §18.3 / NFR-PRI-001）。
/// </summary>
internal sealed class WinFormsTestPage : System.Windows.Forms.Form
{
    /// <summary>只读 TextBox（ReadOnly）。</summary>
    internal System.Windows.Forms.TextBox ReadOnlyTextBox { get; } = new() { Name = "wfReadOnly", ReadOnly = true };

    /// <summary>普通可编辑 TextBox。</summary>
    internal System.Windows.Forms.TextBox NormalTextBox { get; } = new() { Name = "wfNormal", Text = "wf editable seed" };

    /// <summary>多行 TextBox（Multiline + ScrollBars）。</summary>
    internal System.Windows.Forms.TextBox MultilineTextBox { get; } = new()
    {
        Name = "wfMultiline",
        Multiline = true,
        ScrollBars = System.Windows.Forms.ScrollBars.Both,
        AcceptsReturn = true,
    };

    /// <summary>密码 TextBox（UseSystemPasswordChar；只测能力，不记录/断言内容，NFR-PRI-001）。</summary>
    internal System.Windows.Forms.TextBox PasswordTextBox { get; } = new()
    {
        Name = "wfPassword",
        UseSystemPasswordChar = true,
        Text = "wf password seed",
    };

    /// <summary>可点击 Button。</summary>
    internal System.Windows.Forms.Button ActionButton { get; } = new() { Name = "wfAction", Text = "wf action" };

    /// <summary>不可聚焦的 Label。</summary>
    internal System.Windows.Forms.Label NonFocusableLabel { get; } = new() { Name = "wfLabel", Text = "wf label" };

    /// <summary>Button.Click 事件计数。</summary>
    internal int ClickCount { get; private set; }

    /// <summary>WinForms 键计数（NFR-COMP-001：WinForms 路径独立计数，不与 WPF 页混用）。</summary>
    internal int WfKeyCount { get; private set; }

    /// <summary>
    /// 模拟一次表单级按键（仅自检用，NFR-PRI-001：走本类型真实的 OnKeyPress 虚方法链）。
    /// </summary>
    internal void SimulateFormKey(System.Windows.Forms.Keys key)
    {
        _ = key;
        OnKeyPress(new System.Windows.Forms.KeyPressEventArgs('k'));
    }

    /// <summary>Enter 焦点事件计数（逐控件订阅，不用 Form.ControlEnter——该 API 不存在）。</summary>
    internal int EnterEventCount { get; private set; }

    /// <summary>Leave 焦点事件计数（同上）。</summary>
    internal int LeaveEventCount { get; private set; }

    public WinFormsTestPage()
    {
        Name = "WfSelfTestForm";
        ClientSize = new System.Drawing.Size(520, 360);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Text = "VirtualKeyboard TestHost - WinForms Page";

        var layout = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            ColumnCount = 2,
            Padding = new System.Windows.Forms.Padding(8),
        };
        layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50f));
        layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48));
        layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48));
        layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80));
        layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));

        layout.Controls.Add(ReadOnlyTextBox);
        layout.Controls.Add(NormalTextBox);
        layout.Controls.Add(MultilineTextBox);
        layout.Controls.Add(PasswordTextBox);
        layout.Controls.Add(ActionButton);
        layout.Controls.Add(NonFocusableLabel);
        Controls.Add(layout);

        ActionButton.Click += OnActionButtonClick;
        WireEnterLeaveEvents();
    }

    /// <summary>逐控件订阅 Enter/Leave（NFR-COMP-001：不用 Form 级 ControlEnter/ControlLeave）。</summary>
    private void WireEnterLeaveEvents()
    {
        ReadOnlyTextBox.Enter += OnControlEnter;
        ReadOnlyTextBox.Leave += OnControlLeave;
        NormalTextBox.Enter += OnControlEnter;
        NormalTextBox.Leave += OnControlLeave;
        MultilineTextBox.Enter += OnControlEnter;
        MultilineTextBox.Leave += OnControlLeave;
        PasswordTextBox.Enter += OnControlEnter;
        PasswordTextBox.Leave += OnControlLeave;
        ActionButton.Enter += OnControlEnter;
        ActionButton.Leave += OnControlLeave;
    }

    private void OnActionButtonClick(object? sender, System.EventArgs e) => ClickCount++;

    private void OnControlEnter(object? sender, System.EventArgs e) => EnterEventCount++;

    private void OnControlLeave(object? sender, System.EventArgs e) => LeaveEventCount++;

    /// <summary>
    /// WinForms 路径的键计数（NFR-COMP-001：WPF PreviewKeyDown 与 WinForms 按键计数互不混用）。
    /// </summary>
    protected override void OnKeyPress(System.Windows.Forms.KeyPressEventArgs e)
    {
        WfKeyCount++;
        base.OnKeyPress(e);
    }
}
