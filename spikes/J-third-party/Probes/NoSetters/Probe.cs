using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Probes;

// Every member maps to RatingBar, but the record has no Setters: the generated descriptor reads e.Setters anyway.
[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record PlainRatingElement(int Value) : Element;
