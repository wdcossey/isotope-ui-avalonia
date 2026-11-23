using Avalonia;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Layout;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core
{
    /// <summary>
    /// A CSS flexbox container, as our Avalonia <see cref="Layout.FlexPanel"/> over upstream's Yoga. Mirrors upstream's
    /// <c>FlexElement</c> (Reactor's member names); the per-child flex data is attached with
    /// <see cref="FlexExtensions.Flex{T}"/>.
    /// </summary>
    [GenerateReactorDescriptor(typeof(FlexPanel))]
    [WrapPanelChildren(PerChild = "ApplyFlexChildAttached")]
    [WrapManual("FlexPadding")]
    public partial record FlexElement(Element[] Children) : Element
    {
        private readonly Element[] _children = Reconciler.Flatten(Children);

        /// <summary>The children, with any <see cref="GroupElement"/> flattened into them.</summary>
        public Element[] Children
        {
            get => _children;
            init => _children = Reconciler.Flatten(value);
        }

        public FlexDirection Direction { get; init; } = FlexDirection.Row;
        public FlexJustify JustifyContent { get; init; } = FlexJustify.FlexStart;
        public FlexAlign AlignItems { get; init; } = FlexAlign.Stretch;
        public FlexAlign AlignContent { get; init; } = FlexAlign.FlexStart;
        public FlexWrap Wrap { get; init; } = FlexWrap.NoWrap;
        public double ColumnGap { get; init; }
        public double RowGap { get; init; }
        public Thickness FlexPadding { get; init; }
        internal Action<FlexPanel>[] Setters { get; init; } = [];

        // As upstream's: FlexPadding (a non-nullable Thickness) is written by hand.
        private static partial ControlDescriptor<FlexElement, FlexPanel> Customize(ControlDescriptor<FlexElement, FlexPanel> d)
            => d.OneWay(
                get: static e => e.FlexPadding,
                set: static (c, v) => c.FlexPadding = v);

        // As upstream's: every child gets its flex data written, and a child with none is reset to the defaults, so a
        // control the reconciler reuses never keeps stale values. Our FlexPanel names the min sizes FlexMinWidth and
        // FlexMinHeight (Spike D: Avalonia's Layoutable already has MinWidth and MinHeight).
        private static void ApplyFlexChildAttached(FlexPanel panel, UIElement ctrl, Element child)
        {
            var fa = child.GetAttached<FlexAttached>();
            FlexPanel.SetGrow(ctrl, fa?.Grow ?? 0);
            FlexPanel.SetShrink(ctrl, fa?.Shrink ?? 1);
            if (fa is { Basis: { } basis }) FlexPanel.SetBasis(ctrl, basis);
            else ctrl.ClearValue(FlexPanel.BasisProperty);
            if (fa is { MinWidth: { } minWidth }) FlexPanel.SetFlexMinWidth(ctrl, minWidth);
            else ctrl.ClearValue(FlexPanel.FlexMinWidthProperty);
            if (fa is { MinHeight: { } minHeight }) FlexPanel.SetFlexMinHeight(ctrl, minHeight);
            else ctrl.ClearValue(FlexPanel.FlexMinHeightProperty);
            if (fa is { AlignSelf: { } alignSelf }) FlexPanel.SetAlignSelf(ctrl, alignSelf);
            else ctrl.ClearValue(FlexPanel.AlignSelfProperty);
            FlexPanel.SetPosition(ctrl, fa?.Position ?? FlexPositionType.Relative);
            if (fa is { Left: { } left }) FlexPanel.SetLeft(ctrl, left);
            else ctrl.ClearValue(FlexPanel.LeftProperty);
            if (fa is { Top: { } top }) FlexPanel.SetTop(ctrl, top);
            else ctrl.ClearValue(FlexPanel.TopProperty);
            if (fa is { Right: { } right }) FlexPanel.SetRight(ctrl, right);
            else ctrl.ClearValue(FlexPanel.RightProperty);
            if (fa is { Bottom: { } bottom }) FlexPanel.SetBottom(ctrl, bottom);
            else ctrl.ClearValue(FlexPanel.BottomProperty);
        }
    }

    /// <summary>A child's flex data (upstream's <c>FlexAttached</c>), set with <see cref="FlexExtensions.Flex{T}"/>.</summary>
    public record FlexAttached(
        double Grow = 0,
        double Shrink = 1,
        double? Basis = null,
        double? MinWidth = null,
        double? MinHeight = null,
        FlexAlign? AlignSelf = null,
        FlexPositionType Position = FlexPositionType.Relative,
        double? Left = null,
        double? Top = null,
        double? Right = null,
        double? Bottom = null);
}

namespace Isotope.UI
{
    using Isotope.UI.Core;

    /// <summary>Sets an element's flex data for a <see cref="FlexElement"/> parent (upstream's <c>FlexExtensions</c>).</summary>
    public static class FlexExtensions
    {
        /// <summary>
        /// This element with its flex data: grow and shrink factors, basis, minimum sizes (null: CSS <c>auto</c>),
        /// cross-axis alignment and absolute position offsets.
        /// </summary>
        public static T Flex<T>(this T el,
            double grow = 0,
            double shrink = 1,
            double? basis = null,
            double? minWidth = null,
            double? minHeight = null,
            FlexAlign? alignSelf = null,
            FlexPositionType position = FlexPositionType.Relative,
            double? left = null,
            double? top = null,
            double? right = null,
            double? bottom = null) where T : Element
        {
            if (grow < 0 || double.IsNaN(grow) || double.IsInfinity(grow))
                throw new ArgumentOutOfRangeException(nameof(grow), "Grow must be a non-negative, finite value.");
            if (shrink < 0 || double.IsNaN(shrink) || double.IsInfinity(shrink))
                throw new ArgumentOutOfRangeException(nameof(shrink), "Shrink must be a non-negative, finite value.");
            if (minWidth is { } mw && (mw < 0 || double.IsNaN(mw) || double.IsInfinity(mw)))
                throw new ArgumentOutOfRangeException(nameof(minWidth), "MinWidth must be a non-negative, finite value (or null for CSS `min-width: auto`).");
            if (minHeight is { } mh && (mh < 0 || double.IsNaN(mh) || double.IsInfinity(mh)))
                throw new ArgumentOutOfRangeException(nameof(minHeight), "MinHeight must be a non-negative, finite value (or null for CSS `min-height: auto`).");
            return (T)el.SetAttached(new FlexAttached(grow, shrink, basis, minWidth, minHeight, alignSelf, position, left, top, right, bottom));
        }
    }
}
