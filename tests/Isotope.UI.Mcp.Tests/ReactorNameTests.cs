using Isotope.UI.Migration;
using ModelContextProtocol;
using Xunit;

namespace Isotope.UI.Mcp.Tests;

/// <summary>map_reactor_name, from the migration's embedded tables (the rename table and its own two).</summary>
public class ReactorNameTests
{
    static MigrationTables Tables => MigrationTables.Default;

    [Theory]
    [InlineData("TabView", "Factory: `TabView` is `TabControl` in Isotope.")]
    [InlineData("HAlign", "Modifier: `HAlign` is `HorizontalAlignment` in Isotope.")]
    [InlineData("TabViewElement", "Type: `TabViewElement` is `TabControlElement` in Isotope.")]
    [InlineData("TextBlockElement.Content", "Member: `TextBlockElement.Content` is `TextBlockElement.Text` in Isotope.")]
    [InlineData("label", "Factory parameter: `Button(label: …)` is `Button(content: …)` in Isotope.")]
    [InlineData("VStack", "Factory: `VStack` keeps Reactor's name in Isotope.")]
    [InlineData("GridDefinition", "Type: `GridDefinition` has no counterpart in Isotope. Removed with GridElement.Definition")]
    [InlineData("ProgressRing", "Factory: `ProgressRing` is a WinUI control with no Isotope counterpart. Avalonia's core has no ProgressRing: use ProgressIndeterminate()")]
    [InlineData("RelativePanel", "Factory: `RelativePanel` has no Isotope factory yet.")]
    [InlineData("Microsoft.UI.Xaml.Thickness", "WinUI type: `Microsoft.UI.Xaml.Thickness` is `Avalonia.Thickness` in Avalonia.")]
    [InlineData("Visibility", "WinUI type: `Microsoft.UI.Xaml.Visibility` has no Avalonia counterpart.")]
    [InlineData("Microsoft.UI.Reactor.Core", "`Microsoft.UI.Reactor.Core` is `Isotope.UI.Core`")]
    [InlineData("Microsoft.UI.Reactor.Factories.ScrollView", "Factory: `ScrollView` is `ScrollViewer` in Isotope.")]
    [InlineData("SpinButtons", "Modifier: `SpinButtons` has no counterpart in Isotope. No modifier: Avalonia's NumericUpDown shows its spinner by default")]
    [InlineData("NumberBoxElement.Range", "Modifier: `Range` has no counterpart in Isotope. No modifier: set the record's Minimum and Maximum")]
    [InlineData("NumberBox", "It has no header (NumberBox's third argument): put a TextBlock label beside it")]
    [InlineData("ScrollView", "The migration writes `(ScrollViewer(...) with { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled })`")]
    public void A_Reactor_name_maps_to_Isotopes(string reactor, string expected) =>
        Assert.Contains(expected, IsotopeTools.MapReactorName(reactor));

    [Fact]
    public void A_name_the_table_does_not_rename_is_Isotopes_too()
    {
        var text = IsotopeTools.MapReactorName("UseState");
        Assert.Contains("Same name: the rename table has no entry for `UseState`, so Isotope keeps Reactor's name.", text);
        Assert.Contains("hook `(T Value, Action<T> Set) RenderContext.UseState<T>(", text);
        Assert.DoesNotContain("Same name", IsotopeTools.MapReactorName("TabView"));
    }

    [Fact]
    public void A_record_member_the_table_does_not_rename_is_found_on_its_record()
    {
        var text = IsotopeTools.MapReactorName("ButtonElement.OnClick");
        Assert.Contains("Same name: the rename table has no entry for `ButtonElement.OnClick`", text);
        Assert.Contains("member `Action? ButtonElement.OnClick { get; init; }`", text);
    }

    [Fact]
    public void A_record_member_neither_has_is_not_answered_with_another_records_member()
    {
        // SliderElement has no OnClick; ButtonElement's is another record's.
        var slider = IsotopeTools.MapReactorName("SliderElement.OnClick");
        Assert.Contains("Element record: `SliderElement` keeps its name in Isotope, and has no `OnClick`", slider);
        Assert.DoesNotContain("ButtonElement", slider);
        // NumberBoxElement has no Header; ExpanderElement's and ToggleSwitchElement's are other records' (#68).
        var header = IsotopeTools.MapReactorName("NumberBoxElement.Header");
        Assert.Contains("Element record: `NumberBoxElement` is `NumericUpDownElement` in Isotope, and has no `Header`", header);
        Assert.DoesNotContain("ExpanderElement", header);
        Assert.DoesNotContain("ToggleSwitchElement", header);
        // A member Isotope's renamed record has under Reactor's name is found on it.
        Assert.Contains("member `double NumericUpDownElement.Minimum", IsotopeTools.MapReactorName("NumericUpDownElement.Minimum"));
        // The last part alone still answers a factory or a type: Factories.Button is Button.
        Assert.Contains("factory `static ButtonElement Factories.Button(", IsotopeTools.MapReactorName("Factories.Button"));
    }

    [Fact]
    public void A_renamed_parameter_is_not_also_reported_as_a_same_named_modifier()
    {
        // "header" is ToggleSwitch's parameter, renamed; the Header modifier is another name.
        var text = IsotopeTools.MapReactorName("header");
        Assert.Contains("Factory parameter: `ToggleSwitch(header: …)`", text);
        Assert.DoesNotContain("Same name", text);
    }

    [Fact]
    public void An_unknown_name_says_so()
    {
        Assert.StartsWith("`NoSuchReactorThing` is not in the rename table", IsotopeTools.MapReactorName("NoSuchReactorThing"));
        Assert.Throws<McpException>(() => IsotopeTools.MapReactorName(" "));
    }

    // Every entry of the tables is answered, so the tool reads them rather than a copy.
    [Fact]
    public void Every_renamed_type_factory_and_modifier_maps_to_its_Isotope_name()
    {
        var entries = Tables.Renames.Types.Select(e => ("Type", e)).Concat(Tables.Renames.Factories.Select(e => ("Factory", e)))
            .Concat(Tables.Renames.Modifiers.Select(e => ("Modifier", e))).ToList();
        Assert.True(entries.Count > 40, entries.Count + " entries");
        foreach (var (what, e) in entries)
        {
            var text = IsotopeTools.MapReactorName(e.Reactor);
            var target = e.Target is null ? "" : $" (on `{e.Target}`)";
            var expected = e.Avalonia is null ? $"{what}: `{e.Reactor}` has no counterpart in Isotope."
                : e.Status == "kept" ? $"{what}: `{e.Reactor}` keeps Reactor's name in Isotope."
                : $"{what}: `{e.Reactor}`{target} is `{e.Avalonia}` in Isotope";
            Assert.True(text.Contains(expected, StringComparison.Ordinal), e.Reactor + ": expected\n" + expected + "\nin\n" + text);
        }
    }

    [Fact]
    public void Every_member_and_control_entry_is_answered()
    {
        foreach (var m in Tables.Renames.Members)
            Assert.Contains($"Member: `{m.Record}.{m.Reactor}`", IsotopeTools.MapReactorName(m.Record + "." + m.Reactor));
        foreach (var c in Tables.Controls.Factories.Values)
            Assert.Contains($"Factory: `{c.Factory}`", IsotopeTools.MapReactorName(c.Factory));
        foreach (var t in Tables.WinUI.Types)
            Assert.Contains($"WinUI type: `{t.FullName}`", IsotopeTools.MapReactorName(t.FullName));
    }
}
