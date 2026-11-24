using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvTreeView = Avalonia.Controls.TreeView;
using AvTreeViewItem = Avalonia.Controls.TreeViewItem;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The tree view (#43), as an Avalonia TreeView of TreeViewItems built from node data: items updated in place by
/// position, declared expansion written only when it changes, OnItemInvoked on a tap or Enter (not on the expand
/// chevron), OnExpanding on the user's expansions only, in a headless window.
/// </summary>
public sealed class TreeViewTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Show(TestHost host)
    {
        if (TopLevel.GetTopLevel(host.Root) is Window shown) return shown;
        var window = new Window { Width = 400, Height = 400, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static AvTreeView Tree(TestHost host) => Assert.IsType<AvTreeView>(host.Root.Child);

    private static AvTreeViewItem Item(ItemsControl parent, int index) => Assert.IsType<AvTreeViewItem>(parent.Items[index]);

    private static List<string?> Headers(ItemsControl parent) => parent.Items.Cast<AvTreeViewItem>().Select(i => i.Header as string).ToList();

    // A real headless click at the centre of a control, through the window's input.
    private static void ClickOn(Window window, Visual target)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Visual HeaderOf(AvTreeViewItem item) =>
        item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter");

    [AvaloniaFact]
    public void TreeView_mounts_nested_items_and_updates_them_in_place_by_position()
    {
        var rename = false;
        var more = false;
        var host = new TestHost(() =>
        {
            TreeViewItemData[] docs = more ? [TreeViewItem("Report.docx"), TreeViewItem("Slides.pptx")] : [TreeViewItem("Report.docx")];
            TreeViewItemData[] top = [TreeViewItem(rename ? "Docs" : "Documents", docs), TreeViewItem("Music"), TreeViewItem("Pictures")];
            return TreeView(more ? top[..2] : top);
        });
        var tree = Tree(host);
        Assert.Equal(["Documents", "Music", "Pictures"], Headers(tree));
        var docs = Item(tree, 0);
        Assert.Equal(["Report.docx"], Headers(docs));
        var report = Item(docs, 0);

        rename = true;
        more = true;
        host.Render();
        Assert.Same(tree, Tree(host));
        Assert.Same(docs, Item(tree, 0));
        Assert.Same(report, Item(docs, 0));
        Assert.Equal(["Docs", "Music"], Headers(tree));
        Assert.Equal(["Report.docx", "Slides.pptx"], Headers(docs));
    }

    [AvaloniaFact]
    public void Declared_expansion_is_written_when_it_changes_and_the_user_s_stays_until_then()
    {
        var expanded = false;
        var label = "Root";
        var host = new TestHost(() => TreeView(TreeViewItem(label, TreeViewItem("Child")) with { IsExpanded = expanded }));
        var root = Item(Tree(host), 0);
        Assert.False(root.IsExpanded);

        root.IsExpanded = true; // the user expands it
        host.Render(); // a render that changes nothing about it keeps the user's expansion
        Assert.True(root.IsExpanded);
        label = "Root (4)"; // and so does a new header on the same node (a live count)
        host.Render();
        Assert.True(root.IsExpanded);

        expanded = true;
        host.Render();
        Assert.True(root.IsExpanded);
        expanded = false; // a declared change is written
        host.Render();
        Assert.False(root.IsExpanded);
    }

    [AvaloniaFact]
    public void Nodes_that_move_come_or_go_keep_their_items_and_the_user_s_expansions()
    {
        string[] order = ["A", "B"];
        var host = new TestHost(() => TreeView(order.Select(h => TreeViewItem(h, TreeViewItem(h + ".child"))).ToArray()));
        var tree = Tree(host);
        var (a, b) = (Item(tree, 0), Item(tree, 1));
        a.IsExpanded = true; // the user expands A only

        order = ["B", "A"]; // a sort
        host.Render();
        Assert.Equal([b, a], tree.Items.Cast<AvTreeViewItem>());
        Assert.Equal([false, true], tree.Items.Cast<AvTreeViewItem>().Select(i => i.IsExpanded));
        Assert.Equal(["A.child"], Headers(a)); // each item keeps its own node's children

        order = ["X", "B", "A"]; // one comes first: a new item, the others untouched
        host.Render();
        Assert.Equal(["X", "B", "A"], Headers(tree));
        Assert.Same(b, Item(tree, 1));
        Assert.Same(a, Item(tree, 2));
        Assert.True(a.IsExpanded);
        Assert.False(Item(tree, 0).IsExpanded);

        order = ["A"]; // the others go
        host.Render();
        Assert.Equal([a], tree.Items.Cast<AvTreeViewItem>());
        Assert.True(a.IsExpanded);
    }

    [AvaloniaFact]
    public void A_tap_on_an_item_invokes_it_with_its_data_and_the_expand_chevron_does_not()
    {
        var invoked = new List<string>();
        var host = new TestHost(() => TreeView(TreeViewItem("Documents", TreeViewItem("Report.docx")), TreeViewItem("Music"))
            with { OnItemInvoked = node => invoked.Add(node.Header) });
        var window = Show(host);
        var tree = Tree(host);

        ClickOn(window, HeaderOf(Item(tree, 1)));
        Assert.Equal(["Music"], invoked);

        var docs = Item(tree, 0);
        ClickOn(window, docs.GetVisualDescendants().OfType<ToggleButton>().First(b => b.IsEffectivelyVisible));
        Assert.True(docs.IsExpanded);
        Assert.Equal(["Music"], invoked);

        ClickOn(window, HeaderOf(Item(docs, 0)));
        Assert.Equal(["Music", "Report.docx"], invoked);
    }

    [AvaloniaFact]
    public void Enter_on_an_item_invokes_it_and_the_current_render_s_callback_runs()
    {
        var invoked = new List<string>();
        var round = 1;
        var host = new TestHost(() =>
        {
            var r = round;
            return TreeView(TreeViewItem("A"), TreeViewItem("B")) with { OnItemInvoked = node => invoked.Add(r + node.Header) };
        });
        var window = Show(host);
        var b = Item(Tree(host), 1);
        b.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["1B"], invoked);

        round = 2;
        host.Render();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["1B", "2B"], invoked);

        // A modified key changes a multiple selection: no invoke.
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["1B", "2B"], invoked);
    }

    [AvaloniaFact]
    public void Enter_on_a_parent_node_invokes_it_and_Avalonia_expands_it_which_OnExpanding_hears()
    {
        var invoked = new List<string>();
        var expanding = new List<string>();
        var host = new TestHost(() => TreeView(TreeViewItem("Parent", TreeViewItem("Child")))
            with { OnItemInvoked = node => invoked.Add(node.Header), OnExpanding = node => expanding.Add(node.Header) });
        var window = Show(host);
        var parent = Item(Tree(host), 0);
        parent.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Parent"], invoked);
        // Avalonia's TreeViewItem expands a parent on Enter (measured): a user expansion, so OnExpanding is called too.
        Assert.True(parent.IsExpanded);
        Assert.Equal(["Parent"], expanding);
    }

    [AvaloniaFact]
    public void OnExpanding_hears_the_user_s_expansions_and_not_the_engine_s()
    {
        var expanding = new List<string>();
        var declared = false;
        var host = new TestHost(() => TreeView(
                TreeViewItem("Root", TreeViewItem("Child", TreeViewItem("Leaf"))) with { IsExpanded = declared })
            with { OnExpanding = node => expanding.Add(node.Header) });
        Show(host);
        var root = Item(Tree(host), 0);

        declared = true; // the engine expands it
        host.Render();
        Dispatcher.UIThread.RunJobs(); // its children are realized
        Assert.True(root.IsExpanded);
        Assert.Empty(expanding);

        var child = Item(root, 0);
        child.IsExpanded = true; // the user expands a nested item
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Child"], expanding);

        child.IsExpanded = false; // collapsing isn't expanding
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Child"], expanding);
    }

    [AvaloniaFact]
    public void SelectionMode_is_written_and_cleared()
    {
        SelectionMode? mode = SelectionMode.Multiple;
        var host = new TestHost(() => TreeView(TreeViewItem("A")) with { SelectionMode = mode });
        var tree = Tree(host);
        Assert.Equal(SelectionMode.Multiple, tree.SelectionMode);

        mode = null;
        host.Render();
        Assert.Equal(new AvTreeView().SelectionMode, tree.SelectionMode);
    }
}
