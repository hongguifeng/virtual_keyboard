using System.Globalization;
using System.Windows;
using VirtualKeyboard.Core.Configuration;

namespace VirtualKeyboard.App;

public partial class SettingsWindow : Window
{
    private readonly ConfigurationRepository _repository;
    public SettingsWindow(ConfigurationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        PositionModeComboBox.ItemsSource = Enum.GetValues<ManualPositionMode>();
        LoadConfiguration(_repository.Current);
    }

    internal KeyboardConfiguration ReadConfiguration() => new(
        ConfigurationSchemaLimits.SupportedSchemaVersion,
        EnabledCheckBox.IsChecked == true, AutoShowCheckBox.IsChecked == true, AutoHideCheckBox.IsChecked == true,
        Parse(OpacityTextBox.Text), Parse(WidthTextBox.Text), Parse(HeightTextBox.Text), Parse(MarginTextBox.Text),
        LayoutIdTextBox.Text, PositionModeComboBox.SelectedItem is ManualPositionMode mode ? mode : ManualPositionMode.UntilTargetChanges,
        DiagnosticsCheckBox.IsChecked == true, CustomKeyLabelTextBox.Text, CustomKeyTextBox.Text);

    private void LoadConfiguration(KeyboardConfiguration c)
    {
        EnabledCheckBox.IsChecked = c.Enabled; AutoShowCheckBox.IsChecked = c.AutoShow; AutoHideCheckBox.IsChecked = c.AutoHide;
        OpacityTextBox.Text = c.Opacity.ToString(CultureInfo.InvariantCulture); WidthTextBox.Text = c.KeyboardWidthDip.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = c.KeyboardHeightDip.ToString(CultureInfo.InvariantCulture); MarginTextBox.Text = c.MarginDip.ToString(CultureInfo.InvariantCulture);
        LayoutIdTextBox.Text = c.LayoutId ?? string.Empty; PositionModeComboBox.SelectedItem = c.ManualPositionMode;
        CustomKeyLabelTextBox.Text = c.CustomKeyLabel; CustomKeyTextBox.Text = c.CustomKeyText; DiagnosticsCheckBox.IsChecked = c.DetailedDiagnostics;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            KeyboardConfiguration configuration = ReadConfiguration();
            ConfigurationValidationResult validation = ConfigurationValidator.Validate(configuration);
            if (!validation.IsValid) { StatusText.Text = "设置无效，请检查输入范围。"; return; }
            ConfigurationSaveResult result = _repository.Save(configuration);
            if (!result.IsSaved) { StatusText.Text = "设置无法保存，已保留当前内存配置。"; return; }
            DialogResult = true;
        }
        catch (FormatException) { StatusText.Text = "设置无效，请输入数字。"; }
    }
    private void OnCancelClick(object sender, RoutedEventArgs e) { DialogResult = false; }
    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
