using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Microsoft.Extensions.Logging;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Turns elements into Avalonia controls and keeps them in step across renders. Written for Avalonia; the
/// members upstream's engine files call (<c>Mount</c>, <c>UpdateChildTracked</c>, <c>UnmountChild</c>,
/// <c>CanUpdate</c>, the skip and exit-transition members, component nodes) keep upstream's names and
/// contracts, so <c>ChildReconciler</c> and <c>KeyedListDiff</c> compile and run unmodified.
/// </summary>
/// <remarks>
/// A minimal reconciler: it mounts the elements in <c>Seam/Element.cs</c>, function, memo and class
/// components (each wrapped in a <see cref="Border"/>, as upstream does), and reconciles container children
/// through upstream's <c>ChildReconciler</c>. Exit transitions, ambient animations, flyouts, gestures and
/// hot reload are tier 3: their members answer "none".
/// </remarks>
public sealed partial class Reconciler
{
    internal readonly ILogger? _logger;
    private readonly ContextScope _contextScope = new();
    private readonly Dictionary<UIElement, ComponentNode> _componentNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<ComponentNode> _renderStack = new();

    // Controls still in a collection only while an exit transition plays. Exit transitions are tier 3,
    // so nothing adds to this yet; the members that read it keep upstream's contract.
#pragma warning disable CS0649 // never assigned until exit transitions land
    private HashSet<UIElement>? _exitingControls;
#pragma warning restore CS0649

    // The controls on the path from the root to each self-triggered component, and for each the child
    // (or children) the path continues through. Built at the start of a root pass (see Reconcile).
    private HashSet<UIElement>? _dirtyAncestorPath;
    private Dictionary<UIElement, object>? _dirtyPathChildren;

    /// <summary>Number of child elements skipped as unchanged (diagnostics).</summary>
    public int DebugElementsSkipped;

    public Reconciler(ILogger? logger = null) => _logger = logger;

    /// <summary>The host that renders this reconciler's content (tier 3: no Avalonia host sets it yet).</summary>
    internal IContentOwner? Owner { get; init; }

    // ════════════════════════════════════════════════════════════════════
    //  Root pass
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reconciles a root: mounts <paramref name="newElement"/> when there is no existing control, updates
    /// <paramref name="existingControl"/> in place when it can, and otherwise mounts a replacement and
    /// unmounts the old control. Returns the control that now represents <paramref name="newElement"/>.
    /// </summary>
    public UIElement? Reconcile(Element? oldElement, Element newElement, UIElement? existingControl, Action requestRerender)
    {
        BuildDirtyAncestorPath();
        try
        {
            return ReconcileSlot(oldElement, newElement, existingControl, requestRerender);
        }
        finally
        {
            _dirtyAncestorPath = null;
            _dirtyPathChildren = null;
        }
    }

    private void BuildDirtyAncestorPath()
    {
        foreach (var (control, node) in _componentNodes)
        {
            if (!node.SelfTriggered) continue;
            var path = _dirtyAncestorPath ??= new(ReferenceEqualityComparer.Instance);
            path.Add(control);
            var child = control;
            for (var parent = control.Parent as Control; parent is not null; child = parent, parent = parent.Parent as Control)
            {
                var edges = _dirtyPathChildren ??= new(ReferenceEqualityComparer.Instance);
                AddDirtyPathChild(edges, parent, child);
                if (!path.Add(parent)) break; // the rest of the path is already recorded
            }
        }
    }

    private static void AddDirtyPathChild(Dictionary<UIElement, object> edges, UIElement parent, UIElement child)
    {
        if (!edges.TryGetValue(parent, out var existing))
            edges[parent] = child;
        else if (existing is List<UIElement> list)
        {
            if (!list.Contains(child)) list.Add(child);
        }
        else if (!ReferenceEquals(existing, child))
            edges[parent] = new List<UIElement> { (UIElement)existing, child };
    }

    internal bool IsOnDirtyAncestorPath(UIElement? control) =>
        control is not null && _dirtyAncestorPath is { } set && set.Contains(control);

    /// <summary>
    /// The children of <paramref name="parentControl"/> that lead to a self-triggered component, which a
    /// reconcile of that container must descend into even when their elements are unchanged.
    /// </summary>
    internal DirtyChildIndices ResolveDirtyChildIndices(UIElement? parentControl, IChildCollection children)
    {
        if (_dirtyAncestorPath is not { Count: > 0 } path)
            return default;
        if (parentControl is null)
            return DirtyChildIndices.ProbeEachChild;
        if (!path.Contains(parentControl))
            return default;
        if (_dirtyPathChildren is null || !_dirtyPathChildren.TryGetValue(parentControl, out var recorded))
            return default;
        if (recorded is UIElement single)
        {
            int index = children.IndexOf(single);
            return index >= 0 ? DirtyChildIndices.At(index) : DirtyChildIndices.ProbeEachChild;
        }
        var many = (List<UIElement>)recorded;
        var indices = new int[many.Count];
        for (int k = 0; k < many.Count; k++)
        {
            int index = children.IndexOf(many[k]);
            if (index < 0) return DirtyChildIndices.ProbeEachChild;
            indices[k] = index;
        }
        return DirtyChildIndices.AtAll(indices);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Skip decisions
    // ════════════════════════════════════════════════════════════════════

    /// <summary>True during a hot-reload force pass. Hot reload is tier 3: never.</summary>
    internal bool ForceFullRenderActive => false;

    /// <summary>Whether a force pass must render through this wrapper. Hot reload is tier 3: never.</summary>
    internal bool ForceRenderThroughWrapper(Element el) => false;

    /// <summary>True while any provided context value is in scope.</summary>
    internal bool HasActiveContextValues => _contextScope.HasActiveValues;

    /// <summary>
    /// Whether a structurally unchanged child must still be reconciled because a context it reads may have
    /// changed. Conservative: declines every skip while a context value is in scope (upstream narrows this
    /// to subtrees that consumed a changed context).
    /// </summary>
    internal bool ShouldDeclineSkip(UIElement control) => HasActiveContextValues;

    /// <summary>Flyout decorators are tier 3: none.</summary>
    internal static bool IsFlyoutDecorator(Element element) => false;

    /// <summary>Gesture and drag modifiers are tier 3: none.</summary>
    internal static bool HasGestureOrDragSlots(ElementModifiers? m) => false;

    /// <summary>Whether a skipped child's source stamp changed, so its mounted element must be refreshed.</summary>
    internal static bool CallSiteChangedOnSkip(Element oldEl, Element newEl) => !Equals(oldEl.CallSite, newEl.CallSite);

    /// <summary>
    /// Refreshes a skipped child's mounted element, so event handlers dispatch through the current render's
    /// closures rather than the ones captured when the control was last updated.
    /// </summary>
    internal void RefreshSkippedChildState(FrameworkElement control, Element oldElement, Element newElement)
        => GetOrCreateReactorState(control).Element = newElement;

    internal bool CanUpdate(Element oldEl, Element newEl)
    {
        oldEl = Unwrap(oldEl, out _);
        newEl = Unwrap(newEl, out _);
        if (oldEl.GetType() != newEl.GetType()) return false;
        if (oldEl.Key != newEl.Key) return false;
        if (oldEl is ComponentElement oldComp && newEl is ComponentElement newComp)
            return oldComp.ComponentType == newComp.ComponentType;
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Exit transitions (tier 3: none play, so removal is immediate)
    // ════════════════════════════════════════════════════════════════════

    internal bool HasExitingControls => _exitingControls is { Count: > 0 };

    internal bool IsExiting(UIElement control) => _exitingControls?.Contains(control) == true;

    internal bool HoldsExitingControlIn(IChildCollection children)
    {
        if (_exitingControls is not { Count: > 0 } exiting) return false;
        foreach (var control in exiting)
            if (children.IndexOf(control) >= 0) return true;
        return false;
    }

    /// <summary>Removes the child at <paramref name="index"/> and unmounts it.</summary>
    internal void RemoveChildWithExitTransition(IChildCollection children, int index)
    {
        var control = children.Get(index);
        children.RemoveAt(index);
        UnmountChild(control);
    }

    /// <summary>
    /// Replaces the child at <paramref name="index"/> with <paramref name="newControl"/> and unmounts the old
    /// one. Returns true only when an exit transition re-inserted the old control, which never happens yet.
    /// </summary>
    internal bool ReplaceChildWithExitTransition(IChildCollection children, int index, UIElement newControl)
    {
        var oldControl = children.Get(index);
        UnmountChild(oldControl);
        children.Replace(index, newControl);
        return false;
    }

    /// <summary>Ambient enter animations are tier 3: nothing plays.</summary>
    internal static void ApplyAmbientEnterAnimation(UIElement container, AnimationKind kind) { }

    // ════════════════════════════════════════════════════════════════════
    //  Diagnostics
    // ════════════════════════════════════════════════════════════════════

    private static long s_perfChanges;

    /// <summary>Counts UI changes for the perf provider's tally.</summary>
    internal static void CountPerfChanges(uint count = 1) => Interlocked.Add(ref s_perfChanges, count);

    /// <summary>The name of the component being rendered, for diagnostics; null outside a render.</summary>
    internal string? DiagnosticComponent =>
        _renderStack.Count == 0 ? null : Diagnostics.ComponentNames.For(_renderStack[^1].Component, _renderStack[^1].Element);

    /// <summary>A component's name on the perf spans.</summary>
    internal static string PerfComponentName(ComponentNode node, Element? element)
        => node.PerfName ??= Diagnostics.ComponentNames.For(node.Component, element ?? node.Element);

    // ════════════════════════════════════════════════════════════════════
    //  Per-control state
    // ════════════════════════════════════════════════════════════════════

    /// <summary>What the reconciler records on each control it mounts.</summary>
    internal sealed class ReactorState
    {
        /// <summary>The element the control currently represents.</summary>
        public Element? Element;

        /// <summary>The component diagnostic id mapped to this control (0: none).</summary>
        internal long ComponentDiagnosticId;

        /// <summary>Duplicate-key reports already made for this control.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<long, int>? KeyedDiagnosticLedger;
    }

    private sealed class ReactorAttached : AvaloniaObject
    {
        internal static readonly AttachedProperty<ReactorState?> StateProperty =
            AvaloniaProperty.RegisterAttached<ReactorAttached, Control, ReactorState?>("ReactorState");
    }

    internal static ReactorState GetOrCreateReactorState(UIElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is { } state)
            return state;
        state = new ReactorState();
        fe.SetValue(ReactorAttached.StateProperty, state);
        return state;
    }

    internal static bool TryGetReactorState(FrameworkElement fe, [NotNullWhen(true)] out ReactorState? state)
    {
        state = fe.GetValue(ReactorAttached.StateProperty);
        return state is not null;
    }

    /// <summary>The element <paramref name="control"/> was last mounted or updated with.</summary>
    internal static Element? GetMountedElement(UIElement control)
        => control.GetValue(ReactorAttached.StateProperty)?.Element;

    // ════════════════════════════════════════════════════════════════════
    //  Mount
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Creates the Avalonia control tree for <paramref name="element"/>; null for an empty element.</summary>
    public UIElement? Mount(Element element, Action requestRerender)
    {
        element = Unwrap(element, out var modifiers);
        int pushed = PushContext(element);
        try
        {
            UIElement? control = element switch
            {
                EmptyElement => null,
                FuncElement func => MountFunc(func, requestRerender),
                MemoElement memo => MountMemo(memo, requestRerender),
                ComponentElement comp => MountComponent(comp, requestRerender),
                TextBlockElement text => new TextBlock { Text = text.Content },
                ButtonElement button => MountButton(button),
                StackElement stack => MountStack(stack, requestRerender),
                GroupElement => throw new NotSupportedException(
                    "A GroupElement is flattened by the container factories; it can't be mounted on its own."),
                _ => throw new NotSupportedException(
                    $"{element.GetType().Name} has no Avalonia control yet (the element vocabulary is tier 3)."),
            };
            if (control is not null)
            {
                ApplyModifiers(control, null, modifiers);
                GetOrCreateReactorState(control).Element = element;
            }
            return control;
        }
        finally
        {
            PopContext(pushed);
        }
    }

    private Button MountButton(ButtonElement element)
    {
        var button = new Button { Content = element.Label, IsEnabled = element.IsEnabled };
        // Dispatch through the mounted element, so a skipped update's refreshed closure is the one called.
        button.Click += static (sender, _) =>
        {
            if (sender is Button b && GetMountedElement(b) is ButtonElement { OnClick: { } onClick }) onClick();
        };
        return button;
    }

    private StackPanel MountStack(StackElement element, Action requestRerender)
    {
        var panel = new StackPanel { Orientation = element.Orientation, Spacing = element.Spacing };
        ChildReconciler.Reconcile([], element.Children, new PanelChildCollection(panel), this, requestRerender, panel);
        return panel;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Update
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates <paramref name="control"/> from <paramref name="oldEl"/> to <paramref name="newEl"/> in place and
    /// returns null, or returns a new control when it had to build one (the caller unmounts the old control,
    /// unless <paramref name="unmountedByUpdate"/>, and puts the new one in its slot).
    /// </summary>
    internal UIElement? UpdateChildTracked(Element oldEl, Element newEl, UIElement control, Action requestRerender,
        out bool unmountedByUpdate)
    {
        unmountedByUpdate = false;
        if (!CanUpdate(oldEl, newEl))
            return Mount(newEl, requestRerender);
        var oldInner = Unwrap(oldEl, out var oldModifiers);
        var newInner = Unwrap(newEl, out var newModifiers);
        int pushed = PushContext(newInner);
        try
        {
            if (Element.CanSkipUpdate(oldInner, newInner) && Element.ModifiersEqual(oldModifiers, newModifiers)
                && !IsOnDirtyAncestorPath(control) && !ShouldDeclineSkip(control))
            {
                DebugElementsSkipped++;
                GetOrCreateReactorState(control).Element = newInner;
                return null;
            }
            switch (newInner)
            {
                case FuncElement func:
                    ReconcileFunc(func, (Border)control, requestRerender);
                    break;
                case MemoElement memo:
                    ReconcileMemo(memo, (Border)control, requestRerender);
                    break;
                case ComponentElement comp:
                    ReconcileComponent(comp, (Border)control, requestRerender);
                    break;
                case TextBlockElement text:
                    var tb = (TextBlock)control;
                    if (tb.Text != text.Content) tb.Text = text.Content;
                    break;
                case ButtonElement button:
                    var b = (Button)control;
                    if (!Equals(b.Content, button.Label)) b.Content = button.Label;
                    if (b.IsEnabled != button.IsEnabled) b.IsEnabled = button.IsEnabled;
                    break;
                case StackElement stack:
                    var panel = (StackPanel)control;
                    if (panel.Orientation != stack.Orientation) panel.Orientation = stack.Orientation;
                    if (panel.Spacing != stack.Spacing) panel.Spacing = stack.Spacing;
                    ChildReconciler.Reconcile(((StackElement)oldInner).Children, stack.Children,
                        new PanelChildCollection(panel), this, requestRerender, panel);
                    break;
            }
            ApplyModifiers(control, oldModifiers, newModifiers);
            GetOrCreateReactorState(control).Element = newInner;
            return null;
        }
        finally
        {
            PopContext(pushed);
        }
    }

    /// <summary>
    /// One slot that holds at most one control (a root, a component's output): mount, update, replace or
    /// remove. Returns the control the slot holds afterwards.
    /// </summary>
    private UIElement? ReconcileSlot(Element? oldElement, Element newElement, UIElement? existing, Action requestRerender)
    {
        if (existing is null || oldElement is null)
            return Mount(newElement, requestRerender);
        if (newElement is EmptyElement)
        {
            UnmountChild(existing);
            return null;
        }
        if (CanUpdate(oldElement, newElement))
        {
            var replacement = UpdateChildTracked(oldElement, newElement, existing, requestRerender, out var unmountedByUpdate);
            if (replacement is null) return existing;
            if (!unmountedByUpdate) UnmountChild(existing);
            return replacement;
        }
        var fresh = Mount(newElement, requestRerender);
        UnmountChild(existing);
        return fresh;
    }

    private static Element Unwrap(Element element, out ElementModifiers? modifiers)
    {
        modifiers = element.Modifiers;
        if (element is ModifiedElement mod)
        {
            modifiers = mod.WrappedModifiers;
            if (mod.Inner.Modifiers is not null)
                modifiers = modifiers.Merge(mod.Inner.Modifiers);
            return mod.Inner;
        }
        return element;
    }

    private int PushContext(Element element)
    {
        if (element.ContextValues is not { Count: > 0 } values) return 0;
        _contextScope.Push(values);
        return values.Count;
    }

    private void PopContext(int count)
    {
        if (count > 0) _contextScope.Pop(count);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Modifiers
    // ════════════════════════════════════════════════════════════════════

    private static void ApplyModifiers(UIElement control, ElementModifiers? oldM, ElementModifiers? newM)
    {
        if (oldM is null && newM is null) return;
        Set(control, Layoutable.MarginProperty, oldM?.Margin, newM?.Margin);
        Set(control, Layoutable.WidthProperty, oldM?.Width, newM?.Width);
        Set(control, Layoutable.HeightProperty, oldM?.Height, newM?.Height);
        Set(control, Layoutable.HorizontalAlignmentProperty, oldM?.HorizontalAlignment, newM?.HorizontalAlignment);
        Set(control, Layoutable.VerticalAlignmentProperty, oldM?.VerticalAlignment, newM?.VerticalAlignment);
        Set(control, Visual.OpacityProperty, oldM?.Opacity, newM?.Opacity);
        Set(control, InputElement.IsEnabledProperty, oldM?.IsEnabled, newM?.IsEnabled);
        if (BackgroundProperty(control) is { } background)
            SetRef(control, background, oldM?.Background, newM?.Background);
        SetRef(control, AutomationProperties.NameProperty, oldM?.AutomationName, newM?.AutomationName);
    }

    private static AvaloniaProperty<Avalonia.Media.IBrush?>? BackgroundProperty(UIElement control) => control switch
    {
        Avalonia.Controls.Panel => Avalonia.Controls.Panel.BackgroundProperty,
        Border => Border.BackgroundProperty,
        TemplatedControl => TemplatedControl.BackgroundProperty,
        TextBlock => TextBlock.BackgroundProperty,
        _ => null,
    };

    // Writes a modifier that is set, and clears one that is no longer set.
    private static void Set<T>(UIElement control, AvaloniaProperty<T> property, T? oldValue, T? newValue) where T : struct
    {
        if (newValue is { } v)
        {
            if (!Equals(oldValue, newValue)) control.SetValue(property, v);
        }
        else if (oldValue is not null)
            control.ClearValue(property);
    }

    private static void SetRef<T>(UIElement control, AvaloniaProperty<T> property, T? oldValue, T? newValue) where T : class?
    {
        if (newValue is not null)
        {
            if (!Equals(oldValue, newValue)) control.SetValue(property, newValue);
        }
        else if (oldValue is not null)
            control.ClearValue(property);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Components
    // ════════════════════════════════════════════════════════════════════

    internal sealed class ComponentNode
    {
        /// <summary>The class component instance (null for function components).</summary>
        public Component? Component { get; set; }
        /// <summary>The render context of a function or memo component (null for class components).</summary>
        public RenderContext? Context { get; set; }
        /// <summary>The element the last render returned.</summary>
        public Element? RenderedElement { get; set; }
        /// <summary>The component, function or memo element that created this node.</summary>
        public Element? Element { get; set; }
        /// <summary>Props at the last render (class components).</summary>
        public object? PreviousProps { get; set; }
        /// <summary>Dependencies at the last memo render.</summary>
        public object?[]? MemoDependencies { get; set; }
        private volatile bool _selfTriggered;
        /// <summary>Set when the component's own state changed and a re-render is queued.</summary>
        public bool SelfTriggered { get => _selfTriggered; set => _selfTriggered = value; }
        public long DiagnosticId => (Component?.Context ?? Context)?.DiagnosticId ?? 0;
        public long EnsureDiagnosticId() => (Component?.Context ?? Context)?.EnsureDiagnosticId() ?? 0;
        /// <summary>The component's name on the perf spans, once resolved.</summary>
        public string? PerfName { get; set; }
    }

    /// <summary>Tries to find the component node a wrapper control belongs to.</summary>
    internal bool TryFindComponentNode(UIElement control, [NotNullWhen(true)] out ComponentNode? node)
        => _componentNodes.TryGetValue(control, out node);

    // A state change marks the node and asks the parent's rerender, so every component above it is on
    // the next root pass's dirty-ancestor path.
    private static Action CreateComponentRerender(ComponentNode node, Action requestRerender) => () =>
    {
        node.SelfTriggered = true;
        requestRerender();
    };

    private Element Render(ComponentNode node, RenderContext ctx, Action componentRerender, Func<Element> render)
    {
        _renderStack.Add(node);
        try
        {
            ctx.BeginRender(componentRerender, _contextScope, Owner);
            var element = render() ?? EmptyElement.Instance;
            ctx.FlushEffects();
            return element;
        }
        finally
        {
            _renderStack.RemoveAt(_renderStack.Count - 1);
        }
    }

    private Border MountNode(ComponentNode node, RenderContext ctx, Action requestRerender, Func<Element> render)
    {
        var wrapper = new Border();
        _componentNodes[wrapper] = node;
        var componentRerender = CreateComponentRerender(node, requestRerender);
        var child = Render(node, ctx, componentRerender, render);
        wrapper.Child = Mount(child, componentRerender);
        node.RenderedElement = child;
        return wrapper;
    }

    private UIElement MountFunc(FuncElement func, Action requestRerender)
    {
        var ctx = new RenderContext();
        var node = new ComponentNode { Context = ctx, Element = func };
        return MountNode(node, ctx, requestRerender, () => func.RenderFunc(ctx));
    }

    private UIElement MountMemo(MemoElement memo, Action requestRerender)
    {
        var ctx = new RenderContext();
        var node = new ComponentNode { Context = ctx, Element = memo, MemoDependencies = memo.Dependencies };
        return MountNode(node, ctx, requestRerender, () => memo.RenderFunc(ctx));
    }

    private UIElement MountComponent(ComponentElement comp, Action requestRerender)
    {
        var component = comp.CreateInstance();
        if (component is IPropsReceiver receiver && comp.Props is not null)
            receiver.SetProps(comp.Props);
        var node = new ComponentNode { Component = component, Element = comp, PreviousProps = comp.Props };
        return MountNode(node, component.Context, requestRerender, component.Render);
    }

    private void RerenderNode(ComponentNode node, RenderContext ctx, Border wrapper, Action requestRerender, Func<Element> render)
    {
        var componentRerender = CreateComponentRerender(node, requestRerender);
        var child = Render(node, ctx, componentRerender, render);
        var existing = wrapper.Child;
        var next = ReconcileSlot(node.RenderedElement, child, existing, componentRerender);
        if (!ReferenceEquals(next, existing)) wrapper.Child = next;
        node.RenderedElement = child;
    }

    // The node's previous output, walked against itself: reaches a self-triggered component further down
    // without re-running this one.
    private void DescendOnly(ComponentNode node, Border wrapper, Action requestRerender)
    {
        if (node.RenderedElement is { } rendered && wrapper.Child is { } child && IsOnDirtyAncestorPath(wrapper))
            ReconcileSlot(rendered, rendered, child, CreateComponentRerender(node, requestRerender));
    }

    private void ReconcileFunc(FuncElement func, Border wrapper, Action requestRerender)
    {
        var node = _componentNodes[wrapper];
        node.SelfTriggered = false;
        node.Element = func;
        var ctx = node.Context!;
        RerenderNode(node, ctx, wrapper, requestRerender, () => func.RenderFunc(ctx));
    }

    private void ReconcileMemo(MemoElement memo, Border wrapper, Action requestRerender)
    {
        var node = _componentNodes[wrapper];
        bool selfTriggered = node.SelfTriggered;
        node.SelfTriggered = false;
        node.Element = memo;
        var oldDeps = node.MemoDependencies;
        var newDeps = memo.Dependencies;
        bool depsChanged = oldDeps is null && newDeps is null
            ? false // both null: render once, never re-render from the parent
            : oldDeps is null || newDeps is null || !RenderContext.DepsEqual(oldDeps, newDeps);
        if (!selfTriggered && !depsChanged)
        {
            DescendOnly(node, wrapper, requestRerender);
            return;
        }
        node.MemoDependencies = newDeps;
        var ctx = node.Context!;
        RerenderNode(node, ctx, wrapper, requestRerender, () => memo.RenderFunc(ctx));
    }

    private void ReconcileComponent(ComponentElement comp, Border wrapper, Action requestRerender)
    {
        var node = _componentNodes[wrapper];
        bool selfTriggered = node.SelfTriggered;
        node.SelfTriggered = false;
        node.Element = comp;
        var component = node.Component!;
        if (!selfTriggered)
        {
            bool propsChanged = component is IPropsComparable comparable
                ? comparable.CompareProps(node.PreviousProps, comp.Props)
                : component.ShouldUpdate();
            if (!propsChanged)
            {
                // Refresh live props so handlers that read Props at dispatch time see the current ones.
                if (component is IPropsReceiver skipReceiver && comp.Props is not null)
                {
                    skipReceiver.SetProps(comp.Props);
                    node.PreviousProps = comp.Props;
                }
                DescendOnly(node, wrapper, requestRerender);
                return;
            }
        }
        if (component is IPropsReceiver receiver && comp.Props is not null)
            receiver.SetProps(comp.Props);
        node.PreviousProps = comp.Props;
        RerenderNode(node, component.Context, wrapper, requestRerender, component.Render);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Unmount
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tears down the subtree under <paramref name="control"/>: runs effect cleanups (a component before its
    /// children, as upstream does) and forgets the component nodes. Does not detach it from its parent.
    /// </summary>
    public void UnmountChild(UIElement control)
    {
        CountPerfChanges();
        _exitingControls?.Remove(control);
        if (_componentNodes.Remove(control, out var node))
        {
            node.Component?.Context.RunCleanups();
            node.Context?.RunCleanups();
        }
        switch (control)
        {
            case Avalonia.Controls.Panel panel:
                foreach (var child in panel.Children.ToArray()) UnmountChild(child);
                break;
            case Decorator { Child: { } child }:
                UnmountChild(child);
                break;
            case ContentControl { Content: Control content }:
                UnmountChild(content);
                break;
        }
    }
}
