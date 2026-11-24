using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Error boundaries (#25), headless: a failing subtree shows its boundary's fallback and the rest of the tree keeps its
/// controls and state; nested boundaries, retries, keys, effects, the rollback of a failed child, and errors outside any
/// boundary (still the host's). Some run in a <see cref="ReactorHostControl"/>, which sets process-wide state, so the
/// class runs alone and restores it.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class ErrorBoundaryTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly RenderErrorHandler? _previousHandler = ReactorApp.DefaultRenderErrorHandler;

    public void Dispose()
    {
        ReactorApp.UIDispatcher = _previousDispatcher;
        ReactorApp.DefaultRenderErrorHandler = _previousHandler;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static List<T> All<T>(Control? root) where T : Control
    {
        var found = new List<T>();
        void Walk(Control? c)
        {
            switch (c)
            {
                case null: return;
                case T t: found.Add(t); break;
            }
            switch (c)
            {
                case Panel p: foreach (var child in p.Children) Walk(child); break;
                case Decorator d: Walk(d.Child); break;
                case ContentControl { Content: Control content }: Walk(content); break;
            }
        }
        Walk(root);
        return found;
    }

    private static List<string?> Texts(Control? root) => All<TextBlock>(root).Select(t => t.Text).ToList();

    private static Button ButtonLabelled(Control root, string label)
        => All<Button>(root).FirstOrDefault(b => Equals(b.Content, label)) ?? throw new InvalidOperationException("no button " + label);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

    private static Element Throws(string message) => Component(_ => throw new InvalidOperationException(message));

    // A component whose effect cleanup counts into 'cleanups'.
    private static Element Tracked(StrongBox<int> cleanups, string text) => Component(c =>
    {
        c.UseEffect(() => () => cleanups.Value++, Array.Empty<object>());
        return TextBlock(text);
    });

    private sealed class StrongBox<T>(T value)
    {
        public T Value = value;
    }

    [AvaloniaFact]
    public void A_child_that_throws_on_mount_shows_the_fallback_and_the_rest_of_the_tree_works()
    {
        var errors = new List<RenderError>();
        var host = new ReactorHostControl(_ => VStack(
            ErrorBoundary(Throws("boom"), ex => TextBlock("fallback: " + ex.Message)),
            Component(Examples.Counter)))
        {
            RenderErrorHandler = e => { errors.Add(e); return null; },
        };
        Pump();
        Assert.Equal(["fallback: boom", "Clicked 0 times"], Texts(host));

        Click(ButtonLabelled(host, "Click"));
        Pump();
        Assert.Equal(["fallback: boom", "Clicked 1 times"], Texts(host));
        Assert.Empty(errors); // caught by the boundary: the host's handler never sees it
    }

    [AvaloniaFact]
    public void A_child_whose_own_state_change_throws_is_replaced_and_its_sibling_keeps_its_state_and_controls()
    {
        var errors = new List<RenderError>();
        var host = new ReactorHostControl(_ => VStack(
            Component(Examples.Counter),
            ErrorBoundary(
                Component(c =>
                {
                    var (broken, setBroken) = c.UseState(false);
                    if (broken) throw new InvalidOperationException("broke itself");
                    return Button("Break", () => setBroken(true));
                }),
                ex => TextBlock("fallback: " + ex.Message))))
        {
            RenderErrorHandler = e => { errors.Add(e); return null; },
        };
        Pump();
        Click(ButtonLabelled(host, "Click"));
        Pump();
        var counterText = All<TextBlock>(host).Single();
        Assert.Equal("Clicked 1 times", counterText.Text);

        Click(ButtonLabelled(host, "Break"));
        Pump();

        Assert.Equal(["Clicked 1 times", "fallback: broke itself"], Texts(host));
        Assert.Same(counterText, All<TextBlock>(host)[0]); // the sibling wasn't remounted
        Assert.Empty(errors);
    }

    [AvaloniaFact]
    public void Nested_boundaries_catch_at_the_nearest_one()
    {
        var host = new TestHost(() => ErrorBoundary(
            VStack(
                TextBlock("outer child"),
                ErrorBoundary(Throws("inner"), ex => TextBlock("inner fallback: " + ex.Message))),
            ex => TextBlock("outer fallback: " + ex.Message)));

        Assert.Equal(["outer child", "inner fallback: inner"], host.Texts());
        Assert.Equal(2, host.Reconciler.ErrorBoundaryNodeCount);
    }

    [AvaloniaFact]
    public void A_fallback_that_throws_goes_to_the_next_boundary_out()
    {
        var host = new TestHost(() => ErrorBoundary(
            VStack(
                TextBlock("outer child"),
                ErrorBoundary(Throws("inner"), _ => throw new InvalidOperationException("inner fallback"))),
            ex => TextBlock("outer fallback: " + ex.Message)));

        Assert.Equal(["outer fallback: inner fallback"], host.Texts());
        Assert.Equal(1, host.Reconciler.ErrorBoundaryNodeCount);
    }

    [AvaloniaFact]
    public void A_fallback_that_throws_with_no_boundary_out_is_discarded_and_escapes()
    {
        var partial = new StrongBox<int>(0);
        var ex = Assert.Throws<InvalidOperationException>(() => new TestHost(() => ErrorBoundary(
            Throws("child"),
            _ => VStack(Tracked(partial, "part of the fallback"), Throws("fallback")))));

        Assert.Equal("fallback", ex.Message);
        Assert.Equal(1, partial.Value); // what the failed fallback mounted is torn down
    }

    [AvaloniaFact]
    public void A_re_render_of_the_boundary_retries_the_child_and_recovers()
    {
        bool fail = true;
        int renders = 0;
        var host = new TestHost(() => ErrorBoundary(
            Component(c =>
            {
                renders++;
                if (fail) throw new InvalidOperationException("not yet");
                var (n, setN) = c.UseState(0);
                return Button($"n={n}", () => setN(n + 1));
            }),
            ex => TextBlock("fallback: " + ex.Message)));
        Assert.Equal(["fallback: not yet"], host.Texts());

        host.Render(); // still failing: retried, and the fallback again
        Assert.Equal(2, renders);
        Assert.Equal(["fallback: not yet"], host.Texts());

        fail = false;
        host.Render();
        Assert.Empty(host.Texts());
        var button = Assert.Single(host.All<Button>());
        Assert.Equal("n=0", button.Content);

        TestHost.Click(button);
        host.Flush();
        host.Render(); // a working child is updated in place, not remounted
        Assert.Same(button, Assert.Single(host.All<Button>()));
        Assert.Equal("n=1", button.Content);
    }

    [AvaloniaFact]
    public void A_new_key_remounts_the_child()
    {
        string key = "first";
        var host = new TestHost(() => ErrorBoundary(Component(Examples.Counter), _ => TextBlock("fallback")).WithKey(key));
        TestHost.Click(host.All<Button>().Single());
        host.Flush();
        Assert.Equal(["Clicked 1 times"], host.Texts());
        var button = host.All<Button>().Single();

        key = "second";
        host.Render();

        Assert.Equal(["Clicked 0 times"], host.Texts());
        Assert.NotSame(button, host.All<Button>().Single());
    }

    [AvaloniaFact]
    public void An_effect_that_throws_in_the_subtree_is_caught()
    {
        var host = new TestHost(() => VStack(
            TextBlock("sibling"),
            ErrorBoundary(
                Component(c =>
                {
                    c.UseEffect(() => throw new InvalidOperationException("effect"), Array.Empty<object>());
                    return TextBlock("child");
                }),
                ex => TextBlock("fallback: " + ex.Message))));

        Assert.Equal(["sibling", "fallback: effect"], host.Texts());
    }

    [AvaloniaFact]
    public void A_child_that_fails_part_way_through_its_mount_is_rolled_back()
    {
        var sibling = new StrongBox<int>(0);
        var parent = new StrongBox<int>(0);
        var host = new TestHost(() => ErrorBoundary(
            VStack(
                Tracked(sibling, "mounted before the failure"),
                Component(c =>
                {
                    // Its effect runs before its child mounts and throws.
                    c.UseEffect(() => () => parent.Value++, Array.Empty<object>());
                    return VStack(Throws("deep"));
                })),
            _ => TextBlock("fallback")));

        Assert.Equal(["fallback"], host.Texts());
        Assert.Equal(1, sibling.Value); // a completed subtree of the failed child: unmounted
        Assert.Equal(1, parent.Value);  // a component whose mount failed: its cleanups run
        Assert.Equal(0, host.Reconciler.ComponentNodeCount);
    }

    [AvaloniaFact]
    public void A_failed_retry_unmounts_the_child_and_what_the_retry_mounted()
    {
        var old = new StrongBox<int>(0);
        var added = new StrongBox<int>(0);
        bool fail = false;
        var host = new TestHost(() => ErrorBoundary(
            fail ? VStack(Tracked(added, "added"), Throws("retry")) : Tracked(old, "old"),
            _ => TextBlock("fallback")));
        Assert.Equal(["old"], host.Texts());

        fail = true;
        host.Render();

        Assert.Equal(["fallback"], host.Texts());
        Assert.Equal(1, old.Value);   // the child the retry replaced
        Assert.Equal(1, added.Value); // mounted by the failed retry, never attached
        Assert.Equal(0, host.Reconciler.ComponentNodeCount);
    }

    [AvaloniaFact]
    public void A_failed_retry_in_place_unmounts_the_child_and_what_it_added()
    {
        var kept = new StrongBox<int>(0);
        var added = new StrongBox<int>(0);
        bool fail = false;
        var host = new TestHost(() => ErrorBoundary(
            fail ? VStack(Tracked(kept, "kept"), Tracked(added, "added"), Throws("retry")) : VStack(Tracked(kept, "kept")),
            _ => TextBlock("fallback")));

        fail = true;
        host.Render();

        Assert.Equal(["fallback"], host.Texts());
        Assert.Equal(1, kept.Value);
        Assert.Equal(1, added.Value);
        Assert.Equal(0, host.Reconciler.ComponentNodeCount);
    }

    [AvaloniaFact]
    public void An_error_outside_every_boundary_still_reaches_the_host()
    {
        var errors = new List<RenderError>();
        var host = new ReactorHostControl(_ => VStack(
            ErrorBoundary(TextBlock("guarded"), TextBlock("fallback")),
            Throws("outside")))
        {
            RenderErrorHandler = e => { errors.Add(e); return TextBlock("host: " + e.Exception.Message); },
        };
        Pump();

        var error = Assert.Single(errors);
        Assert.Equal(RenderErrorSource.Reconcile, error.Source);
        Assert.Equal(["host: outside"], Texts(host));
    }

    [AvaloniaFact]
    public void The_guide_example_isolates_the_failing_panel_and_its_retry_recovers()
    {
        var original = ErrorBoundaryExamples.Forecast;
        try
        {
            var host = new ReactorHostControl(ErrorBoundaryExamples.Dashboard);
            Pump();
            Assert.Equal(["The weather panel failed: no forecast yet", "Clicked 0 times"], Texts(host));
            Click(ButtonLabelled(host, "Click"));
            Pump();

            ErrorBoundaryExamples.Forecast = () => "sunny";
            Click(ButtonLabelled(host, "Retry"));
            Pump();

            Assert.Equal(["Today: sunny", "Clicked 1 times"], Texts(host));
            host.Dispose();
        }
        finally
        {
            ErrorBoundaryExamples.Forecast = original;
        }
    }
}
