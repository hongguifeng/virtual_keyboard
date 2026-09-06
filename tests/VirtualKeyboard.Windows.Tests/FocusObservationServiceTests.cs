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

    private sealed class FakeFocusAutomationSource : IFocusAutomationSource
    {
        private Action? _notification;

        public Exception? RegistrationError { get; init; }
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
            _notification = null;
        }

        public void Raise()
        {
            Action notification = Assert.IsType<Action>(_notification);
            notification();
        }
    }
}
