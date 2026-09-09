using System.Windows.Automation;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Runs UI Automation focus observation and consumer notifications on a dedicated MTA thread.</summary>
public sealed class FocusObservationService : IDisposable
{
    private const int LifecycleTimeoutMilliseconds = 5_000;
    private const int PendingFocusCapacity = 256;
    internal const int FocusStabilityMilliseconds = 50;
    internal const int FocusPollingMilliseconds = 250;
    /// <summary>Additional evaluations per focus/event, spaced by the polling interval.</summary>
    public const int MaxEvaluationRetries = 4;

    private readonly IFocusAutomationSource _source;
    private readonly IFocusSnapshotSource _snapshotSource;
    private readonly Action<FocusChangedNotification>? _observer;
    private readonly Action<int>? _errorObserver;
    private readonly Func<FocusChangedNotification, bool>? _evaluate;
    private readonly object _gate = new();
    private readonly ManualResetEvent _started = new(false);
    private readonly ManualResetEvent _stopRequested = new(false);
    private readonly SemaphoreSlim _focusPending = new(0, PendingFocusCapacity);
    private Thread? _thread;
    private Exception? _startupError;
    private bool _disposed;

    /// <param name="observer">Optional notification consumer on the MTA thread.</param>
    /// <param name="errorObserver">Receives numeric boundary failures.</param>
    /// <param name="evaluate">Return true to request a bounded retry; never injects input.</param>
    public FocusObservationService(Action<FocusChangedNotification>? observer = null, Action<int>? errorObserver = null, Func<FocusChangedNotification, bool>? evaluate = null)
        : this(
            new SystemFocusAutomationSource(),
            new SystemFocusSnapshotSource(new FocusSnapshotFactory(Environment.ProcessId)),
            observer,
            errorObserver, evaluate)
    {
    }

    internal FocusObservationService(IFocusAutomationSource source, Action<FocusChangedNotification>? observer = null)
        : this(source, NullFocusSnapshotSource.Instance, observer, null)
    {
    }

    internal FocusObservationService(
        IFocusAutomationSource source,
        IFocusSnapshotSource snapshotSource,
        Action<FocusChangedNotification>? observer = null,
        Action<int>? errorObserver = null, Func<FocusChangedNotification, bool>? evaluate = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
        _observer = observer;
        _errorObserver = errorObserver;
        _evaluate = evaluate;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is { IsAlive: true } && !_stopRequested.WaitOne(0);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is { IsAlive: true })
            {
                return;
            }

            _startupError = null;
            _started.Reset();
            _stopRequested.Reset();
            while (_focusPending.Wait(0))
            {
            }

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "VirtualKeyboard.UIA.FocusObserver",
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        if (!_started.WaitOne(LifecycleTimeoutMilliseconds))
        {
            _stopRequested.Set();
            throw new TimeoutException("UI Automation focus observation did not start within the lifecycle timeout.");
        }

        if (_startupError is not null)
        {
            throw new InvalidOperationException("UI Automation focus observation could not start.", _startupError);
        }
    }

    /// <summary>Requests one bounded evaluation of the currently focused element.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not { IsAlive: true }) throw new InvalidOperationException("Focus observation is not running.");
            try { _focusPending.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    /// <summary>Requests shutdown and returns false only if the observer thread misses the bounded timeout.</summary>
    public bool Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null)
            {
                return true;
            }

            _stopRequested.Set();
        }

        return thread != Thread.CurrentThread && thread.Join(LifecycleTimeoutMilliseconds);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (Stop())
        {
            _started.Dispose();
            _stopRequested.Dispose();
            _focusPending.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        bool registered = false;
        try
        {
            _source.Register(OnSourceFocusChanged);
            registered = true;
        }
        catch (Exception exception)
        {
            _startupError = exception;
        }
        finally
        {
            _started.Set();
        }

        try
        {
            if (!registered)
            {
                return;
            }

            FocusSnapshot? lastPolledSnapshot = null;
            bool retryPending = false;
            int retryAttempt = 0;
            WaitHandle[] handles = [_stopRequested, _focusPending.AvailableWaitHandle];
            while (true)
            {
                int waitResult = WaitHandle.WaitAny(handles, FocusPollingMilliseconds);
                if (waitResult == 0)
                {
                    break;
                }

                bool triggeredByEvent = waitResult == 1;
                if (triggeredByEvent)
                {
                    if (!_focusPending.Wait(0))
                    {
                        continue;
                    }

                    bool stop = false;
                    while (true)
                    {
                        int stableWait = WaitHandle.WaitAny(handles, FocusStabilityMilliseconds);
                        if (stableWait == 0)
                        {
                            stop = true;
                            break;
                        }

                        if (stableWait == WaitHandle.WaitTimeout)
                        {
                            break;
                        }

                        while (_focusPending.Wait(0))
                        {
                        }
                    }

                    if (stop)
                    {
                        break;
                    }
                }

                FocusSnapshot? snapshot;
                try
                {
                    snapshot = _snapshotSource.Capture();
                }
                catch (Exception exception)
                {
                    try { _errorObserver?.Invoke(exception.HResult & 0xFFFF); }
                    catch { }
                    continue;
                }

                bool sameFocus = SameFocus(snapshot, lastPolledSnapshot);
                if (!triggeredByEvent && sameFocus && !retryPending)
                {
                    continue;
                }

                if (triggeredByEvent || !sameFocus) retryAttempt = 0;
                else if (retryPending) retryAttempt++;
                lastPolledSnapshot = snapshot;
                retryPending = false;
                try
                {
                    var notification = new FocusChangedNotification(
                        DateTimeOffset.UtcNow, Environment.CurrentManagedThreadId, snapshot, retryAttempt);
                    _observer?.Invoke(notification);
                    bool needsRetry = _evaluate?.Invoke(notification) == true;
                    retryPending = needsRetry && retryAttempt < MaxEvaluationRetries;
                }
                catch (Exception exception)
                {
                    retryPending = retryAttempt < MaxEvaluationRetries;
                    // Numeric ranges distinguish consumer failures without recording exception text.
                    try { _errorObserver?.Invoke(100_000 + (exception.HResult & 0xFFFF)); }
                    catch { }
                }
            }
        }
        finally
        {
            if (registered)
            {
                try
                {
                    _source.Unregister(OnSourceFocusChanged);
                }
                catch
                {
                    // Teardown failures must not escape the observer thread.
                }
            }

            lock (_gate)
            {
                _thread = null;
            }
        }
    }

    private void OnSourceFocusChanged()
    {
        try
        {
            _focusPending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Bounded backpressure: a burst cannot grow pending work beyond the fixed capacity.
        }
        catch (ObjectDisposedException)
        {
            // A late native callback may race with service disposal.
        }
    }

    private static bool SameFocus(FocusSnapshot? left, FocusSnapshot? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.ProcessId == right.ProcessId &&
            left.TopLevelHwnd == right.TopLevelHwnd &&
            Equals(left.RuntimeId, right.RuntimeId) &&
            left.ControlType == right.ControlType &&
            left.HasKeyboardFocus == right.HasKeyboardFocus &&
            left.IsEnabled == right.IsEnabled &&
            left.IsOffscreen == right.IsOffscreen &&
            left.IsPassword == right.IsPassword;
    }
}

public readonly record struct FocusChangedNotification(
    DateTimeOffset OccurredAtUtc,
    int ObserverThreadId,
    FocusSnapshot? Snapshot,
    int RetryAttempt = 0);

internal interface IFocusAutomationSource
{
    void Register(Action notification);

    void Unregister(Action notification);
}

internal sealed class SystemFocusAutomationSource : IFocusAutomationSource
{
    private AutomationFocusChangedEventHandler? _handler;

    public void Register(Action notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _handler = (_, _) => notification();
        Automation.AddAutomationFocusChangedEventHandler(_handler);
    }

    public void Unregister(Action notification)
    {
        _ = notification;
        if (_handler is not null)
        {
            Automation.RemoveAutomationFocusChangedEventHandler(_handler);
            _handler = null;
        }
    }
}

internal interface IFocusSnapshotSource
{
    FocusSnapshot? Capture();
}

internal sealed class SystemFocusSnapshotSource(FocusSnapshotFactory factory) : IFocusSnapshotSource
{
    public FocusSnapshot? Capture()
    {
        AutomationElement? element = FocusedElementResolver.Capture();
        return element is not null && factory.TryCreate(element, out FocusSnapshot? snapshot) ? snapshot : null;
    }
}

internal sealed class NullFocusSnapshotSource : IFocusSnapshotSource
{
    public static NullFocusSnapshotSource Instance { get; } = new();

    public FocusSnapshot? Capture() => null;
}
