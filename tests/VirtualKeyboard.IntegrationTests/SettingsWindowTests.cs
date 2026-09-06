using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.App;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.IntegrationTests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void WindowIsActivatingAndLoadsEveryConfigurationField()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var expected = new KeyboardConfiguration(1, false, true, false, 0.75, 900, 400, 12, "custom.layout", ManualPositionMode.Persistent, true, "邮箱", "user@example.com");
            Assert.True(fixture.Repository.Save(expected).IsSaved);
            var window = new SettingsWindow(fixture.Repository);

            Assert.True(window.ShowActivated);
            Assert.True(Find<CheckBox>(window, "AutoShowCheckBox").IsChecked);
            Assert.Equal("900", Find<TextBox>(window, "WidthTextBox").Text);
            Assert.Equal("custom.layout", Find<TextBox>(window, "LayoutIdTextBox").Text);
            Assert.Equal(ManualPositionMode.Persistent, Find<ComboBox>(window, "PositionModeComboBox").SelectedItem);
            Assert.Equal("邮箱", Find<TextBox>(window, "CustomKeyLabelTextBox").Text);
            Assert.Equal("user@example.com", Find<TextBox>(window, "CustomKeyTextBox").Text);
            window.Close();
        });
    }

    [Fact]
    public void CustomKeyAppearsAboveInvertedTArrowsAndResizePersistsDimensions()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Repository.Save(new(1, true, true, true, 0.9, 800, 300, 8,
                "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false, "邮箱", "user@example.com")).IsSaved);
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            var layout = Find<KeyboardLayoutView>(window, "LayoutView");
            var custom = Assert.IsType<NonFocusableKeyButton>(Assert.IsType<Grid>(layout.Children[2]).Children[^1]);
            var up = Assert.IsType<NonFocusableKeyButton>(Assert.IsType<Grid>(layout.Children[3]).Children[^1]);

            Assert.Equal("key.custom", custom.Key.Id);
            Assert.Equal(LayoutActionTypes.Text, custom.Key.Action.Type);
            Assert.Equal("key.up", up.Key.Id);
            Assert.False(custom.Key.SafeForPassword);

            window.ApplyCompletedResize(960, 420);
            Assert.Equal(960, fixture.Repository.Current.KeyboardWidthDip);
            Assert.Equal(420, fixture.Repository.Current.KeyboardHeightDip);
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
            using var fixture = new Fixture();
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            Assert.True(window.BeginSettingsSession());
            Assert.Equal(TargetCoordinatorState.SettingsOpen, window.CoordinatorState);
            Assert.Null(window.CurrentTargetSession);
            Assert.False(window.BeginSettingsSession());
            Assert.True(window.EndSettingsSession());
            Assert.Equal(TargetCoordinatorState.Hidden, window.CoordinatorState);
        });
    }

    [Fact]
    public void EvaluatedEditableFocusCreatesSessionPlacesOverlayAndNonEditableFocusClearsIt()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            var editable = new FocusSnapshot(1, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([1, 2]),
                FocusControlType.Edit, true, true, false, false);
            var editableEvaluation = new FocusTargetEvaluation(FocusTargetEvaluationStatus.Evaluated, editable,
                new(1, Editability.Editable, ClassificationReasonCode.ValuePattern, false), (nint)101,
                new PhysicalPixelRect(300, 300, 100, 30));

            Assert.True(window.ApplyEvaluatedFocusForTest(editableEvaluation));
            Assert.Equal(TargetCoordinatorState.VisibleTracking, window.CoordinatorState);
            Assert.NotNull(window.CurrentTargetSession);
            Assert.True(window.IsVisible);

            Find<Button>(window, "CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(TargetCoordinatorState.ManuallySuppressed, window.CoordinatorState);
            Assert.False(window.IsVisible);
            var repeated = editable with { Version = 2 };
            Assert.True(window.ApplyEvaluatedFocusForTest(editableEvaluation with
            {
                Snapshot = repeated,
                Classification = editableEvaluation.Classification with { Version = 2 }
            }));
            Assert.Equal(TargetCoordinatorState.ManuallySuppressed, window.CoordinatorState);
            Assert.False(window.IsVisible);

            var nextEdit = editable with { Version = 3, RuntimeId = new RuntimeIdentity([3]) };
            Assert.True(window.ApplyEvaluatedFocusForTest(editableEvaluation with
            {
                Snapshot = nextEdit,
                Classification = editableEvaluation.Classification with { Version = 3 }
            }));
            Assert.Equal(TargetCoordinatorState.VisibleTracking, window.CoordinatorState);
            Assert.True(window.IsVisible);

            var button = editable with { Version = 4, ControlType = FocusControlType.Button, RuntimeId = new RuntimeIdentity([4]) };
            var buttonEvaluation = new FocusTargetEvaluation(FocusTargetEvaluationStatus.Evaluated, button,
                new(4, Editability.NotEditable, ClassificationReasonCode.NoEditableEvidence, false), (nint)101, null);
            Assert.True(window.ApplyEvaluatedFocusForTest(buttonEvaluation));
            Assert.Equal(TargetCoordinatorState.Hidden, window.CoordinatorState);
            Assert.Null(window.CurrentTargetSession);
            Assert.False(window.IsVisible);
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

    private sealed class UnusedCapture : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => TargetCaptureResult.Failure(TargetCaptureStatus.NoForegroundWindow);
    }
}
