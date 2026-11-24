using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// A component that fails outside every error boundary (#28), headless: its slot shows a fallback and the rest of the
/// tree keeps its state, as upstream's; the host's handler is told and its outcomes apply to the slot; a pass that
/// throws tears down what it mounted; and a control's unmount hook that throws inside an isolated teardown. Some run
/// in a <see cref="ReactorHostControl"/>, which sets process-wide state, so the class runs alone and restores it.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class ComponentErrorTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;
    private readonly RenderErrorHandler? _previousHandler = ReactorApp.DefaultRenderErrorHandler;

    public ComponentErrorTests() => ReactorApp.DefaultRenderErrorHandler = null;

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

    // ── The fallback in the component's slot ─────────────────────────────

    [AvaloniaFact]
    public void A_component_whose_state_change_throws_shows_a_fallback_in_its_slot_and_its_sibling_keeps_its_state()
    {
        var errors = new List<RenderError>();
        using var host = new ReactorHostControl(_ => VStack(
            Component(Examples.Counter),
            Component(c =>
            {
                var (broken, setBroken) = c.UseState(false);
                if (broken) throw new InvalidOperationException("broke itself");
                return Button("Break", () => setBroken(true));
            })))
        {
            RenderErrorHandler = e => { errors.Add(e); return TextBlock("slot: " + e.Exception.Message); },
        };
        Pump();
        Click(ButtonLabelled(host, "Click"));
        Pump();
        var counterText = All<TextBlock>(host)[0];

        Click(ButtonLabelled(host, "Break"));
        Pump();

        Assert.Equal(["Clicked 1 times", "slot: broke itself"], Texts(host));
        Assert.Same(counterText, All<TextBlock>(host)[0]); // the sibling wasn't remounted
        var error = Assert.Single(errors);
        Assert.Equal(RenderErrorSource.ComponentRender, error.Source);
        Assert.False(error.IsHostLevel);
        Assert.Null(error.ComponentName); // a function component has no type name, as upstream's

        // The sibling still works. The root's render runs again with it, so the failed component is retried, fails again
        // (its state still says broken) and the handler is told again, as upstream's; the fallback is updated in place.
        var slotText = All<TextBlock>(host)[1];
        Click(ButtonLabelled(host, "Click"));
        Pump();
        Assert.Equal(["Clicked 2 times", "slot: broke itself"], Texts(host));
        Assert.Same(counterText, All<TextBlock>(host)[0]);
        Assert.Same(slotText, All<TextBlock>(host)[1]);
        Assert.Equal(2, errors.Count);
    }

    [AvaloniaFact]
    public void With_no_handler_the_slot_shows_the_exception_and_the_component_stays_mounted()
    {
        var host = new TestHost(() => VStack(TextBlock("before"), Throws("boom"), TextBlock("after")));

        var texts = host.Texts();
        Assert.Equal(3, texts.Count);
        Assert.Equal("before", texts[0]);
        Assert.StartsWith("⚠ Render error: InvalidOperationException: boom", texts[1]);
        Assert.Contains("System.InvalidOperationException: boom", texts[1]); // the full exception, as upstream's
        Assert.Equal("after", texts[2]);
        Assert.Single(host.All<ScrollViewer>());
        Assert.Equal(1, host.Reconciler.ComponentNodeCount);
    }

    [AvaloniaFact]
    public void A_later_render_retries_the_component_and_it_recovers_with_its_state()
    {
        bool fail = false;
        Action<int>? set = null;
        var host = new TestHost(() => VStack(Component(c =>
        {
            var (n, setN) = c.UseState(0);
            set = setN;
            if (fail && n == 1) throw new InvalidOperationException("not at one");
            return TextBlock($"n={n}");
        })));
        fail = true;
        set!(1);
        host.Flush();
        Assert.StartsWith("⚠ Render error: InvalidOperationException: not at one", Assert.Single(host.Texts()));

        fail = false;
        host.Render(); // the parent renders it again: retried

        Assert.Equal(["n=1"], host.Texts()); // its state survived the failure
    }

    private sealed class EffectThrows : Component
    {
        public override Element Render()
        {
            UseEffect(() => throw new InvalidOperationException("effect"), Array.Empty<object>());
            return TextBlock("rendered");
        }
    }

    [AvaloniaFact]
    public void An_effect_that_throws_is_reported_as_Effects_with_the_class_components_name()
    {
        var errors = new List<RenderError>();
        ReactorApp.DefaultRenderErrorHandler = e => { errors.Add(e); return TextBlock("slot"); };

        var host = new TestHost(() => VStack(TextBlock("sibling"), new ComponentElement(typeof(EffectThrows))));

        var error = Assert.Single(errors);
        Assert.Equal(RenderErrorSource.Effects, error.Source);
        Assert.Equal(nameof(EffectThrows), error.ComponentName);
        Assert.False(error.IsHostLevel);
        Assert.Equal(["sibling", "slot"], host.Texts());
    }

    [AvaloniaFact]
    public void A_handler_that_throws_gets_the_neutral_text_in_the_slot()
    {
        ReactorApp.DefaultRenderErrorHandler = _ => throw new InvalidOperationException("handler");

        var host = new TestHost(() => VStack(TextBlock("sibling"), Throws("boom")));

        Assert.Equal(["sibling", Reconciler.NeutralFallbackText], host.Texts());
    }

    [AvaloniaFact]
    public void A_handler_element_that_throws_gets_the_neutral_text_and_the_handler_is_not_asked_again()
    {
        int calls = 0;
        ReactorApp.DefaultRenderErrorHandler = _ =>
        {
            calls++;
            return Component(_ => throw new InvalidOperationException("the fallback"));
        };

        var host = new TestHost(() => VStack(TextBlock("sibling"), Throws("boom")));

        Assert.Equal(["sibling", Reconciler.NeutralFallbackText], host.Texts());
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public void Inside_a_boundary_the_boundary_catches_and_the_handler_is_not_told()
    {
        int calls = 0;
        ReactorApp.DefaultRenderErrorHandler = _ => { calls++; return TextBlock("slot"); };

        var host = new TestHost(() => VStack(ErrorBoundary(
            VStack(TextBlock("inside"), Throws("boom")),
            ex => TextBlock("boundary: " + ex.Message))));

        Assert.Equal(["boundary: boom"], host.Texts());
        Assert.Equal(0, calls);
    }

    [AvaloniaFact]
    public void Propagate_shows_nothing_releases_the_tree_and_rethrows_out_of_the_dispatcher()
    {
        var sibling = new StrongBox<int>(0);
        int calls = 0;
        using var host = new ReactorHostControl(_ => VStack(Tracked(sibling, "sibling"), Throws("boom")))
        {
            RenderErrorHandler = e => { calls++; e.Propagate(); return null; },
        };

        var ex = Assert.Throws<InvalidOperationException>(Pump);

        Assert.Equal("boom", ex.Message);
        Assert.Null(host.Child);
        Assert.Equal(1, calls); // the host doesn't ask again
        Assert.Equal(1, sibling.Value); // the tree is released

        // As HostTests' Propagate test: surface the faulted dispatcher task now, while this collection runs alone.
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    }

    [AvaloniaFact]
    public void A_host_fallback_with_a_component_that_throws_gets_the_neutral_text_and_the_handler_is_asked_once()
    {
        var errors = new List<RenderError>();
        using var host = new ReactorHostControl(_ => throw new InvalidOperationException("root"))
        {
            RenderErrorHandler = e =>
            {
                errors.Add(e);
                return VStack(TextBlock("host fallback"), Throws("in the fallback"));
            },
        };
        Pump();

        Assert.Equal([Reconciler.NeutralFallbackText], Texts(host));
        Assert.Equal(RenderErrorSource.RootRender, Assert.Single(errors).Source);
    }

    [AvaloniaFact]
    public void An_exception_propagated_in_one_pass_is_an_ordinary_error_in_the_next()
    {
        var cached = new InvalidOperationException("the same instance");
        bool fromRoot = false;
        Action<int>? rerender = null;
        var sources = new List<RenderErrorSource>();
        using var host = new ReactorHostControl(ctx =>
        {
            var (n, set) = ctx.UseState(0);
            rerender = set;
            if (fromRoot) throw cached;
            return VStack(Component(_ => throw cached));
        })
        {
            RenderErrorHandler = e =>
            {
                sources.Add(e.Source);
                if (e.Source == RenderErrorSource.ComponentRender) e.Propagate();
                return TextBlock("handled");
            },
        };
        Assert.Same(cached, Assert.Throws<InvalidOperationException>(Pump));

        fromRoot = true;
        rerender!(1); // the root throws the instance propagated before: the handler is asked
        Pump();

        Assert.Equal([RenderErrorSource.ComponentRender, RenderErrorSource.RootRender], sources);
        Assert.Equal(["handled"], Texts(host));
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    }

    // ── A failed mount outside every boundary ────────────────────────────

    [AvaloniaFact]
    public void A_failed_mount_outside_every_boundary_runs_its_cleanups_once()
    {
        var sibling = new StrongBox<int>(0);
        var parent = new StrongBox<int>(0);
        var reconciler = new Reconciler();
        var element = VStack(
            Tracked(sibling, "mounted before the failure"),
            Component(c =>
            {
                // Its effect runs before its child mounts and throws.
                c.UseEffect(() => () => parent.Value++, Array.Empty<object>());
                return VStack(TextBlock("x").Width(-1));
            }));

        Assert.Throws<ArgumentException>(() => reconciler.Reconcile(null, element, null, () => { }));

        Assert.Equal(1, sibling.Value); // a completed subtree the failed pass never attached
        Assert.Equal(1, parent.Value);  // a component whose mount failed
        Assert.Equal(0, reconciler.ComponentNodeCount);
    }

    [AvaloniaFact]
    public void A_failed_update_in_the_host_runs_every_cleanup_once()
    {
        var kept = new StrongBox<int>(0);
        var added = new StrongBox<int>(0);
        var parent = new StrongBox<int>(0);
        Action<bool>? setBroken = null;
        var host = new ReactorHostControl(_ => Component(c =>
        {
            var (broken, set) = c.UseState(false);
            setBroken = set;
            return broken
                ? VStack(
                    Tracked(kept, "kept"),
                    Tracked(added, "added"),
                    Component(c2 =>
                    {
                        c2.UseEffect(() => () => parent.Value++, Array.Empty<object>());
                        return TextBlock("x").Width(-1);
                    }))
                : VStack(Tracked(kept, "kept"));
        }));
        Pump();

        setBroken!(true); // the update mounts two components, and a control under the second fails
        Pump();

        Assert.IsType<Border>(host.Child); // the host's built-in panel: a reconcile error
        Assert.Equal(1, kept.Value);
        Assert.Equal(1, added.Value);
        Assert.Equal(1, parent.Value);

        host.Dispose(); // nothing is torn down twice
        Assert.Equal(1, kept.Value);
        Assert.Equal(1, added.Value);
        Assert.Equal(1, parent.Value);
    }

    // ── Isolated control teardown ───────────────────────────────────────

    // A content control, and a panel, whose descriptor's unmount hook throws. The content control's strategy tears its
    // content down itself; the panel's children are left to the engine's walk.
    private sealed record ThrowingUnmountElement(Element Child) : Element;

    private sealed record ThrowingUnmountPanelElement(params Element[] Children) : Element;

    static ComponentErrorTests()
    {
        ControlRegistry.Register<ThrowingUnmountElement, ContentControl>(static () =>
            new DescriptorHandler<ThrowingUnmountElement, ContentControl>(
                new ControlDescriptor<ThrowingUnmountElement, ContentControl>()
                    .WithContent(static e => e.Child, static _ => null)
                    .WithUnmount(static (in UnmountContext _, ContentControl _) =>
                        throw new InvalidOperationException("unmount hook"))));
        ControlRegistry.Register<ThrowingUnmountPanelElement, StackPanel>(static () =>
            new DescriptorHandler<ThrowingUnmountPanelElement, StackPanel>(
                new ControlDescriptor<ThrowingUnmountPanelElement, StackPanel>
                {
                    Children = new Panel<ThrowingUnmountPanelElement, StackPanel>(static e => e.Children, static p => p.Children),
                }.WithUnmount(static (in UnmountContext _, StackPanel _) =>
                    throw new InvalidOperationException("panel unmount hook"))));
    }

    [AvaloniaFact]
    public void A_control_unmount_hook_that_throws_in_an_isolated_teardown_leaves_the_other_cleanups_running()
    {
        var inside = new StrongBox<int>(0);
        var sibling = new StrongBox<int>(0);
        var reported = new List<RenderError>();
        var host = new ReactorHostControl(_ => VStack(
            new ThrowingUnmountElement(Tracked(inside, "inside")),
            Tracked(sibling, "sibling")))
        {
            RenderErrorHandler = e => { reported.Add(e); return null; },
        };
        Pump();
        Assert.Equal(["inside", "sibling"], Texts(host));

        host.Dispose(); // the host's teardown is isolated: every cleanup runs

        Assert.Equal(1, inside.Value);  // under the failing control: its content is still torn down
        Assert.Equal(1, sibling.Value); // after it: the walk went on
        var error = Assert.Single(reported); // reported once
        Assert.Equal(RenderErrorSource.Cleanup, error.Source);
        Assert.Equal("unmount hook", error.Exception.Message);
    }

    [AvaloniaFact]
    public void A_panel_whose_unmount_hook_throws_still_has_its_children_torn_down_once()
    {
        var first = new StrongBox<int>(0);
        var second = new StrongBox<int>(0);
        var sibling = new StrongBox<int>(0);
        var reported = new List<RenderError>();
        var host = new ReactorHostControl(_ => VStack(
            new ThrowingUnmountPanelElement(Tracked(first, "first"), Tracked(second, "second")),
            Tracked(sibling, "sibling")))
        {
            RenderErrorHandler = e => { reported.Add(e); return null; },
        };
        Pump();
        Assert.Equal(["first", "second", "sibling"], Texts(host));

        host.Dispose();

        // The hook threw before the engine's walk reached the panel's children: the recovery walk tears them down.
        Assert.Equal(1, first.Value);
        Assert.Equal(1, second.Value);
        Assert.Equal(1, sibling.Value);
        Assert.Equal("panel unmount hook", Assert.Single(reported).Exception.Message);
    }
}
