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
    public void Members_that_map_and_callbacks_are_not_reported()
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
}
