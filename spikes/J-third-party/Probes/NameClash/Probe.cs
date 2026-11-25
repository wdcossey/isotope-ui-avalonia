using Isotope.UI.Core;
using Vendor.Controls;
using static Isotope.UI.Factories;
using static Vendor.Controls.Isotope.VendorFactories;
using static Probes.OtherFactories;

namespace Probes;

/// <summary>A library that also calls its bordered box a Card, with Isotope's parameters.</summary>
public static class OtherFactories
{
    public static Element Card(Element child) => child;
}

public static class Uses
{
    // CS0121: Isotope's Factories.Card(Element) and OtherFactories.Card(Element) are both candidates.
    public static Element Clash() => Card(TextBlock("x"));

    // CS0119: with the factory imported, RatingBar names the method group, not the control type.
    public static object Property() => RatingBar.ValueProperty;
}
