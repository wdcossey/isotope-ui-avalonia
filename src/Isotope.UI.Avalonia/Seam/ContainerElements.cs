using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Isotope.UI.Core.V1Protocol;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The content containers (#19): Border, ScrollViewer and Expander, each holding one child element, and the WrapPanel.
// Records mirror upstream's (Reactor's member names, Avalonia's value types), except WrapPanel, which upstream doesn't
// have (Avalonia's names). Descriptors are generated: the child is the control's single content slot, reconciled in
// place across renders, so a child component keeps its state.

/// <summary>A single child with a background, border and rounded corners, as an Avalonia <see cref="Avalonia.Controls.Border"/>.</summary>
[GenerateReactorDescriptor(typeof(Border), ClearValueOnUnset = true)]
[WrapContent("Child")]
[WrapManual("Background")]
[WrapManual("CornerRadius")]
[WrapManual("BorderThickness")]
public partial record BorderElement(Element? Child) : Element
{
    /// <summary>The same radius on every corner, in DIPs.</summary>
    public double? CornerRadius { get; init; }

    /// <summary>
    /// The background. It has a second source, the <c>.Background(...)</c> modifier: the reconciler writes the two as
    /// one value, this one first, so neither overwrites the other (upstream folds them in Border's descriptor).
    /// </summary>
    public IBrush? Background { get; init; }

    public IBrush? BorderBrush { get; init; }

    /// <summary>The same border width on every side, in DIPs.</summary>
    public double? BorderThickness { get; init; }

    /// <summary>Space between the border and the child (Avalonia's <c>Padding</c>; upstream's record has none).</summary>
    public Thickness? Padding { get; init; }

    internal Action<Border>[] Setters { get; init; } = [];

    // Background is written by the reconciler with the modifier's (Reconciler.ApplyModifiers). BorderBrush and Padding
    // are generated, on the ClearValue channel. CornerRadius and BorderThickness are written here, on the same channel,
    // so a member set back to null gives the control its default again: upstream's [WrapConvert] (a scalar written
    // through the struct's constructor) skips the write for null instead of clearing.
    private static partial ControlDescriptor<BorderElement, Border> Customize(ControlDescriptor<BorderElement, Border> d)
        => d.OneWay<CornerRadius>(
                get: static e => e.CornerRadius is { } r ? new CornerRadius(r) : Optional<CornerRadius>.Unset,
                set: static (c, v) => c.CornerRadius = v,
                dp:  Border.CornerRadiusProperty)
            .OneWay<Thickness>(
                get: static e => e.BorderThickness is { } t ? new Thickness(t) : Optional<Thickness>.Unset,
                set: static (c, v) => c.BorderThickness = v,
                dp:  Border.BorderThicknessProperty);
}

/// <summary>
/// A scrollable view of one child, as an Avalonia <see cref="Avalonia.Controls.ScrollViewer"/>. Both scroll bars default
/// to <see cref="ScrollBarVisibility.Auto"/>, as upstream's do (Avalonia's own horizontal default is Disabled).
/// </summary>
[GenerateReactorDescriptor(typeof(ScrollViewer))]
public partial record ScrollViewerElement(Element Child) : Element
{
    public ScrollBarVisibility HorizontalScrollBarVisibility { get; init; } = ScrollBarVisibility.Auto;
    public ScrollBarVisibility VerticalScrollBarVisibility { get; init; } = ScrollBarVisibility.Auto;

    internal Action<ScrollViewer>[] Setters { get; init; } = [];
}

/// <summary>
/// A header that shows or hides its content, as an Avalonia <see cref="Avalonia.Controls.Expander"/>.
/// <see cref="IsExpanded"/> defaults to <see cref="Optional{T}.Unset"/>, so the control owns its state until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(Expander))]
[WrapManual("Content")]
[WrapManual("Header")]
[WrapManual("HeaderContent")]
[WrapManual("IsExpanded")]
public partial record ExpanderElement(
    string Header,
    Element Content,
    Optional<bool> IsExpanded = default,
    Action<bool>? OnIsExpandedChanged = null) : Element
{
    public ExpandDirection ExpandDirection { get; init; } = ExpandDirection.Down;

    /// <summary>
    /// An element as the header, taking precedence over the string <see cref="Header"/> (upstream's member, #26): a
    /// mounted child, reconciled in place across renders.
    /// </summary>
    public Element? HeaderContent { get; init; }

    internal Action<Expander>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnIsExpandedChanged is not null;

    // As upstream's: Content is the single content slot, Header a string, and IsExpanded controlled. Unlike upstream's,
    // the controlled entry listens to IsExpanded's own change: Avalonia raises Expanding and Collapsing before the
    // change (either can be cancelled) and Expanded and Collapsed after a content transition, posted, where WinUI
    // raises Expanding and Collapsed with the change. The property change is raised inside the write, where upstream's
    // Controlled entry, which arms the value it writes, drops it as the echo of the engine's own write.
    private static partial ControlDescriptor<ExpanderElement, Expander> Customize(ControlDescriptor<ExpanderElement, Expander> d)
    {
        d.Children = new SingleContent<ExpanderElement, Expander>(
            GetChild: static e => e.Content,
            SetChild: static (c, ui) => c.Content = ui)
        {
            GetCurrentChild = static c => c.Content as Control,
        };
        // HeaderContent, as upstream's: a secondary element slot on the imperative bridge (the control's one children
        // strategy is the content), and the string header is written only without it.
        return d
            .ImperativeBridged(
                mount: static (ctx, c, e) =>
                {
                    if (e.HeaderContent is null) return;
                    if (ctx.MountChild(e.HeaderContent) is { } mounted) c.Header = mounted;
                },
                update: static (ctx, c, oldEl, newEl) =>
                {
                    if (oldEl.HeaderContent is null && newEl.HeaderContent is null) return;
                    var existing = c.Header as Control;
                    var next = ctx.Reconciler.ReconcileV1Child(oldEl.HeaderContent, newEl.HeaderContent, existing, ctx.RequestRerender);
                    if (!ReferenceEquals(existing, next)) c.Header = next;
                },
                unmount: static (ctx, c) =>
                {
                    if (Reconciler.GetElementTag(c) is not ExpanderElement { HeaderContent: not null }) return;
                    if (c.Header is Control child) ctx.Reconciler.UnmountChild(child);
                    c.Header = null;
                })
            .OneWayConditional(
                get:         static e => e.Header,
                set:         static (c, v) => c.Header = v,
                shouldWrite: static e => e.HeaderContent is null)
            .Controlled<bool, AvaloniaPropertyChangedEventArgs>(
                get:         static e => e.IsExpanded,
                set:         static (c, v) => c.IsExpanded = v,
                subscribe:   static (fe, h) => fe.PropertyChanged += (s, a) =>
                {
                    if (a.Property == Expander.IsExpandedProperty) h(s, a);
                },
                unsubscribe: static (fe, h) => { },
                callback:    static e => e.OnIsExpandedChanged,
                readBack:    static c => c.IsExpanded);
    }
}

/// <summary>
/// Children laid out left to right (or top to bottom) and wrapped onto new lines, as an Avalonia
/// <see cref="Avalonia.Controls.WrapPanel"/>. Avalonia-only (upstream has no WrapPanel), so Avalonia's member names.
/// </summary>
[GenerateReactorDescriptor(typeof(WrapPanel))]
public partial record WrapPanelElement(Element[] Children) : Element
{
    private readonly Element[] _children = Reconciler.Flatten(Children);

    /// <summary>The children, with any <see cref="GroupElement"/> flattened into them.</summary>
    public Element[] Children
    {
        get => _children;
        init => _children = Reconciler.Flatten(value);
    }

    public Orientation Orientation { get; init; } = Orientation.Horizontal;

    /// <summary>Every item's width, in DIPs; NaN (the default) sizes each to its content.</summary>
    public double ItemWidth { get; init; } = double.NaN;

    /// <summary>Every item's height, in DIPs; NaN (the default) sizes each to its content.</summary>
    public double ItemHeight { get; init; } = double.NaN;

    /// <summary>Space between items on a line, in DIPs.</summary>
    public double ItemSpacing { get; init; }

    /// <summary>Space between lines, in DIPs.</summary>
    public double LineSpacing { get; init; }

    public WrapPanelItemsAlignment ItemsAlignment { get; init; } = WrapPanelItemsAlignment.Start;

    internal Action<WrapPanel>[] Setters { get; init; } = [];
}
