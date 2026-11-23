using Avalonia;
using Isotope.UI.Core;
using Isotope.UI.Elements;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>The skip contract on <see cref="Element"/> that upstream's child diff relies on.</summary>
public class ElementTests
{
    [Fact]
    public void Equal_leaves_can_skip_and_different_ones_cannot()
    {
        Assert.True(Element.CanSkipUpdate(TextBlock("a"), TextBlock("a")));
        Assert.False(Element.CanSkipUpdate(TextBlock("a"), TextBlock("b")));
    }

    [Fact]
    public void Delegates_are_ignored_but_a_change_in_callback_presence_declines_the_skip()
    {
        Assert.True(Element.CanSkipUpdate(new ButtonElement("x", () => { }), new ButtonElement("x", () => { })));
        Assert.False(Element.CanSkipUpdate(new ButtonElement("x"), new ButtonElement("x", () => { })));
    }

    [Fact]
    public void A_modifier_or_key_change_declines_the_skip()
    {
        var plain = TextBlock("a");
        Assert.False(Element.CanSkipUpdate(plain, plain with { Modifiers = new ElementModifiers { Margin = new Thickness(4) } }));
        Assert.True(Element.CanSkipUpdate(
            plain with { Modifiers = new ElementModifiers { Margin = new Thickness(4) } },
            plain with { Modifiers = new ElementModifiers { Margin = new Thickness(4) } }));
        Assert.False(Element.CanSkipUpdate(plain, plain with { Key = "k" }));
    }

    [Fact]
    public void A_theme_sensitive_element_always_goes_through_update()
    {
        var themed = TextBlock("a") with
        {
            ResourceOverrides = new ResourceOverrides(
                new Dictionary<string, object>(),
                new Dictionary<string, ThemeRef> { ["Fg"] = new ThemeRef("SystemAccentColor") }),
        };
        Assert.False(Element.CanSkipUpdate(themed, themed with { }));
    }

    [Fact]
    public void Containers_compare_children_by_reference()
    {
        Element[] children = [TextBlock("a")];
        Assert.True(Element.CanSkipUpdate(VStack(children), VStack(children)));
        Assert.False(Element.CanSkipUpdate(VStack(children), VStack([TextBlock("a")])));
    }

    [Fact]
    public void Components_only_skip_as_the_same_instance()
    {
        Func<RenderContext, Element> render = _ => TextBlock("a");
        var func = new FuncElement(render);
        Assert.True(Element.CanSkipUpdate(func, func));
        Assert.False(Element.CanSkipUpdate(func, new FuncElement(render)));
    }

    [Fact]
    public void Provide_adds_a_context_value_and_leaves_the_original_alone()
    {
        var ctx = new Context<int>(0);
        var plain = TextBlock("a");
        var provided = plain.Provide(ctx, 7);
        Assert.Null(plain.ContextValues);
        Assert.Equal(7, provided.ContextValues![ctx]);
    }

    [Fact]
    public void Merge_lets_the_other_modifiers_win()
    {
        var merged = new ElementModifiers { Width = 10, Height = 20 }.Merge(new ElementModifiers { Width = 30 });
        Assert.Equal(30, merged.Width);
        Assert.Equal(20, merged.Height);
    }
}
