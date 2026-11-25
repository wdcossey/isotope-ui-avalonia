using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Probes;

// RatingBar has no Sparkle: the descriptor would never write it.
[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record SparklyRatingElement(int Value) : Element
{
    public bool Sparkle { get; init; }

    internal Action<RatingBar>[] Setters { get; init; } = [];
}
