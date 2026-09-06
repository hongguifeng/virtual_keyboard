using System.Windows.Automation;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Reads only allow-listed UIA metadata on the MTA observer thread.</summary>
internal sealed class FocusSnapshotFactory
{
    private readonly FocusSnapshotVersionGenerator _versions;
    private readonly Func<DateTimeOffset> _utcNow;

    public FocusSnapshotFactory(int currentProcessId, Func<DateTimeOffset>? utcNow = null)
    {
        _versions = new FocusSnapshotVersionGenerator(currentProcessId);
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    public bool TryCreate(AutomationElement element, out FocusSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(element);
        snapshot = null;
        try
        {
            var info = element.Current;
            int processId = info.ProcessId;
            nint hwnd = ResolveTopLevelWindow(element, processId);
            FocusControlType controlType = MapControlType(info.ControlType);
            int[]? runtimeId = element.GetRuntimeId();
            bool isPassword = info.IsPassword;
            return _versions.TryCreate(
                _utcNow(),
                processId,
                hwnd,
                runtimeId,
                controlType,
                info.HasKeyboardFocus,
                info.IsEnabled,
                info.IsOffscreen,
                isPassword,
                out snapshot);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static nint ResolveTopLevelWindow(AutomationElement element, int processId)
    {
        nint topLevel = nint.Zero;
        AutomationElement? current = element;
        for (int depth = 0; depth < 64 && current is not null; depth++)
        {
            var info = current.Current;
            if (info.ProcessId != processId)
            {
                break;
            }

            if (info.NativeWindowHandle != 0)
            {
                topLevel = (nint)info.NativeWindowHandle;
            }

            current = TreeWalker.ControlViewWalker.GetParent(current);
        }

        return topLevel;
    }

    private static FocusControlType MapControlType(ControlType controlType)
    {
        if (controlType == ControlType.Edit) return FocusControlType.Edit;
        if (controlType == ControlType.Document) return FocusControlType.Document;
        if (controlType == ControlType.Button) return FocusControlType.Button;
        if (controlType == ControlType.Window) return FocusControlType.Window;
        if (controlType == ControlType.Pane) return FocusControlType.Pane;
        if (controlType == ControlType.Text) return FocusControlType.Text;
        if (controlType == ControlType.Custom) return FocusControlType.Custom;
        return FocusControlType.Other;
    }
}
