using Isotope.UI.Core;

namespace Probes;

public static class Uses
{
    public static bool Same(Element a, Element b) => Element.ShallowEquals(a, b);
}
