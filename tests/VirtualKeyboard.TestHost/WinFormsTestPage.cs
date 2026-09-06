// T0.5b1: WinForms 控件测试页（NFR-COMP-001 / NFR-PRI-001）。
namespace VirtualKeyboard.TestHost;

/// <summary>仅供 TestHost 使用的 WinForms 控件页，不包含产品逻辑。</summary>
internal sealed class WinFormsTestPage : System.Windows.Forms.Form
{
    internal System.Windows.Forms.TextBox NormalTextBox { get; }
    internal System.Windows.Forms.TextBox ReadOnlyTextBox { get; }
    internal System.Windows.Forms.TextBox PasswordTextBox { get; }
    internal System.Windows.Forms.TextBox MultilineTextBox { get; }
    internal System.Windows.Forms.Button ActionButton { get; }
    internal System.Windows.Forms.Label NonFocusableLabel { get; }
    internal System.Windows.Forms.Label FocusDisplay { get; }
    internal System.Windows.Forms.Label KeyCountDisplay { get; }
    internal System.Windows.Forms.TableLayoutPanel LayoutPanel { get; }

    private readonly Dictionary<string, int> _keyCounts = new(StringComparer.Ordinal);

    internal int EnterEventCount { get; private set; }
    internal int LeaveEventCount { get; private set; }
    internal int TotalKeyCount => _keyCounts.Values.Sum();

    public WinFormsTestPage()
    {
        Name = "WfSelfTestForm";
        Text = "Virtual Keyboard TestHost - WinForms 控件页";
        ClientSize = new System.Drawing.Size(720, 520);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

        NormalTextBox = new() { Name = "wfNormal", Dock = System.Windows.Forms.DockStyle.Fill };
        ReadOnlyTextBox = new() { Name = "wfReadOnly", ReadOnly = true, Text = "只读内容", Dock = System.Windows.Forms.DockStyle.Fill };
        PasswordTextBox = new() { Name = "wfPassword", UseSystemPasswordChar = true, Dock = System.Windows.Forms.DockStyle.Fill };
        MultilineTextBox = new()
        {
            Name = "wfMultiline",
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = System.Windows.Forms.ScrollBars.Both,
            Dock = System.Windows.Forms.DockStyle.Fill,
        };
        ActionButton = new() { Name = "wfAction", Text = "测试按钮", Dock = System.Windows.Forms.DockStyle.Fill };
        NonFocusableLabel = new()
        {
            Name = "wfBlank",
            Text = "不可聚焦空白区",
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Dock = System.Windows.Forms.DockStyle.Fill,
            TabStop = false,
            Enabled = false,
        };
        FocusDisplay = new() { Name = "wfFocusDisplay", AutoSize = true, Text = "当前焦点：(none)" };
        KeyCountDisplay = new() { Name = "wfKeyCountDisplay", AutoSize = true, Text = "按键计数：normal=0 readonly=0 password=0 multiline=0 button=0 total=0" };

        LayoutPanel = new()
        {
            Name = "wfControlLayout",
            Dock = System.Windows.Forms.DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new System.Windows.Forms.Padding(8),
        };
        LayoutPanel.ColumnStyles.Add(new(System.Windows.Forms.SizeType.Percent, 50));
        LayoutPanel.ColumnStyles.Add(new(System.Windows.Forms.SizeType.Percent, 50));
        for (int i = 0; i < 3; i++)
        {
            LayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 33.333f));
        }
        LayoutPanel.Controls.Add(CreateZone("普通 TextBox", NormalTextBox), 0, 0);
        LayoutPanel.Controls.Add(CreateZone("只读 TextBox", ReadOnlyTextBox), 1, 0);
        LayoutPanel.Controls.Add(CreateZone("PasswordBox（仅计数）", PasswordTextBox), 0, 1);
        LayoutPanel.Controls.Add(CreateZone("多行编辑框", MultilineTextBox), 1, 1);
        LayoutPanel.Controls.Add(CreateZone("Button", ActionButton), 0, 2);
        LayoutPanel.Controls.Add(CreateZone("不可聚焦空白区", NonFocusableLabel), 1, 2);

        var status = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            AutoSize = true,
            FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
            WrapContents = false,
            Padding = new System.Windows.Forms.Padding(8),
        };
        status.Controls.Add(FocusDisplay);
        status.Controls.Add(KeyCountDisplay);
        Controls.Add(LayoutPanel);
        Controls.Add(status);
        Activated += OnPageActivated;
        Deactivate += OnPageDeactivated;

        foreach (var control in FocusableControls())
        {
            control.Enter += OnControlEnter;
            control.Leave += OnControlLeave;
            control.KeyDown += OnControlKeyDown;
        }
        UpdateFocusDisplay();
        UpdateKeyCountDisplay();
    }

    private static System.Windows.Forms.GroupBox CreateZone(string title, System.Windows.Forms.Control control)
    {
        var zone = new System.Windows.Forms.GroupBox { Text = title, Dock = System.Windows.Forms.DockStyle.Fill, Padding = new System.Windows.Forms.Padding(8) };
        zone.Controls.Add(control);
        return zone;
    }

    private IEnumerable<System.Windows.Forms.Control> FocusableControls()
    {
        yield return NormalTextBox;
        yield return ReadOnlyTextBox;
        yield return PasswordTextBox;
        yield return MultilineTextBox;
        yield return ActionButton;
    }

    private void OnControlEnter(object? sender, System.EventArgs e)
    {
        _ = e;
        EnterEventCount++;
        UpdateFocusDisplay();
    }

    private void OnControlLeave(object? sender, System.EventArgs e)
    {
        _ = e;
        LeaveEventCount++;
        UpdateFocusDisplay();
    }

    private void OnPageActivated(object? sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateFocusDisplay();
    }

    private void OnPageDeactivated(object? sender, System.EventArgs e)
    {
        _ = sender;
        _ = e;
        FocusDisplay.Text = "当前焦点：(none)";
    }

    private void OnControlKeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
    {
        _ = e;
        if (sender is System.Windows.Forms.Control control && ControlKey(control.Name) is { } key)
        {
            _keyCounts[key] = _keyCounts.GetValueOrDefault(key) + 1;
            UpdateKeyCountDisplay();
        }
    }

    internal int KeyCount(string key) => _keyCounts.GetValueOrDefault(key);

    internal void UpdateFocusDisplay()
    {
        var focused = ActiveControl;
        FocusDisplay.Text = focused is null ? "当前焦点：(none)" : $"当前焦点：{focused.Name} ({focused.GetType().Name})";
    }

    private void UpdateKeyCountDisplay()
    {
        KeyCountDisplay.Text = $"按键计数：normal={KeyCount("normal")} readonly={KeyCount("readonly")} password={KeyCount("password")} multiline={KeyCount("multiline")} button={KeyCount("button")} total={TotalKeyCount}";
    }

    private static string? ControlKey(string name) => name switch
    {
        "wfNormal" => "normal",
        "wfReadOnly" => "readonly",
        "wfPassword" => "password",
        "wfMultiline" => "multiline",
        "wfAction" => "button",
        _ => null,
    };
}
