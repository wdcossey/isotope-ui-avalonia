// Tier 3: upstream's src/Reactor/Yoga/FlexPanel.cs (microsoft-ui-reactor, unchanged from 28a87225 to 14172dce), ported
// from WinUI 3 to Avalonia 12 in Spike D (spikes/D-flex keeps the spike) and brought into the library in #2, over the
// vendored Yoga files (src/Upstream/src/Reactor/Yoga, tier 1). tools/sync lists the upstream PRs that touch the original.
// The layout logic (two-pass measure, arrange from cached Yoga results, MeasureFunction bridge, CSS §4.5
// automatic minimum size) is kept line-for-line where the platforms agree; the differences are:
//   • StyledProperty/AttachedProperty instead of DependencyProperty; AffectsMeasure + Changed handlers
//     instead of PropertyMetadata callbacks.
//   • Visibility.Collapsed → !IsVisible; XamlRoot.RasterizationScale → TopLevel.RenderScaling;
//     Unloaded → OnDetachedFromVisualTree; no ScrollView (only ScrollViewer).
//   • Min-content (ComputeMinContent): Avalonia clamps DesiredSize to the size offered, so upstream's measure-at-0
//     probe reads 0; content that can't get narrower is measured unconstrained instead (#15).
//   • A nested panel and the outer Yoga height mode (#9, an intended divergence): a nested FlexPanel fills its slot or
//     fits its content depending on upstream's [ThreadStatic] mode, but Avalonia skips a Measure whose constraint is
//     unchanged, and re-measures an invalidated panel on its own at its old constraint, with no mode set. As ported, a
//     panel then kept a size from the other mode (a fill height under a fit-content outer). Here each panel
//     records the mode its last measure used and which outer panel measured it. Before the outer measures a child (in
//     its Yoga bridge, final sweep or min-content probe), a nested panel in it whose cached result came from the other
//     mode is invalidated; a panel re-measured on its own keeps its last mode. The modes themselves are upstream's:
//     the probe still runs under the outer's mode (issue 1 of #9), and here its result can't depend on it.
// Avalonia's Measure/DesiredSize/Arrange include the child's Margin exactly as WinUI's do, so the margin
// arithmetic is unchanged.

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
namespace Isotope.UI.Layout;

/// <summary>A panel that implements CSS Flexbox using upstream Reactor's Yoga port.</summary>
public class FlexPanel : Panel
{
    private readonly YogaConfig _yogaConfig = new();
    private readonly Dictionary<Control, YogaNode> _nodeCache = new();
    private readonly YogaNode _rootNode;
    private readonly HashSet<Control> _syncCurrentChildren = new();
    private readonly List<Control> _syncToRemove = new();
    private readonly Dictionary<Control, AttachedProps> _attachedCache = new();
    private static readonly ConditionalWeakTable<Control, object> s_detachedAttachedDirty = new();
    private static readonly object s_detachedDirtyMarker = new();

    private struct AttachedProps
    {
        public double Grow, Shrink, Basis, MinWidth, MinHeight, Left, Top, Right, Bottom;
        public FlexAlign AlignSelf;
        public FlexPositionType Position;
    }

    public FlexPanel() => _rootNode = new YogaNode(_yogaConfig);

    // ── Container properties ──
    public static readonly StyledProperty<FlexDirection> DirectionProperty =
        AvaloniaProperty.Register<FlexPanel, FlexDirection>(nameof(Direction), FlexDirection.Row);
    public static readonly StyledProperty<FlexJustify> JustifyContentProperty =
        AvaloniaProperty.Register<FlexPanel, FlexJustify>(nameof(JustifyContent), FlexJustify.FlexStart);
    public static readonly StyledProperty<FlexAlign> AlignItemsProperty =
        AvaloniaProperty.Register<FlexPanel, FlexAlign>(nameof(AlignItems), FlexAlign.Stretch);
    public static readonly StyledProperty<FlexAlign> AlignContentProperty =
        AvaloniaProperty.Register<FlexPanel, FlexAlign>(nameof(AlignContent), FlexAlign.FlexStart);
    public static readonly StyledProperty<FlexWrap> WrapProperty =
        AvaloniaProperty.Register<FlexPanel, FlexWrap>(nameof(Wrap), FlexWrap.NoWrap);
    public static readonly StyledProperty<FlexLayoutDirection> LayoutDirectionProperty =
        AvaloniaProperty.Register<FlexPanel, FlexLayoutDirection>(nameof(LayoutDirection), FlexLayoutDirection.LTR);
    public static readonly StyledProperty<double> ColumnGapProperty =
        AvaloniaProperty.Register<FlexPanel, double>(nameof(ColumnGap), 0.0);
    public static readonly StyledProperty<double> RowGapProperty =
        AvaloniaProperty.Register<FlexPanel, double>(nameof(RowGap), 0.0);
    public static readonly StyledProperty<Thickness> FlexPaddingProperty =
        AvaloniaProperty.Register<FlexPanel, Thickness>(nameof(FlexPadding));

    public FlexDirection Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public FlexJustify JustifyContent { get => GetValue(JustifyContentProperty); set => SetValue(JustifyContentProperty, value); }
    public FlexAlign AlignItems { get => GetValue(AlignItemsProperty); set => SetValue(AlignItemsProperty, value); }
    public FlexAlign AlignContent { get => GetValue(AlignContentProperty); set => SetValue(AlignContentProperty, value); }
    public FlexWrap Wrap { get => GetValue(WrapProperty); set => SetValue(WrapProperty, value); }
    public FlexLayoutDirection LayoutDirection { get => GetValue(LayoutDirectionProperty); set => SetValue(LayoutDirectionProperty, value); }
    public double ColumnGap { get => GetValue(ColumnGapProperty); set => SetValue(ColumnGapProperty, value); }
    public double RowGap { get => GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }
    public Thickness FlexPadding { get => GetValue(FlexPaddingProperty); set => SetValue(FlexPaddingProperty, value); }

    // ── Attached properties (for children) ──
    public static readonly AttachedProperty<double> GrowProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Grow", 0.0);
    public static readonly AttachedProperty<double> ShrinkProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Shrink", 1.0);
    public static readonly AttachedProperty<double> BasisProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Basis", double.NaN);
    public static readonly AttachedProperty<double> FlexMinWidthProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("FlexMinWidth", double.NaN);
    public static readonly AttachedProperty<double> FlexMinHeightProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("FlexMinHeight", double.NaN);
    public static readonly AttachedProperty<FlexAlign> AlignSelfProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, FlexAlign>("AlignSelf", FlexAlign.Auto);
    public static readonly AttachedProperty<FlexPositionType> PositionProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, FlexPositionType>("Position", FlexPositionType.Relative);
    public static readonly AttachedProperty<double> LeftProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Left", double.NaN);
    public static readonly AttachedProperty<double> TopProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Top", double.NaN);
    public static readonly AttachedProperty<double> RightProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Right", double.NaN);
    public static readonly AttachedProperty<double> BottomProperty = AvaloniaProperty.RegisterAttached<FlexPanel, Control, double>("Bottom", double.NaN);

    public static void SetGrow(Control el, double value) => el.SetValue(GrowProperty, value);
    public static double GetGrow(Control el) => el.GetValue(GrowProperty);
    public static void SetShrink(Control el, double value) => el.SetValue(ShrinkProperty, value);
    public static double GetShrink(Control el) => el.GetValue(ShrinkProperty);
    public static void SetBasis(Control el, double value) => el.SetValue(BasisProperty, value);
    public static double GetBasis(Control el) => el.GetValue(BasisProperty);
    // Named after the registered properties so XAML resolves FlexPanel.FlexMinWidth (upstream calls them
    // SetMinWidth/GetMinWidth, which in Avalonia XAML would hit Layoutable.MinWidth instead).
    public static void SetFlexMinWidth(Control el, double value) => el.SetValue(FlexMinWidthProperty, value);
    public static double GetFlexMinWidth(Control el) => el.GetValue(FlexMinWidthProperty);
    public static void SetFlexMinHeight(Control el, double value) => el.SetValue(FlexMinHeightProperty, value);
    public static double GetFlexMinHeight(Control el) => el.GetValue(FlexMinHeightProperty);
    public static void SetAlignSelf(Control el, FlexAlign value) => el.SetValue(AlignSelfProperty, value);
    public static FlexAlign GetAlignSelf(Control el) => el.GetValue(AlignSelfProperty);
    public static void SetPosition(Control el, FlexPositionType value) => el.SetValue(PositionProperty, value);
    public static FlexPositionType GetPosition(Control el) => el.GetValue(PositionProperty);
    public static void SetLeft(Control el, double value) => el.SetValue(LeftProperty, value);
    public static double GetLeft(Control el) => el.GetValue(LeftProperty);
    public static void SetTop(Control el, double value) => el.SetValue(TopProperty, value);
    public static double GetTop(Control el) => el.GetValue(TopProperty);
    public static void SetRight(Control el, double value) => el.SetValue(RightProperty, value);
    public static double GetRight(Control el) => el.GetValue(RightProperty);
    public static void SetBottom(Control el, double value) => el.SetValue(BottomProperty, value);
    public static double GetBottom(Control el) => el.GetValue(BottomProperty);

    static FlexPanel()
    {
        // A child's own size, margin or visibility feeds its Yoga node. WinUI re-measures the parent when these
        // change; in Avalonia the child is re-measured at its old (exact) slot, so its DesiredSize can stay the
        // same and the panel never hears about it unless it registers for them.
        AffectsParentMeasure<FlexPanel>(WidthProperty, HeightProperty, MinWidthProperty, MinHeightProperty,
            MaxWidthProperty, MaxHeightProperty, MarginProperty, IsVisibleProperty);
        AffectsMeasure<FlexPanel>(DirectionProperty, JustifyContentProperty, AlignItemsProperty, AlignContentProperty,
            WrapProperty, LayoutDirectionProperty, ColumnGapProperty, RowGapProperty, FlexPaddingProperty);
        foreach (var p in new AvaloniaProperty[] { GrowProperty, ShrinkProperty, BasisProperty, FlexMinWidthProperty, FlexMinHeightProperty,
                     AlignSelfProperty, PositionProperty, LeftProperty, TopProperty, RightProperty, BottomProperty })
            p.Changed.Subscribe(new ChildPropertyObserver());
    }

    private sealed class ChildPropertyObserver : IObserver<AvaloniaPropertyChangedEventArgs>
    {
        public void OnNext(AvaloniaPropertyChangedEventArgs e) { if (e.Sender is Control c) OnChildPropertyChanged(c); }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private static void OnChildPropertyChanged(Control el)
    {
        // Visual parent, as upstream (VisualTreeHelper.GetParent): an ItemsPanel's children have the
        // ItemsControl as logical parent.
        if (el.GetVisualParent() is FlexPanel panel)
        {
            panel._attachedCache.Remove(el);
            panel.InvalidateMeasure();
        }
        else
        {
            s_detachedAttachedDirty.AddOrUpdate(el, s_detachedDirtyMarker);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var node in _nodeCache.Values)
            _rootNode.RemoveChild(node);
        _nodeCache.Clear();
        _attachedCache.Clear();
        _ambientDependents.Clear();
        _ambientPanel = null;
        _ambientSlot = null;
    }

    // ── The outer Yoga height mode and Avalonia's measure cache (#9, not upstream's; see the header) ──

    // The mode this measure runs under, recorded with the outer panel that measures it. Measured with no outer panel
    // while it still has one (Avalonia's layout manager re-measuring this panel, or a wrapper, on its own at the old
    // constraint): the mode that outer panel gave last.
    private YogaMeasureMode? TrackAmbientMode()
    {
        if (s_outerPanel is { } outer)
        {
            (_ambientPanel, _ambientSlot, _ambientMode) = (outer, s_outerSlot, _outerYogaHeightMode);
            if (s_outerSlot is { } slot && slot != this) outer.AddAmbientDependent(slot, this);
            return _outerYogaHeightMode;
        }
        return _ambientPanel is not null ? _ambientMode : null;
    }

    // Upstream's outerFlexWantsContent: a basis or fit-content measure (Undefined, AtMost) reports content, not the cap.
    private static bool WantsContent(YogaMeasureMode? mode) => mode is YogaMeasureMode.Undefined or YogaMeasureMode.AtMost;

    private void AddAmbientDependent(Control slot, FlexPanel nested)
    {
        if (!_ambientDependents.TryGetValue(slot, out var set)) _ambientDependents[slot] = set = new HashSet<FlexPanel>();
        set.Add(nested);
    }

    // True when a Measure at an unchanged constraint would return a result computed in the other mode, or skip it.
    private bool IsStaleFor(bool wantsContent) => !IsMeasureValid || (_ambientSensitive && _ambientWantsContent != wantsContent);

    // Before the Yoga bridge measures a child: a nested panel in it (the child itself, or one inside it that this panel
    // measured last) whose cached result came from the other mode is invalidated, with the path up to the child, so
    // Avalonia measures it again even at the same constraint.
    private void DropStaleNestedMeasures(Control slot, bool wantsContent)
    {
        if (slot is FlexPanel direct)
        {
            if (direct.IsStaleFor(wantsContent)) direct.InvalidateMeasure();
            return;
        }
        if (!_ambientDependents.TryGetValue(slot, out var nested)) return;
        List<FlexPanel>? gone = null;
        foreach (var d in nested)
        {
            if (d._ambientPanel != this || d._ambientSlot != slot || !slot.IsVisualAncestorOf(d)) { (gone ??= new()).Add(d); continue; }
            if (!d.IsStaleFor(wantsContent)) continue;
            for (Visual? v = d; v is not null; v = v.GetVisualParent())
            {
                if (v is Layoutable l) l.InvalidateMeasure();
                if (v == slot) break;
            }
        }
        if (gone is null) return;
        foreach (var d in gone) nested.Remove(d);
        if (nested.Count == 0) _ambientDependents.Remove(slot);
    }

    // A child measured by this panel's own code rather than its Yoga bridge (the final sweep, which measures what Yoga
    // didn't, such as a child it sized Exactly on both axes; the min-content probe): the mode stays the outer's, as
    // upstream's, but a cached result from the other mode is dropped the same way, and nested panels record this panel
    // as the one that measured them.
    private void MeasureAsSlot(Control child, Size constraint)
    {
        DropStaleNestedMeasures(child, WantsContent(_outerYogaHeightMode));
        var (prevPanel, prevSlot) = (s_outerPanel, s_outerSlot);
        (s_outerPanel, s_outerSlot) = (this, child);
        try { child.Measure(constraint); }
        finally { (s_outerPanel, s_outerSlot) = (prevPanel, prevSlot); }
    }

    private void SyncPointScaleLazy()
    {
        var scale = (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
        if (scale <= 0) return;
        if (Math.Abs(_yogaConfig.PointScaleFactor - scale) < 0.0001f) return;
        _yogaConfig.PointScaleFactor = scale;
        _rootNode.MarkDirtyAndPropagate();
    }

    // ── Layout (see upstream FlexPanel.cs for the full CSS rationale of each branch) ──
    private struct ChildLayout { public float X, Y, Width, Height; }
    private readonly List<ChildLayout> _cachedChildLayouts = new();
    private Size _cachedDesiredSize;
    private bool _arranging;
    private readonly HashSet<Control> _measuredThisPass = new();

    [ThreadStatic] private static YogaMeasureMode? _outerYogaHeightMode;

    // Not upstream's (#9). Beside the mode: the panel whose measure is running, and which of its children it measures.
    [ThreadStatic] private static FlexPanel? s_outerPanel;
    [ThreadStatic] private static Control? s_outerSlot;

    // What this panel's last MeasureOverride used (#9): the outer panel and its child that holds this panel (the panel
    // itself or a wrapper), the mode, and whether the result depended on the mode (fill or fit-content).
    private FlexPanel? _ambientPanel;
    private Control? _ambientSlot;
    private YogaMeasureMode? _ambientMode;
    private bool _ambientWantsContent, _ambientSensitive;
    // Nested panels inside a wrapper child (a component's Border, a card), last measured by this panel, by child (#9).
    private readonly Dictionary<Control, HashSet<FlexPanel>> _ambientDependents = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncPointScaleLazy();
        _measuredThisPass.Clear();
        SyncYogaTree();
        SetRootConstraints();

        bool hasDefiniteWidth = !double.IsInfinity(availableSize.Width);
        bool hasDefiniteHeight = !double.IsInfinity(availableSize.Height);
        bool fillInlineAxis = HorizontalAlignment == HorizontalAlignment.Stretch;

        float rootWidth;
        if (fillInlineAxis && hasDefiniteWidth)
        {
            rootWidth = (float)availableSize.Width;
            _rootNode.MaxWidth = YogaValue.Undefined;
        }
        else
        {
            rootWidth = float.NaN;
            _rootNode.MaxWidth = hasDefiniteWidth ? YogaValue.Point((float)availableSize.Width) : YogaValue.Undefined;
        }

        bool hasExplicitHeight = !double.IsNaN(Height);
        var outerMode = TrackAmbientMode();
        bool outerFlexWantsContent = WantsContent(outerMode);
        bool fillBlockAxis = !hasExplicitHeight && !outerFlexWantsContent
            && VerticalAlignment == VerticalAlignment.Stretch && hasDefiniteHeight;
        _ambientWantsContent = outerFlexWantsContent;
        _ambientSensitive = !hasExplicitHeight && VerticalAlignment == VerticalAlignment.Stretch && hasDefiniteHeight;
        _rootNode.MaxHeight = YogaValue.Undefined;

        float rootHeight = hasExplicitHeight ? (float)Height : fillBlockAxis ? (float)availableSize.Height : float.NaN;
        _rootNode.CalculateLayout(rootWidth, rootHeight, LayoutDirection);

        bool hasDefiniteOwnHeight = hasExplicitHeight || fillBlockAxis;
        float reportedHeight = hasDefiniteOwnHeight
            ? Math.Min(_rootNode.LayoutHeight, (float)availableSize.Height)
            : _rootNode.LayoutHeight;
        _cachedDesiredSize = new Size(_rootNode.LayoutWidth, reportedHeight);

        _cachedChildLayouts.Clear();
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (_nodeCache.TryGetValue(child, out var childNode))
            {
                var layout = new ChildLayout { X = childNode.LayoutX, Y = childNode.LayoutY, Width = childNode.LayoutWidth, Height = childNode.LayoutHeight };
                _cachedChildLayouts.Add(layout);
                if (!_measuredThisPass.Contains(child))
                {
                    var m = child.Margin;
                    MeasureAsSlot(child, new Size(Math.Max(0, layout.Width + m.Left + m.Right), Math.Max(0, layout.Height + m.Top + m.Bottom)));
                }
            }
            else _cachedChildLayouts.Add(default);
        }
        return _cachedDesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool sizeChanged = Math.Abs(finalSize.Width - _cachedDesiredSize.Width) > 0.5
                        || Math.Abs(finalSize.Height - _cachedDesiredSize.Height) > 0.5;
        if (sizeChanged)
        {
            _arranging = true;
            try
            {
                _rootNode.MaxWidth = YogaValue.Undefined;
                _rootNode.MaxHeight = YogaValue.Undefined;
                bool hasExplicitHeight = !double.IsNaN(Height);
                bool fillBlockAxisAtArrange = !hasExplicitHeight && VerticalAlignment == VerticalAlignment.Stretch
                    && !double.IsInfinity(finalSize.Height);
                float arrangeHeight = (hasExplicitHeight || fillBlockAxisAtArrange) ? (float)finalSize.Height : float.NaN;
                _rootNode.CalculateLayout((float)finalSize.Width, arrangeHeight, LayoutDirection);

                _cachedChildLayouts.Clear();
                for (int i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    _cachedChildLayouts.Add(_nodeCache.TryGetValue(child, out var n)
                        ? new ChildLayout { X = n.LayoutX, Y = n.LayoutY, Width = n.LayoutWidth, Height = n.LayoutHeight }
                        : default);
                }
            }
            finally { _arranging = false; }
        }

        for (int i = 0; i < Children.Count && i < _cachedChildLayouts.Count; i++)
        {
            var layout = _cachedChildLayouts[i];
            var child = Children[i];
            var m = child.Margin;
            // Clamped: negative margins larger than the slot would give an invalid (negative) rect.
            child.Arrange(new Rect(layout.X - m.Left, layout.Y - m.Top, Math.Max(0, layout.Width + m.Left + m.Right), Math.Max(0, layout.Height + m.Top + m.Bottom)));
        }
        return finalSize;
    }

    private void SetRootConstraints()
    {
        _rootNode.FlexDirection = Direction;
        _rootNode.JustifyContent = JustifyContent;
        _rootNode.AlignItems = AlignItems;
        _rootNode.AlignContent = AlignContent;
        _rootNode.FlexWrap = Wrap;
        _rootNode.SetGap(YogaGutter.Column, (float)ColumnGap);
        _rootNode.SetGap(YogaGutter.Row, (float)RowGap);
        var p = FlexPadding;
        _rootNode.SetPadding(YogaEdge.Left, YogaValue.Point((float)p.Left));
        _rootNode.SetPadding(YogaEdge.Top, YogaValue.Point((float)p.Top));
        _rootNode.SetPadding(YogaEdge.Right, YogaValue.Point((float)p.Right));
        _rootNode.SetPadding(YogaEdge.Bottom, YogaValue.Point((float)p.Bottom));
    }

    private void SyncYogaTree()
    {
        _syncCurrentChildren.Clear();
        foreach (var child in Children) _syncCurrentChildren.Add(child);
        _syncToRemove.Clear();
        foreach (var kvp in _nodeCache)
            if (!_syncCurrentChildren.Contains(kvp.Key)) _syncToRemove.Add(kvp.Key);
        foreach (var el in _syncToRemove)
        {
            if (_nodeCache.TryGetValue(el, out var node)) _rootNode.RemoveChild(node);
            _nodeCache.Remove(el);
            _attachedCache.Remove(el);
            _ambientDependents.Remove(el);
        }

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (!_nodeCache.TryGetValue(child, out var childNode))
            {
                childNode = new YogaNode(_yogaConfig);
                _nodeCache[child] = childNode;
                var capturedChild = child;
                var panel = this;
                childNode.MeasureFunction = (node, w, wMode, h, hMode) =>
                {
                    var m = capturedChild.Margin;
                    double mH = m.Left + m.Right, mV = m.Top + m.Bottom;
                    if (panel._arranging)
                    {
                        var aOutW = (float)(capturedChild.DesiredSize.Width - mH);
                        var aOutH = (float)(capturedChild.DesiredSize.Height - mV);
                        if (wMode == YogaMeasureMode.Exactly) aOutW = (float)w;
                        if (hMode == YogaMeasureMode.Exactly) aOutH = (float)h;
                        return new YogaSize(Math.Max(0, aOutW), Math.Max(0, aOutH));
                    }
                    var constraintW = wMode == YogaMeasureMode.Undefined ? double.PositiveInfinity : Math.Max(0, w + mH);
                    var constraintH = hMode == YogaMeasureMode.Undefined ? double.PositiveInfinity : Math.Max(0, h + mV);
                    var prevYogaH = _outerYogaHeightMode;
                    var (prevPanel, prevSlot) = (s_outerPanel, s_outerSlot);
                    panel.DropStaleNestedMeasures(capturedChild, WantsContent(hMode));
                    _outerYogaHeightMode = hMode;
                    (s_outerPanel, s_outerSlot) = (panel, capturedChild);
                    try { capturedChild.Measure(new Size(constraintW, constraintH)); }
                    finally { _outerYogaHeightMode = prevYogaH; (s_outerPanel, s_outerSlot) = (prevPanel, prevSlot); }
                    panel._measuredThisPass.Add(capturedChild);
                    var outW = (float)(capturedChild.DesiredSize.Width - mH);
                    var outH = (float)(capturedChild.DesiredSize.Height - mV);
                    if (wMode == YogaMeasureMode.Exactly) outW = (float)w;
                    if (hMode == YogaMeasureMode.Exactly) outH = (float)h;
                    return new YogaSize(Math.Max(0, outW), Math.Max(0, outH));
                };
            }

            ApplyAttachedProperties(child, childNode);
            childNode.Display = child.IsVisible ? YogaDisplay.Flex : YogaDisplay.None;

            if (i < _rootNode.ChildCount)
            {
                if (_rootNode.GetChild(i) != childNode)
                {
                    _rootNode.RemoveChild(childNode);
                    _rootNode.InsertChild(childNode, i);
                }
            }
            else if (childNode.Owner != _rootNode)
            {
                _rootNode.InsertChild(childNode, i);
            }
        }
        while (_rootNode.ChildCount > Children.Count)
            _rootNode.RemoveChild(_rootNode.ChildCount - 1);
    }

    private void ApplyAttachedProperties(Control el, YogaNode node)
    {
        bool hit = _attachedCache.TryGetValue(el, out var ap);
        if (hit && s_detachedAttachedDirty.TryGetValue(el, out _)) hit = false;
        if (!hit)
        {
            s_detachedAttachedDirty.Remove(el);
            ap = new AttachedProps
            {
                Grow = GetGrow(el), Shrink = GetShrink(el), Basis = GetBasis(el), AlignSelf = GetAlignSelf(el),
                Position = GetPosition(el), MinWidth = GetFlexMinWidth(el), MinHeight = GetFlexMinHeight(el),
                Left = GetLeft(el), Top = GetTop(el), Right = GetRight(el), Bottom = GetBottom(el),
            };
            _attachedCache[el] = ap;
        }

        node.Style.FlexGrow = (float)ap.Grow;
        node.Style.FlexShrink = (float)ap.Shrink;
        node.Style.FlexBasis = double.IsNaN(ap.Basis) ? YogaValue.Auto : YogaValue.Point((float)ap.Basis);
        node.Style.AlignSelf = ap.AlignSelf;
        node.Style.PositionType = ap.Position;

        var mainAxisIsRow = FlexDirectionHelper.IsRow(Direction);
        node.MinWidth = ResolveMinDimension(el, axisIsMain: mainAxisIsRow, explicitMin: ap.MinWidth, basis: ap.Basis, isWidth: true);
        node.MinHeight = ResolveMinDimension(el, axisIsMain: !mainAxisIsRow, explicitMin: ap.MinHeight, basis: ap.Basis, isWidth: false);

        node.Style.Position[(int)YogaEdge.Left] = double.IsNaN(ap.Left) ? YogaValue.Undefined : YogaValue.Point((float)ap.Left);
        node.Style.Position[(int)YogaEdge.Top] = double.IsNaN(ap.Top) ? YogaValue.Undefined : YogaValue.Point((float)ap.Top);
        node.Style.Position[(int)YogaEdge.Right] = double.IsNaN(ap.Right) ? YogaValue.Undefined : YogaValue.Point((float)ap.Right);
        node.Style.Position[(int)YogaEdge.Bottom] = double.IsNaN(ap.Bottom) ? YogaValue.Undefined : YogaValue.Point((float)ap.Bottom);

        node.Width = double.IsNaN(el.Width) ? YogaValue.Auto : YogaValue.Point((float)el.Width);
        node.Height = double.IsNaN(el.Height) ? YogaValue.Auto : YogaValue.Point((float)el.Height);

        var margin = el.Margin;
        bool any = margin.Left != 0 || margin.Top != 0 || margin.Right != 0 || margin.Bottom != 0;
        node.SetMargin(YogaEdge.Left, any ? YogaValue.Point((float)margin.Left) : YogaValue.Undefined);
        node.SetMargin(YogaEdge.Top, any ? YogaValue.Point((float)margin.Top) : YogaValue.Undefined);
        node.SetMargin(YogaEdge.Right, any ? YogaValue.Point((float)margin.Right) : YogaValue.Undefined);
        node.SetMargin(YogaEdge.Bottom, any ? YogaValue.Point((float)margin.Bottom) : YogaValue.Undefined);
    }

    private YogaValue ResolveMinDimension(Control el, bool axisIsMain, double explicitMin, double basis, bool isWidth)
    {
        if (!double.IsNaN(explicitMin)) return YogaValue.Point((float)Math.Max(0, explicitMin));
        if (!axisIsMain) return YogaValue.Undefined;
        if (!double.IsNaN(basis) && basis <= 0) return YogaValue.Point(0);
        if (el is ScrollViewer) return YogaValue.Point(0);
        var minContent = ComputeMinContent(el, isWidth);
        if (!double.IsNaN(basis)) return YogaValue.Point((float)Math.Max(0, Math.Min(basis, minContent)));
        return YogaValue.Point((float)Math.Max(0, minContent));
    }

    // Not upstream's: upstream measures at 0 on the axis and reads DesiredSize, which WinUI doesn't clamp. Avalonia
    // clamps DesiredSize to the size offered, so that probe reads 0 (#15). Instead:
    //   • a subtree that can't get narrower (no wrapping or trimmed text, scrolling, wrap panel, Viewbox or stretched
    //     image or shape) has min-content = max-content, so it's measured unconstrained;
    //   • a wrapping TextBlock is measured at a width of 1 and its TextLayout read, which isn't clamped (the widest
//     line: the longest word with WrapWithOverflow);
    //   • anything else, and an element with an explicit size on the axis, keeps upstream's probe (0 on Avalonia),
    //     so it still shrinks freely as before.
    private double ComputeMinContent(Control el, bool isWidth)
    {
        var margin = el.Margin;
        double mH = margin.Left + margin.Right, mV = margin.Top + margin.Bottom;
        if (double.IsNaN(isWidth ? el.Width : el.Height))
        {
            // Scanned before and after the unconstrained measure: before, so content that can shrink costs no extra
            // measure once its templates are applied; after, since the first measure applies them.
            if (!CanShrink(el))
            {
                MeasureAsSlot(el, Size.Infinity);
                if (!CanShrink(el))
                    return isWidth ? Math.Max(0, el.DesiredSize.Width - mH) : Math.Max(0, el.DesiredSize.Height - mV);
            }
        }
        if (isWidth && double.IsNaN(el.Width) && el is TextBlock tb && IsWrapping(tb) && !IsTrimming(tb))
        {
            // 1, not 0: at a width of 0 Avalonia's TextBlock lays out nothing (measured: width 0, one line).
            // TextBlock takes its Padding off the width offered, so it's added back.
            var p = tb.Padding;
            MeasureAsSlot(el, new Size(1 + Math.Max(0, mH) + Math.Max(0, p.Left + p.Right), double.PositiveInfinity));
            var layout = tb.TextLayout;
            return Math.Max(0, Math.Min(tb.MaxWidth, Math.Max(tb.MinWidth, layout.OverhangLeading + layout.WidthIncludingTrailingWhitespace + layout.OverhangTrailing + p.Left + p.Right)));
        }
        var constraint = isWidth ? new Size(Math.Max(0, mH), double.PositiveInfinity) : new Size(double.PositiveInfinity, Math.Max(0, mV));
        MeasureAsSlot(el, constraint);
        return isWidth ? Math.Max(0, el.DesiredSize.Width - mH) : Math.Max(0, el.DesiredSize.Height - mV);
    }

    private static bool IsWrapping(TextBlock tb) => tb.TextWrapping != TextWrapping.NoWrap;
    private static bool IsTrimming(TextBlock tb) => tb.TextTrimming != TextTrimming.None;

    // True when something in the subtree can lay out narrower (or shorter) than its unconstrained size.
    private static bool CanShrink(Control el)
    {
        if (Shrinks(el)) return true;
        foreach (var v in el.GetVisualDescendants())
            if (v is Control c && c.IsEffectivelyVisible && Shrinks(c)) return true;
        return false;

        static bool Shrinks(Control c) => c switch
        {
            TextBlock tb => IsWrapping(tb) || IsTrimming(tb),
            TextPresenter tp => tp.TextWrapping != TextWrapping.NoWrap,
            ScrollViewer or ScrollContentPresenter or WrapPanel or Viewbox => true,
            FlexPanel fp => fp.Wrap != FlexWrap.NoWrap,
            Image img => img.Stretch != Stretch.None,
            Shape shape => shape.Stretch != Stretch.None,
            _ => false,
        };
    }
}