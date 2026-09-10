using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Core.Tests;

public sealed class TargetStateCoordinatorTests
{
    [Fact]
    public void ClickFromOlderLauncherCannotExpandNewlyClassifiedTarget()
    {
        var coordinator = new TargetStateCoordinator();
        foreach (FocusSnapshot snapshot in new[] { Snapshot(1, 10), Snapshot(2, 20) })
        {
            coordinator.Observe(snapshot);
            coordinator.ApplyClassification(snapshot, Classification(snapshot, Editability.Editable), showLauncherButton: true);
        }
        Assert.False(coordinator.UserShow(expectedFocusVersion: 1).Accepted);
        Assert.Equal(TargetCoordinatorState.LauncherTracking, coordinator.State);
        Assert.True(coordinator.UserShow(expectedFocusVersion: 2).Accepted);
    }

    [Fact]
    public void MissingRuntimeIdentityDoesNotCarryExpansionToAnotherFocusResult()
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot first = Snapshot(1, 10) with { RuntimeId = null };
        coordinator.Observe(first);
        coordinator.ApplyClassification(first, Classification(first, Editability.Editable), showLauncherButton: true);
        coordinator.UserShow();
        FocusSnapshot next = first with { Version = 2 };
        coordinator.Observe(next);
        Assert.Equal(TargetCoordinatorState.LauncherTracking,
            coordinator.ApplyClassification(next, Classification(next, Editability.Editable), showLauncherButton: true).CurrentState);
    }

    [Fact]
    public void LauncherModeRequiresExplicitExpansionAndRetainsItOnlyForSameTarget()
    {
        var coordinator = new TargetStateCoordinator();
        AssertLauncher(1, 10);
        Assert.Equal(TargetCoordinatorAction.ShowOrUpdateOverlay, coordinator.UserShow().Actions);
        Assert.False(coordinator.UserShow().Accepted);

        FocusSnapshot duplicate = Snapshot(2, 10);
        coordinator.Observe(duplicate);
        Assert.Equal(TargetCoordinatorState.VisibleTracking,
            coordinator.ApplyClassification(duplicate, Classification(duplicate, Editability.Editable), showLauncherButton: true).CurrentState);
        AssertLauncher(3, 20);
        AssertLauncher(4, 10);

        void AssertLauncher(long version, int runtimeId)
        {
            FocusSnapshot snapshot = Snapshot(version, runtimeId);
            coordinator.Observe(snapshot);
            TargetStateTransition result = coordinator.ApplyClassification(snapshot, Classification(snapshot, Editability.Editable), showLauncherButton: true);
            Assert.Equal(TargetCoordinatorState.LauncherTracking, result.CurrentState);
            Assert.Equal(TargetCoordinatorAction.ShowOrUpdateLauncher, result.Actions);
        }
    }

    [Theory]
    [InlineData(Editability.NotEditable)]
    [InlineData(Editability.Unknown)]
    public void LeavingExpandedTargetResetsLauncherAndOldResultsCannotExpandIt(Editability value)
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot first = Snapshot(1, 10);
        coordinator.Observe(first);
        coordinator.ApplyClassification(first, Classification(first, Editability.Editable), showLauncherButton: true);
        coordinator.UserShow();
        FocusSnapshot other = Snapshot(2, 20);
        coordinator.Observe(other);
        Assert.False(coordinator.UserShow().Accepted);
        coordinator.ApplyClassification(other, Classification(other, value), showLauncherButton: true);
        Assert.False(coordinator.UserShow().Accepted);
        Assert.False(coordinator.ApplyClassification(first, Classification(first, Editability.Editable), showLauncherButton: true).Accepted);

        FocusSnapshot returned = Snapshot(3, 10);
        coordinator.Observe(returned);
        Assert.Equal(TargetCoordinatorState.LauncherTracking,
            coordinator.ApplyClassification(returned, Classification(returned, Editability.Editable), showLauncherButton: true).CurrentState);
    }

    [Fact]
    public void LauncherModePreservesManualCloseAndTrayShowSemantics()
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot first = Snapshot(1, 10);
        coordinator.Observe(first);
        coordinator.ApplyClassification(first, Classification(first, Editability.Editable), showLauncherButton: true);
        coordinator.UserShow();
        Assert.Equal(TargetCoordinatorState.ManuallySuppressed, coordinator.UserClose().CurrentState);
        FocusSnapshot duplicate = Snapshot(2, 10);
        coordinator.Observe(duplicate);
        Assert.Equal(TargetCoordinatorAction.None,
            coordinator.ApplyClassification(duplicate, Classification(duplicate, Editability.Editable), showLauncherButton: true).Actions);
        Assert.Equal(TargetCoordinatorState.VisibleTracking, coordinator.UserShow().CurrentState);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("disable")]
    [InlineData("invalid")]
    [InlineData("destroyed")]
    public void LifecycleBoundariesResetExpandedTarget(string boundary)
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot first = Snapshot(1, 10);
        coordinator.Observe(first);
        coordinator.ApplyClassification(first, Classification(first, Editability.Editable), showLauncherButton: true);
        coordinator.UserShow();
        switch (boundary)
        {
            case "settings": coordinator.OpenSettings(); coordinator.CloseSettings(); break;
            case "disable": coordinator.SetEnabled(false); coordinator.SetEnabled(true); break;
            case "invalid": coordinator.InvalidateCurrentTarget(); break;
            case "destroyed": coordinator.TargetDestroyed(first); break;
        }
        Assert.False(coordinator.UserShow().Accepted);
        FocusSnapshot next = Snapshot(2, 10);
        coordinator.Observe(next);
        Assert.Equal(TargetCoordinatorState.LauncherTracking,
            coordinator.ApplyClassification(next, Classification(next, Editability.Editable), showLauncherButton: true).CurrentState);
    }

    [Fact]
    public void EditableLatestResultShowsOverlay()
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot snapshot = Snapshot(1, 10);
        Assert.Equal(TargetCoordinatorAction.BeginEvaluation, coordinator.Observe(snapshot).Actions);

        TargetStateTransition result = coordinator.ApplyClassification(snapshot, Classification(snapshot, Editability.Editable));

        Assert.True(result.Accepted);
        Assert.Equal(TargetCoordinatorState.VisibleTracking, result.CurrentState);
        Assert.Equal(TargetCoordinatorAction.ShowOrUpdateOverlay, result.Actions);
    }

    [Fact]
    public void OlderObservationAndResultCannotReopenOverlay()
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot first = Snapshot(1, 10);
        FocusSnapshot second = Snapshot(2, 20);
        coordinator.Observe(first);
        coordinator.Observe(second);

        Assert.False(coordinator.ApplyClassification(first, Classification(first, Editability.Editable)).Accepted);
        Assert.False(coordinator.Observe(first).Accepted);
        Assert.Equal(TargetCoordinatorState.Evaluating, coordinator.State);
    }

    [Theory]
    [InlineData(Editability.NotEditable)]
    [InlineData(Editability.Unknown)]
    public void NonEditableResultHidesAndClearsTarget(Editability value)
    {
        var coordinator = new TargetStateCoordinator();
        FocusSnapshot snapshot = Snapshot(1, 10);
        coordinator.Observe(snapshot);
        TargetStateTransition result = coordinator.ApplyClassification(snapshot, Classification(snapshot, value));
        Assert.Equal(TargetCoordinatorState.Hidden, result.CurrentState);
        Assert.True(result.Actions.HasFlag(TargetCoordinatorAction.HideOverlay));
        Assert.True(result.Actions.HasFlag(TargetCoordinatorAction.ClearTargetSession));
    }

    [Fact]
    public void ManualSuppressionSurvivesDuplicateTargetButNotNewTarget()
    {
        var coordinator = VisibleOn(Snapshot(1, 10));
        Assert.Equal(TargetCoordinatorState.ManuallySuppressed, coordinator.UserClose().CurrentState);

        FocusSnapshot duplicate = Snapshot(2, 10);
        coordinator.Observe(duplicate);
        Assert.Equal(TargetCoordinatorState.ManuallySuppressed,
            coordinator.ApplyClassification(duplicate, Classification(duplicate, Editability.Editable)).CurrentState);

        FocusSnapshot different = Snapshot(3, 20);
        coordinator.Observe(different);
        TargetStateTransition result = coordinator.ApplyClassification(different, Classification(different, Editability.Editable));
        Assert.Equal(TargetCoordinatorState.VisibleTracking, result.CurrentState);
        Assert.Equal(TargetCoordinatorAction.ShowOrUpdateOverlay, result.Actions);
    }

    [Fact]
    public void UserShowClearsSuppression()
    {
        var coordinator = VisibleOn(Snapshot(1, 10));
        coordinator.UserClose();
        Assert.Equal(TargetCoordinatorState.VisibleTracking, coordinator.UserShow().CurrentState);
    }

    [Fact]
    public void DisableAndReenableClearStateAndRequestRefresh()
    {
        var coordinator = VisibleOn(Snapshot(1, 10));
        TargetStateTransition disabled = coordinator.SetEnabled(false);
        Assert.Equal(TargetCoordinatorState.Disabled, disabled.CurrentState);
        Assert.True(disabled.Actions.HasFlag(TargetCoordinatorAction.HideOverlay));
        Assert.False(coordinator.Observe(Snapshot(2, 20)).Accepted);

        TargetStateTransition enabled = coordinator.SetEnabled(true);
        Assert.Equal(TargetCoordinatorState.Hidden, enabled.CurrentState);
        Assert.True(enabled.Actions.HasFlag(TargetCoordinatorAction.RefreshFocus));
    }

    [Fact]
    public void SettingsAndShutdownRejectFocusWork()
    {
        var coordinator = new TargetStateCoordinator();
        Assert.Equal(TargetCoordinatorState.SettingsOpen, coordinator.OpenSettings().CurrentState);
        Assert.False(coordinator.Observe(Snapshot(1, 10)).Accepted);
        Assert.True(coordinator.CloseSettings().Actions.HasFlag(TargetCoordinatorAction.RefreshFocus));
        Assert.Equal(TargetCoordinatorState.ShuttingDown, coordinator.Shutdown().CurrentState);
        Assert.False(coordinator.Observe(Snapshot(2, 20)).Accepted);
        Assert.False(coordinator.Shutdown().Accepted);
    }

    [Fact]
    public void DestroyedCurrentTargetHidesButUnrelatedTargetIsIgnored()
    {
        FocusSnapshot current = Snapshot(1, 10);
        var coordinator = VisibleOn(current);
        Assert.False(coordinator.TargetDestroyed(Snapshot(2, 20)).Accepted);
        Assert.Equal(TargetCoordinatorState.Hidden, coordinator.TargetDestroyed(current).CurrentState);
    }

    [Fact]
    public void ProviderLossInvalidatesTargetWithoutRequiringDestroyedElementIdentity()
    {
        var coordinator = VisibleOn(Snapshot(1, 10));
        TargetStateTransition result = coordinator.InvalidateCurrentTarget();
        Assert.Equal(TargetCoordinatorState.Hidden, result.CurrentState);
        Assert.True(result.Actions.HasFlag(TargetCoordinatorAction.ClearTargetSession));
        Assert.True(result.Actions.HasFlag(TargetCoordinatorAction.CancelPendingWork));
    }

    private static TargetStateCoordinator VisibleOn(FocusSnapshot snapshot)
    {
        var coordinator = new TargetStateCoordinator();
        coordinator.Observe(snapshot);
        coordinator.ApplyClassification(snapshot, Classification(snapshot, Editability.Editable));
        return coordinator;
    }

    private static FocusSnapshot Snapshot(long version, int runtimeId) =>
        new(version, DateTimeOffset.UtcNow, 42, (nint)100, new RuntimeIdentity([runtimeId]), FocusControlType.Edit, true, true, false, false);

    private static ClassificationResult Classification(FocusSnapshot snapshot, Editability value) =>
        new(snapshot.Version, value, ClassificationReasonCode.ValuePattern, snapshot.IsPassword);
}
