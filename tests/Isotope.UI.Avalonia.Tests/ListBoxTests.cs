using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// The list box's element and typed items (#94, Reactor's ListView and ListView&lt;T&gt;): items mounted as Avalonia realises
/// their containers (#102) and reconciled as a panel's children (in place, keyed moves as one Move each, a component's
/// state kept, out of view too), the controlled and multiple selection, the item click, and a long list's realisation,
/// in a headless window.
/// </summary>
public sealed class ListBoxTests : IDisposable
{
    private readonly List<Window> _windows = new();

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private Window Show(TestHost host, double height = 300)
    {
        var window = new Window { Width = 300, Height = height, Content = host.Root, SizeToContent = SizeToContent.Manual };
        _windows.Add(window);
        window.Show();
        Layout(window);
        return window;
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static ListBox List(TestHost host) => host.All<ListBox>()[0];

    // The control mounted for an item (#102: the list box's items are slots, each with its element's control once shown).
    private static Control? ItemControl(ListBox list, int index) => ((ListBoxItemSlot)list.Items[index]!).Control;

    private static List<string?> Texts(ListBox list) => Enumerable.Range(0, list.Items.Count).Select(i => TextOf(ItemControl(list, i))).ToList();

    private static int Mounted(ListBox list) => list.Items.Cast<ListBoxItemSlot>().Count(s => s.Mounted);

    private static void ScrollTo(Window window, ListBox list, int index)
    {
        list.ScrollIntoView(index);
        Layout(window);
    }

    // An item's text: its text block's, or the first one inside it (a component's border, a stack).
    private static string? TextOf(object? item) => item switch
    {
        TextBlock t => t.Text,
        Decorator d => TextOf(d.Child),
        Panel p => p.Children.Count > 0 ? TextOf(p.Children[0]) : null,
        _ => null,
    };

    private static Element[] Rows(IEnumerable<string> keys) => keys.Select(k => (Element)TextBlock(k).WithKey(k)).ToArray();

    // A real headless click at the centre of an item's container, through the window's input.
    private static void ClickItem(Window window, ListBox list, int index, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var container = list.ContainerFromIndex(index) ?? throw new InvalidOperationException("item " + index + " isn't realised");
        var centre = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left, modifiers);
        window.MouseUp(centre, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Element_items_mount_into_the_list_and_update_in_place()
    {
        var first = "one";
        var more = true;
        var host = new TestHost(() => more
            ? ListBox(TextBlock(first), TextBlock("two"), null, TextBlock("three"))
            : ListBox(TextBlock(first)));
        Show(host);
        var list = List(host);
        Assert.Equal(["one", "two", "three"], Texts(list));
        var control = ItemControl(list, 0);

        first = "uno";
        host.Render();
        Assert.Same(list, List(host));
        Assert.Same(control, ItemControl(list, 0));
        Assert.Equal(["uno", "two", "three"], Texts(list));

        more = false;
        host.Render();
        Assert.Single(list.Items);
        Assert.Same(control, ItemControl(list, 0));
    }

    [AvaloniaFact]
    public void A_keyed_reorder_moves_each_item_with_one_Move_and_keeps_its_control()
    {
        string[] keys = ["a", "b", "c", "d", "e"];
        var host = new TestHost(() => ListBox(Rows(keys)));
        var window = Show(host);
        var list = List(host);
        var before = Enumerable.Range(0, keys.Length).Select(i => (TextBlock)ItemControl(list, i)!).ToDictionary(t => t.Text!);
        var edits = new List<NotifyCollectionChangedAction>();
        list.Items.CollectionChanged += (_, e) => edits.Add(e.Action);

        keys = ["e", "d", "c", "b", "a"];
        host.Render();
        Assert.Equal(keys, Texts(list));
        Assert.All(keys, (k, i) => Assert.Same(before[k], ItemControl(list, i)));
        Assert.NotEmpty(edits);
        Assert.All(edits, a => Assert.Equal(NotifyCollectionChangedAction.Move, a));

        // Avalonia recycles a moved item's container; once laid out, each control is shown again, in its item's container.
        Layout(window);
        Assert.All(keys, (k, i) => Assert.Same(before[k], ((ContentControl)list.ContainerFromIndex(i)!).Content));
    }

    [AvaloniaFact]
    public void A_component_in_an_item_keeps_its_state_across_moves_and_re_renders_itself_there()
    {
        string[] keys = ["a", "b", "c"];
        var setters = new Dictionary<string, Action<int>>();
        int mounts = 0, cleanups = 0;
        Element Row(string key) => Component(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            setters[key] = setN;
            ctx.UseEffect(() =>
            {
                mounts++;
                return () => cleanups++;
            });
            return TextBlock(key + " " + n);
        }).WithKey(key);
        var host = new TestHost(() => ListBox(keys.Select(Row).ToArray()));
        Show(host);
        var list = List(host);
        Assert.Equal(3, mounts);

        setters["b"](5);
        host.Flush();
        Assert.Equal(["a 0", "b 5", "c 0"], Texts(list));

        keys = ["c", "b", "a"];
        host.Render();
        Assert.Equal(["c 0", "b 5", "a 0"], Texts(list));
        Assert.Equal(3, mounts);
        Assert.Equal(0, cleanups);

        // The moved component re-renders itself in its new place: the dirty path goes through the list box.
        setters["a"](7);
        host.Flush();
        Assert.Equal(["c 0", "b 5", "a 7"], Texts(list));

        host.Unmount();
        Assert.Equal(3, cleanups);
    }

    private static readonly Context<string> Suffix = new("-");

    [AvaloniaFact]
    public void A_component_in_an_item_hears_a_context_change_when_the_list_is_unchanged()
    {
        // The same items array each render: the list box element is equal, so only the context check reaches the item.
        Element[] items = [Component(ctx => TextBlock("x" + ctx.UseContext(Suffix))).WithKey("x")];
        var suffix = "1";
        var host = new TestHost(() => VStack(ListBox(items)).Provide(Suffix, suffix));
        Show(host);
        var list = List(host);
        Assert.Equal(["x1"], Texts(list));

        suffix = "2";
        host.Render();
        Assert.Equal(["x2"], Texts(list));
    }

    [AvaloniaFact]
    public void A_controlled_selection_reports_the_user_s_choice_and_the_declared_index_wins()
    {
        var reported = new List<int>();
        var follow = true;
        var host = new TestHost(() => Component(ctx =>
        {
            var (index, setIndex) = ctx.UseState(0);
            return VStack(TextBlock("state " + index), ListBox(index, v =>
            {
                reported.Add(v);
                if (follow) setIndex(v);
            }, Rows(["a", "b", "c"])));
        }));
        var list = List(host);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Empty(reported);

        list.SelectedIndex = 2;
        host.Flush();
        Assert.Equal([2], reported);
        Assert.Contains("state 2", host.Texts());

        // A render whose index the control already shows writes nothing.
        var changes = 0;
        list.SelectionChanged += (_, _) => changes++;
        host.Render();
        Assert.Equal(0, changes);

        // The state stays at 2 while the user picks 1: the next render writes 2 back, as upstream's controlled index does,
        // and that write isn't reported.
        follow = false;
        list.SelectedIndex = 1;
        host.Render();
        Assert.Equal([2, 1], reported);
        Assert.Equal(2, list.SelectedIndex);
    }

    [AvaloniaFact]
    public void Only_the_list_s_own_selection_is_reported_not_a_combo_box_s_in_an_item()
    {
        var reported = new List<int>();
        var host = new TestHost(() => ListBox(TextBlock("a"), ComboBox(["x", "y"])) with { OnSelectedIndexChanged = reported.Add });
        Show(host);
        var combo = host.Root.Child is ListBox l ? (ComboBox)ItemControl(l, 1)! : throw new InvalidOperationException();
        combo.SelectedIndex = 1;
        Assert.Empty(reported);

        List(host).SelectedIndex = 0;
        Assert.Equal([0], reported);
    }

    [AvaloniaFact]
    public void An_uncontrolled_selection_follows_its_item_across_a_keyed_move_and_the_app_hears_its_index()
    {
        string[] keys = ["a", "b", "c", "d"];
        var reported = new List<int>();
        var host = new TestHost(() => ListBox(Rows(keys)) with { OnSelectedIndexChanged = reported.Add });
        var list = List(host);
        list.SelectedIndex = 1;
        reported.Clear();

        keys = ["d", "c", "b", "a"];
        host.Render();
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("b", ((ListBoxItemSlot)list.SelectedItem!).Key);
        Assert.Equal([2], reported);
        reported.Clear();

        // The selected item removed: the selection is gone, and the app hears it.
        keys = ["d", "c", "a"];
        host.Render();
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal([-1], reported);
    }

    [AvaloniaFact]
    public void A_move_keeps_exactly_the_selection_with_AlwaysSelected_and_a_typed_list_reports_the_items_left()
    {
        IReadOnlyList<Person> people = [new("a", "A"), new("b", "B"), new("c", "C"), new("d", "D")];
        var snapshots = new List<IReadOnlyList<Person>>();
        var host = new TestHost(() => (ListBox(people, p => p.Id, (p, _) => TextBlock(p.Name)) with { OnSelectionChanged = snapshots.Add })
            .SelectionMode(SelectionMode.Multiple | SelectionMode.AlwaysSelected));
        var list = List(host);
        list.Selection.Select(3);
        list.Selection.Deselect(0);
        Assert.Equal([3], list.Selection.SelectedIndexes);

        // D moves to the front: it alone stays selected (AlwaysSelected picks no other item while it moves), and the app
        // hears it there.
        people = [people[3], people[0], people[1], people[2]];
        host.Render();
        Assert.Equal([0], list.Selection.SelectedIndexes);
        Assert.Equal(["D"], snapshots[^1].Select(p => p.Name));

        // B and D selected, then B removed: the items reported are those still selected, D.
        list.Selection.Select(2);
        Assert.Equal(["D", "B"], snapshots[^1].Select(p => p.Name));
        people = [people[0], people[1], people[3]];
        host.Render();
        Assert.Equal(["D"], snapshots[^1].Select(p => p.Name));
        Assert.Equal([0], list.Selection.SelectedIndexes);
    }

    // Reactor's ListViewSelectionMode values, as the migration maps them (winui-types.json), measured with headless clicks.
    [AvaloniaTheory]
    [InlineData("Single", SelectionMode.Single, false, new[] { 2 })]
    [InlineData("Extended, plain clicks", SelectionMode.Multiple, false, new[] { 2 })]
    [InlineData("Extended, Ctrl on the second", SelectionMode.Multiple, true, new[] { 0, 2 })]
    [InlineData("Multiple", SelectionMode.Multiple | SelectionMode.Toggle, false, new[] { 0, 2 })]
    public void Each_selection_mode_selects_as_its_Reactor_value_did(string reactor, SelectionMode mode, bool control, int[] expected)
    {
        _ = reactor;
        var snapshots = new List<IReadOnlyList<int>>();
        var host = new TestHost(() => (ListBox(Rows(["a", "b", "c"])) with { OnSelectionChanged = snapshots.Add }).SelectionMode(mode));
        var window = Show(host);
        var list = List(host);
        Assert.Equal(mode, list.SelectionMode);

        ClickItem(window, list, 0);
        ClickItem(window, list, 2, control ? RawInputModifiers.Control : RawInputModifiers.None);
        Assert.Equal(expected, snapshots[^1]);
        Assert.Equal(expected, list.Selection.SelectedIndexes.Order());
    }

    [AvaloniaFact]
    public void Toggle_deselects_a_selected_item_on_a_second_click()
    {
        var snapshots = new List<IReadOnlyList<int>>();
        var host = new TestHost(() => (ListBox(Rows(["a", "b", "c"])) with { OnSelectionChanged = snapshots.Add })
            .SelectionMode(SelectionMode.Multiple | SelectionMode.Toggle));
        var window = Show(host);
        var list = List(host);
        ClickItem(window, list, 0);
        ClickItem(window, list, 2);
        ClickItem(window, list, 0);
        Assert.Equal([2], snapshots[^1]);
    }

    [AvaloniaFact]
    public void An_item_click_comes_after_the_selection_a_tap_makes_and_from_Enter_or_Space_on_an_item()
    {
        var events = new List<string>();
        var host = new TestHost(() => ListBox(TextBlock("a"), HStack(TextBlock("b"), Button("go", () => events.Add("button"))), HStack(TextBlock("c"), TextBox("note"))) with
        {
            OnSelectedIndexChanged = i => events.Add("selected " + i),
            OnItemClick = i => events.Add("click " + i),
        });
        var window = Show(host);
        var list = List(host);

        ClickItem(window, list, 0);
        Assert.Equal(["selected 0", "click 0"], events);

        // Clicking the selected item again (a double tap, headless): a click and no selection change.
        events.Clear();
        ClickItem(window, list, 0);
        Assert.Equal(["click 0"], events);

        // A button in an item handles its own click.
        events.Clear();
        var button = ((StackPanel)ItemControl(list, 1)!).Children.OfType<Button>().Single();
        TestHost.HoldOffRepeat(button);
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(events, e => e.StartsWith("click", StringComparison.Ordinal));
        Assert.Contains("button", events);

        // Nor is a tap in a text box in an item a click on the item.
        events.Clear();
        var box = ((StackPanel)ItemControl(list, 2)!).Children.OfType<TextBox>().Single();
        var inBox = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inBox, MouseButton.Left);
        window.MouseUp(inBox, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(events, e => e.StartsWith("click", StringComparison.Ordinal));

        events.Clear();
        list.ContainerFromIndex(0)!.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, events.Count(e => e == "click 0"));

        // Ctrl+Space is a selection key, not a click.
        events.Clear();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(events, e => e.StartsWith("click", StringComparison.Ordinal));
    }

    private sealed record Person(string Id, string Name);

    private sealed record Fruit(string Id, string Name) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    [AvaloniaFact]
    public void Typed_items_are_keyed_by_their_selector_and_report_the_items_themselves()
    {
        IReadOnlyList<Person> people = [new("p1", "Ada"), new("p2", "Grace"), new("p3", "Linus")];
        var clicked = new List<Person>();
        var snapshots = new List<IReadOnlyList<Person>>();
        var host = new TestHost(() => ListBox(people, p => p.Id, (p, i) => TextBlock(i + ":" + p.Name).WithKey("ignored")) with
        {
            OnItemClick = clicked.Add,
            OnSelectionChanged = snapshots.Add,
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
        });
        var window = Show(host);
        var list = List(host);
        var element = Assert.IsType<ListBoxElement<Person>>(global::Isotope.UI.Core.Reconciler.GetElementTag(list));
        Assert.Equal(["p1", "p2", "p3"], element.Items.Cast<Element>().Select(e => e.Key));
        Assert.Same(people, element.ItemsSource);
        Assert.Equal(["0:Ada", "1:Grace", "2:Linus"], Texts(list));
        var grace = ItemControl(list, 1);

        ClickItem(window, list, 2);
        ClickItem(window, list, 0);
        Assert.Equal([people[2], people[0]], clicked);
        Assert.Equal([people[0], people[2]], snapshots[^1]);

        people = [people[1], people[2], people[0]];
        host.Render();
        Assert.Same(grace, ItemControl(list, 0));
        Assert.Equal(["0:Grace", "1:Linus", "2:Ada"], Texts(list));
    }

    [AvaloniaFact]
    public void An_item_the_template_leaves_out_shifts_neither_the_clicks_nor_the_selection()
    {
        IReadOnlyList<Person> people = [new("p1", "Hidden"), new("p2", "Grace"), new("p3", "Linus")];
        var clicked = new List<Person>();
        var snapshots = new List<IReadOnlyList<Person>>();
        var host = new TestHost(() => ListBox(people, p => p.Id, (p, _) => p.Name == "Hidden" ? Empty() : TextBlock(p.Name)) with
        {
            OnItemClick = clicked.Add,
            OnSelectionChanged = snapshots.Add,
        });
        var window = Show(host);
        var list = List(host);
        Assert.Equal(["Grace", "Linus"], Texts(list));

        ClickItem(window, list, 0);
        Assert.Equal([people[1]], clicked);
        Assert.Equal([people[1]], snapshots[^1]);
        Assert.Throws<InvalidOperationException>(() => ListBox(people, p => p.Id, (p, _) => Group(TextBlock(p.Name))));
    }

    [AvaloniaFact]
    public void Self_keyed_typed_items_take_their_own_keys_and_a_null_template_result_is_refused()
    {
        IReadOnlyList<Fruit> fruits = [new("f1", "Apple"), new("f2", "Pear")];
        var list = ListBox(fruits, (f, _) => TextBlock(f.Name));
        Assert.Equal(["f1", "f2"], list.Items.Cast<Element>().Select(e => e.Key));
        Assert.Throws<InvalidOperationException>(() => ListBox(fruits, f => f.Id, (_, _) => null!));
        Assert.Throws<ArgumentNullException>(() => ListBox((Element[])null!));
    }

    [AvaloniaFact]
    public void The_guide_s_example_selects_and_opens_a_person()
    {
        IReadOnlyList<ListBoxExamples.Person> people = [new("p1", "Ada"), new("p2", "Grace")];
        var host = new TestHost(() => Component(ctx => ListBoxExamples.People(ctx, people)));
        var window = Show(host);
        var lists = host.All<ListBox>();
        Assert.Equal(["1. Ada", "2. Grace"], Texts(lists[0]));
        Assert.Equal(SelectionMode.Multiple, lists[1].SelectionMode);

        ClickItem(window, lists[0], 1);
        host.Flush();
        Assert.Equal(1, List(host).SelectedIndex);
        Assert.Contains("Opened Grace", host.Texts());
    }

    [AvaloniaFact]
    public void A_long_list_mounts_only_the_items_in_view_and_a_reverse_mounts_those_it_brings_into_view()
    {
        var keys = Enumerable.Range(0, 10_000).Select(i => "k" + i).ToArray();
        var host = new TestHost(() => ListBox(Rows(keys)).Height(300));
        var list = List(host);
        Assert.Equal(10_000, list.Items.Count);
        Assert.Equal(0, Mounted(list)); // nothing shown yet
        var window = Show(host, 400);
        var realised = list.GetRealizedContainers().Count();
        Assert.InRange(realised, 1, 50); // 8 measured: 300 DIPs of 32-DIP Fluent items, plus the panel's buffer
        Assert.Equal(realised, Mounted(list));
        var top = ItemControl(list, 0);

        var edits = new List<NotifyCollectionChangedAction>();
        list.Items.CollectionChanged += (_, e) => edits.Add(e.Action);
        keys = Enumerable.Reverse(keys).ToArray();
        host.Render();
        Layout(window);
        Assert.All(edits, a => Assert.Equal(NotifyCollectionChangedAction.Move, a));
        var realisedAfter = list.GetRealizedContainers().Count();
        Assert.InRange(realisedAfter, 1, 50);
        Assert.Equal("k9999", ((TextBlock)((ContentControl)list.ContainerFromIndex(0)!).Content!).Text);
        // The rows now in view were mounted as they came into view; those shown before stay mounted, at the end.
        Assert.Equal(realised + realisedAfter, Mounted(list));
        Assert.Same(top, ItemControl(list, 9_999));
    }

    [AvaloniaFact]
    public void An_item_is_mounted_when_it_is_first_shown_from_its_latest_element()
    {
        var keys = Enumerable.Range(0, 1000).Select(i => "k" + i).ToArray();
        var label = "a";
        var host = new TestHost(() => ListBox(keys.Select(k => (Element)TextBlock(k + " " + label).WithKey(k)).ToArray()).Height(300));
        var list = List(host);
        Assert.Equal(0, Mounted(list));
        var window = Show(host);
        var shown = Mounted(list);
        Assert.InRange(shown, 1, 50);

        // A render updates the items shown and mounts none: an item out of view only takes its new element.
        label = "b";
        host.Render();
        Assert.Equal(shown, Mounted(list));
        Assert.Equal("k0 b", TextOf(ItemControl(list, 0)));
        Assert.Null(ItemControl(list, 500));

        ScrollTo(window, list, 500);
        Assert.Equal("k500 b", TextOf(ItemControl(list, 500)));
        Assert.Same(ItemControl(list, 500), ((ContentControl)list.ContainerFromIndex(500)!).Content);
    }

    [AvaloniaFact]
    public void An_item_scrolled_out_of_view_stays_mounted_keeps_its_state_and_re_renders_itself_there()
    {
        var keys = Enumerable.Range(0, 1000).Select(i => "k" + i).ToArray();
        var setters = new Dictionary<string, Action<int>>();
        int cleanups = 0;
        Element Row(string key) => Component(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            setters[key] = setN;
            ctx.UseEffect(() => () => cleanups++);
            return TextBlock(key + " " + n);
        }).WithKey(key);
        // One element for every render: a row's own re-render reaches it only through the dirty path.
        var rows = ListBox(keys.Select(Row).ToArray()).Height(300);
        var host = new TestHost(() => rows);
        var window = Show(host);
        var list = List(host);
        setters["k1"](5);
        host.Flush();
        var row = ItemControl(list, 1)!;
        Assert.Equal("k1 5", TextOf(row));

        ScrollTo(window, list, 900);
        Assert.Null(list.ContainerFromIndex(1)); // its container shows another item now
        Assert.Null(row.Parent);
        Assert.Equal(0, cleanups);

        // Out of view, it re-renders itself: the dirty path goes on from the list box.
        setters["k1"](6);
        host.Flush();
        Assert.Equal("k1 6", TextOf(row));

        ScrollTo(window, list, 0);
        Assert.Same(row, ((ContentControl)list.ContainerFromIndex(1)!).Content);
        Assert.Equal("k1 6", TextOf(row));
        Assert.Equal(0, cleanups);

        var mounted = Mounted(list);
        host.Unmount();
        Assert.Equal(mounted, cleanups);
    }

    [AvaloniaFact]
    public void An_item_mounted_during_layout_reads_the_providers_the_list_was_rendered_under()
    {
        var keys = Enumerable.Range(0, 1000).Select(i => "k" + i).ToArray();
        var suffix = "1";
        var host = new TestHost(() => VStack(
            ListBox(keys.Select(k => (Element)Component(ctx => TextBlock(k + ctx.UseContext(Suffix))).WithKey(k)).ToArray()).Height(300))
            .Provide(Suffix, suffix));
        var window = Show(host);
        var list = List(host);
        Assert.Equal("k01", TextOf(ItemControl(list, 0)));

        ScrollTo(window, list, 500);
        Assert.Equal("k5001", TextOf(ItemControl(list, 500)));

        suffix = "2";
        host.Render();
        ScrollTo(window, list, 900);
        Assert.Equal("k9002", TextOf(ItemControl(list, 900)));
        Assert.Equal("k5002", TextOf(ItemControl(list, 500))); // mounted before, out of view: rendered with the new value
    }

    // Every text under a control: its text blocks', its content's and its children's.
    private static IEnumerable<string> AllTexts(object? c) => c switch
    {
        TextBlock t => t.Text is { } s ? [s] : [],
        Decorator d => AllTexts(d.Child),
        Panel p => p.Children.SelectMany(AllTexts),
        ContentControl cc => AllTexts(cc.Content),
        _ => [],
    };

    [AvaloniaFact]
    public void An_item_that_throws_when_it_is_shown_shows_the_fallback_in_its_place()
    {
        var host = new TestHost(() => ListBox(
            TextBlock("a").WithKey("a"),
            Component(_ => throw new InvalidOperationException("row b failed")).WithKey("b"),
            TextBlock("c").WithKey("c")));
        Show(host);
        var list = List(host);
        Assert.Equal("a", TextOf(ItemControl(list, 0)));
        Assert.Contains(AllTexts(ItemControl(list, 1)), t => t.Contains("row b failed", StringComparison.Ordinal));
        Assert.Equal("c", TextOf(ItemControl(list, 2)));
    }

    [AvaloniaFact]
    public void Removing_an_item_tears_down_what_it_mounted_and_one_never_shown_has_nothing_to_tear_down()
    {
        var keys = Enumerable.Range(0, 1000).Select(i => "k" + i).ToArray();
        var cleaned = new List<string>();
        Element Row(string key) => Component(ctx =>
        {
            ctx.UseEffect(() => () => cleaned.Add(key));
            return TextBlock(key);
        }).WithKey(key);
        var host = new TestHost(() => ListBox(keys.Select(Row).ToArray()).Height(300));
        Show(host);
        var list = List(host);

        keys = keys.Where(k => k is not ("k0" or "k900")).ToArray();
        host.Render();
        Assert.Equal(["k0"], cleaned);
        Assert.Equal(998, list.Items.Count);
        Assert.Equal("k1", TextOf(ItemControl(list, 0)));
    }

    [AvaloniaFact]
    public void An_item_whose_element_changes_type_gets_a_new_control_in_its_container()
    {
        var button = false;
        var host = new TestHost(() => ListBox(
            TextBlock("a").WithKey("a"),
            button ? Button("b", () => { }).WithKey("b") : TextBlock("b").WithKey("b")));
        Show(host);
        var list = List(host);
        Assert.IsType<TextBlock>(ItemControl(list, 1));

        button = true;
        host.Render();
        var shown = Assert.IsType<Button>(ItemControl(list, 1));
        Assert.Same(shown, ((ContentControl)list.ContainerFromIndex(1)!).Content);
    }

    [AvaloniaFact]
    public void A_list_goes_from_strings_to_elements_and_back()
    {
        var elements = false;
        var cleanups = 0;
        string[] strings = ["s1", "s2"];
        var host = new TestHost(() => elements
            ? ListBox(Component(ctx =>
            {
                ctx.UseEffect(() => () => cleanups++);
                return TextBlock("e1");
            }).WithKey("e1"))
            : ListBox(strings));
        var window = Show(host);
        var list = List(host);
        Assert.Equal(strings, list.Items.Cast<string>());

        elements = true;
        host.Render();
        Layout(window);
        Assert.Same(list, List(host));
        Assert.Equal(["e1"], Texts(list));

        elements = false;
        host.Render();
        Assert.Equal(1, cleanups);
        Assert.Equal(strings, list.Items.Cast<string>());
    }

    [AvaloniaFact]
    public void A_list_of_values_and_elements_shows_both()
    {
        var host = new TestHost(() => new ListBoxElement(["value", TextBlock("element")]));
        Show(host);
        var list = List(host);
        Assert.Equal("value", ((ContentControl)list.ContainerFromIndex(0)!).Content);
        Assert.Equal("element", TextOf(((ContentControl)list.ContainerFromIndex(1)!).Content));
    }
}
