using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Isotope.UI.Avalonia.Tests.TestApp))]
// One test at a time (#105). Avalonia's statics expect one thread: a property's id comes from an unlocked counter and
// the property registry is a set of plain dictionaries, and Dispatcher.UIThread binds to the first thread that asks
// while a headless test sets up its application. Plain [Fact]s touch them off the session's thread: making an element
// record builds its descriptor, which registers its control's properties (here and in upstream's vendored tests), and
// a test that makes a control gets a dispatcher of its own. Run beside the session, those gave two properties one id
// (an InvalidCastException in Button.OnPropertyChanged, then failures in unrelated tests) and a setup that failed
// Dispatcher.VerifyAccess. The session runs every headless test on its one thread anyway: only the plain tests lose
// their overlap with it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
