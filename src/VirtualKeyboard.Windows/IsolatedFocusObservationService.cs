using System.Diagnostics;
using System.IO;
using System.Text;

namespace VirtualKeyboard.Windows;

public enum FocusWorkerStatus { Started, Heartbeat, Stalled, Exited, Restarting, Recovered, Exhausted, BoundaryError, Rearmed }

public readonly record struct RemoteFocusNotification(FocusTargetEvaluation? Evaluation, int RetryAttempt,
    bool UsedEventTarget, long DurationMs, Func<bool> IsCurrent);

/// <summary>Supervises one headless worker at a time. A killed generation can never publish again.</summary>
public sealed class IsolatedFocusObservationService : IDisposable
{
    private readonly string _executable;
    private readonly Action<RemoteFocusNotification> _observer;
    private readonly Action<bool> _invalidate;
    private readonly Action<FocusWorkerStatus, int, int> _diagnostic;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _task;
    private Process? _child;
    private long _stamp;
    private long _version;
    private long _lastProgressTimestamp;
    private bool _disposed;
    private bool _terminationFailed;
    internal Func<ProcessStartInfo>? StartInfoFactory { get; init; }
    internal TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(5);
    internal TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(1);

    public IsolatedFocusObservationService(string executable, Action<RemoteFocusNotification> observer,
        Action<bool> invalidate, Action<FocusWorkerStatus, int, int> diagnostic)
    {
        _executable = executable;
        _observer = observer;
        _invalidate = invalidate;
        _diagnostic = diagnostic;
    }

    public bool IsRunning => _task is { IsCompleted: false };
    public bool IsStopped => !_disposed && _task is { IsCompleted: true };
    public bool CanRestart => IsStopped && !_terminationFailed;
    public bool IsHealthy => IsRunning && Volatile.Read(ref _lastProgressTimestamp) is long timestamp && timestamp != 0 &&
        Stopwatch.GetElapsedTime(timestamp) < TimeSpan.FromSeconds(1);

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Explicit user rearm only after the previous supervisor and owned child have stopped.
            // Keep version/stamp counters so no old notification can become authoritative again.
            if (_task is null || CanRestart)
            {
                if (_task is not null) _diagnostic(FocusWorkerStatus.Rearmed, 0, 0);
                _task = Task.Run(RunAsync);
            }
        }
    }

    private async Task RunAsync()
    {
        int failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            int stage = 0;
            int workerId = 0;
            var elapsed = Stopwatch.StartNew();
            try
            {
                var start = new ProcessStartInfo(_executable)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardInput = true,
                    WorkingDirectory = Path.GetDirectoryName(_executable)!,
                };
                start.ArgumentList.Add("--focus-worker");
                start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start = StartInfoFactory?.Invoke() ?? start;
                Process child;
                lock (_gate)
                {
                    if (_stop.IsCancellationRequested) return;
                    child = Process.Start(start) ?? throw new IOException("Worker start failed.");
                    _child = child;
                }
                _diagnostic(FocusWorkerStatus.Started, child.Id, failures);
                workerId = child.Id;
                var health = new FocusWorkerHealth(StallTimeout);
                long lastResult = -1;
                long lastEvent = -1;
                bool first = true;
                long nextHeartbeat = 30;
                bool recovered = false;
                using var reader = child.StandardOutput;
                while (!_stop.IsCancellationRequested)
                {
                    using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    readDeadline.CancelAfter(first ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(2));
                    string? line;
                    try { line = await ReadBoundedLineAsync(reader, readDeadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { throw new TimeoutException(); }
                    if (line is null) throw new EndOfStreamException();
                    var packet = FocusWorkerPacket.Parse(line);
                    if (first) { health = new FocusWorkerHealth(StallTimeout); elapsed.Restart(); first = false; }
                    stage = packet.Stage;
                    if (packet.ProgressTimestamp > Stopwatch.GetTimestamp() ||
                        (packet.ProgressTimestamp > 0 && Stopwatch.GetElapsedTime(packet.ProgressTimestamp) >= StallTimeout))
                        throw new TimeoutException();
                    if (health.Observe(packet.Progress, elapsed.Elapsed))
                        Volatile.Write(ref _lastProgressTimestamp, packet.ProgressTimestamp);
                    if (health.IsExpired(elapsed.Elapsed)) throw new TimeoutException();
                    if (packet.EventVersion != lastEvent)
                    {
                        Interlocked.Increment(ref _stamp);
                        _invalidate(false);
                        lastEvent = packet.EventVersion;
                    }
                    if (packet.ResultSequence != lastResult &&
                        (packet.Result is null || packet.Result.EventVersion == packet.EventVersion))
                    {
                        long stamp = Interlocked.Increment(ref _stamp);
                        lastResult = packet.ResultSequence;
                        var result = packet.Result;
                        _observer(new(result?.ToEvaluation(Interlocked.Increment(ref _version)), result?.RetryAttempt ?? 0,
                            result?.UsedEventTarget ?? false, result?.DurationMs ?? 0,
                            () => !_stop.IsCancellationRequested && stamp == Volatile.Read(ref _stamp)));
                    }
                    if (packet.ErrorCode != 0) throw new IOException("Worker observation failed.");
                    if (elapsed.Elapsed.TotalSeconds >= nextHeartbeat)
                    {
                        _diagnostic(FocusWorkerStatus.Heartbeat, child.Id, stage);
                        nextHeartbeat += 30;
                    }
                    if (!recovered && packet.Progress > 1)
                    {
                        recovered = true;
                        if (failures > 0) _diagnostic(FocusWorkerStatus.Recovered, child.Id, failures);
                    }
                    // Reset the consecutive failure budget only after sustained healthy work.
                    if (elapsed.Elapsed >= TimeSpan.FromSeconds(30)) failures = 0;
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (TimeoutException) { _diagnostic(FocusWorkerStatus.Stalled, workerId, stage); }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or
                System.ComponentModel.Win32Exception or InvalidOperationException)
            { _diagnostic(FocusWorkerStatus.Exited, 0, exception.HResult & 0xffff); }
            finally
            {
                Interlocked.Increment(ref _stamp);
                Volatile.Write(ref _lastProgressTimestamp, 0);
                try { _invalidate(true); }
                finally { StopChild(); }
            }
            if (_stop.IsCancellationRequested) return;
            if (_terminationFailed) { _diagnostic(FocusWorkerStatus.Exhausted, 0, failures); return; }
            if (++failures > 3) { _diagnostic(FocusWorkerStatus.Exhausted, 0, failures); return; }
            _diagnostic(FocusWorkerStatus.Restarting, 0, failures);
            try { await Task.Delay(RestartDelay * (1 << (failures - 1)), _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return line.ToString();
            if (line.Length >= FocusWorkerPacket.MaximumLineLength) throw new InvalidDataException("Oversized focus packet.");
            line.Append(character[0]);
        }
        return line.Length == 0 ? null : throw new EndOfStreamException();
    }

    private void StopChild()
    {
        lock (_gate)
        {
            if (_child is null) return;
            try
            {
                // Only terminate the exact process we started. Never kill other applications/providers.
                if (!_child.HasExited) _child.Kill();
                if (!_child.WaitForExit(2000)) _terminationFailed = true;
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception exception)
            { _terminationFailed = true; _diagnostic(FocusWorkerStatus.BoundaryError, 0, exception.NativeErrorCode); }
            finally { _child.Dispose(); _child = null; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        Interlocked.Increment(ref _stamp);
        StopChild();
        // No UI-thread join: observers may have posted Dispatcher work. Cancellation unwinds the task.
        if (_task is null) _stop.Dispose();
        else _ = _task.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
