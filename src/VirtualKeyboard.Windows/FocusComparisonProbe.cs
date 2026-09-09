using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Automation;

namespace VirtualKeyboard.Windows;

public enum FocusProbeSource { EventTarget, CurrentAfterEvent, Poll }

/// <summary>Only allow-listed metadata; no element Name, Value, text, or window title.</summary>
public readonly record struct FocusProbeRow(
    long Sequence, DateTimeOffset ObservedAtUtc, FocusProbeSource Source,
    int ProcessId, int ControlTypeId, bool HasKeyboardFocus, bool IsEnabled,
    bool IsOffscreen, bool IsPassword, bool? HasValuePattern, bool? IsReadOnly,
    bool? HasTextPattern, int NativeProcessId, long NativeTopLevelHwnd, long NativeFocusHwnd,
    bool? SameIdentityAsEvent, long EventAgeMs, long DurationMs, int ErrorCode);

public readonly record struct FocusProbeResult(int Rows, int DroppedEvents, bool WorkerStopped, int WriteError);

/// <summary>Read-only, bounded diagnostic experiment. Run in a disposable TestHost process.</summary>
public static class FocusComparisonProbe
{
    public static FocusProbeResult Run(string outputPath, TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(duration));
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        using var stream = new StreamWriter(new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        using var writer = new FocusProbeWriter(stream, 4096);
        // Retain these until the worker has stopped: COM providers may outlive the deadline.
        var pending = new BlockingCollection<(AutomationElement Element, long Timestamp)>(64);
        var stop = new ManualResetEventSlim();
        int dropped = 0;
        var worker = new Thread(() =>
        {
            AutomationFocusChangedEventHandler handler = (sender, _) =>
            {
                if (sender is AutomationElement element && !stop.IsSet &&
                    !pending.TryAdd((element, Stopwatch.GetTimestamp()))) Interlocked.Increment(ref dropped);
            };
            bool registered = false;
            long sequence = 0;
            try
            {
                Automation.AddAutomationFocusChangedEventHandler(handler);
                registered = true;
                while (!stop.IsSet && writer.Rows < 4096 && writer.WriteError == 0)
                {
                    if (pending.TryTake(out var notification, 250))
                    {
                        int[]? identity = Capture(notification.Element, FocusProbeSource.EventTarget,
                            notification.Timestamp, null, ++sequence, writer);
                        Capture(null, FocusProbeSource.CurrentAfterEvent, notification.Timestamp, identity, ++sequence, writer);
                    }
                    else Capture(null, FocusProbeSource.Poll, Stopwatch.GetTimestamp(), null, ++sequence, writer);
                }
            }
            catch (Exception error)
            {
                writer.TryWrite(new FocusProbeRow { Sequence = ++sequence, ObservedAtUtc = DateTimeOffset.UtcNow,
                    ErrorCode = error.HResult, ProcessId = -1 });
            }
            finally
            {
                if (registered)
                {
                    try { Automation.RemoveAutomationFocusChangedEventHandler(handler); }
                    catch (Exception error)
                    {
                        writer.TryWrite(new FocusProbeRow { Sequence = ++sequence, ObservedAtUtc = DateTimeOffset.UtcNow,
                            ErrorCode = error.HResult, ProcessId = -1 });
                    }
                }
            }
        }) { IsBackground = true, Name = "VirtualKeyboard.FocusComparisonProbe" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        bool stopped = worker.Join(duration);
        stop.Set();
        if (!stopped) stopped = worker.Join(TimeSpan.FromSeconds(1));
        if (stopped) { pending.Dispose(); stop.Dispose(); }
        return new(writer.Rows, Volatile.Read(ref dropped), stopped, writer.WriteError);
    }

    private static int[]? Capture(AutomationElement? element, FocusProbeSource source, long timestamp,
        int[]? eventIdentity, long sequence, FocusProbeWriter writer)
    {
        long start = Stopwatch.GetTimestamp();
        var row = new FocusProbeRow { Sequence = sequence, ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = source, ProcessId = -1, EventAgeMs = (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds };
        int[]? identity = null;
        try
        {
            var native = new NativeFocusAdapter().Capture();
            if (native.IsCaptured) row = row with { NativeProcessId = native.Snapshot!.ProcessId,
                NativeTopLevelHwnd = native.Snapshot.TopLevelHwnd.ToInt64(), NativeFocusHwnd = native.Snapshot.FocusHwnd.ToInt64() };
            element ??= AutomationElement.FocusedElement;
            if (element is not null)
            {
                var current = element.Current;
                row = row with { ProcessId = current.ProcessId, ControlTypeId = current.ControlType.Id,
                    HasKeyboardFocus = current.HasKeyboardFocus, IsEnabled = current.IsEnabled,
                    IsOffscreen = current.IsOffscreen, IsPassword = current.IsPassword };
                identity = element.GetRuntimeId();
                if (eventIdentity is not null && identity is not null)
                    row = row with { SameIdentityAsEvent = identity.AsSpan().SequenceEqual(eventIdentity) };
                if (!row.IsPassword)
                {
                    bool value = element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern);
                    row = row with { HasValuePattern = value,
                        IsReadOnly = value ? ((ValuePattern)pattern).Current.IsReadOnly : null,
                        HasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out _) };
                }
            }
        }
        catch (Exception error) { row = row with { ErrorCode = error.HResult }; }
        writer.TryWrite(row with { DurationMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds });
        return identity;
    }
}

internal sealed class FocusProbeWriter(TextWriter output, int maxRows) : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly object _gate = new();
    private bool _closed;
    private int _rows;
    private int _writeError;
    public int WriteError { get { lock (_gate) return _writeError; } }
    public int Rows { get { lock (_gate) return _rows; } }
    public bool TryWrite(FocusProbeRow row)
    {
        lock (_gate)
        {
            if (_closed || _rows >= maxRows) return false;
            try
            {
                output.WriteLine(JsonSerializer.Serialize(row, Options));
                output.Flush();
                _rows++;
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _writeError = error.HResult;
                _closed = true;
                return false;
            }
        }
    }
    public void Dispose() { lock (_gate) _closed = true; }
}
