using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Isotope.UI.Avalonia.Tests.Dsl;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Upstream's ChildReconciler and KeyedListDiff, unmodified, reconciling Avalonia panels through our Reconciler.
/// </summary>
public class ChildDiffTests
{
    [AvaloniaFact]
    public void Mount_builds_the_Avalonia_controls_in_order()
    {
        var host = new TestHost(() => VStack(Text("a"), Text("b")));
        var panel = Assert.IsType<StackPanel>(host.Root.Child);
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.Equal(8, panel.Spacing);
        Assert.Equal(["a", "b"], host.Texts());
    }

    [AvaloniaFact]
    public void Keyed_reorder_moves_the_same_controls()
    {
        string[] items = ["a", "b", "c"];
        var host = new TestHost(() => VStack(items.Select(i => (Element)Text(i, key: i)).ToArray()));
        var before = host.All<TextBlock>().ToDictionary(t => t.Text!);

        items = ["c", "a", "b"];
        host.Render();

        var after = host.All<TextBlock>();
        Assert.Equal(["c", "a", "b"], after.Select(t => t.Text));
        Assert.All(after, t => Assert.Same(before[t.Text!], t));
    }

    [AvaloniaFact]
    public void Keyed_insert_and_remove_keep_the_survivors_and_unmount_the_removed()
    {
        var log = new List<string>();
        string[] items = ["a", "b", "c"];
        Element Row(string name) => Component(ctx =>
        {
            ctx.UseEffect(() => () => log.Add("cleanup " + name), Array.Empty<object>());
            return Text(name);
        }, key: name);
        var host = new TestHost(() => VStack(items.Select(Row).ToArray()));
        var before = host.All<TextBlock>().ToDictionary(t => t.Text!);

        items = ["x", "a", "c"];
        host.Render();

        Assert.Equal(["x", "a", "c"], host.Texts());
        Assert.Same(before["a"], host.All<TextBlock>()[1]);
        Assert.Same(before["c"], host.All<TextBlock>()[2]);
        Assert.Equal(["cleanup b"], log);
    }

    [AvaloniaFact]
    public void Unkeyed_children_update_in_place()
    {
        var text = "a";
        var host = new TestHost(() => VStack(Text(text), Text("fixed")));
        var before = host.All<TextBlock>();

        text = "b";
        host.Render();

        Assert.Equal(["b", "fixed"], host.Texts());
        Assert.Equal(before, host.All<TextBlock>());
    }

    [AvaloniaFact]
    public void A_conditional_child_does_not_shift_its_siblings_onto_other_controls()
    {
        var show = false;
        var host = new TestHost(() => VStack(show ? Text("banner") : EmptyElement.Instance, Text("a"), Text("b")));
        var a = host.All<TextBlock>()[0];
        var b = host.All<TextBlock>()[1];

        show = true;
        host.Render();

        Assert.Equal(["banner", "a", "b"], host.Texts());
        Assert.Same(a, host.All<TextBlock>()[1]);
        Assert.Same(b, host.All<TextBlock>()[2]);
    }

    [AvaloniaFact]
    public void A_type_change_replaces_the_control_and_unmounts_the_old_one()
    {
        var log = new List<string>();
        var asButton = false;
        var host = new TestHost(() => VStack(asButton
            ? Button("now a button")
            : Component(ctx =>
            {
                ctx.UseEffect(() => () => log.Add("cleanup"), Array.Empty<object>());
                return Text("text");
            })));

        asButton = true;
        host.Render();

        var panel = Assert.IsType<StackPanel>(host.Root.Child);
        Assert.IsType<Button>(Assert.Single(panel.Children));
        Assert.Equal(["cleanup"], log);
    }

    [AvaloniaFact]
    public void Duplicate_sibling_keys_are_reported_with_the_component_that_rendered_them()
    {
        var logger = new ListLogger();
        _ = new TestHost(() => Component(_ => VStack(Text("1", key: "k"), Text("2", key: "k"))), logger);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("duplicate child keys", warning.Message);
        Assert.Contains("duplicate child keys: k.", warning.Message);
        Assert.DoesNotContain("Component: <unknown>", warning.Message);
    }

    [AvaloniaFact]
    public void Modifiers_are_applied_and_cleared_when_removed()
    {
        var withModifiers = true;
        var host = new TestHost(() => VStack(withModifiers
            ? Text("a").WithModifiers(m => m with { Margin = new Thickness(4), Width = 50, Background = Brushes.Red })
            : Text("a")));
        var text = host.All<TextBlock>()[0];
        Assert.Equal(new Thickness(4), text.Margin);
        Assert.Equal(50, text.Width);
        Assert.Same(Brushes.Red, text.Background);

        withModifiers = false;
        host.Render();

        Assert.Same(text, host.All<TextBlock>()[0]);
        Assert.Equal(default, text.Margin);
        Assert.True(double.IsNaN(text.Width));
        Assert.Null(text.Background);
    }

    [AvaloniaFact]
    public void A_modified_element_merges_its_modifiers_under_the_inner_ones()
    {
        var inner = Text("a").WithModifiers(m => m with { Width = 10 });
        var host = new TestHost(() => VStack(new ModifiedElement(inner, new ElementModifiers { Width = 99, Height = 20 })));
        var text = host.All<TextBlock>()[0];
        Assert.Equal(10, text.Width);
        Assert.Equal(20, text.Height);
    }

    [AvaloniaFact]
    public void A_skipped_button_dispatches_to_the_current_render_closure()
    {
        var host = new TestHost(() => Component(ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            return VStack(Text($"count {count}"), Button("inc", () => setCount(count + 1)));
        }));

        for (int i = 0; i < 3; i++)
        {
            TestHost.Click(host.All<Button>()[0]);
            host.Flush();
        }

        Assert.Equal("count 3", host.Texts()[0]);
    }
}
