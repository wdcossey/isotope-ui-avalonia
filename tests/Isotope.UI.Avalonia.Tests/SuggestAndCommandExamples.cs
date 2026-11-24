using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The guide's examples for the auto-suggest box and commands (#29): each region is a snippet of
/// docs/guide/writing-components.md (DocSnippetTests checks they match), and the tests below run them.
/// </summary>
internal static class SuggestAndCommandExamples
{
    #region auto-suggest-box
    public static Element FruitPicker(RenderContext ctx, string[] fruit)
    {
        var (query, setQuery) = ctx.UseState("");
        var (chosen, setChosen) = ctx.UseState("");
        var matches = query.Length == 0 ? [] : fruit.Where(f => f.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return VStack(
            AutoCompleteBox(onTextChanged: setQuery, onQuerySubmitted: setChosen).SuggestionChosen(setChosen)
                with { ItemsSource = matches },
            TextBlock($"Chosen: {chosen}"));
    }
    #endregion

    #region command
    public static Element Uploader(RenderContext ctx, Func<Task> upload)
    {
        var send = ctx.UseCommand(new Command { Label = "Upload", ExecuteAsync = upload, Description = "Upload the file" });
        return VStack(
            Button(send),
            TextBlock(send.IsExecuting ? "Uploading..." : "Ready"));
    }
    #endregion
}

public sealed class SuggestAndCommandExampleTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Show(TestHost host)
    {
        var window = new Window { Width = 400, Height = 300, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Settle(TestHost host)
    {
        Dispatcher.UIThread.RunJobs();
        host.Flush();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void FruitPicker_suggests_from_what_is_typed_and_shows_the_pick()
    {
        var host = new TestHost(() => Component(ctx => SuggestAndCommandExamples.FruitPicker(ctx, ["Apple", "Apricot", "Banana"])));
        var window = Show(host);
        var box = host.All<AutoCompleteBox>()[0];
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("ap");
        Settle(host);
        Assert.Equal(["Apple", "Apricot"], box.ItemsSource!.Cast<string>());
        Assert.True(box.IsDropDownOpen);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Settle(host);
        Assert.Equal("Chosen: Apple", host.Texts()[^1]);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(host);
        Assert.False(box.IsDropDownOpen);
        Assert.Equal("Chosen: Apple", host.Texts()[^1]);
    }

    [AvaloniaFact]
    public void Uploader_disables_its_button_while_the_upload_runs()
    {
        var upload = new TaskCompletionSource();
        var host = new TestHost(() => Component(ctx => SuggestAndCommandExamples.Uploader(ctx, () => upload.Task)));
        var window = Show(host);
        var button = host.All<Button>()[0];
        Assert.Equal(("Upload", "Upload the file"), (button.Content, ToolTip.GetTip(button)));

        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Settle(host);
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Equal("Uploading...", host.Texts()[^1]);

        upload.SetResult(); // the hook's continuation may run off this thread: wait for the render it asks for
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (host.Texts()[^1] != "Ready" && DateTime.UtcNow < deadline)
        {
            Settle(host);
            Thread.Sleep(10);
        }
        Assert.Equal("Ready", host.Texts()[^1]);
        Assert.True(button.IsEffectivelyEnabled);
    }
}
