using System.Runtime.InteropServices;
using System.Windows.Automation;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Reads pattern availability and read-only state without reading control values or text.</summary>
internal sealed class EditabilityEvidenceFactory(IEditabilityAutomationSource? source = null)
{
    private readonly IEditabilityAutomationSource _source = source ?? new SystemEditabilityAutomationSource();

    public bool TryCreate(
        AutomationElement element,
        FocusSnapshot snapshot,
        ScreenRectangle? caretRectangle,
        nint caretOwnerHwnd,
        out EditabilityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(snapshot);
        evidence = default;
        try
        {
            EditabilityPatternMetadata metadata = _source.Read(element);
            evidence = new EditabilityEvidence(
                snapshot,
                metadata.IsValuePatternAvailable,
                metadata.IsValueReadOnly,
                metadata.IsTextEditPatternAvailable,
                metadata.IsTextPatternAvailable,
                caretRectangle,
                caretOwnerHwnd);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }
}

internal readonly record struct EditabilityPatternMetadata(
    bool IsValuePatternAvailable,
    bool IsValueReadOnly,
    bool IsTextEditPatternAvailable,
    bool IsTextPatternAvailable);

internal interface IEditabilityAutomationSource
{
    EditabilityPatternMetadata Read(AutomationElement element);
}

internal sealed class SystemEditabilityAutomationSource : IEditabilityAutomationSource
{
    // UIA identifiers missing from the managed System.Windows.Automation facade.
    private const int IsTextPattern2AvailablePropertyId = 30119;
    private const int IsTextEditPatternAvailablePropertyId = 30149;

    public EditabilityPatternMetadata Read(AutomationElement element)
    {
        bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObject);
        bool isReadOnly = hasValue && ((ValuePattern)valueObject).Current.IsReadOnly;
        bool hasText = element.TryGetCurrentPattern(TextPattern.Pattern, out _) ||
            ReadBooleanProperty(element, IsTextPattern2AvailablePropertyId);
        bool hasTextEdit = ReadBooleanProperty(element, IsTextEditPatternAvailablePropertyId);
        return new(hasValue, isReadOnly, hasTextEdit, hasText);
    }

    private static bool ReadBooleanProperty(AutomationElement element, int propertyId)
    {
        AutomationProperty property = AutomationProperty.LookupById(propertyId);
        object value = element.GetCurrentPropertyValue(property, true);
        return value is bool result && result;
    }
}
