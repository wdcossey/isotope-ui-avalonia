using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Vendor.Controls.Tests.TestApp))]
// The hosts share Avalonia's one UI thread and Isotope's process-wide registry and dispatcher: one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Vendor.Controls.Tests;

/// <summary>The Fluent theme (its resources are what the theme tokens resolve to), headless drawing.</summary>
public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
