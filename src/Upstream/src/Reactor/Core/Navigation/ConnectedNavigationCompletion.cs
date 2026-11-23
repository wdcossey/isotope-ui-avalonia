// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Navigation/ConnectedNavigationCompletion.cs, last changed upstream in 6f5e15275f19. Do not edit: change the tools/sync rules.
namespace Microsoft.UI.Reactor.Navigation;

/// <summary>Both independent motions must finish before navigation releases its pages.</summary>
internal struct ConnectedNavigationCompletion
{
    private bool _pageCompleted;
    private bool _connectedCompleted;

    internal bool CompletePage()
    {
        _pageCompleted = true;
        return _connectedCompleted;
    }

    internal bool CompleteConnected()
    {
        _connectedCompleted = true;
        return _pageCompleted;
    }
}
