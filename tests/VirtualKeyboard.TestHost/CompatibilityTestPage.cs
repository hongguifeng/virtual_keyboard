using Forms = System.Windows.Forms;
using System.Runtime.InteropServices;

namespace VirtualKeyboard.TestHost;

/// <summary>Focused, synthetic input fixtures for real UIA/worker compatibility tests.</summary>
internal sealed class CompatibilityTestPage : Forms.Form
{
    private Forms.Control? _target;

    public CompatibilityTestPage(string scenario)
    {
        Text = "Virtual Keyboard - Input compatibility";
        ClientSize = new(500, 180);
        StartPosition = Forms.FormStartPosition.CenterScreen;
        SetScenario(scenario);
        Shown += (_, _) => FocusFixture();
    }

    internal void SetScenario(string scenario)
    {
        foreach (Forms.Control control in Controls.Cast<Forms.Control>().ToArray()) control.Dispose();
        Controls.Clear();
        _target = scenario switch
        {
            "numeric" => new Forms.NumericUpDown { Minimum = 0, Maximum = 100, Value = 42 },
            "numeric-readonly" => new Forms.NumericUpDown { ReadOnly = true, Value = 42 },
            "masked" => new Forms.MaskedTextBox { Mask = "0000-00-00" },
            "combo-edit" => Combo(Forms.ComboBoxStyle.DropDown),
            "combo-select" => Combo(Forms.ComboBoxStyle.DropDownList),
            "richtext" => new Forms.RichTextBox(),
            "richtext-readonly" => new Forms.RichTextBox { ReadOnly = true, Text = "Read-only fixture" },
            "slider" => new Forms.TrackBar(),
            "list" => List(),
            _ => throw new ArgumentException("Unknown compatibility fixture.", nameof(scenario)),
        };
        _target.Name = "CompatibilityInput";
        _target.SetBounds(20, 50, 460, 100);
        Controls.Add(_target);
        Controls.Add(new Forms.Label { Text = scenario, Left = 20, Top = 15, AutoSize = true });
        if (Visible) FocusFixture();
    }

    // TestHost only: establish the test precondition even under desktop/window managers.
    // Production overlay and input paths never attach input threads or activate a target.
    private void FocusFixture()
    {
        uint current = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attach = foreground != 0 && foreground != current;
        if (attach && !AttachThreadInput(current, foreground, true))
            throw new InvalidOperationException("Could not establish fixture focus.");
        try { Activate(); _target!.Focus(); }
        finally { if (attach) AttachThreadInput(current, foreground, false); }
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint from, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);

    private static Forms.ComboBox Combo(Forms.ComboBoxStyle style)
    {
        var combo = new Forms.ComboBox { DropDownStyle = style };
        combo.Items.AddRange(["First", "Second"]);
        combo.SelectedIndex = 0;
        return combo;
    }

    private static Forms.ListBox List()
    {
        var list = new Forms.ListBox();
        list.Items.AddRange(["First", "Second"]);
        list.SelectedIndex = 0;
        return list;
    }
}
