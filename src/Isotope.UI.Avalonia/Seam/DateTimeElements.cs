using Avalonia.Controls;
using Isotope.UI.Core.V1Protocol.Descriptor;
using Isotope.UI.Wrappers;

namespace Isotope.UI.Core;

// The date and time pickers (#19). Records mirror upstream's (Reactor's member names, Avalonia's value types): Reactor's
// Date and Time are Avalonia's SelectedDate and SelectedTime, which are nullable on Avalonia. Avalonia raises their
// change events synchronously inside the write; upstream's Controlled entry arms each value it writes and drops the
// event that reads it back, so the engine's own writes don't echo.

/// <summary>
/// A date picker, as an Avalonia <see cref="Avalonia.Controls.DatePicker"/>. <see cref="Date"/> defaults to
/// <see cref="Optional{T}.Unset"/>, so the control owns its date until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(DatePicker), ClearValueOnUnset = true)]
[WrapManual("Date")]
[WrapManual("MinYear")]
[WrapManual("MaxYear")]
public partial record DatePickerElement(
    Optional<DateTimeOffset> Date = default,
    Action<DateTimeOffset>? OnDateChanged = null) : Element
{
    /// <summary>The earliest year offered; null keeps Avalonia's (a hundred years back).</summary>
    public DateTimeOffset? MinYear { get; init; }

    /// <summary>The latest year offered; null keeps Avalonia's (a hundred years ahead).</summary>
    public DateTimeOffset? MaxYear { get; init; }
    public bool DayVisible { get; init; } = true;
    public bool MonthVisible { get; init; } = true;
    public bool YearVisible { get; init; } = true;

    /// <summary>The day column's format; null keeps Avalonia's.</summary>
    public string? DayFormat { get; init; }

    /// <summary>The month column's format; null keeps Avalonia's.</summary>
    public string? MonthFormat { get; init; }

    /// <summary>The year column's format; null keeps Avalonia's.</summary>
    public string? YearFormat { get; init; }

    internal Action<DatePicker>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnDateChanged is not null;

    // The year bounds Avalonia's constructor gives a picker, which clearing the property wouldn't restore (its default
    // is the whole DateTimeOffset range). Read once, from a picker of our own, on the UI thread.
    private static DatePicker? s_defaults;
    private static DatePicker Defaults => s_defaults ??= new DatePicker();

    // The year bounds are written before the date, so a first date lands in its range; a null bound writes the
    // constructor's. The control's value is nullable and the record's isn't: a cleared date isn't reported (the
    // callback takes a date).
    private static partial ControlDescriptor<DatePickerElement, DatePicker> Customize(ControlDescriptor<DatePickerElement, DatePicker> d)
        => d.OneWay(
                get: static e => e.MinYear ?? Defaults.MinYear,
                set: static (c, v) => c.MinYear = v)
            .OneWay(
                get: static e => e.MaxYear ?? Defaults.MaxYear,
                set: static (c, v) => c.MaxYear = v)
            .Controlled<DateTimeOffset?, DatePickerSelectedValueChangedEventArgs>(
            get:         static e => e.Date.HasValue ? e.Date.Value : Optional<DateTimeOffset?>.Unset,
            set:         static (c, v) => c.SelectedDate = v,
            subscribe:   static (fe, h) => ((DatePicker)fe).SelectedDateChanged += h,
            unsubscribe: static (fe, h) => { },
            callback:    static e => e.OnDateChanged is { } cb ? v => { if (v.HasValue) cb(v.Value); } : null,
            readBack:    static c => c.SelectedDate);
}

/// <summary>
/// A time picker, as an Avalonia <see cref="Avalonia.Controls.TimePicker"/>. <see cref="Time"/> defaults to
/// <see cref="Optional{T}.Unset"/>, so the control owns its time until one is given.
/// </summary>
[GenerateReactorDescriptor(typeof(TimePicker), ClearValueOnUnset = true)]
[WrapManual("Time")]
[WrapManual("ClockIdentifier")]
public partial record TimePickerElement(
    Optional<TimeSpan> Time = default,
    Action<TimeSpan>? OnTimeChanged = null) : Element
{
    public int MinuteIncrement { get; init; } = 1;

    /// <summary>12 (the default) or 24, as upstream's; Avalonia's <c>"12HourClock"</c> or <c>"24HourClock"</c>.</summary>
    public int ClockIdentifier { get; init; } = 12;

    internal Action<TimePicker>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnTimeChanged is not null;

    // ClockIdentifier is an int on the record and a string on Avalonia's control (upstream leaves it unmapped, because
    // WinUI's is a string too; here it is converted rather than dropped). The time is nullable on the control.
    private static partial ControlDescriptor<TimePickerElement, TimePicker> Customize(ControlDescriptor<TimePickerElement, TimePicker> d)
        => d.OneWay(
                get: static e => e.ClockIdentifier == 24 ? "24HourClock" : "12HourClock",
                set: static (c, v) => c.ClockIdentifier = v)
            .Controlled<TimeSpan?, TimePickerSelectedValueChangedEventArgs>(
                get:         static e => e.Time.HasValue ? e.Time.Value : Optional<TimeSpan?>.Unset,
                set:         static (c, v) => c.SelectedTime = v,
                subscribe:   static (fe, h) => ((TimePicker)fe).SelectedTimeChanged += h,
                unsubscribe: static (fe, h) => { },
                callback:    static e => e.OnTimeChanged is { } cb ? v => { if (v.HasValue) cb(v.Value); } : null,
                readBack:    static c => c.SelectedTime);
}
