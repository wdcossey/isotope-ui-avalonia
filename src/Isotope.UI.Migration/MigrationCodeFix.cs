using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Isotope.UI.Migration;

/// <summary>
/// The one code fix for every fixable migration diagnostic: it applies the edit the analyzer put in the diagnostic's
/// properties (<see cref="Props"/>). Fix All applies a document's diagnostics in one pass, so edits from different
/// rules on the same file (two rules adding the same using) can't conflict.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MigrationCodeFix)), Shared]
public sealed class MigrationCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => Rules.Fixable;

    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(async (context, document, diagnostics) =>
            await ApplyAsync(document, diagnostics, context.CancellationToken).ConfigureAwait(false));

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!HasEdit(diagnostic)) continue;
            context.RegisterCodeFix(
                CodeAction.Create("Migrate to Isotope: " + diagnostic.GetMessage(),
                    ct => ApplyAsync(context.Document, [diagnostic], ct), equivalenceKey: diagnostic.Id),
                diagnostic);
        }
        return Task.CompletedTask;
    }

    static bool HasEdit(Diagnostic d) =>
        d.Properties.ContainsKey(Props.Replace) || d.Properties.ContainsKey(Props.RemoveLine) || d.Properties.ContainsKey(Props.Usings);

    internal static async Task<Document> ApplyAsync(Document document, IEnumerable<Diagnostic> diagnostics, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var changes = new List<TextChange>();
        var usings = new List<string>();
        foreach (var d in diagnostics)
        {
            if (!d.Location.IsInSource || !HasEdit(d)) continue;
            var span = d.Location.SourceSpan;
            if (d.Properties.TryGetValue(Props.RemoveLine, out var remove) && remove == "1")
                changes.Add(new TextChange(LineSpan(text, span), ""));
            else if (d.Properties.TryGetValue(Props.Replace, out var replace) && replace is not null)
                changes.Add(new TextChange(span, replace));
            if (d.Properties.TryGetValue(Props.Usings, out var list) && !string.IsNullOrEmpty(list))
                usings.AddRange(list!.Split(';'));
        }

        // Identical edits (one span reported twice) apply once; of two overlapping edits the first wins, and the
        // next run of the fix picks up what is left.
        var kept = new List<TextChange>();
        var end = -1;
        foreach (var change in changes.GroupBy(c => (c.Span, c.NewText)).Select(g => g.First()).OrderBy(c => c.Span.Start).ThenByDescending(c => c.Span.Length))
        {
            if (change.Span.Start < end) continue;
            kept.Add(change);
            end = change.Span.End;
        }
        var newText = text.WithChanges(kept);
        if (usings.Count > 0) newText = AddUsings(document, newText, usings, ct);
        return document.WithText(newText);
    }

    /// <summary>The span of the whole lines a directive takes, when nothing else is on them.</summary>
    static TextSpan LineSpan(SourceText text, TextSpan span)
    {
        var first = text.Lines.GetLineFromPosition(span.Start);
        var last = text.Lines.GetLineFromPosition(span.End);
        var before = text.ToString(TextSpan.FromBounds(first.Start, span.Start));
        var after = text.ToString(TextSpan.FromBounds(span.End, last.End));
        if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after)) return span;
        return TextSpan.FromBounds(first.Start, last.EndIncludingLineBreak);
    }

    static SourceText AddUsings(Document document, SourceText text, IEnumerable<string> usings, CancellationToken ct)
    {
        var options = document.Project.ParseOptions as CSharpParseOptions ?? CSharpParseOptions.Default;
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, options, cancellationToken: ct).GetRoot(ct);
        // Every plain using already there, at the top or in a namespace, without global:: (`using global::Avalonia.Media;`).
        var existing = new HashSet<string>(root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<UsingDirectiveSyntax>()
            .Where(u => u.Alias is null && u.StaticKeyword == default && u.Name is not null)
            .Select(u => (u.GlobalKeyword != default ? "global " : "") + PlainName(u.Name!)));
        var missing = usings.Select(u => u.Trim()).Where(u => u.Length > 0).Distinct()
            .Where(u => !existing.Contains(u) && !existing.Contains("global " + u))
            .OrderBy(u => u, System.StringComparer.Ordinal).ToList();
        if (missing.Count == 0) return text;

        var nl = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var globals = missing.Where(u => u.StartsWith("global ")).Select(u => u.Substring("global ".Length)).ToList();
        var locals = missing.Where(u => !u.StartsWith("global ")).ToList();
        var changes = new List<TextChange>();
        if (globals.Count > 0)
        {
            // Global usings go before every other using (CS8915).
            var lastGlobal = root.Usings.LastOrDefault(u => u.GlobalKeyword != default);
            var lines = string.Concat(globals.Select(n => "global using " + n + ";" + nl));
            if (lastGlobal is not null) changes.Add(AfterLine(text, lastGlobal, lines, nl));
            else if (root.Usings.FirstOrDefault() is { } firstUsing) changes.Add(new TextChange(new TextSpan(text.Lines.GetLineFromPosition(firstUsing.SpanStart).Start, 0), lines));
            else changes.Add(new TextChange(new TextSpan(Top(text, root), 0), lines + (locals.Count == 0 ? BlankAfter(text, root, nl) : "")));
        }
        if (locals.Count > 0)
        {
            var lines = string.Concat(locals.Select(n => "using " + n + ";" + nl));
            if (root.Usings.LastOrDefault() is { } lastUsing) changes.Add(AfterLine(text, lastUsing, lines, nl));
            else changes.Add(new TextChange(new TextSpan(Top(text, root), 0), lines + BlankAfter(text, root, nl)));
        }
        return text.WithChanges(changes);
    }

    /// <summary>Where usings go in a file with none: after any extern alias (a using before one is CS0439), else the start.</summary>
    static int Top(SourceText text, CompilationUnitSyntax root)
    {
        if (root.Externs.LastOrDefault() is not { } last) return 0;
        var line = text.Lines.GetLineFromPosition(last.Span.End);
        return line.EndIncludingLineBreak;
    }

    /// <summary>A blank line after usings added at the top, unless one is there already (left by a removed using).</summary>
    static string BlankAfter(SourceText text, CompilationUnitSyntax root, string nl)
    {
        var at = Top(text, root);
        return at < text.Length && text[at] is '\r' or '\n' ? "" : nl;
    }

    static string PlainName(NameSyntax name) => name switch
    {
        AliasQualifiedNameSyntax a => PlainName(a.Name),
        QualifiedNameSyntax q => PlainName(q.Left) + "." + q.Right.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => name.ToString(),
    };

    static TextChange AfterLine(SourceText text, SyntaxNode node, string lines, string nl)
    {
        var line = text.Lines.GetLineFromPosition(node.Span.End);
        return line.EndIncludingLineBreak == line.End
            ? new TextChange(new TextSpan(line.End, 0), nl + lines.Substring(0, lines.Length - nl.Length))
            : new TextChange(new TextSpan(line.EndIncludingLineBreak, 0), lines);
    }
}
