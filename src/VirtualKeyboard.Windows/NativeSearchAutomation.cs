using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows;

/// <summary>Modern UIA metadata absent from the managed facade. Owned by one MTA capture in the worker.</summary>
internal sealed class NativeSearchAutomation : ISearchAutomation, IDisposable
{
    private readonly List<object> _references = [];
    private readonly NativeUia _uia;
    private readonly NativeWalker _walker;

    public NativeSearchAutomation()
    {
        try
        {
            _uia = Own((NativeUia)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!)!)!;
            _walker = Own(_uia.GetControlViewWalker())!;
        }
        catch { Dispose(); throw; }
    }

    public object? GetFocus() => Own(_uia.GetFocusedElement());
    public object? Parent(object node) => Own(_walker.GetParentElement((NativeElement)node));
    public object? FirstChild(object node) => Own(_walker.GetFirstChildElement((NativeElement)node));
    public object? NextSibling(object node) => Own(_walker.GetNextSiblingElement((NativeElement)node));

    public SearchNodeMetadata Read(object node)
    {
        var element = (NativeElement)node;
        object Property(int id) => element.GetCurrentPropertyValue(id);
        bool True(int id) => Property(id) is true;
        int type = Property(30003) is int controlType ? controlType : 0;
        int pid = Property(30002) is int processId ? processId : 0;
        int[]? runtimeId = element.GetRuntimeId();
        var identity = runtimeId is { Length: > 0 and <= 64 } ? new RuntimeIdentity(runtimeId) : null;
        bool password = Property(30019) is not false;
        bool enabled = True(30010), offscreen = Property(30022) is not false;
        var metadata = new SearchNodeMetadata(pid, type, identity, True(30008), enabled, offscreen, password);
        if (type is not (50003 or 50004) || password || !enabled || offscreen) return metadata;
        bool writable = True(30043) && Property(30046) is false;
        if (!writable) return metadata;
        // Only compare allow-listed semantic tokens. Never persist ARIA strings or control values.
        bool popup = HasListPopup(Property(30102) as string);
        PhysicalPixelRect? bounds = Property(30001) is double[] { Length: 4 } rectangle
            ? new(rectangle[0], rectangle[1], rectangle[2], rectangle[3]) : null;
        return metadata with { Focusable = True(30009), WritableValue = true, Text = True(30040),
            ListPopup = popup, Bounds = bounds is { IsValid: true } ? bounds : null };
    }

    internal static bool HasListPopup(string? aria) => aria is { Length: <= 4096 } &&
        aria.Split(';', StringSplitOptions.TrimEntries).Contains("haspopup=listbox", StringComparer.Ordinal);

    public IReadOnlyList<RuntimeIdentity> Controllers(object node)
    {
        object property = ((NativeElement)node).GetCurrentPropertyValue(30104);
        if (!Marshal.IsComObject(property)) return [];
        Own(property);
        if (property is not NativeElementArray array) return [];
        int count = array.GetLength();
        if (count < 0 || count > SearchInputResolver.MaximumRelations) throw new InvalidOperationException("UIA relationship limit.");
        var identities = new List<RuntimeIdentity>(count);
        for (int i = 0; i < count; i++)
        {
            var related = Own(array.GetElement(i));
            if (related?.GetRuntimeId() is { Length: > 0 and <= 64 } id) identities.Add(new(id));
        }
        return identities;
    }

    private T? Own<T>(T? value) where T : class
    {
        if (value is not null) _references.Add(value);
        return value;
    }

    public void Dispose()
    {
        for (int i = _references.Count - 1; i >= 0; i--) Marshal.ReleaseComObject(_references[i]);
        _references.Clear();
    }
}

// Read-only prefixes of Windows SDK UIAutomationClient.idl interfaces, in exact vtable order.
// Reserved slots are never called; no focus-changing or subtree-search API is exposed by the adapter.
[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface NativeUia
{
    void ReservedCompareElements(); void ReservedCompareRuntimeIds(); void ReservedGetRootElement();
    void ReservedElementFromHandle(); void ReservedElementFromPoint();
    NativeElement? GetFocusedElement();
    void ReservedRootCache(); void ReservedHandleCache(); void ReservedPointCache(); void ReservedFocusCache();
    void ReservedCreateTreeWalker();
    NativeWalker GetControlViewWalker();
}

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface NativeElement
{
    void ReservedFocus();
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[]? GetRuntimeId();
    void ReservedFindFirst(); void ReservedFindAll(); void ReservedFindFirstCache(); void ReservedFindAllCache();
    void ReservedBuildCache();
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int id);
}

[ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface NativeWalker
{
    NativeElement? GetParentElement(NativeElement element);
    NativeElement? GetFirstChildElement(NativeElement element);
    void ReservedLastChild();
    NativeElement? GetNextSiblingElement(NativeElement element);
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface NativeElementArray
{
    int GetLength();
    NativeElement? GetElement(int index);
}
