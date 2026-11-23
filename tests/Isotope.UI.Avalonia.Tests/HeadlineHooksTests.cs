using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Avalonia.Tests.Dsl;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The headline hooks (#2) through the reconciler on Avalonia, each pinned to upstream's semantics as its engine
/// implements them (src/Upstream/src/Reactor/Core/RenderContext.cs). HooksTests covers the basics; these pin the
/// details an app relies on. Upstream's own unit tests of the same hooks run too (src/Upstream/tests).
/// </summary>
public class HeadlineHooksTests
{
    [AvaloniaFact]
    public void UseState_setter_is_the_same_delegate_every_render()
    {
        var setters = new List<Action<int>>();
        var host = new TestHost(() => Component(ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            setters.Add(setCount);
            return Button($"count {count}", () => setCount(count + 1));
        }));

        TestHost.Click(host.All<Button>()[0]);
        host.Flush();

        Assert.Equal(2, setters.Count);
        Assert.Same(setters[0], setters[1]);
        Assert.Equal("count 1", host.All<Button>()[0].Content);
    }

    [AvaloniaFact]
    public void UseState_setters_called_twice_from_one_closure_see_the_rendered_value()
    {
        // Upstream's setter takes a value, not an update: two calls from the same render's closure both write
        // count + 1, so the count moves once. UseReducer's functional form is the way to compose updates.
        var host = new TestHost(() => Component(ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            return Button($"count {count}", () => { setCount(count + 1); setCount(count + 1); });
        }));

        TestHost.Click(host.All<Button>()[0]);
        host.Flush();

        Assert.Equal("count 1", host.All<Button>()[0].Content);
    }

    [AvaloniaFact]
    public void UseState_keeps_separate_state_per_component_instance()
    {
        Element Counter(string key) => Component(ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            return Button($"{key} {count}", () => setCount(count + 1));
        }, key);
        var host = new TestHost(() => VStack(Counter("a"), Counter("b")));

        TestHost.Click(host.All<Button>()[1]);
        host.Flush();
        TestHost.Click(host.All<Button>()[1]);
        host.Flush();

        Assert.Equal(["a 0", "b 2"], host.All<Button>().Select(b => (string?)b.Content));
    }

    [AvaloniaFact]
    public void UseReducer_functional_form_composes_updates_queued_before_a_render()
    {
        var renders = 0;
        var host = new TestHost(() => Component(ctx =>
        {
            renders++;
            var (count, update) = ctx.UseReducer(1);
            return Button($"count {count}", () => { update(c => c + 1); update(c => c * 10); });
        }));

        TestHost.Click(host.All<Button>()[0]);
        host.Flush();

        Assert.Equal("count 20", host.All<Button>()[0].Content);
        Assert.Equal(2, renders); // two updates, one render
    }

    [AvaloniaFact]
    public void UseReducer_functional_form_returning_an_equal_value_asks_for_no_render()
    {
        Action<Func<int, int>>? update = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (count, u) = ctx.UseReducer(3);
            update = u;
            return Text(count.ToString());
        }));

        update!(c => c);

        Assert.False(host.RerenderRequested);
    }

    private enum Op { Add, Reset }

    [AvaloniaFact]
    public void UseReducer_dispatch_is_stable_and_runs_the_current_renders_reducer()
    {
        // The dispatch delegate keeps its identity across renders but reads the latest reducer, so a reducer that
        // closes over render values (here the step) never goes stale behind a cached dispatch.
        var step = 1;
        var dispatches = new List<Action<Op>>();
        var host = new TestHost(() => Component(ctx =>
        {
            var s = step;
            var (total, dispatch) = ctx.UseReducer<int, Op>((state, op) => op == Op.Add ? state + s : 0, 0);
            dispatches.Add(dispatch);
            return Text(total.ToString());
        }));
        dispatches[0](Op.Add);
        host.Flush();
        Assert.Equal(["1"], host.Texts());

        step = 5;
        host.Render(); // the new reducer is stored on this render
        dispatches[0](Op.Add); // the first render's dispatch
        host.Flush();

        Assert.Equal(["6"], host.Texts());
        Assert.All(dispatches, d => Assert.Same(dispatches[0], d));

        dispatches[0](Op.Reset);
        host.Flush();
        Assert.Equal(["0"], host.Texts());
    }

    [AvaloniaFact]
    public void UseReducer_dispatch_that_leaves_the_state_equal_asks_for_no_render()
    {
        Action<Op>? dispatch = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (total, d) = ctx.UseReducer<int, Op>((state, op) => op == Op.Add ? state + 1 : 0, 0);
            dispatch = d;
            return Text(total.ToString());
        }));

        dispatch!(Op.Reset); // already 0

        Assert.False(host.RerenderRequested);
    }

    [AvaloniaFact]
    public void UseEffect_runs_after_the_render_function_and_an_empty_dependency_list_runs_once()
    {
        var log = new List<string>();
        var label = "a";
        var show = true;
        var host = new TestHost(() => VStack(show
            ? Component(ctx =>
            {
                log.Add("render " + label);
                ctx.UseEffect(() =>
                {
                    log.Add("mount");
                    return () => log.Add("unmount");
                });
                return Text(label);
            })
            : EmptyElement.Instance));
        Assert.Equal(["render a", "mount"], log);

        label = "b";
        host.Render();
        host.Render();
        Assert.Equal(["render a", "mount", "render b", "render b"], log);
        Assert.Equal(["b"], host.Texts());

        show = false;
        host.Render();
        Assert.Equal(["render a", "mount", "render b", "render b", "unmount"], log);
    }

    [AvaloniaFact]
    public void UseEffect_runs_every_pending_cleanup_before_any_new_effect_in_hook_order()
    {
        var log = new List<string>();
        var dep = 1;
        var host = new TestHost(() => Component(ctx =>
        {
            var d = dep;
            ctx.UseEffect(() => { log.Add($"run A{d}"); return () => log.Add($"cleanup A{d}"); }, d);
            ctx.UseEffect(() => { log.Add($"run B{d}"); return () => log.Add($"cleanup B{d}"); }, d);
            ctx.UseEffect(() => { log.Add("run C"); return () => log.Add("cleanup C"); }, "fixed");
            return Text(d.ToString());
        }));
        Assert.Equal(["run A1", "run B1", "run C"], log);
        log.Clear();

        dep = 2;
        host.Render();

        // C's dependency didn't change: neither its cleanup nor its effect runs.
        Assert.Equal(["cleanup A1", "cleanup B1", "run A2", "run B2"], log);
        log.Clear();

        host.Unmount();
        Assert.Equal(["cleanup A2", "cleanup B2", "cleanup C"], log);
    }

    [AvaloniaFact]
    public void UseEffect_of_a_parent_runs_before_its_child_mounts_and_its_cleanup_runs_first()
    {
        // Upstream flushes a component's effects as soon as its render returns, before its output is mounted, and
        // unmounts a component before its children (unlike React, whose child effects run first).
        var log = new List<string>();
        Element Child() => Component(ctx =>
        {
            ctx.UseEffect(() => { log.Add("child effect"); return () => log.Add("child cleanup"); });
            return Text("child");
        });
        var host = new TestHost(() => Component(ctx =>
        {
            ctx.UseEffect(() => { log.Add("parent effect"); return () => log.Add("parent cleanup"); });
            return VStack(Child());
        }));
        Assert.Equal(["parent effect", "child effect"], log);
        log.Clear();

        host.Unmount();

        Assert.Equal(["parent cleanup", "child cleanup"], log);
    }

    [AvaloniaFact]
    public void UseEffect_setting_state_renders_again_with_the_new_value()
    {
        var host = new TestHost(() => Component(ctx =>
        {
            var (loaded, setLoaded) = ctx.UseState("loading");
            ctx.UseEffect(() => setLoaded("ready"));
            return Text(loaded);
        }));
        Assert.Equal(["loading"], host.Texts());
        Assert.True(host.RerenderRequested);

        host.Flush();

        Assert.Equal(["ready"], host.Texts());
    }

    [AvaloniaFact]
    public void UseCallback_returns_the_same_delegate_until_a_dependency_changes()
    {
        var seen = new List<Action>();
        var dep = "x";
        var calls = new List<string>();
        var host = new TestHost(() => Component(ctx =>
        {
            var d = dep;
            var onClick = ctx.UseCallback(() => calls.Add(d), d);
            seen.Add(onClick);
            return Button("go", onClick);
        }));
        host.Render();
        Assert.Same(seen[0], seen[1]);

        dep = "y";
        host.Render();
        Assert.NotSame(seen[1], seen[2]);

        TestHost.Click(host.All<Button>()[0]);
        Assert.Equal(["y"], calls); // the button dispatches to the new callback
    }

    [AvaloniaFact]
    public void UseMemo_with_no_dependencies_computes_once()
    {
        var computed = 0;
        var host = new TestHost(() => Component(ctx =>
        {
            var value = ctx.UseMemo(() => ++computed);
            return Text(value.ToString());
        }));
        host.Render();
        host.Render();

        Assert.Equal(1, computed);
        Assert.Equal(["1"], host.Texts());
    }

    [AvaloniaFact]
    public void UseRef_is_per_component_instance_and_survives_its_state_changes()
    {
        var refs = new Dictionary<string, Ref<int>>();
        Element Row(string key) => Component(ctx =>
        {
            var renders = ctx.UseRef(0);
            renders.Current++;
            refs[key] = renders;
            var (n, setN) = ctx.UseState(0);
            return Button($"{key} {n}", () => setN(n + 1));
        }, key);
        var host = new TestHost(() => VStack(Row("a"), Row("b")));
        var a = refs["a"];

        TestHost.Click(host.All<Button>()[0]);
        host.Flush();

        Assert.Same(a, refs["a"]);
        Assert.NotSame(refs["a"], refs["b"]);
        Assert.Equal(2, refs["a"].Current);
    }

    private static readonly Context<int> Size = new(10);

    [AvaloniaFact]
    public void UseContext_a_provider_inside_a_component_reaches_its_descendants_when_its_state_changes()
    {
        Action? grow = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (size, setSize) = ctx.UseState(12);
            grow = () => setSize(size + 1);
            return VStack(
                Component(c => Text("inner " + c.UseContext(Size))),
                Text("outside")).Provide(Size, size);
        }));
        Assert.Equal(["inner 12", "outside"], host.Texts());

        grow!();
        host.Flush();

        Assert.Equal(["inner 13", "outside"], host.Texts());
    }
}
