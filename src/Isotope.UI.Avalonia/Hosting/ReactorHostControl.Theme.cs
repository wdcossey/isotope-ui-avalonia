using Isotope.UI.Core;

namespace Isotope.UI.Hosting;

// The host as a theme resource listener (#41): Theme.NotifyResourcesChanged() (vendored) reaches every live host.
public sealed partial class ReactorHostControl : IThemeResourceListener
{
    /// <summary>
    /// Application resources were edited (<see cref="Theme.NotifyResourcesChanged"/>): renders the tree again from the
    /// root, so a token a component resolves in its render (<c>ThemeRef.Resolve</c>) is resolved again. Tokens bound to
    /// controls (<c>.Background(Theme.CardBackground)</c>) follow resource changes on their own. Unlike upstream's, a
    /// memoized component whose inputs didn't change still skips. Callable from any thread.
    /// </summary>
    void IThemeResourceListener.OnThemeResourcesChanged() => RequestRender();
}
