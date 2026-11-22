// Spike A: hand-written stand-ins the generator can't produce (nested types).
#pragma warning disable
namespace Microsoft.UI.Reactor.Hooks
{
    public sealed partial class AnnounceHandle { } // stub: Hooks/UseAnnounce.cs is not linked (see FINDINGS)
}
namespace Microsoft.UI.Reactor.Core
{
    public partial class Reconciler { internal sealed partial class ComponentNode { } }
}
