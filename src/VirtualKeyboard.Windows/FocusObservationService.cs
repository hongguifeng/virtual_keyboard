using System.Windows.Automation;

namespace VirtualKeyboard.Windows;

/// <summary>Runs UI Automation focus observation and consumer notifications on a dedicated MTA thread.</summary>
public sealed class FocusObservationService : IDisposable
{
    private const int LifecycleTimeoutMilliseconds = 5_000;

    private readonly IFocusAutomationSource _source;
    private readonly Action<FocusChangedNotification>? _observer;
    private readonly object _gate = new();
    private readonly ManualResetEvent _started = new(false);
    private readonly ManualResetEvent _stopRequested = new(false);
    private readonly AutoResetEvent _focusPending = new(false);
    private Thread? _thread;
    private Exception? _startupError;
    private bool _disposed;

    public FocusObservationService(Action<FocusChangedNotification>? observer = null)
        : this(new SystemFocusAutomationSource(), observer)
    {
    }

    internal FocusObservationService(IFocusAutomationSource source, Action<FocusChangedNotification>? observer = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _observer = observer;
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
            while (_focusPending.WaitOne(0))
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

            WaitHandle[] handles = [_stopRequested, _focusPending];
            while (WaitHandle.WaitAny(handles) == 1)
            {
                try
                {
                    _observer?.Invoke(new FocusChangedNotification(
                        DateTimeOffset.UtcNow,
                        Environment.CurrentManagedThreadId));
                }
                catch
                {
                    // Consumer failures are isolated from the UIA event thread.
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

    private void OnSourceFocusChanged() => _focusPending.Set();
}

public readonly record struct FocusChangedNotification(DateTimeOffset OccurredAtUtc, int ObserverThreadId);

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
