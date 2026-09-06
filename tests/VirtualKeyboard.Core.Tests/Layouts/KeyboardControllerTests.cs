using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Tests.Layouts;

public sealed class KeyboardControllerTests
{
    [Fact]
    public void TargetChangeClearsTransientModifiersAndRefreshesCapsLock()
    {
        var caps = new FakeCapsLockService { IsOn = true };
        using var controller = new KeyboardController(caps);
        controller.SetTargetSession(1);
        controller.ToggleModifier(KeyboardModifier.Shift);
        controller.ToggleModifier(KeyboardModifier.Control);

        KeyboardControllerState state = controller.SetTargetSession(2);

        Assert.Equal(2, state.TargetSessionId);
        Assert.False(state.ShiftLatched);
        Assert.False(state.ControlLatched);
        Assert.True(state.IsCapsLockKnown);
        Assert.True(state.IsCapsLockOn);
        Assert.Equal(2, caps.ReadCalls);
    }

    [Fact]
    public void ShiftIsConsumedOnlyByPrintableAction()
    {
        using var controller = ControllerWithTarget();
        controller.ToggleModifier(KeyboardModifier.Shift);

        KeyboardActionPreparation navigation = controller.PrepareAction(KeyAction("Left"));
        KeyboardActionPreparation letter = controller.PrepareAction(KeyAction("A"));

        Assert.True(navigation.UseShift);
        Assert.False(navigation.IsPrintable);
        Assert.True(navigation.StateAfterConsumption.ShiftLatched);
        Assert.True(letter.UseShift);
        Assert.True(letter.IsPrintable);
        Assert.False(letter.StateAfterConsumption.ShiftLatched);
    }

    [Fact]
    public void ControlAndAltAreOneShotForNextAction()
    {
        using var controller = ControllerWithTarget();
        controller.ToggleModifier(KeyboardModifier.Control);
        controller.ToggleModifier(KeyboardModifier.Alt);

        KeyboardActionPreparation first = controller.PrepareAction(KeyAction("Tab"));
        KeyboardActionPreparation second = controller.PrepareAction(KeyAction("Tab"));

        Assert.True(first.UseControl);
        Assert.True(first.UseAlt);
        Assert.False(first.StateAfterConsumption.ControlLatched);
        Assert.False(first.StateAfterConsumption.AltLatched);
        Assert.False(second.UseControl);
        Assert.False(second.UseAlt);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("7")]
    [InlineData("D7")]
    [InlineData("Space")]
    public void StandardPrintableKeysConsumeShift(string virtualKey)
    {
        using var controller = ControllerWithTarget();
        controller.ToggleModifier(KeyboardModifier.Shift);

        Assert.True(controller.PrepareAction(KeyAction(virtualKey)).IsPrintable);
        Assert.False(controller.State.ShiftLatched);
    }

    [Fact]
    public void TextIsPrintableWithoutInspectingOrReturningItsValue()
    {
        using var controller = ControllerWithTarget();
        controller.ToggleModifier(KeyboardModifier.Shift);
        var action = new LayoutActionDefinition(LayoutActionTypes.Text, value: "private text");

        KeyboardActionPreparation preparation = controller.PrepareAction(action);

        Assert.True(preparation.IsPrintable);
        Assert.DoesNotContain(
            typeof(KeyboardActionPreparation).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void RefreshMirrorsPhysicalCapsLockChanges()
    {
        var caps = new FakeCapsLockService();
        using var controller = new KeyboardController(caps);
        Assert.False(controller.SetTargetSession(1).IsCapsLockOn);
        caps.IsOn = true;

        KeyboardControllerState state = controller.RefreshCapsLock();

        Assert.True(state.IsCapsLockOn);
        Assert.True(state.IsCapsLockKnown);
    }

    [Fact]
    public void ToggleCapsLockUsesCurrentSessionAndAppliesReturnedState()
    {
        var caps = new FakeCapsLockService();
        using var controller = new KeyboardController(caps);
        controller.SetTargetSession(42);

        CapsLockOperationResult result = controller.ToggleCapsLock();

        Assert.True(result.IsSuccess);
        Assert.Equal(42, caps.LastToggleSessionId);
        Assert.True(controller.State.IsCapsLockOn);
    }

    [Fact]
    public void FailedCapsLockRefreshMarksStateUnknownWithoutInventingValue()
    {
        var caps = new FakeCapsLockService { IsOn = true };
        using var controller = new KeyboardController(caps);
        controller.SetTargetSession(1);
        caps.Status = CapsLockOperationStatus.Failed;
        caps.IsOn = false;

        KeyboardControllerState state = controller.RefreshCapsLock();

        Assert.False(state.IsCapsLockKnown);
        Assert.True(state.IsCapsLockOn);
    }

    [Fact]
    public void ClearAndDisposeRemoveAllTransientState()
    {
        var caps = new FakeCapsLockService();
        var controller = new KeyboardController(caps);
        controller.SetTargetSession(1);
        controller.ToggleModifier(KeyboardModifier.Shift);
        controller.ToggleModifier(KeyboardModifier.Control);
        controller.ToggleModifier(KeyboardModifier.Alt);

        KeyboardControllerState cleared = controller.ClearTargetSession();
        controller.Dispose();

        Assert.Equal(0, cleared.TargetSessionId);
        Assert.False(cleared.ShiftLatched || cleared.ControlLatched || cleared.AltLatched);
        Assert.Throws<ObjectDisposedException>(() => controller.ToggleModifier(KeyboardModifier.Shift));
    }

    [Fact]
    public void ModifierAndUnknownActionsCannotBePrepared()
    {
        using var controller = ControllerWithTarget();

        Assert.Throws<ArgumentException>(() => controller.PrepareAction(new(LayoutActionTypes.Modifier, modifier: "Shift")));
        Assert.Throws<ArgumentException>(() => controller.PrepareAction(new("command")));
    }

    [Fact]
    public void ActionCannotConsumeStateWithoutTargetSession()
    {
        using var controller = new KeyboardController(new FakeCapsLockService());
        controller.ToggleModifier(KeyboardModifier.Shift);

        Assert.Throws<InvalidOperationException>(() => controller.PrepareAction(KeyAction("A")));
        Assert.True(controller.State.ShiftLatched);
    }

    private static KeyboardController ControllerWithTarget()
    {
        var controller = new KeyboardController(new FakeCapsLockService());
        controller.SetTargetSession(1);
        return controller;
    }

    private static LayoutActionDefinition KeyAction(string virtualKey) =>
        new(LayoutActionTypes.Key, virtualKey: virtualKey);

    private sealed class FakeCapsLockService : ICapsLockStateService
    {
        public CapsLockOperationStatus Status { get; set; } = CapsLockOperationStatus.Succeeded;

        public bool IsOn { get; set; }

        public int ReadCalls { get; private set; }

        public long LastToggleSessionId { get; private set; }

        public CapsLockOperationResult Read()
        {
            ReadCalls++;
            return new(Status, IsOn);
        }

        public CapsLockOperationResult Toggle(long targetSessionId)
        {
            LastToggleSessionId = targetSessionId;
            if (Status == CapsLockOperationStatus.Succeeded)
            {
                IsOn = !IsOn;
            }
            return new(Status, IsOn);
        }
    }
}
