// T0.5b1: WinForms 自检（NFR-COMP-001 / NFR-PRI-001）。
namespace VirtualKeyboard.TestHost;

/// <summary>通过真实控件焦点和 WM_KEYDOWN 消息验证 WinForms 测试页。</summary>
internal static class WinFormsSelfTest
{
    internal static int Run(WinFormsTestPage page)
    {
        int checks = 0;
        int failures = 0;
        void Check(string name, bool pass)
        {
            checks++;
            if (!pass) failures++;
            Console.WriteLine((pass ? "PASS " : "FAIL ") + name);
        }

        Check("01 normal TextBox", page.NormalTextBox is { ReadOnly: false });
        Check("02 read-only TextBox", page.ReadOnlyTextBox is { ReadOnly: true });
        Check("03 password TextBox masked", page.PasswordTextBox is { UseSystemPasswordChar: true });
        Check("04 multiline TextBox", page.MultilineTextBox is { Multiline: true, AcceptsReturn: true });
        Check("05 Button", page.ActionButton is System.Windows.Forms.Button);
        Check("06 blank Label disabled and not tab-stoppable", !page.NonFocusableLabel.Enabled && !page.NonFocusableLabel.TabStop);

        page.Show();
        System.Windows.Forms.Application.DoEvents();
        Check("07 form handle created", page.IsHandleCreated);

        System.Windows.Forms.Control[] focusables =
        {
            page.NormalTextBox,
            page.ReadOnlyTextBox,
            page.PasswordTextBox,
            page.MultilineTextBox,
            page.ActionButton,
        };
        foreach (var control in focusables)
        {
            bool focused = control.Focus();
            System.Windows.Forms.Application.DoEvents();
            Check($"focus {control.Name} accepted", focused && page.ActiveControl == control);
            Check($"focus display {control.Name}", page.FocusDisplay.Text.Contains(control.Name, StringComparison.Ordinal));
        }

        Check("blank Label rejects focus", !page.NonFocusableLabel.Focus() && page.ActiveControl != page.NonFocusableLabel);
        Check("Enter events observed", page.EnterEventCount >= focusables.Length);
        Check("Leave events observed", page.LeaveEventCount >= focusables.Length - 1);

        for (int i = 0; i < focusables.Length; i++)
        {
            var control = focusables[i];
            control.Focus();
            System.Windows.Forms.Application.DoEvents();
            Win32Test.SendKeyDown(control.Handle, (System.Windows.Forms.Keys)((int)System.Windows.Forms.Keys.A + i));
            System.Windows.Forms.Application.DoEvents();
            string key = i switch { 0 => "normal", 1 => "readonly", 2 => "password", 3 => "multiline", _ => "button" };
            Check($"real WM_KEYDOWN counted for {key}", page.KeyCount(key) == 1);
        }

        Check("total key count", page.TotalKeyCount == focusables.Length);
        Check("key count display updated", page.KeyCountDisplay.Text.Contains("normal=1", StringComparison.Ordinal)
            && page.KeyCountDisplay.Text.Contains("button=1", StringComparison.Ordinal)
            && page.KeyCountDisplay.Text.Contains("total=5", StringComparison.Ordinal));
        page.PasswordTextBox.Focus();
        System.Windows.Forms.Application.DoEvents();
        Check("password focus display", page.FocusDisplay.Text.Contains("wfPassword", StringComparison.Ordinal));
        Check("layout has six zones", page.LayoutPanel.Controls.Count == 6);
        Check("layout has two columns", page.LayoutPanel.ColumnCount == 2);
        Check("layout has three rows", page.LayoutPanel.RowCount == 3);
        Check("page has expected name", page.Name == "WfSelfTestForm");
        Check("page has visible key display", page.KeyCountDisplay.Visible);
        Check("page has visible focus display", page.FocusDisplay.Visible);

        page.Close();
        System.Windows.Forms.Application.DoEvents();
        Check("form closed", !page.Visible);
        Check("closed form is not an open form", !System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Any(form => ReferenceEquals(form, page)));
        Console.WriteLine($"=== WinForms 自检：{checks} 项，失败 {failures} 项 ===");
        return failures;
    }
}
