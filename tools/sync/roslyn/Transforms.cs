using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Isotope.Sync.Roslyn;

public sealed record Request(List<FileRequest>? Files);
public sealed record FileRequest(string Path, string Text, List<Op> Ops);
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
public sealed record OpResult(string Id, int Hits, string? Error, string? Info = null);
public sealed record FileResponse(string Path, string Text, List<OpResult> Results, List<string> Errors);
public sealed record Response(List<FileResponse> Files);

sealed class OpException(string message) : Exception(message);

public static class Transforms
{
    // Preview, so whatever the pinned SDK's compiler accepts parses. No preprocessor symbols: code under an #if is
    // inactive, so it is neither matched nor changed.
    static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.Parse);

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
                text = next;
                results.Add(new OpResult(op.Id, hits, null));
            }
            catch (OpException e)
            {
                results.Add(new OpResult(op.Id, 0, e.Message));
            }
        }
        // A transform must never leave broken syntax behind: compare the syntax errors before and after.
        if (text != file.Text)
        {
            var before = SyntaxErrors(file.Text);
            var added = SyntaxErrors(text).Where(d => !before.Remove(d)).ToList();
            if (added.Count > 0)
                errors.Add("the Roslyn transforms leave " + added.Count + " new syntax error(s), the first: " + added[0]);
        }
        return new FileResponse(file.Path, text, results, errors);
    }

    static SyntaxTree Parse(string text) => CSharpSyntaxTree.ParseText(text, Options);

    // Syntax errors as "line N: CSxxxx message" (line numbers are kept by every op, so they compare across ops).
    static List<string> SyntaxErrors(string text) => Parse(text).GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => "line " + (d.Location.GetLineSpan().StartLinePosition.Line + 1) + ": " + d.Id + " " + d.GetMessage())
        .ToList();

    static string Need(string? value, string field, Op op) =>
        string.IsNullOrWhiteSpace(value) ? throw new OpException(op.Kind + " needs \"" + field + "\"") : value;

    static string Squash(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    // ---- locate-member (a measurement aid: tools/measure-roslyn-drops.mjs) ----

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
            .OfType<MemberDeclarationSyntax>().Count(x => MemberName(x) == name && InType(x, chain));
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
            .Where(m => MemberName(m) == member && InType(m, path) && (wanted is null || ParameterList(m) == wanted))
            .ToList();
        if (found.Count != 1)
            throw new OpException("matched " + found.Count + " members, expected 1 (" + Describe(op) + ")");
        var m = found[0];
        if (m is BaseFieldDeclarationSyntax f && f.Declaration.Variables.Count > 1)
            throw new OpException(member + " is declared with other fields in one declaration, so it cannot be dropped alone");
        var (start, end) = LinesOf(text, m);
        return (Blank(text, start, end), 1);
    }

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
            var p = PathOf(n);
            if (p is null || Unglobal(p.Value.Text) != from) continue;
            edits.Add((n.SpanStart, p.Value.End));
        }
        if (edits.Count == 0) throw new OpException("no longer matches: no reference to " + op.From + " in code");
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
            var attrs = list.Attributes;
            for (var i = 0; i < attrs.Count; i++)
            {
                if (!match.Contains(attrs[i])) continue;
                // An attribute and the separator after it (or before it, for the last one).
                edits.Add(i < attrs.Count - 1
                    ? (attrs[i].SpanStart, attrs[i + 1].SpanStart)
                    : (attrs.GetSeparator(i - 1).SpanStart, attrs[i].Span.End));
            }
        }
        if (hits == 0) throw new OpException("no longer matches: no attribute " + op.Attribute + " in code");
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
