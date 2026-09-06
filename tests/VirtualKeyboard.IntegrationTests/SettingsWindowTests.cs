using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.IntegrationTests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void WindowIsActivatingAndLoadsEveryConfigurationField()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var expected = new KeyboardConfiguration(1, false, true, false, 0.75, 900, 400, 12, "custom.layout", ManualPositionMode.Persistent, true);
            Assert.True(fixture.Repository.Save(expected).IsSaved);
            var window = new SettingsWindow(fixture.Repository);

            Assert.True(window.ShowActivated);
            Assert.True(Find<CheckBox>(window, "AutoShowCheckBox").IsChecked);
            Assert.Equal("900", Find<TextBox>(window, "WidthTextBox").Text);
            Assert.Equal("custom.layout", Find<TextBox>(window, "LayoutIdTextBox").Text);
            Assert.Equal(ManualPositionMode.Persistent, Find<ComboBox>(window, "PositionModeComboBox").SelectedItem);
            window.Close();
        });
    }

    [Fact]
    public void InvalidSettingsAreRejectedBeforeRepositorySave()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);
            Find<TextBox>(window, "OpacityTextBox").Text = "0.10";

            Find<Button>(window, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Contains("无效", Find<TextBlock>(window, "StatusText").Text);
            Assert.False(File.Exists(fixture.ConfigurationFile));
            window.Close();
        });
    }

    [Fact]
    public void SaveFailureKeepsEditedMemoryConfigurationAndShowsMessage()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            Directory.CreateDirectory(fixture.ConfigurationFile);
            var window = new SettingsWindow(fixture.Repository);
            Find<TextBox>(window, "WidthTextBox").Text = "901";

            Find<Button>(window, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(901, fixture.Repository.Current.KeyboardWidthDip);
            Assert.Contains("无法保存", Find<TextBlock>(window, "StatusText").Text);
            window.Close();
        });
    }

    [Fact]
    public void SettingsSessionUsesCoordinatorAndInvalidatesTarget()
    {
        RunOnStaThread(() =>
        {
            using var window = new MainWindow();
            Assert.True(window.BeginSettingsSession());
            Assert.Equal(TargetCoordinatorState.SettingsOpen, window.CoordinatorState);
            Assert.Null(window.CurrentTargetSession);
            Assert.False(window.BeginSettingsSession());
            Assert.True(window.EndSettingsSession());
            Assert.Equal(TargetCoordinatorState.Hidden, window.CoordinatorState);
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
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"VirtualKeyboard.SettingsTests.{Guid.NewGuid():N}");
        public Fixture() { ConfigurationFile = Path.Combine(_root, "config.json"); Repository = new(new(ConfigurationFile, Path.Combine(_root, "recovery"))); }
        public string ConfigurationFile { get; }
        public ConfigurationRepository Repository { get; }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
