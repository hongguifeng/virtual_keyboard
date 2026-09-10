using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    public void CustomKeyLocationsCommitEditsIndependentlyAndPersistBothLists()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var window = new SettingsWindow(fixture.Repository);
            var location = Find<ComboBox>(window, "CustomKeyLocationComboBox");
            var list = Find<ListBox>(window, "CustomKeysList");
            Find<Button>(window, "AddCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>(window, "CustomKeyLabelTextBox").Text = "Keyboard";
            Find<TextBox>(window, "CustomTextTextBox").Text = "keyboard-only";
            location.SelectedIndex = 1;
            Assert.Empty(list.Items);
            Find<Button>(window, "AddCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TextBox>(window, "CustomKeyLabelTextBox").Text = "Floating";
            Find<TextBox>(window, "CustomTextTextBox").Text = "floating-only";
            location.SelectedIndex = 0;
            Assert.Equal("Keyboard", Find<TextBox>(window, "CustomKeyLabelTextBox").Text);
            location.SelectedIndex = 1;
            Assert.Equal("floating-only", Find<TextBox>(window, "CustomTextTextBox").Text);
            Find<ComboBox>(window, "LanguageComboBox").SelectedIndex = 1;
            Assert.Equal("悬浮按钮", Find<ComboBoxItem>(window, "LauncherLocationItem").Content);
            Assert.Equal(1, location.SelectedIndex);
            Assert.True(fixture.Repository.Save(window.ReadConfiguration()).IsSaved);
            using var reopened = new SettingsWindow(fixture.Repository);
            Find<ComboBox>(reopened, "CustomKeyLocationComboBox").SelectedIndex = 1;
            Assert.Equal("Floating", Find<TextBox>(reopened, "CustomKeyLabelTextBox").Text);
            Find<Button>(reopened, "DeleteCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            KeyboardConfiguration remaining = reopened.ReadConfiguration();
            Assert.Empty(remaining.LauncherCustomKeys);
            Assert.Equal("keyboard-only", Assert.Single(remaining.CustomKeys).Input);
        });
    }

    [Fact]
    public void LauncherShortcutsCanBeRecordedAndSwitchingLocationCancelsRecording()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var window = new SettingsWindow(fixture.Repository);
            var location = Find<ComboBox>(window, "CustomKeyLocationComboBox");
            location.SelectedIndex = 1;
            Find<Button>(window, "AddCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<ComboBox>(window, "CustomActionModeComboBox").SelectedIndex = 1;
            Find<Button>(window, "RecordShortcutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.ApplyRecordedChordForTest(WindowsKeyboardKey.Control, WindowsKeyboardKey.S));
            Assert.Equal(["Control", "S"], Assert.Single(window.ReadConfiguration().LauncherCustomKeys).Modifiers);
            Find<Button>(window, "RecordShortcutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            location.SelectedIndex = 0;
            Assert.False(window.ApplyRecordedChordForTest(WindowsKeyboardKey.Enter));
            Assert.Empty(window.ReadConfiguration().CustomKeys);
            Assert.Equal(["Control", "S"], Assert.Single(window.ReadConfiguration().LauncherCustomKeys).Modifiers);
        });
    }

    [Fact]
    public void LauncherSettingLoadsSavesAndSwitchesLanguageWithoutLosingChoice()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            using var window = new SettingsWindow(fixture.Repository);
            CheckBox launcher = Find<CheckBox>(window, "ShowLauncherButtonCheckBox");
            Assert.False(launcher.IsChecked);
            Assert.Equal("Show a floating button before opening the keyboard", launcher.Content);
            launcher.IsChecked = true;
            Find<ComboBox>(window, "LanguageComboBox").SelectedIndex = 1;
            Assert.Equal("先在光标附近显示悬浮按钮，点击后展开键盘", launcher.Content);
            Assert.True(fixture.Repository.Save(window.ReadConfiguration()).IsSaved);
            using var reopened = new SettingsWindow(fixture.Repository);
            Assert.True(Find<CheckBox>(reopened, "ShowLauncherButtonCheckBox").IsChecked);
            Assert.True(fixture.Repository.Load().Configuration.ShowLauncherButton);
            Find<CheckBox>(window, "AutoShowCheckBox").IsChecked = false;
            Assert.False(launcher.IsEnabled);
            Assert.True(window.ReadConfiguration().ShowLauncherButton);
        });
    }

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
            var positionOption = Assert.IsType<ManualPositionModeOption>(Find<ComboBox>(window, "PositionModeComboBox").SelectedItem);
            Assert.Equal(ManualPositionMode.Persistent, positionOption.Mode);
            Assert.Equal("Keep until changed", positionOption.DisplayName);
            Assert.Contains("switching", Find<TextBlock>(window, "PositionModeDescription").Text);
            var list = Find<ListBox>(window, "CustomKeysList");
            Assert.Equal(2, list.Items.Count);
            Assert.Equal("邮箱", Assert.IsType<CustomKeyEditorItem>(list.Items[0]).Label);
            Assert.Equal(["Control"], Assert.IsType<CustomKeyEditorItem>(list.Items[1]).Modifiers);
            window.Close();
        });
    }

    [Fact]
    public void DefaultsToEnglishAndCanPersistSimplifiedChinese()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);

            Assert.Equal("Virtual Keyboard Settings", window.Title);
            Assert.Equal("Enable keyboard", Find<CheckBox>(window, "EnabledCheckBox").Content);
            Find<ComboBox>(window, "LanguageComboBox").SelectedIndex = 1;

            Assert.Equal("Virtual Keyboard 设置", window.Title);
            Assert.Equal("启用键盘", Find<CheckBox>(window, "EnabledCheckBox").Content);
            Assert.Equal(UiLanguage.SimplifiedChinese, window.ReadConfiguration().UiLanguage);
            window.Close();
        });
    }

    [Fact]
    public void SettingsUsesModernCardAndAccentActionStyling()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);
            var card = Find<Border>(window, "SettingsCard");
            var save = Find<Button>(window, "SaveButton");

            Assert.Equal(new CornerRadius(12), card.CornerRadius);
            Assert.Equal(Color.FromRgb(0x28, 0x78, 0xF0), Assert.IsType<SolidColorBrush>(save.Background).Color);
            Assert.Equal("Segoe UI Variable Text, Segoe UI", window.FontFamily.Source);
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

            Assert.Contains("Invalid", Find<TextBlock>(window, "StatusText").Text);
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
            Assert.Contains("could not be saved", Find<TextBlock>(window, "StatusText").Text);
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

    [Fact]
    public void RecordButtonIsNotCoveredByRecordingHelpInSimplifiedChinese()
    {
        RunOnStaThread(() =>
        {
            using var fixture = new Fixture();
            var window = new SettingsWindow(fixture.Repository);
            Find<ComboBox>(window, "LanguageComboBox").SelectedIndex = 1;
            Find<Button>(window, "AddCustomKeyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<ComboBox>(window, "CustomActionModeComboBox").SelectedIndex = 1;

            // 窗口高度必须跟随内容（原固定 760 高度 + 等权 * 行会把内容压回重叠）。
            Assert.Equal(SizeToContent.Height, window.SizeToContent);

            var root = Assert.IsType<Border>(window.Content);
            root.Measure(new Size(720, double.PositiveInfinity));
            // 在窗口实际会取得的高度下 arrange（SizeToContent 窗口 = 内容 desired 高度，受 MinHeight 700 约束）。
            // 等权 * 行只有在受限高度下才会被压缩到小于内容，所以必须在这个高度下检查。
            double windowHeight = Math.Max(700, root.DesiredSize.Height);
            root.Arrange(new Rect(0, 0, 720, windowHeight));

            // 自定义按键组不得被压缩（原回归：* 行把它压到远小于内容高度，内容溢出覆盖下一行）。
            var group = Find<GroupBox>(window, "CustomKeysGroup");
            // DesiredSize 含 Margin、ActualHeight 不含，比较时需扣除 Margin。
            // 原回归中该组被 * 行压缩到远小于内容高度（约 266px 溢出），容差 6px 足以拦截该类回归。
            Assert.InRange(group.ActualHeight, group.DesiredSize.Height - group.Margin.Top - 6, double.MaxValue);

            // 原始回归断言：录制按键按钮不得被简体中文提示文字覆盖。
            Rect buttonRect = ElementRect(root, Find<Button>(window, "RecordShortcutButton"));
            Rect helpRect = ElementRect(root, Find<TextBlock>(window, "RecordingHelpText"));
            Assert.InRange(helpRect.Height, 0.5, double.MaxValue);
            double overlapX = Math.Min(buttonRect.Right, helpRect.Right) - Math.Max(buttonRect.Left, helpRect.Left);
            double overlapY = Math.Min(buttonRect.Bottom, helpRect.Bottom) - Math.Max(buttonRect.Top, helpRect.Top);
            Assert.True(overlapX <= 0.5 || overlapY <= 0.5,
                $"录制按键按钮不得被提示文字覆盖: button={buttonRect} help={helpRect} overlapX={overlapX} overlapY={overlapY}");

            // 强化回归断言：任意两个独立控件（含自定义按键组与“拖拽位置保留”行等）不得互相重叠。
            // 父/子嵌套是合法的，只检查非嵌套关系。这可以拦截任何“某行被压缩到小于内容高度”
            // 导致的视觉覆盖回归。
            foreach (var (first, second, rect) in FindOverlappingSiblingRects(window, root))
            {
                Assert.Fail($"控件重叠: {first.Name} vs {second.Name}: rect={rect}");
            }
        });
    }

    private static IEnumerable<(FrameworkElement, FrameworkElement, Rect)> FindOverlappingSiblingRects(Window window, FrameworkElement root)
    {
        var candidates = new FrameworkElement?[]
        {
            Find<ComboBox>(window, "LanguageComboBox"), Find<CheckBox>(window, "EnabledCheckBox"),
            Find<CheckBox>(window, "AutoShowCheckBox"), Find<CheckBox>(window, "AutoHideCheckBox"),
            Find<CheckBox>(window, "ShowLauncherButtonCheckBox"),
            Find<ComboBox>(window, "CustomKeyLocationComboBox"),
            Find<TextBox>(window, "WidthTextBox"), Find<TextBox>(window, "HeightTextBox"),
            Find<TextBox>(window, "MarginTextBox"), Find<Slider>(window, "OpacitySlider"),
            Find<TextBox>(window, "LayoutIdTextBox"), Find<ListBox>(window, "CustomKeysList"),
            Find<Button>(window, "AddCustomKeyButton"), Find<Button>(window, "DeleteCustomKeyButton"),
            Find<TextBox>(window, "CustomKeyLabelTextBox"), Find<ComboBox>(window, "CustomActionModeComboBox"),
            Find<TextBox>(window, "CustomTextTextBox"), Find<TextBlock>(window, "RecordedShortcutText"),
            Find<Button>(window, "RecordShortcutButton"), Find<TextBlock>(window, "RecordingHelpText"),
            Find<ComboBox>(window, "PositionModeComboBox"), Find<CheckBox>(window, "DiagnosticsCheckBox"),
            Find<TextBlock>(window, "PositionModeDescription"), Find<Button>(window, "SaveButton"),
            Find<Button>(window, "CancelButton"),
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            for (int j = i + 1; j < candidates.Length; j++)
            {
                if (candidates[i] is not FrameworkElement a || candidates[j] is not FrameworkElement b)
                {
                    continue;
                }
                if (IsInAncestorChain(a, b) || IsInAncestorChain(b, a))
                {
                    continue; // 父/子嵌套是合法的
                }
                var rectA = ElementRect(root, a);
                var rectB = ElementRect(root, b);
                double overlapX = Math.Min(rectA.Right, rectB.Right) - Math.Max(rectA.Left, rectB.Left);
                double overlapY = Math.Min(rectA.Bottom, rectB.Bottom) - Math.Max(rectA.Top, rectB.Top);
                if (overlapX > 1 && overlapY > 1)
                {
                    yield return (a, b, rectB);
                }
            }
        }
    }

    private static bool IsInAncestorChain(DependencyObject ancestor, DependencyObject element)
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == ancestor)
            {
                return true;
            }
        }
        return false;
    }

    private static Rect ElementRect(FrameworkElement root, FrameworkElement element)
    {
        Point top = element.TranslatePoint(new Point(0, 0), root);
        return new Rect(top.X, top.Y, element.ActualWidth, element.ActualHeight);
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
