using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Isotope.UI.Avalonia.Tests.TestApp))]

namespace Isotope.UI.Avalonia.Tests;

public sealed class TestApp : Application
{
    // The Fluent theme gives controls in a (headless) window their templates, so rendered frames show real controls.
    // Tooltips open after hovering for ShowDelay (400 ms by default), and a headless input call runs the due timers first,
    // so on a slow runner a tooltip opened over the next control and took its click (#55). No test opens a tooltip by
    // hovering, so here they wait a minute. TestHost.HoldOffRepeat does the same for a RepeatButton's repeat.
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(x => x.Is<Control>()) { Setters = { new Setter(ToolTip.ShowDelayProperty, 60_000) } });
    }

    // Skia rendering (not headless drawing), as in Spike D, so tests can capture rendered frames to PNG.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
