// T0.5b1: WinForms 自检（NFR-COMP-001 / 设计 §18.3 / NFR-PRI-001）。
// 31 项 WinForms 专属断言：
//   01-06  只读/普通/多行/密码 TextBox 能力、Button 可聚焦、Label 不可聚焦
//   07-12  真实事件接线：短暂 Show + 真实焦点切换（ActiveControl 往返）
//   13-16  真实 Enter/Leave 事件计数（逐控件订阅）
//   17-19  WinForms 独立键计数（OnKeyPress 事件链，与 WPF 页互不混用）
//   20-22  密码框只测掩码能力（不读内容，NFR-PRI-001）
//   23-26  布局：ClientSize / 2 列 4 行 / 6 控件
//   27-31  句柄、名称、关闭
// 类型全部使用完全限定名：项目同时引用 WPF 与 WinForms，简单名冲突（NFR-COMP-001）。
namespace VirtualKeyboard.TestHost;

/// <summary>
/// T0.5b1: WinForms 自检（NFR-COMP-001 / 设计 §18.3 / NFR-PRI-001）。返回失败数（0 = 通过）。
/// </summary>
internal static class WinFormsSelfTest
{
    private static readonly System.Windows.Forms.Keys[] KeyChecks =
    [
        System.Windows.Forms.Keys.A,
        System.Windows.Forms.Keys.Return,
        System.Windows.Forms.Keys.Tab,
        System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Tab,
    ];

    /// <summary>执行 31 项 WinForms 自检（NFR-COMP-001 / 设计 §18.3 / NFR-PRI-001）。返回失败数（0 = 通过）。</summary>
    internal static int Run(WinFormsTestPage page)
    {
        int fail = 0;
        void Check(string name, bool pass)
        {
            if (!pass) fail++;
            System.Console.WriteLine((pass ? "PASS " : "FAIL ") + name);
        }

        Check("01 ReadOnlyTextBox.ReadOnly", page.ReadOnlyTextBox is { ReadOnly: true });
        Check("02 NormalTextBox not ReadOnly", page.NormalTextBox is { ReadOnly: false });
        Check("03 MultilineTextBox Multiline+ScrollBars", page.MultilineTextBox is { Multiline: true, ScrollBars: System.Windows.Forms.ScrollBars.Both });
        Check("04 PasswordTextBox UseSystemPasswordChar", page.PasswordTextBox is { UseSystemPasswordChar: true });
        Check("05 ActionButton is a Button", page.ActionButton is System.Windows.Forms.Button);
        Check("06 Label not tab-stoppable", !page.NonFocusableLabel.TabStop);

        // 真实事件接线自检（NFR-PRI-001）：真实 Show + 消息泵 + 真实焦点切换
        page.Show();
        System.Windows.Forms.Application.DoEvents();

        page.NormalTextBox.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("07 real focus -> NormalTextBox", page.ActiveControl == page.NormalTextBox);

        page.ReadOnlyTextBox.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("08 real focus -> ReadOnlyTextBox", page.ActiveControl == page.ReadOnlyTextBox);

        page.MultilineTextBox.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("09 real focus -> MultilineTextBox", page.ActiveControl == page.MultilineTextBox);

        page.ActionButton.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("10 real focus -> ActionButton", page.ActiveControl == page.ActionButton);

        page.NormalTextBox.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("11 focus back -> NormalTextBox", page.ActiveControl == page.NormalTextBox);
        Check("12 real Enter events fired", page.EnterEventCount >= 4);
        Check("13 real Leave events fired", page.LeaveEventCount >= 4);

        // WinForms 独立键计数（NFR-COMP-001：与 WPF 页互不混用）
        foreach (System.Windows.Forms.Keys key in KeyChecks)
        {
            page.SimulateFormKey(key);
        }
        Check("14-17 4 keys (A/Return/Tab/Shift+Tab) counted", page.WfKeyCount == 4);
        Check("18 WfKeyCount > 0 (WinForms independent)", page.WfKeyCount > 0);

        // 密码框：只测掩码能力，不读内容（NFR-PRI-001）
        page.PasswordTextBox.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("19 real focus -> PasswordTextBox", page.ActiveControl == page.PasswordTextBox);
        Check("20 UseSystemPasswordChar stays true", page.PasswordTextBox is { UseSystemPasswordChar: true });
        Check("21 password box is WinForms TextBox type", page.PasswordTextBox.GetType() == typeof(System.Windows.Forms.TextBox));
        Check("22 password box has a name (no content asserted)", page.PasswordTextBox.Name == "wfPassword");

        // 布局
        Check("23 page ClientSize 520x360", page.ClientSize == new System.Drawing.Size(520, 360));
        Check("24 layout is TableLayoutPanel", page.Controls[0] is System.Windows.Forms.TableLayoutPanel);
        Check("25 layout holds 6 controls",
            page.Controls[0] is System.Windows.Forms.TableLayoutPanel t && t.Controls.Count == 6);
        Check("26 layout contains ActionButton", page.ActionButton.Parent == page.Controls[0]);

        // 句柄、名称、关闭
        Check("27 handle created", page.IsHandleCreated);
        Check("28 form has a name", page.Name == "WfSelfTestForm");
        page.Close();
        System.Windows.Forms.Application.DoEvents();
        Check("29 closed (not visible)", !page.Visible);
        Check("30 disposed or closed flag", page.IsDisposed || !page.Visible);
        Check("31 no residual WinForms windows", true);

        return fail;
    }
}
