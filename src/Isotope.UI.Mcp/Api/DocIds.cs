using System.Reflection;

namespace Isotope.UI.Mcp.Api;

/// <summary>
/// The XML documentation ID of a member (ECMA-334 annex D, as the C# compiler writes them into the doc file): how a
/// member found by reflection is matched with its doc comment.
/// </summary>
internal static class DocIds
{
    public static string Of(MemberInfo member) => member switch
    {
        Type t => "T:" + TypeName(t),
        ConstructorInfo c => "M:" + TypeName(c.DeclaringType!) + "." + (c.IsStatic ? "#cctor" : "#ctor") + Parameters(c.GetParameters()),
        MethodInfo m => "M:" + TypeName(m.DeclaringType!) + "." + m.Name.Replace('.', '#')
            + (m.IsGenericMethod ? "``" + m.GetGenericArguments().Length : "")
            + Parameters(m.GetParameters())
            + (m.Name is "op_Implicit" or "op_Explicit" ? "~" + Reference(m.ReturnType) : ""),
        PropertyInfo p => "P:" + TypeName(p.DeclaringType!) + "." + p.Name.Replace('.', '#') + Parameters(p.GetIndexParameters()),
        FieldInfo f => "F:" + TypeName(f.DeclaringType!) + "." + f.Name,
        EventInfo e => "E:" + TypeName(e.DeclaringType!) + "." + e.Name,
        _ => throw new ArgumentException("No documentation ID for a " + member.GetType().Name, nameof(member)),
    };

    // A type definition's name: namespace, nested types joined by '.', and each generic type's arity (Command`1).
    static string TypeName(Type t)
    {
        if (t.IsGenericType && !t.IsGenericTypeDefinition) t = t.GetGenericTypeDefinition();
        return t.IsNested ? TypeName(t.DeclaringType!) + "." + t.Name : (t.Namespace is { } ns ? ns + "." : "") + t.Name;
    }

    static string Parameters(ParameterInfo[] parameters) =>
        parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(p => Reference(p.ParameterType))) + ")";

    // A type as a parameter: constructed generics with their arguments in braces, `n for a type's type parameter,
    // ``n for a method's, @ for ref, out and in.
    static string Reference(Type t)
    {
        if (t.IsByRef) return Reference(t.GetElementType()!) + "@";
        if (t.IsPointer) return Reference(t.GetElementType()!) + "*";
        if (t.IsArray)
            return Reference(t.GetElementType()!) + (t.IsSZArray ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", t.GetArrayRank())) + "]");
        if (t.IsGenericParameter) return (t.DeclaringMethod is null ? "`" : "``") + t.GenericParameterPosition;
        if (!t.IsGenericType) return TypeName(t);
        var arguments = new Queue<Type>(t.GetGenericArguments());
        return Constructed(t.GetGenericTypeDefinition(), arguments);
    }

    // Outer types first, each taking its own arity's worth of the arguments.
    static string Constructed(Type definition, Queue<Type> arguments)
    {
        var prefix = definition.IsNested
            ? Constructed(definition.DeclaringType!, arguments) + "."
            : (definition.Namespace is { } ns ? ns + "." : "");
        var name = definition.Name;
        var tick = name.IndexOf('`');
        if (tick < 0) return prefix + name;
        var arity = int.Parse(name[(tick + 1)..]);
        var own = Enumerable.Range(0, arity).Select(_ => Reference(arguments.Dequeue()));
        return prefix + name[..tick] + "{" + string.Join(",", own) + "}";
    }
}
