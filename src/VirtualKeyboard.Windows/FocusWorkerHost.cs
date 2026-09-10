using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VirtualKeyboard.Windows;

/// <summary>Headless process entry point. Only its dedicated MTA observer accesses UIA.</summary>
public static class FocusWorkerHost
{
    public static int Run(int parentProcessId)
    {
        // Inherited stdin closes on parent death, including abnormal termination. No orphan UIA process.
        _ = Task.Run(() => { using var input = Console.OpenStandardInput(); input.ReadByte(); Environment.Exit(0); });
        object gate = new();
        FocusWorkerResult? latest = null;
        long sequence = 0;
        int error = 0;
        var evaluator = new FocusTargetEvaluator();
        FocusObservationService? observer = null;
        observer = new FocusObservationService(errorObserver: code => Volatile.Write(ref error, code), evaluate: notification =>
        {
            long eventVersion = observer!.EventVersion;
            long start = Stopwatch.GetTimestamp();
            FocusWorkerResult? result = null;
            bool retry = false;
            if (notification.Snapshot is { } snapshot && snapshot.ProcessId != parentProcessId)
            {
                var evaluation = evaluator.Evaluate(notification);
                retry = evaluation.NeedsRetry;
                result = FocusWorkerResult.From(notification, evaluation, eventVersion,
                    (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            if (notification.IsCurrent)
                lock (gate) { latest = result; sequence++; }
            return retry;
        });
        // Startup itself can block in UIA. Parent startup deadline covers this too.
        observer.Start();
        observer.Refresh();
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        try
        {
            while (true)
            {
                FocusWorkerPacket packet;
                lock (gate)
                    packet = new(observer.CompletedCycles, observer.EventVersion, observer.Stage, sequence, latest, Volatile.Read(ref error))
                    { ProgressTimestamp = observer.ProgressTimestamp };
                string line = JsonSerializer.Serialize(packet);
                if (line.Length > FocusWorkerPacket.MaximumLineLength) return 2;
                output.WriteLine(line);
                Thread.Sleep(100);
            }
        }
        catch (IOException) { return 0; }
        // Do not join a potentially blocked COM thread here; process exit is the cancellation boundary.
    }
}
