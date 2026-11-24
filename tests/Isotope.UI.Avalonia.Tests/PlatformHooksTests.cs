using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The platform hooks (#20), headless: each reads the right value on the first render, re-renders when the platform
/// changes, and leaves no subscription behind on unmount; outside a host each returns upstream's fallback. Hosts set
/// the process-wide <see cref="ReactorApp.UIDispatcher"/>, so these run alone and restore it.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class PlatformHooksTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly List<Window> _windows = new();
    private readonly List<ReactorHostControl> _hosts = new();

    public void Dispose()
    {
        // Hosts first (a closing guard may hold its window open), so nothing outlives the test.
        foreach (var h in _hosts) h.Dispose();
        foreach (var w in _windows) w.Close();
        Pump();
        ReactorApp.UIDispatcher = _previousDispatcher;
    }

    private ReactorHostControl Track(ReactorHostControl host)
    {
        _hosts.Add(host);
        return host;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private Window NewWindow(double width = 400, double height = 300)
    {
        var window = new Window { Width = width, Height = height };
        _windows.Add(window);
        return window;
    }

    // A root component in a shown headless window; counts its renders.
    private sealed class Mounted
    {
        public required Window Window;
        public required ReactorHostControl Host;
        public int Renders;

        public string? Text => (Host.Child as TextBlock)?.Text ?? FirstText(Host.Child);

        private static string? FirstText(Control? c) => c switch
        {
            TextBlock t => t.Text,
            Panel p => p.Children.Select(FirstText).FirstOrDefault(t => t is not null),
            Decorator d => FirstText(d.Child),
            _ => null,
        };
    }

    private Mounted Mount(Func<RenderContext, string> render, Window? window = null, Action<ReactorHostControl>? setUp = null)
    {
        window ??= NewWindow();
        Mounted? m = null;
        var host = Track(new ReactorHostControl(ctx =>
        {
            m!.Renders++;
            return TextBlock(render(ctx));
        }));
        setUp?.Invoke(host);
        m = new Mounted { Window = window, Host = host };
        window.Content = host;
        window.Show();
        Pump();
        return m;
    }

    // ── Theme ──

    [AvaloniaFact]
    public void UseColorScheme_reads_the_windows_theme_on_the_first_render_and_follows_it()
    {
        var window = NewWindow();
        window.RequestedThemeVariant = ThemeVariant.Dark;
        var m = Mount(ctx => $"{ctx.UseColorScheme()} {ctx.UseIsDarkTheme()}", window);
        Assert.Equal("Dark True", m.Text);
        Assert.Equal(1, m.Renders);

        window.RequestedThemeVariant = ThemeVariant.Light;
        Pump();
        Assert.Equal("Light False", m.Text);
        Assert.Equal(2, m.Renders);
    }

    [AvaloniaFact]
    public void UseColorScheme_outside_a_host_follows_the_app()
    {
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            var host = new TestHost(() => Component(ctx => TextBlock(ctx.UseColorScheme().ToString())));
            Assert.Equal(["Dark"], host.Texts());

            app.RequestedThemeVariant = ThemeVariant.Light;
            Assert.True(host.RerenderRequested);
            host.Flush();
            Assert.Equal(["Light"], host.Texts());
            host.Unmount();
        }
        finally
        {
            app.RequestedThemeVariant = previous;
        }
    }

    private sealed class FakeColorValues : IColorValuesSource
    {
        public PlatformColorValues Values = new() { ThemeVariant = PlatformThemeVariant.Light };
        public int Handlers;
        public int Adds;
        private EventHandler<PlatformColorValues>? _changed;

        /// <summary>Called on each read, before it returns: lets a test change the value with no event.</summary>
        public Action? OnRead;

        public PlatformColorValues GetColorValues()
        {
            var values = Values;
            OnRead?.Invoke();
            return values;
        }

        public event EventHandler<PlatformColorValues>? ColorValuesChanged
        {
            add { _changed += value; Handlers++; Adds++; }
            remove { _changed -= value; Handlers--; }
        }

        public void Set(ColorContrastPreference contrast)
        {
            Values = Values with { ContrastPreference = contrast };
            _changed?.Invoke(this, Values);
        }
    }

    [AvaloniaFact]
    public void UseHighContrast_reads_the_contrast_preference_follows_it_and_unsubscribes()
    {
        var fake = new FakeColorValues();
        fake.Set(ColorContrastPreference.High);
        var m = Mount(ctx => ctx.UseHighContrast() ? "high" : "normal", setUp: h => h.ColorValuesOverride = fake);
        Assert.Equal("high", m.Text);
        Assert.Equal(1, fake.Handlers);

        fake.Set(ColorContrastPreference.NoPreference);
        Pump();
        Assert.Equal("normal", m.Text);

        // A signal with nothing changed doesn't render.
        var renders = m.Renders;
        fake.Set(ColorContrastPreference.NoPreference);
        Pump();
        Assert.Equal(renders, m.Renders);
        // Re-renders keep the one subscription made at mount.
        Assert.Equal(1, fake.Adds);

        m.Host.Dispose();
        Assert.Equal(0, fake.Handlers);
    }

    [AvaloniaFact]
    public void A_change_between_the_render_and_the_subscription_is_caught()
    {
        var fake = new FakeColorValues();
        fake.Values = fake.Values with { ContrastPreference = ColorContrastPreference.High };
        // The first read (the render's) sees high contrast; the preference then flips, with no event, before the
        // effect subscribes.
        fake.OnRead = () =>
        {
            fake.Values = fake.Values with { ContrastPreference = ColorContrastPreference.NoPreference };
            fake.OnRead = null;
        };
        var m = Mount(ctx => ctx.UseHighContrast() ? "high" : "normal", setUp: h => h.ColorValuesOverride = fake);
        Assert.Equal("normal", m.Text);
        Assert.Equal(2, m.Renders);
    }

    [AvaloniaFact]
    public void UseHighContrast_without_a_fake_reads_the_platform_settings()
    {
        // Headless platform settings report no contrast preference.
        var m = Mount(ctx => ctx.UseHighContrast() ? "high" : "normal");
        Assert.Equal("normal", m.Text);
    }

    // ── Window size and breakpoints ──

    [AvaloniaFact]
    public void UseWindowSize_reads_the_client_size_and_follows_resizes()
    {
        var m = Mount(ctx => { var (w, h) = ctx.UseWindowSize(); return $"{w}x{h}"; }, NewWindow(400, 300));
        Assert.Equal("400x300", m.Text);
        Assert.Equal(1, m.Renders);

        m.Window.Width = 500;
        Pump();
        Assert.Equal("500x300", m.Text);
    }

    [AvaloniaFact]
    public void UseWindowSize_of_a_given_top_level_follows_that_one()
    {
        var other = NewWindow(250, 150);
        other.Show();
        var m = Mount(ctx => { var (w, h) = ctx.UseWindowSize(other); return $"{w}x{h}"; }, NewWindow(400, 300));
        Assert.Equal("250x150", m.Text);

        other.Height = 175;
        Pump();
        Assert.Equal("250x175", m.Text);
    }

    [AvaloniaFact]
    public void UseWindowSize_follows_a_new_top_level_when_the_argument_changes()
    {
        var a = NewWindow(250, 150);
        var b = NewWindow(260, 160);
        a.Show();
        b.Show();
        Action<bool>? useB = null;
        var m = Mount(ctx =>
        {
            var (which, set) = ctx.UseState(false);
            useB = set;
            var (w, h) = ctx.UseWindowSize(which ? b : a);
            return $"{w}x{h}";
        });
        Assert.Equal("250x150", m.Text);

        useB!(true);
        Pump();
        Assert.Equal("260x160", m.Text);
        b.Width = 300;
        Pump();
        Assert.Equal("300x160", m.Text);

        // The old one no longer drives it.
        var renders = m.Renders;
        a.Width = 400;
        Pump();
        Assert.Equal(renders, m.Renders);
    }

    [AvaloniaFact]
    public void UseBreakpoint_rerenders_only_when_the_width_crosses_it()
    {
        var m = Mount(ctx => ctx.UseBreakpoint(450) ? "wide" : "narrow", NewWindow(400, 300));
        Assert.Equal("narrow", m.Text);
        Assert.Equal(1, m.Renders);

        m.Window.Width = 420;
        Pump();
        Assert.Equal(1, m.Renders);

        m.Window.Width = 450;
        Pump();
        Assert.Equal("wide", m.Text);
        Assert.Equal(2, m.Renders);
    }

    [AvaloniaFact]
    public void UseBreakpoint_of_a_given_top_level_compares_its_width()
    {
        var other = NewWindow(700, 150);
        other.Show();
        var m = Mount(ctx => ctx.UseBreakpoint(other, 640) ? "wide" : "narrow", NewWindow(400, 300));
        Assert.Equal("wide", m.Text);

        other.Width = 600;
        Pump();
        Assert.Equal("narrow", m.Text);
    }

    [AvaloniaFact]
    public void A_host_moved_to_another_window_follows_the_new_one()
    {
        var first = NewWindow(400, 300);
        var m = Mount(ctx => { var (w, h) = ctx.UseWindowSize(); return $"{w}x{h}"; }, first);
        Assert.Equal("400x300", m.Text);

        var second = NewWindow(300, 200);
        second.Show();
        first.Content = null;
        second.Content = m.Host;
        Pump();
        Assert.Equal("300x200", m.Text);

        // The old window no longer drives it; the new one does.
        var renders = m.Renders;
        first.Width = 600;
        Pump();
        Assert.Equal(renders, m.Renders);
        second.Width = 350;
        Pump();
        Assert.Equal("350x200", m.Text);
    }

    // ── Window state, activation, position, DPI ──

    [AvaloniaFact]
    public void UseWindow_returns_the_hosts_window_and_follows_a_move()
    {
        Window? seen = null;
        var m = Mount(ctx => { seen = ctx.UseWindow(); return "x"; });
        Assert.Same(m.Window, seen);

        var second = NewWindow();
        second.Show();
        m.Window.Content = null;
        Pump();
        Assert.Null(seen);
        second.Content = m.Host;
        Pump();
        Assert.Same(second, seen);
    }

    [AvaloniaFact]
    public void A_host_in_an_overlay_popup_sees_its_window()
    {
        // Headless puts popups in the window's overlay layer. (Desktop platforms give a popup its own PopupRoot, which
        // the hooks walk out of to the window that opened it; headless can't create one, so that path isn't covered.)
        var window = NewWindow(400, 300);
        Window? seen = null;
        string? size = null;
        var host = Track(new ReactorHostControl(ctx =>
        {
            seen = ctx.UseWindow();
            var (w, h) = ctx.UseWindowSize();
            size = $"{w}x{h}";
            return TextBlock("in a popup");
        }));
        var anchor = new Border { Width = 10, Height = 10 };
        var popup = new global::Avalonia.Controls.Primitives.Popup { Child = host, PlacementTarget = anchor };
        window.Content = new StackPanel { Children = { anchor, popup } };
        window.Show();
        popup.IsOpen = true;
        Pump();
        Assert.Same(window, seen);
        Assert.Equal("400x300", size);
        popup.IsOpen = false;
    }

    [AvaloniaFact]
    public void A_detached_host_watches_nothing_app_wide_until_it_joins_a_window()
    {
        var fake = new FakeColorValues();
        var before = PlatformHookDiagnostics.LiveSubscriptions;
        var m = Mount(ctx => $"{ctx.UseColorScheme()} {ctx.UseHighContrast()}", setUp: h => h.ColorValuesOverride = fake);
        Assert.Equal(1, fake.Handlers);

        m.Window.Content = null;
        Pump();
        // Left: each hook's watch of its own host (two hooks), nothing on the app or the window.
        Assert.Equal(0, fake.Handlers);
        Assert.Equal(before + 2, PlatformHookDiagnostics.LiveSubscriptions);

        var other = NewWindow();
        other.RequestedThemeVariant = ThemeVariant.Dark;
        other.Content = m.Host;
        other.Show();
        Pump();
        Assert.Equal(1, fake.Handlers);
        Assert.Equal("Dark False", m.Text);
    }

    [AvaloniaFact]
    public void A_window_closed_without_disposing_its_host_releases_the_hooks_subscriptions()
    {
        var fake = new FakeColorValues();
        var before = PlatformHookDiagnostics.LiveSubscriptions;
        var m = Mount(ctx => { var (w, _) = ctx.UseWindowSize(); return $"{w} {ctx.UseHighContrast()}"; },
            setUp: h => h.ColorValuesOverride = fake);
        Assert.Equal(1, fake.Handlers);

        m.Window.Close();
        Pump();
        Assert.Equal(0, fake.Handlers);
        Assert.Equal(before + 2, PlatformHookDiagnostics.LiveSubscriptions);
    }

    [AvaloniaFact]
    public void UseWindowState_reads_the_state_and_follows_it()
    {
        var m = Mount(ctx => ctx.UseWindowState().ToString());
        Assert.Equal("Normal", m.Text);

        m.Window.WindowState = WindowState.Maximized;
        Pump();
        Assert.Equal("Maximized", m.Text);
    }

    [AvaloniaFact]
    public void UseIsActive_follows_activation()
    {
        var m = Mount(ctx => ctx.UseIsActive() ? "active" : "inactive");
        m.Window.Activate();
        Pump();
        Assert.Equal("active", m.Text);

        // Headless deactivates a window when it is hidden.
        m.Window.Hide();
        Pump();
        Assert.Equal("inactive", m.Text);

        m.Window.Show();
        Pump();
        Assert.Equal("active", m.Text);
    }

    [AvaloniaFact]
    public void UseWindowPosition_reads_the_position_in_dips_and_follows_moves()
    {
        var window = NewWindow();
        window.Position = new PixelPoint(100, 50);
        var m = Mount(ctx => { var (x, y) = ctx.UseWindowPosition(); return $"{x},{y}"; }, window);
        Assert.Equal("100,50", m.Text);

        window.Position = new PixelPoint(120, 60);
        Pump();
        Assert.Equal("120,60", m.Text);

        window.SetRenderScaling(2);
        Pump();
        Assert.Equal("60,30", m.Text);
    }

    [AvaloniaFact]
    public void UseDpi_reads_the_render_scaling_and_follows_it()
    {
        var window = NewWindow();
        window.Show();
        window.SetRenderScaling(1.25);
        var m = Mount(ctx => ctx.UseDpi().ToString(), window);
        Assert.Equal("120", m.Text);
        Assert.Equal(1, m.Renders);

        window.SetRenderScaling(1.5);
        Pump();
        Assert.Equal("144", m.Text);
    }

    // ── Closing guard ──

    [AvaloniaFact]
    public void UseClosingGuard_cancels_a_close_while_it_returns_false()
    {
        bool allow = false;
        var m = Mount(ctx => { ctx.UseClosingGuard(() => allow); return "x"; });
        bool closed = false;
        m.Window.Closed += (_, _) => closed = true;

        m.Window.Close();
        Pump();
        Assert.False(closed);

        allow = true;
        m.Window.Close();
        Pump();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void UseClosingGuard_asks_the_latest_renders_predicate()
    {
        Action<bool>? setLocked = null;
        var m = Mount(ctx =>
        {
            var (locked, set) = ctx.UseState(true);
            setLocked = set;
            ctx.UseClosingGuard(() => !locked);
            return locked ? "locked" : "open";
        });
        bool closed = false;
        m.Window.Closed += (_, _) => closed = true;
        m.Window.Close();
        Pump();
        Assert.False(closed);

        // The state the guard's lambda captures changes on a new render, with a new lambda.
        setLocked!(false);
        Pump();
        Assert.Equal("open", m.Text);
        m.Window.Close();
        Pump();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void A_throwing_closing_guard_cancels_the_close()
    {
        var m = Mount(ctx => { ctx.UseClosingGuard(() => throw new InvalidOperationException("guard")); return "x"; });
        bool closed = false;
        m.Window.Closed += (_, _) => closed = true;
        m.Window.Close();
        Pump();
        Assert.False(closed);

        m.Host.Dispose();
        m.Window.Close();
        Pump();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Closing_guards_stack_and_a_later_one_is_not_asked_after_a_cancel()
    {
        int secondAsked = 0;
        var m = Mount(ctx =>
        {
            ctx.UseClosingGuard(() => false);
            ctx.UseClosingGuard(() => { secondAsked++; return true; });
            return "x";
        });
        bool closed = false;
        m.Window.Closed += (_, _) => closed = true;
        m.Window.Close();
        Pump();
        Assert.False(closed);
        Assert.Equal(0, secondAsked);
    }

    // ── Displays ──

    [AvaloniaFact]
    public void UseDisplays_snapshots_the_top_levels_screens()
    {
        IReadOnlyList<DisplayInfo>? seen = null;
        Mount(ctx => { seen = ctx.UseDisplays(); return "x"; });
        // Headless has one primary 1920 x 1280 screen at scaling 1.
        var display = Assert.Single(seen!);
        Assert.Equal("1", display.Id); // the platform handle (headless numbers its one screen 1), not the name
        Assert.True(display.IsPrimary);
        Assert.Equal(new Rect(0, 0, 1920, 1280), display.BoundsDip);
        Assert.Equal(96u, display.Dpi);
    }

    private sealed class FakeDisplays : IDisplaySource
    {
        public IReadOnlyList<DisplayInfo> Displays { get; set; } = [];
        public int Handlers;
        private EventHandler? _changed;

        public event EventHandler? Changed
        {
            add { _changed += value; Handlers++; }
            remove { _changed -= value; Handlers--; }
        }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
    }

    private static DisplayInfo Display(string id, double width, uint dpi = 96) =>
        new() { Id = id, IsPrimary = id == "1", BoundsDip = new Rect(0, 0, width, 1000), WorkAreaDip = new Rect(0, 0, width, 960), Dpi = dpi };

    [AvaloniaFact]
    public void UseDisplays_rerenders_when_a_display_changes_and_not_for_an_equal_snapshot()
    {
        var fake = new FakeDisplays { Displays = [Display("1", 1920)] };
        var m = Mount(ctx => string.Join(";", ctx.UseDisplays().Select(d => $"{d.Id}:{d.BoundsDip.Width}@{d.Dpi}")),
            setUp: h => h.DisplaysOverride = fake);
        Assert.Equal("1:1920@96", m.Text);

        fake.Displays = [Display("1", 1920)]; // a new snapshot, equal
        fake.Raise();
        Pump();
        Assert.Equal(1, m.Renders);

        fake.Displays = [Display("1", 1920), Display("2", 1280, 144)];
        fake.Raise();
        Pump();
        Assert.Equal("1:1920@96;2:1280@144", m.Text);

        m.Host.Dispose();
        Assert.Equal(0, fake.Handlers);
    }

    // ── Outside a host: upstream's fallbacks ──

    [AvaloniaFact]
    public void Outside_a_host_the_hooks_return_upstreams_fallbacks()
    {
        string? seen = null;
        bool guardAsked = false;
        var host = new TestHost(() => Component(ctx =>
        {
            var (w, h) = ctx.UseWindowSize();
            var (x, y) = ctx.UseWindowPosition();
            ctx.UseClosingGuard(() => guardAsked = true);
            seen = string.Join("|",
                ctx.UseWindow() is null, $"{w}x{h}", $"{x},{y}", ctx.UseDpi(), ctx.UseWindowState(), ctx.UseIsActive(),
                ctx.UseBreakpoint(1), ctx.UseBreakpoint(0), ctx.UseDisplays().Count, ctx.UseHighContrast());
            return TextBlock(seen);
        }));
        Assert.Equal("True|0x0|0,0|96|Normal|True|False|False|0|False", seen);
        Assert.False(guardAsked);
        host.Unmount();
    }

    public static TheoryData<string> ValueHooks() =>
    [
        "UseColorScheme", "UseHighContrast", "UseWindow", "UseWindowSize", "UseBreakpoint", "UseWindowState", "UseIsActive",
        "UseWindowPosition", "UseDpi", "UseDisplays",
    ];

    private static void Call(RenderContext ctx, string hook)
    {
        switch (hook)
        {
            case "UseColorScheme": ctx.UseColorScheme(); break;
            case "UseHighContrast": ctx.UseHighContrast(); break;
            case "UseWindow": ctx.UseWindow(); break;
            case "UseWindowSize": ctx.UseWindowSize(); break;
            case "UseBreakpoint": ctx.UseBreakpoint(100); break;
            case "UseWindowState": ctx.UseWindowState(); break;
            case "UseIsActive": ctx.UseIsActive(); break;
            case "UseWindowPosition": ctx.UseWindowPosition(); break;
            case "UseDpi": ctx.UseDpi(); break;
            case "UseDisplays": ctx.UseDisplays(); break;
            case "UseClosingGuard": ctx.UseClosingGuard(() => true); break;
            default: throw new ArgumentException(hook);
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ValueHooks))]
    public void A_value_hook_takes_the_same_slots_with_or_without_a_window(string hook)
    {
        var bare = new RenderContext();
        bare.BeginRender(() => { });
        Call(bare, hook);
        var withoutWindow = bare.SnapshotHooks().Select(s => s.Hook).ToArray();

        RenderContext? hosted = null;
        Mount(ctx => { hosted = ctx; Call(ctx, hook); return "x"; });
        var inWindow = hosted!.SnapshotHooks().Select(s => s.Hook).ToArray();

        Assert.Equal(["useRef", "useEffect"], withoutWindow);
        Assert.Equal(withoutWindow, inWindow);
    }

    public static TheoryData<string> AllHooks()
    {
        var data = new TheoryData<string>();
        foreach (var row in ValueHooks()) data.Add(row.Data);
        data.Add("UseClosingGuard");
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(AllHooks))]
    public void Unmounting_removes_every_platform_subscription(string hook)
    {
        var before = PlatformHookDiagnostics.LiveSubscriptions;
        var m = Mount(ctx => { Call(ctx, hook); return "x"; });
        Assert.True(PlatformHookDiagnostics.LiveSubscriptions > before, "the hook subscribed to nothing");

        m.Host.Dispose();
        Assert.Equal(before, PlatformHookDiagnostics.LiveSubscriptions);
    }

    [AvaloniaFact]
    public void A_child_component_unmounted_while_its_host_stays_unsubscribes()
    {
        var before = PlatformHookDiagnostics.LiveSubscriptions;
        Action<bool>? setShow = null;
        var window = NewWindow();
        var host = Track(new ReactorHostControl(ctx =>
        {
            var (show, set) = ctx.UseState(true);
            setShow = set;
            return VStack(show ? Component(c => TextBlock(c.UseWindowState().ToString())) : null);
        }));
        window.Content = host;
        window.Show();
        Pump();
        var mounted = PlatformHookDiagnostics.LiveSubscriptions;
        Assert.True(mounted > before);

        setShow!(false);
        Pump();
        Assert.Equal(before, PlatformHookDiagnostics.LiveSubscriptions);
        host.Dispose();
    }

    // ── Class components (#21) ──

    // Every platform hook, as one line of text, from a function component's context or a class component's methods.
    private static string Describe(ColorScheme scheme, bool dark, bool contrast, Window? window, (double W, double H) size,
        (double W, double H) sizeOf, bool wide, bool wideOf, WindowState state, bool active, (double X, double Y) position,
        uint dpi, int displays)
        => $"{scheme} {dark} {contrast} {window is not null} {size.W}x{size.H} {sizeOf.W}x{sizeOf.H} {wide} {wideOf} " +
           $"{state} {active} {position.X},{position.Y} {dpi} {displays}";

    // A component's text, through any decorator the component's node puts around it.
    private static string? TextOf(Control? c) => c switch
    {
        TextBlock t => t.Text,
        Decorator d => TextOf(d.Child),
        _ => null,
    };

    private sealed record ProbeProps(TopLevel TopLevel);

    // Component<TProps> derives from Component, so it has the hooks too; the props carry the top level for the explicit
    // overloads.
    private sealed class PlatformProbe : Component<ProbeProps>
    {
        public override Element Render() => TextBlock(Describe(UseColorScheme(), UseIsDarkTheme(), UseHighContrast(),
            UseWindow(), UseWindowSize(), UseWindowSize(Props.TopLevel), UseBreakpoint(450),
            UseBreakpoint(Props.TopLevel, 450), UseWindowState(), UseIsActive(), UseWindowPosition(), UseDpi(),
            UseDisplays().Count));
    }

    [AvaloniaFact]
    public void A_class_component_reads_the_same_platform_values_as_a_function_component_and_follows_them()
    {
        var window = NewWindow(400, 300);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        var host = Track(new ReactorHostControl(_ => VStack(
            Component(ctx => TextBlock(Describe(ctx.UseColorScheme(), ctx.UseIsDarkTheme(), ctx.UseHighContrast(),
                ctx.UseWindow(), ctx.UseWindowSize(), ctx.UseWindowSize(window), ctx.UseBreakpoint(450),
                ctx.UseBreakpoint(window, 450), ctx.UseWindowState(), ctx.UseIsActive(), ctx.UseWindowPosition(),
                ctx.UseDpi(), ctx.UseDisplays().Count))),
            new ComponentElement<ProbeProps>(typeof(PlatformProbe), new ProbeProps(window)))));
        window.Content = host;
        window.Show();
        Pump();
        string?[] Texts() => ((StackPanel)host.Child!).Children.Select(TextOf).ToArray();

        var first = Texts();
        Assert.StartsWith("Dark True False True 400x300 400x300 False False ", first[1]);
        Assert.Equal(first[0], first[1]);

        // A theme change and a resize across the breakpoint re-render the class component too.
        window.RequestedThemeVariant = ThemeVariant.Light;
        window.Width = 500;
        Pump();
        var second = Texts();
        Assert.StartsWith("Light False False True 500x300 500x300 True True ", second[1]);
        Assert.Equal(second[0], second[1]);
    }

    private static bool s_probeAllowsClose;

    private sealed class GuardProbe : Component
    {
        public override Element Render()
        {
            UseClosingGuard(() => s_probeAllowsClose);
            return TextBlock("guarded");
        }
    }

    [AvaloniaFact]
    public void A_class_components_closing_guard_cancels_a_close()
    {
        s_probeAllowsClose = false;
        try
        {
            var window = NewWindow();
            var host = Track(new ReactorHostControl(_ => new ComponentElement(typeof(GuardProbe))));
            window.Content = host;
            window.Show();
            Pump();
            bool closed = false;
            window.Closed += (_, _) => closed = true;

            window.Close();
            Pump();
            Assert.False(closed);

            s_probeAllowsClose = true;
            window.Close();
            Pump();
            Assert.True(closed);
        }
        finally
        {
            // A failed assert mustn't leave the guard cancelling the cleanup's close.
            s_probeAllowsClose = true;
        }
    }

    // ── The host as content owner ──

    [AvaloniaFact]
    public void The_host_reports_whether_its_content_has_a_window()
    {
        var host = Track(new ReactorHostControl(_ => TextBlock("x")));
        IContentOwner owner = host;
        Assert.Equal(ContentOwnerStatus.Detached, owner.TryGetWindowId(out _));

        var window = NewWindow();
        window.Content = host;
        window.Show();
        Pump();
        Assert.Equal(ContentOwnerStatus.Available, owner.TryGetWindowId(out _));
        Assert.Null(owner.ReactorWindow);

        host.Dispose();
        Assert.Equal(ContentOwnerStatus.Closed, owner.TryGetWindowId(out _));
    }

    // ── The guide's examples ──

    [AvaloniaFact]
    public void Guide_adaptive_example_follows_the_width_and_the_theme()
    {
        var window = NewWindow(500, 300);
        var host = Track(new ReactorHostControl(PlatformHookExamples.Adaptive));
        window.Content = host;
        window.Show();
        Pump();
        Assert.IsType<StackPanel>(host.Child);
        Assert.Equal(Orientation.Vertical, ((StackPanel)host.Child!).Orientation);
        Assert.Equal("500 x 300, light", ((TextBlock)((StackPanel)host.Child).Children[0]).Text);

        window.Width = 700;
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Pump();
        var row = Assert.IsType<StackPanel>(host.Child);
        Assert.Equal(Orientation.Horizontal, row.Orientation);
        Assert.Equal("700 x 300, dark", ((TextBlock)row.Children[0]).Text);
        host.Dispose();
    }

    [AvaloniaFact]
    public void Guide_editor_example_keeps_the_window_open_with_unsaved_changes()
    {
        var window = NewWindow();
        var host = Track(new ReactorHostControl(PlatformHookExamples.Editor));
        window.Content = host;
        window.Show();
        Pump();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        var panel = (StackPanel)host.Child!;
        var box = (TextBox)panel.Children[0];
        var save = (Button)panel.Children[1];

        box.Text = "draft";
        Pump();
        window.Close();
        Pump();
        Assert.False(closed);

        save.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Pump();
        Assert.Equal("Saved", save.Content);
        window.Close();
        Pump();
        Assert.True(closed);
    }
}
