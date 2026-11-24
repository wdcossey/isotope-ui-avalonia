using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Isotope.UI.Core;

// Types the linked upstream files name whose behaviour is tier 3 (windows, hosting, animation, theming,
// platform signals). Each carries only what the linked files need to compile, and says what it does
// instead; the Avalonia versions come later.
namespace Isotope.UI
{
    /// <summary>
    /// Tier 3 placeholder for upstream's top-level window wrapper. Nothing creates one yet, so every
    /// <c>OwningReactorWindow</c> is <c>null</c> and window-scoped persisted state falls back to the
    /// application scope, as upstream does for content without an owning window.
    /// </summary>
    public sealed class ReactorWindow
    {
        private ReactorWindow() { }

        internal WindowPersistedScope PersistedScope
            => throw new NotSupportedException("ReactorWindow is tier 3: not on Avalonia yet.");
    }

    /// <summary>Tier 3 placeholder for a top-level window's identity.</summary>
    public readonly record struct WindowId(ulong Value);

    /// <summary>Tier 3 placeholder for the OS memory-pressure signal: never raised on Avalonia yet.</summary>
    internal static class MemoryManager
    {
        internal static event EventHandler<object>? AppMemoryUsageIncreased { add { } remove { } }

        internal static AppMemoryUsageLevel AppMemoryUsageLevel => AppMemoryUsageLevel.Low;
    }

    internal enum AppMemoryUsageLevel { Low, Medium, High, OverLimit }
}

namespace Isotope.UI.Input
{
    /// <summary>
    /// A ref to a mounted control. Tier 3 placeholder: the Avalonia reconciler doesn't attach refs yet,
    /// so <see cref="Current"/> stays null.
    /// </summary>
    public sealed class ElementRef
    {
        internal Avalonia.Controls.Control? _current;

        /// <summary>The expected control type, when this is the inner of a typed ref.</summary>
        internal Type? ExpectedType { get; set; }

        /// <summary>The mounted control, or null.</summary>
        public Avalonia.Controls.Control? Current => _current;

        internal void SetCurrent(Avalonia.Controls.Control? value) => _current = value;
    }
}

namespace Isotope.UI.Core
{
    /// <summary>A reference to a theme resource by key. Theming is tier 3.</summary>
    public readonly record struct ThemeRef(string ResourceKey);
}

namespace Isotope.UI.Elements
{
    /// <summary>Per-control resource overrides. Tier 3: recorded on elements, not applied on Avalonia yet.</summary>
    public sealed record ResourceOverrides(
        IReadOnlyDictionary<string, object> Literals,
        IReadOnlyDictionary<string, ThemeRef> ThemeRefs);
}

namespace Isotope.UI.Animation
{
    /// <summary>Tier 3 placeholder for an element's enter/exit transition. Nothing can create one yet.</summary>
    public sealed record ElementTransition
    {
        private ElementTransition() { }
    }

    /// <summary>Tier 3 placeholder for an animation curve. Nothing can create one yet.</summary>
    public sealed class Curve
    {
        private Curve() { }
    }

    /// <summary>Tier 3: composition animations are not wired on Avalonia yet.</summary>
    internal static class AnimationHelper
    {
        internal static CompositionVisual GetElementVisual(UIElement element)
            => throw new NotSupportedException("Composition animations are tier 3: not on Avalonia yet.");

        internal static CompositionAnimation CreateVector3ImplicitAnimation(Compositor compositor, string target, Curve curve)
            => throw new NotSupportedException("Composition animations are tier 3: not on Avalonia yet.");
    }

    /// <summary>The features that share a visual's implicit animation keys, as upstream names them.</summary>
    internal enum ImplicitFeature : byte { Move = 0, Transition = 1, Animate = 2, Layout = 3 }

    /// <summary>
    /// Tier 3: who claims each implicit animation key on a control (upstream's spec 066). Composition
    /// animations are not wired on Avalonia yet, so nothing reaches a claim.
    /// </summary>
    internal static class ImplicitAnimationSlots
    {
        internal static void Claim(UIElement element, string key, ImplicitFeature feature, int slot, object value)
            => throw new NotSupportedException("Composition animations are tier 3: not on Avalonia yet.");
    }
}

namespace Isotope.UI.Core.Internal
{
    /// <summary>
    /// Maps an ambient animation kind to a curve. Animations are tier 3, so every kind maps to no curve,
    /// which the engine treats as "no transition this render".
    /// </summary>
    internal static class AnimationKindMap
    {
        public static Isotope.UI.Animation.Curve? ToCurve(AnimationKind kind) => null;

        public static bool IsActive(AnimationKind kind) => false;
    }
}
