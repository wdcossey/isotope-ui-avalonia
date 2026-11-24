using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Isotope.UI.Core;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>Upstream's Accelerator factory (#43): the shortcut a command and a menu item carry, as Avalonia key types.</summary>
public class AcceleratorTests
{
    [AvaloniaFact]
    public void Accelerator_builds_the_shortcut_a_command_gives_its_menu_item()
    {
        Assert.Equal(new KeyboardAcceleratorData(Key.S, KeyModifiers.Control | KeyModifiers.Shift), Accelerator(Key.S, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(KeyModifiers.None, Accelerator(Key.Escape).Modifiers);

        var item = MenuItem(new Command { Label = "New", Execute = () => { }, Accelerator = Accelerator(Key.N, KeyModifiers.Control) });
        Assert.Equal([new KeyboardAcceleratorData(Key.N, KeyModifiers.Control)], item.KeyboardAccelerators!);
    }
}
