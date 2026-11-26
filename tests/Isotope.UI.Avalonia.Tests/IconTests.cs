using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;
using AvPathIcon = Avalonia.Controls.PathIcon;
using AvImage = Avalonia.Controls.Image;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Icons (#91): upstream's standalone icon over Avalonia's controls. The data's kind chooses the control (a PathIcon, a
/// text block, a bitmap icon, an Image); the same kind is updated in place and another kind mounts a new control. Sizing
/// and the foreground reach the icon, checked on rendered frames too.
/// </summary>
public class IconTests
{
    private const string Triangle = "M 0,0 L 24,0 L 12,20 Z";
    private const string Glyph = "\uE790";

    private static Control Mounted(TestHost host) => Assert.IsAssignableFrom<Control>(host.Root.Child);

    // A PNG of w by h pixels, opaque red in its left half and transparent in its right half.
    private static Uri Png(int w = 4, int h = 4)
    {
        var path = Path.Combine(Path.GetTempPath(), "isotope-icon-" + Guid.NewGuid().ToString("N") + ".png");
        using (var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
        {
            using (var fb = bitmap.Lock())
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Marshal.WriteInt32(fb.Address + y * fb.RowBytes + x * 4, x < w / 2 ? unchecked((int)0xFFFF0000) : 0);
            bitmap.Save(path, new PngBitmapEncoderOptions());
        }
        return new Uri(path);
    }

    private static void Delete(params Uri[] uris)
    {
        foreach (var uri in uris) File.Delete(uri.LocalPath);
    }

    // A window showing the host's root, laid out and rendered.
    private static Window Show(TestHost host, double width = 100, double height = 100)
    {
        var window = new Window { Width = width, Height = height, Background = Brushes.White, Content = host.Root };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // The colour of the rendered pixel at (x, y), in device pixels (the headless window's scaling is 1).
    private static Color Pixel(Window window, double x, double y)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        using var fb = frame.Lock();
        var p = (uint)Marshal.ReadInt32(fb.Address + (int)y * fb.RowBytes + (int)x * 4); // little-endian: bytes 0..3
        var (b0, b1, b2, a) = ((byte)p, (byte)(p >> 8), (byte)(p >> 16), (byte)(p >> 24));
        return fb.Format == PixelFormat.Rgba8888 ? Color.FromArgb(a, b0, b1, b2) : Color.FromArgb(a, b2, b1, b0);
    }

    // The window point at (x, y) inside the control.
    private static Point At(Control control, Window window, double x, double y) =>
        control.TranslatePoint(new Point(x, y), window) ?? throw new InvalidOperationException("not shown");

    // ── Path ─────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_path_icon_mounts_an_Avalonia_PathIcon_with_the_markup_parsed_as_Avalonia_parses_a_path()
    {
        var host = new TestHost(() => Icon(PathIcon(Triangle)));
        var icon = Assert.IsType<AvPathIcon>(Mounted(host));
        Assert.Equal(new Rect(0, 0, 24, 20), Assert.IsAssignableFrom<Geometry>(icon.Data).Bounds);
    }

    [AvaloniaFact]
    public void Blank_path_markup_draws_nothing_and_markup_Avalonia_rejects_throws()
    {
        var host = new TestHost(() => Icon(PathIcon("  ")));
        Assert.Null(Assert.IsType<AvPathIcon>(Mounted(host)).Data);
        Assert.ThrowsAny<Exception>(() => new TestHost(() => Icon(PathIcon("M 0,0 X 5"))));
    }

    [AvaloniaFact]
    public void A_path_icon_is_updated_in_place_and_its_unchanged_markup_is_not_parsed_again()
    {
        var data = Triangle;
        var width = 20.0;
        var host = new TestHost(() => Icon(PathIcon(data)).Width(width));
        var icon = Assert.IsType<AvPathIcon>(Mounted(host));
        var geometry = icon.Data;

        width = 30; // the same data, another modifier
        host.Render();
        Assert.Same(icon, Mounted(host));
        Assert.Same(geometry, icon.Data);
        Assert.Equal(30, icon.Width);

        data = "M 0,0 L 10,0 L 10,10 Z";
        host.Render();
        Assert.Same(icon, Mounted(host));
        Assert.Equal(new Rect(0, 0, 10, 10), icon.Data!.Bounds);
    }

    [AvaloniaFact]
    public void An_unchanged_icon_is_skipped()
    {
        var host = new TestHost(() => Icon(PathIcon(Triangle)));
        var skipped = host.Reconciler.DebugElementsSkipped;
        host.Render();
        Assert.Equal(skipped + 1, host.Reconciler.DebugElementsSkipped);
    }

    // ── Font glyph ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_font_icon_mounts_a_text_block_of_its_glyph_in_WinUIs_icon_fonts_at_20_when_it_names_neither()
    {
        var host = new TestHost(() => Icon(FontIcon(Glyph)));
        var text = Assert.IsType<TextBlock>(Mounted(host));
        Assert.Equal((Glyph, 20.0), (text.Text, text.FontSize));
        Assert.Equal(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), text.FontFamily);
    }

    [AvaloniaFact]
    public void A_font_icons_own_font_and_size_win_over_the_modifiers_which_win_over_the_defaults()
    {
        FontFamily? family = "Consolas";
        double? size = 16;
        var host = new TestHost(() => Icon(FontIcon("A", family, size)).FontFamily("Arial").FontSize(30));
        var text = Assert.IsType<TextBlock>(Mounted(host));
        Assert.Equal(("Consolas", 16.0), (text.FontFamily.Name, text.FontSize));

        family = null;
        size = null;
        host.Render();
        Assert.Same(text, Mounted(host));
        Assert.Equal(("Arial", 30.0), (text.FontFamily.Name, text.FontSize));
    }

    [AvaloniaFact]
    public void A_font_icon_is_updated_in_place()
    {
        var glyph = "A";
        double? size = null;
        var host = new TestHost(() => Icon(FontIcon(glyph, fontSize: size)));
        var text = Assert.IsType<TextBlock>(Mounted(host));

        glyph = "B";
        size = 32;
        host.Render();
        Assert.Same(text, Mounted(host));
        Assert.Equal(("B", 32.0), (text.Text, text.FontSize));

        size = null; // back to WinUI's size
        host.Render();
        Assert.Equal(20.0, text.FontSize);
    }

    // ── Bitmap ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_bitmap_icon_mounts_a_bitmap_icon_of_the_loaded_bitmap_monochrome_by_default()
    {
        var png = Png(3, 2);
        try
        {
            var host = new TestHost(() => Icon(BitmapIcon(png)));
            var icon = Assert.IsType<BitmapIconPresenter>(Mounted(host));
            Assert.Equal(new PixelSize(3, 2), icon.Source!.PixelSize);
            Assert.True(icon.ShowAsMonochrome);
        }
        finally { Delete(png); }
    }

    [AvaloniaFact]
    public void A_bitmap_icon_is_updated_in_place_disposing_the_bitmap_it_replaced_and_the_last_on_unmount()
    {
        var (first, second) = (Png(2, 2), Png(3, 3));
        try
        {
            var source = first;
            var mono = true;
            var host = new TestHost(() => Icon(BitmapIcon(source, mono)));
            var icon = Assert.IsType<BitmapIconPresenter>(Mounted(host));
            var loaded = icon.Source!;

            mono = false; // the same source: not loaded again
            host.Render();
            Assert.Same(icon, Mounted(host));
            Assert.Same(loaded, icon.Source);
            Assert.False(icon.ShowAsMonochrome);

            source = second;
            host.Render();
            Assert.Same(icon, Mounted(host));
            var next = icon.Source!;
            Assert.Equal(new PixelSize(3, 3), next.PixelSize);
            Assert.Throws<ObjectDisposedException>(() => loaded.PixelSize);

            host.Unmount();
            Assert.Throws<ObjectDisposedException>(() => next.PixelSize);
        }
        finally { Delete(first, second); }
    }

    // ── Image ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void An_image_icon_mounts_an_Avalonia_Image_updated_in_place_disposing_its_bitmaps()
    {
        var (first, second) = (Png(2, 2), Png(3, 3));
        try
        {
            var source = first;
            var host = new TestHost(() => Icon(ImageIcon(source)));
            var image = Assert.IsType<AvImage>(Mounted(host));
            var loaded = Assert.IsType<Bitmap>(image.Source);
            Assert.Equal(new PixelSize(2, 2), loaded.PixelSize);

            source = second;
            host.Render();
            Assert.Same(image, Mounted(host));
            var next = Assert.IsType<Bitmap>(image.Source);
            Assert.Equal(new PixelSize(3, 3), next.PixelSize);
            Assert.Throws<ObjectDisposedException>(() => loaded.PixelSize);

            host.Unmount();
            Assert.Throws<ObjectDisposedException>(() => next.PixelSize);
        }
        finally { Delete(first, second); }
    }

    [AvaloniaFact]
    public void An_icon_whose_bitmap_cant_be_loaded_is_empty()
    {
        var missing = new Uri(Path.Combine(Path.GetTempPath(), "isotope-missing-" + Guid.NewGuid().ToString("N") + ".png"));
        Assert.Null(Assert.IsType<AvImage>(Mounted(new TestHost(() => Icon(ImageIcon(missing))))).Source);
        Assert.Null(Assert.IsType<BitmapIconPresenter>(Mounted(new TestHost(() => Icon(BitmapIcon(missing))))).Source);
    }

    // ── Kind changes ─────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_change_of_kind_mounts_a_new_control_and_unmounts_the_old_one()
    {
        var png = Png();
        try
        {
            IconData data = PathIcon(Triangle);
            var host = new TestHost(() => VStack(TextBlock("before"), Icon(data), TextBlock("after")));
            var panel = Assert.IsType<StackPanel>(host.Root.Child);
            Control Slot() => panel.Children[1];
            var path = Assert.IsType<AvPathIcon>(Slot());

            data = FontIcon("A");
            host.Render();
            var text = Assert.IsType<TextBlock>(Slot());
            Assert.Equal("A", text.Text);
            Assert.Null(path.Parent); // the path icon left the panel

            data = ImageIcon(png);
            host.Render();
            var image = Assert.IsType<AvImage>(Slot());
            var bitmap = Assert.IsType<Bitmap>(image.Source);

            data = BitmapIcon(png);
            host.Render();
            Assert.IsType<BitmapIconPresenter>(Slot());
            Assert.Throws<ObjectDisposedException>(() => bitmap.PixelSize); // the image unmounted

            data = PathIcon(Triangle);
            host.Render();
            Assert.NotSame(path, Assert.IsType<AvPathIcon>(Slot()));
            Assert.Equal(3, panel.Children.Count);
            Assert.Equal(("before", "after"), (((TextBlock)panel.Children[0]).Text, ((TextBlock)panel.Children[2]).Text));
        }
        finally { Delete(png); }
    }

    [AvaloniaFact]
    public void A_change_within_a_kind_keeps_the_control_and_its_modifiers()
    {
        IconData data = PathIcon(Triangle);
        var host = new TestHost(() => Icon(data).Width(40).Foreground(Brushes.Red));
        var icon = Assert.IsType<AvPathIcon>(Mounted(host));
        data = PathIcon("M 0,0 L 4,4 Z");
        host.Render();
        Assert.Same(icon, Mounted(host));
        Assert.Equal((40.0, (IBrush)Brushes.Red), (icon.Width, icon.Foreground!));

        data = FontIcon("A"); // a new control gets the modifiers too
        host.Render();
        var text = Assert.IsType<TextBlock>(Mounted(host));
        Assert.Equal((40.0, (IBrush)Brushes.Red), (text.Width, text.Foreground!));
    }

    // ── Sizing and foreground ────────────────────────────────────────

    [AvaloniaFact]
    public void A_path_icon_is_20_by_20_in_the_Fluent_theme_and_takes_the_size_modifiers()
    {
        var size = (double?)null;
        var host = new TestHost(() => HStack(size is { } s ? Icon(PathIcon(Triangle)).Width(s).Height(s) : Icon(PathIcon(Triangle))));
        var window = Show(host);
        var icon = host.All<AvPathIcon>().Single();
        Assert.Equal(new Size(20, 20), icon.Bounds.Size);

        size = 48;
        host.Render();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(icon, host.All<AvPathIcon>().Single());
        Assert.Equal(new Size(48, 48), icon.Bounds.Size);
        window.Close();
    }

    [AvaloniaFact]
    public void A_path_icon_is_filled_with_its_foreground()
    {
        IBrush brush = Brushes.Red;
        var host = new TestHost(() => HStack(Icon(PathIcon("M 0,0 L 10,0 L 10,10 L 0,10 Z")).Width(40).Height(40).Foreground(brush)));
        var window = Show(host);
        var icon = host.All<AvPathIcon>().Single();
        var centre = At(icon, window, 20, 20);
        Assert.Equal(Colors.Red, Pixel(window, centre.X, centre.Y));

        brush = Brushes.Blue;
        host.Render();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.Blue, Pixel(window, centre.X, centre.Y));
        window.Close();
    }

    [AvaloniaFact]
    public void A_font_icons_size_sets_its_glyphs_size_and_its_foreground_its_colour()
    {
        double? size = 20;
        var host = new TestHost(() => HStack(Icon(FontIcon("M", "Inter", size)).Foreground(Brushes.Green)));
        var window = Show(host);
        var text = host.All<TextBlock>().Single();
        var small = text.DesiredSize.Height;
        Assert.Equal((IBrush)Brushes.Green, text.Foreground);

        size = 40;
        host.Render();
        Dispatcher.UIThread.RunJobs();
        Assert.True(text.DesiredSize.Height > small * 1.8, $"{text.DesiredSize.Height} against {small}");
        window.Close();
    }

    [AvaloniaFact]
    public void A_monochrome_bitmap_icon_draws_its_shape_in_the_foreground_and_otherwise_its_colours()
    {
        var png = Png(4, 4); // red left half, transparent right half
        try
        {
            var mono = true;
            var host = new TestHost(() => HStack(Icon(BitmapIcon(png, mono)).Width(40).Height(40).Foreground(Brushes.Blue)));
            var window = Show(host);
            var icon = host.All<BitmapIconPresenter>().Single();
            Assert.Equal(new Size(40, 40), icon.Bounds.Size);
            var left = At(icon, window, 10, 20);
            var right = At(icon, window, 30, 20);
            Assert.Equal((Colors.Blue, Colors.White), (Pixel(window, left.X, left.Y), Pixel(window, right.X, right.Y)));

            mono = false;
            host.Render();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((Colors.Red, Colors.White), (Pixel(window, left.X, left.Y), Pixel(window, right.X, right.Y)));
            window.Close();
        }
        finally { Delete(png); }
    }

    [AvaloniaFact]
    public void A_bitmap_icon_with_no_size_takes_its_bitmaps_size_scaled_uniformly_into_its_space()
    {
        var png = Png(8, 4);
        try
        {
            var host = new TestHost(() => HStack(Icon(BitmapIcon(png)), Icon(BitmapIcon(png)).Height(10)));
            var window = Show(host, 200, 100);
            var icons = host.All<BitmapIconPresenter>();
            Assert.Equal(new Size(200, 100), window.ClientSize);
            Assert.Equal(new Size(200, 100), icons[0].DesiredSize); // 8 by 4, scaled up to the stack's height
            Assert.Equal(new Size(20, 10), icons[1].Bounds.Size);
            window.Close();
        }
        finally { Delete(png); }
    }

    [AvaloniaFact]
    public void The_guides_example_shows_a_glyph_then_a_shape_once_saved()
    {
        var host = new TestHost(() => Component(IconExamples.SaveButton));
        var button = host.All<Button>().Single();
        var tick = Assert.IsType<AvPathIcon>(button.Content);
        Assert.Equal(Colors.Gray, Assert.IsAssignableFrom<ISolidColorBrush>(tick.Foreground).Color);
        Assert.Equal("!", host.All<TextBlock>().Single().Text);
        Assert.Null(host.All<AvImage>().Single().Source); // the app's logo isn't in the tests

        TestHost.Click(button);
        host.Flush();
        Assert.Same(tick, button.Content);
        Assert.Equal(Colors.SeaGreen, Assert.IsAssignableFrom<ISolidColorBrush>(tick.Foreground).Color);
        Assert.Empty(host.All<TextBlock>());
        Assert.Equal(2, host.All<AvPathIcon>().Count);
    }

    private sealed record UnknownIconData : IconData;

    [AvaloniaFact]
    public void An_icon_of_data_it_doesnt_know_throws_and_Icon_takes_no_null()
    {
        var error = Assert.Throws<NotSupportedException>(() => new TestHost(() => new Isotope.UI.Core.IconElement(new UnknownIconData())));
        Assert.Contains("UnknownIconData", error.Message);
        Assert.Throws<ArgumentNullException>(() => Icon(null!));
    }

    [AvaloniaFact]
    public void An_icon_follows_a_theme_token_for_its_foreground()
    {
        // The token resolves as it does on a text block, and isn't the icon's own (inherited) foreground.
        var plain = new TestHost(() => HStack(Icon(PathIcon(Triangle))));
        var host = new TestHost(() => HStack(TextBlock("a").Foreground(Theme.Accent), Icon(PathIcon(Triangle)).Foreground(Theme.Accent)));
        var (w1, w2) = (Show(plain), Show(host));
        var icon = host.All<AvPathIcon>().Single();
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(host.All<TextBlock>().Single().Foreground).Color;
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color);
        Assert.NotEqual(expected, Assert.IsAssignableFrom<ISolidColorBrush>(plain.All<AvPathIcon>().Single().Foreground).Color);
        w1.Close();
        w2.Close();
    }
}
