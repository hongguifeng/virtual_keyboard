using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace VirtualKeyboard.TestHost;

// Isolated test process only: deterministic frozen-progress and recovery transport fixtures.
internal static class FocusWorkerFixture
{
    private static readonly int[] FixtureRuntimeId = [1, 2];
    public static int Run(string mode)
    {
        if (mode is not ("stall" or "healthy" or "exit" or "stale" or "changed-event")) return 2;
        if (mode == "exit") return 3;
        _ = Task.Run(() => { using var input = Console.OpenStandardInput(); input.ReadByte(); Environment.Exit(0); });
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        long progress = 0;
        long progressTimestamp = 0;
        for (int i = 0; i < 300; i++)
        {
            if (mode != "stall" || progress == 0) { progress++; progressTimestamp = Stopwatch.GetTimestamp(); }
            if (mode == "stale") progressTimestamp -= Stopwatch.Frequency * 10;
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                Progress = progress, EventVersion = mode == "changed-event" && i >= 4 ? 2 : 1, Stage = 2, ResultSequence = 1, ErrorCode = 0,
                ProgressTimestamp = progressTimestamp,
                Result = new
                {
                    EventVersion = 1, RetryAttempt = 0, UsedEventTarget = false, ProcessId = 42,
                    TopLevelHwnd = 100, RuntimeId = FixtureRuntimeId, ControlType = 1,
                    HasKeyboardFocus = true, IsEnabled = true, IsOffscreen = false, IsPassword = false,
                    Status = 0, Verdict = 0, Reason = 4, FocusHwnd = 101,
                    Anchor = new { X = 100, Y = 100, Width = 200, Height = 30 }, UsedFallback = false, DurationMs = 1,
                },
            }));
            Thread.Sleep(50);
        }
        return 0;
    }
}
