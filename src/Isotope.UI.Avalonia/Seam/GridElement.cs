using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core
{
    /// <summary>
    /// Children placed in rows and columns, as an Avalonia <see cref="Avalonia.Controls.Grid"/>. Mirrors upstream's
    /// <c>GridElement</c>, with Avalonia's member names (#31): Reactor's <c>Definition</c> is
    /// <see cref="ColumnDefinitions"/> and <see cref="RowDefinitions"/>. Each child's cell is attached with
    /// <see cref="GridExtensions.Grid{T}"/>.
    /// </summary>
    [GenerateReactorDescriptor(typeof(Grid))]
    [WrapPanelChildren(PerChild = "ApplyGridAttached")]
    [WrapManual("ColumnDefinitions")]
    [WrapManual("RowDefinitions")]
    public partial record GridElement(GridSize[] ColumnDefinitions, GridSize[] RowDefinitions, Element[] Children) : Element
    {
        private readonly Element[] _children = Reconciler.Flatten(Children);

        /// <summary>The children, with any <see cref="GroupElement"/> flattened into them.</summary>
        public Element[] Children
        {
            get => _children;
            init => _children = Reconciler.Flatten(value);
        }

        /// <summary>Space between rows, in DIPs.</summary>
        public double RowSpacing { get; init; }

        /// <summary>Space between columns, in DIPs.</summary>
        public double ColumnSpacing { get; init; }

        internal Action<Grid>[] Setters { get; init; } = [];

        // As upstream's: every child gets its cell written, and a child with none is reset, so a control the reconciler
        // reuses never keeps a stale cell. A span of 1 is the default, so it is cleared rather than written.
        private static void ApplyGridAttached(Grid grid, UIElement ui, Element child)
        {
            var ga = child.GetAttached<GridAttached>();
            if (ga is null)
            {
                ui.ClearValue(Grid.RowProperty);
                ui.ClearValue(Grid.ColumnProperty);
                ui.ClearValue(Grid.RowSpanProperty);
                ui.ClearValue(Grid.ColumnSpanProperty);
                return;
            }
            Grid.SetRow(ui, ga.Row);
            Grid.SetColumn(ui, ga.Column);
            if (ga.RowSpan > 1) Grid.SetRowSpan(ui, ga.RowSpan);
            else ui.ClearValue(Grid.RowSpanProperty);
            if (ga.ColumnSpan > 1) Grid.SetColumnSpan(ui, ga.ColumnSpan);
            else ui.ClearValue(Grid.ColumnSpanProperty);
        }

        // Each set of tracks is rebuilt only when it changes. Unlike upstream's, which compares the definition's
        // instance, it compares the tracks, so the arrays a component builds on every render don't rebuild them each
        // time.
        private static partial ControlDescriptor<GridElement, Grid> Customize(ControlDescriptor<GridElement, Grid> d)
            => d.ImperativeBridged(
                mount: static (ctx, c, e) => WriteTracks(c, e.ColumnDefinitions, e.RowDefinitions, columns: true, rows: true),
                update: static (ctx, c, o, n) => WriteTracks(c, n.ColumnDefinitions, n.RowDefinitions,
                    columns: !TracksEqual(o.ColumnDefinitions, n.ColumnDefinitions),
                    rows: !TracksEqual(o.RowDefinitions, n.RowDefinitions)));

        // Both sets are converted before the grid is touched: upstream's GridSize doesn't check an Auto track's value,
        // and Avalonia's GridLength rejects a bad one, so such a render fails with all the grid's tracks as they were.
        private static void WriteTracks(Grid c, GridSize[]? columnSizes, GridSize[]? rowSizes, bool columns, bool rows)
        {
            if (!columns && !rows) return;
            var newColumns = columns ? Array.ConvertAll(columnSizes ?? [], ColumnDefinitionOf) : null;
            var newRows = rows ? Array.ConvertAll(rowSizes ?? [], RowDefinitionOf) : null;
            if (newColumns is not null)
            {
                c.ColumnDefinitions.Clear();
                foreach (var col in newColumns) c.ColumnDefinitions.Add(col);
            }
            if (newRows is not null)
            {
                c.RowDefinitions.Clear();
                foreach (var row in newRows) c.RowDefinitions.Add(row);
            }
        }

        /// <summary>Whether two track arrays hold the same tracks (upstream's record compares the instance).</summary>
        internal static bool TracksEqual(GridSize[]? a, GridSize[]? b) =>
            ReferenceEquals(a, b) || (a is not null && b is not null && a.AsSpan().SequenceEqual(b));

        private static ColumnDefinition ColumnDefinitionOf(GridSize size)
        {
            var def = new ColumnDefinition { Width = size };
            if (size.Min is { } min) def.MinWidth = min;
            if (size.Max is { } max) def.MaxWidth = max;
            return def;
        }

        private static RowDefinition RowDefinitionOf(GridSize size)
        {
            var def = new RowDefinition { Height = size };
            if (size.Min is { } min) def.MinHeight = min;
            if (size.Max is { } max) def.MaxHeight = max;
            return def;
        }
    }

    /// <summary>A grid child's cell (upstream's <c>GridAttached</c>), set with <see cref="GridExtensions.Grid{T}"/>.</summary>
    public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1);
}

namespace Isotope.UI
{
    using Isotope.UI.Core;

    /// <summary>Sets an element's cell for a <see cref="GridElement"/> parent (upstream's <c>GridExtensions</c>).</summary>
    public static class GridExtensions
    {
        /// <summary>This element in row <paramref name="row"/> and column <paramref name="column"/>, spanning the given counts.</summary>
        public static T Grid<T>(this T el, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : Element =>
            (T)el.SetAttached(new GridAttached(row, column, rowSpan, columnSpan));
    }
}
