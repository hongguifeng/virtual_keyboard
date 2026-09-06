using System.IO;
using System.Windows.Controls;
using VirtualKeyboard.Core.Diagnostics;

namespace VirtualKeyboard.App;

public partial class FocusDiagnosticsView : UserControl
{
    private FocusDiagnosticReport? _current;

    public FocusDiagnosticsView() => InitializeComponent();

    public FocusDiagnosticReport? Current => _current;

    public void Update(FocusDiagnosticReport report)
    {
        _current = report;
        ClassificationText.Text = $"{report.Editability} · {report.ReasonCode}";
        IdentityText.Text = $"版本 {report.FocusVersion} · PID {report.ProcessId} · HWND 0x{report.TopLevelHwnd:X}";
        MetadataText.Text = $"控件 {report.ControlType} · 密码 {report.IsPassword} · 降级 {report.UsedFallback}";
    }

    public void ExportCurrent(Stream destination)
    {
        if (_current is not FocusDiagnosticReport report)
            throw new InvalidOperationException("No focus diagnostic report is available.");
        FocusDiagnosticExporter.Write(destination, report);
    }
}
