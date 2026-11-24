// A consumer of the packed packages (#52). tools/packaging/pack.mjs builds this against the packages alone and runs it
// headless; it exits with 1 if a check fails. It proves what a consumer gets from the packages: the library's types
// and hooks, the desktop package's startup, and the wrappers generator, which must run in this compilation.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Isotope.UI.Wrappers;
using static Isotope.UI.Factories;
// Factories.Button would hide Avalonia's Button in member access (CS0119).
using AvaloniaButton = Avalonia.Controls.Button;

// The desktop one-liner opens a real window, so it is only compiled here: nothing passes --window.
if (args.Contains("--window"))
    return ReactorApp.Run("Isotope package smoke", Smoke.Counter, width: 400, height: 300);

// The desktop package's default builder (its app and the Fluent theme), on the headless platform instead of a real one.
ReactorApp.CreateDefaultAppBuilder()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
    .SetupWithoutStarting();

var failures = new List<string>();
int passed = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    if (ok) passed++; else failures.Add(what);
}

var counter = new ReactorHostControl(Smoke.Counter);
Dispatcher.UIThread.RunJobs();
Check(Smoke.Texts(counter).SequenceEqual(["Clicked 0 times"]), "the counter mounts");
Smoke.Find<AvaloniaButton>(counter)?.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(Smoke.Texts(counter).SequenceEqual(["Clicked 1 times"]), "a click re-renders it (UseState)");

// SelectableTextElement.Descriptor exists only if the generator ran in this build; the mount checks it is registered.
Check(SelectableTextElement.Descriptor is not null, "the bundled generator wrote the consumer's descriptor");
var wrapped = new ReactorHostControl(_ => new SelectableTextElement("wrapped by the generator"));
Dispatcher.UIThread.RunJobs();
Check(Smoke.Find<SelectableTextBlock>(wrapped)?.Text == "wrapped by the generator", "the consumer's element mounts its control");

counter.Dispose();
wrapped.Dispose();
Console.WriteLine(failures.Count == 0
    ? $"package smoke: {passed} checks passed"
    : $"package smoke: {failures.Count} of {passed + failures.Count} checks failed");
return failures.Count == 0 ? 0 : 1;

/// <summary>An Avalonia control the library doesn't wrap, wrapped here as a consumer would.</summary>
[GenerateReactorDescriptor(typeof(SelectableTextBlock))]
public partial record SelectableTextElement(string Text) : Element
{
    internal Action<SelectableTextBlock>[] Setters { get; init; } = [];
}

static class Smoke
{
    // The package README's example (which DocSnippetTests checks against the library tests' own copy).
    public static Element Counter(RenderContext ctx)
    {
        var (count, setCount) = ctx.UseState(0);
        return VStack(
            TextBlock($"Clicked {count} times"),
            Button("Click", () => setCount(count + 1)));
    }

    public static List<string?> Texts(Control? root) =>
        [.. Controls(root).OfType<TextBlock>().Select(t => t.Text)];

    public static T? Find<T>(Control? root) where T : Control => Controls(root).OfType<T>().FirstOrDefault();

    // The mounted controls, through panels, decorators and content (no templates are applied outside a window).
    static IEnumerable<Control> Controls(Control? c)
    {
        if (c is null) yield break;
        yield return c;
        IEnumerable<Control> children = c switch
        {
            Panel p => p.Children,
            Decorator { Child: { } child } => [child],
            ContentControl { Content: Control content } => [content],
            _ => [],
        };
        foreach (var child in children)
            foreach (var d in Controls(child))
                yield return d;
    }
}
