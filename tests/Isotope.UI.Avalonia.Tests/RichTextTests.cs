using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using System.Diagnostics.Tracing;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;
using AvTextBlock = Avalonia.Controls.TextBlock;
using AvSelectableTextBlock = Avalonia.Controls.SelectableTextBlock;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Rich text (#65): inline elements in a text block's Inlines, and the selectable text block with upstream's paragraphs.
/// Each inline list is diffed as a panel's children are: mounted, updated in place, matched by key and moved, and styled with
/// the text modifiers; an inline UI container's child is a child slot whose component keeps its state.
/// </summary>
public sealed class RichTextTests
{
    // ── helpers ─────────────────────────────────────────────────────

    private static AvTextBlock TextOf(TestHost host) => host.All<AvTextBlock>()[0];

    private static InlineCollection InlinesOf(TestHost host) => TextOf(host).Inlines!;

    // A readable picture of an inline list: runs as their text, spans as Kind(...), a line break as /, inline UI as [Type].
    private static string Describe(InlineCollection inlines) => string.Join(" ", inlines.Select(Describe));

    private static string Describe(Inline inline) => inline switch
    {
        Run run => run.Text ?? "",
        LineBreak _ => "/",
        InlineUIContainer ui => "[" + ui.Child?.GetType().Name + "]",
        Bold b => "Bold(" + Describe(b.Inlines) + ")",
        Italic i => "Italic(" + Describe(i.Inlines) + ")",
        Underline u => "Underline(" + Describe(u.Inlines) + ")",
        Span s => "Span(" + Describe(s.Inlines) + ")",
        _ => inline.GetType().Name,
    };

    private static Button UIChild(InlineCollection inlines, int index) => Assert.IsType<Button>(((InlineUIContainer)inlines[index]).Child);

    // ── mount ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_text_block_mounts_its_inlines_and_no_text()
    {
        var host = new TestHost(() => TextBlock("Hello, ", Bold("big ", Italic("wide")), LineBreak(), Underline(Span("world")),
            InlineUIContainer(Button("Go"))));
        var block = TextOf(host);
        Assert.Equal("Hello,  Bold(big  Italic(wide)) / Underline(Span(world)) [Button]", Describe(block.Inlines!));
        Assert.Null(block.Text);
        Assert.Equal("Go", UIChild(block.Inlines!, 4).Content);
    }

    [AvaloniaFact]
    public void A_null_inline_is_dropped_and_a_string_is_a_run()
    {
        var host = new TestHost(() => TextBlock("a", null, Run("b"), false ? "c" : null));
        Assert.Equal("a b", Describe(InlinesOf(host)));
    }

    [AvaloniaFact]
    public void An_inline_outside_a_text_block_is_reported()
    {
        var error = Assert.Throws<NotSupportedException>(() => new TestHost(() => VStack(Run("x"))));
        Assert.Contains("RunElement is an inline", error.Message);
    }

    // ── update in place ─────────────────────────────────────────────

    [AvaloniaFact]
    public void A_re_render_updates_the_same_inlines_in_place()
    {
        var who = "world";
        var host = new TestHost(() => TextBlock("Hello, ", Bold(who), "!"));
        var block = TextOf(host);
        var before = block.Inlines!.ToArray();
        var bold = (Bold)before[1];
        var boldRun = bold.Inlines[0];

        who = "there";
        host.Render();
        Assert.Same(block, TextOf(host));
        Assert.Equal("Hello,  Bold(there) !", Describe(block.Inlines!));
        Assert.Equal(before, block.Inlines!.ToArray());
        Assert.Same(boldRun, bold.Inlines[0]);
    }

    [AvaloniaFact]
    public void An_inline_of_another_type_is_replaced_and_the_others_kept()
    {
        var bold = true;
        var host = new TestHost(() => TextBlock("a", bold ? Bold("b") : Italic("b"), "c"));
        var before = InlinesOf(host).ToArray();

        bold = false;
        host.Render();
        var after = InlinesOf(host).ToArray();
        Assert.Equal("a Italic(b) c", Describe(InlinesOf(host)));
        Assert.Same(before[0], after[0]);
        Assert.NotSame(before[1], after[1]);
        Assert.Same(before[2], after[2]);
    }

    [AvaloniaFact]
    public void Inlines_added_and_removed_at_the_end_keep_the_rest()
    {
        string[] words = ["a", "b"];
        var host = new TestHost(() => TextBlock(words.Select(w => (InlineElement?)w).ToArray()));
        var first = InlinesOf(host)[0];

        words = ["a", "b", "c"];
        host.Render();
        Assert.Equal("a b c", Describe(InlinesOf(host)));
        Assert.Same(first, InlinesOf(host)[0]);

        words = ["a"];
        host.Render();
        Assert.Equal("a", Describe(InlinesOf(host)));
        Assert.Same(first, InlinesOf(host)[0]);
    }

    [AvaloniaFact]
    public void Text_and_inlines_replace_each_other_on_the_same_control()
    {
        var step = 0;
        var host = new TestHost(() => step switch
        {
            0 => TextBlock("plain"),
            1 => TextBlock("rich ", Bold("text")),
            _ => TextBlock("plain again"),
        });
        var block = TextOf(host);
        Assert.Equal("plain", block.Text);

        step = 1;
        host.Render();
        Assert.Same(block, TextOf(host));
        Assert.Null(block.Text);
        Assert.Equal("rich  Bold(text)", Describe(block.Inlines!));

        step = 2;
        host.Render();
        Assert.Same(block, TextOf(host));
        Assert.Equal("plain again", block.Text);
        Assert.Empty(block.Inlines!);
    }

    [AvaloniaFact]
    public void An_unchanged_inlines_array_is_skipped_and_a_new_one_updated()
    {
        InlineElement[] inlines = [Run("a"), Bold("b")];
        var host = new TestHost(() => VStack(new TextBlockElement("") { Inlines = inlines }));
        var skipped = host.Reconciler.DebugElementsSkipped;
        host.Render();
        Assert.True(host.Reconciler.DebugElementsSkipped > skipped, "the text block was not skipped");

        inlines = [Run("a"), Bold("c")];
        host.Render();
        Assert.Equal("a Bold(c)", Describe(InlinesOf(host)));
    }

    // ── keyed reorder ───────────────────────────────────────────────

    [AvaloniaFact]
    public void Keyed_inlines_move_with_their_keys()
    {
        string[] order = ["a", "b", "c", "d"];
        var host = new TestHost(() => TextBlock(order.Select(k => (InlineElement?)Run(k).WithKey(k)).ToArray()));
        var before = InlinesOf(host).ToDictionary(i => ((Run)i).Text!);

        order = ["d", "b", "a", "c"];
        host.Render();
        Assert.Equal("d b a c", Describe(InlinesOf(host)));
        Assert.All(order, k => Assert.Same(before[k], InlinesOf(host).Single(i => ((Run)i).Text == k)));

        order = ["c", "e", "a"];
        host.Render();
        Assert.Equal("c e a", Describe(InlinesOf(host)));
        Assert.Same(before["c"], InlinesOf(host)[0]);
        Assert.Same(before["a"], InlinesOf(host)[2]);
    }

    [AvaloniaFact]
    public void Unkeyed_inlines_keep_their_places_among_moving_keyed_ones()
    {
        var swap = false;
        var host = new TestHost(() => swap
            ? TextBlock(Run("y").WithKey("y"), "plain", Run("x").WithKey("x"))
            : TextBlock(Run("x").WithKey("x"), "plain", Run("y").WithKey("y")));
        var (x, plain, y) = (InlinesOf(host)[0], InlinesOf(host)[1], InlinesOf(host)[2]);

        swap = true;
        host.Render();
        Assert.Equal([y, plain, x], InlinesOf(host).ToArray());
    }

    [AvaloniaFact]
    public void A_keyed_inline_element_moves_with_its_component_state()
    {
        string[] order = ["a", "b"];
        var host = new TestHost(() => TextBlock(order.Select(k => (InlineElement?)InlineUIContainer(Component(Counter)).WithKey(k)).ToArray()));
        var a = CounterButton(InlinesOf(host), 0);
        TestHost.Click(a);
        host.Flush();
        Assert.Equal("1", CounterButton(InlinesOf(host), 0).Content);

        order = ["b", "a"];
        host.Render();
        Assert.Same(a, CounterButton(InlinesOf(host), 1));
        Assert.Equal("1", a.Content);
        Assert.Equal("0", CounterButton(InlinesOf(host), 0).Content);
    }

    // A button that counts its clicks, as a component inside an inline (the container's child is the component's border).
    private static Element Counter(RenderContext ctx)
    {
        var (count, setCount) = ctx.UseState(0);
        return Button($"{count}", () => setCount(count + 1));
    }

    // ── inline UI ───────────────────────────────────────────────────

    private static Button CounterButton(InlineCollection inlines, int index) =>
        Assert.IsType<Button>(Assert.IsType<Border>(((InlineUIContainer)inlines[index]).Child).Child);

    [AvaloniaFact]
    public void A_component_in_an_inline_keeps_its_state_across_renders_of_its_text()
    {
        var label = "first";
        var host = new TestHost(() => TextBlock(label + " ", InlineUIContainer(Component(Counter))));
        var button = CounterButton(InlinesOf(host), 1);
        TestHost.Click(button);
        host.Flush();
        Assert.Equal("1", button.Content);

        label = "second";
        host.Render();
        Assert.Equal("second ", ((Run)InlinesOf(host)[0]).Text);
        Assert.Same(button, CounterButton(InlinesOf(host), 1));
        Assert.Equal("1", button.Content);
    }

    [AvaloniaFact]
    public void A_state_change_in_an_inline_component_reaches_it_past_an_unchanged_text_block()
    {
        // The text block's element is the same instance every render, so only the dirty path leads the pass into it.
        var text = TextBlock("Count: ", InlineUIContainer(Component(Counter)));
        var host = new TestHost(() => VStack(text));
        var button = CounterButton(InlinesOf(host), 1);

        TestHost.Click(button);
        host.Flush();
        Assert.Equal("1", button.Content);
        TestHost.Click(button);
        host.Flush();
        Assert.Equal("2", button.Content);
    }

    private static readonly Context<string> Label = new("none");

    [AvaloniaFact]
    public void A_context_change_reaches_a_component_in_an_inline_past_an_unchanged_text_block()
    {
        var text = TextBlock("Label: ", InlineUIContainer(Component(c => Button(c.UseContext(Label)))));
        var value = "one";
        var host = new TestHost(() => VStack(text).Provide(Label, value));
        Assert.Equal("one", CounterButton(InlinesOf(host), 1).Content);

        value = "two";
        host.Render();
        Assert.Equal("two", CounterButton(InlinesOf(host), 1).Content);
    }

    [AvaloniaFact]
    public void A_component_in_an_inline_is_cleaned_up_when_its_inline_or_its_text_block_goes()
    {
        var log = new List<string>();
        Element Tracked(RenderContext ctx)
        {
            ctx.UseEffect(() => () => log.Add("cleanup"), Array.Empty<object>());
            return Button("x");
        }

        var step = 0;
        var host = new TestHost(() => step switch
        {
            0 => TextBlock("a", Span(InlineUIContainer(Component(Tracked)))),
            1 => TextBlock("a"),
            2 => TextBlock("b", InlineUIContainer(Component(Tracked))),
            _ => VStack(),
        });
        Assert.Empty(log);

        step = 1; // the inline goes
        host.Render();
        Assert.Equal(["cleanup"], log);

        step = 2;
        host.Render();
        step = 3; // the text block goes
        host.Render();
        Assert.Equal(["cleanup", "cleanup"], log);
    }

    [AvaloniaFact]
    public void An_inline_child_s_element_can_change_type()
    {
        var button = true;
        var host = new TestHost(() => TextBlock(InlineUIContainer(button ? Button("b") : TextBlock("t"))));
        Assert.IsType<Button>(((InlineUIContainer)InlinesOf(host)[0]).Child);

        button = false;
        host.Render();
        Assert.Equal("t", Assert.IsType<AvTextBlock>(((InlineUIContainer)InlinesOf(host)[0]).Child).Text);
    }

    // ── styling ─────────────────────────────────────────────────────

    // Mounts first(), re-renders with second(), then with a plain run; reads the value from the one run each time, and the
    // property is cleared at the end, so the run inherits again.
    private static void AssertRunStyled<T>(Func<RunElement, InlineElement> first, Func<RunElement, InlineElement> second,
        AvaloniaProperty property, Func<Run, T> read, T v1, T v2)
    {
        var step = 0;
        var host = new TestHost(() => TextBlock(step switch { 0 => first(Run("a")), 1 => second(Run("a")), _ => Run("a") }));
        var run = (Run)InlinesOf(host)[0];
        Assert.Equal(v1, read(run));

        step = 1;
        host.Render();
        Assert.Same(run, InlinesOf(host)[0]);
        Assert.Equal(v2, read(run));

        step = 2;
        host.Render();
        Assert.Same(run, InlinesOf(host)[0]);
        Assert.False(run.IsSet(property), property.Name + " is still set");
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void FontSize_on_an_inline() =>
        AssertRunStyled(r => r.FontSize(20), r => r.FontSize(30), TextElement.FontSizeProperty, r => r.FontSize, 20.0, 30.0);

    [AvaloniaFact]
    public void FontWeight_from_Bold_SemiBold_and_FontWeight() =>
        AssertRunStyled(r => r.Bold(), r => r.SemiBold().FontWeight(FontWeight.Light), TextElement.FontWeightProperty, r => r.FontWeight,
            FontWeight.Bold, FontWeight.Light);

    [AvaloniaFact]
    public void FontStyle_from_Italic_and_FontStyle() =>
        AssertRunStyled(r => r.Italic(), r => r.FontStyle(FontStyle.Oblique), TextElement.FontStyleProperty, r => r.FontStyle,
            FontStyle.Italic, FontStyle.Oblique);

    [AvaloniaFact]
    public void FontFamily_and_FontStretch_on_an_inline()
    {
        AssertRunStyled(r => r.FontFamily("Courier New"), r => r.FontFamily("Times New Roman"), TextElement.FontFamilyProperty,
            r => r.FontFamily.Name, "Courier New", "Times New Roman");
        AssertRunStyled(r => r.FontStretch(FontStretch.Condensed), r => r.FontStretch(FontStretch.Expanded), TextElement.FontStretchProperty,
            r => r.FontStretch, FontStretch.Condensed, FontStretch.Expanded);
    }

    [AvaloniaFact]
    public void Foreground_and_Background_on_an_inline()
    {
        AssertRunStyled(r => r.Foreground("Red"), r => r.Foreground(Brushes.Blue), TextElement.ForegroundProperty,
            r => ColorOf(r.Foreground), Colors.Red, Colors.Blue);
        AssertRunStyled(r => r.Background("Yellow"), r => r.Background(Brushes.Green), TextElement.BackgroundProperty,
            r => ColorOf(r.Background), Colors.Yellow, Colors.Green);
    }

    [AvaloniaFact]
    public void Decorations_letter_spacing_and_baseline_on_an_inline()
    {
        AssertRunStyled(r => r.Underline(), r => r.Strikethrough(), Inline.TextDecorationsProperty,
            r => r.TextDecorations!.Single().Location, TextDecorationLocation.Underline, TextDecorationLocation.Strikethrough);
        AssertRunStyled(r => r.LetterSpacing(2), r => r.LetterSpacing(3), TextElement.LetterSpacingProperty, r => r.LetterSpacing, 2.0, 3.0);
        AssertRunStyled(r => r.BaselineAlignment(BaselineAlignment.Superscript), r => r.BaselineAlignment(BaselineAlignment.Subscript),
            Inline.BaselineAlignmentProperty, r => r.BaselineAlignment, BaselineAlignment.Superscript, BaselineAlignment.Subscript);
    }

    [AvaloniaFact]
    public void Underline_and_Strikethrough_add_to_each_other()
    {
        var run = (Run)InlinesOf(new TestHost(() => TextBlock(Run("a").Underline().Strikethrough())))[0];
        Assert.Equal([TextDecorationLocation.Underline, TextDecorationLocation.Strikethrough], run.TextDecorations!.Select(d => d.Location));
        run = (Run)InlinesOf(new TestHost(() => TextBlock(Run("a").Strikethrough().Strikethrough())))[0];
        Assert.Equal([TextDecorationLocation.Strikethrough], run.TextDecorations!.Select(d => d.Location));
        // An Underline span is underlined by default: struck through, it is both.
        var span = (Underline)InlinesOf(new TestHost(() => TextBlock(Underline("a").Strikethrough())))[0];
        Assert.Equal([TextDecorationLocation.Underline, TextDecorationLocation.Strikethrough], span.TextDecorations!.Select(d => d.Location));
    }

    [AvaloniaFact]
    public void Equal_decorations_built_afresh_are_not_written_again()
    {
        // Avalonia lays the text out again on any inline change, so a write of the same decorations isn't free.
        var host = new TestHost(() => TextBlock(Run("a").Underline().Strikethrough()));
        var run = (Run)InlinesOf(host)[0];
        var writes = 0;
        run.PropertyChanged += (_, e) => { if (e.Property == Inline.TextDecorationsProperty) writes++; };
        host.Render();
        Assert.Equal(0, writes);
    }

    [AvaloniaFact]
    public void An_inline_takes_the_element_wide_modifiers_where_its_members_are_unset()
    {
        AssertRunStyled(r => r.WithModifiers(m => m with { FontSize = 18, Foreground = Brushes.Red }),
            r => r.WithModifiers(m => m with { FontSize = 24, Foreground = Brushes.Blue }), TextElement.FontSizeProperty, r => (r.FontSize, ColorOf(r.Foreground)),
            (18.0, Colors.Red), (24.0, Colors.Blue));
        // The member wins over the modifier.
        var run = (Run)InlinesOf(new TestHost(() => TextBlock(Run("a").FontSize(12).WithModifiers(m => m with { FontSize = 40 }))))[0];
        Assert.Equal(12, run.FontSize);
    }

    [AvaloniaFact]
    public void A_span_s_style_is_inherited_by_its_inlines()
    {
        var host = new TestHost(() => TextBlock(Span("a", Run("b").FontSize(30)).FontSize(20).Foreground("Red")));
        var span = (Span)InlinesOf(host)[0];
        Assert.Equal(20, span.FontSize);
        Assert.Equal((20.0, 30.0), (span.Inlines[0].FontSize, span.Inlines[1].FontSize));
        Assert.Equal(Colors.Red, ColorOf(span.Inlines[0].Foreground));
    }

    private sealed record MarginRun(string Words) : RunElement(Words);

    [AvaloniaFact]
    public void A_modifier_an_inline_doesn_t_take_is_reported_once_per_inline_type()
    {
        using var listener = new WarningListener();
        var host = new TestHost(() => TextBlock(new MarginRun("a").Margin(4), new MarginRun("b").ToolTip("tip"), Run("c").FontSize(20)));
        host.Render();
        Assert.Single(listener.Of(nameof(MarginRun)));
        Assert.Empty(listener.Of(nameof(RunElement) + " has"));
    }

    internal sealed class WarningListener : EventListener
    {
        private readonly List<string> _messages = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Isotope-UI") EnableEvents(source, EventLevel.Warning, (EventKeywords)0x20);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == "Warning" && e.Payload is { } payload)
                lock (_messages) _messages.Add(string.Join(" | ", payload));
        }

        public List<string> Of(string text)
        {
            lock (_messages) return _messages.Where(s => s.Contains(text, StringComparison.Ordinal)).ToList();
        }
    }

    [AvaloniaFact]
    public void Inlines_a_setter_replaced_are_rebuilt_not_cast()
    {
        // A setter that swaps the bold span for a run leaves a collection the elements can't be matched to.
        var word = "a";
        var host = new TestHost(() => new TextBlockElement("")
        {
            Inlines = [Bold(word)],
            Setters = [tb => { if (tb.Inlines![0] is Bold) tb.Inlines[0] = new Run("swapped"); }],
        });
        Assert.Equal("swapped", Describe(InlinesOf(host)));

        word = "b";
        host.Render(); // rebuilt from the elements (then the setter swaps it again)
        Assert.Equal("swapped", Describe(InlinesOf(host)));
    }

    // ── selectable text block and paragraphs ────────────────────────

    [AvaloniaFact]
    public void A_selectable_text_block_shows_its_text_or_its_inlines()
    {
        var rich = false;
        var host = new TestHost(() => rich ? SelectableTextBlock("Copy ", Bold("me")) : SelectableTextBlock("plain"));
        var block = Assert.IsType<AvSelectableTextBlock>(TextOf(host));
        Assert.Equal("plain", block.Text);

        rich = true;
        host.Render();
        Assert.Same(block, TextOf(host));
        Assert.Equal("Copy  Bold(me)", Describe(block.Inlines!));
    }

    [AvaloniaFact]
    public void Paragraphs_are_spans_with_a_line_break_between_and_move_with_their_keys()
    {
        var swap = false;
        RichTextParagraph P(string key, string text) => Paragraph(text).WithKey(key);
        var host = new TestHost(() => swap
            ? SelectableTextBlock(P("b", "Two"), P("a", "One").FontSize(20))
            : SelectableTextBlock(P("a", "One").FontSize(20), P("b", "Two")));
        var inlines = InlinesOf(host);
        Assert.Equal("Span(One) / Span(Two)", Describe(inlines));
        var (one, two) = (inlines[0], inlines[2]);
        Assert.Equal(20, one.FontSize);

        swap = true;
        host.Render();
        Assert.Equal("Span(Two) / Span(One)", Describe(inlines));
        Assert.Same(two, inlines[0]);
        Assert.Same(one, inlines[2]);
    }

    [AvaloniaFact]
    public void Paragraphs_win_over_inlines_and_text()
    {
        var host = new TestHost(() => new SelectableTextBlockElement("text") { Inlines = [Run("inline")], Paragraphs = [Paragraph("para")] });
        Assert.Equal("Span(para)", Describe(InlinesOf(host)));
        Assert.Null(TextOf(host).Text);
    }

    [AvaloniaFact]
    public void A_selectable_text_block_s_members_and_font_modifiers()
    {
        var step = 0;
        var host = new TestHost(() => step == 0
            ? SelectableTextBlock("a").FontSize(20).Bold().TextWrapping().SelectionBrush(Brushes.Orange)
            : ((Element)SelectableTextBlock("a")).FontSize(30));
        var block = Assert.IsType<AvSelectableTextBlock>(TextOf(host));
        Assert.Equal((20.0, FontWeight.Bold, TextWrapping.Wrap), (block.FontSize, block.FontWeight, block.TextWrapping));
        Assert.Equal(Colors.Orange, ColorOf(block.SelectionBrush));

        step = 1;
        host.Render();
        Assert.Equal(30, block.FontSize);
        Assert.False(block.IsSet(AvTextBlock.FontWeightProperty));
        Assert.False(block.IsSet(AvSelectableTextBlock.SelectionBrushProperty));
    }

    // ── the guide's example ─────────────────────────────────────────

    [AvaloniaFact]
    public void The_guide_s_example_counts_likes_from_a_button_in_its_text()
    {
        var host = new TestHost(() => Component(RichTextExamples.ReleaseNote));
        var text = host.All<AvTextBlock>().First(t => t is not AvSelectableTextBlock);
        Assert.Equal("Version 2 is  Bold(out) . Liked  0  times  [Button]", Describe(text.Inlines!));
        var like = UIChild(text.Inlines!, 5);
        TestHost.Click(like);
        host.Flush();
        Assert.Equal("1", ((Run)text.Inlines![3]).Text);
        Assert.Same(like, UIChild(text.Inlines!, 5));

        var selectable = host.All<AvSelectableTextBlock>()[0];
        Assert.Equal("Span(Italic(New: ) rich text, as runs, spans and line breaks.) / Span(Select this text to copy it.)", Describe(selectable.Inlines!));
    }
}

/// <summary>
/// Theme tokens on inlines (#65): <c>.Foreground(Theme.X)</c> on a run resolves for its text block and follows a theme switch
/// with no re-render, as a control's does (#41). Hosts set the process-wide UI dispatcher, so these run alone.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class RichTextThemeTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly List<Window> _windows = new();
    private readonly List<ReactorHostControl> _hosts = new();

    public void Dispose()
    {
        foreach (var h in _hosts) h.Dispose();
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
        ReactorApp.UIDispatcher = _previousDispatcher;
    }

    private (Window Window, AvTextBlock Text) Show(Func<RenderContext, Element> render)
    {
        var host = new ReactorHostControl(render);
        _hosts.Add(host);
        var window = new Window { Width = 400, Height = 300, RequestedThemeVariant = ThemeVariant.Light, Content = host };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, Assert.IsAssignableFrom<AvTextBlock>(host.Child));
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void A_theme_token_on_an_inline_follows_a_theme_switch_and_goes_when_the_inline_does()
    {
        Action<bool>? setThemed = null;
        var (window, text) = Show(ctx =>
        {
            var (themed, set) = ctx.UseState(true);
            setThemed = set;
            return TextBlock("a ", themed ? Run("b").Foreground(Theme.PrimaryText).Background(Theme.SolidBackground) : Run("c"));
        });
        var run = (Run)text.Inlines![1];
        Assert.Equal(Color.FromUInt32(0xE4000000u), ColorOf(run.Foreground));
        Assert.Equal(Color.FromUInt32(0xFFF3F3F3u), ColorOf(run.Background));

        window.RequestedThemeVariant = ThemeVariant.Dark; // no render
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.FromUInt32(0xFFFFFFFFu), ColorOf(run.Foreground));
        Assert.Equal(Color.FromUInt32(0xFF202020u), ColorOf(run.Background));

        setThemed!(false); // the token goes: the run inherits the text block's foreground again
        Dispatcher.UIThread.RunJobs();
        Assert.Same(run, text.Inlines[1]);
        Assert.False(run.IsSet(TextElement.ForegroundProperty));
        Assert.Empty(Reconciler.BoundThemeProperties(run));
    }
}
