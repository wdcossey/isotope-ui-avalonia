using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvFontFamily = Avalonia.Media.FontFamily;
using AvTextBlock = Avalonia.Controls.TextBlock;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Text styling (#42): upstream's TextBlock members and their modifiers, and the element-wide Foreground, FontSize,
/// FontWeight and FontFamily modifiers, with Avalonia's names and value types. Each member is checked on mount, on an
/// update that changes only it (so the skip arm must see it), and when it goes (the control's own value comes back).
/// </summary>
public sealed class TextStylingTests
{
    private static readonly AvFontFamily Mono = new("Courier New");
    private static readonly AvFontFamily Serif = new("Times New Roman");

    // Mounts first(), re-renders with second(), then with plain(); reads the value from the one control each time.
    private static void AssertSetChangedAndCleared<TControl, T>(Func<Element> first, Func<Element> second, Func<Element> plain,
        Func<TControl, T> read, T v1, T v2) where TControl : Control
    {
        var step = 0;
        var host = new TestHost(() => step switch { 0 => first(), 1 => second(), _ => plain() });
        var control = host.All<TControl>()[0];
        Assert.Equal(v1, read(control));

        step = 1;
        host.Render();
        Assert.Same(control, host.All<TControl>()[0]);
        Assert.Equal(v2, read(control));

        step = 2;
        host.Render();
        var fresh = new TestHost(plain).All<TControl>()[0];
        Assert.Equal(read(fresh), read(control));
    }

    private static void AssertTextBlock<T>(Func<TextBlockElement, TextBlockElement> set1, Func<TextBlockElement, TextBlockElement> set2,
        Func<TextBlock, T> read, T v1, T v2)
        => AssertSetChangedAndCleared(() => set1(TextBlock("a")), () => set2(TextBlock("a")), () => TextBlock("a"), read, v1, v2);

    // ── TextBlockElement members, through upstream's modifiers ─────────

    [AvaloniaFact]
    public void FontSize_member() =>
        AssertTextBlock(t => t.FontSize(20), t => t.FontSize(30), c => c.FontSize, 20.0, 30.0);

    [AvaloniaFact]
    public void FontWeight_member_from_Bold_and_SemiBold() =>
        AssertTextBlock(t => t.Bold(), t => t.SemiBold(), c => c.FontWeight, FontWeight.Bold, FontWeight.SemiBold);

    [AvaloniaFact]
    public void FontWeight_member_in_an_initializer() =>
        AssertTextBlock(t => t with { FontWeight = FontWeight.Light }, t => t with { FontWeight = FontWeight.Black },
            c => c.FontWeight, FontWeight.Light, FontWeight.Black);

    [AvaloniaFact]
    public void FontWeight_after_Bold_on_a_text_block_is_the_member_too() =>
        AssertTextBlock(t => t.Bold().FontWeight(FontWeight.Light), t => t.SemiBold().FontWeight(FontWeight.Black),
            c => c.FontWeight, FontWeight.Light, FontWeight.Black);

    [AvaloniaFact]
    public void FontStyle_member() =>
        AssertTextBlock(t => t.FontStyle(FontStyle.Italic), t => t.FontStyle(FontStyle.Oblique), c => c.FontStyle, FontStyle.Italic, FontStyle.Oblique);

    [AvaloniaFact]
    public void FontFamily_member_by_name_and_by_value() =>
        AssertTextBlock(t => t.FontFamily("Courier New"), t => t.FontFamily(Serif), c => c.FontFamily.Name, "Courier New", "Times New Roman");

    [AvaloniaFact]
    public void TextWrapping_member_wraps_by_default() =>
        AssertTextBlock(t => t.TextWrapping(), t => t.TextWrapping(TextWrapping.WrapWithOverflow), c => c.TextWrapping,
            TextWrapping.Wrap, TextWrapping.WrapWithOverflow);

    [AvaloniaFact]
    public void TextAlignment_member() =>
        AssertTextBlock(t => t.TextAlignment(TextAlignment.Center), t => t.TextAlignment(TextAlignment.Right), c => c.TextAlignment,
            TextAlignment.Center, TextAlignment.Right);

    [AvaloniaFact]
    public void TextTrimming_member() =>
        AssertTextBlock(t => t.TextTrimming(TextTrimming.CharacterEllipsis), t => t.TextTrimming(TextTrimming.WordEllipsis), c => c.TextTrimming,
            TextTrimming.CharacterEllipsis, TextTrimming.WordEllipsis);

    [AvaloniaFact]
    public void LineHeight_member() =>
        AssertTextBlock(t => t.LineHeight(24), t => t.LineHeight(32), c => c.LineHeight, 24.0, 32.0);

    [AvaloniaFact]
    public void MaxLines_member() =>
        AssertTextBlock(t => t.MaxLines(2), t => t.MaxLines(3), c => c.MaxLines, 2, 3);

    [AvaloniaFact]
    public void An_unset_MaxLines_is_not_written()
    {
        var block = new TestHost(() => TextBlock("a")).All<TextBlock>()[0];
        Assert.False(block.IsSet(AvTextBlock.MaxLinesProperty));
    }

    // ── Element-wide modifiers ──────────────────────────────────────────

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void Foreground_on_a_text_block_from_a_string_and_a_brush() =>
        AssertTextBlock(t => t.Foreground("Red"), t => t.Foreground(Brushes.Blue), c => c.Foreground is null ? default : ColorOf(c.Foreground),
            Colors.Red, Colors.Blue);

    [AvaloniaFact]
    public void Foreground_on_a_button() =>
        AssertSetChangedAndCleared<Button, Color?>(() => Button("Go").Foreground("#FF00FF00"), () => Button("Go").Foreground("Red"), () => Button("Go"),
            c => (c.Foreground as ISolidColorBrush)?.Color, Color.Parse("#FF00FF00"), Colors.Red);

    [AvaloniaFact]
    public void FontSize_on_a_button_and_a_text_box()
    {
        AssertSetChangedAndCleared<Button, double>(() => Button("Go").FontSize(20), () => Button("Go").FontSize(30), () => Button("Go"),
            c => c.FontSize, 20.0, 30.0);
        AssertSetChangedAndCleared<TextBox, double>(() => TextBox("x").FontSize(20), () => TextBox("x").FontSize(30), () => TextBox("x"),
            c => c.FontSize, 20.0, 30.0);
    }

    [AvaloniaFact]
    public void FontWeight_on_a_button() =>
        AssertSetChangedAndCleared<Button, FontWeight>(() => Button("Go").FontWeight(FontWeight.Bold), () => Button("Go").FontWeight(FontWeight.Thin),
            () => Button("Go"), c => c.FontWeight, FontWeight.Bold, FontWeight.Thin);

    [AvaloniaFact]
    public void FontFamily_on_a_text_box() =>
        AssertSetChangedAndCleared<TextBox, string>(() => TextBox("x").FontFamily("Courier New"), () => TextBox("x").FontFamily(Serif),
            () => TextBox("x"), c => c.FontFamily.Name, "Courier New", "Times New Roman");

    [AvaloniaFact]
    public void The_element_wide_font_modifiers_reach_a_text_block_too()
    {
        // Through the generic overloads, and as modifiers.
        AssertSetChangedAndCleared<TextBlock, double>(() => ((Element)TextBlock("a")).FontSize(20), () => ((Element)TextBlock("a")).FontSize(30),
            () => TextBlock("a"), c => c.FontSize, 20.0, 30.0);
        AssertSetChangedAndCleared<TextBlock, FontWeight>(() => ((Element)TextBlock("a")).FontWeight(FontWeight.Bold),
            () => ((Element)TextBlock("a")).FontWeight(FontWeight.Light), () => TextBlock("a"), c => c.FontWeight, FontWeight.Bold, FontWeight.Light);
        AssertSetChangedAndCleared<TextBlock, string>(() => ((Element)TextBlock("a")).FontFamily(Mono),
            () => ((Element)TextBlock("a")).FontFamily("Times New Roman"), () => TextBlock("a"), c => c.FontFamily.Name, "Courier New", "Times New Roman");
        AssertSetChangedAndCleared<TextBlock, double>(() => TextBlock("a").WithModifiers(m => m with { FontSize = 20 }),
            () => TextBlock("a").WithModifiers(m => m with { FontSize = 30 }), () => TextBlock("a"), c => c.FontSize, 20.0, 30.0);
        AssertSetChangedAndCleared<TextBlock, FontWeight>(() => TextBlock("a").WithModifiers(m => m with { FontWeight = FontWeight.Bold }),
            () => TextBlock("a").WithModifiers(m => m with { FontWeight = FontWeight.Light }), () => TextBlock("a"), c => c.FontWeight, FontWeight.Bold, FontWeight.Light);
        AssertSetChangedAndCleared<TextBlock, string>(() => TextBlock("a").WithModifiers(m => m with { FontFamily = Mono }),
            () => TextBlock("a").WithModifiers(m => m with { FontFamily = Serif }), () => TextBlock("a"), c => c.FontFamily.Name, "Courier New", "Times New Roman");
    }

    private sealed record Heading(string Title) : TextBlockElement(Title);

    [AvaloniaFact]
    public void The_last_font_call_wins_on_a_text_block_typed_as_Element_or_derived()
    {
        Element typed = TextBlock("a").Bold();
        var block = new TestHost(() => typed.FontWeight(FontWeight.Light)).All<TextBlock>()[0];
        Assert.Equal(FontWeight.Light, block.FontWeight);

        var heading = new TestHost(() => (new Heading("b") { FontSize = 14, FontFamily = Mono }).FontSize(20).FontFamily("Times New Roman"))
            .All<TextBlock>()[0];
        Assert.Equal((20.0, "Times New Roman"), (heading.FontSize, heading.FontFamily.Name));
    }

    [AvaloniaFact]
    public void On_a_text_block_the_member_wins_over_the_modifier_and_the_modifier_returns_when_the_member_goes()
    {
        var member = true;
        var modifier = 10.0;
        var host = new TestHost(() =>
        {
            var text = member ? TextBlock("a").FontSize(20) : TextBlock("a");
            return text.WithModifiers(m => m with { FontSize = modifier });
        });
        var block = host.All<TextBlock>()[0];
        Assert.Equal(20, block.FontSize);

        modifier = 12; // a changed modifier doesn't overwrite the member
        host.Render();
        Assert.Equal(20, block.FontSize);

        member = false; // the member goes: the modifier's value
        host.Render();
        Assert.Equal(12, block.FontSize);
    }

    [AvaloniaFact]
    public void A_wrapper_s_text_modifiers_merge_with_the_inner_element_s()
    {
        var inner = TextBlock("a").Margin(4);
        var wrapped = new ModifiedElement(inner, new ElementModifiers
        {
            Foreground = Brushes.Red, FontSize = 21, FontWeight = FontWeight.Bold, FontFamily = Mono,
        });
        var block = new TestHost(() => wrapped).All<TextBlock>()[0];
        Assert.Equal(Colors.Red, ColorOf(block.Foreground));
        Assert.Equal(21, block.FontSize);
        Assert.Equal(FontWeight.Bold, block.FontWeight);
        Assert.Equal("Courier New", block.FontFamily.Name);
        Assert.Equal(new global::Avalonia.Thickness(4), block.Margin);
    }

    [AvaloniaFact]
    public void The_modifiers_do_nothing_on_a_panel_as_upstream_s()
    {
        var host = new TestHost(() => VStack(TextBlock("a")).Foreground("Red").FontSize(30));
        var panel = host.All<StackPanel>()[0];
        Assert.False(panel.IsSet(AvTextBlock.ForegroundProperty));
        Assert.False(panel.IsSet(AvTextBlock.FontSizeProperty));
        Assert.NotEqual(30, host.All<TextBlock>()[0].FontSize);
    }

    [AvaloniaFact]
    public void An_unchanged_styled_text_block_is_skipped()
    {
        // Each render parses a new brush and font family: they compare by value, so the update is still skipped.
        var host = new TestHost(() => TextBlock("a").Foreground("Red").FontFamily("Courier New").Bold().FontSize(14)
            .TextTrimming(TextTrimming.CharacterEllipsis));
        var before = host.Reconciler.DebugElementsSkipped;
        host.Render();
        Assert.True(host.Reconciler.DebugElementsSkipped > before, "the text block was not skipped");
    }

    [AvaloniaFact]
    public void The_guide_s_example_styles_its_text_and_button()
    {
        var host = new TestHost(() => TextStylingExamples.Notice("Update", "A new version is ready.", urgent: true, () => { }));
        var (title, body) = (host.All<TextBlock>()[0], host.All<TextBlock>()[1]);
        Assert.Equal((20.0, FontWeight.SemiBold), (title.FontSize, title.FontWeight));
        Assert.Equal((TextWrapping.Wrap, 3, TextTrimming.CharacterEllipsis), (body.TextWrapping, body.MaxLines, body.TextTrimming));
        Assert.Equal(Colors.Red, ColorOf(host.All<Button>()[0].Foreground));
    }
}
