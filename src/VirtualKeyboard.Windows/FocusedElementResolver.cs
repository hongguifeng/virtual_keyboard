using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VirtualKeyboard.Windows;

/// <summary>Shared UIA/native lookup; callers must still validate identity and editability.</summary>
internal static class FocusedElementResolver
{
    public static AutomationElement? Capture() => Capture(out _);

    public static AutomationElement? Capture(out bool usedFallback) => Resolve(
        () => AutomationElement.FocusedElement,
        () =>
        {
            var native = new NativeFocusAdapter().Capture();
            return native.IsCaptured && native.Snapshot!.FocusHwnd != nint.Zero
                ? AutomationElement.FromHandle(native.Snapshot.FocusHwnd) : null;
        }, out usedFallback);

    internal static AutomationElement? Resolve(Func<AutomationElement?> primary,
        Func<AutomationElement?> fallback, out bool usedFallback)
    {
        usedFallback = false;
        try
        {
            AutomationElement? element = primary();
            if (element is not null) return element;
        }
        catch (Exception e) when (IsProviderFailure(e)) { }
        usedFallback = true;
        try { return fallback(); }
        catch (Exception e) when (IsProviderFailure(e)) { return null; }
    }

    private static bool IsProviderFailure(Exception e) =>
        e is ElementNotAvailableException or InvalidOperationException or COMException;
}
