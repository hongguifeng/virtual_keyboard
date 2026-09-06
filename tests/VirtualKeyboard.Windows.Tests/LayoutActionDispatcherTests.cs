using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Core.Layouts;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class LayoutActionDispatcherTests
{
    [Fact]
    public void StandardKeyUsesKeyPathWithValidatedTarget()
    {
        using Fixture fixture = Fixture.Create();

        InputSendResult result = fixture.Dispatch(Key("a", new(LayoutActionTypes.Key, virtualKey: "A")));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.KeyCalls);
        Assert.Equal(WindowsKeyboardKey.A, fixture.LastKey);
        Assert.Equal((nint)101, fixture.LastFocusHwnd);
        Assert.Equal(42, fixture.LastProcessId);
        Assert.Equal(0, fixture.HotkeyCalls);
    }

    [Fact]
    public void LatchedModifiersUseHotkeyPathAndAreConsumed()
    {
        using Fixture fixture = Fixture.Create();
        Assert.True(fixture.Dispatch(Key("shift", new(LayoutActionTypes.Modifier, modifier: "Shift"))).IsSuccess);
        Assert.True(fixture.Dispatch(Key("control", new(LayoutActionTypes.Modifier, modifier: "Control"))).IsSuccess);

        InputSendResult result = fixture.Dispatch(Key("a", new(LayoutActionTypes.Key, virtualKey: "A")));

        Assert.True(result.IsSuccess);
        Assert.Equal([HotkeyModifier.Shift, HotkeyModifier.Control], fixture.LastModifiers);
        Assert.False(fixture.Controller.State.ShiftLatched);
        Assert.False(fixture.Controller.State.ControlLatched);
        Assert.Equal(1, fixture.HotkeyCalls);
    }

    [Fact]
    public void DeclaredHotkeyAndLatchAreMergedWithoutDuplicates()
    {
        using Fixture fixture = Fixture.Create();
        fixture.Dispatch(Key("control", new(LayoutActionTypes.Modifier, modifier: "Control")));
        var action = new LayoutActionDefinition(
            LayoutActionTypes.Hotkey,
            virtualKey: "A",
            modifiers: ["Control", "Shift"]);

        Assert.True(fixture.Dispatch(Key("select-all", action)).IsSuccess);
        Assert.Equal([HotkeyModifier.Control, HotkeyModifier.Shift], fixture.LastModifiers);
    }

    [Fact]
    public void TextUsesDedicatedUnicodePath()
    {
        using Fixture fixture = Fixture.Create();

        Assert.True(fixture.Dispatch(Key("text", new(LayoutActionTypes.Text, value: "你好"))).IsSuccess);

        Assert.Equal(1, fixture.TextCalls);
        Assert.Equal("你好", fixture.LastText);
        Assert.Equal(0, fixture.KeyCalls);
        Assert.Equal(0, fixture.HotkeyCalls);
    }

    [Fact]
    public void CapsLockUsesValidatedTogglePath()
    {
        using Fixture fixture = Fixture.Create();

        Assert.True(fixture.Dispatch(Key("caps", new(LayoutActionTypes.Modifier, modifier: "CapsLock"))).IsSuccess);

        Assert.Equal(fixture.Session.SessionId, fixture.LastCapsSessionId);
    }

    [Fact]
    public void ReplacedSessionFailsBeforeAnySenderOrStateConsumption()
    {
        using Fixture fixture = Fixture.Create();
        fixture.Controller.ToggleModifier(KeyboardModifier.Shift);

        InputSendResult result = fixture.Dispatcher.Dispatch(fixture.Session.SessionId + 1, Key("a", new(LayoutActionTypes.Key, virtualKey: "A")));

        Assert.Equal(InputSendStatus.TargetInvalid, result.Status);
        Assert.True(fixture.Controller.State.ShiftLatched);
        Assert.Equal(0, fixture.TotalSendCalls);
    }

    [Fact]
    public void PasswordTargetRejectsHotkeyEvenWhenLayoutMarksItSafe()
    {
        using Fixture fixture = Fixture.Create(isPassword: true);
        var hotkey = Key("copy", new(LayoutActionTypes.Hotkey, virtualKey: "C", modifiers: ["Control"]));

        InputSendResult result = fixture.Dispatch(hotkey);

        Assert.Equal(InputSendStatus.InvalidInput, result.Status);
        Assert.Equal(0, fixture.TotalSendCalls);
    }

    [Fact]
    public void UnsafeFlagOnlyBlocksPasswordTargets()
    {
        using Fixture normal = Fixture.Create();
        using Fixture password = Fixture.Create(isPassword: true);
        KeyViewModel key = Key("phrase", new(LayoutActionTypes.Text, value: "phrase"), safe: false);

        Assert.True(normal.Dispatch(key).IsSuccess);
        Assert.Equal(InputSendStatus.InvalidInput, password.Dispatch(key).Status);
    }

    private static KeyViewModel Key(string id, LayoutActionDefinition action, bool safe = true) =>
        new(id, id, 1, safe, action);

    private sealed class Fixture : IDisposable
    {
        private Fixture(TargetSession session, KeyboardController controller, LayoutActionDispatcher dispatcher, FakeCapsLock capsLock)
        {
            Session = session;
            Controller = controller;
            Dispatcher = dispatcher;
            CapsLock = capsLock;
        }

        public TargetSession Session { get; }
        public KeyboardController Controller { get; }
        public LayoutActionDispatcher Dispatcher { get; }
        private FakeCapsLock CapsLock { get; }
        public int KeyCalls { get; private set; }
        public int HotkeyCalls { get; private set; }
        public int TextCalls { get; private set; }
        public int TotalSendCalls => KeyCalls + HotkeyCalls + TextCalls;
        public WindowsKeyboardKey LastKey { get; private set; }
        public HotkeyModifier[] LastModifiers { get; private set; } = [];
        public nint LastFocusHwnd { get; private set; }
        public int LastProcessId { get; private set; }
        public string? LastText { get; private set; }
        public long LastCapsSessionId => CapsLock.LastSessionId;

        public static Fixture Create(bool isPassword = false)
        {
            var sessions = new TargetSessionStore();
            var capture = new TargetCaptureSnapshot(DateTimeOffset.UtcNow, 42, (nint)100, (nint)101);
            TargetSession session;
            if (isPassword)
            {
                var focus = new FocusSnapshot(1, DateTimeOffset.UtcNow, 42, (nint)100, null, FocusControlType.Edit, true, true, false, true);
                session = sessions.Replace(focus, (nint)101, null);
            }
            else
            {
                session = sessions.Replace(capture);
            }
            var capsLock = new FakeCapsLock();
            var controller = new KeyboardController(capsLock);
            controller.SetTargetSession(session.SessionId);
            var holder = new FixtureHolder();
            var validator = new TargetSessionValidator(sessions, new StubCapture(capture));
            var dispatcher = new LayoutActionDispatcher(
                validator,
                controller,
                holder.SendKey,
                holder.SendHotkey,
                holder.SendText);
            var fixture = new Fixture(session, controller, dispatcher, capsLock);
            holder.Target = fixture;
            return fixture;
        }

        public InputSendResult Dispatch(KeyViewModel key) => Dispatcher.Dispatch(Session.SessionId, key);

        public void Dispose() => Controller.Dispose();

        private sealed class FixtureHolder
        {
            public Fixture? Target { get; set; }

            public InputSendResult SendKey(WindowsKeyboardKey key, nint hwnd, KeyInputTransition transition, int processId)
            {
                Fixture target = Target!;
                target.KeyCalls++;
                target.LastKey = key;
                target.LastFocusHwnd = hwnd;
                target.LastProcessId = processId;
                Assert.Equal(KeyInputTransition.Press, transition);
                return Success();
            }

            public InputSendResult SendHotkey(IReadOnlyList<HotkeyModifier> modifiers, WindowsKeyboardKey key, nint hwnd, int processId, CancellationToken cancellation)
            {
                Fixture target = Target!;
                target.HotkeyCalls++;
                target.LastModifiers = modifiers.ToArray();
                target.LastKey = key;
                target.LastFocusHwnd = hwnd;
                target.LastProcessId = processId;
                Assert.False(cancellation.IsCancellationRequested);
                return Success();
            }

            public InputSendResult SendText(string text, int processId)
            {
                Fixture target = Target!;
                target.TextCalls++;
                target.LastText = text;
                target.LastProcessId = processId;
                return Success();
            }

            private static InputSendResult Success() => new(InputSendStatus.Succeeded, 2, 2, 0);
        }
    }

    private sealed class FakeCapsLock : ICapsLockStateService
    {
        public long LastSessionId { get; private set; }
        public CapsLockOperationResult Read() => new(CapsLockOperationStatus.Succeeded, false);
        public CapsLockOperationResult Toggle(long targetSessionId)
        {
            LastSessionId = targetSessionId;
            return new(CapsLockOperationStatus.Succeeded, true);
        }
    }

    private sealed class StubCapture(TargetCaptureSnapshot snapshot) : IForegroundTargetCapture
    {
        public TargetCaptureResult Capture() => TargetCaptureResult.Success(snapshot);
    }
}
