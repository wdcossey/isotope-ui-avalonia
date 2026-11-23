// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/V1Protocol/ReportedUnmountFailureException.cs, last changed upstream in 59541d3f6af1; transforms: namespace-isotope. Do not edit: change the tools/sync rules.
namespace Isotope.UI.Core.V1Protocol;

/// <summary>
/// Thrown by a control handler's unmount, inside an isolated teardown
/// (<see cref="Reconciler.IsolateUnmountCleanupFailures"/>), after it has already reported its
/// failures to the isolation sink itself (in the order they happened). The engine treats the
/// teardown as failed (recovery walk, no pooling) and reports nothing more.
/// </summary>
internal sealed class ReportedUnmountFailureException : Exception
{
    public ReportedUnmountFailureException()
        : base("A control's unmount callbacks failed; the failures were already reported.") { }
}
