using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Isotope.Sync.Roslyn;

public sealed record Request(List<FileRequest>? Files);
/// <summary>One file. Original (optional): the text to compare syntax errors with, when Text already carries
/// transforms made elsewhere (tools/sync's text rules); by default the check compares with Text.</summary>
public sealed record FileRequest(string Path, string Text, List<Op>? Ops, string? Original = null);
/// <summary>
/// One transform. drop-member: Member (a name; "this" for an indexer), Type (the dotted names of the types that
/// contain it, innermost last, matched as a suffix; omitted for a type declared in a namespace), Parameters (optional,
/// "(T1, T2)": the parameter types as written, to pick one overload). retarget-type: From (a type name as written in
/// code, dotted, with or without global::), To (the text that replaces it). drop-attribute: Attribute (its name, with or
/// without the Attribute suffix; dotted to match only that qualified form).
/// </summary>
public sealed class Op
{
    public string Id { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("op")]
    public string Kind { get; init; } = "";
    public string? Member { get; init; }
    public string? Type { get; init; }
    public string? Parameters { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Attribute { get; init; }
    public int? At { get; init; }
}
/// <summary>Hits: matches found. Changed: whether the op changed the text. Info: locate-member's answer.</summary>
public sealed record OpResult(string Id, int Hits, string? Error, string? Info = null, bool Changed = false);
public sealed record FileResponse(string Path, string Text, List<OpResult> Results, List<string> Errors);
public sealed record Response(List<FileResponse> Files);

sealed class OpException(string message) : Exception(message);

public static class Transforms
{
    // Preview, so whatever the pinned SDK's compiler accepts parses. The symbols are a net10.0 Debug build's, so code
    // under #if DEBUG is seen; code an #if leaves out is not, so an op whose target appears there fails (Inactive), and
    // the syntax check also parses without DEBUG.
    static readonly string[] Net10 = ["TRACE", "NET", "NETCOREAPP", "NET10_0",
        "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER", .. Enumerable.Range(5, 6).Select(v => "NET" + v + "_0_OR_GREATER")];
    static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: ["DEBUG", .. Net10]);
    static readonly CSharpParseOptions ReleaseOptions = new(LanguageVersion.Preview, DocumentationMode.Parse, preprocessorSymbols: Net10);

    public static FileResponse Apply(FileRequest file)
    {
        var text = file.Text;
        var results = new List<OpResult>();
        var errors = new List<string>();
        foreach (var op in file.Ops ?? [])
        {
            try
            {
                if (op.Kind == "locate-member")
                {
                    results.Add(new OpResult(op.Id, 1, null, LocateMember(text, op)));
                    continue;
                }
                var (next, hits) = op.Kind switch
                {
                    "drop-member" => DropMember(text, op),
                    "retarget-type" => RetargetType(text, op),
                    "drop-attribute" => DropAttribute(text, op),
                    _ => throw new OpException("unknown op " + op.Kind),
                };
                results.Add(new OpResult(op.Id, hits, null, null, next != text));
                text = next;
            }
            catch (OpException e)
            {
                results.Add(new OpResult(op.Id, 0, e.Message));
            }
            catch (Exception e)
            {
                // A bug in an op must name the op and file, not take the whole batch down.
                results.Add(new OpResult(op.Id, 0, "failed inside the tool: " + e.GetType().Name + ": " + e.Message));
            }
        }
        // A transform must never leave broken syntax behind: compare the syntax errors before and after.
        var baseline = file.Original ?? file.Text;
        if (text != baseline)
            foreach (var options in new[] { Options, ReleaseOptions })
            {
                // Compared by error code, not line: text rules may move lines, and an error upstream has stays allowed.
                var before = SyntaxErrors(baseline, options).GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.Count());
                var added = SyntaxErrors(text, options).GroupBy(d => d.Code)
                    .Where(g => g.Count() > before.GetValueOrDefault(g.Key)).SelectMany(g => g.Skip(before.GetValueOrDefault(g.Key))).ToList();
                if (added.Count == 0) continue;
                errors.Add("the transforms leave " + added.Count + " new syntax error(s)" + (options == ReleaseOptions ? " (without DEBUG)" : "") +
                    ", the first: " + added[0].Text);
                break;
            }
        return new FileResponse(file.Path, text, results, errors);
    }

    static SyntaxTree Parse(string text) => CSharpSyntaxTree.ParseText(text, Options);

    // Syntax errors: the code, and "line N: CSxxxx message" for the report.
    static List<(string Code, string Text)> SyntaxErrors(string text, CSharpParseOptions options) =>
        CSharpSyntaxTree.ParseText(text, options).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => (d.Id, "line " + (d.Location.GetLineSpan().StartLinePosition.Line + 1) + ": " + d.Id + " " + d.GetMessage()))
            .ToList();

    // Fails when the name appears in code an #if leaves out (with a Debug build's symbols): the op can't see it there,
    // so it would leave it untransformed while reporting success.
    static void Inactive(SyntaxNode root, string name, Op op)
    {
        var word = new Regex(@"(?<![\w@])" + Regex.Escape(name) + @"(?!\w)");
        foreach (var t in root.DescendantTrivia(descendIntoTrivia: true))
            if (t.IsKind(SyntaxKind.DisabledTextTrivia) && word.IsMatch(t.ToString()))
                throw new OpException(name + " also appears in code an #if leaves out (line " + (t.GetLocation().GetLineSpan().StartLinePosition.Line + 1) +
                    "), which " + op.Kind + " can't see: use a text rule there");
    }

    static string Need(string? value, string field, Op op) =>
        string.IsNullOrWhiteSpace(value) ? throw new OpException(op.Kind + " needs \"" + field + "\"") : value;

    static string Squash(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    // ---- locate-member (a measurement aid: tools/measure-roslyn.mjs) ----

    // The member declared at offset At, as "member|type|parameters|overloads": what a drop-member op needs to name it,
    // and how many members of the same type share its name.
    static string LocateMember(string text, Op op)
    {
        var root = Parse(text).GetRoot();
        var at = op.At ?? throw new OpException("locate-member needs \"at\"");
        var m = root.FindToken(at).Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(x => x is not BaseNamespaceDeclarationSyntax)
            ?? throw new OpException("no member at " + at);
        var name = MemberName(m) ?? throw new OpException("the member at " + at + " has no name this tool can drop by");
        var chain = m.Ancestors().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text).Reverse().ToArray();
        var overloads = root.DescendantNodes(n => n is not BlockSyntax and not ArrowExpressionClauseSyntax)
            .OfType<MemberDeclarationSyntax>().Count(x => Declares(x, name) && InType(x, chain));
        return name + "|" + string.Join(".", chain) + "|" + (ParameterList(m) ?? "") + "|" + overloads;
    }

    // ---- drop-member ----

    static (string, int) DropMember(string text, Op op)
    {
        var member = Need(op.Member, "member", op);
        var root = Parse(text).GetRoot();
        var path = op.Type is null ? [] : op.Type.Split('.').Select(s => s.Trim()).ToArray();
        var wanted = op.Parameters is null ? null : Squash(op.Parameters);
        var found = root.DescendantNodes(n => n is not BlockSyntax and not ArrowExpressionClauseSyntax)
            .OfType<MemberDeclarationSyntax>()
            .Where(m => Declares(m, member) && InType(m, path) && (wanted is null || ParameterList(m) == wanted))
            .ToList();
        Inactive(root, member, op);
        if (found.Count != 1)
            throw new OpException("matched " + found.Count + " members, expected 1 (" + Describe(op) + ")");
        var m = found[0];
        if (m is BaseFieldDeclarationSyntax f && f.Declaration.Variables.Count > 1)
            throw new OpException(member + " is declared with other fields in one declaration, so it cannot be dropped alone");
        var (start, end) = LinesOf(text, m);
        return (Blank(text, start, end), 1);
    }

    // Whether m declares the name (any of a field declaration's variables).
    static bool Declares(MemberDeclarationSyntax m, string name) =>
        m is BaseFieldDeclarationSyntax f ? f.Declaration.Variables.Any(v => v.Identifier.Text == name) : MemberName(m) == name;

    static string Describe(Op op) => (op.Type is null ? "" : op.Type + ".") + op.Member + (op.Parameters ?? "");

    static string? MemberName(MemberDeclarationSyntax m) => m switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        MethodDeclarationSyntax x => x.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        IndexerDeclarationSyntax => "this",
        EnumMemberDeclarationSyntax e => e.Identifier.Text,
        BaseFieldDeclarationSyntax f => f.Declaration.Variables.Count == 1
            ? f.Declaration.Variables[0].Identifier.Text
            : f.Declaration.Variables.Select(v => v.Identifier.Text).FirstOrDefault(),
        _ => null,
    };

    // The containing types, outermost first, must end with path; with no path, the member is a top-level type.
    static bool InType(MemberDeclarationSyntax m, string[] path)
    {
        var chain = m.Ancestors().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text).Reverse().ToArray();
        if (path.Length == 0) return chain.Length == 0;
        return chain.Length >= path.Length && chain.AsSpan(chain.Length - path.Length).SequenceEqual(path);
    }

    static string? ParameterList(MemberDeclarationSyntax m)
    {
        var list = m switch
        {
            BaseMethodDeclarationSyntax x => x.ParameterList.Parameters,
            DelegateDeclarationSyntax d => d.ParameterList.Parameters,
            IndexerDeclarationSyntax i => i.ParameterList.Parameters,
            _ => (SeparatedSyntaxList<ParameterSyntax>?)null,
        };
        return list is null ? null : "(" + string.Join(",", list.Value.Select(p => Squash(p.Type?.ToString() ?? ""))) + ")";
    }

    // The member's lines: from its first doc-comment line (a run of /// lines right above it; a blank line or another
    // comment ends the run) to the end of the line it ends on. Attributes are part of the member.
    static (int, int) LinesOf(string text, MemberDeclarationSyntax m)
    {
        var start = m.SpanStart;
        foreach (var t in m.GetLeadingTrivia().Reverse())
        {
            if (t.IsKind(SyntaxKind.WhitespaceTrivia)) continue;
            if (t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                start = t.FullSpan.Start; // Span leaves out the leading /// (the first token's leading trivia)
                continue;
            }
            break;
        }
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        if (start == 0) lineStart = 0;
        if (text[lineStart..start].Trim().Length > 0)
            throw new OpException("code precedes the member on its first line");
        var end = m.Span.End;
        var lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;
        var next = m.GetLastToken().GetNextToken();
        if (!next.IsKind(SyntaxKind.None) && !next.IsKind(SyntaxKind.EndOfFileToken) && next.SpanStart < lineEnd)
            throw new OpException("code follows the member on its last line");
        return (lineStart, lineEnd);
    }

    // Blanks [start, end) line by line, keeping each line ending, so later line numbers don't move.
    static string Blank(string text, int start, int end)
    {
        var sb = new StringBuilder(text.Length);
        sb.Append(text, 0, start);
        for (var i = start; i < end; i++) if (text[i] is '\r' or '\n') sb.Append(text[i]);
        sb.Append(text, end, text.Length - end);
        return sb.ToString();
    }

    // ---- retarget-type ----

    static (string, int) RetargetType(string text, Op op)
    {
        var from = Unglobal(Squash(Need(op.From, "from", op)));
        var to = op.To ?? throw new OpException("retarget-type needs \"to\"");
        if (to.Contains('\n')) throw new OpException("\"to\" must be one line");
        var root = Parse(text).GetRoot();
        var edits = new List<(int Start, int End)>();
        foreach (var n in root.DescendantNodes())
        {
            if (n is not (NameSyntax or MemberAccessExpressionSyntax)) continue;
            // Only the whole name: never the right-hand part of a qualified name or member access (b in a.b), or a
            // name that is itself the left part of a longer match (handled at the longer node).
            if (n.Parent is QualifiedNameSyntax q && q.Right == n) continue;
            if (n.Parent is MemberAccessExpressionSyntax ma && ma.Name == n) continue;
            if (n.Parent is AliasQualifiedNameSyntax aq && aq.Name == n) continue;
            if (n.Parent is MemberBindingExpressionSyntax) continue;
            if (n is IdentifierNameSyntax && n.Parent is NameColonSyntax or NameEqualsSyntax) continue;
            // A namespace's own name is not a reference to a type.
            if (n.Parent is BaseNamespaceDeclarationSyntax ns && ns.Name == n) continue;
            var p = PathOf(n);
            if (p is null || Unglobal(p.Value.Text) != from) continue;
            edits.Add((n.SpanStart, p.Value.End));
        }
        if (edits.Count == 0) throw new OpException("no longer matches: no reference to " + op.From + " in code");
        Inactive(root, from.Split('.')[^1], op);
        // A longer match contains no shorter one (from is one name), so the spans don't overlap.
        var sb = new StringBuilder(text);
        foreach (var (s, e) in edits.OrderByDescending(x => x.Start)) sb.Remove(s, e - s).Insert(s, to);
        return (sb.ToString(), edits.Count);
    }

    static string Unglobal(string s) => s.StartsWith("global::", StringComparison.Ordinal) ? s[8..] : s;

    // A dotted name's text (type arguments of the last part left out) and where that text ends.
    static (string Text, int End)? PathOf(SyntaxNode n)
    {
        switch (n)
        {
            case IdentifierNameSyntax i: return (i.Identifier.Text, i.Span.End);
            case GenericNameSyntax g: return (g.Identifier.Text, g.Identifier.Span.End);
            case AliasQualifiedNameSyntax a:
                return PathOf(a.Name) is { } an ? (a.Alias.Identifier.Text + "::" + an.Text, an.End) : null;
            case QualifiedNameSyntax q:
                return Left(q.Left) is { } ql && PathOf(q.Right) is { } qr ? (ql + "." + qr.Text, qr.End) : null;
            case MemberAccessExpressionSyntax m when m.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                return Left(m.Expression) is { } ml && PathOf(m.Name) is { } mr ? (ml + "." + mr.Text, mr.End) : null;
            default: return null;
        }
    }

    // The left part of a dotted name, written out with its type arguments; null if it isn't a name.
    static string? Left(SyntaxNode n) => n switch
    {
        IdentifierNameSyntax or GenericNameSyntax or AliasQualifiedNameSyntax or QualifiedNameSyntax => Squash(n.ToString()),
        MemberAccessExpressionSyntax m when m.IsKind(SyntaxKind.SimpleMemberAccessExpression) && Left(m.Expression) is { } l => l + "." + Squash(m.Name.ToString()),
        _ => null,
    };

    // ---- drop-attribute ----

    static (string, int) DropAttribute(string text, Op op)
    {
        var wanted = Unglobal(Squash(Need(op.Attribute, "attribute", op)));
        var root = Parse(text).GetRoot();
        var edits = new List<(int Start, int End)>();
        var hits = 0;
        foreach (var list in root.DescendantNodes().OfType<AttributeListSyntax>())
        {
            var match = list.Attributes.Where(a => AttributeIs(a, wanted)).ToList();
            if (match.Count == 0) continue;
            hits += match.Count;
            if (match.Count == list.Attributes.Count)
            {
                // The whole [...] goes, with the spaces after it on its line; a line left empty keeps its line ending.
                var end = list.Span.End;
                while (end < text.Length && text[end] is ' ' or '\t') end++;
                edits.Add((list.SpanStart, end));
                continue;
            }
            // Each run of matched attributes goes with the separators that join it to the list: up to the kept attribute
            // after it, or, for a run that ends the list, from the separator after the kept attribute before it.
            var attrs = list.Attributes;
            for (var i = 0; i < attrs.Count; i++)
            {
                if (!match.Contains(attrs[i])) continue;
                var j = i;
                while (j + 1 < attrs.Count && match.Contains(attrs[j + 1])) j++;
                edits.Add(j < attrs.Count - 1
                    ? (attrs[i].SpanStart, attrs[j + 1].SpanStart)
                    : (attrs.GetSeparator(i - 1).SpanStart, attrs[j].Span.End));
                i = j;
            }
        }
        if (hits == 0) throw new OpException("no longer matches: no attribute " + op.Attribute + " in code");
        var bare = wanted.Split('.')[^1];
        Inactive(root, bare.EndsWith("Attribute", StringComparison.Ordinal) ? bare[..^9] : bare, op);
        var sb = new StringBuilder(text);
        foreach (var (s, e) in edits.OrderByDescending(x => x.Start)) sb.Remove(s, e - s);
        return (sb.ToString(), hits);
    }

    static bool AttributeIs(AttributeSyntax a, string wanted)
    {
        var name = Unglobal(Squash(a.Name.ToString()));
        static string Bare(string s) => s.EndsWith("Attribute", StringComparison.Ordinal) ? s[..^9] : s;
        if (wanted.Contains('.')) return Bare(name) == Bare(wanted);
        return Bare(name.Split('.')[^1]) == Bare(wanted);
    }
}
