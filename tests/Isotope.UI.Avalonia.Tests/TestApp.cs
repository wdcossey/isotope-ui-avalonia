using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Isotope.UI.Avalonia.Tests.TestApp))]

namespace Isotope.UI.Avalonia.Tests;

public sealed class TestApp : Application
{
    // The Fluent theme gives controls in a (headless) window their templates, so rendered frames show real controls.
    public override void Initialize() => Styles.Add(new FluentTheme());

    // Skia rendering (not headless drawing), as in Spike D, so tests can capture rendered frames to PNG.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
