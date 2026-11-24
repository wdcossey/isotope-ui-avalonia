using System.Diagnostics.Tracing;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;
using AvaloniaButton = Avalonia.Controls.Button;
using AvaloniaTextBlock = Avalonia.Controls.TextBlock;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Named text styles (#44), headless: upstream's type-ramp factories and ApplyStyle give a text block the ControlTheme of
/// a WinUI style key, an app's own theme of that key winning and followed without a re-render; Heading, SubHeading and
/// Caption take upstream's fixed values; Card takes upstream's card look. Hosts set the process-wide UI dispatcher, and
/// one test writes the application's resources, so these run alone.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class TextStyleTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly List<Window> _windows = new();
    private readonly List<ReactorHostControl> _hosts = new();

    public void Dispose()
    {
        foreach (var h in _hosts) h.Dispose();
        foreach (var w in _windows) w.Close();
        Pump();
        ReactorApp.UIDispatcher = _previousDispatcher;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class Mounted
    {
        public required Window Window;
        public required ReactorHostControl Host;
        public int Renders;

        public List<T> All<T>() where T : Control
        {
            var found = new List<T>();
            void Walk(Control? c)
            {
                if (c is T t) found.Add(t);
                switch (c)
                {
                    case Panel p: foreach (var child in p.Children) Walk(child); break;
                    case Decorator d: Walk(d.Child); break;
                    case ContentControl { Content: Control content }: Walk(content); break;
                }
            }
            Walk(Host.Child);
            return found;
        }

        public T First<T>() where T : Control => All<T>()[0];
    }

    private Mounted Mount(Func<RenderContext, Element> render, ThemeVariant? theme = null, Action<Window>? setUp = null)
    {
        var window = new Window { Width = 400, Height = 300 };
        _windows.Add(window);
        if (theme is not null) window.RequestedThemeVariant = theme;
        setUp?.Invoke(window);
        Mounted? m = null;
        var host = new ReactorHostControl(ctx =>
        {
            m!.Renders++;
            return render(ctx);
        });
        _hosts.Add(host);
        m = new Mounted { Window = window, Host = host };
        window.Content = host;
        window.Show();
        Pump();
        return m;
    }

    private static ControlTheme TextTheme(double size)
    {
        var theme = new ControlTheme(typeof(AvaloniaTextBlock));
        theme.Setters.Add(new Setter(AvaloniaTextBlock.FontSizeProperty, size));
        return theme;
    }

    private static FontWeight Weight(string name) => Enum.Parse<FontWeight>(name);

    // ── The type ramp ──

    public static TheoryData<string, double, string> RampFactories() => new()
    {
        { nameof(Title), 28, "SemiBold" },
        { nameof(Subtitle), 20, "SemiBold" },
        { nameof(Body), 14, "Normal" },
        { nameof(BodyStrong), 14, "SemiBold" },
        { nameof(BodyLarge), 18, "Normal" },
        { nameof(TitleLarge), 40, "SemiBold" },
        { nameof(Display), 68, "SemiBold" },
    };

    private static TextBlockElement RampFactory(string name, string text) => name switch
    {
        nameof(Title) => Title(text),
        nameof(Subtitle) => Subtitle(text),
        nameof(Body) => Body(text),
        nameof(BodyStrong) => BodyStrong(text),
        nameof(BodyLarge) => BodyLarge(text),
        nameof(TitleLarge) => TitleLarge(text),
        nameof(Display) => Display(text),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [AvaloniaTheory]
    [MemberData(nameof(RampFactories))]
    public void Each_type_ramp_factory_takes_WinUIs_style(string factory, double size, string weight)
    {
        var m = Mount(_ => RampFactory(factory, "x"));
        var text = m.First<TextBlock>();
        Assert.Equal("x", text.Text);
        Assert.NotNull(text.Theme);
        Assert.Equal(size, text.FontSize);
        Assert.Equal(Weight(weight), text.FontWeight);
        // BaseTextBlockStyle's wrapping and trimming, which every type-ramp style is based on.
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
    }

    [AvaloniaTheory]
    [InlineData("BaseTextBlockStyle", 14, "SemiBold")]
    [InlineData("HeaderTextBlockStyle", 46, "Light")]
    [InlineData("SubheaderTextBlockStyle", 34, "Light")]
    [InlineData("CaptionTextBlockStyle", 12, "Normal")]
    [InlineData("BodyLargeStrongTextBlockStyle", 18, "SemiBold")]
    public void ApplyStyle_takes_WinUIs_other_TextBlock_styles(string key, double size, string weight)
    {
        var text = Mount(_ => TextBlock("x").ApplyStyle(key)).First<TextBlock>();
        Assert.Equal(size, text.FontSize);
        Assert.Equal(Weight(weight), text.FontWeight);
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
    }

    [AvaloniaFact]
    public void Heading_SubHeading_and_Caption_take_upstreams_fixed_values()
    {
        var texts = Mount(_ => VStack(Heading("h"), SubHeading("s"), Caption("c"), TextBlock("t"))).All<TextBlock>();
        Assert.Equal((28.0, FontWeight.Bold, 1), (texts[0].FontSize, texts[0].FontWeight, AutomationProperties.GetHeadingLevel(texts[0])));
        Assert.Equal((20.0, FontWeight.SemiBold, 2), (texts[1].FontSize, texts[1].FontWeight, AutomationProperties.GetHeadingLevel(texts[1])));
        Assert.Equal((12.0, FontWeight.Normal), (texts[2].FontSize, texts[2].FontWeight));
        // As upstream's: values on the members, with no style (so no wrapping), and no heading level on a caption.
        Assert.All(texts, t => Assert.Null(t.Theme));
        Assert.All(texts, t => Assert.Equal(TextWrapping.NoWrap, t.TextWrapping));
        Assert.False(texts[2].IsSet(AutomationProperties.HeadingLevelProperty));
    }

    [AvaloniaFact]
    public void A_heading_level_that_goes_is_cleared()
    {
        var (heading, setHeading) = (true, (Action<bool>?)null);
        var m = Mount(ctx =>
        {
            var (h, set) = ctx.UseState(true);
            (heading, setHeading) = (h, set);
            return h ? Heading("x") : TextBlock("x");
        });
        var text = m.First<TextBlock>();
        Assert.Equal(1, AutomationProperties.GetHeadingLevel(text));
        setHeading!(false);
        Pump();
        Assert.Same(text, m.First<TextBlock>());
        Assert.False(text.IsSet(AutomationProperties.HeadingLevelProperty));
    }

    [AvaloniaFact]
    public void The_elements_members_and_modifiers_win_over_the_style()
    {
        var texts = Mount(_ => VStack(
            Title("a").FontSize(14),
            Body("b").Bold(),
            Caption("c").ApplyStyle("TitleTextBlockStyle"),
            Subtitle("d").TextWrapping(TextWrapping.NoWrap))).All<TextBlock>();
        Assert.Equal((14.0, FontWeight.SemiBold), (texts[0].FontSize, texts[0].FontWeight));
        Assert.Equal((14.0, FontWeight.Bold), (texts[1].FontSize, texts[1].FontWeight));
        // Caption's own 12 wins over the Title style's 28; the style's weight shows.
        Assert.Equal((12.0, FontWeight.SemiBold), (texts[2].FontSize, texts[2].FontWeight));
        Assert.Equal((20.0, TextWrapping.NoWrap), (texts[3].FontSize, texts[3].TextWrapping));
    }

    // ── App resources win ──

    [AvaloniaFact]
    public void An_apps_theme_of_the_key_wins_and_follows_its_resources_without_a_render()
    {
        var m = Mount(_ => Title("x"));
        var text = m.First<TextBlock>();
        Assert.Equal(28, text.FontSize);

        var mine = TextTheme(50);
        m.Window.Resources["TitleTextBlockStyle"] = mine;
        Pump();
        Assert.Same(mine, text.Theme);
        Assert.Equal(50, text.FontSize);

        m.Window.Resources.Remove("TitleTextBlockStyle");
        Pump();
        Assert.Equal(28, text.FontSize);
        Assert.Equal(1, m.Renders);
    }

    [AvaloniaFact]
    public void An_apps_theme_in_the_application_resources_wins_for_any_key()
    {
        var app = Application.Current!;
        app.Resources["IsotopeTestNoteStyle"] = TextTheme(22);
        try
        {
            var texts = Mount(_ => VStack(TextBlock("a").ApplyStyle("IsotopeTestNoteStyle"), Body("b"))).All<TextBlock>();
            Assert.Equal(22, texts[0].FontSize);
            Assert.Equal(14, texts[1].FontSize);
        }
        finally
        {
            app.Resources.Remove("IsotopeTestNoteStyle");
        }
    }

    [AvaloniaFact]
    public void A_theme_per_theme_variant_follows_the_variant_without_a_render()
    {
        var m = Mount(_ => Title("x"), ThemeVariant.Light, window =>
        {
            window.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["TitleTextBlockStyle"] = TextTheme(30) };
            window.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["TitleTextBlockStyle"] = TextTheme(32) };
        });
        var text = m.First<TextBlock>();
        Assert.Equal(30, text.FontSize);
        m.Window.RequestedThemeVariant = ThemeVariant.Dark;
        Pump();
        Assert.Equal(32, text.FontSize);
        Assert.Equal(1, m.Renders);
    }

    [AvaloniaFact]
    public void A_resource_of_the_key_that_is_no_text_block_theme_leaves_the_type_ramp()
    {
        var m = Mount(_ => VStack(Title("a"), Body("b")), setUp: window =>
        {
            window.Resources["TitleTextBlockStyle"] = new ControlTheme(typeof(AvaloniaButton));
            window.Resources["BodyTextBlockStyle"] = "not a theme";
        });
        var texts = m.All<TextBlock>();
        Assert.Equal(28, texts[0].FontSize);
        Assert.Equal(14, texts[1].FontSize);
        Assert.Equal(FontWeight.Normal, texts[1].FontWeight);
    }

    // ── Changes on a render ──

    [AvaloniaFact]
    public void A_changed_or_removed_style_applies_on_the_next_render()
    {
        Action<int>? setStep = null;
        var m = Mount(ctx =>
        {
            var (step, set) = ctx.UseState(0);
            setStep = set;
            return step switch
            {
                0 => Title("x"),
                1 => TextBlock("x").ApplyStyle("CaptionTextBlockStyle"),
                _ => TextBlock("x"),
            };
        });
        var text = m.First<TextBlock>();
        Assert.Equal(28, text.FontSize);

        setStep!(1);
        Pump();
        Assert.Same(text, m.First<TextBlock>());
        Assert.Equal(12, text.FontSize);
        Assert.Equal(FontWeight.Normal, text.FontWeight);

        setStep!(2);
        Pump();
        Assert.Same(text, m.First<TextBlock>());
        Assert.Null(text.Theme);
        Assert.Equal(14, text.FontSize);
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
        Assert.Null(TextStyles.KeyOf(text));
    }

    // ── An unknown key ──

    private sealed class WarningListener : EventListener
    {
        public readonly List<string> Messages = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Isotope-UI") EnableEvents(source, EventLevel.Warning, (EventKeywords)0x20);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == "Warning" && e.Payload is { } payload)
                lock (Messages) Messages.Add(string.Join(" | ", payload));
        }
    }

    [AvaloniaFact]
    public void An_unknown_style_keeps_the_default_look_and_warns_once()
    {
        var key = "NoSuchStyle" + Guid.NewGuid().ToString("N");
        using var listener = new WarningListener();
        var texts = Mount(_ => VStack(TextBlock("a").ApplyStyle(key), TextBlock("b").ApplyStyle(key))).All<TextBlock>();
        Assert.All(texts, t => Assert.Null(t.Theme));
        Assert.All(texts, t => Assert.Equal(14, t.FontSize));
        List<string> mine;
        lock (listener.Messages) mine = listener.Messages.Where(s => s.Contains(key, StringComparison.Ordinal)).ToList();
        Assert.Single(mine);
        Assert.Contains("ApplyStyle", mine[0], StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => TextBlock("c").ApplyStyle(null!));
    }

    // ── The guide's examples ──

    [AvaloniaFact]
    public void The_guides_article_renders_its_styles_and_keeps_them_across_a_render()
    {
        var m = Mount(TextStyleExamples.Article, ThemeVariant.Light);
        var texts = m.All<AvaloniaTextBlock>();
        Assert.Equal((28.0, FontWeight.SemiBold), (texts[0].FontSize, texts[0].FontWeight));
        Assert.Equal(12, texts[1].FontSize);
        Assert.Equal((14.0, FontWeight.Normal), (texts[2].FontSize, texts[2].FontWeight));
        Assert.Equal(new CornerRadius(8), m.First<Border>().CornerRadius);

        TestHost.Click(m.First<Button>());
        Pump();
        Assert.Equal(2, m.Renders);
        Assert.Equal("Like (1)", m.First<Button>().Content);
        Assert.Same(texts[0], m.All<AvaloniaTextBlock>()[0]);
        Assert.Equal(28, texts[0].FontSize);
    }

    [AvaloniaFact]
    public void The_guides_override_gives_every_title_the_apps_theme()
    {
        var app = Application.Current!;
        TextStyleExamples.UseLargerTitles(app);
        try
        {
            var text = Mount(_ => Title("x")).First<AvaloniaTextBlock>();
            Assert.Equal((32.0, FontWeight.Bold), (text.FontSize, text.FontWeight));
        }
        finally
        {
            app.Resources.Remove("TitleTextBlockStyle");
        }
    }

    // ── Card ──

    [AvaloniaFact]
    public void Card_has_upstreams_card_look_with_theme_brushes()
    {
        var m = Mount(_ => Card(TextBlock("x")), ThemeVariant.Light);
        var border = m.First<Border>();
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
        Assert.Equal(new Thickness(16), border.Padding);
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(Color.FromUInt32(0xB3FFFFFF), Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
        Assert.Equal(Color.FromUInt32(0x0F000000), Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color);
        Assert.Equal("x", Assert.IsType<TextBlock>(border.Child).Text);
    }
}
