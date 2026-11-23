using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Upstream's RenderContext hooks core, unmodified apart from the dropped tier 3 hooks, on Avalonia.</summary>
public class HooksTests
{
    [AvaloniaFact]
    public void UseState_setter_rerenders_and_keeps_the_controls()
    {
        var host = new TestHost(() => Component(ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            return VStack(TextBlock($"Clicked {count} times"), Button("Click", () => setCount(count + 1)));
        }));
        var text = host.All<TextBlock>()[0];

        TestHost.Click(host.All<Button>()[0]);
        Assert.True(host.RerenderRequested);
        host.Flush();

        Assert.Equal("Clicked 1 times", text.Text);
        Assert.Same(text, host.All<TextBlock>()[0]);
    }

    [AvaloniaFact]
    public void UseState_setting_an_equal_value_does_not_ask_for_a_render()
    {
        Action<int>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (value, setValue) = ctx.UseState(5);
            set = setValue;
            return TextBlock(value.ToString());
        }));

        set!(5);

        Assert.False(host.RerenderRequested);
    }

    [AvaloniaFact]
    public void UseReducer_applies_the_reducer_to_each_dispatch()
    {
        Action<string>? dispatch = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (total, send) = ctx.UseReducer<int, string>((state, action) => action == "add" ? state + 10 : state - 1, 0);
            dispatch = send;
            return TextBlock(total.ToString());
        }));

        dispatch!("add");
        dispatch!("sub");
        host.Flush();

        Assert.Equal(["9"], host.Texts());
    }

    [AvaloniaFact]
    public void UseEffect_runs_after_render_reruns_on_a_dependency_change_and_cleans_up()
    {
        var log = new List<string>();
        var dep = 1;
        var show = true;
        var host = new TestHost(() => VStack(show
            ? Component(ctx =>
            {
                var d = dep;
                ctx.UseEffect(() =>
                {
                    log.Add($"run {d}");
                    return () => log.Add($"cleanup {d}");
                }, d);
                return TextBlock("x");
            })
            : EmptyElement.Instance));
        Assert.Equal(["run 1"], log);

        host.Render(); // same dependency: no re-run
        Assert.Equal(["run 1"], log);

        dep = 2;
        host.Render();
        Assert.Equal(["run 1", "cleanup 1", "run 2"], log);

        show = false;
        host.Render();
        Assert.Equal(["run 1", "cleanup 1", "run 2", "cleanup 2"], log);
    }

    [AvaloniaFact]
    public void UseMemo_recomputes_only_when_its_dependencies_change()
    {
        int computed = 0;
        var dep = "a";
        var host = new TestHost(() => Component(ctx =>
        {
            var d = dep;
            var value = ctx.UseMemo(() => { computed++; return d.ToUpperInvariant(); }, d);
            return TextBlock(value);
        }));
        host.Render();
        Assert.Equal(1, computed);

        dep = "b";
        host.Render();

        Assert.Equal(2, computed);
        Assert.Equal(["B"], host.Texts());
    }

    [AvaloniaFact]
    public void UseRef_keeps_its_value_across_renders_without_asking_for_one()
    {
        Ref<int>? seen = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var r = ctx.UseRef(0);
            r.Current++;
            seen = r;
            return TextBlock(r.Current.ToString());
        }));
        var first = seen;
        host.Render();
        host.Render();

        Assert.Same(first, seen);
        Assert.Equal(3, seen!.Current);
        Assert.False(host.RerenderRequested);
    }

    private static readonly Context<string> Theme = new("default");

    [AvaloniaFact]
    public void UseContext_reads_the_nearest_provider_or_the_default()
    {
        Element Reader(string key) => Component(ctx => TextBlock(ctx.UseContext(Theme))).WithKey(key);
        var host = new TestHost(() => VStack(
            Reader("outside"),
            VStack(Reader("outer"), VStack(Reader("inner")).Provide(Theme, "inner")).Provide(Theme, "outer")));

        Assert.Equal(["default", "outer", "inner"], host.Texts());
    }

    [AvaloniaFact]
    public void A_provider_change_reaches_a_consumer_inside_a_reused_subtree()
    {
        var value = "one";
        var consumer = VStack(Component(ctx => TextBlock(ctx.UseContext(Theme))));
        var host = new TestHost(() => VStack(consumer).Provide(Theme, value));
        Assert.Equal(["one"], host.Texts());

        value = "two";
        host.Render();

        Assert.Equal(["two"], host.Texts());
    }

    [AvaloniaFact]
    public void A_removed_provider_sends_a_reused_consumer_back_to_the_default()
    {
        var provide = true;
        var consumer = VStack(Component(ctx => TextBlock(ctx.UseContext(Theme))));
        var host = new TestHost(() => provide ? VStack(consumer).Provide(Theme, "one") : VStack(consumer));
        Assert.Equal(["one"], host.Texts());

        provide = false;
        host.Render();

        Assert.Equal(["default"], host.Texts());
    }

    [AvaloniaFact]
    public void A_provider_change_reaches_memo_components_and_what_they_render()
    {
        var value = "one";
        Element Inner(RenderContext ctx) => TextBlock("inner " + ctx.UseContext(Theme));
        var host = new TestHost(() => VStack(
            new MemoElement(ctx => TextBlock("memo " + ctx.UseContext(Theme))),
            new MemoElement(_ => VStack(new FuncElement(Inner)))).Provide(Theme, value));
        Assert.Equal(["memo one", "inner one"], host.Texts());

        value = "two";
        host.Render();

        Assert.Equal(["memo two", "inner two"], host.Texts());
    }

    [AvaloniaFact]
    public void A_setter_called_from_render_under_a_synchronous_host_fails_as_a_render_loop()
    {
        var reconciler = new Reconciler();
        Element? last = null;
        global::Avalonia.Controls.Control? root = null;
        Element App() => Component(ctx =>
        {
            var (value, set) = ctx.UseState(0);
            set(value + 1);
            return TextBlock(value.ToString());
        });
        void Render()
        {
            var element = App();
            root = reconciler.Reconcile(last, element, root, Render);
            last = element;
        }

        var error = Assert.Throws<InvalidOperationException>(Render);
        Assert.Contains("Render loop detected", error.Message);
    }

    [AvaloniaFact]
    public void Changing_the_hook_order_throws_HookOrderException()
    {
        var useRefFirst = false;
        var host = new TestHost(() => Component(ctx =>
        {
            if (useRefFirst) ctx.UseRef(0);
            ctx.UseState(0);
            return TextBlock("x");
        }));

        useRefFirst = true;
        Assert.Throws<HookOrderException>(host.Render);
    }

    [AvaloniaFact]
    public void A_state_change_inside_a_reused_element_subtree_still_renders()
    {
        Element Counter(RenderContext ctx)
        {
            var (count, setCount) = ctx.UseState(0);
            return VStack(TextBlock($"count {count}"), Button("inc", () => setCount(count + 1)));
        }
        int parentRenders = 0;
        var host = new TestHost(() => Component(ctx =>
        {
            parentRenders++;
            // The same element instance every render: the child diff would skip it wholesale
            // unless the dirty-ancestor path makes it descend to the counter.
            return ctx.UseMemo(() => (Element)VStack(new FuncElement(Counter)), Array.Empty<object>());
        }));

        TestHost.Click(host.All<Button>()[0]);
        host.Flush();

        Assert.Equal("count 1", host.Texts()[0]);
        Assert.Equal(2, parentRenders);
    }

    [AvaloniaFact]
    public void UsePersisted_restores_the_value_for_a_remounted_component()
    {
        var key = "hooks-test-" + Guid.NewGuid();
        Action<int>? set = null;
        var show = true;
        var host = new TestHost(() => VStack(show
            ? Component(ctx =>
            {
                var (value, setValue) = ctx.UsePersisted(key, 1, PersistedScope.Application);
                set = setValue;
                return TextBlock(value.ToString());
            })
            : EmptyElement.Instance));
        set!(42);
        host.Flush();

        show = false;
        host.Render();
        show = true;
        host.Render();

        Assert.Equal(["42"], host.Texts());
    }
}

/// <summary>
/// Off-thread state setters, through the DispatcherQueue seam over Avalonia's dispatcher. These set the process-wide
/// <see cref="ReactorApp.UIDispatcher"/>, which upstream's vendored hook tests read (they expect none), so they run
/// alone.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public class ThreadingTests
{
    [AvaloniaFact]
    public void An_off_thread_setter_with_no_captured_dispatcher_throws()
    {
        var previous = ReactorApp.UIDispatcher;
        ReactorApp.UIDispatcher = null;
        try
        {
            Action<int>? set = null;
            _ = new TestHost(() => Component(ctx => { set = ctx.UseState(0).Set; return TextBlock("x"); }));

            var error = OtherThread.Run(() => set!(1));

            Assert.NotNull(error);

            Assert.IsType<InvalidOperationException>(error);
            Assert.Contains("no UI dispatcher", error.Message);
        }
        finally
        {
            ReactorApp.UIDispatcher = previous;
        }
    }

    [AvaloniaFact]
    public void An_off_thread_setter_is_marshalled_to_the_UI_thread()
    {
        var previous = ReactorApp.UIDispatcher;
        ReactorApp.UIDispatcher = DispatcherQueue.ForUIThread;
        try
        {
            Action<int>? set = null;
            var host = new TestHost(() => Component(ctx =>
            {
                var (value, setValue) = ctx.UseState(0);
                set = setValue;
                return TextBlock(value.ToString());
            }));

            Assert.Null(OtherThread.Run(() => set!(7)));
            Assert.False(host.RerenderRequested); // queued, not applied off-thread
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.RerenderRequested);
            host.Flush();

            Assert.Equal(["7"], host.Texts());
        }
        finally
        {
            ReactorApp.UIDispatcher = previous;
        }
    }

    [AvaloniaFact]
    public void A_thread_safe_setter_off_the_UI_thread_asks_for_a_render_on_it()
    {
        var previous = ReactorApp.UIDispatcher;
        ReactorApp.UIDispatcher = DispatcherQueue.ForUIThread;
        try
        {
            Action<int>? set = null;
            var host = new TestHost(() => Component(ctx =>
            {
                var (value, setValue) = ctx.UseState(0, threadSafe: true);
                set = setValue;
                return TextBlock(value.ToString());
            }));

            Assert.Null(OtherThread.Run(() => set!(5)));
            Assert.False(host.RerenderRequested); // the render request was moved to the UI thread
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.RerenderRequested);
            host.Flush();

            Assert.Equal(["5"], host.Texts());
        }
        finally
        {
            ReactorApp.UIDispatcher = previous;
        }
    }

    [AvaloniaFact]
    public void The_queue_exists_on_the_UI_thread_only_and_posts_to_it()
    {
        Assert.NotNull(DispatcherQueue.GetForCurrentThread());
        DispatcherQueue? offThread = DispatcherQueue.ForUIThread;
        Assert.Null(OtherThread.Run(() => offThread = DispatcherQueue.GetForCurrentThread()));
        Assert.Null(offThread);

        int? ranOn = null;
        Assert.True(DispatcherQueue.ForUIThread.TryEnqueue(() => ranOn = Environment.CurrentManagedThreadId));
        Assert.Null(ranOn);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
    }
}

/// <summary>Tests that change <see cref="ReactorApp"/>'s process-wide state run alone, outside the parallel tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReactorAppGlobals
{
    public const string Name = "ReactorAppGlobals";
}

/// <summary>Runs an action on a new thread (never inlined on the caller's), returning what it threw.</summary>
internal static class OtherThread
{
    public static Exception? Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        });
        thread.Start();
        thread.Join();
        return error;
    }
}
