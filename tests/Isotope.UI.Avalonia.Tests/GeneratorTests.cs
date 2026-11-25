using Isotope.UI.Core;
using Isotope.UI.Wrappers.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Upstream's wrappers generator as built for Avalonia (#12), run through Roslyn's generator driver against a record
/// in a test compilation that references the library and Avalonia.
/// </summary>
public class GeneratorTests
{
    private const string Record = """
        using Isotope.UI.Core;
        using Isotope.UI.Wrappers;
        namespace Probe;
        [GenerateReactorDescriptor(typeof(Avalonia.Controls.TextBox))]
        public partial record ProbeTextBoxElement(string? PlaceholderText = null) : Element
        {
            MEMBERS
            internal System.Action<Avalonia.Controls.TextBox>[] Setters { get; init; } = [];
        }
        """;

    private static (IReadOnlyList<Diagnostic> Diagnostics, string? Generated) Run(string members)
    {
        var (diagnostics, generated, _) = Compile(Record.Replace("MEMBERS", members), "ProbeTextBoxElement");
        return (diagnostics, generated);
    }

    // Runs the generator over source; returns its diagnostics, the named record's generated descriptor, and the
    // compiler's errors in the generated code.
    private static (IReadOnlyList<Diagnostic> Diagnostics, string? Generated, IReadOnlyList<Diagnostic> GeneratedErrors) Compile(
        string source, string recordName)
    {
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Element).Assembly.Location, typeof(global::Avalonia.Controls.TextBox).Assembly.Location,
                typeof(global::Avalonia.AvaloniaObject).Assembly.Location])
            .Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("Probe", [CSharpSyntaxTree.ParseText(source, parse)],
            refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new WrapperGenerator().AsSourceGenerator()], parseOptions: parse)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();
        var tree = result.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith(recordName + ".Descriptor.g.cs"));
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is { } t && t == tree)
            .ToList();
        return (result.Diagnostics, tree?.ToString(), errors);
    }

    [Fact]
    public void A_record_member_with_no_Avalonia_counterpart_is_reported()
    {
        // Header and IsSpellCheckEnabled are WinUI TextBox members Avalonia's TextBox doesn't have.
        var (diagnostics, generated) = Run("public string? Header { get; init; } public bool? IsSpellCheckEnabled { get; init; }");

        var missing = Assert.Single(diagnostics, d => d.Id == "ISOTOPEGEN001");
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        var message = missing.GetMessage();
        Assert.Contains("'Header'", message);
        Assert.Contains("'IsSpellCheckEnabled'", message);
        Assert.DoesNotContain("PlaceholderText", message);
        // The descriptor is still generated, for the members that do map.
        Assert.NotNull(generated);
        Assert.Contains("c.PlaceholderText = v", generated);
    }

    [Fact]
    public void Members_that_map_and_callbacks_are_not_reported_as_missing_counterparts()
    {
        var (diagnostics, generated) = Run("""
            public bool? IsReadOnly { get; init; }
            public System.Action<string>? OnChanged { get; init; }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "ISOTOPEGEN001");
        Assert.Contains("c.IsReadOnly = v", generated);
    }

    [Fact]
    public void An_obsolete_Avalonia_property_is_not_mapped()
    {
        // Avalonia 12's TextBox keeps Watermark as an obsolete alias of PlaceholderText.
        var (diagnostics, generated) = Run("public string? Watermark { get; init; }");

        Assert.DoesNotContain("c.Watermark", generated);
        Assert.Contains("'Watermark'", Assert.Single(diagnostics, d => d.Id == "ISOTOPEGEN001").GetMessage());
    }

    [Fact]
    public void An_Avalonia_items_control_gets_the_items_host_strategy()
    {
        // ItemsControl.Items is an ItemCollection: IList, not IList<object>.
        var (diagnostics, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.ListBox))]
            public partial record ProbeListElement(System.Collections.Generic.IReadOnlyList<object> Items) : Element
            {
                internal System.Action<Avalonia.Controls.ListBox>[] Setters { get; init; } = [];
            }
            """, "ProbeListElement");

        Assert.DoesNotContain(diagnostics, d => d.Id == "ISOTOPEGEN001");
        Assert.Contains("ItemsHost<ProbeListElement, global::Avalonia.Controls.ListBox>", generated);
        Assert.Contains("ItemsList.Of(c.Items)", generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void An_interface_typed_member_on_the_ClearValue_channel_compiles()
    {
        // Border.BorderBrush is an IBrush: C# has no implicit conversion from an interface to Optional<IBrush>.
        var (diagnostics, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border), ClearValueOnUnset = true)]
            public partial record ProbeBorderElement(Element? Child) : Element
            {
                public Avalonia.Media.IBrush? BorderBrush { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeBorderElement");

        // The generated descriptor compiles (without the gen-reference-clear-channel-of rule its get lambda doesn't), on the
        // ClearValue channel.
        Assert.Empty(errors);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ISOTOPEGEN001");
        Assert.Contains("Optional<global::Avalonia.Media.IBrush>.Of(e.BorderBrush)", generated);
        Assert.Contains("global::Avalonia.Controls.Border.BorderBrushProperty", generated);
    }

    // #77: a record outside the library gets the skip members, over each member it declares, as the built-in arms compare.
    [Fact]
    public void A_record_outside_the_library_gets_ShallowEqualsCore_and_HasCallbacks()
    {
        var (diagnostics, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeBorderElement(Element? Child) : Element
            {
                public Avalonia.Media.IBrush? Background { get; init; }
                public System.Action? OnTapped { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeBorderElement");

        Assert.Empty(errors);
        // Tapped's args aren't RoutedEventArgs, so the descriptor doesn't wire OnTapped (#78 reports it); the skip
        // members still count it.
        Assert.Equal("ISOTOPEGEN002", Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error).Id);
        Assert.Contains("protected override bool HasCallbacks => this.@OnTapped is not null;", generated);
        Assert.Contains("protected override bool ShallowEqualsCore(global::Isotope.UI.Core.Element other)", generated);
        Assert.Contains("GetType() == typeof(ProbeBorderElement)", generated);
        Assert.Contains("global::System.Object.ReferenceEquals(this.@Child, o.@Child)", generated);
        Assert.Contains("global::System.Object.ReferenceEquals(this.@Setters, o.@Setters)", generated);
        Assert.Contains("(this.@OnTapped is null) == (o.@OnTapped is null)", generated);
        Assert.Contains("EqualityComparer<global::Avalonia.Media.IBrush>.Default.Equals(this.@Background, o.@Background)", generated);
    }

    [Fact]
    public void A_record_that_declares_its_own_skip_members_gets_none()
    {
        var (_, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeBorderElement(Element? Child) : Element
            {
                public System.Action? OnTapped { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
                protected override bool HasCallbacks => OnTapped is not null;
                protected override bool ShallowEqualsCore(Element other) => false;
            }
            """, "ProbeBorderElement");

        Assert.Empty(errors);
        Assert.NotNull(generated);
        Assert.DoesNotContain("ShallowEqualsCore", generated);
        Assert.DoesNotContain("HasCallbacks", generated);
    }

    // #77, what can't be compared safely: another delegate by reference, a computed property not at all, and nothing
    // generated for a struct holding an element, a base's member out of reach, or a HasCallbacks a base declares.
    [Fact]
    public void The_skip_members_compare_only_what_they_safely_can()
    {
        var (_, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeBorderElement(Element? Child) : Element
            {
                public System.Func<double, string>? Format { get; init; }
                public int Computed => Child is null ? 0 : 1;
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeBorderElement");
        Assert.Empty(errors);
        Assert.Contains("global::System.Object.ReferenceEquals(this.@Format, o.@Format)", generated);
        Assert.DoesNotContain("Computed", generated);

        var (_, withOptional, optionalErrors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeBorderElement(Element? Child) : Element
            {
                public Isotope.UI.Optional<Element> Header { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeBorderElement");
        Assert.Empty(optionalErrors);
        Assert.DoesNotContain("ShallowEqualsCore", withOptional);

        // A base in the same assembly declares HasCallbacks; a member hides the base's; a record member holds an element.
        var (_, overBase, overBaseErrors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            public abstract record BaseElement : Element
            {
                public System.Action? OnTapped { get; init; }
                protected override bool HasCallbacks => OnTapped is not null;
            }
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeBorderElement(Element? Child) : BaseElement
            {
                public System.Action? OnOther { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeBorderElement");
        Assert.Empty(overBaseErrors);
        Assert.DoesNotContain("ShallowEqualsCore", overBase);
        Assert.DoesNotContain("HasCallbacks", overBase);

        var (_, holder, holderErrors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            public record HeaderData(string Title, Element Icon);
            public struct Slot { public Element E; }
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeBorderElement(Element? Child) : Element
            {
                public HeaderData? Header { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeSlotElement(Element? Child) : Element
            {
                public Slot Slot { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeBorderElement");
        Assert.Empty(holderErrors);
        Assert.Contains("global::System.Object.ReferenceEquals(this.@Header, o.@Header)", holder);
        var (_, slot, _) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            public struct Slot { public Element E; }
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Border))]
            public partial record ProbeSlotElement(Element? Child) : Element
            {
                public Slot Slot { get; init; }
                internal System.Action<Avalonia.Controls.Border>[] Setters { get; init; } = [];
            }
            """, "ProbeSlotElement");
        Assert.DoesNotContain("ShallowEqualsCore", slot);

        // SliderElement declares HasCallbacks, and its Setters is internal to the library. (The descriptor itself reads
        // e.Setters, which a record outside the library can't reach: CS1061, as before #77.)
        var (_, derived, _) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Slider))]
            public partial record ProbeSliderElement : SliderElement
            {
                public System.Action<double>? OnOther { get; init; }
            }
            """, "ProbeSliderElement");
        Assert.NotNull(derived);
        Assert.DoesNotContain("HasCallbacks", derived);
        Assert.DoesNotContain("ShallowEqualsCore", derived);
    }

    // Runs the generator over source; returns its diagnostics, the hint names of the files it generated, and the
    // compiler's errors in any generated file.
    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> Files, IReadOnlyList<Diagnostic> GeneratedErrors) CompileAll(string source)
    {
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Element).Assembly.Location, typeof(global::Avalonia.Controls.TextBox).Assembly.Location,
                typeof(global::Avalonia.AvaloniaObject).Assembly.Location])
            .Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("Probe", [CSharpSyntaxTree.ParseText(source, parse)],
            refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new WrapperGenerator().AsSourceGenerator()], parseOptions: parse)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();
        var generated = result.GeneratedTrees.ToHashSet();
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is { } t && generated.Contains(t))
            .ToList();
        return (result.Diagnostics, result.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath)).ToList(), errors);
    }

    // A control whose event is a plain EventHandler (not routed), as spike J's Chip; ATTRS and CALLBACK vary.
    private const string ChipRecord = """
        using Isotope.UI.Core;
        using Isotope.UI.Wrappers;
        namespace Probe;
        public class Chip : Avalonia.Controls.ContentControl
        {
            public event System.EventHandler? Closed;
            public void Close() => Closed?.Invoke(this, System.EventArgs.Empty);
        }
        [GenerateReactorDescriptor(typeof(Chip))]
        ATTRS
        public partial record ChipElement(Element Content) : Element
        {
            public CALLBACK OnClosed { get; init; }
            internal System.Action<Chip>[] Setters { get; init; } = [];
        }
        """;

    private static string Chip(string attrs, string callback) => ChipRecord.Replace("ATTRS", attrs).Replace("CALLBACK", callback);

    [Fact]
    public void A_callback_the_descriptor_never_wires_is_reported_at_the_member()
    {
        // #78, gap 2: a plain EventHandler event is neither routed nor [WrapEvent], so upstream dropped OnClosed silently.
        var source = Chip("", "System.Action?");
        var (diagnostics, generated, errors) = Compile(source, "ChipElement");

        var unwired = Assert.Single(diagnostics, d => d.Id == "ISOTOPEGEN002");
        Assert.Equal(DiagnosticSeverity.Error, unwired.Severity);
        Assert.Contains("'OnClosed'", unwired.GetMessage());
        Assert.Contains("[WrapEvent(\"Closed\")]", unwired.GetMessage());
        Assert.Equal(source.Split('\n').ToList().FindIndex(l => l.Contains("OnClosed { get;")), unwired.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain(diagnostics, d => d.Id is "ISOTOPEGEN001" or "ISOTOPEGEN003");
        Assert.NotNull(generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void A_WrapEvent_callback_that_cant_take_the_args_is_reported_at_the_member_not_in_the_generated_file()
    {
        // #78, gap 3: [WrapEvent] passes the whole args, so an Action callback gave CS1501 inside the descriptor.
        var source = Chip("[WrapEvent(\"Closed\")]", "System.Action?");
        var (diagnostics, generated, errors) = Compile(source, "ChipElement");

        var mismatch = Assert.Single(diagnostics, d => d.Id == "ISOTOPEGEN003");
        Assert.Equal(DiagnosticSeverity.Error, mismatch.Severity);
        Assert.Contains("'Action<EventArgs>'", mismatch.GetMessage());
        Assert.Equal(source.Split('\n').ToList().FindIndex(l => l.Contains("OnClosed { get;")), mismatch.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ISOTOPEGEN002");
        Assert.Empty(errors);
        Assert.DoesNotContain("live.OnClosed", generated);

        // Taken over with [WrapManual], it is still called by the generated trampoline, so still checked.
        var manual = Chip("[WrapEvent(\"Closed\")][WrapManual(\"OnClosed\")]", "System.Action?")
            + "\npartial record ChipElement { private static partial Isotope.UI.Core.V1Protocol.Descriptor.ControlDescriptor<ChipElement, Chip> Customize(Isotope.UI.Core.V1Protocol.Descriptor.ControlDescriptor<ChipElement, Chip> d) => d; }";
        var (manualDiagnostics, _, manualErrors) = Compile(manual, "ChipElement");
        Assert.Single(manualDiagnostics, d => d.Id == "ISOTOPEGEN003");
        Assert.Empty(manualErrors);
    }

    [Theory]
    [InlineData("[WrapEvent(\"Closed\")]", "System.Action<System.EventArgs>?", "live.OnClosed?.Invoke(args)")]
    [InlineData("[WrapEvent(\"Closed\")]", "System.Action<object>?", "live.OnClosed?.Invoke(args)")]
    [InlineData("[WrapManual(\"OnClosed\")]", "System.Action?", null)]
    public void A_callback_that_is_wired_or_taken_over_is_not_reported(string attrs, string callback, string? wiring)
    {
        var source = Chip(attrs, callback);
        if (attrs.Contains("WrapManual"))
            source += "\npartial record ChipElement { private static partial Isotope.UI.Core.V1Protocol.Descriptor.ControlDescriptor<ChipElement, Chip> Customize(Isotope.UI.Core.V1Protocol.Descriptor.ControlDescriptor<ChipElement, Chip> d) => d; }";
        var (diagnostics, generated, errors) = Compile(source, "ChipElement");

        Assert.DoesNotContain(diagnostics, d => d.Id is "ISOTOPEGEN002" or "ISOTOPEGEN003");
        Assert.Empty(errors);
        if (wiring is not null) Assert.Contains(wiring, generated);
    }

    [Fact]
    public void A_routed_callback_and_a_controlled_value_callback_are_not_reported()
    {
        var (diagnostics, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Button))]
            public partial record ProbeButtonElement : Element
            {
                public System.Action? OnClick { get; init; }
                internal System.Action<Avalonia.Controls.Button>[] Setters { get; init; } = [];
            }
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.Slider))]
            public partial record ProbeSliderElement(double Value = 0) : Element
            {
                public System.Action<double>? OnValueChanged { get; init; }
                internal System.Action<Avalonia.Controls.Slider>[] Setters { get; init; } = [];
            }
            """, "ProbeButtonElement");

        Assert.DoesNotContain(diagnostics, d => d.Id is "ISOTOPEGEN002" or "ISOTOPEGEN003");
        Assert.Contains("OnClick", generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void A_record_without_Setters_gets_a_descriptor_that_doesnt_read_them()
    {
        // #78, gap 3: the descriptor read e.Setters unconditionally (CS1061 inside the generated file).
        var (diagnostics, generated, errors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.TextBox))]
            public partial record PlainTextBoxElement(string? PlaceholderText = null) : Element;
            """, "PlainTextBoxElement");

        Assert.Empty(errors);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("c.PlaceholderText = v", generated);
        Assert.DoesNotContain("GetSetters", generated);
        // A record with Setters still has them read, as a property or a field.
        Assert.Contains("GetSetters = static e => e.Setters", Run("").Generated);
        var (_, withField, fieldErrors) = Compile("""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.TextBox))]
            public partial record FieldTextBoxElement(string? PlaceholderText = null) : Element
            {
                internal System.Action<Avalonia.Controls.TextBox>[] Setters = [];
            }
            """, "FieldTextBoxElement");
        Assert.Empty(fieldErrors);
        Assert.Contains("GetSetters = static e => e.Setters", withField);
    }

    [Theory]
    [InlineData("WrapDecorator", "[WrapDecorator(nameof(Create))]")]
    [InlineData("WrapPolymorphic", "[WrapPolymorphic(nameof(Create))]")]
    public void A_decorator_record_is_reported_at_the_record_and_nothing_is_generated(string name, string attribute)
    {
        // #78, gap 3: the handler these generate calls engine members Isotope doesn't have (CS0117 inside it).
        var source = $$"""
            using Isotope.UI.Core;
            using Isotope.UI.Wrappers;
            namespace Probe;
            [GenerateReactorDescriptor(typeof(Avalonia.Controls.TextBox))]
            {{attribute}}
            public partial record HandMadeElement(string? Text = null) : Element
            {
                static Avalonia.Controls.TextBox Create(HandMadeElement e) => new() { Text = e.Text };
            }
            """;
        var (diagnostics, files, errors) = CompileAll(source);

        var unsupported = Assert.Single(diagnostics, d => d.Id == "ISOTOPEGEN004");
        Assert.Equal(DiagnosticSeverity.Error, unsupported.Severity);
        Assert.Contains("[" + name + "]", unsupported.GetMessage());
        Assert.Equal(source.Split('\n').ToList().FindIndex(l => l.Contains(name)), unsupported.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain(files, f => f.StartsWith("HandMadeElement", StringComparison.Ordinal));
        Assert.Empty(errors);
    }
}
