using Avalonia.Headless.XUnit;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Isotope.UI.Avalonia.Tests.Dsl;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Memo and class components through the Reconciler's component nodes.</summary>
public class ComponentTests
{
    [AvaloniaFact]
    public void A_memo_component_renders_again_only_when_its_dependencies_change()
    {
        int renders = 0;
        var dep = 1;
        var host = new TestHost(() => VStack(new MemoElement(_ => { renders++; return Text($"dep {dep}"); }, [dep])));
        host.Render();
        Assert.Equal(1, renders);

        dep = 2;
        host.Render();

        Assert.Equal(2, renders);
        Assert.Equal(["dep 2"], host.Texts());
    }

    [AvaloniaFact]
    public void A_memo_component_still_renders_its_own_state_changes()
    {
        Action<int>? set = null;
        var host = new TestHost(() => VStack(new MemoElement(ctx =>
        {
            var (value, setValue) = ctx.UseState(0);
            set = setValue;
            return Text(value.ToString());
        })));

        set!(3);
        host.Flush();

        Assert.Equal(["3"], host.Texts());
    }

    private sealed record GreetingProps(string Name);

    private sealed class Greeting : Component<GreetingProps>
    {
        public static int Renders;

        public override Element Render()
        {
            Renders++;
            var (clicks, setClicks) = UseState(0);
            return VStack(Text($"Hello {Props.Name} ({clicks})"), Button("click", () => setClicks(clicks + 1)));
        }
    }

    [AvaloniaFact]
    public void A_class_component_renders_on_a_props_change_or_its_own_state_and_skips_equal_props()
    {
        Greeting.Renders = 0;
        var name = "Ada";
        var host = new TestHost(() => VStack(new ComponentElement<GreetingProps>(typeof(Greeting), new GreetingProps(name))));
        Assert.Equal(1, Greeting.Renders);

        host.Render(); // equal props (record equality): skipped
        Assert.Equal(1, Greeting.Renders);

        name = "Grace";
        host.Render();
        Assert.Equal(2, Greeting.Renders);
        Assert.Equal("Hello Grace (0)", host.Texts()[0]);

        TestHost.Click(host.All<global::Avalonia.Controls.Button>()[0]);
        host.Flush();
        Assert.Equal(3, Greeting.Renders);
        Assert.Equal("Hello Grace (1)", host.Texts()[0]);
    }

    [AvaloniaFact]
    public void Unmounting_runs_a_component_cleanup_before_its_children()
    {
        var log = new List<string>();
        Element Child(RenderContext ctx)
        {
            ctx.UseEffect(() => () => log.Add("child"), Array.Empty<object>());
            return Text("c");
        }
        var host = new TestHost(() => Component(ctx =>
        {
            ctx.UseEffect(() => () => log.Add("parent"), Array.Empty<object>());
            return VStack(new FuncElement(Child));
        }));

        host.Unmount();

        Assert.Equal(["parent", "child"], log);
    }
}
