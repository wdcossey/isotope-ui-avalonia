using Avalonia.Controls;
using Avalonia.Threading;
using Isotope.UI.Core;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// A root host that schedules its renders on Avalonia's dispatcher, as an app host would (the real host is tier 3):
/// a state change asks for one root pass, posted once however many changes come before it runs.
/// </summary>
internal sealed class DispatcherHost
{
    private readonly Func<Element> _render;
    private Element? _last;
    private bool _queued;

    public DispatcherHost(Func<Element> render)
    {
        _render = render;
        Render();
    }

    public Reconciler Reconciler { get; } = new();
    public Border Root { get; } = new();
    public int Renders { get; private set; }

    private void RequestRender()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            Render();
        });
    }

    private void Render()
    {
        var element = _render();
        var next = Reconciler.Reconcile(_last, element, Root.Child, RequestRender);
        if (!ReferenceEquals(next, Root.Child)) Root.Child = next;
        _last = element;
        Renders++;
    }
}
