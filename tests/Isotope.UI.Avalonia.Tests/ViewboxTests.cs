using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvViewbox = Avalonia.Controls.Viewbox;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>The Viewbox (#43), as Avalonia's: its child mounts and updates in place, keeping a child component's state; its stretch members.</summary>
public class ViewboxTests
{
    private static Element Counter(RenderContext ctx)
    {
        var (n, setN) = ctx.UseState(0);
        return Button("count " + n, () => setN(n + 1));
    }

    private static AvViewbox Box(TestHost host) => Assert.IsType<AvViewbox>(host.Root.Child);

    [AvaloniaFact]
    public void Viewbox_mounts_its_child_and_stretch_and_updates_them_in_place()
    {
        Stretch? stretch = Stretch.Fill;
        StretchDirection? direction = StretchDirection.DownOnly;
        var text = "big";
        var host = new TestHost(() => Viewbox(TextBlock(text)) with { Stretch = stretch, StretchDirection = direction });
        var box = Box(host);
        var child = Assert.IsType<TextBlock>(box.Child);
        Assert.Equal(("big", Stretch.Fill, StretchDirection.DownOnly), (child.Text, box.Stretch, box.StretchDirection));

        stretch = Stretch.UniformToFill;
        text = "bigger";
        host.Render();
        Assert.Same(box, Box(host));
        Assert.Same(child, box.Child);
        Assert.Equal(("bigger", Stretch.UniformToFill), (child.Text, box.Stretch));

        // Back to null: Avalonia's defaults again.
        stretch = null;
        direction = null;
        host.Render();
        var defaults = new AvViewbox();
        Assert.Equal((defaults.Stretch, defaults.StretchDirection), (box.Stretch, box.StretchDirection));
    }

    [AvaloniaFact]
    public void Viewbox_child_component_keeps_its_state_when_the_viewbox_updates()
    {
        Stretch? stretch = Stretch.Fill;
        var host = new TestHost(() => Viewbox(Component(Counter)) with { Stretch = stretch });
        var button = Box(host).GetLogicalDescendants().OfType<Button>().Single(); // inside the component's slot
        TestHost.Click(button);
        host.Flush();
        Assert.Equal("count 1", button.Content);

        stretch = Stretch.None;
        host.Render();
        Assert.Same(button, Box(host).GetLogicalDescendants().OfType<Button>().Single());
        Assert.Equal("count 1", button.Content);
    }
}
