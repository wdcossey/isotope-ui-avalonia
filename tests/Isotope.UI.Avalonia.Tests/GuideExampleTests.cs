using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Runs the guide's hook examples (GuideExamples), so each snippet in the guide is code that works.</summary>
public class GuideExampleTests
{
    [AvaloniaFact]
    public void UseState_toggle_flips_on_each_click()
    {
        var host = new TestHost(() => Component(GuideExamples.Toggle));
        var button = host.All<Button>()[0];
        Assert.Equal("Off", button.Content);

        TestHost.Click(button);
        host.Flush();
        Assert.Equal("On", button.Content);

        TestHost.Click(button);
        host.Flush();
        Assert.Equal("Off", button.Content);
    }

    [AvaloniaFact]
    public void UseReducer_stepper_steps_up_and_down()
    {
        var host = new TestHost(() => Component(GuideExamples.Stepper));
        var buttons = host.All<Button>();

        TestHost.Click(buttons[1]); // +
        host.Flush();
        TestHost.Click(buttons[1]);
        host.Flush();
        TestHost.Click(buttons[0]); // -
        host.Flush();

        Assert.Equal(["1"], host.Texts());
    }

    [AvaloniaFact]
    public void UseEffect_subscribes_after_mount_follows_a_new_ticker_and_unsubscribes_on_unmount()
    {
        var first = new GuideExamples.Ticker();
        var second = new GuideExamples.Ticker();
        var ticker = first;
        var host = new TestHost(() => Component(ctx => GuideExamples.LastTick(ctx, ticker)));

        first.Tick(3);
        host.Flush();
        Assert.Equal(["Last tick: 3"], host.Texts());

        ticker = second;
        host.Render();
        first.Tick(4); // the old ticker was unsubscribed
        Assert.False(host.RerenderRequested);
        second.Tick(5);
        host.Flush();
        Assert.Equal(["Last tick: 5"], host.Texts());

        host.Unmount();
        second.Tick(6);
        Assert.False(host.RerenderRequested);
    }

    [AvaloniaFact]
    public void UseMemo_search_filters_as_the_query_changes()
    {
        string[] names = ["Ada", "Grace", "Alan"];
        var host = new TestHost(() => Component(ctx => GuideExamples.Search(ctx, names)));
        Assert.Equal(["3 of 3 match"], host.Texts());

        host.All<TextBox>()[0].Text = "a"; // as typing does
        Dispatcher.UIThread.RunJobs();
        host.Flush();
        Assert.Equal(["3 of 3 match"], host.Texts());

        host.All<TextBox>()[0].Text = "al";
        Dispatcher.UIThread.RunJobs();
        host.Flush();
        Assert.Equal(["1 of 3 match"], host.Texts());
    }

    [AvaloniaFact]
    public void UseCallback_save_keeps_its_delegate_until_the_draft_changes()
    {
        var saved = new List<string>();
        Action<string> save = saved.Add; // one delegate: a new one would be a new dependency
        var draft = "a";
        var clicks = new List<Action?>();
        var host = new TestHost(() => Component(ctx =>
        {
            var button = (ButtonElement)GuideExamples.SaveButton(ctx, draft, save);
            clicks.Add(button.OnClick);
            return button;
        }));

        host.Render();
        Assert.Same(clicks[0], clicks[1]);
        draft = "b";
        host.Render();
        Assert.NotSame(clicks[1], clicks[2]);

        TestHost.Click(host.All<Button>()[0]);
        Assert.Equal(["b"], saved);
    }

    [AvaloniaFact]
    public void UseRef_counts_renders_without_asking_for_one()
    {
        var host = new TestHost(() => Component(GuideExamples.RenderCounter));
        var button = host.All<Button>()[0];
        Assert.Equal("Rendered 1 times", button.Content);
        Assert.False(host.RerenderRequested);

        TestHost.Click(button);
        host.Flush();
        Assert.Equal("Rendered 2 times", button.Content);
    }

    [AvaloniaFact]
    public void UseContext_greeters_read_the_default_and_the_provided_value()
    {
        var host = new TestHost(() => Component(GuideExamples.Greeters));
        Assert.Equal(["Hello, world", "Bonjour, world"], host.Texts());
    }
}
