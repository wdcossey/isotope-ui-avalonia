using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Fixture.Lib;

// The library the app references: fixed in its own run, before the app's.
public static class Widgets
{
    public static Element Title(string text) => TextBlock(text)
        .HAlign(HorizontalAlignment.Center)
        .Margin(new Thickness(4));

    public static TextBoxElement Entry(string value) => TextBox() with { Value = value, OnChanged = _ => { } };
}
