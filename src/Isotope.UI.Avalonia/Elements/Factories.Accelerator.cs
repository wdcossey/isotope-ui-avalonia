using Avalonia.Input;
using Isotope.UI.Core;

namespace Isotope.UI;

// The keyboard accelerator factory (#43), as part of our own version (tier 3) of upstream's src/Reactor/Elements/Dsl.cs:
// upstream's name, for the KeyboardAcceleratorData record a Command's Accelerator and a menu item's KeyboardAccelerators
// hold, with Avalonia's key types (Key and KeyModifiers for WinUI's VirtualKey and VirtualKeyModifiers). Avalonia's
// own name for the value is KeyGesture, which the library writes from it (CommandBindings.Gesture).

public static partial class Factories
{
    /// <summary>
    /// A keyboard shortcut: <c>new Command { Label = "Save", Execute = save, Accelerator = Accelerator(Key.S, KeyModifiers.Control) }</c>.
    /// It is Avalonia's <see cref="KeyGesture"/> on the button or menu item it reaches.
    /// </summary>
    public static KeyboardAcceleratorData Accelerator(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);
}
