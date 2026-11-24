using Avalonia;
using Avalonia.Themes.Fluent;
using Isotope.UI;

namespace Isotope.UI.Samples;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        ReactorApp.Run(BuildAvaloniaApp(), "Isotope samples", SampleApp.Root, args, width: 640, height: 680);

    // The app's Application, platform and theme; ReactorApp.Run adds the window and the root component.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}

/// <summary>The Fluent theme gives the controls their look.</summary>
public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
