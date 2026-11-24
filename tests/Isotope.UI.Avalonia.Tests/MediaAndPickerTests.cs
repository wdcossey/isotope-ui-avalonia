using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// ProgressBar, Image, DatePicker and TimePicker (#19): mount and update in place, ProgressBar's one-way value, the image
/// source loaded as a bitmap, and the pickers' controlled values, whose engine writes don't echo.
/// </summary>
public class MediaAndPickerTests
{
    [AvaloniaFact]
    public void ProgressBar_writes_its_value_after_its_range_and_null_is_indeterminate()
    {
        double? value = 150;
        var host = new TestHost(() => (value is { } v ? ProgressBar(v) : ProgressIndeterminate()) with { Maximum = 200 });
        var bar = host.All<ProgressBar>()[0];
        Assert.Equal((150.0, 200.0, false), (bar.Value, bar.Maximum, bar.IsIndeterminate));

        value = 60;
        host.Render();
        Assert.Same(bar, host.All<ProgressBar>()[0]);
        Assert.Equal(60, bar.Value);

        value = null;
        host.Render();
        Assert.True(bar.IsIndeterminate);
    }

    [AvaloniaFact]
    public void Image_loads_a_file_path_or_a_file_uri_and_an_unreadable_source_leaves_it_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), "isotope-image-" + Guid.NewGuid().ToString("N") + ".png");
        using (var bitmap = new WriteableBitmap(new PixelSize(3, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            bitmap.Save(path, new PngBitmapEncoderOptions());
        try
        {
            var source = path;
            var host = new TestHost(() => Image(source) with { Stretch = Stretch.Fill });
            var image = host.All<Image>()[0];
            Assert.Equal(new PixelSize(3, 2), Assert.IsType<Bitmap>(image.Source).PixelSize);
            Assert.Equal(Stretch.Fill, image.Stretch);

            source = new Uri(path).AbsoluteUri;
            host.Render();
            Assert.Same(image, host.All<Image>()[0]);
            Assert.Equal(new PixelSize(3, 2), Assert.IsType<Bitmap>(image.Source).PixelSize);

            source = Path.Combine(Path.GetTempPath(), "isotope-missing-" + Guid.NewGuid().ToString("N") + ".png");
            host.Render(); // doesn't throw: the image is left empty, as upstream's
            Assert.Null(image.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void Image_disposes_the_bitmap_it_loaded_when_the_source_changes_or_it_unmounts_and_keeps_odd_file_names()
    {
        // '%20' and '#' are a plain file name's characters, not a URI's escape and fragment.
        var dir = Path.GetTempPath();
        var first = Path.Combine(dir, "isotope image%20" + Guid.NewGuid().ToString("N") + "#1.png");
        var second = Path.Combine(dir, "isotope-image-" + Guid.NewGuid().ToString("N") + ".png");
        foreach (var path in new[] { first, second })
            using (var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
                bitmap.Save(path, new PngBitmapEncoderOptions());
        try
        {
            var source = first;
            var host = new TestHost(() => Image(source));
            var image = host.All<Image>()[0];
            var loaded = Assert.IsType<Bitmap>(image.Source);
            Assert.Equal(new PixelSize(2, 2), loaded.PixelSize);

            source = second;
            host.Render();
            Assert.Throws<ObjectDisposedException>(() => loaded.PixelSize);
            var next = Assert.IsType<Bitmap>(image.Source);

            host.Unmount();
            Assert.Throws<ObjectDisposedException>(() => next.PixelSize);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [AvaloniaFact]
    public void Image_stretch_set_back_to_null_returns_to_the_default()
    {
        var set = true;
        var host = new TestHost(() => set ? Image("none.png") with { Stretch = Stretch.None } : Image("none.png"));
        var image = host.All<Image>()[0];
        Assert.Equal(Stretch.None, image.Stretch);

        set = false;
        host.Render();
        Assert.Equal(new Image().Stretch, image.Stretch);
    }

    private static readonly DateTimeOffset Day1 = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public void DatePicker_user_change_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<DateTimeOffset>();
        Action<DateTimeOffset>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (date, setDate) = ctx.UseState(Day1);
            set = setDate;
            return VStack(TextBlock("state " + date.Month), DatePicker(date, v => { reported.Add(v); setDate(v); }) with { DayVisible = false });
        }));
        var picker = host.All<DatePicker>()[0];
        Assert.Equal((Day1, false), (picker.SelectedDate, picker.DayVisible));

        picker.SelectedDate = Day2; // as the picker's flyout does
        host.Flush();
        Assert.Equal([Day2], reported);
        Assert.Equal(["state 3"], host.Texts());

        set!(Day1);
        host.Flush();
        Assert.Equal(Day1, picker.SelectedDate);
        Assert.Equal([Day2], reported);

        picker.SelectedDate = null; // a cleared date isn't reported: the callback takes a date
        Assert.Equal([Day2], reported);
        Assert.Same(picker, host.All<DatePicker>()[0]);
    }

    [AvaloniaFact]
    public void DatePicker_year_bounds_set_back_to_null_return_to_the_defaults()
    {
        var set = true;
        var host = new TestHost(() => set ? DatePicker() with { MinYear = Day1, MaxYear = Day2, YearFormat = "yy" } : DatePicker());
        var picker = host.All<DatePicker>()[0];
        Assert.Equal((Day1, Day2, "yy"), (picker.MinYear, picker.MaxYear, picker.YearFormat));
        Assert.Null(picker.SelectedDate); // no date given: the control owns it

        set = false;
        host.Render();
        var defaults = new DatePicker();
        Assert.Equal((defaults.MinYear, defaults.MaxYear, defaults.YearFormat), (picker.MinYear, picker.MaxYear, picker.YearFormat));
    }

    [AvaloniaFact]
    public void DatePicker_year_range_moved_past_the_current_one_is_written_in_a_valid_order()
    {
        // Avalonia throws for a MinYear after the current MaxYear (or a MaxYear before the current MinYear).
        var later = true;
        static DateTimeOffset Year(int y) => new(y, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var host = new TestHost(() => later
            ? DatePicker() with { MinYear = Year(2200), MaxYear = Year(2300) }
            : DatePicker() with { MinYear = Year(1700), MaxYear = Year(1800) });
        var picker = host.All<DatePicker>()[0];
        Assert.Equal((Year(2200), Year(2300)), (picker.MinYear, picker.MaxYear));

        later = false;
        host.Render();
        Assert.Equal((Year(1700), Year(1800)), (picker.MinYear, picker.MaxYear));
    }

    [AvaloniaFact]
    public void TimePicker_user_change_reaches_the_state_an_engine_write_does_not_echo_and_the_clock_maps()
    {
        var reported = new List<TimeSpan>();
        Action<TimeSpan>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (time, setTime) = ctx.UseState(TimeSpan.FromHours(9));
            set = setTime;
            return VStack(TextBlock("state " + time.Hours),
                TimePicker(time, v => { reported.Add(v); setTime(v); }) with { ClockIdentifier = 24, MinuteIncrement = 15 });
        }));
        var picker = host.All<TimePicker>()[0];
        Assert.Equal((TimeSpan.FromHours(9), "24HourClock", 15), (picker.SelectedTime, picker.ClockIdentifier, picker.MinuteIncrement));

        picker.SelectedTime = TimeSpan.FromHours(17);
        host.Flush();
        Assert.Equal([TimeSpan.FromHours(17)], reported);
        Assert.Equal(["state 17"], host.Texts());

        set!(TimeSpan.FromHours(8));
        host.Flush();
        Assert.Equal(TimeSpan.FromHours(8), picker.SelectedTime);
        Assert.Equal([TimeSpan.FromHours(17)], reported);
        Assert.Equal("12HourClock", new TestHost(() => TimePicker()).All<TimePicker>()[0].ClockIdentifier);
    }
}
