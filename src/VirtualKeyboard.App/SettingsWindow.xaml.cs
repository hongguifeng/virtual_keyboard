using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

public partial class SettingsWindow : Window, IDisposable
{
    private const string TextMode = "text";
    private static readonly ManualPositionModeOption[] PositionModeOptions =
    [
        new(ManualPositionMode.UntilTargetChanges, "当前输入框", "拖动键盘后，仅为当前输入框保留位置；切换到其他输入框时恢复自动定位。"),
        new(ManualPositionMode.Persistent, "持续保留", "拖动键盘后继续使用手动位置，不因切换输入框而恢复自动定位。"),
    ];
    private readonly ConfigurationRepository _repository;
    private readonly ObservableCollection<CustomKeyEditorItem> _customKeys = [];
    private readonly KeyboardChordRecorder _chordRecorder = new();
    private CustomKeyEditorItem? _editingItem;
    private bool _loadingEditor;
    private bool _isRecordingShortcut;
    private bool _disposed;

    public SettingsWindow(ConfigurationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        PositionModeComboBox.ItemsSource = PositionModeOptions;
        CustomKeysList.ItemsSource = _customKeys;
        _chordRecorder.Captured += OnChordCaptured;
        _chordRecorder.CaptureFailed += OnChordCaptureFailed;
        LoadConfiguration(_repository.Current);
    }

    internal KeyboardConfiguration ReadConfiguration()
    {
        CommitEditor();
        return new(
            ConfigurationSchemaLimits.SupportedSchemaVersion,
            EnabledCheckBox.IsChecked == true, AutoShowCheckBox.IsChecked == true, AutoHideCheckBox.IsChecked == true,
            1 - OpacitySlider.Value, Parse(WidthTextBox.Text), Parse(HeightTextBox.Text), Parse(MarginTextBox.Text),
            LayoutIdTextBox.Text,
            PositionModeComboBox.SelectedItem is ManualPositionModeOption option ? option.Mode : ManualPositionMode.UntilTargetChanges,
            DiagnosticsCheckBox.IsChecked == true,
            _customKeys.Select(static key => new CustomKeyConfiguration(
                key.Label, key.ActionType, key.Input, key.Modifiers)));
    }

    internal bool ApplyRecordedChordForTest(params WindowsKeyboardKey[] keys) =>
        _isRecordingShortcut && ApplyRecordedChord(keys);

    private void LoadConfiguration(KeyboardConfiguration configuration)
    {
        EnabledCheckBox.IsChecked = configuration.Enabled;
        AutoShowCheckBox.IsChecked = configuration.AutoShow;
        AutoHideCheckBox.IsChecked = configuration.AutoHide;
        OpacitySlider.Value = 1 - configuration.Opacity;
        WidthTextBox.Text = configuration.KeyboardWidthDip.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = configuration.KeyboardHeightDip.ToString(CultureInfo.InvariantCulture);
        MarginTextBox.Text = configuration.MarginDip.ToString(CultureInfo.InvariantCulture);
        LayoutIdTextBox.Text = configuration.LayoutId ?? string.Empty;
        PositionModeComboBox.SelectedItem = PositionModeOptions.Single(option => option.Mode == configuration.ManualPositionMode);
        DiagnosticsCheckBox.IsChecked = configuration.DetailedDiagnostics;
        _customKeys.Clear();
        foreach (CustomKeyConfiguration key in configuration.CustomKeys)
        {
            _customKeys.Add(new(key.Label, key.ActionType, key.Input, key.Modifiers));
        }
        CustomKeysList.SelectedIndex = _customKeys.Count > 0 ? 0 : -1;
        LoadEditor(CustomKeysList.SelectedItem as CustomKeyEditorItem);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            KeyboardConfiguration configuration = ReadConfiguration();
            ConfigurationValidationResult validation = ConfigurationValidator.Validate(configuration);
            if (!validation.IsValid) { StatusText.Text = "设置无效，请检查自定义按键或数值范围。"; return; }
            ConfigurationSaveResult result = _repository.Save(configuration);
            if (!result.IsSaved) { StatusText.Text = "设置无法保存，已保留当前内存配置。"; return; }
            DialogResult = true;
        }
        catch (FormatException) { StatusText.Text = "设置无效，请输入数字。"; }
    }

    private void OnAddCustomKeyClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CommitEditor();
        if (_customKeys.Count >= ConfigurationSchemaLimits.MaximumCustomKeys) return;
        var item = new CustomKeyEditorItem("自定义", LayoutActionTypes.Text, string.Empty, []);
        _customKeys.Add(item);
        CustomKeysList.SelectedItem = item;
        CustomKeysList.ScrollIntoView(item);
    }

    private void OnDeleteCustomKeyClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (CustomKeysList.SelectedItem is not CustomKeyEditorItem item) return;
        int index = _customKeys.IndexOf(item);
        _editingItem = null;
        _customKeys.Remove(item);
        CustomKeysList.SelectedIndex = _customKeys.Count == 0 ? -1 : Math.Min(index, _customKeys.Count - 1);
    }

    private void OnCustomKeySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        CommitEditor();
        LoadEditor(CustomKeysList.SelectedItem as CustomKeyEditorItem);
    }

    private void LoadEditor(CustomKeyEditorItem? item)
    {
        StopRecording();
        _editingItem = item;
        _loadingEditor = true;
        CustomKeyEditor.IsEnabled = item is not null;
        CustomKeyLabelTextBox.Text = item?.Label ?? string.Empty;
        bool isText = item?.ActionType == LayoutActionTypes.Text;
        CustomActionModeComboBox.SelectedIndex = isText ? 0 : 1;
        CustomTextTextBox.Text = isText ? item?.Input ?? string.Empty : string.Empty;
        RecordedShortcutText.Text = item is null || isText ? "尚未录制" : item.GestureDisplay;
        _loadingEditor = false;
        UpdateEditorVisibility();
    }

    private void CommitEditor()
    {
        if (_editingItem is null || _loadingEditor) return;
        _editingItem.Label = CustomKeyLabelTextBox.Text;
        if (SelectedMode() == TextMode)
        {
            _editingItem.ActionType = LayoutActionTypes.Text;
            _editingItem.Input = CustomTextTextBox.Text;
            _editingItem.Modifiers = [];
        }
    }

    private void OnCustomActionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!_loadingEditor && _editingItem is not null)
        {
            StopRecording();
            _editingItem.ActionType = SelectedMode() == TextMode ? LayoutActionTypes.Text : LayoutActionTypes.Key;
            _editingItem.Input = string.Empty;
            _editingItem.Modifiers = [];
            CustomTextTextBox.Text = string.Empty;
            RecordedShortcutText.Text = "尚未录制";
        }
        UpdateEditorVisibility();
    }

    private void UpdateEditorVisibility()
    {
        bool isText = SelectedMode() == TextMode;
        CustomTextEditor.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        CustomShortcutEditor.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
    }

    private string SelectedMode() =>
        (CustomActionModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? TextMode;

    private void OnRecordShortcutClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_editingItem is null) return;
        if (_isRecordingShortcut)
        {
            StopRecording();
            RecordedShortcutText.Text = _editingItem.GestureDisplay;
            return;
        }
        if (!_chordRecorder.Start())
        {
            StatusText.Text = "无法启动键盘录制，请重试。";
            return;
        }
        _isRecordingShortcut = true;
        RecordShortcutButton.Content = "请按下组合键…";
        RecordedShortcutText.Text = "等待按键，全部松开后完成…";
    }

    private void OnChordCaptured(object? sender, KeyboardChordCapturedEventArgs e)
    {
        _ = sender;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyRecordedChord(e.Keys));
            return;
        }
        ApplyRecordedChord(e.Keys);
    }

    private void OnChordCaptureFailed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowChordCaptureFailure);
            return;
        }
        ShowChordCaptureFailure();
    }

    private void ShowChordCaptureFailure()
    {
        StopRecording();
        RecordedShortcutText.Text = "组合键最多支持 8 个不同按键，请重新录制。";
    }

    private bool ApplyRecordedChord(IReadOnlyList<WindowsKeyboardKey> keys)
    {
        if (!_isRecordingShortcut || _editingItem is null || keys.Count == 0) return false;
        _editingItem.ActionType = LayoutActionTypes.Chord;
        _editingItem.Input = string.Empty;
        _editingItem.Modifiers = keys.Select(static key => key.ToString()).ToArray();
        RecordedShortcutText.Text = _editingItem.GestureDisplay;
        StopRecording();
        return true;
    }

    private void StopRecording()
    {
        _isRecordingShortcut = false;
        _chordRecorder.Stop();
        if (RecordShortcutButton is not null) RecordShortcutButton.Content = "开始录制";
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _chordRecorder.Captured -= OnChordCaptured;
        _chordRecorder.CaptureFailed -= OnChordCaptureFailed;
        _chordRecorder.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DialogResult = false;
    }

    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}

internal sealed record ManualPositionModeOption(ManualPositionMode Mode, string DisplayName, string Description);

internal sealed class CustomKeyEditorItem : INotifyPropertyChanged
{
    private string _label;

    internal CustomKeyEditorItem(string label, string actionType, string input, IEnumerable<string> modifiers)
    {
        _label = label;
        ActionType = actionType;
        Input = input;
        Modifiers = modifiers.ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new(nameof(Label)));
        }
    }
    public string ActionType { get; set; }
    public string Input { get; set; }
    public IReadOnlyList<string> Modifiers { get; set; }
    public string GestureDisplay => ShortcutGesture.Format(GestureKeys());

    private IEnumerable<string> GestureKeys() => ActionType switch
    {
        LayoutActionTypes.Chord => Modifiers,
        LayoutActionTypes.Hotkey => Modifiers.Select(static modifier => modifier switch
        {
            "Windows" => "LeftWindows",
            _ => modifier,
        }).Append(Input),
        LayoutActionTypes.Key => [Input],
        _ => [],
    };
}

internal static class ShortcutGesture
{
    internal static string Format(IEnumerable<string> keys)
    {
        string[] values = keys.Where(static key => !string.IsNullOrEmpty(key)).Select(FriendlyKey).ToArray();
        return values.Length == 0 ? "尚未录制" : string.Join("+", values);
    }

    private static string FriendlyKey(string key)
    {
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) return key[1..];
        return key switch
        {
            "Control" => "Ctrl",
            "LeftWindows" or "RightWindows" or "Windows" => "Win",
            "OemSemicolon" => ";",
            "OemPlus" => "=",
            "OemComma" => ",",
            "OemMinus" => "-",
            "OemPeriod" => ".",
            "OemQuestion" => "/",
            "OemTilde" => "`",
            "OemOpenBrackets" => "[",
            "OemPipe" => "\\",
            "OemCloseBrackets" => "]",
            "OemQuotes" => "'",
            _ => key,
        };
    }
}
