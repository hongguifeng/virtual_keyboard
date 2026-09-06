using VirtualKeyboard.App;

namespace VirtualKeyboard.IntegrationTests;

public sealed class TrayIconControllerTests
{
    [Fact]
    public void MenuContainsRequiredCommandsAndTracksEnabledState()
    {
        var commands = new StubCommands { IsEnabled = true };
        using var tray = new TrayIconController(commands, visible: false);

        Assert.False(tray.IsVisible);
        Assert.Equal(["暂停键盘", "显示当前键盘", "设置", "重新加载布局", "", "退出"], tray.MenuLabels);

        Assert.True(tray.IsEnabledChecked);
        tray.InvokeMenuItem(0);
        Assert.False(commands.IsEnabled);
        Assert.Equal("启用键盘", tray.MenuLabels[0]);
    }

    [Fact]
    public void EachMenuItemDispatchesExactlyOneCommand()
    {
        var commands = new StubCommands();
        using var tray = new TrayIconController(commands, visible: false);

        for (int index = 1; index < tray.MenuLabels.Count; index++) tray.InvokeMenuItem(index);

        Assert.Equal(1, commands.ShowCount);
        Assert.Equal(1, commands.SettingsCount);
        Assert.Equal(1, commands.ReloadCount);
        Assert.Equal(1, commands.ExitCount);
    }

    private sealed class StubCommands : ITrayCommands
    {
        public bool IsEnabled { get; set; }
        public int ShowCount { get; private set; }
        public int SettingsCount { get; private set; }
        public int ReloadCount { get; private set; }
        public int ExitCount { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
        public void ShowCurrentKeyboard() => ShowCount++;
        public void OpenSettings() => SettingsCount++;
        public void ReloadLayouts() => ReloadCount++;
        public void Exit() => ExitCount++;
    }
}
