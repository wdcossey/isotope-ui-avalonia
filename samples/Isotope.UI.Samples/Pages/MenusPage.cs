using Avalonia;
using Avalonia.Media;
using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Samples;

/// <summary>
/// Context menus (#26): each row has a right-click menu with a plain item, a check item, a submenu of radio items, a
/// separator and a delete, all driven by one reducer; the last command chosen shows under the list.
/// </summary>
public static class MenusPage
{
    public sealed record FileRow(int Id, string Name, bool Pinned = false, string Label = "none");

    private abstract record Command;
    private sealed record Pin(int Id, bool Pinned) : Command;
    private sealed record SetLabel(int Id, string Label) : Command;
    private sealed record Delete(int Id) : Command;
    private sealed record Reset : Command;

    public static readonly FileRow[] Initial = [new(1, "notes.txt"), new(2, "report.docx"), new(3, "photo.png")];

    public static readonly string[] Labels = ["none", "red", "green", "blue"];

    private static FileRow[] Reduce(FileRow[] rows, Command c) => c switch
    {
        Pin p => rows.Select(r => r.Id == p.Id ? r with { Pinned = p.Pinned } : r).ToArray(),
        SetLabel l => rows.Select(r => r.Id == l.Id ? r with { Label = l.Label } : r).ToArray(),
        Delete d => rows.Where(r => r.Id != d.Id).ToArray(),
        Reset => Initial,
        _ => rows,
    };

    public static Element Render(RenderContext ctx)
    {
        var tips = ctx.UseContext(SampleApp.ShowTips);
        var (rows, dispatch) = ctx.UseReducer<FileRow[], Command>(Reduce, Initial);
        var (last, setLast) = ctx.UseState("nothing yet");

        Element Row(FileRow row)
        {
            var title = row.Name + (row.Pinned ? " (pinned)" : "") + (row.Label == "none" ? "" : $" [{row.Label}]");
            var menu = MenuItems(
                MenuItem("Open", () => setLast("Opened " + row.Name)),
                ToggleMenuItem("Pinned", row.Pinned, pinned =>
                {
                    dispatch(new Pin(row.Id, pinned));
                    setLast((pinned ? "Pinned " : "Unpinned ") + row.Name);
                }),
                MenuSubItem("Label", Labels.Select(label => (MenuFlyoutItemBase)RadioMenuItem(label, "label-" + row.Id, row.Label == label, () =>
                {
                    dispatch(new SetLabel(row.Id, label));
                    setLast($"Labelled {row.Name} {label}");
                })).ToArray()),
                MenuSeparator(),
                MenuItem("Delete", () =>
                {
                    dispatch(new Delete(row.Id));
                    setLast("Deleted " + row.Name);
                }));
            return (Border(TextBlock(title)) with
            {
                Background = Brushes.Transparent, // so a right-click anywhere on the row finds it
                BorderBrush = Brushes.Gray, BorderThickness = 1, CornerRadius = 3, Padding = new Thickness(8, 4),
            })
                .ContextFlyout(menu)
                .Tip(tips, "Right-click for this file's menu")
                .WithKey("file-" + row.Id);
        }

        return VStack(12,
            TextBlock("Right-click a file for its menu. The menus in the menu bar work with the mouse too."),
            VStack(4, rows.Select(Row).ToArray()),
            TextBlock("Last command: " + last),
            Button("Bring the files back", () =>
            {
                dispatch(new Reset());
                setLast("Reset");
            }))
            .Margin(12);
    }
}
