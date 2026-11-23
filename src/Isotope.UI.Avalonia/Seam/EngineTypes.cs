namespace Isotope.UI.Core
{
    /// <summary>Which hook produced a hook cell (upstream: Core/Diagnostics/ReactorDiagnostics.Components.cs).</summary>
    internal enum HookKind
    {
        Unknown,
        State,
        Reducer,
        Ref,
        Memo,
        Effect,
        Context,
        Persisted,
        NavigationLifecycle,
    }

    /// <summary>An icon shown with a command. The Avalonia icon records come with the element vocabulary.</summary>
    public abstract record IconData;

    /// <summary>A keyboard shortcut, in Avalonia's key vocabulary.</summary>
    public record KeyboardAcceleratorData(Avalonia.Input.Key Key, Avalonia.Input.KeyModifiers Modifiers = Avalonia.Input.KeyModifiers.None);
}
