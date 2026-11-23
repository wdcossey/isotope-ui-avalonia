using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Isotope.UI.Samples;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The app host (#16), headless: mount, coalesced re-renders, unmount, and the render-error path. A host sets the
/// process-wide <see cref="ReactorApp.UIDispatcher"/> (as upstream's does), so these run alone and restore it.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class HostTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly RenderErrorHandler? _previousHandler = ReactorApp.DefaultRenderErrorHandler;

    public void Dispose()
    {
        ReactorApp.UIDispatcher = _previousDispatcher;
        ReactorApp.DefaultRenderErrorHandler = _previousHandler;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static List<string?> Texts(Control? root)
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

    private static Button ButtonLabelled(Control root, string label)
    {
        Button? found = null;
        void Walk(Control? c)
        {
            switch (c)
            {
                case null: return;
                case Button b when Equals(b.Content, label): found ??= b; break;
                case Panel p: foreach (var child in p.Children) Walk(child); break;
                case Decorator d: Walk(d.Child); break;
            }
        }
        Walk(root);
        return found ?? throw new InvalidOperationException("no button " + label);
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

    [AvaloniaFact]
    public void Mount_renders_from_the_dispatcher_and_a_click_rerenders()
    {
        var host = new ReactorHostControl(Examples.Counter);
        Assert.Null(host.Child);
        Assert.False(host.IsIdle);
        Pump();
        Assert.True(host.IsIdle);
        Assert.Equal(["Clicked 0 times"], Texts(host));

        Click(ButtonLabelled(host, "Click"));
        Assert.Equal(["Clicked 0 times"], Texts(host)); // not synchronous: on the dispatcher
        Pump();
        Assert.Equal(["Clicked 1 times"], Texts(host));
    }

    [AvaloniaFact]
    public void State_changes_before_a_pass_coalesce_into_one_pass()
    {
        int renders = 0;
        Action<int>? set = null;
        var host = new ReactorHostControl(ctx =>
        {
            renders++;
            var (n, setN) = ctx.UseState(0);
            set = setN;
            return TextBlock($"n={n}");
        });
        Pump();
        Assert.Equal(1, renders);

        set!(1);
        set!(2);
        set!(3);
        Pump();
        Assert.Equal(2, renders);
        Assert.Equal(["n=3"], Texts(host));
    }

    [AvaloniaFact]
    public void A_change_during_a_pass_queues_one_more_pass()
    {
        int renders = 0;
        var host = new ReactorHostControl(ctx =>
        {
            renders++;
            var (n, setN) = ctx.UseState(0);
            ctx.UseEffect(() => setN(1), Array.Empty<object>()); // runs during the first pass
            return TextBlock($"n={n}");
        });
        Pump();
        Assert.Equal(2, renders);
        Assert.Equal(["n=1"], Texts(host));
        Assert.True(host.IsIdle);
    }

    [AvaloniaFact]
    public void Leaving_the_tree_keeps_the_root_and_Dispose_unmounts_it()
    {
        int cleanups = 0;
        Element Root(RenderContext ctx)
        {
            ctx.UseEffect(() => () => cleanups++, Array.Empty<object>());
            return Component(Examples.Counter);
        }
        var host = new ReactorHostControl(Root);
        var window = new Window { Content = host };
        window.Show();
        Pump();
        Click(ButtonLabelled(host, "Click"));
        Pump();

        window.Content = null; // detached: maybe a move, so nothing unmounts
        Pump();
        Assert.Equal(0, cleanups);
        window.Content = host;
        Pump();
        Assert.Equal(["Clicked 1 times"], Texts(host));

        var button = ButtonLabelled(host, "Click");
        host.Dispose();
        Assert.Equal(1, cleanups);
        Assert.Null(host.Child);
        Assert.True(host.IsIdle);
        Click(button); // a setter of the unmounted tree: no pass
        Pump();
        Assert.Null(host.Child);
        window.Close();
    }

    [AvaloniaFact]
    public void Mount_replaces_the_root_and_cleans_up_the_old_one()
    {
        int cleanups = 0;
        var host = new ReactorHostControl(ctx =>
        {
            ctx.UseEffect(() => () => cleanups++, Array.Empty<object>());
            return TextBlock("first");
        });
        Pump();
        host.Mount(_ => TextBlock("second"));
        Assert.Equal(1, cleanups);
        Pump();
        Assert.Equal(["second"], Texts(host));
    }

    // A root that throws while 'fail' is set, over a counter whose state the tests follow.
    private sealed class Failing
    {
        public bool Fail;
        public Action<int>? Rerender;

        public Element Root(RenderContext ctx)
        {
            var (tick, setTick) = ctx.UseState(0);
            Rerender = setTick;
            if (Fail) throw new InvalidOperationException("boom " + tick);
            return VStack(TextBlock($"tick {tick}"), Component(Examples.Counter));
        }
    }

    [AvaloniaFact]
    public void A_root_error_with_no_handler_shows_the_exception_and_the_next_pass_recovers_with_state()
    {
        var app = new Failing();
        var host = new ReactorHostControl(app.Root);
        Pump();
        Click(ButtonLabelled(host, "Click"));
        Pump();
        Assert.Equal(["tick 0", "Clicked 1 times"], Texts(host));

        app.Fail = true;
        app.Rerender!(1);
        Pump();
        var panel = Assert.IsType<Border>(host.Child);
        var text = Assert.IsType<SelectableTextBlock>(Assert.IsType<ScrollViewer>(panel.Child).Content);
        Assert.Contains("boom 1", text.Text);

        app.Fail = false;
        app.Rerender!(2);
        Pump();
        Assert.Equal(["tick 2", "Clicked 1 times"], Texts(host)); // the kept tree: the counter's state survived
    }

    [AvaloniaFact]
    public void The_handler_gets_the_error_and_its_element_replaces_the_content()
    {
        var errors = new List<RenderError>();
        ReactorApp.DefaultRenderErrorHandler = e => throw new InvalidOperationException("the host's own handler wins");
        var app = new Failing { Fail = true };
        var host = new ReactorHostControl(app.Root)
        {
            RenderErrorHandler = e => { errors.Add(e); return TextBlock("fallback: " + e.Exception.Message); },
        };
        Pump();
        var error = Assert.Single(errors);
        Assert.Equal(RenderErrorSource.RootRender, error.Source);
        Assert.True(error.IsHostLevel);
        Assert.Equal(["fallback: boom 0"], Texts(host));

        app.Fail = false;
        app.Rerender!(1);
        Pump();
        Assert.Equal(["tick 1", "Clicked 0 times"], Texts(host));
    }

    [AvaloniaFact]
    public void The_app_default_handler_is_used_and_errors_are_attributed_to_their_phase()
    {
        var sources = new List<RenderErrorSource>();
        ReactorApp.DefaultRenderErrorHandler = e => { sources.Add(e.Source); return TextBlock("fallback"); };

        new ReactorHostControl(ctx =>
        {
            ctx.UseEffect(() => throw new InvalidOperationException("effect"), Array.Empty<object>());
            return TextBlock("x");
        });
        new ReactorHostControl(_ => Component(_ => throw new InvalidOperationException("child")));
        Pump();
        Assert.Equal([RenderErrorSource.Effects, RenderErrorSource.Reconcile], sources);
    }

    [AvaloniaFact]
    public void A_throwing_handler_shows_the_neutral_message_and_releases_the_tree()
    {
        int cleanups = 0;
        var app = new Failing();
        var host = new ReactorHostControl(ctx => VStack(
            Component(c => { c.UseEffect(() => () => cleanups++, Array.Empty<object>()); return TextBlock("child"); }),
            Component(app.Root)))
        {
            RenderErrorHandler = _ => throw new InvalidOperationException("handler"),
        };
        Pump();
        app.Fail = true;
        app.Rerender!(1); // the child component's failure reaches the host: a reconcile error
        Pump();
        Assert.Equal([ReactorHostControl.SafeText], Texts(host));
        Assert.Equal(1, cleanups);
    }

    [AvaloniaFact]
    public void Propagate_shows_nothing_and_rethrows_out_of_the_dispatcher()
    {
        var app = new Failing { Fail = true };
        var host = new ReactorHostControl(app.Root);
        host.RenderErrorHandler = e =>
        {
            e.Propagate();
            app.Fail = false;
            app.Rerender!(1); // a change during the failed pass
            return null;
        };
        var ex = Assert.Throws<InvalidOperationException>(Pump);
        Assert.Equal("boom 0", ex.Message);
        Assert.Null(host.Child);

        Pump(); // the gate reopened and the change still renders
        Assert.Equal(["tick 1", "Clicked 0 times"], Texts(host));
        Assert.True(host.IsIdle);

        // Avalonia also faults the dispatcher operation's task with the escaped exception, and nothing observes it.
        // Surface it now, while this collection runs alone, so it can't land in another test's unobserved-exception
        // check (upstream's UseResourceThreadingTests count every unobserved task exception while they run).
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    }

    [AvaloniaFact]
    public void A_throwing_cleanup_on_Dispose_is_reported_or_rethrown_after_the_teardown()
    {
        int otherCleanups = 0;
        Element Root(RenderContext ctx)
        {
            ctx.UseEffect(() => () => throw new InvalidOperationException("cleanup"), Array.Empty<object>());
            return VStack(
                Component(c => { c.UseEffect(() => () => throw new InvalidOperationException("child cleanup"), Array.Empty<object>()); return TextBlock("a"); }),
                Component(c => { c.UseEffect(() => () => otherCleanups++, Array.Empty<object>()); return TextBlock("b"); }));
        }

        var unhandled = new ReactorHostControl(Root);
        var reported = new ReactorHostControl(Root);
        var sources = new List<RenderErrorSource>();
        reported.RenderErrorHandler = e => { sources.Add(e.Source); return null; };
        Pump();

        var ex = Assert.Throws<InvalidOperationException>(unhandled.Dispose);
        Assert.Equal("cleanup", ex.Message);
        Assert.Equal(1, otherCleanups); // the rest of the teardown still ran
        Assert.Null(unhandled.Child);

        reported.Dispose();
        Assert.Equal([RenderErrorSource.Cleanup, RenderErrorSource.Cleanup], sources);
        Assert.Equal(2, otherCleanups);
    }

    [AvaloniaFact]
    public void Root_effects_run_once_the_tree_is_committed()
    {
        ReactorHostControl? host = null;
        var seen = new List<string?>();
        host = new ReactorHostControl(ctx =>
        {
            ctx.UseEffect(() => seen.AddRange(Texts(host!)), Array.Empty<object>());
            return TextBlock("mounted");
        });
        Pump();
        Assert.Equal(["mounted"], seen);
    }

    [AvaloniaFact]
    public void Dispose_during_a_pass_drops_what_the_pass_mounted()
    {
        ReactorHostControl? host = null;
        int cleanups = 0;
        host = new ReactorHostControl(_ => Component(c =>
        {
            c.UseEffect(() => { host!.Dispose(); return () => cleanups++; }, Array.Empty<object>());
            return TextBlock("x");
        }));
        Pump();
        Assert.Null(host.Child);
        Assert.Equal(1, cleanups);
        Assert.True(host.IsIdle);
    }

    [AvaloniaFact]
    public void A_reconcile_error_releases_the_half_updated_tree()
    {
        int cleanups = 0;
        var app = new Failing();
        var host = new ReactorHostControl(_ => VStack(
            Component(c => { c.UseEffect(() => () => cleanups++, Array.Empty<object>()); return TextBlock("sibling"); }),
            Component(app.Root)));
        Pump();
        app.Fail = true;
        app.Rerender!(1); // a child component throws: the reconcile fails part-way
        Pump();
        Assert.IsType<Border>(host.Child);
        Assert.Equal(1, cleanups);
    }

    private static Element Counter(RenderContext ctx) => Examples.Counter(ctx);

    // The guide's "Running an app" block.
    [AvaloniaFact]
    public void The_guide_host_snippet_mounts_shows_errors_and_disposes()
    {
        var window = new Window();
        window.Show();
        #region host-control
        // A root component inside any Avalonia layout: here, a window's content.
        var host = new ReactorHostControl(Counter)
        {
            // Optional: what to show when a render throws (null keeps the built-in panel with the exception).
            RenderErrorHandler = error => TextBlock($"Something broke: {error.Exception.Message}"),
        };
        window.Content = host;

        // When it's no longer needed: unmounts the tree and runs the effects' cleanups.
        host.Dispose();
        #endregion
        Dispatcher.UIThread.RunJobs(); // the pass Mount asked for finds the host disposed
        Assert.Null(host.Child);
        Assert.True(host.IsIdle);
        window.Close();
    }

    [AvaloniaFact]
    public void The_sample_renders_in_a_window_and_closing_it_disposes_the_host()
    {
        var window = ReactorApp.OpenWindow("Isotope samples", SampleApp.Root, width: 520, height: 640);
        Pump();
        var host = Assert.IsType<ReactorHostControl>(window.Content);
        var texts = Texts(host);
        Assert.Contains("Clicked 0 times", texts);
        Assert.Contains("0 of 0 left", texts);

        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        var path = Path.Combine(AppContext.BaseDirectory, "sample-app.png");
#pragma warning disable CS0618 // Save(string) is obsolete in 12.x but still the plain PNG path (as in Spike D)
        frame.Save(path);
#pragma warning restore CS0618
        Assert.True(new FileInfo(path).Length > 1000);

        window.Close();
        Assert.Null(host.Child);
        Assert.True(host.IsIdle);
    }
}
