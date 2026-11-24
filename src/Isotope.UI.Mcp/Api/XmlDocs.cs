using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Isotope.UI.Mcp.Api;

/// <summary>
/// An assembly's XML doc file (the one the package ships next to its DLL), read once, with each member's comment
/// rendered as Markdown.
/// </summary>
internal sealed partial class XmlDocs
{
    readonly Dictionary<string, XElement> _members = new(StringComparer.Ordinal);

    public XmlDocs(XDocument? doc)
    {
        if (doc?.Root?.Element("members") is not { } members) return;
        foreach (var m in members.Elements("member"))
            if (m.Attribute("name")?.Value is { } name) _members[name] = m;
    }

    /// <summary>The doc file next to an assembly (<c>Isotope.UI.Avalonia.xml</c>), or an empty one if it has none.</summary>
    public static XmlDocs For(System.Reflection.Assembly assembly)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        return new XmlDocs(File.Exists(path) ? XDocument.Load(path) : null);
    }

    public int Count => _members.Count;

    public bool Has(string id) => Element(id) is not null;

    public IEnumerable<string> Ids => _members.Keys;

    /// <summary>The member's comment, following <c>&lt;inheritdoc cref="..."/&gt;</c> (as C# 14 extension members have).</summary>
    XElement? Element(string id, int depth = 0)
    {
        if (!_members.TryGetValue(id, out var m)) return null;
        if (depth < 4 && m.Element("inheritdoc")?.Attribute("cref")?.Value is { } cref && m.Element("summary") is null)
            return Element(cref, depth + 1) ?? m;
        return m;
    }

    /// <summary>The summary as one line of text (empty when there is none).</summary>
    public string Summary(string id) => Element(id)?.Element("summary") is { } s ? OneLine(Render(s)) : "";

    /// <summary>The whole comment as Markdown: summary, remarks, type parameters, parameters, return value, exceptions.</summary>
    public string Full(string id)
    {
        if (Element(id) is not { } m) return "";
        var sb = new StringBuilder();
        if (m.Element("summary") is { } summary) sb.AppendLine(Render(summary).Trim()).AppendLine();
        if (m.Element("remarks") is { } remarks) sb.AppendLine("**Remarks.** " + Render(remarks).Trim()).AppendLine();
        AppendNamed(sb, m, "typeparam", "Type parameters");
        AppendNamed(sb, m, "param", "Parameters");
        if (m.Element("returns") is { } returns) sb.AppendLine("**Returns:** " + OneLine(Render(returns))).AppendLine();
        var exceptions = m.Elements("exception").ToList();
        if (exceptions.Count > 0)
        {
            sb.AppendLine("**Exceptions:**");
            foreach (var e in exceptions) sb.AppendLine("- `" + CrefName(e.Attribute("cref")?.Value ?? "") + "`: " + OneLine(Render(e)));
            sb.AppendLine();
        }
        if (m.Element("example") is { } example) sb.AppendLine("**Example.** " + Render(example).Trim()).AppendLine();
        return sb.ToString().Trim();
    }

    static void AppendNamed(StringBuilder sb, XElement m, string tag, string heading)
    {
        var items = m.Elements(tag).ToList();
        if (items.Count == 0) return;
        sb.AppendLine("**" + heading + ":**");
        foreach (var p in items) sb.AppendLine("- `" + p.Attribute("name")?.Value + "`: " + OneLine(Render(p)));
        sb.AppendLine();
    }

    /// <summary>A doc element's content as Markdown text.</summary>
    internal static string Render(XElement element)
    {
        var sb = new StringBuilder();
        foreach (var node in element.Nodes()) RenderNode(node, sb);
        return Blank().Replace(sb.ToString(), "\n\n");
    }

    static void RenderNode(XNode node, StringBuilder sb)
    {
        switch (node)
        {
            case XText text:
                sb.Append(Space().Replace(text.Value, " "));
                break;
            case XElement e:
                switch (e.Name.LocalName)
                {
                    case "see" or "seealso":
                        if (e.Attribute("langword")?.Value is { } word) sb.Append('`').Append(word).Append('`');
                        else if (e.Attribute("href")?.Value is { } href) sb.Append('[').Append(e.Value.Length > 0 ? e.Value : href).Append("](").Append(href).Append(')');
                        else if (e.Value.Length > 0) sb.Append(e.Value);
                        else sb.Append('`').Append(CrefName(e.Attribute("cref")?.Value ?? "")).Append('`');
                        break;
                    case "paramref" or "typeparamref":
                        sb.Append('`').Append(e.Attribute("name")?.Value).Append('`');
                        break;
                    case "c":
                        sb.Append('`').Append(e.Value).Append('`');
                        break;
                    case "code":
                        sb.Append("\n\n```csharp\n").Append(Dedent(e.Value)).Append("\n```\n\n");
                        break;
                    case "para":
                        sb.Append("\n\n").Append(Render(e).Trim()).Append("\n\n");
                        break;
                    case "list":
                        sb.Append("\n\n");
                        foreach (var item in e.Elements("item"))
                        {
                            var term = item.Element("term") is { } t ? Render(t).Trim() : null;
                            var description = item.Element("description") is { } d ? Render(d).Trim() : Render(item).Trim();
                            sb.Append("- ").Append(term is null ? description : "**" + term + "**: " + description).Append('\n');
                        }
                        sb.Append('\n');
                        break;
                    case "br":
                        sb.Append('\n');
                        break;
                    default:
                        foreach (var child in e.Nodes()) RenderNode(child, sb);
                        break;
                }
                break;
        }
    }

    /// <summary>
    /// A cref's short name: <c>T:Avalonia.Controls.Button</c> is <c>Button</c>, and a member keeps its type,
    /// <c>M:Isotope.UI.Factories.Button(System.String)</c> is <c>Factories.Button</c>. A C# 14 extension block's
    /// compiler-made type (<c>&lt;G&gt;$…</c>) is left out.
    /// </summary>
    internal static string CrefName(string cref)
    {
        if (cref.Length > 2 && cref[1] == ':') { var kind = cref[0]; cref = cref[2..]; return Short(cref, kind == 'T' || kind == 'N' ? 1 : 2); }
        return Short(cref, 1);

        static string Short(string name, int keep)
        {
            var paren = name.IndexOf('(');
            if (paren >= 0) name = name[..paren];
            var parts = name.Split('.').Where(p => !p.StartsWith('<')).Select(p => Arity().Replace(p, "")).ToArray();
            return string.Join(".", parts.Skip(Math.Max(0, parts.Length - keep)));
        }
    }

    internal static string OneLine(string text) => Space().Replace(text, " ").Trim();

    static string Dedent(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0) start++;
        var end = lines.Length;
        while (end > start && lines[end - 1].Trim().Length == 0) end--;
        lines = lines[start..end];
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Trim().Length == 0 ? "" : l[indent..].TrimEnd()));
    }

    [GeneratedRegex(@"[ \t\r\n]+")]
    private static partial Regex Space();

    [GeneratedRegex(@"[ \t]*\n[ \t]*(\n[ \t]*)+")]
    private static partial Regex Blank();

    [GeneratedRegex(@"`+\d+")]
    private static partial Regex Arity();
}
