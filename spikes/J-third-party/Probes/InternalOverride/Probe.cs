using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Probes;

[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record OwnRatingElement(Optional<int> Value = default, Action<int>? OnValueChanged = null) : Element
{
    // A built-in record declares this, so a skipped update refreshes its callbacks. Internal: CS0115.
    internal override bool HasCallbacks => OnValueChanged is not null;

    internal Action<RatingBar>[] Setters { get; init; } = [];
}
