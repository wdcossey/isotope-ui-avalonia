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
- **Every re-render retries.** When the boundary renders again (its parent renders, as the retry button above makes
  it do), it tries the child again. A child that now renders is mounted afresh in the fallback's place. A child that
  throws again leaves the fallback where it is, updated with the new exception, so the fallback's own state (a
  "show details" toggle, focus on its retry button) survives the retry.
- **A new key starts the child afresh.** `ErrorBoundary(child, fallback).WithKey(...)` with a new key remounts the
  child, so its state starts again.
- **What failed is torn down.** The part of the child that mounted before the error is unmounted and its effects'
  cleanups run, as when a child is removed. The same happens when a retry fails: the child it replaced is unmounted.
  An effect cleanup that throws during this teardown is logged, and the other cleanups still run.
- **Errors outside every boundary go to the host**, as before: the `ReactorHostControl` shows the exception, or what its
  `RenderErrorHandler` returns, in place of its whole content (see
  [Running an app](writing-components.md#running-an-app)). An error a boundary caught never reaches that handler.

## What differs from Reactor

- **A failing child component outside every boundary replaces the host's content.** Reactor instead shows a fallback in
  that component's place only, and reports it to the handler with `IsHostLevel` false. Here it is reported as a
  `Reconcile` error of the host, as it was before boundaries arrived (#16).
- **A retry doesn't reuse the fallback.** Reactor diffs the retried child against the fallback it replaced, so a child and
  a fallback of the same element type share controls and hook state, and a fallback shown again after a failed retry is
  remounted, losing its state. Here the child is mounted afresh and the fallback is updated in place.
- **An effect cleanup that throws while a failed retry unmounts the old child is logged,** and the other cleanups still
  run. In Reactor it escapes the boundary.
- **Fatal errors pass through.** A boundary doesn't catch `OutOfMemoryException` or `StackOverflowException`, as the
  host doesn't; Reactor's boundaries catch every exception.
