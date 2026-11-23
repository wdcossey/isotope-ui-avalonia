using Avalonia.Layout;
using Isotope.UI.Core;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Short element factories for the tests (the real element vocabulary is tier 3).</summary>
internal static class Dsl
{
    public static TextBlockElement Text(string s, string? key = null) => new(s) { Key = key };

    public static StackElement VStack(params Element[] children) => new(Orientation.Vertical, children);

    public static ButtonElement Button(string label, Action? onClick = null) => new(label, onClick);

    public static FuncElement Component(Func<RenderContext, Element> render, string? key = null) => new(render) { Key = key };
}
