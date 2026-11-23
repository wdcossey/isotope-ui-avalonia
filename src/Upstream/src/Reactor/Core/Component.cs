// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): src/Reactor/Core/Component.cs, last changed upstream in aa7fd62817e4; transforms: drop-Component-UseWindowSize-for-window, drop-Component-UseWindowSize, drop-Component-UseBreakpoint-for-window, drop-Component-UseBreakpoint, drop-Component-UseDpi, drop-Component-UseWindow, drop-Component-UseWindowState, drop-Component-UseIsActive, drop-Component-UseClosingGuard, drop-Component-PickFileAsync, drop-Component-PickMultipleFilesAsync, drop-Component-PickFolderAsync, drop-Component-PickMultipleFoldersAsync, drop-Component-PickSaveFileAsync, drop-Component-UseWindowPosition, drop-Component-UseDisplays, drop-Component-UseIsCovered, drop-Component-UseWindowAspectRatio, drop-Component-UseWindowDragMove, drop-Component-UseOpenWindow, drop-Component-UseTrayIcon, drop-Component-UseColorScheme, drop-Component-UseIsDarkTheme, drop-Component-UseReducedMotion, drop-Component-UseIntl, drop-Component-UseSystemBackButton, drop-Component-UseAnnounce, drop-Component-UseHighContrast, drop-Component-UseHighContrastScheme, namespace-isotope. Do not edit: change the tools/sync rules.
using Isotope.UI.Hooks;

namespace Isotope.UI.Core;

/// <summary>
/// Base class for stateful components (like React class components, but using hooks).
/// Components hold a RenderContext that tracks their hook state across re-renders.
/// </summary>
// <snippet:render-loop>
public abstract class Component
{
    // Settable so the reconciler can transfer a live RenderContext (hooks +
    // cleanups) onto a freshly-constructed instance when a Hot Reload edit
    // mints a new component Type token (spec 049 §7 subtree migration). Outside
    // that path the value is the per-instance context created here.
    internal RenderContext Context { get; set; } = new();

    /// <summary>
    /// Override to describe the UI. Use UseState, UseEffect, etc. from the context.
    /// Must call hooks in the same order every render.
    /// </summary>
    public abstract Element Render();

    /// <summary>
    /// Controls whether this propless component should re-render when its parent re-renders.
    /// Default: false — propless components only re-render from their own state changes or context changes.
    /// Override and return true to always re-render when the parent re-renders.
    /// </summary>
    protected internal virtual bool ShouldUpdate() => false;
// </snippet:render-loop>

    // ── Hook convenience methods (delegate to Context) ─────────────

    protected (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
        => Context.UseState(initialValue, threadSafe);

    protected (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
        => Context.UseReducer(initialValue, threadSafe);

    /// <summary>
    /// Redux-style reducer: takes a reducer function (state, action) => newState.
    /// Returns (currentState, dispatch) where dispatch sends an action through the reducer.
    /// </summary>
    protected (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe = false)
        => Context.UseReducer(reducer, initialValue, threadSafe);

    protected void UseEffect(Action effect, params object[] dependencies)
        => Context.UseEffect(effect, dependencies);

    /// <summary>Typed-arity <see cref="UseEffect(Action, object[])"/> — passes one dependency
    /// positionally, avoiding the <c>params object[]</c> allocation and value-type boxing.</summary>
    protected void UseEffect<T1>(Action effect, T1 d1)
        => Context.UseEffect(effect, d1);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2>(Action effect, T1 d1, T2 d2)
        => Context.UseEffect(effect, d1, d2);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2, T3>(Action effect, T1 d1, T2 d2, T3 d3)
        => Context.UseEffect(effect, d1, d2, d3);

    protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
        => Context.UseEffect(effectWithCleanup, dependencies);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
        => Context.UseEffect(effectWithCleanup, d1);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2>(Func<Action> effectWithCleanup, T1 d1, T2 d2)
        => Context.UseEffect(effectWithCleanup, d1, d2);

    /// <inheritdoc cref="UseEffect{T1}(Action, T1)"/>
    protected void UseEffect<T1, T2, T3>(Func<Action> effectWithCleanup, T1 d1, T2 d2, T3 d3)
        => Context.UseEffect(effectWithCleanup, d1, d2, d3);

    protected T UseMemo<T>(Func<T> factory, params object[] dependencies)
        => Context.UseMemo(factory, dependencies);

    /// <summary>Typed-arity <see cref="UseMemo{T}(Func{T}, object[])"/> — passes one dependency
    /// positionally, avoiding the <c>params object[]</c> allocation and value-type boxing.</summary>
    protected T UseMemo<T, T1>(Func<T> factory, T1 d1)
        => Context.UseMemo(factory, d1);

    /// <inheritdoc cref="UseMemo{T, T1}(Func{T}, T1)"/>
    protected T UseMemo<T, T1, T2>(Func<T> factory, T1 d1, T2 d2)
        => Context.UseMemo(factory, d1, d2);

    /// <inheritdoc cref="UseMemo{T, T1}(Func{T}, T1)"/>
    protected T UseMemo<T, T1, T2, T3>(Func<T> factory, T1 d1, T2 d2, T3 d3)
        => Context.UseMemo(factory, d1, d2, d3);

    protected Action UseCallback(Action callback, params object[] dependencies)
        => Context.UseCallback(callback, dependencies);

    /// <summary>Typed-arity <see cref="UseCallback(Action, object[])"/> — passes one dependency
    /// positionally, avoiding the <c>params object[]</c> allocation and value-type boxing.</summary>
    protected Action UseCallback<T1>(Action callback, T1 d1)
        => Context.UseCallback(callback, d1);

    /// <inheritdoc cref="UseCallback{T1}(Action, T1)"/>
    protected Action UseCallback<T1, T2>(Action callback, T1 d1, T2 d2)
        => Context.UseCallback(callback, d1, d2);

    /// <inheritdoc cref="UseCallback{T1}(Action, T1)"/>
    protected Action UseCallback<T1, T2, T3>(Action callback, T1 d1, T2 d2, T3 d3)
        => Context.UseCallback(callback, d1, d2, d3);

    protected Ref<T> UseRef<T>(T initialValue = default!)
        => Context.UseRef(initialValue);














































































































    protected T UseObservableTree<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservableTree(source);

    protected T UseObservable<T>(T source) where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservable(source);

    protected TSnapshot UseExternalStore<TSnapshot>(
        Func<Action, Action> subscribe,
        Func<TSnapshot> getSnapshot,
        IEqualityComparer<TSnapshot>? comparer = null)
        => Context.UseExternalStore(subscribe, getSnapshot, comparer);

    protected TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName)
        where T : global::System.ComponentModel.INotifyPropertyChanged
        => Context.UseObservableProperty(source, selector, propertyName);

    protected IReadOnlyList<T> UseCollection<T>(global::System.Collections.ObjectModel.ObservableCollection<T> collection)
        => Context.UseCollection(collection);



















    protected T UseContext<T>(Context<T> context)
        => Context.UseContext(context);

    protected Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
        => Context.UseNavigation(initial);

    protected Navigation.NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
        => Context.UseNavigation<TRoute>();

    protected void UseNavigationLifecycle(
        Action<Navigation.NavigatingToContext>? onNavigatingTo = null,
        Action<Navigation.NavigatedToContext>? onNavigatedTo = null,
        Action<Navigation.NavigatingFromContext>? onNavigatingFrom = null,
        Action<Navigation.NavigatedFromContext>? onNavigatedFrom = null)
        => Context.UseNavigationLifecycle(onNavigatingTo, onNavigatedTo, onNavigatingFrom, onNavigatedFrom);






    protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
        => Context.UsePersisted(key, initialValue);

    /// <summary>
    /// Persisted-state hook with explicit scope (spec 033 §2). Prefer this over
    /// the two-arg overload in new code so the call site documents whether the
    /// state is per-window or process-wide.
    /// </summary>
    protected (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue, PersistedScope scope)
        => Context.UsePersisted(key, initialValue, scope);

    protected Command UseCommand(Command command)
        => Context.UseCommand(command);

    protected Command<T> UseCommand<T>(Command<T> command)
        => Context.UseCommand(command);










    protected AsyncValue<T> UseResource<T>(
        Func<CancellationToken, Task<T>> fetcher,
        object[] deps,
        Hooks.ResourceOptions? options = null)
        => Context.UseResource(fetcher, deps, options);

    protected AsyncValue<T> UseResource<T>(
        Func<CancellationToken, Task<T>> fetcher,
        QueryCache cache,
        object[] deps,
        Hooks.ResourceOptions? options = null)
        => Context.UseResource(fetcher, cache, deps, options);

    protected InfiniteResource<TItem> UseInfiniteResource<TItem, TCursor>(
        Func<TCursor?, CancellationToken, Task<Page<TItem, TCursor>>> fetchPage,
        object[] deps,
        InfiniteResourceOptions? options = null)
        => Context.UseInfiniteResource(fetchPage, deps, options);

    /// <summary>Projects a data source into a paged resource using the ambient query cache.</summary>
    /// <remarks>
    /// The initial page size comes from <c>options.PageSize</c> (default 50) when
    /// <paramref name="options"/> is supplied, or <c>request.PageSize</c> otherwise,
    /// and is clamped to at least 1.
    /// This size stays fixed across renders and query restarts. To change it,
    /// have the parent remount the calling component with a different key,
    /// e.g. <c>.WithKey($"tasks:{pageSize}")</c>, or increment a parent-owned
    /// reset version used by <c>.WithKey($"tasks:{resetVersion}")</c>.
    /// </remarks>
    protected InfiniteResource<TItem> UseDataSource<TItem>(
        Data.IDataSource<TItem> source,
        Data.DataRequest request,
        InfiniteResourceOptions? options = null)
        => Data.DataSourceResourceExtensions.UseDataSource(Context, source, request, options);

    protected Hooks.Mutation<TInput, TResult> UseMutation<TInput, TResult>(
        Func<TInput, CancellationToken, Task<TResult>> mutator,
        Hooks.MutationOptions<TInput, TResult>? options = null)
        => Context.UseMutation(mutator, options);
}

/// <summary>
/// Interface for setting props without reflection.
/// </summary>
internal interface IPropsReceiver
{
    void SetProps(object props);

    /// <summary>Boxed current props, for diagnostics (no reflection).</summary>
    object? PropsBoxed { get; }

    /// <summary>The declared props type (<c>TProps</c>), for diagnostics.</summary>
    Type PropsType { get; }
}

/// <summary>
/// Interface for comparing props without reflection (avoids per-reconcile GetMethod/Invoke overhead).
/// </summary>
internal interface IPropsComparable
{
    bool CompareProps(object? oldProps, object? newProps);
}

/// <summary>
/// Base class for components that receive typed props (e.g., navigation parameters).
/// Props are set by the host before rendering.
/// </summary>
public abstract class Component<TProps> : Component, IPropsReceiver, IPropsComparable
{
    /// <summary>
    /// The typed props passed to this component by its parent or host.
    /// </summary>
    public TProps Props { get; internal set; } = default!;

    void IPropsReceiver.SetProps(object props) => Props = (TProps)props;

    object? IPropsReceiver.PropsBoxed => Props;

    Type IPropsReceiver.PropsType => typeof(TProps);

    bool IPropsComparable.CompareProps(object? oldProps, object? newProps)
        => ShouldUpdate((TProps?)oldProps, (TProps?)newProps);

    /// <summary>
    /// Controls whether this component should re-render when its parent re-renders with new props.
    /// Default: structural equality via record Equals — record props get auto-comparison for free;
    /// class props need an Equals override.
    /// Override for custom comparison logic.
    /// </summary>
    protected internal virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps)
        => !Equals(oldProps, newProps);
}
