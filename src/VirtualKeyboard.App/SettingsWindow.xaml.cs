using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.App;

public partial class SettingsWindow : Window
{
    private const string TextMode = "text";
    private readonly ConfigurationRepository _repository;
    private readonly ObservableCollection<CustomKeyEditorItem> _customKeys = [];
    private CustomKeyEditorItem? _editingItem;
    private bool _loadingEditor;
    private bool _isRecordingShortcut;

    public SettingsWindow(ConfigurationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        PositionModeComboBox.ItemsSource = Enum.GetValues<ManualPositionMode>();
        CustomKeysList.ItemsSource = _customKeys;
        LoadConfiguration(_repository.Current);
    }

    internal KeyboardConfiguration ReadConfiguration()
    {
        CommitEditor();
        return new(
            ConfigurationSchemaLimits.SupportedSchemaVersion,
            EnabledCheckBox.IsChecked == true, AutoShowCheckBox.IsChecked == true, AutoHideCheckBox.IsChecked == true,
            OpacitySlider.Value, Parse(WidthTextBox.Text), Parse(HeightTextBox.Text), Parse(MarginTextBox.Text),
            LayoutIdTextBox.Text, PositionModeComboBox.SelectedItem is ManualPositionMode mode ? mode : ManualPositionMode.UntilTargetChanges,
            DiagnosticsCheckBox.IsChecked == true,
            _customKeys.Select(static key => new CustomKeyConfiguration(
                key.Label, key.ActionType, key.Input, key.Modifiers)));
    }

    internal bool RecordShortcutForTest(Key key, ModifierKeys modifiers) =>
        _isRecordingShortcut && RecordShortcut(key, modifiers);

    private void LoadConfiguration(KeyboardConfiguration configuration)
    {
        EnabledCheckBox.IsChecked = configuration.Enabled;
        AutoShowCheckBox.IsChecked = configuration.AutoShow;
        AutoHideCheckBox.IsChecked = configuration.AutoHide;
        OpacitySlider.Value = configuration.Opacity;
        WidthTextBox.Text = configuration.KeyboardWidthDip.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = configuration.KeyboardHeightDip.ToString(CultureInfo.InvariantCulture);
        MarginTextBox.Text = configuration.MarginDip.ToString(CultureInfo.InvariantCulture);
        LayoutIdTextBox.Text = configuration.LayoutId ?? string.Empty;
        PositionModeComboBox.SelectedItem = configuration.ManualPositionMode;
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
        _isRecordingShortcut = !_isRecordingShortcut;
        RecordShortcutButton.Content = _isRecordingShortcut ? "请按下快捷键…" : "开始录制";
        RecordedShortcutText.Text = _isRecordingShortcut ? "等待键盘输入…" : _editingItem.GestureDisplay;
        if (_isRecordingShortcut) Keyboard.Focus(RecordShortcutButton);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _ = sender;
        if (!_isRecordingShortcut) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!RecordShortcut(key, Keyboard.Modifiers)) RecordedShortcutText.Text = "请按一个非修饰键";
    }

    private bool RecordShortcut(Key key, ModifierKeys modifiers)
    {
        if (_editingItem is null || !ShortcutGesture.TryCreate(key, modifiers, out ShortcutGesture gesture)) return false;
        _editingItem.ActionType = gesture.Modifiers.Count == 0 ? LayoutActionTypes.Key : LayoutActionTypes.Hotkey;
        _editingItem.Input = gesture.Key;
        _editingItem.Modifiers = gesture.Modifiers;
        RecordedShortcutText.Text = gesture.Display;
        StopRecording();
        return true;
    }

    private void StopRecording()
    {
        _isRecordingShortcut = false;
        if (RecordShortcutButton is not null) RecordShortcutButton.Content = "开始录制";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DialogResult = false;
    }

    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}

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
    public string GestureDisplay => ShortcutGesture.Format(Input, Modifiers);
}

internal readonly record struct ShortcutGesture(string Key, IReadOnlyList<string> Modifiers)
{
    internal string Display => Format(Key, Modifiers);

    internal static bool TryCreate(Key key, ModifierKeys modifiers, out ShortcutGesture gesture)
    {
        gesture = default;
        if (key is System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or
            System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or
            System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or
            System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
        {
            return false;
        }
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var parsed = (WindowsKeyboardKey)(ushort)virtualKey;
        if (virtualKey <= 0 || !Enum.IsDefined(parsed)) return false;
        var captured = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) captured.Add("Control");
        if (modifiers.HasFlag(ModifierKeys.Shift)) captured.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) captured.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) captured.Add("Windows");
        gesture = new(parsed.ToString(), captured.AsReadOnly());
        return true;
    }

    internal static string Format(string key, IReadOnlyList<string> modifiers)
    {
        if (string.IsNullOrEmpty(key)) return "尚未录制";
        return string.Join("+", modifiers.Select(static modifier => modifier switch
        {
            "Control" => "Ctrl",
            "Windows" => "Win",
            _ => modifier,
        }).Append(FriendlyKey(key)));
    }

    private static string FriendlyKey(string key)
    {
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) return key[1..];
        return key switch
        {
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
