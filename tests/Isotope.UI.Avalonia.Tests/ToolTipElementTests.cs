using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvToolTip = Avalonia.Controls.ToolTip;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Element tooltips (#29): upstream's <c>.WithToolTip(element)</c>, a child subtree on Avalonia's <c>ToolTip.Tip</c>,
/// reconciled in place, kept apart from the text tooltip, and torn down with the modifier or the control.
/// </summary>
public sealed class ToolTipElementTests
{
    private static Control Tip(Control owner) => Assert.IsAssignableFrom<Control>(AvToolTip.GetTip(owner));

    // The text blocks under a mounted subtree, depth first.
    private static List<string?> Texts(Control root)
    {
        var found = new List<string?>();
        void Walk(Control? c)
        {
            switch (c)
            {
                case null: return;
                case TextBlock t: found.Add(t.Text); break;
                case Panel p: foreach (var child in p.Children) Walk(child); break;
                case Decorator d: Walk(d.Child); break;
                case ContentControl { Content: Control content }: Walk(content); break;
            }
        }
        Walk(root);
        return found;
    }

    [AvaloniaFact]
    public void WithToolTip_mounts_the_element_as_the_tip_with_its_placement()
    {
        var host = new TestHost(() => Button("Save").WithToolTip(VStack(TextBlock("Save"), TextBlock("Ctrl+S")), PlacementMode.Bottom));
        var button = host.All<Button>()[0];
        Assert.IsType<StackPanel>(Tip(button));
        Assert.Equal(["Save", "Ctrl+S"], Texts(Tip(button)));
        Assert.Equal(PlacementMode.Bottom, AvToolTip.GetPlacement(button));
    }

    [AvaloniaFact]
    public void WithToolTip_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => Button("Save").WithToolTip(null!));
        Assert.Throws<ArgumentNullException>(() => Button("Save").WithToolTip(null!, PlacementMode.Top));
    }

    [AvaloniaFact]
    public void A_component_in_the_tooltip_keeps_its_state_and_its_control_across_renders()
    {
        Action<int>? bump = null;
        var label = "a";
        var host = new TestHost(() =>
        {
            var current = label;
            return TextBlock("owner").WithToolTip(Component(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                bump = setN;
                return TextBlock(current + n);
            }));
        });
        var owner = host.All<TextBlock>()[0];
        var tip = Tip(owner);
        bump!(5);
        host.Flush();
        Assert.Equal(["a5"], Texts(Tip(owner)));

        label = "b"; // the parent renders a new tooltip element: the component is updated, not remounted
        host.Render();
        Assert.Same(tip, Tip(owner));
        Assert.Equal(["b5"], Texts(Tip(owner)));
    }

    // One element instance: the owner's element is the same on every render, so only the tooltip's own state changes.
    private static readonly List<Action<int>> s_counters = new();
    private static readonly FuncElement s_counter = Component(ctx =>
    {
        var (n, setN) = ctx.UseState(0);
        s_counters.Add(setN);
        return TextBlock("count " + n);
    });

    [AvaloniaFact]
    public void A_state_change_inside_the_tooltip_renders_although_its_owner_is_unchanged()
    {
        s_counters.Clear();
        var host = new TestHost(() => Component(_ => TextBlock("owner").WithToolTip(s_counter)));
        var owner = host.All<TextBlock>()[0];
        Assert.Equal(["count 0"], Texts(Tip(owner)));

        s_counters[^1](3);
        host.Flush();
        Assert.Equal(["count 3"], Texts(Tip(owner)));
    }

    [AvaloniaFact]
    public void Removing_the_tooltip_unmounts_it_and_gives_the_slot_to_the_text_tooltip()
    {
        var log = new List<string>();
        var rich = true;
        var host = new TestHost(() =>
        {
            var owner = TextBlock("owner").ToolTip("plain");
            return rich ? owner.WithToolTip(Component(ctx =>
            {
                ctx.UseEffect(() => () => log.Add("cleanup"), Array.Empty<object>());
                return TextBlock("rich");
            })) : owner;
        });
        var text = host.All<TextBlock>()[0];
        Assert.Equal(["rich"], Texts(Tip(text))); // the element wins over the text

        rich = false;
        host.Render();
        Assert.Equal(["cleanup"], log);
        Assert.Equal("plain", AvToolTip.GetTip(text));

        rich = true;
        host.Render();
        Assert.Equal(["rich"], Texts(Tip(text)));
    }

    [AvaloniaFact]
    public void Removing_the_tooltip_without_a_text_one_clears_the_tip()
    {
        var rich = true;
        var host = new TestHost(() => rich ? TextBlock("owner").WithToolTip(TextBlock("rich")) : TextBlock("owner"));
        var text = host.All<TextBlock>()[0];
        rich = false;
        host.Render();
        Assert.Same(text, host.All<TextBlock>()[0]);
        Assert.Null(AvToolTip.GetTip(text));
    }

    [AvaloniaFact]
    public void Unmounting_the_control_unmounts_its_tooltip()
    {
        var log = new List<string>();
        var shown = true;
        var host = new TestHost(() => VStack(shown
            ? [Button("Save").WithToolTip(Component(ctx =>
            {
                ctx.UseEffect(() => () => log.Add("cleanup"), Array.Empty<object>());
                return TextBlock("tip");
            }))]
            : []));
        var button = host.All<Button>()[0];
        shown = false;
        host.Render();
        Assert.Equal(["cleanup"], log);
        Assert.Null(AvToolTip.GetTip(button));
    }

    [AvaloniaFact]
    public void A_tooltip_of_another_kind_replaces_the_old_one()
    {
        var log = new List<string>();
        var asText = true;
        var host = new TestHost(() => TextBlock("owner").WithToolTip(asText
            ? Component(ctx =>
            {
                ctx.UseEffect(() => () => log.Add("cleanup"), Array.Empty<object>());
                return TextBlock("text");
            })
            : Button("button")));
        var owner = host.All<TextBlock>()[0];
        asText = false;
        host.Render();
        Assert.IsType<Button>(Tip(owner));
        Assert.Equal(["cleanup"], log);
    }

    [AvaloniaFact]
    public void A_setter_tip_is_never_taken_for_the_element_tooltip()
    {
        // A tip a setter wrote over the element tooltip is left alone when the modifier goes.
        var rich = true;
        var host = new TestHost(() =>
        {
            var b = TextBlock("owner") with { Setters = [t => AvToolTip.SetTip(t, "from a setter")] };
            return rich ? b.WithToolTip(TextBlock("rich")) : b;
        });
        var text = host.All<TextBlock>()[0];
        rich = false;
        host.Render();
        Assert.Equal("from a setter", AvToolTip.GetTip(text));
    }
}
