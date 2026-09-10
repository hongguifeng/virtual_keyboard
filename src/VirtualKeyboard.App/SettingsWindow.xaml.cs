using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.Core.AutoStart;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

public partial class SettingsWindow : Window, IDisposable
{
    private const string TextMode = "text";
    private readonly ConfigurationRepository _repository;
    private readonly IAutoStartManager? _autoStart;
    private readonly ObservableCollection<CustomKeyEditorItem> _customKeys = [];
    private readonly KeyboardChordRecorder _chordRecorder = new();
    private CustomKeyEditorItem? _editingItem;
    private bool _loadingEditor;
    private bool _isRecordingShortcut;
    private bool _disposed;
    private AppStrings _strings = AppStrings.For(UiLanguage.English);

    public SettingsWindow(ConfigurationRepository repository, IAutoStartManager? autoStart = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _autoStart = autoStart;
        InitializeComponent();
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
                key.Label, key.ActionType, key.Input, key.Modifiers)),
            SelectedLanguage(),
            AutoStartCheckBox.IsChecked == true,
            ShowLauncherButtonCheckBox.IsChecked == true);
    }

    internal bool ApplyRecordedChordForTest(params WindowsKeyboardKey[] keys) =>
        _isRecordingShortcut && ApplyRecordedChord(keys);

    private void LoadConfiguration(KeyboardConfiguration configuration)
    {
        LanguageComboBox.SelectedIndex = configuration.UiLanguage == UiLanguage.SimplifiedChinese ? 1 : 0;
        ApplyLanguage(configuration.UiLanguage);
        EnabledCheckBox.IsChecked = configuration.Enabled;
        AutoShowCheckBox.IsChecked = configuration.AutoShow;
        ShowLauncherButtonCheckBox.IsChecked = configuration.ShowLauncherButton;
        AutoHideCheckBox.IsChecked = configuration.AutoHide;
        OpacitySlider.Value = 1 - configuration.Opacity;
        WidthTextBox.Text = configuration.KeyboardWidthDip.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = configuration.KeyboardHeightDip.ToString(CultureInfo.InvariantCulture);
        MarginTextBox.Text = configuration.MarginDip.ToString(CultureInfo.InvariantCulture);
        LayoutIdTextBox.Text = configuration.LayoutId ?? string.Empty;
        PositionModeComboBox.SelectedItem = PositionModeComboBox.Items.Cast<ManualPositionModeOption>().Single(option => option.Mode == configuration.ManualPositionMode);
        DiagnosticsCheckBox.IsChecked = configuration.DetailedDiagnostics;
        AutoStartCheckBox.IsChecked = configuration.AutoStart;
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
            if (!validation.IsValid) { StatusText.Text = _strings.InvalidSettings; return; }
            ConfigurationSaveResult result = _repository.Save(configuration);
            if (!result.IsSaved) { StatusText.Text = _strings.SaveFailed; return; }
            if (!ApplyAutoStart(configuration)) return; // 应用失败：保持窗口打开并显示警告，由用户决定重试或取消
            DialogResult = true;
        }
        catch (FormatException) { StatusText.Text = _strings.InvalidNumber; }
    }

    /// <summary>
    /// 保存后将配置镜像应用到注册表（FR-APP-004，设计 14.4）。
    /// 注册表是自启的唯一事实来源：写入/删除失败、或写入后被外部修改（安全软件/策略）时，
    /// 回滚配置镜像并取消勾选，保持“配置镜像 = 注册表事实”不变量；返回 false 表示未能应用。
    /// </summary>
    private bool ApplyAutoStart(KeyboardConfiguration configuration)
    {
        if (_autoStart is null) return true;
        bool wanted = configuration.AutoStart;
        if (!_autoStart.TrySetEnabled(wanted, out _))
        {
            RevertAutoStart(wanted);
            StatusText.Text = _strings.AutoStartApplyFailed;
            return false;
        }
        if (_autoStart.TryGetEnabled(out bool actual) && actual != wanted)
        {
            RevertAutoStart(wanted);
            StatusText.Text = _strings.AutoStartApplyFailed;
            return false;
        }
        return true;
    }

    private void RevertAutoStart(bool wanted)
    {
        KeyboardConfiguration current = _repository.Current;
        _repository.Save(new(
            current.SchemaVersion, current.Enabled, current.AutoShow, current.AutoHide, current.Opacity,
            current.KeyboardWidthDip, current.KeyboardHeightDip, current.MarginDip, current.LayoutId,
            current.ManualPositionMode, current.DetailedDiagnostics, current.CustomKeys, current.UiLanguage,
            autoStart: !wanted, showLauncherButton: current.ShowLauncherButton));
        AutoStartCheckBox.IsChecked = !wanted;
    }

    private void OnAddCustomKeyClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CommitEditor();
        if (_customKeys.Count >= ConfigurationSchemaLimits.MaximumCustomKeys) return;
        var item = new CustomKeyEditorItem(_strings.NewCustomKey, LayoutActionTypes.Text, string.Empty, []);
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
        RecordedShortcutText.Text = item is null || isText ? _strings.NotRecorded : item.GestureDisplay;
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
            RecordedShortcutText.Text = _strings.NotRecorded;
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
            StatusText.Text = _strings.RecordingFailed;
            return;
        }
        _isRecordingShortcut = true;
        RecordShortcutButton.Content = _strings.PressShortcut;
        RecordedShortcutText.Text = _strings.WaitingRelease;
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
        RecordedShortcutText.Text = _strings.TooManyChordKeys;
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
        if (RecordShortcutButton is not null) RecordShortcutButton.Content = _strings.StartRecording;
    }

    private UiLanguage SelectedLanguage() =>
        (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string == nameof(UiLanguage.SimplifiedChinese)
            ? UiLanguage.SimplifiedChinese
            : UiLanguage.English;

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (SaveButton is not null) ApplyLanguage(SelectedLanguage());
    }

    private void ApplyLanguage(UiLanguage language)
    {
        ManualPositionMode selectedMode = PositionModeComboBox.SelectedItem is ManualPositionModeOption selected
            ? selected.Mode
            : ManualPositionMode.UntilTargetChanges;
        _strings = AppStrings.For(language);
        Title = _strings.SettingsTitle;
        LanguageLabel.Content = _strings.LanguageLabel;
        EnabledCheckBox.Content = _strings.Enabled;
        AutoShowCheckBox.Content = _strings.AutoShow;
        ShowLauncherButtonCheckBox.Content = _strings.ShowLauncherButton;
        AutoHideCheckBox.Content = _strings.AutoHide;
        WidthLabel.Content = _strings.Width;
        HeightLabel.Content = _strings.Height;
        MarginLabel.Content = _strings.Margin;
        TransparencyLabel.Content = _strings.Transparency;
        LayoutIdLabel.Content = _strings.LayoutId;
        CustomKeysGroup.Header = _strings.CustomKeys;
        AddCustomKeyButton.Content = _strings.Add;
        DeleteCustomKeyButton.Content = _strings.Delete;
        KeyNameLabel.Content = _strings.KeyName;
        OnPressLabel.Content = _strings.OnPress;
        TextModeItem.Content = _strings.EnterText;
        ShortcutModeItem.Content = _strings.RecordShortcut;
        TextContentLabel.Content = _strings.TextContent;
        RecordedLabel.Content = _strings.Recorded;
        RecordingHelpText.Text = _strings.RecordingHelp;
        PositionRetentionLabel.Content = _strings.PositionRetention;
        DiagnosticsCheckBox.Content = _strings.Diagnostics;
        AutoStartCheckBox.Content = _strings.AutoStart;
        AutoStartDescription.Text = _strings.AutoStartDescription;
        SaveButton.Content = _strings.Save;
        CancelButton.Content = _strings.Cancel;
        PositionModeComboBox.ItemsSource = new[]
        {
            new ManualPositionModeOption(ManualPositionMode.UntilTargetChanges, _strings.CurrentField, _strings.CurrentFieldDescription),
            new ManualPositionModeOption(ManualPositionMode.Persistent, _strings.Persistent, _strings.PersistentDescription),
        };
        PositionModeComboBox.SelectedItem = PositionModeComboBox.Items.Cast<ManualPositionModeOption>().Single(option => option.Mode == selectedMode);
        if (!_isRecordingShortcut && (_editingItem is null || _editingItem.ActionType == LayoutActionTypes.Text || string.IsNullOrEmpty(_editingItem.GestureDisplay)))
            RecordedShortcutText.Text = _strings.NotRecorded;
        StopRecording();
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
