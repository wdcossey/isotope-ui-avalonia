using Avalonia.Controls;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;

namespace Probes;

public class Chip : ContentControl
{
    public event EventHandler? Closed;
    public void Close() => Closed?.Invoke(this, EventArgs.Empty);
}

// [WrapEvent] passes the whole args (Action<EventArgs>); the record declares Action, so the generated trampoline
// doesn't compile.
[GenerateReactorDescriptor(typeof(Chip))]
[WrapEvent("Closed")]
public partial record ChipElement(Element Content) : Element
{
    public Action? OnClosed { get; init; }

    internal Action<Chip>[] Setters { get; init; } = [];
}
