using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Fixture.Lib;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Fixture", width: 480, height: 240);

internal sealed class App : Component
{
    // ScrollView becomes ScrollViewer only after the namespaces are fixed, and the entry's members are the library's.
    public override Element Render() => VStack(
        Widgets.Title("Hello"),
        Widgets.Entry("typed") with { Value = "again" },
        new SliderElement(Value: 3) { Min = 1, Max = 5 },
        Button("Go") with { Label = "Stop" },
        ScrollView(ScrollView(TextBlock("z"))).Margin(4));
}
