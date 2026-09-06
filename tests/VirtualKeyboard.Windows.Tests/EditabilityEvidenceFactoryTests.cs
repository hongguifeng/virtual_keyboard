using System.Runtime.InteropServices;
using System.Windows.Automation;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class EditabilityEvidenceFactoryTests
{
    [Fact]
    public void FactoryTransfersOnlyPatternMetadataAndCaret()
    {
        var source = new StubSource(new(true, false, true, true));
        var factory = new EditabilityEvidenceFactory(source);
        FocusSnapshot snapshot = CreateSnapshot();

        Assert.True(factory.TryCreate(AutomationElement.RootElement, snapshot, new(1, 2, 3, 4), snapshot.TopLevelHwnd, out EditabilityEvidence evidence));

        Assert.True(evidence.IsValuePatternAvailable);
        Assert.False(evidence.IsValueReadOnly);
        Assert.True(evidence.IsTextEditPatternAvailable);
        Assert.True(evidence.IsTextPatternAvailable);
        Assert.Equal(new PhysicalPixelRect(1, 2, 3, 4), evidence.CaretRectangle);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public void ProviderFailuresReturnNoEvidence(Exception failure)
    {
        var factory = new EditabilityEvidenceFactory(new StubSource(default, failure));
        Assert.False(factory.TryCreate(AutomationElement.RootElement, CreateSnapshot(), null, nint.Zero, out _));
    }

    public static TheoryData<Exception> ProviderFailures() => new()
    {
        new ElementNotAvailableException(),
        new InvalidOperationException("synthetic"),
        Marshal.GetExceptionForHR(unchecked((int)0x80004005))!,
    };

    [Fact]
    public void UnregisteredOptionalAutomationPropertyIsUnavailableInsteadOfThrowing()
    {
        Assert.Null(AutomationProperty.LookupById(int.MaxValue));
        Assert.False(SystemEditabilityAutomationSource.ReadBooleanProperty(AutomationElement.RootElement, int.MaxValue));
    }

    private static FocusSnapshot CreateSnapshot() =>
        new(1, DateTimeOffset.UtcNow, 42, (nint)10, null, FocusControlType.Edit, true, true, false, false);

    private sealed class StubSource(EditabilityPatternMetadata metadata, Exception? failure = null) : IEditabilityAutomationSource
    {
        public EditabilityPatternMetadata Read(AutomationElement element)
        {
            _ = element;
            if (failure is not null) throw failure;
            return metadata;
        }
    }
}
