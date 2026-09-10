using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.IntegrationTests;

public sealed class IsolatedFocusObservationTests
{
    [Fact]
    public async Task QueuedOldProgressCannotRenewTargetAuthority()
    {
        var exhausted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int results = 0;
        using var supervisor = new IsolatedFocusObservationService("unused", _ => Interlocked.Increment(ref results), _ => { },
            (status, _, _) => { if (status == FocusWorkerStatus.Exhausted) exhausted.TrySetResult(); })
        {
            StartInfoFactory = () => Fixture("stale"), RestartDelay = TimeSpan.FromMilliseconds(20),
        };
        supervisor.Start();
        await exhausted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, results);
        Assert.False(supervisor.IsHealthy);
    }

    [Fact]
    public async Task ChangedEventInvalidatesPreviouslyPublishedResultEvenBeforeNewEvaluation()
    {
        int invalidations = 0;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new ConcurrentQueue<RemoteFocusNotification>();
        using var supervisor = new IsolatedFocusObservationService("unused", notifications.Enqueue,
            fault => { if (!fault && Interlocked.Increment(ref invalidations) == 2) changed.TrySetResult(); }, (_, _, _) => { })
        { StartInfoFactory = () => Fixture("changed-event") };
        supervisor.Start();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(notifications);
        Assert.False(notifications.Single().IsCurrent());
    }

    [Fact]
    public async Task ProductionWorkerIsHeadlessAndExitsWhenParentPipeCloses()
    {
        var start = Fixture("healthy");
        string root = FindRoot();
        string configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        start.FileName = Path.Combine(root, "src", "VirtualKeyboard.App", "bin", configuration, "net10.0-windows", "VirtualKeyboard.App.exe");
        start.ArgumentList.Clear();
        start.ArgumentList.Add("--focus-worker");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = Process.Start(start)!;
        try
        {
            string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Assert.NotNull(line);
            Assert.True(FocusWorkerPacket.Parse(line).Progress > 0);
            Assert.Equal(IntPtr.Zero, process.MainWindowHandle);
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } }
    }

    [Fact]
    public async Task FrozenProgressKillsWorkerInvalidatesOldResultAndRecoversWithNewGeneration()
    {
        var notifications = new ConcurrentQueue<RemoteFocusNotification>();
        var pids = new ConcurrentQueue<int>();
        var recovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        int faults = 0;
        using var supervisor = new IsolatedFocusObservationService("unused", notifications.Enqueue,
            fault => { if (fault) Interlocked.Increment(ref faults); },
            (status, pid, _) =>
            {
                if (status == FocusWorkerStatus.Started) pids.Enqueue(pid);
                if (status == FocusWorkerStatus.Recovered) recovery.TrySetResult();
            })
        {
            StartInfoFactory = () => Fixture(Interlocked.Increment(ref attempts) == 1 ? "stall" : "healthy"),
            StallTimeout = TimeSpan.FromMilliseconds(400), RestartDelay = TimeSpan.FromMilliseconds(20),
        };
        supervisor.Start();
        await recovery.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, attempts);
        Assert.True(faults >= 1);
        var results = notifications.Where(n => n.Evaluation is not null).ToArray();
        Assert.Equal(2, results.Length);
        Assert.False(results[0].IsCurrent());
        Assert.True(results[1].IsCurrent());
        Assert.True(results[1].Evaluation!.Value.Snapshot.Version > results[0].Evaluation!.Value.Snapshot.Version);
        Assert.True(supervisor.IsHealthy);
        Assert.False(IsAlive(pids.First()));
        supervisor.Dispose();
        Assert.False(results[1].IsCurrent());
        Assert.All(pids, pid => Assert.False(IsAlive(pid)));
    }

    [Fact]
    public async Task RepeatedWorkerFailureHasFiniteRestartBudget()
    {
        int attempts = 0;
        var exhausted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var supervisor = new IsolatedFocusObservationService("unused", _ => { }, _ => { },
            (status, _, _) => { if (status == FocusWorkerStatus.Exhausted) exhausted.TrySetResult(); })
        {
            StartInfoFactory = () => { Interlocked.Increment(ref attempts); return Fixture("exit"); },
            RestartDelay = TimeSpan.FromMilliseconds(20),
        };
        supervisor.Start();
        await exhausted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(4, attempts);
        Assert.False(supervisor.IsHealthy);
    }

    [Fact]
    public async Task ExplicitRearmAfterExhaustionPreservesVersionsAndDoesNotDuplicateRunningWorker()
    {
        int attempts = 0;
        var notifications = new ConcurrentQueue<RemoteFocusNotification>();
        using var supervisor = new IsolatedFocusObservationService("unused", notifications.Enqueue, _ => { }, (_, _, _) => { })
        {
            StartInfoFactory = () => Fixture(Interlocked.Increment(ref attempts) <= 4 ? "stall" : "healthy"),
            StallTimeout = TimeSpan.FromMilliseconds(400), RestartDelay = TimeSpan.FromMilliseconds(20),
        };
        supervisor.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!supervisor.CanRestart) await Task.Delay(20, deadline.Token);
        Assert.True(supervisor.IsStopped);
        Assert.Equal(4, attempts);
        Assert.All(notifications, notification => Assert.False(notification.IsCurrent()));
        long oldVersion = notifications.Last(n => n.Evaluation is not null).Evaluation!.Value.Snapshot.Version;
        supervisor.Start();
        supervisor.Start();
        while (!supervisor.IsHealthy || !notifications.Any(n => n.Evaluation?.Snapshot.Version > oldVersion))
            await Task.Delay(20, deadline.Token);
        Assert.Equal(5, attempts);
        Assert.False(supervisor.IsStopped);
        Assert.False(supervisor.CanRestart);
        Assert.True(notifications.Last().IsCurrent());
    }

    private static ProcessStartInfo Fixture(string mode)
    {
        string root = FindRoot();
        string configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        var info = new ProcessStartInfo(Path.Combine(root, "tests", "VirtualKeyboard.TestHost", "bin", configuration,
            "net10.0-windows", "VirtualKeyboard.TestHost.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true };
        info.ArgumentList.Add("--focus-worker-fixture");
        info.ArgumentList.Add(mode);
        return info;
    }

    private static string FindRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VirtualKeyboard.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }

    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
