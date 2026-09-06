using System.Drawing;
using System.Reflection;
using VirtualKeyboard.Core.Configuration;
using Forms = System.Windows.Forms;

namespace VirtualKeyboard.App;

internal interface ITrayCommands
{
    bool IsEnabled { get; }
    UiLanguage UiLanguage { get; }
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
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly Forms.ToolStripMenuItem _settingsItem;
    private readonly Forms.ToolStripMenuItem _reloadItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private bool _disposed;

    public TrayIconController(ITrayCommands commands, bool visible = true)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _enabledItem = new Forms.ToolStripMenuItem(string.Empty, null, OnToggleEnabled) { CheckOnClick = false };
        _showItem = new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => _commands.ShowCurrentKeyboard());
        _settingsItem = new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => { _commands.OpenSettings(); RefreshState(); });
        _reloadItem = new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => _commands.ReloadLayouts());
        _exitItem = new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => _commands.Exit());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_showItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_reloadItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Virtual Keyboard",
            Icon = LoadAppIcon(),
            ContextMenuStrip = menu,
            Visible = visible,
        };
        _notifyIcon.DoubleClick += OnShowKeyboard;
        RefreshState();
    }

    // The app icon is embedded as VirtualKeyboard.App.Assets.app.ico; the fallback keeps the tray
    // functional if the resource is ever removed from the build.
    private static Icon LoadAppIcon()
    {
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VirtualKeyboard.App.Assets.app.ico");
            if (stream is not null) { using (stream) { return new Icon(stream); } }
        }
        catch (Exception)
        {
            // Fall through to the system icon below.
        }

        return SystemIcons.Application;
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
        AppStrings strings = AppStrings.For(_commands.UiLanguage);
        _enabledItem.Text = _commands.IsEnabled ? strings.PauseKeyboard : strings.EnableKeyboard;
        _showItem.Text = strings.ShowKeyboard;
        _settingsItem.Text = strings.Settings;
        _reloadItem.Text = strings.ReloadLayouts;
        _exitItem.Text = strings.Exit;
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
