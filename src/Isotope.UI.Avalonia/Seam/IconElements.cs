using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Isotope.UI.Core.V1Protocol;
using AvIconElement = Avalonia.Controls.IconElement;

namespace Isotope.UI.Core;

// Icons (#91): upstream's IconData union and its standalone IconElement (src/Reactor/Core/Element.cs, Element.Icon.cs),
// with Avalonia's value types. Upstream's record is [WrapPolymorphic], which Isotope's generator doesn't support
// (ISOTOPEGEN004), so the handler is hand-written: the control is chosen from the data's kind, patched in place while the
// kind stays the same, and mounted afresh when it changes (Reconciler.CanUpdate declines the update). Upstream's
// SymbolIconData isn't here: WinUI's symbols are glyphs of the Segoe icon fonts, which Isotope doesn't ship (they may not
// be shipped to other platforms; developer, 2026-11-26). The guide's "WinUI symbols" (docs/guide/writing-components.md)
// shows FontIcon(glyph, "Segoe Fluent Icons") on Windows, or an app's own icons, and the migration points there for
// Symbol and WinUI's SymbolIcon type (ISOMIG005), the SymbolIcon factory (ISOMIG011) and Icon(string) (ISOMIG010). A command's Icon takes this data too, but nothing draws it yet (menu items have no icons).

/// <summary>
/// What an icon shows (upstream's union): a shape (<see cref="PathIconData"/>), a glyph of a font
/// (<see cref="FontIconData"/>), or a bitmap (<see cref="BitmapIconData"/>, <see cref="ImageIconData"/>). Show one with
/// <c>Icon(...)</c>.
/// </summary>
public abstract record IconData;

/// <summary>
/// A vector shape, from path markup such as <c>"M 0,0 L 24,0 L 12,20 Z"</c>, parsed as Avalonia parses a path's
/// <c>Data</c>; shown as an Avalonia <see cref="Avalonia.Controls.PathIcon"/>, filled with the icon's foreground and scaled
/// to its size (20 by 20 in the Fluent theme). Blank markup draws nothing; markup Avalonia rejects throws, as upstream's
/// does, so a typo stays visible.
/// </summary>
public record PathIconData(string Data) : IconData;

/// <summary>
/// A glyph of a font (upstream's <c>FontIconData</c>; Avalonia's core has no glyph icon), shown as a text block in
/// <see cref="FontFamily"/> at <see cref="FontSize"/>, in the icon's foreground. Left null, they are WinUI's: the Segoe
/// icon fonts that ship with Windows (<c>"Segoe Fluent Icons, Segoe MDL2 Assets"</c>), which other systems lack, and 20.
/// A <c>.FontFamily(...)</c> or <c>.FontSize(...)</c> modifier is used when the member is null.
/// </summary>
public record FontIconData(string Glyph, FontFamily? FontFamily = null, double? FontSize = null) : IconData
{
    /// <summary>
    /// Upstream's form: the font by name, or null for the default. A null string doesn't reach Avalonia's conversion of a
    /// string to a <see cref="Avalonia.Media.FontFamily"/>, which throws on null.
    /// </summary>
    public FontIconData(string Glyph, string? FontFamily, double? FontSize = null)
        : this(Glyph, FontFamily is null ? null : global::Avalonia.Media.FontFamily.Parse(FontFamily), FontSize) { }
}

/// <summary>
/// A bitmap at <see cref="Source"/> (a file path or <c>file://</c> URI, or an <c>avares://</c> resource), scaled
/// uniformly to the icon's size. With <see cref="ShowAsMonochrome"/> (the default, as WinUI's), only its shape is drawn,
/// in the icon's foreground: its opacity masks the foreground. A bitmap that can't be loaded leaves the icon empty.
/// </summary>
public record BitmapIconData(Uri Source, bool ShowAsMonochrome = true) : IconData;

/// <summary>
/// An image at <see cref="Source"/> (a file path or <c>file://</c> URI, or an <c>avares://</c> resource), in its own
/// colours, as an Avalonia <see cref="Avalonia.Controls.Image"/>. One that can't be loaded leaves the icon empty.
/// </summary>
public record ImageIconData(Uri Source) : IconData;

/// <summary>
/// An icon in the element tree (upstream's standalone icon element), made with <c>Icon(...)</c>. Its control comes from
/// <see cref="Data"/>'s kind: an Avalonia <see cref="Avalonia.Controls.PathIcon"/> for a path, a text block for a font glyph,
/// a bitmap icon for a bitmap, an Avalonia <see cref="Avalonia.Controls.Image"/> for an image. While the kind stays the same
/// the control is updated in place; another kind mounts a new control. Size it with <c>.Width(...)</c> and
/// <c>.Height(...)</c> (a glyph with its font size), colour it with <c>.Foreground(...)</c> or a theme token (an image keeps
/// its own colours).
/// </summary>
public record IconElement(IconData Data) : Element
{
    // No Setters, unlike upstream's: nothing public writes them, so the skip arm compares Data alone (Element.cs).

    // Registers the hand-written handler on first use, as a generated descriptor registers from its record's static
    // constructor (Reconciler.TryGetV1Handler runs it).
    static IconElement() => ControlRegistry.Register<IconElement, Control>(static () => new IconHandler());

    /// <summary>WinUI's FontIcon font, the Segoe icon fonts that ship with Windows; the glyph's when it names none.</summary>
    internal static readonly FontFamily SymbolFontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>WinUI's FontIcon size; the glyph's when neither it nor a modifier gives one.</summary>
    internal const double FontIconSize = 20;

    /// <summary>
    /// Whether two icons' data show the same thing: record equality, and a bitmap's or image's URI as written, since
    /// <see cref="Uri"/>'s equality ignores a fragment (<c>#v2</c>) that names another resource to load.
    /// </summary>
    internal static bool SameData(IconData? a, IconData? b) => Equals(a, b) && SourceOf(a) == SourceOf(b);

    // A bitmap's or image's URI as written, which the bitmap is loaded from.
    internal static string? SourceOf(IconData? data) => data switch
    {
        BitmapIconData b => b.Source?.OriginalString,
        ImageIconData i => i.Source?.OriginalString,
        _ => null,
    };

    /// <summary>The kind of control an icon's data mounts; a change of kind mounts a new control.</summary>
    internal static IconKind KindOf(IconData? data) => data switch
    {
        PathIconData => IconKind.Path,
        FontIconData => IconKind.Font,
        BitmapIconData => IconKind.Bitmap,
        ImageIconData => IconKind.Image,
        _ => IconKind.None,
    };
}

internal enum IconKind { None, Path, Font, Bitmap, Image }

/// <summary>
/// The icon's handler: mounts the control for the data's kind and writes what changed. The reconciler never pairs it with
/// another kind's control (CanUpdate), so Update patches in place; unmount disposes the bitmap an icon loaded.
/// </summary>
internal sealed class IconHandler : IElementHandler<IconElement, Control>
{
    public Control Mount(MountContext ctx, IconElement element)
    {
        Control control = IconElement.KindOf(element.Data) switch
        {
            IconKind.Path => new PathIcon(),
            IconKind.Font => new GlyphTextBlock(),
            IconKind.Bitmap => new BitmapIconPresenter(),
            IconKind.Image => new Image(),
            _ => throw new NotSupportedException(
                $"An icon shows a PathIconData, FontIconData, BitmapIconData or ImageIconData, not {element.Data?.GetType().Name ?? "null"}."),
        };
        Write(null, element.Data, control);
        return control;
    }

    public void Update(UpdateContext ctx, IconElement oldEl, IconElement newEl, Control control)
        => Write(oldEl.Data, newEl.Data, control);

    public void Unmount(UnmountContext ctx, Control control)
    {
        switch (control)
        {
            case Image image: ImageElement.Replace(image, null); break;
            case BitmapIconPresenter bitmap: bitmap.Replace(null); break;
        }
    }

    // Writes what differs from the last render's data (all of it on mount), as a controlled value equal to the last
    // render's isn't re-applied. A font glyph's family and size are written with the text modifiers (Reconciler.Text.cs),
    // where they meet .FontFamily(...) and .FontSize(...).
    private static void Write(IconData? old, IconData data, Control control)
    {
        switch (data, control)
        {
            case (PathIconData path, PathIcon icon):
                if (old is not PathIconData oldPath || oldPath.Data != path.Data) icon.Data = ParsePath(path.Data);
                break;
            case (FontIconData font, GlyphTextBlock text):
                if (old is not FontIconData oldFont || oldFont.Glyph != font.Glyph) text.Text = font.Glyph;
                break;
            case (BitmapIconData bitmap, BitmapIconPresenter presenter):
                if (old is not BitmapIconData || IconElement.SourceOf(old) != IconElement.SourceOf(bitmap))
                    presenter.Replace(ImageElement.Load(bitmap.Source.OriginalString));
                presenter.ShowAsMonochrome = bitmap.ShowAsMonochrome;
                break;
            case (ImageIconData image, Image img):
                if (old is not ImageIconData || IconElement.SourceOf(old) != IconElement.SourceOf(image))
                    ImageElement.Replace(img, ImageElement.Load(image.Source.OriginalString));
                break;
            default:
                throw new InvalidOperationException(
                    $"A {data.GetType().Name} was paired with a {control.GetType().Name}: a change of icon kind mounts a new control.");
        }
    }

    // Avalonia's path markup parser, as a path's Data is parsed; blank markup is no geometry, as upstream's PathMarkup.
    private static Geometry? ParsePath(string? data) => string.IsNullOrWhiteSpace(data) ? null : Geometry.Parse(data);
}

/// <summary>
/// A font icon's glyph (Avalonia's core has no glyph icon): a text block whose text is centred in its box, across and
/// down, as WinUI's FontIcon centres its glyph, so a glyph sized with .Width and .Height sits where a shape would.
/// </summary>
internal sealed class GlyphTextBlock : TextBlock
{
    static GlyphTextBlock() => TextAlignmentProperty.OverrideDefaultValue<GlyphTextBlock>(TextAlignment.Center);

    // Styled as a text block: a style or theme for TextBlock reaches the glyph too.
    protected override Type StyleKeyOverride => typeof(TextBlock);

    // A TextBlock draws its text at the top of a box taller than the text: move it down to the middle.
    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var spare = Bounds.Height - Padding.Top - Padding.Bottom - TextLayout.Height;
        base.RenderTextLayout(context, spare > 0 ? new Point(origin.X, origin.Y + spare / 2) : origin);
    }
}

/// <summary>
/// A bitmap icon (upstream's WinUI BitmapIcon; Avalonia's core has none): the bitmap scaled uniformly to the control's
/// size, or with <see cref="ShowAsMonochrome"/> its shape in <see cref="TemplatedControl.Foreground"/> (the bitmap's
/// opacity masks the foreground). Measured as an <see cref="Image"/> with uniform stretch.
/// </summary>
internal sealed class BitmapIconPresenter : AvIconElement
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<BitmapIconPresenter, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<bool> ShowAsMonochromeProperty =
        AvaloniaProperty.Register<BitmapIconPresenter, bool>(nameof(ShowAsMonochrome), defaultValue: true);

    // The monochrome mask, made once per bitmap rather than on every render.
    private ImageBrush? _mask;

    static BitmapIconPresenter()
    {
        AffectsRender<BitmapIconPresenter>(SourceProperty, ShowAsMonochromeProperty, ForegroundProperty);
        AffectsMeasure<BitmapIconPresenter>(SourceProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty) _mask = null;
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool ShowAsMonochrome
    {
        get => GetValue(ShowAsMonochromeProperty);
        set => SetValue(ShowAsMonochromeProperty, value);
    }

    /// <summary>Shows <paramref name="next"/>, disposing the bitmap this icon loaded before, as an image does.</summary>
    internal void Replace(Bitmap? next)
    {
        var previous = Source;
        Source = next;
        ImageElement.Swap(previous, next);
    }

    protected override Size MeasureOverride(Size availableSize)
        => Source is { } source ? Stretch.Uniform.CalculateSize(availableSize, source.Size) : default;

    protected override Size ArrangeOverride(Size finalSize)
        => Source is { } source ? Stretch.Uniform.CalculateSize(finalSize, source.Size) : default;

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0) return;
        var bounds = new Rect(Bounds.Size);
        var scale = Stretch.Uniform.CalculateScaling(bounds.Size, source.Size);
        var size = new Size(source.Size.Width * scale.X, source.Size.Height * scale.Y);
        var target = new Rect(new Point((bounds.Width - size.Width) / 2, (bounds.Height - size.Height) / 2), size);
        if (!ShowAsMonochrome)
        {
            context.DrawImage(source, new Rect(source.Size), target);
            return;
        }
        if (Foreground is not { } foreground) return;
        _mask ??= new ImageBrush(source) { Stretch = Stretch.Fill };
        using (context.PushOpacityMask(_mask, target))
            context.FillRectangle(foreground, target);
    }
}
