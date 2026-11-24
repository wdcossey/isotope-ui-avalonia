using Avalonia;

namespace Isotope.UI;

/// <summary>
/// A snapshot of one display, as <c>RenderContext.UseDisplays</c> returns it: upstream's member names, with Avalonia's
/// value types, built from Avalonia's <c>Screen</c>. Tier 3. A value: two snapshots of an unchanged display are equal.
/// </summary>
public sealed record DisplayInfo
{
    /// <summary>The display's name from the platform, else its platform handle, else its index.</summary>
    public required string Id { get; init; }

    /// <summary>Whether this is the primary display.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>The display's working area (without task bars and docks), in DIPs at this display's scaling.</summary>
    public Rect WorkAreaDip { get; init; }

    /// <summary>The display's bounds, in DIPs at this display's scaling.</summary>
    public Rect BoundsDip { get; init; }

    /// <summary>The display's DPI: 96 x its scaling (96 at 100%, 144 at 150%).</summary>
    public uint Dpi { get; init; }
}
