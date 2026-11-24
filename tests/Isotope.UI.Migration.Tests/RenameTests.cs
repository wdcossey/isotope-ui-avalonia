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
                public static Element Render() => AutoSuggestBox("") with { OnQuerySubmitted = _ => { } };
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
    public void Proposed_type_factory_and_modifier_renames_are_not_applied()
    {
        // The table proposes TabView -> TabControl, NumberBox -> NumericUpDown and HAlign -> HorizontalAlignment;
        // Isotope's code still has Reactor's names, so Reactor code using them compiles and is left alone.
        const string code = Usings + """
            static class C
            {
                public static Element[] Render(NumberBoxElement number) =>
                [
                    NumberBox(1).HAlign(Avalonia.Layout.HorizontalAlignment.Left),
                    number,
                ];
            }
            """;
        Assert.Empty(Migrate.Errors(code));
        Assert.Empty(Migrate.Analyze(code));
    }

    [Fact]
    public void Applied_type_factory_and_modifier_renames_are_switched_by_the_table()
    {
        // Entries Isotope's code doesn't have yet, applied in a copy of the table: Reactor's names become the
        // table's, so the switch is the table's status, not the analyzer's code.
        var tables = Migrate.WithRenames(t =>
        {
            t.Factories.Add(new NameEntry { Reactor = "ScrollView", Avalonia = "ScrollViewer", Status = "applied" });
            t.Types.Add(new NameEntry { Reactor = "ScrollViewElement", Avalonia = "ScrollViewerElement", Status = "applied" });
            t.Modifiers.Add(new NameEntry { Reactor = "Margins", Avalonia = "Margin", Status = "applied" });
        });
        const string reactor = Usings + """
            static class C
            {
                public static Element Render() => ScrollView(TextBlock("x").Margins(4));
                public static ScrollViewElement? Last;
            }
            """;
        Assert.Equal([
            "ISOMIG009: Factory 'ScrollView' is 'ScrollViewer' in Isotope",
            "ISOMIG009: Modifier 'Margins' is 'Margin' in Isotope",
            "ISOMIG009: Type 'ScrollViewElement' is 'ScrollViewerElement' in Isotope",
        ], Migrate.Analyze(reactor, tables).Describe());
        var fixedText = Migrate.Fix(reactor, tables);
        Assert.Contains("ScrollViewer(TextBlock(\"x\").Margin(4))", fixedText);
        Assert.Contains("public static ScrollViewerElement? Last;", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));

        // The same entries as proposed: nothing renames.
        var proposed = Migrate.WithRenames(t =>
        {
            t.Factories.Add(new NameEntry { Reactor = "ScrollView", Avalonia = "ScrollViewer", Status = "proposed" });
            t.Types.Add(new NameEntry { Reactor = "ScrollViewElement", Avalonia = "ScrollViewerElement", Status = "proposed" });
            t.Modifiers.Add(new NameEntry { Reactor = "Margins", Avalonia = "Margin", Status = "proposed" });
        });
        Assert.DoesNotContain(Migrate.Analyze(reactor, proposed), d => d.Id == "ISOMIG009");
    }

    [Fact]
    public void A_record_renamed_by_the_table_keeps_its_member_entries()
    {
        // With StackElement -> StackPanelElement applied, a member entry recorded under Reactor's record name still
        // applies to the renamed record: here Isotope's StackElement stands in for the renamed type.
        var tables = Migrate.WithRenames(t =>
        {
            t.Types.Add(new NameEntry { Reactor = "PanelOfStack", Avalonia = "StackElement", Status = "applied" });
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
