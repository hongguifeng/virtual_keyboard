using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.AutoStart;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;
using Xunit;

namespace VirtualKeyboard.IntegrationTests;

public sealed class AutoStartTests
{
    private sealed class FakeAutoStartManager : IAutoStartManager
    {
        public bool RegistryEnabled;
        public bool ReadFails;
        public bool WriteFails;

        public bool TryGetEnabled(out bool enabled)
        {
            if (ReadFails) { enabled = false; return false; }
            enabled = RegistryEnabled;
            return true;
        }

        public bool TrySetEnabled(bool enabled, out int errorCode)
        {
            if (WriteFails) { errorCode = 2; return false; }
            RegistryEnabled = enabled;
            errorCode = 0;
            return true;
        }
    }

    [Fact]
    public void SettingsWindowAutoStartCheckboxFeedsReadConfiguration()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var window = new SettingsWindow(fixture.Repository);
            var checkBox = Find<CheckBox>(window, "AutoStartCheckBox");

            Assert.False(checkBox.IsChecked); // 首次/默认关闭（FR-APP-004）
            checkBox.IsChecked = true;
            Assert.True(window.ReadConfiguration().AutoStart);
            checkBox.IsChecked = false;
            Assert.False(window.ReadConfiguration().AutoStart);
            // 配置持久化由 Core 往返测试覆盖（autoStart 往返 + 旧版文件默认 false）
        });
    }

    [Fact]
    public void SettingsWindowLoadsAutoStartFromConfiguration()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var saved = fixture.Repository.Current;
            Assert.True(fixture.Repository.Save(new(
                saved.SchemaVersion, saved.Enabled, saved.AutoShow, saved.AutoHide, saved.Opacity,
                saved.KeyboardWidthDip, saved.KeyboardHeightDip, saved.MarginDip, saved.LayoutId,
                saved.ManualPositionMode, saved.DetailedDiagnostics, saved.CustomKeys, saved.UiLanguage,
                autoStart: true)).IsSaved);
            using var window = new SettingsWindow(fixture.Repository);

            Assert.True(Find<CheckBox>(window, "AutoStartCheckBox").IsChecked);
        });
    }

    [Fact]
    public void SettingsWindowRevertsAutoStartWhenApplyToRegistryFails()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var fake = new FakeAutoStartManager { WriteFails = true };
            using var window = new SettingsWindow(fixture.Repository, fake);
            var checkBox = Find<CheckBox>(window, "AutoStartCheckBox");

            checkBox.IsChecked = true;
            Find<CheckBox>(window, "ShowLauncherButtonCheckBox").IsChecked = true;
            Find<ComboBox>(window, "CustomKeyLocationComboBox").SelectedIndex = 1;
            Find<Button>(window, "AddCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>(window, "CustomTextTextBox").Text = "launcher-only";
            Find<Button>(window, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // 注册表写入失败：回滚配置镜像 + 取消勾选 + 显示受控警告（保持 镜像=注册表 不变量）
            Assert.False(fixture.Repository.Current.AutoStart);
            Assert.True(fixture.Repository.Current.ShowLauncherButton);
            Assert.Equal("launcher-only", Assert.Single(fixture.Repository.Current.LauncherCustomKeys).Input);
            Assert.False(checkBox.IsChecked);
            Assert.Equal("Could not apply the auto-start setting", Find<TextBlock>(window, "StatusText").Text);
        });
    }

    [Fact]
    public void MainWindowSyncsConfigurationMirrorFromRegistryOnStartup()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var fake = new FakeAutoStartManager { RegistryEnabled = true };
            Assert.True(fixture.Repository.Save(new(1, true, true, true, 0.9, 800, 300, 8,
                "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false, showLauncherButton: true,
                launcherCustomKeys: [new("Enter", "key", "Enter")])).IsSaved);
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository, fake);

            // 配置默认关闭，注册表已启用（外部启用）→ 启动时把镜像更新为注册表事实
            Assert.True(fixture.Repository.Current.AutoStart);
            Assert.True(fixture.Repository.Current.ShowLauncherButton);
            Assert.Equal("Enter", Assert.Single(fixture.Repository.Current.LauncherCustomKeys).Input);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(fixture.ConfigurationFile));
            Assert.True(json.RootElement.GetProperty("autoStart").GetBoolean());
        });
    }

    [Fact]
    public void MainWindowRevertsConfigurationMirrorWhenRegistryWasDisabledExternally()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var saved = fixture.Repository.Current;
            Assert.True(fixture.Repository.Save(new(
                saved.SchemaVersion, saved.Enabled, saved.AutoShow, saved.AutoHide, saved.Opacity,
                saved.KeyboardWidthDip, saved.KeyboardHeightDip, saved.MarginDip, saved.LayoutId,
                saved.ManualPositionMode, saved.DetailedDiagnostics, saved.CustomKeys, saved.UiLanguage,
                autoStart: true)).IsSaved);
            var fake = new FakeAutoStartManager { RegistryEnabled = false }; // 用户在任务管理器删除了自启项
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository, fake);

            Assert.False(fixture.Repository.Current.AutoStart);
        });
    }

    [Fact]
    public void MainWindowKeepsConfigurationWhenRegistryReadFails()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var fake = new FakeAutoStartManager { ReadFails = true };
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository, fake);

            // 读取失败时保持现有配置（默认关闭），绝不自动启用
            Assert.False(fixture.Repository.Current.AutoStart);
        });
    }

    private static T Find<T>(FrameworkElement root, string name) where T : class => Assert.IsType<T>(root.FindName(name));

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.AutoStartTests.{Guid.NewGuid():N}");
        public Fixture() { ConfigurationFile = Path.Combine(_root, "config.json"); Repository = new(new(ConfigurationFile, Path.Combine(_root, "recovery"))); }
        public string ConfigurationFile { get; }
        public ConfigurationRepository Repository { get; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class UnusedCapture : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => TargetCaptureResult.Failure(TargetCaptureStatus.NoForegroundWindow);
    }
}
