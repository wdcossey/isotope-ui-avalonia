using Isotope.UI.Core;

namespace Isotope.UI;

// The tree view factories (#43), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs. New
// names are Avalonia's (tools/naming/renames.json): upstream's TreeNode is TreeViewItem, returning TreeViewItemData
// (upstream's TreeViewNodeData), and the parameters are a TreeViewItem's members.

public static partial class Factories
{
    /// <summary>
    /// A tree of text items, as an Avalonia <see cref="Avalonia.Controls.TreeView"/>:
    /// <c>TreeView(TreeViewItem("Documents", TreeViewItem("Report.docx")), TreeViewItem("Music"))</c>. Set
    /// <see cref="TreeViewElement.OnItemInvoked"/> to hear which item the user picks.
    /// </summary>
    public static TreeViewElement TreeView(params TreeViewItemData[] items) => new(items);

    /// <summary>A node of a <see cref="TreeView(TreeViewItemData[])"/>: its header text and child items (Reactor's name: TreeNode).</summary>
    public static TreeViewItemData TreeViewItem(string header, params TreeViewItemData[] items) =>
        new(header, items.Length > 0 ? items : null);
}
