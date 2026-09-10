using VirtualKeyboard.Core.Targeting;

namespace VirtualKeyboard.Windows.Tests;

public sealed class SearchInputResolverTests
{
    [Theory]
    [InlineData("expanded=false;haspopup=listbox;required=false", true)]
    [InlineData("haspopup=true", false)]
    [InlineData("haspopup=menu", false)]
    [InlineData("description=haspopup=listbox", false)]
    [InlineData(null, false)]
    public void PopupMetadataRequiresExactSemanticToken(string? metadata, bool expected) =>
        Assert.Equal(expected, NativeSearchAutomation.HasListPopup(metadata));

    private static FocusSnapshot Snapshot => new(1, DateTimeOffset.UtcNow, 42, 100,
        new([1]), FocusControlType.ListItem, true, true, false, false);

    [Fact]
    public void ExplicitControllerRelationshipFindsUnfocusedEditorAndRetainsFocusIdentity()
    {
        var tree = new Tree();
        Assert.Equal(tree.Editor.Metadata.Identity, SearchInputResolver.Resolve(Snapshot, tree)?.Identity);
        Assert.False(tree.Editor.Metadata.Focused);
        Assert.True(tree.Focus.Metadata.Focused);
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("disabled")]
    [InlineData("offscreen")]
    [InlineData("password")]
    [InlineData("no-text")]
    [InlineData("no-popup")]
    [InlineData("not-focusable")]
    [InlineData("wrong-type")]
    [InlineData("wrong-process")]
    [InlineData("no-relation")]
    [InlineData("unrelated-list")]
    [InlineData("ambiguous")]
    [InlineData("stale-focus")]
    [InlineData("focus-changes-during-read")]
    [InlineData("too-many-children")]
    [InlineData("too-many-relations")]
    public void UnsafeOrUnrelatedEditorIsRejected(string scenario)
    {
        var tree = new Tree();
        tree.Editor.Metadata = scenario switch
        {
            "readonly" => tree.Editor.Metadata with { WritableValue = false },
            "disabled" => tree.Editor.Metadata with { Enabled = false },
            "offscreen" => tree.Editor.Metadata with { Offscreen = true },
            "password" => tree.Editor.Metadata with { Password = true },
            "no-text" => tree.Editor.Metadata with { Text = false },
            "no-popup" => tree.Editor.Metadata with { ListPopup = false },
            "not-focusable" => tree.Editor.Metadata with { Focusable = false },
            "wrong-type" => tree.Editor.Metadata with { ControlType = 50000 },
            "wrong-process" => tree.Editor.Metadata with { ProcessId = 43 },
            _ => tree.Editor.Metadata,
        };
        if (scenario == "no-relation") tree.Editor.Relations.Clear();
        if (scenario == "unrelated-list") tree.Editor.Relations = [new([99])];
        if (scenario == "ambiguous") tree.Group.Add(new(tree.Editor.Metadata with { Identity = new([4]) }) { Relations = [new([2])] });
        if (scenario == "stale-focus") tree.Focus.Metadata = tree.Focus.Metadata with { Focused = false };
        if (scenario == "focus-changes-during-read") tree.ChangeFocus = true;
        if (scenario == "too-many-children")
            for (int i = 0; i < 16; i++) tree.Group.Add(new(tree.Group.Metadata));
        if (scenario == "too-many-relations")
            for (int i = 0; i < 8; i++) tree.Editor.Relations.Add(new([i + 30]));
        Assert.Null(SearchInputResolver.Resolve(Snapshot, tree));
    }

    [Fact]
    public void CyclicProviderIsBounded()
    {
        var tree = new Tree();
        tree.Group.Parent = tree.Group;
        _ = SearchInputResolver.Resolve(Snapshot, tree);
        Assert.InRange(tree.ParentReads, 1, SearchInputResolver.MaximumAncestors);
    }

    [Fact]
    public void OrdinaryInputsNeverEnterNativeCompositeTraversal()
    {
        var tree = new Tree();
        Assert.Null(SearchInputResolver.Resolve(Snapshot with { ControlType = FocusControlType.Edit }, tree));
        Assert.Equal(0, tree.FocusReads);
    }

    private sealed class Node(SearchNodeMetadata metadata)
    {
        public SearchNodeMetadata Metadata = metadata;
        public Node? Parent;
        public List<Node> Children = [];
        public List<RuntimeIdentity> Relations = [];
        public void Add(Node node) { Children.Add(node); node.Parent = this; }
    }

    private sealed class Tree : ISearchAutomation
    {
        public Node Focus = new(new(42, 50007, new([1]), true, true, false, false));
        public Node List = new(new(42, 50008, new([2]), false, true, false, false));
        public Node Group = new(new(42, 50026, new([5]), false, true, false, false));
        public Node Editor = new(new(42, 50004, new([3]), false, true, false, false, true, true, true, true)) { Relations = [new([2])] };
        public int ParentReads, FocusReads;
        public bool ChangeFocus;
        public Tree() { Group.Add(List); Group.Add(Editor); List.Add(Focus); }
        public object? GetFocus() { FocusReads++; return ChangeFocus && FocusReads > 1 ? null : Focus; }
        public SearchNodeMetadata Read(object node) => ((Node)node).Metadata;
        public object? Parent(object node) { ParentReads++; return ((Node)node).Parent; }
        public object? FirstChild(object node) => ((Node)node).Children.FirstOrDefault();
        public object? NextSibling(object node)
        {
            var n = (Node)node;
            return n.Parent?.Children.ElementAtOrDefault(n.Parent.Children.IndexOf(n) + 1);
        }
        public IReadOnlyList<RuntimeIdentity> Controllers(object node) => ((Node)node).Relations;
    }
}
