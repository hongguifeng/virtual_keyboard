using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            var expected = new KeyboardConfiguration(1, false, true, false, 0.75, 900, 400, 12, "custom.layout", ManualPositionMode.Persistent, true,
                [new("邮箱", "text", "user@example.com"), new("保存", "hotkey", "S", ["Control"])]);
            Assert.True(fixture.Repository.Save(expected).IsSaved);
            var window = new SettingsWindow(fixture.Repository);

            Assert.True(window.ShowActivated);
            Assert.True(Find<CheckBox>(window, "AutoShowCheckBox").IsChecked);
            Assert.Equal("900", Find<TextBox>(window, "WidthTextBox").Text);
            Assert.Equal(0.25, Find<Slider>(window, "OpacitySlider").Value, precision: 12);
            Assert.Equal("custom.layout", Find<TextBox>(window, "LayoutIdTextBox").Text);
            Assert.Equal(ManualPositionMode.Persistent, Find<ComboBox>(window, "PositionModeComboBox").SelectedItem);
            var list = Find<ListBox>(window, "CustomKeysList");
            Assert.Equal(2, list.Items.Count);
            Assert.Equal("邮箱", Assert.IsType<CustomKeyEditorItem>(list.Items[0]).Label);
            Assert.Equal(["Control"], Assert.IsType<CustomKeyEditorItem>(list.Items[1]).Modifiers);
            window.Close();
        });
    }

    [Fact]
    public void TransparencySliderStoresInverseWholeWindowOpacity()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);
            Slider slider = Find<Slider>(window, "OpacitySlider");

            slider.Value = 0;
            Assert.Equal(1, window.ReadConfiguration().Opacity, precision: 12);
            slider.Value = 0.70;
            Assert.Equal(0.30, window.ReadConfiguration().Opacity, precision: 12);

            window.Close();
        });
    }

    [Fact]
    public void MultipleCustomActionsAppearInSeparateRightColumnAndResizePersistsDimensions()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Repository.Save(new(1, true, true, true, 0.9, 800, 300, 8,
                "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false,
                Enumerable.Range(1, 7).Select(index => new CustomKeyConfiguration($"自定义 {index}", "key", "Enter")))).IsSaved);
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            var layout = Find<KeyboardLayoutView>(window, "LayoutView");
            var customColumn = Find<CustomKeyColumnView>(window, "CustomKeysView");
            var first = Assert.IsType<NonFocusableKeyButton>(customColumn.Children[0]);
            var sixth = Assert.IsType<NonFocusableKeyButton>(customColumn.Children[5]);
            var fourthRow = Assert.IsType<Grid>(layout.Children[3]);
            var up = Assert.IsType<NonFocusableKeyButton>(fourthRow.Children[^2]);
            var fifthRow = Assert.IsType<Grid>(layout.Children[4]);
            var down = Assert.IsType<NonFocusableKeyButton>(fifthRow.Children[^2]);

            Assert.Equal("key.custom.0", first.Key.Id);
            Assert.Equal(LayoutActionTypes.Key, first.Key.Action.Type);
            Assert.Equal(2, customColumn.ColumnDefinitions.Count);
            Assert.Equal(0, Grid.GetRow(first));
            Assert.Equal(0, Grid.GetColumn(first));
            Assert.Equal(0, Grid.GetRow(sixth));
            Assert.Equal(1, Grid.GetColumn(sixth));
            Assert.Equal("key.up", up.Key.Id);
            Assert.False(first.Key.SafeForPassword);
            window.ShowAt(-12000, -11000, 800, 300);
            window.UpdateLayout();
            double upCenter = up.TranslatePoint(new Point(up.ActualWidth / 2, 0), layout).X;
            double downCenter = down.TranslatePoint(new Point(down.ActualWidth / 2, 0), layout).X;
            Assert.InRange(Math.Abs(upCenter - downCenter), 0, 0.5);

            var password = new FocusSnapshot(1, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([9]),
                FocusControlType.Edit, true, true, false, true);
            var passwordEvaluation = new FocusTargetEvaluation(FocusTargetEvaluationStatus.Evaluated, password,
                new(1, Editability.Editable, ClassificationReasonCode.PasswordEdit, false), (nint)101,
                new PhysicalPixelRect(300, 300, 100, 30));
            Assert.True(window.ApplyEvaluatedFocusForTest(passwordEvaluation));
            Assert.Equal(Visibility.Collapsed, customColumn.Visibility);
            Assert.Empty(customColumn.Children);

            window.ApplyCompletedResize(960, 420);
            Assert.Equal(960, fixture.Repository.Current.KeyboardWidthDip);
            Assert.Equal(420, fixture.Repository.Current.KeyboardHeightDip);
        });
    }

    [Fact]
    public void CustomColumnsShrinkWithWindowWithoutOverlappingStandardKeyboard()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Repository.Save(new(1, true, true, true, 0.65, 1000, 300, 8,
                "builtin.qwerty.en-US", ManualPositionMode.UntilTargetChanges, false,
                Enumerable.Range(1, 12).Select(index => new CustomKeyConfiguration($"自定义 {index}", "key", "Enter")))).IsSaved);
            using var window = new MainWindow(new UnusedCapture(), new TargetSessionStore(), fixture.Repository);
            var body = Find<Grid>(window, "KeyboardBodyGrid");
            var layout = Find<KeyboardLayoutView>(window, "LayoutView");
            var custom = Find<CustomKeyColumnView>(window, "CustomKeysView");

            window.ShowAt(-12000, -11000, 1000, 300);
            window.UpdateLayout();
            double wideLayout = layout.ActualWidth;
            double wideCustom = custom.ActualWidth;

            window.ShowAt(-12000, -11000, 620, 300);
            window.UpdateLayout();

            Assert.True(layout.ActualWidth < wideLayout);
            Assert.True(custom.ActualWidth < wideCustom);
            Assert.Equal(3, custom.ColumnCount);
            Assert.All(custom.ColumnDefinitions,
                column => Assert.InRange(Math.Abs(column.ActualWidth - custom.ColumnDefinitions[0].ActualWidth), 0, 0.5));
            Point layoutRight = layout.TranslatePoint(new Point(layout.ActualWidth, 0), body);
            Point customLeft = custom.TranslatePoint(new Point(0, 0), body);
            Point customRight = custom.TranslatePoint(new Point(custom.ActualWidth, 0), body);
            Assert.True(layoutRight.X <= customLeft.X);
            Assert.True(customRight.X <= body.ActualWidth + 0.5);
            Assert.Equal(0.65, window.Opacity, precision: 12);
        });
    }

    [Fact]
    public void InvalidSettingsAreRejectedBeforeRepositorySave()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);
            Find<TextBox>(window, "WidthTextBox").Text = "100";

            Find<Button>(window, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Contains("无效", Find<TextBlock>(window, "StatusText").Text);
            Assert.False(File.Exists(fixture.ConfigurationFile));
            window.Close();
        });
    }

    [Fact]
    public void ShortcutIsRecordedFromPhysicalKeyCombination()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);
            Find<Button>(window, "AddCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>(window, "CustomKeyLabelTextBox").Text = "保存";
            Find<ComboBox>(window, "CustomActionModeComboBox").SelectedIndex = 1;
            Find<Button>(window, "RecordShortcutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(window.ApplyRecordedChordForTest(
                WindowsKeyboardKey.LeftWindows,
                WindowsKeyboardKey.Tab,
                WindowsKeyboardKey.A));
            CustomKeyConfiguration recorded = Assert.Single(window.ReadConfiguration().CustomKeys);

            Assert.Equal(LayoutActionTypes.Chord, recorded.ActionType);
            Assert.Empty(recorded.Input);
            Assert.Equal(["LeftWindows", "Tab", "A"], recorded.Modifiers);
            Assert.Equal("Win+Tab+A", Find<TextBlock>(window, "RecordedShortcutText").Text);
            Assert.False(window.ApplyRecordedChordForTest(WindowsKeyboardKey.B));
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
