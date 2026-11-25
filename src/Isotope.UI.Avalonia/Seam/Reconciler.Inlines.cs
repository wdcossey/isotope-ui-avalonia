using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Isotope.UI.Core.Diagnostics;

namespace Isotope.UI.Core;

// Rich text (#65): a text block's content, its text or its inlines (InlineElements.cs), and the diff of each inline list.
// An inline list is reconciled as a panel's children are: matched by key, else by position among the unkeyed inlines, and
// an inline whose element is of another type is replaced. A matched inline is updated in place (only what changed is
// written), a keyed one moved to its new place, and an inline UI container's child reconciled as a child slot, so a
// component inside keeps its state. Upstream rebuilds a RichTextBlock's blocks unless their shape is unchanged, and keeps
// only inline UI across renders; inlines have no keys there.
public sealed partial class Reconciler
{
    /// <summary>Mounts a text block's content: its inlines when it has some, else its text.</summary>
    internal void MountTextContent(TextBlock control, string? text, InlineElement[]? inlines, Action requestRerender)
    {
        if (inlines is null)
        {
            if (text is not null) control.Text = text;
            return;
        }
        // Avalonia turns a text already there into a run when inlines are added: there is none on a new control.
        var target = control.Inlines ??= new InlineCollection();
        foreach (var inline in inlines) target.Add(MountInline(inline, control, requestRerender));
    }

    /// <summary>
    /// Updates a text block's content. Avalonia clears the inlines when the text is written, and turns the text into a run
    /// when inlines are added, so a change from one to the other tears down the inlines or clears the text first.
    /// </summary>
    internal void UpdateTextContent(TextBlock control, string? oldText, InlineElement[]? oldInlines, string? newText,
        InlineElement[]? newInlines, Action requestRerender)
    {
        if (newInlines is null)
        {
            if (oldInlines is not null)
            {
                UnmountInlines(control.Inlines);
                control.Inlines?.Clear();
                if (newText is not null) control.Text = newText;
                else control.ClearValue(TextBlock.TextProperty);
            }
            else if (newText is not null && !string.Equals(oldText, newText, StringComparison.Ordinal))
                control.Text = newText;
            return;
        }
        if (oldInlines is null)
        {
            control.ClearValue(TextBlock.TextProperty);
            control.Inlines?.Clear();
            MountTextContent(control, null, newInlines, requestRerender);
            return;
        }
        ReconcileInlines(oldInlines, newInlines, control.Inlines ??= new InlineCollection(), control, requestRerender);
    }

    /// <summary>
    /// Unmounts the controls in a text block's inlines (its handler's unmount hook: the generic walk over a control's children
    /// doesn't reach a text block's), as upstream's UnmountInlineUIChildren does for a RichTextBlock.
    /// </summary>
    internal void UnmountTextContent(TextBlock control) => UnmountInlines(control.Inlines);

    // An inline's identity among its siblings: its key, else its position among the unkeyed ones.
    private readonly record struct InlineIdentity(string? Key, int Position);

    private static InlineIdentity IdentityOf(InlineElement inline, ref int unkeyed)
        => inline.Key is { } key ? new InlineIdentity(key, -1) : new InlineIdentity(null, unkeyed++);

    /// <summary>
    /// Reconciles <paramref name="target"/>, which holds what <paramref name="oldInlines"/> mounted, in its order, with
    /// <paramref name="newInlines"/>. <paramref name="host"/> is the text block they are in.
    /// </summary>
    internal void ReconcileInlines(InlineElement[] oldInlines, InlineElement[] newInlines, InlineCollection target, TextBlock host,
        Action requestRerender)
    {
        // Something else changed the collection (a setter): what it holds can't be matched to the elements, so it is rebuilt.
        if (!HoldsWhatWasMounted(target, oldInlines))
        {
            UnmountInlines(target);
            target.Clear();
            foreach (var inline in newInlines) target.Add(MountInline(inline, host, requestRerender));
            return;
        }

        var byIdentity = new Dictionary<InlineIdentity, int>(oldInlines.Length);
        int unkeyed = 0;
        for (int i = 0; i < oldInlines.Length; i++)
            byIdentity.TryAdd(IdentityOf(oldInlines[i], ref unkeyed), i); // a repeated key matches its first inline only

        var kept = new bool[oldInlines.Length];
        var next = new Inline[newInlines.Length];
        unkeyed = 0;
        for (int j = 0; j < newInlines.Length; j++)
        {
            var element = newInlines[j];
            if (byIdentity.Remove(IdentityOf(element, ref unkeyed), out var i) && oldInlines[i].GetType() == element.GetType())
            {
                kept[i] = true;
                next[j] = target[i];
                UpdateInline(oldInlines[i], element, next[j], host, requestRerender);
            }
            else
                next[j] = MountInline(element, host, requestRerender);
        }

        for (int i = oldInlines.Length - 1; i >= 0; i--)
        {
            if (kept[i]) continue;
            var gone = target[i];
            target.RemoveAt(i);
            UnmountInline(gone);
        }

        // The kept inlines are in their old order: each moves to its new place, and a new one goes in at its own.
        for (int j = 0; j < next.Length; j++)
        {
            if (j < target.Count && ReferenceEquals(target[j], next[j])) continue;
            int at = IndexOf(target, next[j], j + 1);
            if (at >= 0) target.Move(at, j);
            else target.Insert(j, next[j]);
        }
    }

    // Whether each inline is of the type its element mounts, as the last render left them.
    private static bool HoldsWhatWasMounted(InlineCollection target, InlineElement[] elements)
    {
        if (target.Count != elements.Length) return false;
        for (int i = 0; i < elements.Length; i++)
            if (target[i].GetType() != InlineType(elements[i])) return false;
        return true;
    }

    private static Type? InlineType(InlineElement element) => element switch
    {
        BoldElement => typeof(Bold),
        ItalicElement => typeof(Italic),
        UnderlineElement => typeof(Underline),
        SpanElement => typeof(Span),
        RunElement => typeof(Run),
        LineBreakElement => typeof(LineBreak),
        InlineUIContainerElement => typeof(InlineUIContainer),
        _ => null,
    };

    private static int IndexOf(InlineCollection inlines, Inline inline, int from)
    {
        for (int i = from; i < inlines.Count; i++)
            if (ReferenceEquals(inlines[i], inline)) return i;
        return -1;
    }

    private Inline MountInline(InlineElement element, TextBlock host, Action requestRerender)
    {
        Inline inline = element switch
        {
            BoldElement => new Bold(),
            ItalicElement => new Italic(),
            UnderlineElement => new Underline(),
            SpanElement => new Span(), // a span, and a paragraph
            RunElement run => new Run(run.Text),
            LineBreakElement => new LineBreak(),
            InlineUIContainerElement => new InlineUIContainer(),
            _ => throw new NotSupportedException($"{element.GetType().Name} is an inline Avalonia has no counterpart for."),
        };
        ApplyInlineProperties(inline, null, element, host);
        switch (element)
        {
            case SpanElement span:
                var inlines = ((Span)inline).Inlines;
                foreach (var child in span.Inlines) inlines.Add(MountInline(child, host, requestRerender));
                break;
            case InlineUIContainerElement { Child: { } child }:
                if (Mount(child, requestRerender) is { } control) ((InlineUIContainer)inline).Child = control;
                break;
        }
        return inline;
    }

    private void UpdateInline(InlineElement oldElement, InlineElement newElement, Inline inline, TextBlock host, Action requestRerender)
    {
        ApplyInlineProperties(inline, oldElement, newElement, host);
        switch (newElement)
        {
            case RunElement run:
                if (!string.Equals(((RunElement)oldElement).Text, run.Text, StringComparison.Ordinal)) ((Run)inline).Text = run.Text;
                break;
            case SpanElement span:
                ReconcileInlines(((SpanElement)oldElement).Inlines, span.Inlines, ((Span)inline).Inlines, host, requestRerender);
                break;
            case InlineUIContainerElement ui:
                var container = (InlineUIContainer)inline;
                var existing = container.Child;
                var child = ReconcileV1Child(((InlineUIContainerElement)oldElement).Child, ui.Child, existing, requestRerender);
                if (ReferenceEquals(child, existing)) break;
                if (child is null) container.ClearValue(InlineUIContainer.ChildProperty);
                else container.Child = child;
                break;
        }
    }

    // An inline's text properties: its member, else (for the five that are element-wide modifiers too) the modifier; under
    // them, its theme tokens (.Foreground(Theme.X)), resolved for its text block, as a control's are for the control.
    private static void ApplyInlineProperties(Inline inline, InlineElement? oldEl, InlineElement newEl, TextBlock host)
    {
        var oldM = oldEl?.Modifiers;
        var newM = newEl.Modifiers;
        if (!ReferenceEquals(oldM, newM)) WarnIfModifiersIgnored(newEl);
        ApplyThemeBindings(inline, host, newEl.ThemeBindings);
        Set(inline, TextElement.FontSizeProperty, oldEl?.FontSize ?? oldM?.FontSize, newEl.FontSize ?? newM?.FontSize);
        Set(inline, TextElement.FontWeightProperty, oldEl?.FontWeight ?? oldM?.FontWeight, newEl.FontWeight ?? newM?.FontWeight);
        Set(inline, TextElement.FontStyleProperty, oldEl?.FontStyle, newEl.FontStyle);
        SetRef(inline, TextElement.FontFamilyProperty, oldEl?.FontFamily ?? oldM?.FontFamily, newEl.FontFamily ?? newM?.FontFamily);
        Set(inline, TextElement.FontStretchProperty, oldEl?.FontStretch, newEl.FontStretch);
        SetRef(inline, TextElement.ForegroundProperty, oldEl?.Foreground ?? oldM?.Foreground, newEl.Foreground ?? newM?.Foreground);
        SetRef(inline, TextElement.BackgroundProperty, oldEl?.Background ?? oldM?.Background, newEl.Background ?? newM?.Background);
        Set(inline, TextElement.LetterSpacingProperty, oldEl?.LetterSpacing, newEl.LetterSpacing);
        // A decoration collection compares by its decorations, so one built afresh each render isn't written again.
        if (!DecorationsEqual(oldEl?.TextDecorations, newEl.TextDecorations))
        {
            if (newEl.TextDecorations is { } decorations) inline.TextDecorations = decorations;
            else if (oldEl?.TextDecorations is not null) inline.ClearValue(Inline.TextDecorationsProperty);
        }
        Set(inline, Inline.BaselineAlignmentProperty, oldEl?.BaselineAlignment, newEl.BaselineAlignment);
    }

    private static bool DecorationsEqual(TextDecorationCollection? a, TextDecorationCollection? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var (x, y) = (a[i], b[i]);
            if (!ReferenceEquals(x, y) && (x.Location != y.Location || !Equals(x.Stroke, y.Stroke) || x.StrokeThickness != y.StrokeThickness
                || x.StrokeThicknessUnit != y.StrokeThicknessUnit || x.StrokeOffset != y.StrokeOffset || x.StrokeOffsetUnit != y.StrokeOffsetUnit
                || x.StrokeLineCap != y.StrokeLineCap || x.StrokeDashOffset != y.StrokeDashOffset
                || !(x.StrokeDashArray ?? []).SequenceEqual(y.StrokeDashArray ?? [])))
                return false;
        }
        return true;
    }

    // Modifiers reach an inline only as its text properties (FontSize, FontWeight, FontFamily, Foreground, Background); any
    // other (a margin, a size, a tooltip…) does nothing on an inline, as a text modifier does nothing on a panel. Reported
    // once per inline type, on the Isotope-UI event source (and Debug output in DEBUG builds).
    private static readonly ConcurrentDictionary<Type, byte> s_ignoredInlineModifiers = new();

    private static void WarnIfModifiersIgnored(InlineElement element)
    {
        if (element.Modifiers is not { } m || !DiagnosticLog.IsWarningEnabled) return;
        var others = m with { FontSize = null, FontWeight = null, FontFamily = null, Foreground = null, Background = null };
        if (others.Equals(ElementModifiers.Empty) || !s_ignoredInlineModifiers.TryAdd(element.GetType(), 0)) return;
        DiagnosticLog.Warning(LogCategory.Reactor, "Inline",
            $"{element.GetType().Name} has modifiers an inline doesn't take: only FontSize, FontWeight, FontFamily, Foreground and " +
            "Background reach it, and the others (a margin, a size, a tooltip…) do nothing.");
    }

    // An inline the reconciler removes: the controls in it are unmounted, and its theme bindings (and its children's) end.
    private void UnmountInline(Inline inline)
    {
        ReleaseThemeBindings(inline);
        switch (inline)
        {
            case InlineUIContainer { Child: { } child }:
                UnmountChild(child);
                break;
            case Span span:
                foreach (var nested in span.Inlines) UnmountInline(nested);
                break;
        }
    }

    private void UnmountInlines(InlineCollection? inlines)
    {
        if (inlines is null) return;
        foreach (var inline in inlines) UnmountInline(inline);
    }

    /// <summary>The controls in a text block's inlines (each inline UI container's child, inside spans too).</summary>
    internal static void VisitInlineUI(TextBlock control, Action<Control> visit)
    {
        if (control.Inlines is not { Count: > 0 } inlines) return;
        foreach (var inline in inlines) VisitInlineUI(inline, visit);
    }

    private static void VisitInlineUI(Inline inline, Action<Control> visit)
    {
        switch (inline)
        {
            case InlineUIContainer { Child: { } child }:
                visit(child);
                break;
            case Span span:
                foreach (var nested in span.Inlines) VisitInlineUI(nested, visit);
                break;
        }
    }
}
