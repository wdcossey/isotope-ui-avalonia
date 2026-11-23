using Isotope.UI;
using Isotope.UI.Core;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// One short example per hook, for docs/guide/writing-components.md: each region is a snippet of the guide
/// (DocSnippetTests checks they match) and GuideExampleTests runs it.
/// </summary>
internal static class GuideExamples
{
    #region use-state
    public static Element Toggle(RenderContext ctx)
    {
        var (on, setOn) = ctx.UseState(false);
        return Button(on ? "On" : "Off", () => setOn(!on));
    }
    #endregion

    #region use-reducer
    public static Element Stepper(RenderContext ctx)
    {
        var (value, dispatch) = ctx.UseReducer<int, string>(
            (n, action) => action == "+" ? n + 1 : n - 1, 0);
        return HStack(
            Button("-", () => dispatch("-")),
            TextBlock(value.ToString()),
            Button("+", () => dispatch("+")));
    }
    #endregion

    #region use-effect
    public sealed class Ticker
    {
        public event Action<int>? Ticked;
        public void Tick(int n) => Ticked?.Invoke(n);
    }

    public static Element LastTick(RenderContext ctx, Ticker ticker)
    {
        var (last, setLast) = ctx.UseState(0);
        ctx.UseEffect(() =>
        {
            ticker.Ticked += setLast;               // runs after the render is applied
            return () => ticker.Ticked -= setLast;  // on unmount, or before re-running for a new ticker
        }, ticker);
        return TextBlock($"Last tick: {last}");
    }
    #endregion

    #region use-memo
    public static Element Search(RenderContext ctx, string[] names)
    {
        var (query, setQuery) = ctx.UseState("");
        var matches = ctx.UseMemo(
            () => names.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(),
            query, names);
        return VStack(
            TextBox(query, setQuery, placeholderText: "Search"),
            TextBlock($"{matches.Length} of {names.Length} match"));
    }
    #endregion

    #region use-callback
    public static Element SaveButton(RenderContext ctx, string draft, Action<string> save)
    {
        // The same delegate on every render until draft changes, so a memoized child that takes it can skip.
        var onSave = ctx.UseCallback(() => save(draft), draft, save);
        return Button("Save", onSave);
    }
    #endregion

    #region use-ref
    public static Element RenderCounter(RenderContext ctx)
    {
        var renders = ctx.UseRef(0);
        renders.Current++; // a ref keeps a value between renders and never asks for one
        var (clicks, setClicks) = ctx.UseState(0);
        return Button($"Rendered {renders.Current} times", () => setClicks(clicks + 1));
    }
    #endregion

    #region use-context
    public static readonly Context<string> Greeting = new("Hello");

    public static Element Greeter(RenderContext ctx) => TextBlock($"{ctx.UseContext(Greeting)}, world");

    public static Element Greeters(RenderContext ctx) => VStack(
        Component(Greeter),
        Component(Greeter).Provide(Greeting, "Bonjour"));
    #endregion
}
