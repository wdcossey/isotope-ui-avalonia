using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// Progress and images (#19). Records mirror upstream's (Reactor's member names, Avalonia's value types).

/// <summary>
/// A progress bar, as an Avalonia <see cref="Avalonia.Controls.ProgressBar"/> (upstream's name: Progress). A null
/// <see cref="Value"/> is indeterminate. The value is one-way: the record has no change callback, so the generator
/// doesn't pair it with RangeBase's ValueChanged.
/// </summary>
[GenerateReactorDescriptor(typeof(ProgressBar))]
public partial record ProgressElement(double? Value = null) : Element
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 100;

    internal Action<ProgressBar>[] Setters { get; init; } = [];
}

/// <summary>
/// An image, as an Avalonia <see cref="Avalonia.Controls.Image"/>. <see cref="Source"/> is a file path, a <c>file://</c>
/// URI or an <c>avares://</c> resource URI, loaded as a bitmap; one that can't be loaded leaves the image empty, as
/// upstream's does. Size it with the <c>.Width(...)</c> and <c>.Height(...)</c> modifiers.
/// </summary>
[GenerateReactorDescriptor(typeof(Image), ClearValueOnUnset = true)]
[WrapManual("Source")]
public partial record ImageElement(string Source) : Element
{
    /// <summary>How the image fills its box; null keeps Avalonia's default (Uniform).</summary>
    public Stretch? Stretch { get; init; }

    internal Action<Image>[] Setters { get; init; } = [];

    private static partial ControlDescriptor<ImageElement, Image> Customize(ControlDescriptor<ImageElement, Image> d)
        => d.OneWay(
            get: static e => e.Source,
            set: static (c, v) => c.Source = Load(v));

    /// <summary>The bitmap at <paramref name="source"/>, or null when it can't be read or decoded.</summary>
    internal static Bitmap? Load(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            return new Bitmap(uri is { IsFile: true } ? uri.LocalPath : source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or UriFormatException or NotSupportedException or InvalidOperationException)
        {
            // As upstream's: an image that doesn't load leaves the source empty rather than failing the render.
            return null;
        }
    }
}
