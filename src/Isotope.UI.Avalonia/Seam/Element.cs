using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;

namespace Isotope.UI.Core;

/// <summary>
/// The base of every element: an immutable description of one piece of UI. The engine-facing members keep
/// upstream's names (<c>Key</c>, <c>Modifiers</c>, <c>CallSite</c>, <c>ContextValues</c>, <c>CanSkipUpdate</c>, …);
/// their value types are Avalonia's.
/// </summary>
public abstract record Element
{
    /// <summary>Stable identity across re-renders. When set, children are matched by key.</summary>
    public string? Key { get; init; }

    /// <summary>Layout and visual modifiers applied to this element's control.</summary>
    public ElementModifiers? Modifiers { get; init; }

    /// <summary>The source location that created this element, or <c>null</c> when nothing stamped it.
    /// Diagnostic only: it takes no part in <see cref="ShallowEquals"/>.</summary>
    public SourceLocation? CallSite { get; init; }

    /// <summary>Enter and exit transitions (tier 3: not played on Avalonia yet).</summary>
    public Isotope.UI.Animation.ElementTransition? ElementTransition { get; init; }

    /// <summary>Resource overrides for this element's subtree (tier 3: not applied on Avalonia yet).</summary>
    public Isotope.UI.Elements.ResourceOverrides? ResourceOverrides { get; init; }

    /// <summary>Context values provided to this element's subtree (see <c>ElementExtensions.Provide</c>).</summary>
    public IReadOnlyDictionary<ContextBase, object?>? ContextValues { get; init; }

    /// <summary>Attached layout data (grid row, canvas position, …), by type.</summary>
    public IReadOnlyDictionary<Type, object>? Attached { get; init; }

    /// <summary>Gets the attached data of type <typeparamref name="T"/>, or <c>null</c>.</summary>
    internal T? GetAttached<T>() where T : class =>
        Attached is not null && Attached.TryGetValue(typeof(T), out var val) ? (T)val : null;

    /// <summary>A copy of this element with <paramref name="data"/> attached under its type.</summary>
    internal Element SetAttached(object data)
    {
        var dict = Attached is null ? new Dictionary<Type, object>(1) : new Dictionary<Type, object>(Attached);
        dict[data.GetType()] = data;
        return this with { Attached = dict };
    }

    /// <summary>A string is a text block: <c>Stack(Orientation.Vertical, "a", "b")</c>.</summary>
    public static implicit operator Element(string text) => new TextBlockElement(text);

    /// <summary>
    /// True when the element carries an event-handler delegate. A skipped update still refreshes the
    /// control's mounted element so its handlers dispatch to the current render's closures, and a change
    /// in callback presence declines the skip.
    /// </summary>
    internal virtual bool HasCallbacks => false;

    /// <summary>
    /// True when the child can be skipped entirely: structurally identical, not theme-sensitive, and with
    /// the same callback presence. Shared by the positional and keyed arms of the child diff.
    /// </summary>
    internal static bool CanSkipUpdate(Element oldEl, Element newEl)
        => ShallowEquals(oldEl, newEl)
            && !ChildDiffHints.IsThemeSensitive(newEl)
            && oldEl.HasCallbacks == newEl.HasCallbacks;

    /// <summary>
    /// True only when the two elements are provably identical for rendering. Delegates are ignored (the
    /// skip path refreshes them); children arrays compare by reference; component wrappers and unknown
    /// element types never compare equal unless they are the same instance.
    /// </summary>
    internal static bool ShallowEquals(Element a, Element b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType()) return false;
        if (!string.Equals(a.Key, b.Key, StringComparison.Ordinal)) return false;
        if (!ModifiersEqual(a.Modifiers, b.Modifiers)) return false;
        if (!ReferenceEquals(a.ElementTransition, b.ElementTransition) && !Equals(a.ElementTransition, b.ElementTransition)) return false;
        if (!Equals(a.ResourceOverrides, b.ResourceOverrides)) return false;
        if (!ContextValuesEqual(a.ContextValues, b.ContextValues)) return false;
        if (!DictionariesEqual(a.Attached, b.Attached)) return false;
        return (a, b) switch
        {
            (EmptyElement, EmptyElement) => true,
            (TextBlockElement x, TextBlockElement y) => x.Content == y.Content && ReferenceEquals(x.Setters, y.Setters),
            (ButtonElement x, ButtonElement y) => x.Label == y.Label && x.IsEnabled == y.IsEnabled
                && x.ContentElement is null && y.ContentElement is null && ReferenceEquals(x.Setters, y.Setters),
            (StackElement x, StackElement y) => x.Orientation == y.Orientation && x.Spacing == y.Spacing
                && ReferenceEquals(x.Children, y.Children) && ReferenceEquals(x.Setters, y.Setters),
            (FlexElement x, FlexElement y) => x.Direction == y.Direction && x.JustifyContent == y.JustifyContent
                && x.AlignItems == y.AlignItems && x.AlignContent == y.AlignContent && x.Wrap == y.Wrap
                && x.ColumnGap == y.ColumnGap && x.RowGap == y.RowGap && x.FlexPadding == y.FlexPadding
                && ReferenceEquals(x.Children, y.Children) && ReferenceEquals(x.Setters, y.Setters),
            (TextBoxElement x, TextBoxElement y) => x.Value.Equals(y.Value) && x.PlaceholderText == y.PlaceholderText
                && x.IsReadOnly == y.IsReadOnly && x.AcceptsReturn == y.AcceptsReturn && x.TextWrapping == y.TextWrapping
                && x.MaxLength == y.MaxLength && ReferenceEquals(x.Setters, y.Setters),
            (SliderElement x, SliderElement y) => x.Value.Equals(y.Value) && x.Min == y.Min && x.Max == y.Max
                && x.Orientation == y.Orientation && x.TickFrequency == y.TickFrequency && x.TickPlacement == y.TickPlacement
                && ReferenceEquals(x.Setters, y.Setters),
            // #19: the records upstream has arms for, over their members here. ListBox, Expander, the pickers and the
            // WrapPanel have none upstream either: they always update.
            (CheckBoxElement x, CheckBoxElement y) => x.IsChecked.Equals(y.IsChecked) && x.Label == y.Label
                && x.IsThreeState == y.IsThreeState && x.CheckedState == y.CheckedState && ReferenceEquals(x.Setters, y.Setters),
            (ToggleSwitchElement x, ToggleSwitchElement y) => x.IsOn.Equals(y.IsOn) && x.OnContent == y.OnContent
                && x.OffContent == y.OffContent && x.Header == y.Header && ReferenceEquals(x.Setters, y.Setters),
            (RadioButtonElement x, RadioButtonElement y) => x.Label == y.Label && x.IsChecked.Equals(y.IsChecked)
                && x.GroupName == y.GroupName && ReferenceEquals(x.Setters, y.Setters),
            // ComboBox's three callbacks are subscribed separately, each when it first appears, so a change in which of
            // them are present declines the skip (HasCallbacks alone only says whether any is).
            (ComboBoxElement x, ComboBoxElement y) => ReferenceEquals(x.Items, y.Items) && x.SelectedIndex.Equals(y.SelectedIndex)
                && x.PlaceholderText == y.PlaceholderText && x.IsEditable == y.IsEditable
                && x.MaxDropDownHeight.Equals(y.MaxDropDownHeight) && ReferenceEquals(x.Setters, y.Setters)
                && (x.OnSelectedIndexChanged is null) == (y.OnSelectedIndexChanged is null)
                && (x.OnDropDownOpened is null) == (y.OnDropDownOpened is null)
                && (x.OnDropDownClosed is null) == (y.OnDropDownClosed is null),
            (ProgressElement x, ProgressElement y) => x.Value == y.Value && x.Minimum == y.Minimum && x.Maximum == y.Maximum
                && ReferenceEquals(x.Setters, y.Setters),
            (ImageElement x, ImageElement y) => x.Source == y.Source && x.Stretch == y.Stretch && ReferenceEquals(x.Setters, y.Setters),
            (BorderElement x, BorderElement y) => Equals(x.Background, y.Background) && Equals(x.BorderBrush, y.BorderBrush)
                && x.CornerRadius == y.CornerRadius && x.BorderThickness == y.BorderThickness && x.Padding == y.Padding
                && ReferenceEquals(x.Child, y.Child) && ReferenceEquals(x.Setters, y.Setters),
            (GridElement x, GridElement y) => x.RowSpacing == y.RowSpacing && x.ColumnSpacing == y.ColumnSpacing
                && Equals(x.Definition, y.Definition) && ReferenceEquals(x.Children, y.Children)
                && ReferenceEquals(x.Setters, y.Setters),
            (ScrollViewerElement x, ScrollViewerElement y) => x.HorizontalScrollBarVisibility == y.HorizontalScrollBarVisibility
                && x.VerticalScrollBarVisibility == y.VerticalScrollBarVisibility && ReferenceEquals(x.Child, y.Child)
                && ReferenceEquals(x.Setters, y.Setters),
            // #26: as upstream's arms, a declared element content always updates (its child is reconciled). Unlike
            // upstream's, ToggleButton's compares the three-state members too, so a changed CheckedState is written.
            (ToggleButtonElement x, ToggleButtonElement y) => x.Label == y.Label && x.IsChecked == y.IsChecked
                && x.IsThreeState == y.IsThreeState && x.CheckedState == y.CheckedState
                && x.ContentElement is null && y.ContentElement is null && ReferenceEquals(x.Setters, y.Setters),
            (RepeatButtonElement x, RepeatButtonElement y) => x.Label == y.Label && x.Delay == y.Delay && x.Interval == y.Interval
                && x.ContentElement is null && y.ContentElement is null && ReferenceEquals(x.Setters, y.Setters),
            (HyperlinkButtonElement x, HyperlinkButtonElement y) => x.Content == y.Content && x.NavigateUri == y.NavigateUri
                && x.ContentElement is null && y.ContentElement is null && ReferenceEquals(x.Setters, y.Setters),
            _ => false,
        };
    }

    /// <summary>Value equality of two modifier records (null equals an empty record).</summary>
    internal static bool ModifiersEqual(ElementModifiers? a, ElementModifiers? b)
        => ReferenceEquals(a, b) || (a ?? ElementModifiers.Empty).Equals(b ?? ElementModifiers.Empty);

    /// <summary>Whether two sets of provided context values are equal.</summary>
    internal static bool ContextValuesEqual(IReadOnlyDictionary<ContextBase, object?>? a, IReadOnlyDictionary<ContextBase, object?>? b)
        => DictionariesEqual(a, b);

    private static bool DictionariesEqual<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? a, IReadOnlyDictionary<TKey, TValue>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
        if (a is null || b is null) return true; // both empty
        foreach (var (k, v) in a)
            if (!b.TryGetValue(k, out var other) || !Equals(v, other)) return false;
        return true;
    }
}

/// <summary>Renders nothing. Filtered out of child lists.</summary>
public record EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();
}

/// <summary>A transparent grouping element (like React's Fragment), flattened into its container's children.</summary>
public record GroupElement(Element[] Children) : Element;

/// <summary>Wraps an element with modifiers. Kept for upstream compatibility: new code sets <see cref="Element.Modifiers"/>.</summary>
public record ModifiedElement(Element Inner, ElementModifiers WrappedModifiers) : Element;

/// <summary>A class component (<see cref="Component"/>) in the element tree.</summary>
public record ComponentElement(
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type ComponentType,
    object? Props = null) : Element
{
    // Creates the instance without reflection. Note: record equality compares fields too, so two elements
    // with different factories are unequal; the reconciler matches components by ComponentType (CanUpdate)
    // and props (ShouldUpdate), never by element equality.
    internal Func<Component>? _factory;

    internal Component CreateInstance() =>
        _factory is not null ? _factory() : (Component)Activator.CreateInstance(ComponentType)!;
}

/// <summary>A <see cref="ComponentElement"/> with typed props.</summary>
public record ComponentElement<TProps> : ComponentElement
{
    public ComponentElement(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type ComponentType,
        TProps Props) : base(ComponentType, Props) { }

    public new TProps Props
    {
        get => (TProps)base.Props!;
        init => base.Props = value;
    }
}

/// <summary>A function component: a render function that receives a <see cref="RenderContext"/> for hooks.</summary>
public record FuncElement(Func<RenderContext, Element> RenderFunc) : Element;

/// <summary>
/// A memoized function component: re-renders from its parent only when <see cref="Dependencies"/> change.
/// <c>null</c> dependencies render once, then only on the component's own state changes.
/// </summary>
public record MemoElement(Func<RenderContext, Element> RenderFunc, object?[]? Dependencies = null) : Element;

/// <summary>
/// Modifiers applied to an element's control, with Avalonia value types. Only what the engine and the
/// mounted Avalonia controls need so far; upstream's full set (accessibility, flyouts, gestures, …) comes
/// with the element vocabulary.
/// </summary>
public record ElementModifiers
{
    internal static readonly ElementModifiers Empty = new();

    public Thickness? Margin { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    public double? Opacity { get; init; }
    public bool? IsEnabled { get; init; }
    public IBrush? Background { get; init; }
    public string? AutomationName { get; init; }

    /// <summary>The text of the control's tooltip (Avalonia's <c>ToolTip.Tip</c>, #26).</summary>
    public string? ToolTip { get; init; }

    /// <summary>Which side of the control its tooltip opens on (Avalonia's <c>ToolTip.Placement</c>).</summary>
    public Avalonia.Controls.PlacementMode? ToolTipPlacement { get; init; }

    /// <summary>The menu the control opens on a right-click or the menu key (Avalonia's <c>ContextFlyout</c>, #26).</summary>
    public MenuFlyoutContentElement? ContextFlyout { get; init; }

    /// <summary>These modifiers, with <paramref name="other"/>'s set values taking precedence.</summary>
    public ElementModifiers Merge(ElementModifiers other) => new()
    {
        Margin = other.Margin ?? Margin,
        Width = other.Width ?? Width,
        Height = other.Height ?? Height,
        HorizontalAlignment = other.HorizontalAlignment ?? HorizontalAlignment,
        VerticalAlignment = other.VerticalAlignment ?? VerticalAlignment,
        Opacity = other.Opacity ?? Opacity,
        IsEnabled = other.IsEnabled ?? IsEnabled,
        Background = other.Background ?? Background,
        AutomationName = other.AutomationName ?? AutomationName,
        ToolTip = other.ToolTip ?? ToolTip,
        ToolTipPlacement = other.ToolTipPlacement ?? ToolTipPlacement,
        ContextFlyout = other.ContextFlyout ?? ContextFlyout,
    };
}

/// <summary>Fluent helpers on elements.</summary>
public static class ElementExtensions
{
    /// <summary>Provides <paramref name="value"/> for <paramref name="context"/> to this element's subtree.</summary>
    public static TElement Provide<TElement, T>(this TElement element, Context<T> context, T value) where TElement : Element
    {
        var dict = element.ContextValues is null
            ? new Dictionary<ContextBase, object?>(1)
            : new Dictionary<ContextBase, object?>(element.ContextValues);
        dict[context] = value;
        return element with { ContextValues = dict };
    }

    /// <summary>This element with modifiers changed by <paramref name="change"/>.</summary>
    public static TElement WithModifiers<TElement>(this TElement element, Func<ElementModifiers, ElementModifiers> change) where TElement : Element
        => element with { Modifiers = change(element.Modifiers ?? ElementModifiers.Empty) };
}
