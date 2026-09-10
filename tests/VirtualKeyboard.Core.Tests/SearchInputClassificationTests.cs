using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Core.Tests;

public sealed class SearchInputClassificationTests
{
    private static FocusSnapshot Snapshot => new(1, DateTimeOffset.UtcNow, 42, 100,
        new([1, 2]), FocusControlType.ListItem, true, true, false, false)
        { InputOwnerRuntimeId = new([1, 3]) };

    [Fact]
    public void VerifiedSearchCompositeRetainsActualFocusIdentity()
    {
        var snapshot = Snapshot;
        var result = EditabilityClassifier.Classify(new(snapshot, false, false, false, false, null, 100, true));
        Assert.Equal(Editability.Editable, result.Value);
        Assert.Equal(ClassificationReasonCode.SearchInputRelationship, result.ReasonCode);
        Assert.Equal(new RuntimeIdentity([1, 2]), snapshot.RuntimeId);
    }

    [Theory]
    [InlineData("no-proof")]
    [InlineData("no-owner")]
    [InlineData("no-focus-id")]
    [InlineData("disabled")]
    [InlineData("offscreen")]
    [InlineData("unfocused")]
    [InlineData("readonly")]
    [InlineData("password")]
    [InlineData("other")]
    [InlineData("invalid-window")]
    public void IncompleteOrUnsafeEvidenceCannotPromoteAList(string condition)
    {
        var snapshot = condition switch
        {
            "no-owner" => Snapshot with { InputOwnerRuntimeId = null },
            "no-focus-id" => Snapshot with { RuntimeId = null },
            "disabled" => Snapshot with { IsEnabled = false },
            "offscreen" => Snapshot with { IsOffscreen = true },
            "unfocused" => Snapshot with { HasKeyboardFocus = false },
            "password" => Snapshot with { IsPassword = true },
            "other" => Snapshot with { ControlType = FocusControlType.Other },
            "invalid-window" => Snapshot with { TopLevelHwnd = 0 },
            _ => Snapshot,
        };
        Assert.NotEqual(Editability.Editable, EditabilityClassifier.Classify(new(snapshot,
            condition == "readonly", condition == "readonly", false, false, null, 100, condition != "no-proof")).Value);
    }
}
