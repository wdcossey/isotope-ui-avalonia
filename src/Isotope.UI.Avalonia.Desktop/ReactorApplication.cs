using Avalonia;
using Avalonia.Themes.Fluent;

namespace Isotope.UI;

/// <summary>
/// The default app builder's <see cref="Application"/> (<see cref="ReactorAppDesktop.CreateDefaultAppBuilder"/>), named
/// after upstream's: it gives the controls their look with Avalonia's Fluent theme, following the system's light or
/// dark setting. An app that wants more (its own resources, another theme) builds its own <see cref="AppBuilder"/>
/// and passes it to <c>ReactorApp.Run(builder, …)</c>.
/// </summary>
public class ReactorApplication : Application
{
    /// <summary>Adds the Fluent theme.</summary>
    public override void Initialize() => Styles.Add(new FluentTheme());
}
