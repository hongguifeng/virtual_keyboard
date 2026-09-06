using System.Threading.Channels;

namespace VirtualKeyboard.Core.Input;

public enum InputActionKind
{
    Text,
    Key,
    Hotkey,
    Modifier,
}

public enum InputQueueStatus
{
    Completed,
    StaleSession,
    QueueFull,
    OperationFailed,
    ServiceStopped,
}

public readonly record struct QueuedInputResult(
    long ActionId,
    long SessionId,
    InputActionKind Kind,
    InputQueueStatus Status,
    InputSendResult? SendResult);

/// <summary>Runs bounded input work on one consumer and invalidates queued work from replaced target sessions.</summary>
public sealed class InputInjectionService : IDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private readonly Channel<QueuedAction> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _consumer;
    private long _currentSessionId;
    private long _lastActionId;
    private bool _disposed;

    public InputInjectionService(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _queue = Channel.CreateBounded<QueuedAction>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        _consumer = Task.Run(ConsumeAsync);
    }

    public long CurrentSessionId
    {
        get { lock (_gate) return _currentSessionId; }
    }

    public void SetCurrentSession(long sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _currentSessionId = sessionId;
        }
    }

    public Task<QueuedInputResult> EnqueueAsync(
        long sessionId,
        InputActionKind kind,
        Func<CancellationToken, ValueTask<InputSendResult>> operation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentNullException.ThrowIfNull(operation);
        long actionId = Interlocked.Increment(ref _lastActionId);
        var completion = new TaskCompletionSource<QueuedInputResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = new QueuedAction(actionId, sessionId, kind, operation, completion);
        lock (_gate)
        {
            if (_disposed)
            {
                completion.SetResult(Result(action, InputQueueStatus.ServiceStopped));
            }
            else if (_currentSessionId != sessionId)
            {
                completion.SetResult(Result(action, InputQueueStatus.StaleSession));
            }
            else if (!_queue.Writer.TryWrite(action))
            {
                completion.SetResult(Result(action, InputQueueStatus.QueueFull));
            }
        }

        return completion.Task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _currentSessionId = 0;
            _queue.Writer.TryComplete();
            _stop.Cancel();
        }

        try { _consumer.Wait(DisposeTimeout); }
        catch (AggregateException exception) when (exception.InnerExceptions.All(static item => item is TaskCanceledException)) { }
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (QueuedAction action in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        action.Completion.TrySetResult(Result(action, InputQueueStatus.ServiceStopped));
                        continue;
                    }

                    if (_currentSessionId != action.SessionId)
                    {
                        action.Completion.TrySetResult(Result(action, InputQueueStatus.StaleSession));
                        continue;
                    }
                }

                try
                {
                    InputSendResult sendResult = await action.Operation(_stop.Token).ConfigureAwait(false);
                    action.Completion.TrySetResult(Result(action, InputQueueStatus.Completed, sendResult));
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    action.Completion.TrySetResult(Result(action, InputQueueStatus.ServiceStopped));
                }
                catch
                {
                    action.Completion.TrySetResult(Result(action, InputQueueStatus.OperationFailed));
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            while (_queue.Reader.TryRead(out QueuedAction? pending))
                pending.Completion.TrySetResult(Result(pending, InputQueueStatus.ServiceStopped));
        }
    }

    private static QueuedInputResult Result(QueuedAction action, InputQueueStatus status, InputSendResult? sendResult = null) =>
        new(action.ActionId, action.SessionId, action.Kind, status, sendResult);

    private sealed record QueuedAction(
        long ActionId,
        long SessionId,
        InputActionKind Kind,
        Func<CancellationToken, ValueTask<InputSendResult>> Operation,
        TaskCompletionSource<QueuedInputResult> Completion);
}
