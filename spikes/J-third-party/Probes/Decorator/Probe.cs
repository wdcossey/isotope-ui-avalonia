using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Probes;

[GenerateReactorDescriptor(typeof(RatingBar))]
[WrapDecorator(nameof(Create), OnUpdate = nameof(Patch))]
public partial record HandMadeRatingElement(int Value) : Element
{
    static RatingBar Create(HandMadeRatingElement e) => new() { Value = e.Value };

    static void Patch(HandMadeRatingElement oldEl, HandMadeRatingElement newEl, RatingBar c) => c.Value = newEl.Value;
}
