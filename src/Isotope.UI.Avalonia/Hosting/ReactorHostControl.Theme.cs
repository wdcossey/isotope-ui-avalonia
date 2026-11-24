using Isotope.UI.Core;

namespace Isotope.UI.Hosting;

// The host as a theme resource listener (#41): Theme.NotifyResourcesChanged() (vendored) reaches every live host.
public sealed partial class ReactorHostControl : IThemeResourceListener
{
    /// <summary>
    /// Application resources were edited (<see cref="Theme.NotifyResourcesChanged"/>): as upstream's host, the next pass
    /// renders the whole tree, past memoized components, so a token a component resolves in its render
    /// (<c>ThemeRef.Resolve</c>) is resolved again. Tokens bound to controls (<c>.Background(Theme.CardBackground)</c>)
    /// follow resource changes on their own. Callable from any thread.
    /// </summary>
    void IThemeResourceListener.OnThemeResourcesChanged()
    {
        if (_disposed) return;
        _reconciler.RequestResourceRefresh();
        RequestRender();
    }
}
