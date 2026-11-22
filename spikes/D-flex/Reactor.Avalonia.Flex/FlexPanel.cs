// Port of upstream src/Reactor/Yoga/FlexPanel.cs (microsoft-ui-reactor 28a87225) from WinUI 3 to Avalonia 12.
// The layout logic (two-pass measure, arrange from cached Yoga results, MeasureFunction bridge, CSS §4.5
// automatic minimum size) is kept line-for-line where the platforms agree; the differences are:
//   • StyledProperty/AttachedProperty instead of DependencyProperty; AffectsMeasure + Changed handlers
//     instead of PropertyMetadata callbacks.
//   • Visibility.Collapsed → !IsVisible; XamlRoot.RasterizationScale → TopLevel.RenderScaling;
//     Unloaded → OnDetachedFromVisualTree; no ScrollView (only ScrollViewer).
// Avalonia's Measure/DesiredSize/Arrange include the child's Margin exactly as WinUI's do, so the margin
// arithmetic is unchanged.

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Microsoft.UI.Reactor.Layout;

namespace Reactor.Avalonia.Layout;

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
        bool outerFlexWantsContent = _outerYogaHeightMode == YogaMeasureMode.Undefined || _outerYogaHeightMode == YogaMeasureMode.AtMost;
        bool fillBlockAxis = !hasExplicitHeight && !outerFlexWantsContent
            && VerticalAlignment == VerticalAlignment.Stretch && hasDefiniteHeight;
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
                    child.Measure(new Size(Math.Max(0, layout.Width + m.Left + m.Right), Math.Max(0, layout.Height + m.Top + m.Bottom)));
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
                    _outerYogaHeightMode = hMode;
                    try { capturedChild.Measure(new Size(constraintW, constraintH)); }
                    finally { _outerYogaHeightMode = prevYogaH; }
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

    private static double ComputeMinContent(Control el, bool isWidth)
    {
        var margin = el.Margin;
        double mH = margin.Left + margin.Right, mV = margin.Top + margin.Bottom;
        var constraint = isWidth ? new Size(Math.Max(0, mH), double.PositiveInfinity) : new Size(double.PositiveInfinity, Math.Max(0, mV));
        el.Measure(constraint);
        return isWidth ? Math.Max(0, el.DesiredSize.Width - mH) : Math.Max(0, el.DesiredSize.Height - mV);
    }
}