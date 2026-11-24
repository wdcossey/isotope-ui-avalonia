using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Isotope.UI.Migration;

/// <summary>
/// Finds Reactor and WinUI code in a project that references Isotope (the project's package references switched
/// first), and reports what changes, with the edit in the diagnostic where it is mechanical. Run it with
/// <c>dotnet format analyzers</c> until nothing changes: the member and parameter renames bind to Isotope's types, so
/// they appear once the namespaces are migrated.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationAnalyzer : DiagnosticAnalyzer
{
    readonly MigrationTables? _tables;

    public MigrationAnalyzer() { }

    internal MigrationAnalyzer(MigrationTables tables) => _tables = tables;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules.All;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            // Not an Isotope project: there is nothing to migrate to (and the renames could not be checked).
            if (Session.FindNamespace(start.Compilation.GlobalNamespace, "Isotope.UI") is not { } isotope) return;
            var session = new Session(_tables ?? MigrationTables.Default, start.Compilation, isotope);
            start.RegisterSyntaxNodeAction(session.UsingDirective, SyntaxKind.UsingDirective);
            start.RegisterSyntaxNodeAction(session.IdentifierName, SyntaxKind.IdentifierName);
            start.RegisterSyntaxNodeAction(session.MemberAccess, SyntaxKind.SimpleMemberAccessExpression);
            start.RegisterSyntaxNodeAction(session.MemberBinding, SyntaxKind.MemberBindingExpression);
            start.RegisterSyntaxNodeAction(session.ObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
            start.RegisterSyntaxNodeAction(session.Initializer, SyntaxKind.WithInitializerExpression, SyntaxKind.ObjectInitializerExpression);
            start.RegisterSyntaxNodeAction(session.Invocation, SyntaxKind.InvocationExpression);
            start.RegisterSyntaxNodeAction(session.ClassDeclaration, SyntaxKind.ClassDeclaration);
        });
    }

    /// <summary>One compilation's analysis: the tables, and what is looked up once.</summary>
    sealed class Session
    {
        readonly MigrationTables _t;
        readonly Compilation _compilation;
        readonly INamespaceSymbol _isotope;
        readonly HashSet<string> _roots;
        readonly Lazy<HashSet<string>> _globalUsings;
        // Applied type renames, Isotope name -> Reactor name, so a renamed record still finds its member entries. A record
        // that is also a Reactor name (ScrollViewerElement, which ScrollViewElement became) finds its own entries first.
        readonly Dictionary<string, string> _recordAliases = new(StringComparer.Ordinal);

        public Session(MigrationTables tables, Compilation compilation, INamespaceSymbol isotope)
        {
            _t = tables;
            _compilation = compilation;
            _isotope = isotope;
            _roots = new HashSet<string>(tables.WinUI.Roots.Concat(tables.WinUI.Excluded).Select(r => r.Split('.')[0]), StringComparer.Ordinal);
            _globalUsings = new Lazy<HashSet<string>>(() => new HashSet<string>(
                compilation.SyntaxTrees.SelectMany(t => ((CompilationUnitSyntax)t.GetRoot()).Usings)
                    .Where(u => u.GlobalKeyword != default && u.Alias is null && u.StaticKeyword == default && u.Name is not null)
                    .Select(u => NameText(u.Name!)), StringComparer.Ordinal));
            foreach (var type in tables.Renames.Types)
                if (type.Applied && type.Avalonia is not null) _recordAliases[type.Avalonia] = type.Reactor;
        }

        // ── using directives (ISOMIG003) ─────────────────────────────────────────────────────────────────

        public void UsingDirective(SyntaxNodeAnalysisContext ctx)
        {
            var u = (UsingDirectiveSyntax)ctx.Node;
            if (u.Alias is not null || u.StaticKeyword != default || u.Name is null) return;
            var ns = NameText(u.Name);
            if (!_t.WinUI.IsWinUINamespace(ns)) return;
            var isGlobal = u.GlobalKeyword != default;
            var needed = new SortedSet<string>(StringComparer.Ordinal);
            var trees = isGlobal ? _compilation.SyntaxTrees : new[] { u.SyntaxTree };
            foreach (var tree in trees)
            {
                var model = tree == ctx.SemanticModel.SyntaxTree ? ctx.SemanticModel : _compilation.GetSemanticModel(tree);
                foreach (var id in tree.GetRoot(ctx.CancellationToken).DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (!_t.WinUI.ByName.TryGetValue(id.Identifier.ValueText, out var types)) continue;
                    var type = types.FirstOrDefault(x => x.Namespace == ns && x.Avalonia is not null);
                    if (type is null || !IsLeftmost(id) || IsInvoked(id) || InUsing(id) || !Unbound(model, id)) continue;
                    needed.Add(type.AvaloniaNamespace!);
                }
            }
            var what = needed.Count == 0 ? "remove it" : "replace it with Avalonia's " + string.Join(", ", needed);
            Report(ctx, Rules.WinUIUsing, u.GetLocation(), removeLine: true,
                usings: needed.Select(n => isGlobal ? "global " + n : n), args: [ns, what]);
        }

        // ── identifiers: qualified names and simple WinUI or renamed type names ─────────────────────────

        public void IdentifierName(SyntaxNodeAnalysisContext ctx)
        {
            var id = (IdentifierNameSyntax)ctx.Node;
            if (QualifiedTypeRename(ctx, id)) return;
            if (!IsLeftmost(id)) return;
            var text = id.Identifier.ValueText;
            if (_roots.Contains(text)) Chain(ctx, id);
            else SimpleName(ctx, id, text);
        }

        void Chain(SyntaxNodeAnalysisContext ctx, IdentifierNameSyntax root)
        {
            var nodes = ChainFrom(root);
            if (nodes.Count < 2) return;
            var segs = nodes.Select(Segment).ToList();
            var top = nodes[nodes.Count - 1];
            if (top.Parent is BaseNamespaceDeclarationSyntax) return;
            var model = ctx.SemanticModel;
            var rootSymbol = model.GetSymbolInfo(root, ctx.CancellationToken).Symbol;
            if (rootSymbol is not null and not INamespaceSymbol) return;
            var topSymbol = model.GetSymbolInfo(top, ctx.CancellationToken).Symbol;
            if (segs.Count >= 3 && segs[0] == "Microsoft" && segs[1] == "UI" && segs[2] == "Reactor")
            {
                // A project's own namespace under Microsoft.UI.Reactor makes the name bind: Microsoft.UI.Reactor itself is
                // still Reactor's (migrated), but a deeper namespace or a type that binds is the project's own.
                if (topSymbol is not null && !(topSymbol is INamespaceSymbol && segs.Count == 3)) return;
                ReactorChain(ctx, nodes, segs);
                return;
            }
            if (topSymbol is not null) return;
            var full = string.Join(".", segs);
            if (!_t.WinUI.IsWinUINamespace(full)) return;
            // A plain using of a WinUI namespace is ISOMIG003's.
            if (top.Parent is UsingDirectiveSyntax plain && plain.Alias is null && plain.StaticKeyword == default) return;

            for (var k = segs.Count; k >= 2; k--)
            {
                var name = string.Join(".", segs.Take(k));
                if (!_t.WinUI.ByFullName.TryGetValue(name, out var type)) continue;
                var node = nodes[k - 1];
                if (type.Avalonia is null)
                {
                    Report(ctx, Rules.NoAvaloniaType, node.GetLocation(), args: [name, Note(type.Note)]);
                    return;
                }
                if (k < segs.Count)
                {
                    var member = segs[k];
                    if (type.Members.TryGetValue(member, out var mapped))
                    {
                        if (mapped is null) Report(ctx, Rules.NoAvaloniaType, nodes[k].GetLocation(), args: [name + "." + member, Note(type.Note)]);
                        else Report(ctx, Rules.WinUIType, nodes[k].GetLocation(), replace: Prefix(nodes) + type.Avalonia + "." + mapped,
                            args: [name + "." + member, type.Avalonia + "." + mapped]);
                        return;
                    }
                    if (!MemberExists(type, member))
                    {
                        Report(ctx, Rules.NoAvaloniaType, nodes[k].GetLocation(), args: [name + "." + member, " ('" + member + "' isn't a member of " + type.Avalonia + ")"]);
                        return;
                    }
                }
                Report(ctx, Rules.WinUIType, node.GetLocation(), replace: Prefix(nodes) + type.Avalonia, args: [name, type.Avalonia]);
                return;
            }
            if (top.Parent is UsingDirectiveSyntax alias && alias.Alias is not null)
            {
                if (_t.WinUI.Namespaces.TryGetValue(full, out var avaloniaNs) && avaloniaNs is not null)
                    Report(ctx, Rules.WinUIType, top.GetLocation(), replace: avaloniaNs, args: [full, avaloniaNs]);
                else
                    Report(ctx, Rules.NoAvaloniaType, top.GetLocation(), args: [full, ""]);
                return;
            }
            Report(ctx, Rules.WinUINotInTable, top.GetLocation(), args: [full]);
        }

        void ReactorChain(SyntaxNodeAnalysisContext ctx, List<SyntaxNode> nodes, List<string> segs)
        {
            INamespaceOrTypeSymbol current = _isotope;
            var consumed = 3;
            for (var i = 3; i < segs.Count; i++)
            {
                var next = current.GetMembers(segs[i]).OfType<INamespaceOrTypeSymbol>().FirstOrDefault();
                if (next is null) break;
                current = next;
                consumed = i + 1;
                if (current is ITypeSymbol) break;
            }
            var found = current is ITypeSymbol || consumed == segs.Count;
            // The namespace resolves and the next name is a type Isotope renamed or removed (renames.json).
            if (!found && current is INamespaceSymbol && _t.Renames.Types.FirstOrDefault(e => e.Applied && e.Reactor == segs[consumed]) is { } type)
            {
                var name = string.Join(".", segs.Take(consumed + 1));
                if (type.Avalonia is null)
                    Report(ctx, Rules.NameRemoved, nodes[consumed].GetLocation(), args: ["Type", name, type.Note ?? ""]);
                else
                {
                    var isotope = "Isotope.UI" + string.Concat(segs.Skip(3).Take(consumed - 3).Select(s => "." + s)) + "." + type.Avalonia;
                    Report(ctx, Rules.NameRenamed, nodes[consumed].GetLocation(), replace: Prefix(nodes) + isotope, args: ["Type", name, isotope]);
                }
                return;
            }
            // The message names the namespace or type: up to what resolved, or up to the segment that didn't.
            var shown = found ? consumed : Math.Min(consumed + 1, segs.Count);
            var reactorName = string.Join(".", segs.Take(shown));
            var isotopeName = "Isotope.UI" + string.Concat(segs.Skip(3).Take(shown - 3).Select(s => "." + s));
            if (found)
                Report(ctx, Rules.ReactorNamespace, nodes[2].GetLocation(), replace: Prefix(nodes) + "Isotope.UI", args: [reactorName, isotopeName]);
            else
                Report(ctx, Rules.NoIsotopeNamespace, nodes[shown - 1].GetLocation(), args: [reactorName, isotopeName]);
        }

        void SimpleName(SyntaxNodeAnalysisContext ctx, IdentifierNameSyntax id, string text)
        {
            var winui = _t.WinUI.ByName.TryGetValue(text, out var types) ? types : null;
            var renamed = _t.Renames.Types.FirstOrDefault(e => e.Applied && e.Reactor == text);
            if (winui is null && renamed is null) return;
            if (IsInvoked(id) || InUsing(id) || !Unbound(ctx.SemanticModel, id)) return;
            if (renamed is not null)
            {
                ReportTypeRename(ctx, id, renamed);
                return;
            }
            var type = Pick(winui!, id);
            if (type is null) return;
            if (type.Avalonia is null)
            {
                Report(ctx, Rules.NoAvaloniaType, id.GetLocation(), args: [type.FullName, Note(type.Note)]);
                return;
            }
            var usings = new[] { type.AvaloniaNamespace! };
            if (id.Parent is MemberAccessExpressionSyntax ma && ma.Expression == id)
            {
                var member = ma.Name.Identifier.ValueText;
                if (type.Members.TryGetValue(member, out var mapped))
                {
                    if (mapped is null)
                        Report(ctx, Rules.NoAvaloniaType, ma.GetLocation(), args: [type.Name + "." + member, Note(type.Note)]);
                    else if (mapped != member || type.AvaloniaName != type.Name)
                        Report(ctx, Rules.WinUIType, ma.GetLocation(), replace: type.AvaloniaName + "." + mapped, usings: usings,
                            args: [type.FullName + "." + member, type.Avalonia + "." + mapped]);
                    return;
                }
                if (!MemberExists(type, member))
                {
                    Report(ctx, Rules.NoAvaloniaType, ma.GetLocation(), args: [type.Name + "." + member, " ('" + member + "' isn't a member of " + type.Avalonia + ")"]);
                    return;
                }
            }
            // A type under the same name comes in with its namespace (ISOMIG003 on the WinUI using).
            if (type.AvaloniaName != type.Name)
                Report(ctx, Rules.WinUIType, id.GetLocation(), replace: type.AvaloniaName, usings: usings, args: [type.FullName, type.Avalonia]);
        }

        /// <summary>The last name of `Isotope.UI.Core.GridDefinition`: a type the table renamed or removed, after an Isotope namespace.</summary>
        bool QualifiedTypeRename(SyntaxNodeAnalysisContext ctx, IdentifierNameSyntax id)
        {
            ExpressionSyntax? left = id.Parent switch
            {
                QualifiedNameSyntax q when q.Right == id => q.Left,
                MemberAccessExpressionSyntax m when m.Name == id => m.Expression,
                _ => null,
            };
            if (left is null || ctx.SemanticModel.GetSymbolInfo(left, ctx.CancellationToken).Symbol is not INamespaceSymbol ns) return false;
            var nsName = ns.ToDisplayString();
            if (nsName != "Isotope.UI" && !nsName.StartsWith("Isotope.UI.", StringComparison.Ordinal)) return false;
            var renamed = _t.Renames.Types.FirstOrDefault(e => e.Applied && e.Reactor == id.Identifier.ValueText);
            if (renamed is null || !Unbound(ctx.SemanticModel, id)) return false;
            ReportTypeRename(ctx, id, renamed);
            return true;
        }

        static void ReportTypeRename(SyntaxNodeAnalysisContext ctx, IdentifierNameSyntax id, NameEntry renamed)
        {
            var text = id.Identifier.ValueText;
            if (renamed.Avalonia is null) Report(ctx, Rules.NameRemoved, id.GetLocation(), args: ["Type", text, renamed.Note ?? ""]);
            else Report(ctx, Rules.NameRenamed, id.GetLocation(), replace: renamed.Avalonia, args: ["Type", text, renamed.Avalonia]);
        }

        // ── element members: with and object initializers, member access ────────────────────────────────

        public void Initializer(SyntaxNodeAnalysisContext ctx)
        {
            var init = (InitializerExpressionSyntax)ctx.Node;
            var type = init.Parent switch
            {
                WithExpressionSyntax w => ctx.SemanticModel.GetTypeInfo(w.Expression, ctx.CancellationToken).Type,
                BaseObjectCreationExpressionSyntax oc => ctx.SemanticModel.GetTypeInfo(oc, ctx.CancellationToken).Type,
                _ => null,
            };
            if (type is null or IErrorTypeSymbol) return;
            var assigned = init.Expressions.OfType<AssignmentExpressionSyntax>().Select(a => a.Left).OfType<IdentifierNameSyntax>()
                .Select(l => l.Identifier.ValueText).ToList();
            foreach (var expr in init.Expressions)
                if (expr is AssignmentExpressionSyntax { Left: IdentifierNameSyntax left } assignment)
                    MemberUse(ctx, type, left, assignment.Right, assigned);
        }

        public void MemberBinding(SyntaxNodeAnalysisContext ctx)
        {
            // `box?.Value`: the receiver is the conditional access's expression.
            var binding = (MemberBindingExpressionSyntax)ctx.Node;
            if (binding.Name is not IdentifierNameSyntax name) return;
            if (binding.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>() is not { } access || access.WhenNotNull.SpanStart != binding.SpanStart) return;
            var receiver = Constraint(ctx.SemanticModel.GetTypeInfo(access.Expression, ctx.CancellationToken).Type);
            if (receiver is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) receiver = nullable.TypeArguments[0];
            if (receiver is null or IErrorTypeSymbol) return;
            if (binding.Parent is InvocationExpressionSyntax inv && inv.Expression == binding)
                InvokedMember(ctx, binding, receiver, name, isStatic: false);
            else
                MemberUse(ctx, receiver, name, null);
        }

        public void ObjectCreation(SyntaxNodeAnalysisContext ctx)
        {
            // `new TextBoxElement(Value: "x")`: a record's positional parameters have its members' names.
            var creation = (BaseObjectCreationExpressionSyntax)ctx.Node;
            if (creation.ArgumentList is not { } list) return;
            var model = ctx.SemanticModel;
            if (model.GetTypeInfo(creation, ctx.CancellationToken).Type is not { } type || type is IErrorTypeSymbol || !IsIsotope(type)) return;
            var info = model.GetSymbolInfo(creation, ctx.CancellationToken);
            if (info.Symbol is not null) return;
            var ctors = info.CandidateSymbols.OfType<IMethodSymbol>().ToList();
            var named = list.Arguments.Where(a => a.NameColon is not null).Select(a => a.NameColon!.Name.Identifier.ValueText).ToList();
            foreach (var arg in list.Arguments)
            {
                if (arg.NameColon is null) continue;
                var parameter = arg.NameColon.Name.Identifier.ValueText;
                if (ctors.Any(c => c.Parameters.Any(p => p.Name == parameter))) continue;
                var entry = FindMember(type, parameter, out var record);
                if (entry is null || entry.Kind == "kept") continue;
                var target = entry.SingleAvalonia;
                // A target already named here would be named twice: leave it to the compiler's error.
                if (target is not null && named.Contains(target)) continue;
                var targetParameter = target is null ? null : ctors.SelectMany(c => c.Parameters).FirstOrDefault(p => p.Name == target);
                var fits = entry.Kind == "rename" || (targetParameter is not null && Converts(model, arg.Expression, targetParameter.Type));
                if (target is not null && target != parameter && targetParameter is not null && fits)
                    Report(ctx, Rules.MemberRenamed, arg.NameColon.Name.GetLocation(), replace: target, args: [record, parameter, target]);
                else
                    Report(ctx, Rules.MemberShape, arg.NameColon.Name.GetLocation(), args: [record, parameter, entry.Note ?? ""]);
            }
        }

        public void MemberAccess(SyntaxNodeAnalysisContext ctx)
        {
            var ma = (MemberAccessExpressionSyntax)ctx.Node;
            if (ma.Name is not IdentifierNameSyntax name) return;
            var model = ctx.SemanticModel;
            var receiverSymbol = model.GetSymbolInfo(ma.Expression, ctx.CancellationToken).Symbol;
            if (receiverSymbol is INamespaceSymbol) return;
            var receiver = Constraint(receiverSymbol as INamedTypeSymbol ?? model.GetTypeInfo(ma.Expression, ctx.CancellationToken).Type);
            if (receiver is null or IErrorTypeSymbol) return;
            var text = name.Identifier.ValueText;
            if (ma.Parent is InvocationExpressionSyntax inv && inv.Expression == ma)
            {
                InvokedMember(ctx, ma, receiver, name, isStatic: receiverSymbol is INamedTypeSymbol);
                return;
            }
            var value = ma.Parent is AssignmentExpressionSyntax a && a.Left == ma ? a.Right : null;
            MemberUse(ctx, receiver, name, value);
        }

        /// <summary>`el.HeaderContent(x)` or `el?.HeaderContent(x)`: a modifier the table renamed, or a member Isotope lacks.</summary>
        void InvokedMember(SyntaxNodeAnalysisContext ctx, ExpressionSyntax call, ITypeSymbol receiver, IdentifierNameSyntax name, bool isStatic)
        {
            var model = ctx.SemanticModel;
            var text = name.Identifier.ValueText;
            {
                var info = model.GetSymbolInfo(call, ctx.CancellationToken);
                if (info.Symbol is not null || !info.CandidateSymbols.IsEmpty) return;
                if (!isStatic)
                {
                    var modifier = _t.Renames.Modifiers.FirstOrDefault(m => m.Applied && m.Reactor == text && (m.Target is null || IsOrDerivesFrom(receiver, m.Target, _recordAliases)));
                    if (modifier is not null)
                    {
                        if (modifier.Avalonia is null) Report(ctx, Rules.NameRemoved, name.GetLocation(), args: ["Modifier", text, modifier.Note ?? ""]);
                        else Report(ctx, Rules.NameRenamed, name.GetLocation(), replace: modifier.Avalonia, args: ["Modifier", text, modifier.Avalonia]);
                        return;
                    }
                }
                if (receiver.Name is "ReactorApp" && text is "Run" or "OpenWindow") return; // Invocation's
                if (receiver.Name is "Factories" && IsFactoryEntry(text)) return; // Invocation's
                if (IsIsotope(receiver)) Report(ctx, Rules.NoIsotopeMember, name.GetLocation(), args: [receiver.Name, text]);
            }
        }

        void MemberUse(SyntaxNodeAnalysisContext ctx, ITypeSymbol type, IdentifierNameSyntax name, ExpressionSyntax? value,
            List<string>? siblings = null)
        {
            var model = ctx.SemanticModel;
            var member = name.Identifier.ValueText;
            var info = model.GetSymbolInfo(name, ctx.CancellationToken);
            var entry = FindMember(type, member, out var record);
            if (entry is not null)
            {
                if (entry.Kind == "kept") return;
                var target = entry.SingleAvalonia;
                if (target == member)
                {
                    // Retyped: flag it only where the value no longer fits.
                    if (info.Symbol is IPropertySymbol p && value is not null && !Converts(model, value, p.Type))
                        Report(ctx, Rules.MemberShape, name.GetLocation(), args: [record, member, entry.Note ?? ""]);
                    return;
                }
                if (info.Symbol is not null) return;
                // Two members of one initializer becoming one (Label and ContentElement are Content): merge them by hand.
                if (siblings is not null && target is not null && siblings.Any(s => s != member &&
                        (s == target || (FindMember(type, s, out _) is { } other && other.SingleAvalonia == target))))
                {
                    Report(ctx, Rules.MemberShape, name.GetLocation(), args: [record, member, "it and another member here are both " + target + " in Isotope: keep one."]);
                    return;
                }
                if (entry.Kind == "rename" && target is not null)
                {
                    Report(ctx, Rules.MemberRenamed, name.GetLocation(), replace: target, args: [record, member, target]);
                    return;
                }
                // A shape change is a rename where the value fits the new member (Label and ContentElement are Content).
                if (target is not null && value is not null && FindProperty(type, target) is { } property && Converts(model, value, property.Type))
                {
                    Report(ctx, Rules.MemberRenamed, name.GetLocation(), replace: target, args: [record, member, target]);
                    return;
                }
                Report(ctx, Rules.MemberShape, name.GetLocation(), args: [record, member, entry.Note ?? ""]);
                return;
            }
            if (info.Symbol is not null || !info.CandidateSymbols.IsEmpty) return;
            if (IsIsotope(type)) Report(ctx, Rules.NoIsotopeMember, name.GetLocation(), args: [type.Name, member]);
        }

        // ── invocations: factories, named arguments, hosting ─────────────────────────────────────────────

        public void Invocation(SyntaxNodeAnalysisContext ctx)
        {
            var inv = (InvocationExpressionSyntax)ctx.Node;
            var model = ctx.SemanticModel;
            switch (inv.Expression)
            {
                case SimpleNameSyntax simple:
                {
                    var info = model.GetSymbolInfo(inv, ctx.CancellationToken);
                    var name = simple.Identifier.ValueText;
                    var location = simple.Identifier.GetLocation();
                    if (info.Symbol is null && info.CandidateSymbols.IsEmpty && Unbound(model, simple))
                    {
                        FactoryCall(ctx, name, location, inv);
                        return;
                    }
                    if (info.Symbol is null) NamedArguments(ctx, inv, name, info.CandidateSymbols);
                    return;
                }
                case MemberAccessExpressionSyntax ma:
                {
                    var name = ma.Name.Identifier.ValueText;
                    var receiver = ReceiverName(ma.Expression);
                    var info = model.GetSymbolInfo(inv, ctx.CancellationToken);
                    if (receiver == "Factories" && info.Symbol is null && info.CandidateSymbols.IsEmpty
                        && model.GetSymbolInfo(ma.Expression, ctx.CancellationToken).Symbol is INamedTypeSymbol factories && IsIsotope(factories))
                        FactoryCall(ctx, name, ma.Name.Identifier.GetLocation(), inv);
                    else if (receiver == "Factories" && info.Symbol is null) NamedArguments(ctx, inv, name, info.CandidateSymbols);
                    else if (receiver == "ReactorApp") Hosting(ctx, inv, name, info);
                    else if (receiver == "Application" && name == "Start" && info.Symbol is null)
                        Report(ctx, Rules.Startup, inv.GetLocation(), args: ["Application.Start (WinUI startup)", ""]);
                    return;
                }
            }
        }

        /// <summary>An unbound factory call: a factory the table renamed or removed, or a control Isotope lacks.</summary>
        void FactoryCall(SyntaxNodeAnalysisContext ctx, string name, Location location, InvocationExpressionSyntax inv)
        {
            var factory = _t.Renames.Factories.FirstOrDefault(f => f.Applied && f.Reactor == name);
            if (factory is not null)
            {
                if (factory.Avalonia is null) Report(ctx, Rules.NameRemoved, location, args: ["Factory", name, factory.Note ?? ""]);
                else if (factory.With is { } with)
                {
                    // The whole call, renamed and given the initializer, in one edit: an edit inside it waits for the next
                    // run of the fix, so the parentheses can't be split.
                    var text = inv.ToString();
                    var at = location.SourceSpan.Start - inv.SpanStart;
                    var renamed = text.Substring(0, at) + factory.Avalonia + text.Substring(at + name.Length);
                    Report(ctx, Rules.NameRenamed, inv.GetLocation(), replace: "(" + renamed + " with { " + with + " })",
                        usings: factory.Using is { } ns ? [ns] : null, args: ["Factory", name, factory.Avalonia]);
                }
                else Report(ctx, Rules.NameRenamed, location, replace: factory.Avalonia, args: ["Factory", name, factory.Avalonia]);
            }
            else if (_t.Controls.Factories.TryGetValue(name, out var control))
                Report(ctx, control.WinUIOnly ? Rules.WinUIOnlyControl : Rules.ControlNotPorted, location, args: [name, control.Alternative ?? ""]);
        }

        bool IsFactoryEntry(string name) =>
            _t.Controls.Factories.ContainsKey(name) || _t.Renames.Factories.Any(f => f.Applied && f.Reactor == name);

        void NamedArguments(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, string factory, ImmutableArray<ISymbol> candidates)
        {
            var methods = candidates.OfType<IMethodSymbol>().ToList();
            if (methods.Count == 0 || !methods.Any(m => m.ContainingType?.Name == "Factories" && IsIsotope(m.ContainingType))) return;
            var reactorFactory = _t.Renames.Factories.FirstOrDefault(f => f.Applied && f.Avalonia == factory)?.Reactor;
            var named = inv.ArgumentList.Arguments.Where(a => a.NameColon is not null).Select(a => a.NameColon!.Name.Identifier.ValueText).ToList();
            foreach (var arg in inv.ArgumentList.Arguments)
            {
                if (arg.NameColon is null) continue;
                var parameter = arg.NameColon.Name.Identifier.ValueText;
                if (methods.Any(m => m.Parameters.Any(p => p.Name == parameter))) continue;
                var entry = _t.Renames.Parameters.FirstOrDefault(p => (p.Factory == factory || p.Factory == reactorFactory) && p.Reactor == parameter);
                // A target already named here would be named twice: leave it to the compiler's error.
                if (entry is not null && !named.Contains(entry.Avalonia))
                    Report(ctx, Rules.ParameterRenamed, arg.NameColon.Name.GetLocation(), replace: entry.Avalonia, args: [factory, parameter, entry.Avalonia]);
            }
        }

        void Hosting(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv, string name, SymbolInfo info)
        {
            if (name == "OpenWindow" && info.Symbol is null)
            {
                Report(ctx, Rules.Startup, inv.GetLocation(), args: ["ReactorApp.OpenWindow (another window)", "; Isotope has one window per app for now"]);
                return;
            }
            if (name != "Run") return;
            var args = inv.ArgumentList.Arguments;
            if (info.Symbol is null && args.Count > 0)
            {
                var first = args[0].Expression;
                if ((first is SimpleLambdaExpressionSyntax || first is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 }) && args.Count == 1)
                {
                    Report(ctx, Rules.Startup, inv.GetLocation(), args: ["ReactorApp.Run(startup) (several windows)", "; Isotope has one window per app for now"]);
                    return;
                }
                if (first is BaseObjectCreationExpressionSyntax creation && (creation is ImplicitObjectCreationExpressionSyntax || ReceiverName(((ObjectCreationExpressionSyntax)creation).Type) == "WindowSpec"))
                {
                    Report(ctx, Rules.Startup, inv.GetLocation(), args: ["ReactorApp.Run(WindowSpec)", "; Isotope has no WindowSpec yet"]);
                    return;
                }
            }
            foreach (var arg in args)
            {
                if (arg.Expression is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax fromPath } call) continue;
                if (fromPath.Name.Identifier.ValueText != "FromPath" || ReceiverName(fromPath.Expression) != "WindowIcon" || call.ArgumentList.Arguments.Count != 1) continue;
                if (arg.NameColon is not null && arg.NameColon.Name.Identifier.ValueText != "icon") continue;
                var replacement = "() => new Avalonia.Controls.WindowIcon(" + call.ArgumentList.Arguments[0].Expression.ToString() + ")";
                Report(ctx, Rules.WindowIcon, call.GetLocation(), replace: replacement, args: [call.ToString(), replacement]);
            }
        }

        public void ClassDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            var c = (ClassDeclarationSyntax)ctx.Node;
            if (c.BaseList is null || !c.BaseList.Types.Any(t => ReceiverName(t.Type) == "Application")) return;
            if (!c.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == "OnLaunched" && m.Modifiers.Any(SyntaxKind.OverrideKeyword))) return;
            Report(ctx, Rules.Startup, c.Identifier.GetLocation(), args: ["WinUI Application '" + c.Identifier.ValueText + "' with OnLaunched", ""]);
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

        WinUIType? Pick(List<WinUIType> types, SyntaxNode node)
        {
            if (types.Count == 1) return types[0];
            var imported = Imported(node);
            var inScope = types.Where(t => imported.Contains(t.Namespace)).ToList();
            if (inScope.Count == 1) return inScope[0];
            var avalonia = types.Select(t => t.Avalonia).Distinct().ToList();
            return avalonia.Count == 1 ? types[0] : null;
        }

        HashSet<string> Imported(SyntaxNode node)
        {
            var result = new HashSet<string>(_globalUsings.Value, StringComparer.Ordinal);
            foreach (var u in node.AncestorsAndSelf().SelectMany(a => a switch
                     {
                         CompilationUnitSyntax cu => cu.Usings,
                         BaseNamespaceDeclarationSyntax nd => nd.Usings,
                         _ => default(SyntaxList<UsingDirectiveSyntax>),
                     }))
                if (u.Alias is null && u.StaticKeyword == default && u.Name is not null) result.Add(NameText(u.Name));
            return result;
        }

        bool MemberExists(WinUIType type, string member)
        {
            if (_compilation.GetTypeByMetadataName(type.Avalonia!) is not { } avalonia) return true; // Avalonia not referenced: can't tell
            for (ITypeSymbol? t = avalonia; t is not null; t = t.BaseType)
                if (!t.GetMembers(member).IsEmpty) return true;
            return avalonia.AllInterfaces.Any(i => !i.GetMembers(member).IsEmpty);
        }

        MemberEntry? FindMember(ITypeSymbol type, string member, out string record)
        {
            for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            {
                foreach (var name in _recordAliases.TryGetValue(t.Name, out var reactor) ? [t.Name, reactor!] : new[] { t.Name })
                {
                    var entry = _t.Renames.Members.FirstOrDefault(m => m.Record == name && m.Reactor == member);
                    if (entry is not null)
                    {
                        record = name;
                        return entry;
                    }
                }
            }
            record = type.Name;
            return null;
        }

        /// <summary>A type parameter's first constraint (`T` in `where T : Element` is an Element here), else the type.</summary>
        static ITypeSymbol? Constraint(ITypeSymbol? type) =>
            type is ITypeParameterSymbol p ? p.ConstraintTypes.FirstOrDefault() : type;

        static IPropertySymbol? FindProperty(ITypeSymbol type, string name)
        {
            for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
                if (t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } p) return p;
            return null;
        }

        static bool IsOrDerivesFrom(ITypeSymbol type, string name, Dictionary<string, string> aliases)
        {
            for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
                if (t.Name == name || (aliases.TryGetValue(t.Name, out var reactor) && reactor == name)) return true;
            return false;
        }

        static bool Converts(SemanticModel model, ExpressionSyntax value, ITypeSymbol target)
        {
            var conversion = model.ClassifyConversion(value, target);
            return conversion.Exists && conversion.IsImplicit;
        }

        static bool IsIsotope(ITypeSymbol type)
        {
            var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
            return ns == "Isotope.UI" || ns.StartsWith("Isotope.UI.", StringComparison.Ordinal);
        }

        static bool Unbound(SemanticModel model, ExpressionSyntax node)
        {
            var info = model.GetSymbolInfo(node);
            return info.Symbol is null && info.CandidateSymbols.IsEmpty;
        }

        internal static INamespaceSymbol? FindNamespace(INamespaceSymbol global, string dotted)
        {
            var current = global;
            foreach (var part in dotted.Split('.'))
            {
                current = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == part)!;
                if (current is null) return null;
            }
            return current;
        }

        /// <summary>Whether a name is the first of its dotted chain (not a member name, a named argument or an initializer's member).</summary>
        static bool IsLeftmost(IdentifierNameSyntax id) => id.Parent switch
        {
            QualifiedNameSyntax q => q.Left == id,
            MemberAccessExpressionSyntax m => m.Expression == id,
            MemberBindingExpressionSyntax => false,
            AliasQualifiedNameSyntax a => a.Name == id && a.Alias.Identifier.IsKind(SyntaxKind.GlobalKeyword),
            NameColonSyntax or NameEqualsSyntax => false,
            AssignmentExpressionSyntax asg when asg.Left == id && asg.Parent is InitializerExpressionSyntax => false,
            _ => true,
        };

        static bool IsInvoked(SyntaxNode node) => node.Parent is InvocationExpressionSyntax inv && inv.Expression == node;

        static bool InUsing(SyntaxNode node) => node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null;

        /// <summary>The nodes of a dotted chain from its first name: element k spans segments 0 to k.</summary>
        static List<SyntaxNode> ChainFrom(IdentifierNameSyntax root)
        {
            SyntaxNode current = root.Parent is AliasQualifiedNameSyntax aq ? aq : root;
            var nodes = new List<SyntaxNode> { current };
            while (true)
            {
                if (current.Parent is QualifiedNameSyntax q && q.Left == current) current = q;
                else if (current.Parent is MemberAccessExpressionSyntax m && m.Expression == current && m.IsKind(SyntaxKind.SimpleMemberAccessExpression)) current = m;
                else break;
                nodes.Add(current);
            }
            return nodes;
        }

        static string Segment(SyntaxNode node) => node switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => node.ToString(),
        };

        static string Prefix(List<SyntaxNode> nodes) => nodes[0] is AliasQualifiedNameSyntax ? "global::" : "";

        static string NameText(NameSyntax name) => name switch
        {
            AliasQualifiedNameSyntax a => NameText(a.Name),
            QualifiedNameSyntax q => NameText(q.Left) + "." + q.Right.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => name.ToString(),
        };

        /// <summary>The last name of a type or receiver expression (`WindowSpec`, `Avalonia.Controls.WindowIcon`).</summary>
        static string? ReceiverName(SyntaxNode node) => node switch
        {
            SimpleNameSyntax s => s.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            _ => null,
        };

        static string Note(string? note) => note is null ? "" : ": " + note;

        static void Report(SyntaxNodeAnalysisContext ctx, DiagnosticDescriptor rule, Location location, string? replace = null,
            IEnumerable<string>? usings = null, bool removeLine = false, object[]? args = null)
        {
            var props = ImmutableDictionary.CreateBuilder<string, string?>();
            if (replace is not null) props[Props.Replace] = replace;
            if (removeLine) props[Props.RemoveLine] = "1";
            var list = usings?.ToList();
            if (list is { Count: > 0 }) props[Props.Usings] = string.Join(";", list);
            ctx.ReportDiagnostic(Diagnostic.Create(rule, location, props.ToImmutable(), args ?? []));
        }
    }
}
