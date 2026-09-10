using System.Diagnostics;
using System.IO;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;
using Xunit.Abstractions;

namespace VirtualKeyboard.IntegrationTests;

[Collection("Input compatibility desktop")]
public sealed class InputCompatibilityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProductionWorkerClassifiesRealNativeControls()
    {
        string root = FindRoot();
        string configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        var start = new ProcessStartInfo(Path.Combine(root, "tests", "VirtualKeyboard.TestHost", "bin", configuration,
            "net10.0-windows", "VirtualKeyboard.TestHost.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--compatibility-case");
        start.ArgumentList.Add("numeric");
        using var target = Process.Start(start)!;
        try
        {
            // Reuse one foreground fixture window. Repeated process launches can be denied activation by Windows.
            (string Scenario, bool Editable)[] cases = [("numeric", true), ("numeric-readonly", false),
                ("masked", true), ("combo-edit", true), ("combo-select", false), ("richtext", true),
                ("richtext-readonly", false), ("slider", false), ("list", false)];
            foreach (var (scenario, editable) in cases)
            {
                await target.StandardInput.WriteLineAsync(scenario);
                await target.StandardInput.FlushAsync();
                Assert.Equal("ready", await target.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                var received = new TaskCompletionSource<FocusTargetEvaluation>(TaskCreationOptions.RunContinuationsAsynchronously);
                string latest = "no observation";
                using var observer = new IsolatedFocusObservationService(Path.Combine(root, "src", "VirtualKeyboard.App", "bin",
                    configuration, "net10.0-windows", "VirtualKeyboard.App.exe"), notification =>
                    {
                        if (notification.Evaluation is { } observed)
                            latest = $"pid={observed.Snapshot.ProcessId}; type={observed.Snapshot.ControlType}; focus={observed.Snapshot.HasKeyboardFocus}; status={observed.Status}";
                        if (notification.IsCurrent() && notification.Evaluation is { IsEvaluated: true } evaluation &&
                            evaluation.Snapshot.ProcessId == target.Id && evaluation.Snapshot.HasKeyboardFocus &&
                            evaluation.Snapshot.ControlType != FocusControlType.Window)
                            received.TrySetResult(evaluation);
                    }, _ => { }, (_, _, _) => { });
                observer.Start();
                FocusTargetEvaluation result;
                try { result = await received.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { throw new TimeoutException($"{scenario}: expected pid={target.Id}; {latest}"); }
                output.WriteLine($"{scenario}: {result.Snapshot.ControlType}/{result.Classification.Value}/{result.Classification.ReasonCode}");
                Assert.True(editable == (result.Classification.Value == Editability.Editable),
                    $"{scenario}: {result.Classification.Value}/{result.Classification.ReasonCode}");
            }
        }
        finally
        {
            if (!target.HasExited)
            {
                target.CloseMainWindow();
                try { await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { target.Kill(); await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            }
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualKeyboard.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

[CollectionDefinition("Input compatibility desktop", DisableParallelization = true)]
public sealed class InputCompatibilityDesktopGroup;
