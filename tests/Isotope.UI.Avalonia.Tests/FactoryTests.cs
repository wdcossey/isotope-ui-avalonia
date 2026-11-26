using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core;
using Isotope.UI.Layout;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Each public factory and modifier builds the element it promises (#17).</summary>
public class FactoryTests
{
    private sealed class Widget : Core.Component
    {
        public override Element Render() => TextBlock("widget");
    }

    private sealed class Labelled : Component<string>
    {
        public override Element Render() => TextBlock(Props);
    }

    [Fact]
    public void TextBlock_builds_a_text_block()
        => Assert.Equal("hi", TextBlock("hi").Text);

    [Fact]
    public void Button_builds_a_button_with_its_label_and_click()
    {
        Action click = () => { };
        var b = Button("Go", click);
        Assert.Equal("Go", b.Content);
        Assert.Same(click, b.OnClick);
        Assert.Null(Button("Go").OnClick);
    }

    [Fact]
    public void TextBox_builds_a_text_box_controlled_or_not()
    {
        Action<string> changed = _ => { };
        var t = TextBox("x", changed, placeholderText: "type");
        Assert.Equal(Optional<string>.Of("x"), t.Text);
        Assert.Same(changed, t.OnTextChanged);
        Assert.Equal("type", t.PlaceholderText);
        Assert.False(TextBox().Text.HasValue);
    }

    [Fact]
    public void Slider_builds_a_slider_with_its_range()
    {
        Action<double> changed = _ => { };
        var s = Slider(5, 1, 10, changed);
        Assert.Equal(Optional<double>.Of(5), s.Value);
        Assert.Equal((1.0, 10.0), (s.Minimum, s.Maximum));
        Assert.Same(changed, s.OnValueChanged);
        var d = Slider();
        Assert.Equal((false, 0.0, 100.0), (d.Value.HasValue, d.Minimum, d.Maximum));
    }

    // A headless test: it makes an Avalonia control, which takes the calling thread's dispatcher (#105).
    [AvaloniaFact]
    public void Factories_named_like_Avalonia_controls_are_the_factories_in_calls_and_the_controls_as_types()
    {
        // This file imports Avalonia.Controls and the factories (#46). A call names the factory, a type names Avalonia's
        // control, and the alignment modifiers leave the enums' simple names alone.
        var tabs = TabControl(TabItem("A", NumericUpDown(1)), TabItem("B", AutoCompleteBox("x")));
        Assert.Equal(["A", "B"], tabs.Items.Select(t => t.Header));
        TabControl control = new TabControl();
        Assert.IsType<global::Avalonia.Controls.TabControl>(control);
        Assert.Equal(typeof(global::Avalonia.Controls.ProgressBar), typeof(ProgressBar));
        var bar = ProgressBar(0.5).HorizontalAlignment(HorizontalAlignment.Center).VerticalAlignment(VerticalAlignment.Bottom);
        Assert.Equal((HorizontalAlignment.Center, VerticalAlignment.Bottom), (bar.Modifiers?.HorizontalAlignment, bar.Modifiers?.VerticalAlignment));
    }

    [Fact]
    public void VStack_and_HStack_build_stacks_with_default_or_given_spacing()
    {
        Element a = TextBlock("a"), b = TextBlock("b");
        var v = VStack(a, b);
        Assert.Equal((Orientation.Vertical, 8.0), (v.Orientation, v.Spacing));
        Assert.Equal([a, b], v.Children);
        Assert.Equal((Orientation.Vertical, 2.0), (VStack(2, a).Orientation, VStack(2, a).Spacing));
        var h = HStack(a, b);
        Assert.Equal((Orientation.Horizontal, 8.0), (h.Orientation, h.Spacing));
        Assert.Equal([a, b], h.Children);
        Assert.Equal((Orientation.Horizontal, 3.0), (HStack(3, a).Orientation, HStack(3, a).Spacing));
        Assert.Equal([a], HStack(3, a).Children);
    }

    [Fact]
    public void Flex_factories_build_flex_containers_in_their_direction()
    {
        Element a = TextBlock("a");
        Assert.Equal(FlexDirection.Row, Flex(a).Direction);
        Assert.Equal([a], Flex(a).Children);
        Assert.Equal(FlexDirection.ColumnReverse, Flex(FlexDirection.ColumnReverse, a).Direction);
        Assert.Equal([a], Flex(FlexDirection.ColumnReverse, a).Children);
        Assert.Equal(FlexDirection.Row, FlexRow(a).Direction);
        Assert.Equal([a], FlexRow(a).Children);
        Assert.Equal(FlexDirection.Column, FlexColumn(a).Direction);
        Assert.Equal([a], FlexColumn(a).Children);
    }

    [Fact]
    public void Component_and_RenderEachTime_build_function_components()
    {
        Func<RenderContext, Element> render = _ => TextBlock("x");
        Assert.Same(render, Component(render).RenderFunc);
        Assert.Same(render, RenderEachTime(render).RenderFunc);
    }

    [AvaloniaFact]
    public void Component_of_a_class_builds_and_mounts_it()
    {
        var c = Component<Widget>();
        Assert.Equal(typeof(Widget), c.ComponentType);
        Assert.IsType<Widget>(c.CreateInstance());
        Assert.Equal(["widget"], new TestHost(() => c).Texts());
    }

    [AvaloniaFact]
    public void Component_of_a_class_with_props_builds_and_mounts_it()
    {
        var c = Component<Labelled, string>("hello");
        Assert.Equal(typeof(Labelled), c.ComponentType);
        Assert.Equal("hello", c.Props);
        Assert.IsType<Labelled>(c.CreateInstance());
        Assert.Equal(["hello"], new TestHost(() => c).Texts());
    }

    [Fact]
    public void Memo_keeps_its_dependencies_and_none_means_null()
    {
        Func<RenderContext, Element> render = _ => TextBlock("x");
        var m = Memo(render, 1, "a");
        Assert.Same(render, m.RenderFunc);
        Assert.Equal([1, "a"], m.Dependencies!);
        Assert.Null(Memo(render).Dependencies);
    }

    [Fact]
    public void Group_and_Empty()
    {
        Element a = TextBlock("a");
        Assert.Equal([a], Assert.IsType<GroupElement>(Group(a, null)).Children);
        Assert.Same(EmptyElement.Instance, Empty());
    }

    [Fact]
    public void Children_drop_null_and_empty_and_flatten_groups()
    {
        Element a = TextBlock("a"), b = TextBlock("b"), c = TextBlock("c");
        Assert.Equal([a, b, c], VStack(a, null, Empty(), Group(b, c)).Children);
        Assert.Equal([a, b], Flex(null, a, Group(b)).Children);
        Assert.Equal([a], HStack(a, Empty()).Children); // an empty child alone is dropped too
        Element?[] kept = [a, b];
        Assert.Same(kept, VStack(kept).Children); // nothing dropped: the caller's array, as upstream's
    }

    [AvaloniaFact]
    public void A_conditional_child_keeps_its_unkeyed_siblings_controls()
    {
        var show = true;
        var host = new TestHost(() => VStack(show ? TextBlock("banner") : null, TextBox(), TextBlock("end")));
        var box = host.All<TextBox>()[0];
        var end = host.All<TextBlock>().Single(t => t.Text == "end");

        show = false;
        host.Render();
        Assert.Same(box, host.All<TextBox>()[0]);
        Assert.Same(end, host.All<TextBlock>().Single(t => t.Text == "end"));

        show = true;
        host.Render();
        Assert.Same(box, host.All<TextBox>()[0]);
        Assert.Equal(["banner", "end"], host.Texts());
    }

    [Fact]
    public void WithKey_sets_the_key()
    {
        Assert.Equal("k", TextBlock("a").WithKey("k").Key);
        var keyed = TextBlock("a").WithKey("k");
        Assert.Same(keyed, keyed.WithKey(keyed.Key!));
    }

    [Fact]
    public void Margin_overloads_build_the_thickness()
    {
        Assert.Equal(new Thickness(4), TextBlock("a").Margin(4).Modifiers!.Margin);
        Assert.Equal(new Thickness(1, 2, 1, 2), TextBlock("a").Margin(1, 2).Modifiers!.Margin);
        Assert.Equal(new Thickness(0, 10, 0, 0), TextBlock("a").Margin(top: 10).Modifiers!.Margin);
        Assert.Equal(new Thickness(1, 2, 3, 4), TextBlock("a").Margin(1, 2, 3, 4).Modifiers!.Margin);
        Assert.Equal(new Thickness(5, 6, 7, 8), TextBlock("a").Margin(new Thickness(5, 6, 7, 8)).Modifiers!.Margin);
    }

    [Fact]
    public void Size_and_alignment_modifiers()
    {
        var m = TextBlock("a").Width(10).Height(20).Modifiers!;
        Assert.Equal((10.0, 20.0), (m.Width, m.Height));
        var s = TextBlock("a").Size(30, 40).Modifiers!;
        Assert.Equal((30.0, 40.0), (s.Width, s.Height));
        var al = TextBlock("a").HorizontalAlignment(HorizontalAlignment.Right).VerticalAlignment(VerticalAlignment.Bottom).Modifiers!;
        Assert.Equal((HorizontalAlignment.Right, VerticalAlignment.Bottom), (al.HorizontalAlignment, al.VerticalAlignment));
    }

    [Fact]
    public void Visual_and_state_modifiers()
    {
        Assert.Equal(0.5, TextBlock("a").Opacity(0.5).Modifiers!.Opacity);
        Assert.False(Button("a").IsEnabled(false).Modifiers!.IsEnabled);
        Assert.True(Button("a").IsEnabled().Modifiers!.IsEnabled);
        Assert.Same(Brushes.Red, TextBlock("a").Background(Brushes.Red).Modifiers!.Background);
        Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(TextBlock("a").Background("#FF0000").Modifiers!.Background).Color);
        Assert.Equal("Save", Button("a").AutomationName("Save").Modifiers!.AutomationName);
        // A colour string parses to an equal brush each render, so an unchanged element can still be skipped.
        Assert.True(Element.ModifiersEqual(TextBlock("a").Background("#FF0000").Modifiers, TextBlock("a").Background("#FF0000").Modifiers));
        Assert.True(Element.ModifiersEqual(TextBlock("a").Background("LightGray").Modifiers, TextBlock("a").Background("LightGray").Modifiers));
    }

    [Fact]
    public void Modifiers_chain_and_keep_the_element_type()
    {
        ButtonElement b = Button("a").Margin(2).Width(5).Opacity(0.5).WithKey("k");
        Assert.Equal(new ElementModifiers { Margin = new Thickness(2), Width = 5, Opacity = 0.5 }, b.Modifiers);
        Assert.Equal("k", b.Key);
    }

    [AvaloniaFact]
    public void Modifiers_reach_the_mounted_control()
    {
        var host = new TestHost(() => TextBlock("a").Margin(3).Width(40).AutomationName("label"));
        var t = host.All<TextBlock>()[0];
        Assert.Equal((new Thickness(3), 40.0), (t.Margin, t.Width));
        Assert.Equal("label", global::Avalonia.Automation.AutomationProperties.GetName(t));
    }
}
