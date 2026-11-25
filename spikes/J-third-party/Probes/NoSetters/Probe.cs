using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Probes;

// Every member maps to RatingBar, and the record has no Setters: the descriptor doesn't read them (#78).
[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record PlainRatingElement(int Value) : Element;
