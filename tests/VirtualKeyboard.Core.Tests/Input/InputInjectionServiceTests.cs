using VirtualKeyboard.Core.Input;

namespace VirtualKeyboard.Core.Tests.Input;

public sealed class InputInjectionServiceTests
{
    private static readonly InputSendResult Success = new(InputSendStatus.Succeeded, 2, 2, 0);

    [Fact]
    public async Task ConcurrentProducersExecuteStrictlySeriallyWithMonotonicIds()
    {
        using var service = new InputInjectionService();
        service.SetCurrentSession(7);
        int running = 0;
        int maximumRunning = 0;
        var order = new List<int>();

        Task<QueuedInputResult>[] tasks = Enumerable.Range(1, 10).Select(index =>
            service.EnqueueAsync(7, InputActionKind.Key, async cancellation =>
            {
                int now = Interlocked.Increment(ref running);
                maximumRunning = Math.Max(maximumRunning, now);
                lock (order) order.Add(index);
                await Task.Delay(5, cancellation);
                Interlocked.Decrement(ref running);
                return Success;
            })).ToArray();

        QueuedInputResult[] results = await Task.WhenAll(tasks);
        Assert.Equal(1, maximumRunning);
        Assert.Equal(Enumerable.Range(1, 10), order);
        Assert.Equal(Enumerable.Range(1, 10).Select(value => (long)value), results.Select(result => result.ActionId));
        Assert.All(results, result => Assert.Equal(InputQueueStatus.Completed, result.Status));
    }

    [Fact]
    public async Task ReplacingSessionCancelsOldActionsThatHaveNotStarted()
    {
        using var service = new InputInjectionService();
        service.SetCurrentSession(1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<QueuedInputResult> first = service.EnqueueAsync(1, InputActionKind.Key, async _ =>
        {
            started.SetResult();
            await release.Task;
            return Success;
        });
        await started.Task;
        Task<QueuedInputResult> pending = service.EnqueueAsync(1, InputActionKind.Key, _ => ValueTask.FromResult(Success));

        service.SetCurrentSession(2);
        release.SetResult();

        Assert.Equal(InputQueueStatus.Completed, (await first).Status);
        Assert.Equal(InputQueueStatus.StaleSession, (await pending).Status);
    }

    [Fact]
    public async Task FullQueueFailsClosedWithoutExecutingDroppedAction()
    {
        using var service = new InputInjectionService(1);
        service.SetCurrentSession(1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<QueuedInputResult> first = service.EnqueueAsync(1, InputActionKind.Key, async _ =>
        {
            started.SetResult();
            await release.Task;
            return Success;
        });
        await started.Task;
        Task<QueuedInputResult> second = service.EnqueueAsync(1, InputActionKind.Key, _ => ValueTask.FromResult(Success));
        int thirdCalls = 0;
        Task<QueuedInputResult> third = service.EnqueueAsync(1, InputActionKind.Key, _ =>
        {
            Interlocked.Increment(ref thirdCalls);
            return ValueTask.FromResult(Success);
        });

        Assert.Equal(InputQueueStatus.QueueFull, (await third).Status);
        Assert.Equal(0, thirdCalls);
        release.SetResult();
        await first;
        await second;
    }

    [Fact]
    public async Task StaleSessionAndOperationFailureHaveClosedResults()
    {
        using var service = new InputInjectionService();
        service.SetCurrentSession(2);
        QueuedInputResult stale = await service.EnqueueAsync(1, InputActionKind.Text, _ => ValueTask.FromResult(Success));
        QueuedInputResult failed = await service.EnqueueAsync(2, InputActionKind.Hotkey, _ => throw new InvalidOperationException("synthetic"));
        Assert.Equal(InputQueueStatus.StaleSession, stale.Status);
        Assert.Equal(InputQueueStatus.OperationFailed, failed.Status);
        Assert.Null(failed.SendResult);
    }

    [Fact]
    public async Task DisposeCancelsRunningAndPendingWork()
    {
        var service = new InputInjectionService();
        service.SetCurrentSession(1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<QueuedInputResult> running = service.EnqueueAsync(1, InputActionKind.Modifier, async cancellation =>
        {
            started.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
            return Success;
        });
        await started.Task;
        Task<QueuedInputResult> pending = service.EnqueueAsync(1, InputActionKind.Key, _ => ValueTask.FromResult(Success));

        service.Dispose();

        Assert.Equal(InputQueueStatus.ServiceStopped, (await running).Status);
        Assert.Equal(InputQueueStatus.ServiceStopped, (await pending).Status);
    }
}
