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

    /// <summary>A keyboard shortcut, in Avalonia's key vocabulary.</summary>
    public record KeyboardAcceleratorData(Avalonia.Input.Key Key, Avalonia.Input.KeyModifiers Modifiers = Avalonia.Input.KeyModifiers.None);
}
