using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

internal sealed record SearchInputOwner(RuntimeIdentity Identity, PhysicalPixelRect? Bounds);

/// <summary>Recognizes an explicit text-owner/list relationship, never a nearby editor alone.</summary>
internal static class SearchInputResolver
{
    internal const int MaximumAncestors = 4;
    internal const int MaximumChildren = 16;
    internal const int MaximumRelations = 8;

    public static SearchInputOwner? Capture(FocusSnapshot snapshot)
    {
        if (!Eligible(snapshot)) return null;
        try
        {
            using var source = new NativeSearchAutomation();
            return Resolve(snapshot, source);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or
            System.Windows.Automation.ElementNotAvailableException)
        {
            return null;
        }
    }

    internal static bool Eligible(FocusSnapshot snapshot) => snapshot.ControlType == FocusControlType.ListItem &&
        snapshot.HasKeyboardFocus && snapshot.IsEnabled && !snapshot.IsOffscreen && !snapshot.IsPassword &&
        snapshot.ProcessId > 0 && snapshot.TopLevelHwnd != 0 && snapshot.RuntimeId is { Count: > 0 and <= 64 };

    internal static SearchInputOwner? Resolve(FocusSnapshot snapshot, ISearchAutomation source)
    {
        if (!Eligible(snapshot)) return null;
        var focus = source.GetFocus();
        if (focus is null || !CurrentFocus(source.Read(focus), snapshot)) return null;
        var lists = new List<RuntimeIdentity>();
        SearchInputOwner? owner = null;
        var ancestor = focus;
        // Only a small local ControlView neighborhood, with no FindAll/Descendants query.
        for (int depth = 0; depth < MaximumAncestors; depth++)
        {
            ancestor = source.Parent(ancestor);
            if (ancestor is null) break;
            var parent = source.Read(ancestor);
            if (parent.ProcessId != snapshot.ProcessId) break;
            if (parent.ControlType == 50008 && parent.Enabled && !parent.Offscreen && parent.Identity is { } listId)
                lists.Add(listId);
            // A candidate cannot control the ancestor that contains the candidate itself.
            if (lists.Count == 0 || parent.ControlType == 50008) continue;
            var child = source.FirstChild(ancestor);
            for (int index = 0; child is not null && index < MaximumChildren; index++)
            {
                var candidate = source.Read(child);
                if (candidate.ProcessId == snapshot.ProcessId && candidate.IsWritableSearchInput &&
                    candidate.Identity is { Count: > 0 and <= 64 } candidateId)
                {
                    var controllers = source.Controllers(child);
                    if (controllers.Count <= MaximumRelations && controllers.Any(lists.Contains))
                    {
                        if (owner is not null && !owner.Identity.Equals(candidateId)) return null;
                        owner = new(candidateId, candidate.Bounds);
                    }
                }
                child = source.NextSibling(child);
            }
            // Truncation could hide a second owner: reject ambiguity instead of selecting the first.
            if (child is not null) return null;
            if (owner is not null) break;
        }
        if (owner is null) return null;
        var latest = source.GetFocus();
        return latest is not null && CurrentFocus(source.Read(latest), snapshot) ? owner : null;
    }

    private static bool CurrentFocus(SearchNodeMetadata metadata, FocusSnapshot snapshot) =>
        metadata.ProcessId == snapshot.ProcessId && metadata.ControlType == 50007 && metadata.Focused &&
        metadata.Enabled && !metadata.Offscreen && !metadata.Password && Equals(metadata.Identity, snapshot.RuntimeId);
}

internal readonly record struct SearchNodeMetadata(int ProcessId, int ControlType, RuntimeIdentity? Identity,
    bool Focused, bool Enabled, bool Offscreen, bool Password, bool Focusable = false,
    bool WritableValue = false, bool Text = false, bool ListPopup = false, PhysicalPixelRect? Bounds = null)
{
    public bool IsWritableSearchInput => ControlType is 50003 or 50004 && Enabled && !Offscreen &&
        !Password && Focusable && WritableValue && Text && ListPopup;
}

internal interface ISearchAutomation
{
    object? GetFocus();
    SearchNodeMetadata Read(object node);
    object? Parent(object node);
    object? FirstChild(object node);
    object? NextSibling(object node);
    IReadOnlyList<RuntimeIdentity> Controllers(object node);
}
