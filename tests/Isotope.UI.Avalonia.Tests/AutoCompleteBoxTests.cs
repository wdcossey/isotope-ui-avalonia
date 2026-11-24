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
/// The auto-suggest box (#29), as an Avalonia AutoCompleteBox, in a headless window: what the user types reaches
/// OnTextChanged and the engine's own writes don't, new suggestions show while the user types, a picked suggestion
/// reaches OnSuggestionChosen, Enter submits the query.
/// </summary>
public sealed class AutoCompleteBoxTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Show(TestHost host)
    {
        if (TopLevel.GetTopLevel(host.Root) is Window shown) return shown;
        var window = new Window { Width = 400, Height = 300, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static AutoCompleteBox Box(TestHost host) => host.All<AutoCompleteBox>()[0];

    // Real keystrokes, through the focused box's text box, then the root passes the state changes asked for.
    private void Type(TestHost host, string text)
    {
        var window = Show(host);
        var box = Box(host);
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        // Focusing selects the text: put the caret after it, so the keystrokes add to it.
        var inner = box.GetVisualDescendants().OfType<TextBox>().First();
        inner.SelectionStart = inner.SelectionEnd = inner.Text?.Length ?? 0;
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
        host.Flush();
        Dispatcher.UIThread.RunJobs();
    }

    private void Press(TestHost host, PhysicalKey key)
    {
        Show(host).KeyPressQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        host.Flush();
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] Fruit(string query) =>
        query.Length == 0 ? [] : new[] { "apple", "apricot", "avocado", "banana" }.Where(f => f.StartsWith(query, StringComparison.Ordinal)).ToArray();

    [AvaloniaFact]
    public void AutoCompleteBox_mounts_its_text_suggestions_and_placeholder_and_shows_them_unfiltered()
    {
        var host = new TestHost(() => AutoCompleteBox("ap").PlaceholderText("Fruit") with { ItemsSource = ["banana", "apple"] });
        var box = Box(host);
        Assert.Equal(("ap", "Fruit"), (box.Text, box.PlaceholderText));
        Assert.Equal(["banana", "apple"], box.ItemsSource!.Cast<string>());
        Assert.Equal(AutoCompleteFilterMode.None, box.FilterMode);
        Assert.False(box.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void AutoCompleteBox_modifiers_set_the_callbacks()
    {
        Action<string> a = _ => { }, b = _ => { }, c = _ => { };
        var el = AutoCompleteBox().TextChanged(a).QuerySubmitted(b).SuggestionChosen(c);
        Assert.Equal((a, b, c), (el.OnTextChanged!, el.OnQuerySubmitted!, el.OnSuggestionChosen!));
        Assert.Null(el.TextChanged(null).OnTextChanged);
    }

    [AvaloniaFact]
    public void Typing_reaches_the_state_and_an_engine_write_does_not_echo()
    {
        var reported = new List<string>();
        Action<string>? set = null;
        var host = new TestHost(() => Component(ctx =>
        {
            var (text, setText) = ctx.UseState("");
            set = setText;
            return AutoCompleteBox(text, t => { reported.Add(t); setText(t); });
        }));
        Type(host, "a");
        Assert.Equal(["a"], reported);
        Assert.Equal("a", Box(host).Text);

        set!("apple"); // from the state: written, not reported
        host.Flush();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("apple", Box(host).Text);
        Assert.Equal(["a"], reported);

        Type(host, "s"); // and the next keystroke isn't swallowed
        Assert.Equal(["a", "apples"], reported);
    }

    [AvaloniaFact]
    public void Showing_a_box_with_text_in_a_window_reports_nothing()
    {
        // Avalonia fills the template's text box with the text when the box is shown, and raises TextChanged for it.
        var reported = new List<string>();
        var host = new TestHost(() => AutoCompleteBox("pear", t => reported.Add(t)));
        Show(host);
        Assert.Equal("pear", Box(host).Text);
        Assert.Empty(reported);
    }

    [AvaloniaFact]
    public void Suggestions_given_after_a_keystroke_open_the_list()
    {
        // The app derives its suggestions from the typed text: they arrive on the render after the keystroke.
        var host = new TestHost(() => Component(ctx =>
        {
            var (query, setQuery) = ctx.UseState("");
            return AutoCompleteBox(onTextChanged: setQuery) with { ItemsSource = Fruit(query) };
        }));
        Type(host, "ap");
        var box = Box(host);
        Assert.Equal(["apple", "apricot"], box.ItemsSource!.Cast<string>());
        Assert.True(box.IsDropDownOpen);

        Type(host, "x"); // no suggestion matches "apx": the list closes
        Assert.False(box.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void Suggestions_rebuilt_with_the_same_strings_are_not_rewritten()
    {
        var n = 0;
        var host = new TestHost(() => AutoCompleteBox() with { ItemsSource = n >= 0 ? ["apple", "avocado"] : [] });
        var items = Box(host).ItemsSource;
        n = 1;
        host.Render();
        Assert.Same(items, Box(host).ItemsSource);
    }

    [AvaloniaFact]
    public void A_picked_suggestion_reaches_OnSuggestionChosen_and_not_OnTextChanged()
    {
        var typed = new List<string>();
        var chosen = new List<string>();
        var host = new TestHost(() => AutoCompleteBox(onTextChanged: t => typed.Add(t)).SuggestionChosen(s => chosen.Add(s))
            with { ItemsSource = ["apple", "avocado"] });
        Type(host, "a");
        Assert.True(Box(host).IsDropDownOpen);

        Press(host, PhysicalKey.ArrowDown);
        Assert.Equal("apple", Box(host).Text);
        Assert.Equal(["apple"], chosen);
        Assert.Equal(["a"], typed);
    }

    [AvaloniaFact]
    public void Typing_a_suggestion_out_is_typing_not_a_choice()
    {
        // Avalonia selects the suggestion the typed text equals; as on WinUI, only a pick is a choice.
        var typed = new List<string>();
        var chosen = new List<string>();
        var host = new TestHost(() => AutoCompleteBox(onTextChanged: t => typed.Add(t)).SuggestionChosen(s => chosen.Add(s))
            with { ItemsSource = ["apple", "avocado"] });
        Type(host, "avocado");
        Assert.Equal("avocado", Box(host).SelectedItem);
        Assert.Equal(["avocado"], typed);
        Assert.Empty(chosen);
    }

    [AvaloniaFact]
    public void An_engine_write_that_matches_a_suggestion_is_not_a_choice()
    {
        // Avalonia selects the suggestion a written text equals; that selection is the engine's, not the user's.
        var chosen = new List<string>();
        var text = "a";
        var host = new TestHost(() => AutoCompleteBox(text).SuggestionChosen(s => chosen.Add(s)) with { ItemsSource = ["apple", "avocado"] });
        Show(host);
        text = "avocado";
        host.Render();
        Assert.Equal("avocado", Box(host).SelectedItem);
        Assert.Empty(chosen);
    }

    [AvaloniaFact]
    public void Enter_submits_the_query_with_the_text()
    {
        var submitted = new List<string>();
        var host = new TestHost(() => AutoCompleteBox(onQuerySubmitted: q => submitted.Add(q)) with { ItemsSource = ["apple"] });
        Type(host, "pie");
        Press(host, PhysicalKey.Enter);
        Assert.Equal(["pie"], submitted);
    }

    [AvaloniaFact]
    public void IsDropDownOpen_opens_and_closes_the_list()
    {
        var open = false;
        var host = new TestHost(() => AutoCompleteBox("a") with { ItemsSource = ["apple"], IsDropDownOpen = open });
        Show(host);
        Assert.False(Box(host).IsDropDownOpen);
        open = true;
        host.Render();
        Assert.True(Box(host).IsDropDownOpen);
        open = false;
        host.Render();
        Assert.False(Box(host).IsDropDownOpen);
    }

    [AvaloniaFact]
    public void FilterMode_lets_Avalonia_filter_the_suggestions()
    {
        var host = new TestHost(() => AutoCompleteBox() with { ItemsSource = ["apple", "banana"], FilterMode = AutoCompleteFilterMode.StartsWith });
        Assert.Equal(AutoCompleteFilterMode.StartsWith, Box(host).FilterMode);
        Type(host, "b");
        Assert.True(Box(host).IsDropDownOpen);
        Press(host, PhysicalKey.ArrowDown); // the first suggestion shown is the first that starts with "b"
        Assert.Equal("banana", Box(host).Text);
    }
}
