using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Isotope.UI.Avalonia.Tests.TestApp))]

namespace Isotope.UI.Avalonia.Tests;

public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
