using Avalonia.Controls;
using Isotope.UI.Core;

namespace Isotope.UI.Hosting;

// The host as the owner of its content (upstream's IContentOwner): its reconciler records it on every RenderContext it
// renders, so the platform hooks (RenderContext.PlatformHooks.cs) find their host, its top level and its window.
public sealed partial class ReactorHostControl : IContentOwner
{
    // A host control has no ReactorWindow (as upstream's ReactorHostControl), only the Avalonia window it is in.
    ReactorWindow? IContentOwner.ReactorWindow => null;

    ContentOwnerStatus IContentOwner.TryGetWindowId(out WindowId id)
    {
        id = default;
        if (_disposed) return ContentOwnerStatus.Closed;
        if (!CheckAccess()) return ContentOwnerStatus.WrongThread;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return ContentOwnerStatus.Detached;
        id = new WindowId(unchecked((ulong)(topLevel.TryGetPlatformHandle()?.Handle ?? 0)));
        return ContentOwnerStatus.Available;
    }

    /// <summary>Tests only: colour values to report instead of the platform's (headless can't change them).</summary>
    internal IColorValuesSource? ColorValuesOverride { get; set; }

    /// <summary>Tests only: displays to report instead of the top level's screens (headless can't change them).</summary>
    internal IDisplaySource? DisplaysOverride { get; set; }
}
