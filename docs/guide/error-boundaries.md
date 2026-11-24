# Error boundaries

An error boundary keeps a failing part of the UI from taking the rest with it. `ErrorBoundary(child, fallback)` renders
`child`. If anything in the child's subtree throws while it renders, or in an effect that render runs, the boundary shows
`fallback(exception)` in the child's place. The rest of the tree keeps its controls and its state. They behave as
Microsoft.UI.Reactor's do (`ErrorBoundaryElement`, with the same factories).

The code block on this page is a region of the test project, run by `ErrorBoundaryTests`.

<!-- snippet: error-boundary -->
```csharp
// A part of the window that can fail: it throws while there's no forecast.
public static Element WeatherPanel(RenderContext ctx) => TextBlock($"Today: {Forecast()}");

public static Element Dashboard(RenderContext ctx)
{
    var (attempt, setAttempt) = ctx.UseState(0);
    return VStack(
        ErrorBoundary(
            Component(WeatherPanel),
            error => VStack(
                TextBlock($"The weather panel failed: {error.Message}"),
                // Any re-render of the boundary retries its child: here, the click's.
                Button("Retry", () => setAttempt(attempt + 1)))),
        // Outside the boundary: it keeps its state whatever the weather panel does.
        Component(Examples.Counter));
}
```

## How they behave

- **They catch errors from rendering:** a component's render, the effects it runs, and the mount or update of the
  controls under it, at any depth in the subtree.
  - Like React's, they don't catch errors from event handlers. A click handler that throws isn't rendering; a state change it
    made before throwing still renders as usual.
- **The nearest one catches.** A boundary inside another catches first. If its fallback throws too, the next boundary out
  catches that.
- **Every re-render retries.** When the boundary's parent renders it again (as the retry button above makes
  `Dashboard` do), the boundary tries the child again. A child that now renders is mounted afresh in the fallback's
  place. A child that throws again leaves the fallback where it is, updated with the new exception, so the fallback's
  own state (a "show details" toggle, focus on its retry button) survives the retry.
  - A state change inside the fallback re-renders the components above it too, so it also retries the child.
  - A boundary element the parent reuses (hoisted, or kept by `UseMemo`) isn't rendered again, like any reused
    element: it doesn't retry, and a state change inside its fallback updates only the fallback.
- **A new key starts the child afresh.** `ErrorBoundary(child, fallback).WithKey(...)` with a new key remounts the
  child, so its state starts again.
- **What failed is torn down.** The part of the child that mounted before the error is unmounted and its effects'
  cleanups run, as when a child is removed. The same happens when a retry fails: the child it replaced is unmounted.
  An effect cleanup that throws during this teardown is logged, and the other cleanups still run.
- **Errors outside every boundary go to the host's `RenderErrorHandler`** (see
  [Running an app](writing-components.md#running-an-app)). A child component that fails there is replaced in its own
  place, with what the handler returns or the exception, as Reactor does. The rest of the tree keeps its state. Other
  errors, such as the root's render or a control that fails to mount, replace the host's whole content. When such a
  pass fails, what it mounted is torn down and its effects' cleanups run, as a boundary does for its child. An error a
  boundary caught never reaches that handler.

## What differs from Reactor

- **A retry doesn't reuse the fallback.** Reactor diffs the retried child against the fallback it replaced, so a child and
  a fallback of the same element type share controls and hook state, and a fallback shown again after a failed retry is
  remounted, losing its state. Here the child is mounted afresh and the fallback is updated in place.
- **An unchanged boundary element doesn't retry.** When a pass reaches a reused boundary element only on its way to a
  state change inside the fallback, only the fallback is updated; Reactor retries the child there too.
- **An effect cleanup that throws while a failed retry unmounts the old child is logged,** and the other cleanups still
  run. In Reactor it escapes the boundary.
- **Fatal errors pass through.** A boundary doesn't catch `OutOfMemoryException` or `StackOverflowException`, as the
  host doesn't; Reactor's boundaries catch every exception.
- **A failed component's built-in placeholder is plain text.** It has Reactor's wording (the exception's type, message
  and stack) in a scroll viewer, but the text isn't selectable or monospaced yet.
- **`Propagate()` has no app-level hook.** Reactor first offers the exception to `ReactorApplication.OnUnhandledException`,
  which can mark it handled. Here it is rethrown out of the dispatcher, as for the host's own errors (#16).
