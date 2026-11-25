using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>ISOMIG006 to ISOMIG010: the rename table (tools/naming/renames.json).</summary>
public class RenameTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void Renamed_members_in_with_and_object_initializers_and_member_access_take_Avalonia_names()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render(TextBoxElement box) =>
                [
                    TextBox() with { Value = "typed", OnChanged = _ => { } },
                    new SliderElement(Value: 3) { Min = 1, Max = 5 },
                    TextBlock(box.Value.ToString()!),
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG006: 'TextBoxElement.Value' is 'Text' in Isotope",
            "ISOMIG006: 'TextBoxElement.OnChanged' is 'OnTextChanged' in Isotope",
            "ISOMIG006: 'SliderElement.Min' is 'Minimum' in Isotope",
            "ISOMIG006: 'SliderElement.Max' is 'Maximum' in Isotope",
            "ISOMIG006: 'TextBoxElement.Value' is 'Text' in Isotope",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("TextBox() with { Text = \"typed\", OnTextChanged = _ => { } }", fixedText);
        Assert.Contains("{ Minimum = 1, Maximum = 5 }", fixedText);
        Assert.Contains("box.Text.ToString()", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void A_shape_change_is_renamed_where_the_value_fits_and_reported_where_it_does_not()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render(Action<bool> onChecked) =>
                [
                    Button("Go") with { Label = "Stop" },
                    Button("Go") with { ContentElement = TextBlock("Stop") },
                    ToggleButton("Bold") with { OnIsCheckedChanged = onChecked },
                    ToggleButton("Bold") with { IsChecked = true },
                    new GridElement([], [], []) { Definition = null! },
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG006: 'ButtonElement.Label' is 'Content' in Isotope",
            "ISOMIG006: 'ButtonElement.ContentElement' is 'Content' in Isotope",
            "ISOMIG007: 'ToggleButtonElement.OnIsCheckedChanged' changed shape in Isotope: Now an Action<bool?> on the record (it was Action<bool>); the ToggleButton factory adapts an Action<bool>.",
            "ISOMIG007: 'GridElement.Definition' changed shape in Isotope: GridDefinition(Columns, Rows) is two GridSize[] members, as Avalonia's ColumnDefinitions and RowDefinitions; GridDefinition is gone (see types).",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("Button(\"Go\") with { Content = \"Stop\" }", fixedText);
        Assert.Contains("with { Content = TextBlock(\"Stop\") }", fixedText);
        Assert.Contains("OnIsCheckedChanged = onChecked", fixedText);
    }

    [Fact]
    public void Two_members_that_become_one_are_reported_rather_than_renamed_twice()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => Button("Go") with { Label = "Save", ContentElement = TextBlock("Save") };
            }
            """;
        Assert.Equal([
            "ISOMIG007: 'ButtonElement.Label' changed shape in Isotope: it and another member here are both Content in Isotope: keep one.",
            "ISOMIG007: 'ButtonElement.ContentElement' changed shape in Isotope: it and another member here are both Content in Isotope: keep one.",
        ], Migrate.Analyze(reactor).Describe());
        Assert.Equal(reactor, Migrate.Fix(reactor));
    }

    [Fact]
    public void Record_constructor_arguments_and_null_conditional_reads_are_renamed()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element Render(TextBoxElement? box) =>
                    new TextBoxElement(Value: "typed", OnChanged: _ => { }) { PlaceholderText = box?.Value.ToString() };
            }
            """;
        Assert.Equal([
            "ISOMIG006: 'TextBoxElement.Value' is 'Text' in Isotope",
            "ISOMIG006: 'TextBoxElement.OnChanged' is 'OnTextChanged' in Isotope",
            "ISOMIG006: 'TextBoxElement.Value' is 'Text' in Isotope",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("new TextBoxElement(Text: \"typed\", OnTextChanged: _ => { }) { PlaceholderText = box?.Text.ToString() }", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void NumberBox_modifiers_with_no_counterpart_are_reported_with_what_to_set_instead()
    {
        // #68: a fresh agent found .Range and .SpinButtons unexplained. They are record members in Isotope.
        const string reactor = """
            using Microsoft.UI.Reactor;
            using static Microsoft.UI.Reactor.Factories;
            static class C
            {
                public static Element A(double v) => NumberBox(v, _ => { }).Range(0, 100).SpinButtons();
                public static Element B(double v) => NumberBox(v, _ => { }).Range(0, 100).NoSuchThing();
                public static object E() => new System.Text.StringBuilder().Missing().HAlign(1);
            }
            """;
        // As migrate_code reports them: what is left once the fixes ran (the factory renamed, so the receiver resolves).
        var described = Migrate.Analyze(Migrate.Fix(reactor)).Describe();
        Assert.Contains(described, d => d.StartsWith("ISOMIG010: Modifier 'Range' is gone in Isotope: No modifier: set the record's Minimum and Maximum", StringComparison.Ordinal));
        Assert.Contains(described, d => d.StartsWith("ISOMIG010: Modifier 'SpinButtons' is gone in Isotope: No modifier: Avalonia's NumericUpDown shows its spinner", StringComparison.Ordinal));
        // Past an unresolved link only the table's modifiers are named: an unknown name is left to the compiler.
        Assert.DoesNotContain(described, d => d.Contains("NoSuchThing", StringComparison.Ordinal));
        // Only an element's chain: HAlign on a StringBuilder's is no modifier of Isotope's.
        Assert.DoesNotContain(described, d => d.Contains("HAlign", StringComparison.Ordinal));
        Assert.Equal(2, described.Count(d => d.Contains("Modifier 'Range'", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_factory_parameter_with_no_counterpart_is_reported_named_or_positional()
    {
        // #68: NumberBox(value, onValueChanged, header) left a header NumericUpDown has no place for, as CS1501 only.
        const string reactor = """
            using Microsoft.UI.Reactor;
            using static Microsoft.UI.Reactor.Factories;
            static class C
            {
                public static Element A(double v) => NumberBox(v, _ => { }, "Enter a number");
                public static Element B(double v) => NumberBox(v, _ => { }, header: "Percentage");
                public static Element D(double v) => NumberBox(v, _ => { });
            }
            """;
        var described = Migrate.Analyze(Migrate.Fix(reactor)).Describe();
        Assert.Equal(2, described.Count(d => d.StartsWith("ISOMIG010: Parameter 'NumberBox(header: …)' is gone in Isotope: Avalonia's NumericUpDown has no header", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_qualified_type_the_table_renamed_or_removed_is_renamed_or_reported()
    {
        var tables = Migrate.WithRenames(t =>
            t.Types.Add(new NameEntry { Reactor = "ScrollViewElement", Avalonia = "ScrollViewerElement", Status = "applied" }));
        const string reactor = """
            static class C
            {
                public static Microsoft.UI.Reactor.ScrollViewElement? A;
                public static Isotope.UI.ScrollViewElement? B;
                public static Microsoft.UI.Reactor.GridDefinition? D;
            }
            """;
        Assert.Equal([
            "ISOMIG009: Type 'Microsoft.UI.Reactor.ScrollViewElement' is 'Isotope.UI.ScrollViewerElement' in Isotope",
            "ISOMIG009: Type 'ScrollViewElement' is 'ScrollViewerElement' in Isotope",
            "ISOMIG010: Type 'Microsoft.UI.Reactor.GridDefinition' is gone in Isotope: Removed with GridElement.Definition: pass the GridSize[] columns and rows to GridElement or Grid(...); strings become tracks through GridSize.Parse (Array.ConvertAll(columns, GridSize.Parse)).",
        ], Migrate.Analyze(reactor, tables).Describe());
        var fixedText = Migrate.Fix(reactor, tables);
        Assert.Contains("public static Isotope.UI.ScrollViewerElement? A;", fixedText);
        Assert.Contains("public static Isotope.UI.ScrollViewerElement? B;", fixedText);
    }

    [Fact]
    public void A_modifier_entry_follows_its_record_when_the_table_renames_the_record()
    {
        // ToolTip's target record (Reactor's name) renamed in the table to Isotope's TextBlockElement.
        var tables = Migrate.WithRenames(t =>
        {
            t.Types.Add(new NameEntry { Reactor = "LabelElement", Avalonia = "TextBlockElement", Status = "applied" });
            t.Modifiers.Add(new NameEntry { Reactor = "Hint", Avalonia = "ToolTip", Target = "LabelElement", Status = "applied" });
        });
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => TextBlock("x").Hint("y");
            }
            """;
        Assert.Equal(["ISOMIG009: Modifier 'Hint' is 'ToolTip' in Isotope"], Migrate.Analyze(reactor, tables).Describe());
        Assert.Empty(Migrate.Errors(Migrate.Fix(reactor, tables)));
    }

    [Fact]
    public void Record_constructor_arguments_with_a_shape_change_are_renamed_where_they_fit()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render() =>
                [
                    new ButtonElement(Label: "Save"),
                    new GridElement(Definition: null!, RowDefinitions: [], Children: []),
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG006: 'ButtonElement.Label' is 'Content' in Isotope",
            "ISOMIG007: 'GridElement.Definition' changed shape in Isotope: GridDefinition(Columns, Rows) is two GridSize[] members, as Avalonia's ColumnDefinitions and RowDefinitions; GridDefinition is gone (see types).",
        ], Migrate.Analyze(reactor).Describe());
        Assert.Contains("new ButtonElement(Content: \"Save\")", Migrate.Fix(reactor));
    }

    [Fact]
    public void A_null_conditional_modifier_call_is_renamed()
    {
        var tables = Migrate.WithRenames(t =>
            t.Modifiers.Add(new NameEntry { Reactor = "Margins", Avalonia = "Margin", Status = "applied" }));
        const string reactor = Usings + """
            static class C
            {
                public static Element? Render(TextBlockElement? text) => text?.Margins(4);
            }
            """;
        Assert.Equal(["ISOMIG009: Modifier 'Margins' is 'Margin' in Isotope"], Migrate.Analyze(reactor, tables).Describe());
        Assert.Empty(Migrate.Errors(Migrate.Fix(reactor, tables)));
    }

    [Fact]
    public void A_parameter_whose_new_name_is_already_given_is_left_to_the_compiler()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render() =>
                [
                    TextBox(value: "x", text: "y"),
                    new TextBoxElement(Value: "x", Text: "y"),
                ];
            }
            """;
        Assert.Empty(Migrate.Analyze(reactor));
    }

    [Fact]
    public void Kept_members_are_not_reported()
    {
        const string code = Usings + """
            static class C
            {
                public static Element Render() => AutoCompleteBox("") with { OnQuerySubmitted = _ => { } };
            }
            """;
        Assert.Empty(Migrate.Errors(code));
        Assert.Empty(Migrate.Analyze(code));
    }

    [Fact]
    public void Renamed_factory_parameters_take_Avalonia_names()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render() =>
                [
                    TextBox(value: "x", onChanged: _ => { }),
                    Slider(value: 2, min: 0, max: 10),
                    Factories.Button(label: "Go", onClick: () => { }),
                    Grid(columns: [GridSize.Auto], rows: [GridSize.Auto], TextBlock("cell")),
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG008: Parameter 'value' of 'TextBox' is 'text' in Isotope",
            "ISOMIG008: Parameter 'onChanged' of 'TextBox' is 'onTextChanged' in Isotope",
            "ISOMIG008: Parameter 'min' of 'Slider' is 'minimum' in Isotope",
            "ISOMIG008: Parameter 'max' of 'Slider' is 'maximum' in Isotope",
            "ISOMIG008: Parameter 'label' of 'Button' is 'content' in Isotope",
            "ISOMIG008: Parameter 'columns' of 'Grid' is 'columnDefinitions' in Isotope",
            "ISOMIG008: Parameter 'rows' of 'Grid' is 'rowDefinitions' in Isotope",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("TextBox(text: \"x\", onTextChanged: _ => { })", fixedText);
        Assert.Contains("Slider(value: 2, minimum: 0, maximum: 10)", fixedText);
        Assert.Contains("Factories.Button(content: \"Go\", onClick: () => { })", fixedText);
        Assert.Contains("Grid(columnDefinitions: [GridSize.Auto], rowDefinitions: [GridSize.Auto]", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void Reactor_type_factory_and_modifier_names_take_the_Avalonia_names_of_the_table()
    {
        // The table's applied renames (#46): Reactor's names become Avalonia's, and the kept ones (VStack, MenuBar, Menu)
        // are left alone. With `using System;`, Reactor's Progress(0.5) names System.Progress<T> in an Isotope project.
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render(NumberBoxElement number, StackElement stack) =>
                [
                    TabView(0, null, Tab("A", NumberBox(1)), Tab("B", AutoSuggestBox("x"))),
                    Progress(0.5),
                    VStack(number, stack),
                    MenuBar(Menu("File", MenuItem("Open"))),
                ];
                public static TabViewElement? Tabs;
            }
            """;
        Assert.Equal([
            "ISOMIG009: Type 'NumberBoxElement' is 'NumericUpDownElement' in Isotope",
            "ISOMIG009: Type 'StackElement' is 'StackPanelElement' in Isotope",
            "ISOMIG009: Factory 'TabView' is 'TabControl' in Isotope",
            "ISOMIG009: Factory 'Tab' is 'TabItem' in Isotope",
            "ISOMIG009: Factory 'NumberBox' is 'NumericUpDown' in Isotope",
            "ISOMIG009: Factory 'Tab' is 'TabItem' in Isotope",
            "ISOMIG009: Factory 'AutoSuggestBox' is 'AutoCompleteBox' in Isotope",
            "ISOMIG009: Factory 'Progress' is 'ProgressBar' in Isotope",
            "ISOMIG009: Type 'TabViewElement' is 'TabControlElement' in Isotope",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("Render(NumericUpDownElement number, StackPanelElement stack)", fixedText);
        Assert.Contains("TabControl(0, null, TabItem(\"A\", NumericUpDown(1)), TabItem(\"B\", AutoCompleteBox(\"x\")))", fixedText);
        Assert.Contains("ProgressBar(0.5)", fixedText);
        Assert.Contains("VStack(number, stack)", fixedText);
        Assert.Contains("MenuBar(Menu(\"File\", MenuItem(\"Open\")))", fixedText);
        Assert.Contains("public static TabControlElement? Tabs;", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void Reactor_modifier_names_take_the_Avalonia_names_of_the_table()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render() =>
                [
                    TextBlock("a").HAlign(Avalonia.Layout.HorizontalAlignment.Left),
                    TextBlock("b").VAlign(Avalonia.Layout.VerticalAlignment.Top),
                    TextBlock("c").WithContextFlyout(MenuItems(MenuItem("Copy"))),
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG009: Modifier 'HAlign' is 'HorizontalAlignment' in Isotope",
            "ISOMIG009: Modifier 'VAlign' is 'VerticalAlignment' in Isotope",
            "ISOMIG009: Modifier 'WithContextFlyout' is 'ContextFlyout' in Isotope",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains(".HorizontalAlignment(Avalonia.Layout.HorizontalAlignment.Left)", fixedText);
        Assert.Contains(".VerticalAlignment(Avalonia.Layout.VerticalAlignment.Top)", fixedText);
        Assert.Contains(".ContextFlyout(MenuItems(MenuItem(\"Copy\")))", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void Applied_type_factory_and_modifier_renames_are_switched_by_the_table()
    {
        // Entries Isotope's code doesn't have yet, applied in a copy of the table: Reactor's names become the
        // table's, so the switch is the table's status, not the analyzer's code.
        var tables = Migrate.WithRenames(t =>
        {
            t.Factories.Add(new NameEntry { Reactor = "WrapGrid", Avalonia = "WrapPanel", Status = "applied" });
            t.Types.Add(new NameEntry { Reactor = "WrapGridElement", Avalonia = "WrapPanelElement", Status = "applied" });
            t.Modifiers.Add(new NameEntry { Reactor = "Margins", Avalonia = "Margin", Status = "applied" });
        });
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => WrapGrid(TextBlock("x").Margins(4));
                public static WrapGridElement? Last;
            }
            """;
        Assert.Equal([
            "ISOMIG009: Factory 'WrapGrid' is 'WrapPanel' in Isotope",
            "ISOMIG009: Modifier 'Margins' is 'Margin' in Isotope",
            "ISOMIG009: Type 'WrapGridElement' is 'WrapPanelElement' in Isotope",
        ], Migrate.Analyze(reactor, tables).Describe());
        var fixedText = Migrate.Fix(reactor, tables);
        Assert.Contains("WrapPanel(TextBlock(\"x\").Margin(4))", fixedText);
        Assert.Contains("public static WrapPanelElement? Last;", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));

        // The same entries as proposed: nothing renames.
        var proposed = Migrate.WithRenames(t =>
        {
            t.Factories.Add(new NameEntry { Reactor = "WrapGrid", Avalonia = "WrapPanel", Status = "proposed" });
            t.Types.Add(new NameEntry { Reactor = "WrapGridElement", Avalonia = "WrapPanelElement", Status = "proposed" });
            t.Modifiers.Add(new NameEntry { Reactor = "Margins", Avalonia = "Margin", Status = "proposed" });
        });
        Assert.DoesNotContain(Migrate.Analyze(reactor, proposed), d => d.Id == "ISOMIG009");
    }

    [Fact]
    public void A_record_renamed_by_the_table_keeps_its_member_entries()
    {
        // A member entry recorded under Reactor's record name still applies to the renamed record: here a made-up
        // Reactor record stands for Isotope's StackPanelElement.
        var tables = Migrate.WithRenames(t =>
        {
            t.Types.Add(new NameEntry { Reactor = "PanelOfStack", Avalonia = "StackPanelElement", Status = "applied" });
            t.Members.Add(new MemberEntry { Record = "PanelOfStack", Reactor = "Gap", Avalonia = ["Spacing"], Kind = "rename" });
        });
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => VStack(TextBlock("x")) with { Gap = 4 };
            }
            """;
        Assert.Equal(["ISOMIG006: 'PanelOfStack.Gap' is 'Spacing' in Isotope"], Migrate.Analyze(reactor, tables).Describe());
        Assert.Empty(Migrate.Errors(Migrate.Fix(reactor, tables)));
    }

    [Fact]
    public void A_type_the_table_removed_is_reported_with_its_note()
    {
        const string reactor = Usings + """
            static class C
            {
                public static GridDefinition? Definition;
            }
            """;
        Assert.Equal([
            "ISOMIG010: Type 'GridDefinition' is gone in Isotope: Removed with GridElement.Definition: pass the GridSize[] columns and rows to GridElement or Grid(...); strings become tracks through GridSize.Parse (Array.ConvertAll(columns, GridSize.Parse)).",
        ], Migrate.Analyze(reactor).Describe());
    }
}
