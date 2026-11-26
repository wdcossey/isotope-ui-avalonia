using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Wrappers;
using Vendor.Controls;

namespace Probes;

// #77: a library's record may decide its own skip, as a built-in record's arm does: HasCallbacks and ShallowEqualsCore are
// protected (before #77, HasCallbacks was internal: CS0115). The generator then writes neither. The region is a C# block of
// docs/guide/writing-controls.md (DocSnippetTests).
#region controls-own-skip
[GenerateReactorDescriptor(typeof(RatingBar))]
public partial record OwnRatingElement(Optional<int> Value = default, Action<int>? OnValueChanged = null) : Element
{
    protected override bool HasCallbacks => OnValueChanged is not null;

    protected override bool ShallowEqualsCore(Element other)
        => other is OwnRatingElement o && Value.Equals(o.Value) && ReferenceEquals(Setters, o.Setters)
            && (OnValueChanged is null) == (o.OnValueChanged is null);

    internal Action<RatingBar>[] Setters { get; init; } = [];
}
#endregion
