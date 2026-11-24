using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core
{
    /// <summary>
    /// Children placed in rows and columns, as an Avalonia <see cref="Avalonia.Controls.Grid"/>. Mirrors upstream's
    /// <c>GridElement</c> (Reactor's member names); each child's cell is attached with <see cref="GridExtensions.Grid{T}"/>.
    /// </summary>
    [GenerateReactorDescriptor(typeof(Grid))]
    [WrapPanelChildren(PerChild = "ApplyGridAttached")]
    [WrapManual("Definition")]
    public partial record GridElement(GridDefinition Definition, Element[] Children) : Element
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

        // The definition rebuilds the row and column definitions only when it changes. Unlike upstream's, which compares
        // the instance, it compares the tracks (GridDefinition's equality), so the definition the Grid factory builds on
        // every render doesn't rebuild the tracks each time.
        private static partial ControlDescriptor<GridElement, Grid> Customize(ControlDescriptor<GridElement, Grid> d)
            => d.OneWay<GridDefinition>(
                get: static e => e.Definition,
                set: static (c, v) =>
                {
                    c.ColumnDefinitions.Clear();
                    c.RowDefinitions.Clear();
                    if (v is null) return;
                    foreach (var col in v.Columns) c.ColumnDefinitions.Add(ColumnDefinitionOf(col));
                    foreach (var row in v.Rows) c.RowDefinitions.Add(RowDefinitionOf(row));
                });

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

    /// <summary>
    /// A grid's column and row tracks (upstream's <c>GridDefinition</c>). Two definitions are equal when their tracks are
    /// (upstream's record compares the arrays by reference).
    /// </summary>
    public record GridDefinition(GridSize[] Columns, GridSize[] Rows)
    {
        public virtual bool Equals(GridDefinition? other)
            => ReferenceEquals(this, other)
                || (other is not null && EqualityContract == other.EqualityContract
                    && Columns.AsSpan().SequenceEqual(other.Columns) && Rows.AsSpan().SequenceEqual(other.Rows));

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var c in Columns) hash.Add(c);
            hash.Add(Columns.Length);
            foreach (var r in Rows) hash.Add(r);
            return hash.ToHashCode();
        }

        /// <summary>Tracks from strings: <c>"Auto"</c>, <c>"*"</c>, <c>"2*"</c> or a size in DIPs (<see cref="GridSize.Parse"/>).</summary>
        public GridDefinition(string[] columns, string[] rows)
            : this(Parse(columns, nameof(columns)), Parse(rows, nameof(rows)))
        {
        }

        private static GridSize[] Parse(string[] sizes, string name)
        {
            ArgumentNullException.ThrowIfNull(sizes, name);
            var result = new GridSize[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) result[i] = GridSize.Parse(sizes[i]);
            return result;
        }
    }

    /// <summary>A grid child's cell (upstream's <c>GridAttached</c>), set with <see cref="GridExtensions.Grid{T}"/>.</summary>
    public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1);
}

namespace Isotope.UI
{
    using Isotope.UI.Core;

    /// <summary>
    /// One grid track: <see cref="Auto"/>, a <see cref="Star"/> weight or a size in pixels (<see cref="Px"/>), with an
    /// optional minimum and maximum. Our version (tier 3) of upstream's <c>Elements/GridSize.cs</c>, with the same
    /// members; it converts to Avalonia's <see cref="GridLength"/>.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly record struct GridSize
    {
        /// <summary>A track of <paramref name="value"/> in <paramref name="type"/> units.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A negative or non-finite size, a weight that isn't positive, or a negative bound.</exception>
        public GridSize(double value, GridUnitType type, double? min = null, double? max = null)
        {
            if (min is { } mn && !IsFiniteAndNonNegative(mn))
                throw new ArgumentOutOfRangeException(nameof(min), min, "Min size must be a number and >= 0.");
            if (max is { } mx && (mx < 0 || double.IsNaN(mx)))
                throw new ArgumentOutOfRangeException(nameof(max), max, "Max size must be a number and >= 0.");
            if (type == GridUnitType.Pixel && !IsFiniteAndNonNegative(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Pixel size must be a number and >= 0.");
            if (type == GridUnitType.Star && (!IsFiniteAndNonNegative(value) || value <= 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Star weight must be a number and > 0.");
            Value = value;
            Type = type;
            Min = min;
            Max = max;
        }

        public double Value { get; }
        public GridUnitType Type { get; }
        public double? Min { get; }
        public double? Max { get; }

        public void Deconstruct(out double value, out GridUnitType type) => (value, type) = (Value, Type);

        public void Deconstruct(out double value, out GridUnitType type, out double? min, out double? max)
            => (value, type, min, max) = (Value, Type, Min, Max);

        /// <summary>Sized to the track's content.</summary>
        public static GridSize Auto { get; } = new(1, GridUnitType.Auto);

        /// <summary>A share of the space left, by <paramref name="weight"/>.</summary>
        public static GridSize Star(double weight = 1) => new(weight, GridUnitType.Star);

        /// <summary>A fixed size in DIPs.</summary>
        public static GridSize Px(double pixels) => new(pixels, GridUnitType.Pixel);

        public static implicit operator GridLength(GridSize s) => new(s.Value, s.Type);

        public override string ToString() => Type switch
        {
            GridUnitType.Auto => "Auto",
            GridUnitType.Star when Value == 1 => "*",
            GridUnitType.Star => Value.ToString("R", CultureInfo.InvariantCulture) + "*",
            GridUnitType.Pixel => Value.ToString("R", CultureInfo.InvariantCulture),
            _ => $"GridSize({Value}, {Type})",
        };

        /// <summary>Parses <c>"Auto"</c>, <c>"*"</c>, <c>"2.5*"</c> or a size in DIPs (invariant culture).</summary>
        /// <exception cref="FormatException">Any other text.</exception>
        public static GridSize Parse(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            var trimmed = s.Trim();
            if (trimmed.Length == 0) throw new FormatException("Empty grid track string.");
            if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase)) return Auto;
            if (trimmed == "*") return Star();
            if (trimmed.EndsWith('*'))
            {
                var numeric = trimmed[..^1];
                if (numeric.Length > 0
                    && double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var stars)
                    && double.IsFinite(stars) && stars > 0)
                    return Star(stars);
                throw new FormatException($"Invalid grid track '{s}': could not parse star weight.");
            }
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && double.IsFinite(px) && px >= 0)
                return Px(px);
            throw new FormatException($"Invalid grid track '{s}'.");
        }

        private static bool IsFiniteAndNonNegative(double value) => double.IsFinite(value) && value >= 0;
    }

    /// <summary>A track's bounds (upstream's <c>GridSizeExtensions</c>): <c>GridSize.Star().MinSize(100)</c>.</summary>
    public static class GridSizeExtensions
    {
        public static GridSize MinSize(this GridSize size, double min) => new(size.Value, size.Type, min, size.Max);

        public static GridSize MaxSize(this GridSize size, double max) => new(size.Value, size.Type, size.Min, max);
    }

    /// <summary>Sets an element's cell for a <see cref="GridElement"/> parent (upstream's <c>GridExtensions</c>).</summary>
    public static class GridExtensions
    {
        /// <summary>This element in row <paramref name="row"/> and column <paramref name="column"/>, spanning the given counts.</summary>
        public static T Grid<T>(this T el, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : Element =>
            (T)el.SetAttached(new GridAttached(row, column, rowSpan, columnSpan));
    }
}
