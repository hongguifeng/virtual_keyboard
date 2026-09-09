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
    public void PollingPublishesFocusWhenProviderDoesNotRaiseNativeEvent()
    {
        var source = new FakeFocusAutomationSource();
        var expected = Snapshot(version: 1, runtimeId: 10);
        var snapshots = new StubFocusSnapshotSource(expected);
        using var observed = new ManualResetEventSlim(false);
        FocusChangedNotification notification = default;
        using var service = new FocusObservationService(source, snapshots, value =>
        {
            notification = value;
            observed.Set();
        });

        service.Start();

        Assert.True(observed.Wait(TimeSpan.FromSeconds(2)));
        Assert.Same(expected, notification.Snapshot);
        Assert.Equal(250, FocusObservationService.FocusPollingMilliseconds);
    }

    [Fact]
    public void PollingSuppressesEquivalentFocusSnapshots()
    {
        var source = new FakeFocusAutomationSource();
        var snapshots = new StubFocusSnapshotSource(Snapshot(version: 1, runtimeId: 10));
        using var firstObserved = new ManualResetEventSlim(false);
        int notificationCount = 0;
        using var service = new FocusObservationService(source, snapshots, _ =>
        {
            Interlocked.Increment(ref notificationCount);
            firstObserved.Set();
        });
        service.Start();
        Assert.True(firstObserved.Wait(TimeSpan.FromSeconds(2)));

        snapshots.Current = Snapshot(version: 2, runtimeId: 10);
        Thread.Sleep(FocusObservationService.FocusPollingMilliseconds * 2);

        Assert.Equal(1, Volatile.Read(ref notificationCount));
    }

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

    [Fact]
    public void FailedEvaluationRetriesSameFocusThenStopsAfterRecovery()
    {
        var source = new FakeFocusAutomationSource();
        var snapshots = new StubFocusSnapshotSource(Snapshot(1, 10));
        using var recovered = new ManualResetEventSlim(false);
        int calls = 0;
        using var service = new FocusObservationService(source, snapshots, evaluate: notification =>
        {
            if (Interlocked.Increment(ref calls) == 1) return true;
            recovered.Set();
            return false;
        });
        service.Start();
        Assert.True(recovered.Wait(TimeSpan.FromSeconds(3)));
        Thread.Sleep(FocusObservationService.FocusPollingMilliseconds * 2);
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public void PersistentFailureHasBoundedRetriesAndNewFocusCanRecover()
    {
        var source = new FakeFocusAutomationSource();
        var snapshots = new StubFocusSnapshotSource(Snapshot(1, 10));
        using var exhausted = new ManualResetEventSlim(false);
        using var recovered = new ManualResetEventSlim(false);
        int calls = 0;
        using var service = new FocusObservationService(source, snapshots, evaluate: notification =>
        {
            if (notification.Snapshot!.RuntimeId!.Equals(new RuntimeIdentity([20])))
            {
                recovered.Set();
                return false;
            }
            if (Interlocked.Increment(ref calls) == FocusObservationService.MaxEvaluationRetries + 1)
                exhausted.Set();
            return true;
        });
        service.Start();
        Assert.True(exhausted.Wait(TimeSpan.FromSeconds(4)));
        Thread.Sleep(FocusObservationService.FocusPollingMilliseconds * 2);
        Assert.Equal(FocusObservationService.MaxEvaluationRetries + 1, Volatile.Read(ref calls));
        snapshots.Current = Snapshot(2, 20);
        Assert.True(recovered.Wait(TimeSpan.FromSeconds(2)));
    }
    private static FocusSnapshot Snapshot(long version, int runtimeId) => new(
        version,
        DateTimeOffset.UtcNow,
        42,
        (nint)100,
        new RuntimeIdentity([runtimeId]),
        FocusControlType.Edit,
        true,
        true,
        false,
        false);

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
        public FocusSnapshot Current { get; set; } = snapshot;
        public int CaptureThreadId { get; private set; }
        public ApartmentState CaptureApartment { get; private set; }

        public FocusSnapshot Capture()
        {
            CaptureThreadId = Environment.CurrentManagedThreadId;
            CaptureApartment = Thread.CurrentThread.GetApartmentState();
            return Current;
        }
    }
}
