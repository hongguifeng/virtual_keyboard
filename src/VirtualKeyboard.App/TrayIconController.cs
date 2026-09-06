using System.Drawing;
using Forms = System.Windows.Forms;

namespace VirtualKeyboard.App;

internal interface ITrayCommands
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
    void ShowCurrentKeyboard();
    void OpenSettings();
    void ReloadLayouts();
    void Exit();
}

internal sealed class TrayIconController : IDisposable
{
    private readonly ITrayCommands _commands;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _enabledItem;
    private bool _disposed;

    public TrayIconController(ITrayCommands commands, bool visible = true)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _enabledItem = new Forms.ToolStripMenuItem("启用键盘", null, OnToggleEnabled) { CheckOnClick = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("显示当前键盘", null, (_, _) => _commands.ShowCurrentKeyboard()));
        menu.Items.Add(new Forms.ToolStripMenuItem("设置", null, (_, _) => { _commands.OpenSettings(); RefreshState(); }));
        menu.Items.Add(new Forms.ToolStripMenuItem("重新加载布局", null, (_, _) => _commands.ReloadLayouts()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("退出", null, (_, _) => _commands.Exit()));
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Virtual Keyboard",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = visible,
        };
        _notifyIcon.DoubleClick += OnShowKeyboard;
        RefreshState();
    }

    internal Forms.ContextMenuStrip Menu => _notifyIcon.ContextMenuStrip!;
    internal bool IsVisible => _notifyIcon.Visible;
    internal IReadOnlyList<string> MenuLabels => Menu.Items.Cast<Forms.ToolStripItem>().Select(item => item.Text ?? string.Empty).ToArray();
    internal bool IsEnabledChecked => _enabledItem.Checked;
    internal void InvokeMenuItem(int index)
    {
        if (Menu.Items[index] is Forms.ToolStripMenuItem item) item.PerformClick();
    }

    public void RefreshState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _enabledItem.Checked = _commands.IsEnabled;
        _enabledItem.Text = _commands.IsEnabled ? "暂停键盘" : "启用键盘";
    }

    private void OnToggleEnabled(object? sender, EventArgs e)
    {
        _ = sender; _ = e;
        _commands.SetEnabled(!_commands.IsEnabled);
        RefreshState();
    }

    private void OnShowKeyboard(object? sender, EventArgs e) { _ = sender; _ = e; _commands.ShowCurrentKeyboard(); }

    public void Dispose()
    {
        if (_disposed) return;
        _notifyIcon.DoubleClick -= OnShowKeyboard;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
