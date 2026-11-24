using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Isotope.UI.Mcp.Api;

/// <summary>C# declarations of reflected members, with nullable annotations and default values, as a reader writes them.</summary>
internal sealed class Signatures
{
    // Not thread-safe; the catalog uses one instance while it is built, on one thread.
    readonly NullabilityInfoContext _nullability = new();

    static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char", [typeof(decimal)] = "decimal", [typeof(double)] = "double", [typeof(float)] = "float",
        [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong",
        [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(object)] = "object", [typeof(string)] = "string",
        [typeof(nint)] = "nint", [typeof(nuint)] = "nuint",
    };

    /// <summary>A type as C# writes it: keywords, generic arguments, <c>T?</c>, tuples.</summary>
    public static string TypeName(Type t) => Name(t, null, null);

    string Name(ParameterInfo p) => Name(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType,
        Nullability(() => _nullability.Create(p)), p.GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames);

    NullabilityInfo? Nullability(Func<NullabilityInfo> create)
    {
        try { return create(); } catch { return null; }
    }

    static string Name(Type t, NullabilityInfo? n, IList<string?>? tupleNames)
    {
        var mark = n is { ReadState: NullabilityState.Nullable } && !t.IsValueType ? "?" : "";
        if (t.IsByRef) return Name(t.GetElementType()!, n?.ElementType, null);
        if (t.IsPointer) return Name(t.GetElementType()!, null, null) + "*";
        if (t.IsArray) return Name(t.GetElementType()!, n?.ElementType, null) + "[" + new string(',', t.GetArrayRank() - 1) + "]" + mark;
        // An unconstrained T's annotation reads as T? whenever the compiler can't prove it non-null: left out.
        if (t.IsGenericParameter) return t.Name;
        if (Nullable.GetUnderlyingType(t) is { } underlying) return Name(underlying, n?.GenericTypeArguments.FirstOrDefault(), tupleNames) + "?";
        if (Keywords.TryGetValue(t, out var keyword)) return keyword + mark;
        var prefix = t.IsNested && !t.IsGenericParameter ? Plain(t.DeclaringType!) + "." : "";
        if (!t.IsGenericType) return prefix + t.Name + mark;

        var args = t.GetGenericArguments();
        string Arg(int i) => Name(args[i], n is { GenericTypeArguments.Length: > 0 } && i < n.GenericTypeArguments.Length ? n.GenericTypeArguments[i] : null, null);
        if (t.GetGenericTypeDefinition().FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true && args.Length <= 7)
        {
            var parts = Enumerable.Range(0, args.Length).Select(i =>
                Arg(i) + (tupleNames is not null && i < tupleNames.Count && tupleNames[i] is { } name ? " " + name : ""));
            return "(" + string.Join(", ", parts) + ")" + mark;
        }
        var own = t.IsNested ? t.GetGenericArguments().Length - t.DeclaringType!.GetGenericArguments().Length : args.Length;
        var tick = t.Name.IndexOf('`');
        var bare = tick < 0 ? t.Name : t.Name[..tick];
        if (own <= 0) return prefix + bare + mark;
        var first = args.Length - own;
        return prefix + bare + "<" + string.Join(", ", Enumerable.Range(first, own).Select(Arg)) + ">" + mark;
    }

    // A containing type's name, without its generic arguments.
    static string Plain(Type t)
    {
        var tick = t.Name.IndexOf('`');
        var bare = tick < 0 ? t.Name : t.Name[..tick];
        return t.IsNested ? Plain(t.DeclaringType!) + "." + bare : bare;
    }

    /// <summary>A type's display name with its type parameters (<c>Command&lt;T&gt;</c>, <c>Outer.Inner</c>).</summary>
    public static string Display(Type t) => Name(t, null, null);

    /// <summary>A method's declaration: <c>static ButtonElement Factories.Button(Element content, Action? onClick = null)</c>.</summary>
    public string Method(MethodBase m, string owner)
    {
        var mods = m.IsStatic ? "static " : "";
        var generics = m.IsGenericMethod ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
        var extension = m.IsDefined(typeof(ExtensionAttribute), false);
        var parameters = string.Join(", ", m.GetParameters().Select((p, i) => Parameter(p, extension && i == 0)));
        if (m is ConstructorInfo) return "new " + owner + "(" + parameters + ")";
        var method = (MethodInfo)m;
        var returns = Name(method.ReturnType.IsByRef ? method.ReturnType.GetElementType()! : method.ReturnType,
            Nullability(() => _nullability.Create(method.ReturnParameter)),
            method.ReturnParameter.GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames);
        return mods + returns + " " + owner + "." + m.Name + generics + "(" + parameters + ")" + Constraints(m.IsGenericMethod ? m.GetGenericArguments() : []);
    }

    string Parameter(ParameterInfo p, bool isThis)
    {
        var prefix = isThis ? "this " : "";
        if (p.IsDefined(typeof(ParamArrayAttribute), false)) prefix += "params ";
        if (p.ParameterType.IsByRef) prefix += p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
        var text = prefix + Name(p) + " " + p.Name;
        if (p.HasDefaultValue) text += " = " + Literal(p.DefaultValue, p.ParameterType);
        return text;
    }

    static string Literal(object? value, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return value switch
        {
            null => underlying.IsValueType && Nullable.GetUnderlyingType(type) is null ? "default" : "null",
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            bool b => b ? "true" : "false",
            char c => "'" + c + "'",
            _ when underlying.IsEnum => Enum.GetName(underlying, value) is { } name ? underlying.Name + "." + name : "(" + underlying.Name + ")" + Convert.ToString(value, CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "default",
        };
    }

    static string Constraints(Type[] parameters)
    {
        var clauses = new List<string>();
        foreach (var p in parameters)
        {
            var list = new List<string>();
            var attrs = p.GenericParameterAttributes;
            if (attrs.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) list.Add("struct");
            else if (attrs.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) list.Add("class");
            list.AddRange(p.GetGenericParameterConstraints().Where(c => c != typeof(ValueType)).Select(TypeName));
            if (attrs.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !attrs.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) list.Add("new()");
            if (list.Count > 0) clauses.Add(" where " + p.Name + " : " + string.Join(", ", list));
        }
        return string.Concat(clauses);
    }

    /// <summary>A property's declaration: <c>string? ButtonElement.Content { get; init; }</c>.</summary>
    public string Property(PropertyInfo p, string owner)
    {
        var getter = p.GetMethod is { IsPublic: true };
        var setter = p.SetMethod is { IsPublic: true } set ? (set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit") ? "init; " : "set; ") : "";
        var isStatic = (p.GetMethod ?? p.SetMethod)?.IsStatic == true ? "static " : "";
        var type = Name(p.PropertyType, Nullability(() => _nullability.Create(p)), p.GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames);
        var index = p.GetIndexParameters();
        var name = index.Length == 0 ? owner + "." + p.Name : owner + "[" + string.Join(", ", index.Select(i => Parameter(i, false))) + "]";
        return isStatic + type + " " + name + " { " + (getter ? "get; " : "") + setter + "}";
    }

    public string Field(FieldInfo f, string owner)
    {
        var mods = f.IsLiteral ? "const " : (f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : "");
        var type = Name(f.FieldType, Nullability(() => _nullability.Create(f)), null);
        var value = f.IsLiteral && f.GetRawConstantValue() is { } v ? " = " + Literal(v, f.FieldType) : "";
        return mods + type + " " + owner + "." + f.Name + value;
    }

    public string Event(EventInfo e, string owner) =>
        "event " + Name(e.EventHandlerType!, Nullability(() => _nullability.Create(e)), null) + " " + owner + "." + e.Name;

    /// <summary>A type's declaration: <c>sealed record ButtonElement : Element</c>.</summary>
    public static string Type(Type t)
    {
        string kind;
        if (t.IsEnum) kind = "enum";
        else if (t.IsInterface) kind = "interface";
        else if (typeof(Delegate).IsAssignableFrom(t)) kind = "delegate";
        else if (t.IsValueType) kind = IsRecord(t) ? "record struct" : "struct";
        else if (t.IsAbstract && t.IsSealed) kind = "static class";
        else kind = (t.IsAbstract ? "abstract " : t.IsSealed ? "sealed " : "") + (IsRecord(t) ? "record" : "class");
        var baseType = t.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum) && !typeof(Delegate).IsAssignableFrom(t)
            ? " : " + Display(b) : "";
        return kind + " " + Display(t) + baseType + (t.IsGenericTypeDefinition ? Constraints(t.GetGenericArguments()) : "");
    }

    /// <summary>A record has the compiler's clone method (a class) or its printer (a struct).</summary>
    public static bool IsRecord(Type t) =>
        t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null
        || (t.IsValueType && t.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null);
}
