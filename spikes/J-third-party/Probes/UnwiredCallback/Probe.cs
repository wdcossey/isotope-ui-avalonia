using Avalonia.Controls;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;

namespace Probes;

/// <summary>A control whose event is a plain EventHandler, as many controls' are (not routed, not EventHandler&lt;T&gt;).</summary>
public class Chip : ContentControl
{
    public event EventHandler? Closed;
    public void Close() => Closed?.Invoke(this, EventArgs.Empty);
}

[GenerateReactorDescriptor(typeof(Chip))]
public partial record ChipElement(Element Content) : Element
{
    public Action? OnClosed { get; init; }

    internal Action<Chip>[] Setters { get; init; } = [];
}
