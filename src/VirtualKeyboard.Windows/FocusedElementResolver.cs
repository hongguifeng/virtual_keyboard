using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VirtualKeyboard.Windows;

/// <summary>Shared UIA/native lookup; callers must still validate identity and editability.</summary>
internal static class FocusedElementResolver
{
    public static AutomationElement? Capture() => Capture(out _);

    internal static AutomationElement? Capture(AutomationElement? eventTarget, out bool usedFallback, out bool usedEventTarget) =>
        ResolveEventFocus(() => AutomationElement.FocusedElement, eventTarget, IsCurrentEventTarget,
            CaptureNativeFocusedElement, element => element.Current.HasKeyboardFocus,
            out usedFallback, out usedEventTarget);

    internal static bool IsCurrentEventTarget(AutomationElement element)
    {
        var info = element.Current;
        if (!info.HasKeyboardFocus || !info.IsEnabled || info.IsOffscreen) return false;
        var native = new NativeFocusAdapter().Capture();
        return native.IsCaptured && info.ProcessId == native.Snapshot!.ProcessId &&
            FocusSnapshotFactory.ResolveTopLevelWindow(element, info.ProcessId) == native.Snapshot.TopLevelHwnd;
    }

    internal static T? ResolveEventFocus<T>(Func<T?> primary, T? eventTarget, Func<T, bool> validateEvent,
        Func<T?> fallback, Func<T, bool> hasFocus, out bool usedFallback, out bool usedEventTarget) where T : class
    {
        usedFallback = false;
        usedEventTarget = false;
        T? original = null;
        try
        {
            original = primary();
            if (original is not null && hasFocus(original)) return original;
        }
        catch (Exception e) when (IsProviderFailure(e)) { }
        try
        {
            if (eventTarget is not null && validateEvent(eventTarget))
            {
                usedEventTarget = true;
                return eventTarget;
            }
        }
        catch (Exception e) when (IsProviderFailure(e)) { }
        usedFallback = true;
        try
        {
            T? candidate = fallback();
            return candidate is not null && hasFocus(candidate) ? candidate : original;
        }
        catch (Exception e) when (IsProviderFailure(e)) { return original; }
    }

    public static AutomationElement? Capture(out bool usedFallback) => ResolveFocus(
        () => AutomationElement.FocusedElement,
        CaptureNativeFocusedElement,
        element => element.Current.HasKeyboardFocus, out usedFallback);

    private static AutomationElement? CaptureNativeFocusedElement()
    {
        var native = new NativeFocusAdapter().Capture();
        if (!native.IsCaptured) return null;
        // A subtree search can enter unbounded MSAA sibling traversal even under one HWND.
        // Only inspect the native focus root. HWND-less editors use UIA focus or the validated event target.
        return ResolveNativeFocus(native.Snapshot!.FocusHwnd, native.Snapshot.ProcessId,
            AutomationElement.FromHandle, element => element.Current.ProcessId,
            element => element.Current.HasKeyboardFocus);
    }

    internal static T? ResolveNativeFocus<T>(nint hwnd, int expectedProcessId, Func<nint, T> fromHandle,
        Func<T, int> processId, Func<T, bool> hasFocus) where T : class
    {
        if (hwnd == nint.Zero) return null;
        T root = fromHandle(hwnd);
        return processId(root) == expectedProcessId && hasFocus(root) ? root : null;
    }

    internal static AutomationElement? Resolve(Func<AutomationElement?> primary,
        Func<AutomationElement?> fallback, out bool usedFallback) =>
        ResolveFocus(primary, fallback, _ => true, out usedFallback);

    // Generic seam lets tests model inconsistent provider focus without creating real HWNDs.
    internal static T? ResolveFocus<T>(Func<T?> primary, Func<T?> fallback,
        Func<T, bool> hasFocus, out bool usedFallback) where T : class
    {
        usedFallback = false;
        T? original = null;
        try
        {
            original = primary();
            if (original is not null && hasFocus(original)) return original;
        }
        catch (Exception e) when (IsProviderFailure(e)) { }
        usedFallback = true;
        try
        {
            T? candidate = fallback();
            return candidate is not null && hasFocus(candidate) ? candidate : original;
        }
        catch (Exception e) when (IsProviderFailure(e)) { return original; }
    }

    private static bool IsProviderFailure(Exception e) =>
        e is ElementNotAvailableException or InvalidOperationException or COMException;
}
