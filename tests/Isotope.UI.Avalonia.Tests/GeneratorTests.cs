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
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Element).Assembly.Location, typeof(global::Avalonia.Controls.TextBox).Assembly.Location,
                typeof(global::Avalonia.AvaloniaObject).Assembly.Location])
            .Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Probe",
            [CSharpSyntaxTree.ParseText(Record.Replace("MEMBERS", members), new CSharpParseOptions(LanguageVersion.Preview))],
            refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new WrapperGenerator().AsSourceGenerator())
            .RunGenerators(compilation);
        var result = driver.GetRunResult();
        var generated = result.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("ProbeTextBoxElement.Descriptor.g.cs"))?.ToString();
        return (result.Diagnostics, generated);
    }

    [Fact]
    public void A_record_member_with_no_Avalonia_counterpart_is_reported()
    {
        // Header and IsSpellCheckEnabled are WinUI TextBox members Avalonia's TextBox doesn't have.
        var (diagnostics, generated) = Run("public string? Header { get; init; } public bool? IsSpellCheckEnabled { get; init; }");

        var missing = Assert.Single(diagnostics, d => d.Id == "ISOTOPEGEN001");
        Assert.Equal(DiagnosticSeverity.Warning, missing.Severity);
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
}
