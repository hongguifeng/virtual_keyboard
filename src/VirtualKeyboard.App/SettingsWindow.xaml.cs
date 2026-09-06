using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VirtualKeyboard.Core.Configuration;
using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.App;

public partial class SettingsWindow : Window
{
    private readonly ConfigurationRepository _repository;
    private readonly ObservableCollection<CustomKeyEditorItem> _customKeys = [];
    public SettingsWindow(ConfigurationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        PositionModeComboBox.ItemsSource = Enum.GetValues<ManualPositionMode>();
        ActionTypeColumn.ItemsSource = new[] { LayoutActionTypes.Text, LayoutActionTypes.Key, LayoutActionTypes.Hotkey };
        CustomKeysGrid.ItemsSource = _customKeys;
        LoadConfiguration(_repository.Current);
    }

    internal KeyboardConfiguration ReadConfiguration() => new(
        ConfigurationSchemaLimits.SupportedSchemaVersion,
        EnabledCheckBox.IsChecked == true, AutoShowCheckBox.IsChecked == true, AutoHideCheckBox.IsChecked == true,
        Parse(OpacityTextBox.Text), Parse(WidthTextBox.Text), Parse(HeightTextBox.Text), Parse(MarginTextBox.Text),
        LayoutIdTextBox.Text, PositionModeComboBox.SelectedItem is ManualPositionMode mode ? mode : ManualPositionMode.UntilTargetChanges,
        DiagnosticsCheckBox.IsChecked == true,
        _customKeys.Select(static key => new CustomKeyConfiguration(
            key.Label, key.ActionType, key.Input, SplitModifiers(key.Modifiers))));

    private void LoadConfiguration(KeyboardConfiguration c)
    {
        EnabledCheckBox.IsChecked = c.Enabled; AutoShowCheckBox.IsChecked = c.AutoShow; AutoHideCheckBox.IsChecked = c.AutoHide;
        OpacityTextBox.Text = c.Opacity.ToString(CultureInfo.InvariantCulture); WidthTextBox.Text = c.KeyboardWidthDip.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = c.KeyboardHeightDip.ToString(CultureInfo.InvariantCulture); MarginTextBox.Text = c.MarginDip.ToString(CultureInfo.InvariantCulture);
        LayoutIdTextBox.Text = c.LayoutId ?? string.Empty; PositionModeComboBox.SelectedItem = c.ManualPositionMode;
        _customKeys.Clear();
        foreach (CustomKeyConfiguration key in c.CustomKeys)
        {
            _customKeys.Add(new(key.Label, key.ActionType, key.Input, string.Join("+", key.Modifiers)));
        }
        DiagnosticsCheckBox.IsChecked = c.DetailedDiagnostics;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CustomKeysGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            CustomKeysGrid.CommitEdit(DataGridEditingUnit.Row, true);
            KeyboardConfiguration configuration = ReadConfiguration();
            ConfigurationValidationResult validation = ConfigurationValidator.Validate(configuration);
            if (!validation.IsValid) { StatusText.Text = "设置无效，请检查输入范围。"; return; }
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
        if (_customKeys.Count < ConfigurationSchemaLimits.MaximumCustomKeys)
        {
            var item = new CustomKeyEditorItem("自定义", LayoutActionTypes.Text, string.Empty, string.Empty);
            _customKeys.Add(item);
            CustomKeysGrid.SelectedItem = item;
            CustomKeysGrid.ScrollIntoView(item);
        }
    }

    private void OnDeleteCustomKeyClick(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (CustomKeysGrid.SelectedItem is CustomKeyEditorItem item) _customKeys.Remove(item);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) { DialogResult = false; }
    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string[] SplitModifiers(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(['+', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class CustomKeyEditorItem(string label, string actionType, string input, string modifiers)
{
    public string Label { get; set; } = label;
    public string ActionType { get; set; } = actionType;
    public string Input { get; set; } = input;
    public string Modifiers { get; set; } = modifiers;
}
