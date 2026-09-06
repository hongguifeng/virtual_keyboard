using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class FocusObservationServiceTests
{
    [Fact]
    public void StartAndStopKeepRegistrationAndNotificationsOnDedicatedMtaThread()
    {
        var source = new FakeFocusAutomationSource();
        using var observed = new ManualResetEventSlim(false);
        FocusChangedNotification notification = default;
        ApartmentState observerApartment = ApartmentState.Unknown;
        using var service = new FocusObservationService(source, value =>
        {
            notification = value;
            observerApartment = Thread.CurrentThread.GetApartmentState();
            observed.Set();
        });

        service.Start();
        Assert.True(service.IsRunning);
        source.Raise();

        Assert.True(observed.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(ApartmentState.MTA, source.RegisterApartment);
        Assert.Equal(ApartmentState.MTA, observerApartment);
        Assert.Equal(source.RegisterThreadId, notification.ObserverThreadId);
        Assert.True(service.Stop());
        Assert.False(service.IsRunning);
        Assert.Equal(source.RegisterThreadId, source.UnregisterThreadId);
    }

    [Fact]
    public void ObserverExceptionDoesNotStopFollowingNotifications()
    {
        var source = new FakeFocusAutomationSource();
        var calls = 0;
        using var firstObserved = new ManualResetEventSlim(false);
        using var secondObserved = new ManualResetEventSlim(false);
        using var service = new FocusObservationService(source, _ =>
        {
            int call = Interlocked.Increment(ref calls);
            (call == 1 ? firstObserved : secondObserved).Set();
            throw new InvalidOperationException("synthetic observer failure");
        });
        service.Start();

        source.Raise();
        Assert.True(firstObserved.Wait(TimeSpan.FromSeconds(2)));
        source.Raise();
        Assert.True(secondObserved.Wait(TimeSpan.FromSeconds(2)));

        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.True(service.IsRunning);
    }

    [Fact]
    public void LifecycleCallsAreIdempotentAndServiceCanRestart()
    {
        var source = new FakeFocusAutomationSource();
        using var service = new FocusObservationService(source);

        service.Start();
        service.Start();
        Assert.Equal(1, source.RegisterCount);
        Assert.True(service.Stop());
        Assert.True(service.Stop());

        service.Start();
        Assert.Equal(2, source.RegisterCount);
        Assert.True(service.Stop());
        Assert.Equal(2, source.UnregisterCount);
    }

    [Fact]
    public void RegistrationFailureIsReportedWithoutLeavingAWorkerRunning()
    {
        var source = new FakeFocusAutomationSource { RegistrationError = new InvalidOperationException("synthetic") };
        using var service = new FocusObservationService(source);

        var error = Assert.Throws<InvalidOperationException>(service.Start);

        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.False(service.IsRunning);
        Assert.True(service.Stop());
        Assert.Equal(0, source.UnregisterCount);
    }

    [Fact]
    public void LateNativeCallbackAfterDisposeIsIgnored()
    {
        var source = new FakeFocusAutomationSource { KeepNotificationAfterUnregister = true };
        var service = new FocusObservationService(source);
        service.Start();
        service.Dispose();

        Assert.Null(Record.Exception(source.Raise));
    }

    [Fact]
    public void SnapshotIsCapturedAndPublishedOnObserverMtaThread()
    {
        var source = new FakeFocusAutomationSource();
        var expected = new FocusSnapshot(
            7,
            DateTimeOffset.UtcNow,
            42,
            (nint)100,
            new RuntimeIdentity([1, 2]),
            FocusControlType.Edit,
            true,
            true,
            false,
            false);
        var snapshots = new StubFocusSnapshotSource(expected);
        using var observed = new ManualResetEventSlim(false);
        FocusChangedNotification notification = default;
        using var service = new FocusObservationService(source, snapshots, value =>
        {
            notification = value;
            observed.Set();
        });
        service.Start();

        source.Raise();

        Assert.True(observed.Wait(TimeSpan.FromSeconds(2)));
        Assert.Same(expected, notification.Snapshot);
        Assert.Equal(source.RegisterThreadId, snapshots.CaptureThreadId);
        Assert.Equal(ApartmentState.MTA, snapshots.CaptureApartment);
    }

    [Fact]
    public void BurstIsCoalescedIntoOneNotificationAfterStabilityWindow()
    {
        const int signalCount = 64;
        var source = new FakeFocusAutomationSource();
        using var observed = new ManualResetEventSlim(false);
        int notificationCount = 0;
        using var service = new FocusObservationService(source, _ =>
        {
            Interlocked.Increment(ref notificationCount);
            observed.Set();
        });
        service.Start();

        for (int index = 0; index < signalCount; index++)
        {
            source.Raise();
        }

        Assert.True(observed.Wait(TimeSpan.FromSeconds(2)));
        Thread.Sleep(FocusObservationService.FocusStabilityMilliseconds * 2);
        Assert.Equal(1, Volatile.Read(ref notificationCount));
    }

    [Fact]
    public void StabilityWindowIsFiftyMilliseconds() =>
        Assert.Equal(50, FocusObservationService.FocusStabilityMilliseconds);

    [Fact]
    public void RefreshRequestsCurrentFocusEvaluationWithoutNativeEvent()
    {
        var source = new FakeFocusAutomationSource();
        using var observed = new ManualResetEventSlim(false);
        using var service = new FocusObservationService(source, _ => observed.Set());

        Assert.Throws<InvalidOperationException>(service.Refresh);
        service.Start();
        service.Refresh();

        Assert.True(observed.Wait(TimeSpan.FromSeconds(2)));
    }

    private sealed class FakeFocusAutomationSource : IFocusAutomationSource
    {
        private Action? _notification;

        public Exception? RegistrationError { get; init; }
        public bool KeepNotificationAfterUnregister { get; init; }
        public int RegisterCount { get; private set; }
        public int UnregisterCount { get; private set; }
        public int RegisterThreadId { get; private set; }
        public int UnregisterThreadId { get; private set; }
        public ApartmentState RegisterApartment { get; private set; }

        public void Register(Action notification)
        {
            RegisterCount++;
            RegisterThreadId = Environment.CurrentManagedThreadId;
            RegisterApartment = Thread.CurrentThread.GetApartmentState();
            if (RegistrationError is not null)
            {
                throw RegistrationError;
            }

            _notification = notification;
        }

        public void Unregister(Action notification)
        {
            Assert.Equal(_notification, notification);
            UnregisterCount++;
            UnregisterThreadId = Environment.CurrentManagedThreadId;
            if (!KeepNotificationAfterUnregister)
            {
                _notification = null;
            }
        }

        public void Raise()
        {
            Action notification = Assert.IsType<Action>(_notification);
            notification();
        }
    }

    private sealed class StubFocusSnapshotSource(FocusSnapshot snapshot) : IFocusSnapshotSource
    {
        public int CaptureThreadId { get; private set; }
        public ApartmentState CaptureApartment { get; private set; }

        public FocusSnapshot Capture()
        {
            CaptureThreadId = Environment.CurrentManagedThreadId;
            CaptureApartment = Thread.CurrentThread.GetApartmentState();
            return snapshot;
        }
    }
}
