using Isotope.UI.Core;

namespace Probes;

// #77: the engine's skip test stays internal (CS0117), and a record's part of it, ShallowEqualsCore, is protected: only
// the record itself calls it (CS0122 from anywhere else).
public static class Uses
{
    public static bool Same(Element a, Element b) => Element.ShallowEquals(a, b);
}

public static class UsesCore
{
    public static bool Same(Element a, Element b) => a.ShallowEqualsCore(b);
}
