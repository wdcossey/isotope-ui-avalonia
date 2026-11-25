using Isotope.UI.Core;
using Isotope.UI.Mcp.Api;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModelContextProtocol;
using Xunit;

namespace Isotope.UI.Mcp.Tests;

/// <summary>search_api and get_api, over the library and desktop startup this test process references (as the tool does).</summary>
public class ApiTests
{
    [Fact]
    public void Search_finds_factories_by_name_with_their_signature_and_summary()
    {
        var text = IsotopeTools.SearchApi("button");
        Assert.Contains("- factory `static ButtonElement Factories.Button(string content, Action? onClick = null)` — A push button with a text content", text);
        Assert.Contains("- element `record ButtonElement : Element`", text);
        // The exact name comes first.
        var lines = text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
        Assert.StartsWith("- factory `static ButtonElement Factories.Button(", lines[0]);
    }

    [Fact]
    public void Search_finds_hooks_and_modifiers_and_matches_summaries()
    {
        Assert.Contains("- hook `(T Value, Action<T> Set) RenderContext.UseState<T>(T initialValue, bool threadSafe = false)`", IsotopeTools.SearchApi("UseSt"));
        Assert.Contains("- modifier `static T ElementExtensions.Padding<T>(this T el, double uniform) where T : Element`", IsotopeTools.SearchApi("padding"));
        // Words of the summary, not of the name: ToggleButton's summary is "A button that stays pressed…".
        Assert.Contains("Factories.ToggleButton(", IsotopeTools.SearchApi("stays pressed"));
    }

    [Fact]
    public void Search_takes_a_kind_and_a_limit()
    {
        var hooks = IsotopeTools.SearchApi("use", kind: "hook", limit: 200).Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
        Assert.True(hooks.Count >= 10, "only " + hooks.Count + " hooks");
        Assert.All(hooks, l => Assert.StartsWith("- hook ", l));
        Assert.Equal(2, IsotopeTools.SearchApi("button", limit: 2).Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
        Assert.Throws<McpException>(() => IsotopeTools.SearchApi("button", kind: "widget"));
        // Only the kinds' names: not a number, a list, or the members get_api lists.
        Assert.Throws<McpException>(() => IsotopeTools.SearchApi("button", kind: "4"));
        Assert.Throws<McpException>(() => IsotopeTools.SearchApi("button", kind: "factory, hook"));
        Assert.Throws<McpException>(() => IsotopeTools.SearchApi("button", kind: "member"));
        Assert.Throws<McpException>(() => IsotopeTools.SearchApi(" "));
        Assert.StartsWith("Nothing in Isotope's API matches", IsotopeTools.SearchApi("zzqqxx"));
    }

    [Fact]
    public void Get_api_gives_a_hook_with_its_doc_comment_and_a_tested_example()
    {
        var text = IsotopeTools.GetApi("UseState");
        Assert.Contains("# RenderContext.UseState (hook)", text);
        Assert.Contains("(T Value, Action<T> Set) RenderContext.UseState<T>(T initialValue, bool threadSafe = false)", text);
        // From the XML doc file next to the library's DLL.
        Assert.Contains("Declares a piece of state.", text);
        var snippet = Guides.Snippets.Single(s => s.Name == "use-state");
        Assert.Contains("tested snippet `use-state`", text);
        Assert.Contains(snippet.Code, text);
    }

    [Fact]
    public void Get_api_ignores_generic_arguments_nested_or_not()
    {
        Assert.Contains("# RenderContext.UseState (hook)", IsotopeTools.GetApi("UseState<List<int>>"));
        Assert.Contains("# RenderContext.UseState (hook)", IsotopeTools.GetApi("RenderContext.UseState<Dictionary<string, List<int>>>()"));
    }

    [Fact]
    public void Hooks_are_the_Use_methods_of_a_components_context()
    {
        var hooks = ApiCatalog.Default.Entries.Where(e => e.Kind == ApiKind.Hook).ToList();
        Assert.Contains(hooks, h => h.Owner == "RenderContext" && h.Name == "UseState");
        var owners = hooks.Select(h => h.Member is System.Reflection.MethodInfo m && m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
            ? m.GetParameters()[0].ParameterType : ((System.Reflection.MethodInfo)h.Member).DeclaringType!).Distinct();
        Assert.All(owners, t => Assert.True(typeof(RenderContext).IsAssignableFrom(t) || typeof(Component).IsAssignableFrom(t), t + " has a hook"));
        // A Use* method elsewhere is not a hook.
        var others = ApiCatalog.Default.Entries.Where(e => e.Kind != ApiKind.Hook && e.Member is System.Reflection.MethodInfo && e.Name.StartsWith("Use", StringComparison.Ordinal) && e.Name.Length > 3 && char.IsUpper(e.Name[3])).ToList();
        Assert.All(others, e => Assert.False(typeof(RenderContext).IsAssignableFrom(((System.Reflection.MethodInfo)e.Member).DeclaringType!), e.Signature));
    }

    [Fact]
    public void Get_api_gives_every_overload_with_its_parameters()
    {
        var text = IsotopeTools.GetApi("Button");
        Assert.Contains("# Factories.Button (factory, 3 overloads)", text);
        Assert.Contains("`using static Isotope.UI.Factories;`", text);
        Assert.Contains("static ButtonElement Factories.Button(Command command)", text);
        Assert.Contains("A push button driven by a command", text);
    }

    [Fact]
    public void Get_api_reads_a_CSharp_14_extension_member_from_its_declaration()
    {
        // ReactorApp.Run<TRoot> is declared in an extension block of the desktop package's ReactorAppDesktop: it is
        // listed under ReactorApp, with the signature and doc of the block's member (its implementation's doc is an
        // inheritdoc pointing there).
        var text = IsotopeTools.GetApi("ReactorApp.Run");
        Assert.Contains("static int ReactorApp.Run<TRoot>(string title = \"Isotope App\", double? width = null", text);
        Assert.Contains("Runs an app whose one window holds a `TRoot` component", text);
        Assert.Contains("- `title`: The window's title.", text);
        // Its example names ReactorApp and Run.
        Assert.Contains("tested snippet `run-app`", text);
    }

    [Fact]
    public void A_members_example_must_name_its_type()
    {
        // A snippet uses the word Text (a text style's), but none uses TextBoxElement: no example.
        Assert.NotEmpty(Guides.Using("Text"));
        Assert.DoesNotContain("Example (", IsotopeTools.GetApi("TextBoxElement.Text"));
    }

    [Fact]
    public void Default_values_are_written_as_CSharp_literals()
    {
        var m = typeof(LiteralProbe).GetMethod(nameof(LiteralProbe.Defaults))!;
        Assert.Equal("static void LiteralProbe.Defaults(char quote = '\\'', string text = \"a\\nb\\t\\\"c\\\"\", int count = 3, string? none = null)",
            new Signatures().Method(m, "LiteralProbe"));
    }

    [Fact]
    public void Get_api_lists_a_records_members_and_the_factories_that_build_it()
    {
        var text = IsotopeTools.GetApi("ButtonElement");
        Assert.Contains("# ButtonElement (element record)", text);
        Assert.Contains("Built by:", text);
        Assert.Contains("- `static ButtonElement Factories.Button(string content, Action? onClick = null)`", text);
        Assert.Contains("- `Action? ButtonElement.OnClick { get; init; }`", text);

        var member = IsotopeTools.GetApi("ButtonElement.OnClick");
        Assert.Contains("Action? ButtonElement.OnClick { get; init; }", member);
        Assert.DoesNotContain("Built by:", member);
    }

    [Fact]
    public void Get_api_on_an_unknown_name_offers_close_matches()
    {
        var text = IsotopeTools.GetApi("Togg");
        Assert.StartsWith("Isotope's API has no \"Togg\".", text);
        Assert.Contains("Factories.ToggleButton(", text);
        Assert.Throws<McpException>(() => IsotopeTools.GetApi(""));
    }

    [Fact]
    public void A_doc_comment_is_followed_through_its_inheritdoc()
    {
        var docs = new XmlDocs(System.Xml.Linq.XDocument.Parse("""
            <doc><members>
              <member name="M:A.Impl"><inheritdoc cref="M:A.&lt;G&gt;$1.Decl"/></member>
              <member name="M:A.&lt;G&gt;$1.Decl"><summary>The declared member, as <see cref="T:N.B"/> and <c>code</c>.</summary></member>
            </members></doc>
            """));
        Assert.Equal("The declared member, as `B` and `code`.", docs.Summary("M:A.Impl"));
        Assert.Equal("", docs.Summary("M:A.Missing"));
    }

    [Fact]
    public void The_doc_files_ship_next_to_the_assemblies()
    {
        var docs = ApiCatalog.Default.Docs;
        Assert.True(docs["Isotope.UI.Avalonia"].Count > 1000, "library doc members: " + docs["Isotope.UI.Avalonia"].Count);
        Assert.True(docs["Isotope.UI.Avalonia.Desktop"].Count > 5, "desktop doc members: " + docs["Isotope.UI.Avalonia.Desktop"].Count);
    }

    /// <summary>
    /// The doc IDs computed from reflection are the compiler's: for every public method, constructor and property of the
    /// library's and the desktop startup's public types, Roslyn's documentation comment ID of the same member, read from
    /// the same DLLs, is the same set.
    /// </summary>
    [Fact]
    public void Doc_ids_from_reflection_are_the_compilers()
    {
        var assemblies = new[] { typeof(Factories).Assembly, typeof(ReactorAppDesktop).Assembly };
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create("Probe", references: tpa.Select(p => MetadataReference.CreateFromFile(p)));
        foreach (var assembly in assemblies)
        {
            var symbol = compilation.GetAssemblyOrModuleSymbol(compilation.References.Single(r => Path.GetFileName(((PortableExecutableReference)r).FilePath) == Path.GetFileName(assembly.Location))) as IAssemblySymbol;
            Assert.NotNull(symbol);
            var roslyn = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in AllTypes(symbol.GlobalNamespace).Where(t => IsPublic(t) && !t.Name.Contains('<') && !t.IsExtension))
                foreach (var m in type.GetMembers().Where(m => m.DeclaredAccessibility == Accessibility.Public && !m.Name.Contains('<') && !m.IsImplicitlyDeclared))
                    if (m is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor } or IPropertySymbol)
                        roslyn.Add(m.GetDocumentationCommentId()!);
            var reflection = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in assembly.GetExportedTypes().Where(t => !t.Name.Contains('<')))
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var m in type.GetMethods(flags).Where(m => !m.IsSpecialName && !m.Name.Contains('<'))) reflection.Add(DocIds.Of(m));
                foreach (var c in type.GetConstructors(flags)) reflection.Add(DocIds.Of(c));
                foreach (var p in type.GetProperties(flags).Where(p => !p.Name.Contains('<'))) reflection.Add(DocIds.Of(p));
            }
            // Members the compiler declares implicitly (a record's EqualityContract, Clone, PrintMembers) are on the
            // reflection side only.
            var missing = roslyn.Except(reflection).ToList();
            Assert.True(missing.Count == 0, assembly.GetName().Name + ": " + missing.Count + " of " + roslyn.Count + " doc IDs not computed from reflection, e.g.\n" + string.Join("\n", missing.Take(15)));
            Assert.True(roslyn.Count > 100 || assembly == assemblies[1], "only " + roslyn.Count + " members compared");
        }

        static bool IsPublic(INamedTypeSymbol t) => t.DeclaredAccessibility == Accessibility.Public && (t.ContainingType is null || IsPublic(t.ContainingType));
        static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol ns) =>
            ns.GetMembers().SelectMany(m => m switch
            {
                INamespaceSymbol n => AllTypes(n),
                INamedTypeSymbol t => [t, .. AllTypes(t)],
                _ => [],
            });
    }
}

public static class LiteralProbe
{
    public static void Defaults(char quote = '\'', string text = "a\nb\t\"c\"", int count = 3, string? none = null) { }
}
