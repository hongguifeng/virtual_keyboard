using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class FocusSnapshotFactoryTests
{
    [Fact]
    public void ComboBoxMetadataRemainsDistinctFromEditAndOther()
    {
        Assert.Equal(FocusControlType.ComboBox, FocusSnapshotFactory.MapControlType(ControlType.ComboBox));
        Assert.Equal(FocusControlType.Edit, FocusSnapshotFactory.MapControlType(ControlType.Edit));
        Assert.Equal(FocusControlType.Other, FocusSnapshotFactory.MapControlType(ControlType.ListItem));
    }

    [Fact]
    public void FactoryReadsAllowListedMetadataFromRealAutomationElement()
    {
        RunOnStaThread(() =>
        {
            using var window = new TestWindow();
            nint hwnd = new WindowInteropHelper(window).EnsureHandle();
            AutomationElement element = AutomationElement.FromHandle(hwnd);
            var observedAt = new DateTimeOffset(2026, 9, 6, 2, 3, 4, TimeSpan.Zero);
            var factory = new FocusSnapshotFactory(int.MaxValue, () => observedAt);

            Assert.True(factory.TryCreate(element, out FocusSnapshot? snapshot));

            Assert.NotNull(snapshot);
            Assert.Equal(1, snapshot.Version);
            Assert.Equal(observedAt, snapshot.ObservedAt);
            Assert.Equal(Environment.ProcessId, snapshot.ProcessId);
            Assert.Equal(hwnd, snapshot.TopLevelHwnd);
            Assert.Equal(FocusControlType.Window, snapshot.ControlType);
            Assert.NotNull(snapshot.RuntimeId);
        });
    }

    [Fact]
    public void FactoryIgnoresElementFromOwnProcess()
    {
        RunOnStaThread(() =>
        {
            using var window = new TestWindow();
            nint hwnd = new WindowInteropHelper(window).EnsureHandle();
            AutomationElement element = AutomationElement.FromHandle(hwnd);
            var factory = new FocusSnapshotFactory(Environment.ProcessId);

            Assert.False(factory.TryCreate(element, out FocusSnapshot? snapshot));
            Assert.Null(snapshot);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class TestWindow : Window, IDisposable
    {
        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
}
