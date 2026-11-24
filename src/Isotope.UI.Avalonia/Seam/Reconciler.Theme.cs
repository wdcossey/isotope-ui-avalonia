using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Hosting;

namespace Isotope.UI.Core;

// Theme bindings (tier 3, #41): an element's ThemeBindings (.Background(Theme.CardBackground), …) on its Avalonia control.

public sealed partial class Reconciler
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, ThemeBindingSet> s_themeBindings = new();

    // Theme.NotifyResourcesChanged() (upstream's resource refresh): the host's next root pass renders the whole tree,
    // past memoized components and the skip of unchanged elements, so a token a render resolves (ThemeRef.Resolve) is
    // resolved again everywhere. Requested from any thread; taken by the host's root pass, and put back when that pass
    // doesn't reconcile, so a request isn't lost.
    private int _resourceRefreshPending;
    private bool _resourceRefreshActive;

    /// <summary>Asks for a resource-refresh pass (upstream's member). Thread-safe.</summary>
    internal void RequestResourceRefresh() => Interlocked.Exchange(ref _resourceRefreshPending, 1);

    /// <summary>Called by a host before its root render: a requested refresh is this pass. UI thread.</summary>
    internal void BeginResourceRefreshPass() => _resourceRefreshActive = Interlocked.Exchange(ref _resourceRefreshPending, 0) != 0;

    /// <summary>Ends a host's root pass; a refresh it took but didn't reconcile is requested again.</summary>
    internal void EndResourceRefreshPass(bool reconciled)
    {
        if (_resourceRefreshActive && !reconciled) RequestResourceRefresh();
        _resourceRefreshActive = false;
    }

    /// <summary>
    /// Applies an element's <see cref="Element.ThemeBindings"/> to its control (upstream's member): each property named
    /// is bound to its token's brush, resolved for the control (<see cref="ThemeRef.Resolve(string, FrameworkElement)"/>)
    /// and resolved again when the control's theme variant, the resources it sees or the platform's contrast preference
    /// change, with no re-render. Bound at style priority, as upstream's synthesized style is, so a brush the element sets
    /// itself (a modifier or a member) wins. A property the control doesn't have is skipped, as are properties whose
    /// type isn't a brush. Bindings equal to the last ones change nothing.
    /// </summary>
    internal static void ApplyThemeBindings(UIElement control, IReadOnlyDictionary<string, ThemeRef>? bindings)
    {
        if (bindings is not { Count: > 0 } && !s_themeBindings.TryGetValue(control, out _)) return;
        s_themeBindings.GetValue(control, static c => new ThemeBindingSet(c)).Apply(bindings);
    }

    /// <summary>Tests only: the brush properties a control's theme bindings drive now.</summary>
    internal static IReadOnlyCollection<string> BoundThemeProperties(Control control) =>
        s_themeBindings.TryGetValue(control, out var set) ? set.Properties : [];

    /// <summary>One control's theme bindings, and what makes them resolve again.</summary>
    private sealed class ThemeBindingSet(Control control)
    {
        private readonly Dictionary<string, ThemeSlot> _slots = new(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, ThemeRef>? _bindings;
        private bool _hooked;
        private IColorValuesSource? _colorValues;

        public IReadOnlyCollection<string> Properties => _slots.Keys;

        public void Apply(IReadOnlyDictionary<string, ThemeRef>? bindings)
        {
            if (Same(_bindings, bindings)) return;
            _bindings = bindings is null ? null : new Dictionary<string, ThemeRef>(bindings);
            foreach (var name in _slots.Keys.ToArray())
            {
                if (bindings is not null && bindings.ContainsKey(name)) continue;
                _slots[name].Dispose();
                _slots.Remove(name);
            }
            foreach (var (name, themeRef) in bindings ?? new Dictionary<string, ThemeRef>())
            {
                if (_slots.TryGetValue(name, out var slot))
                {
                    if (slot.Ref != themeRef)
                    {
                        slot.Ref = themeRef;
                        slot.Push(Resolve(themeRef));
                    }
                    continue;
                }
                if (BrushProperty(control, name) is not { } property) continue;
                slot = new ThemeSlot(themeRef, Resolve(themeRef));
                slot.Binding = control.Bind(property, slot, BindingPriority.Style);
                _slots[name] = slot;
            }
            if (_slots.Count > 0) Hook();
            else Unhook();
        }

        private IBrush? Resolve(ThemeRef themeRef) => ThemeRef.Resolve(themeRef.ResourceKey, control);

        private void Refresh()
        {
            foreach (var slot in _slots.Values) slot.Push(Resolve(slot.Ref));
        }

        // The control's own events, which it alone holds; the platform's colour values only while the control is in a
        // visual tree, so an unmounted control isn't kept alive by them.
        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            control.ActualThemeVariantChanged += OnChanged;
            control.ResourcesChanged += OnResourcesChanged;
            control.AttachedToVisualTree += OnAttached;
            control.DetachedFromVisualTree += OnDetached;
            if (control.IsAttachedToVisualTree()) WatchColorValues();
        }

        private void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            control.ActualThemeVariantChanged -= OnChanged;
            control.ResourcesChanged -= OnResourcesChanged;
            control.AttachedToVisualTree -= OnAttached;
            control.DetachedFromVisualTree -= OnDetached;
            UnwatchColorValues();
        }

        private void OnChanged(object? sender, EventArgs e) => Refresh();

        private void OnResourcesChanged(object? sender, ResourcesChangedEventArgs e) => Refresh();

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            WatchColorValues();
            // The host (and so the colour values) may differ from where it was resolved.
            Refresh();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => UnwatchColorValues();

        private void WatchColorValues()
        {
            UnwatchColorValues();
            _colorValues = ThemeRef.ColorValuesSource(control);
            if (_colorValues is not null) _colorValues.ColorValuesChanged += OnColorValuesChanged;
        }

        private void UnwatchColorValues()
        {
            if (_colorValues is not null) _colorValues.ColorValuesChanged -= OnColorValuesChanged;
            _colorValues = null;
        }

        private void OnColorValuesChanged(object? sender, PlatformColorValues e)
        {
            if (control.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        }

        private static bool Same(IReadOnlyDictionary<string, ThemeRef>? a, IReadOnlyDictionary<string, ThemeRef>? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
            if (a is null || b is null) return true;
            foreach (var (k, v) in a)
                if (!b.TryGetValue(k, out var other) || other != v) return false;
            return true;
        }

        // The control's brush property of that name (Background, Foreground, BorderBrush, PaneBackground, …).
        private static AvaloniaProperty? BrushProperty(Control control, string name) =>
            AvaloniaPropertyRegistry.Instance.FindRegistered(control, name) is { IsReadOnly: false } p
                && p.PropertyType == typeof(IBrush)
                ? p
                : null;
    }

    /// <summary>One bound property: the token and the brush it resolved to, pushed to the binding when it changes.</summary>
    private sealed class ThemeSlot(ThemeRef themeRef, IBrush? value) : IObservable<object?>, IDisposable
    {
        private IObserver<object?>? _observer;
        private IBrush? _value = value;

        public ThemeRef Ref = themeRef;
        public IDisposable? Binding;

        public void Push(IBrush? value)
        {
            if (Equals(value, _value)) return;
            _value = value;
            _observer?.OnNext(Boxed);
        }

        // No brush: no value at this priority, so the control's own (its theme's) shows.
        private object? Boxed => (object?)_value ?? AvaloniaProperty.UnsetValue;

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            _observer = observer;
            observer.OnNext(Boxed);
            return this;
        }

        public void Dispose()
        {
            var binding = Binding;
            Binding = null;
            _observer = null;
            binding?.Dispose();
        }
    }
}
