using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Isotope.UI;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;
using static Isotope.UI.Factories;

namespace Isotope.UI.Avalonia.Tests;

/// <summary>
/// Startup (#32), headless: upstream's one-line <c>ReactorApp.Run&lt;TRoot&gt;(title, width, height)</c> and its function
/// form over the default app builder, and the builder-taking overloads they share. A test seam
/// (<see cref="ReactorApp.StartForTest"/>) takes the builder and the main window's factory instead of starting the
/// platform, and the tests open that window headless: no real window.
/// </summary>
[Collection(ReactorAppGlobals.Name)]
public sealed class AppStartupTests : IDisposable
{
    private readonly DispatcherQueue? _previousDispatcher = ReactorApp.UIDispatcher;

    public void Dispose()
    {
        ReactorApp.StartForTest = null;
        ReactorApp.UIDispatcher = _previousDispatcher;
    }

    private sealed record Started(AppBuilder Builder, string[] Args, Func<Window> OpenMainWindow, ApartmentState Apartment);

    private const int ExitCode = 7;

    // Runs 'run' with the seam set, and returns what Run handed to the platform's start.
    private static Started Capture(Action run)
    {
        Started? started = null;
        ReactorApp.StartForTest = (builder, args, open) =>
        {
            Assert.Null(started); // started once
            started = new Started(builder, args, open, Thread.CurrentThread.GetApartmentState());
            return ExitCode;
        };
        run();
        return started ?? throw new InvalidOperationException("Run did not start the app");
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static List<string?> Texts(Control? root)
    {
        var found = new List<string?>();
        void Walk(Control? c)
        {
            switch (c)
            {
                case null: return;
                case TextBlock t: found.Add(t.Text); break;
                case Panel p: foreach (var child in p.Children) Walk(child); break;
                case Decorator d: Walk(d.Child); break;
                case ContentControl { Content: Control content }: Walk(content); break;
            }
        }
        Walk(root);
        return found;
    }

    private static Button OnlyButton(Control root)
    {
        Button? found = null;
        void Walk(Control? c)
        {
            switch (c)
            {
                case null: return;
                case Button b: found = b; break;
                case Panel p: foreach (var child in p.Children) Walk(child); break;
                case Decorator d: Walk(d.Child); break;
            }
        }
        Walk(root);
        return found ?? throw new InvalidOperationException("no button");
    }

    private static void AssertDefaultBuilder(AppBuilder builder)
    {
        Assert.Equal(typeof(ReactorApplication), builder.ApplicationType);
        // UsePlatformDetect: this OS's windowing backend (only Win32 names itself; X11 and macOS's leave the name
        // empty), drawn with Skia and shaped with HarfBuzz.
        Assert.NotNull(builder.WindowingSubsystemInitializer);
        if (OperatingSystem.IsWindows()) Assert.Equal("Win32", builder.WindowingSubsystemName);
        Assert.Equal("Skia", builder.RenderingSubsystemName);
        Assert.Equal("HarfBuzz", builder.TextShapingSubsystemName);
    }

    // The window a run opens hosts the counter: it renders, a click re-renders, closing it disposes the host.
    private static void AssertHostsTheCounter(Window window)
    {
        var host = Assert.IsType<ReactorHostControl>(window.Content);
        Pump();
        Assert.Equal(["Clicked 0 times"], Texts(host));
        TestHost.Click(OnlyButton(host));
        Pump();
        Assert.Equal(["Clicked 1 times"], Texts(host));
        window.Close();
        Assert.Null(host.Child);
        Assert.True(host.IsIdle);
    }

    [AvaloniaFact]
    public void Run_TRoot_starts_the_default_builder_with_a_window_that_hosts_TRoot()
    {
        var started = Capture(() =>
        {
            #region run-app
            ReactorApp.Run<CounterApp>("Counter", width: 400, height: 300);
            #endregion
        });
        AssertDefaultBuilder(started.Builder);
        Assert.Equal(Environment.GetCommandLineArgs().Skip(1), started.Args);
        // As upstream's: started on an STA thread on Windows, whatever the caller's apartment.
        if (OperatingSystem.IsWindows()) Assert.Equal(ApartmentState.STA, started.Apartment);

        var window = started.OpenMainWindow();
        Assert.Equal("Counter", window.Title);
        Assert.Equal(400, window.Width);
        Assert.Equal(300, window.Height);
        Assert.Equal(WindowState.Normal, window.WindowState);
        AssertHostsTheCounter(window);
    }

    [AvaloniaFact]
    public void Run_with_a_render_function_starts_the_default_builder_with_a_window_that_hosts_it()
    {
        int code = 0;
        var started = Capture(() => code = ReactorApp.Run("Counter", Examples.Counter, width: 400, height: 300));
        Assert.Equal(ExitCode, code);
        AssertDefaultBuilder(started.Builder);
        Assert.Equal(Environment.GetCommandLineArgs().Skip(1), started.Args);
        if (OperatingSystem.IsWindows()) Assert.Equal(ApartmentState.STA, started.Apartment);

        var window = started.OpenMainWindow();
        Assert.Equal("Counter", window.Title);
        Assert.Equal(400, window.Width);
        Assert.Equal(300, window.Height);
        AssertHostsTheCounter(window);
    }

    [AvaloniaFact]
    public void Run_TRoot_with_no_arguments_takes_the_defaults_and_returns_the_exit_code()
    {
        int code = 0;
        var started = Capture(() => code = ReactorApp.Run<CounterApp>());
        Assert.Equal(ExitCode, code);
        var window = started.OpenMainWindow();
        Assert.Equal(ReactorAppDesktop.DefaultTitle, window.Title);
        Assert.True(double.IsNaN(window.Width));  // Avalonia's default size
        Assert.True(double.IsNaN(window.Height));
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Null(window.Icon);
        AssertHostsTheCounter(window);
    }

    [AvaloniaFact]
    public void FullScreen_icon_and_configure_reach_the_window_and_its_host_before_the_root_mounts()
    {
        var icon = new WindowIcon(new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul));
        var renders = 0;
        var configured = new List<(ReactorHostControl Host, int Renders, Control? Child)>();
        RenderErrorHandler handler = e => TextBlock("handled");
        var started = Capture(() => ReactorApp.Run("Probe", _ => { renders++; return TextBlock("root"); },
            fullScreen: true, icon: icon, configure: host =>
            {
                configured.Add((host, renders, host.Child));
                host.RenderErrorHandler = handler;
            }));
        Assert.Empty(configured); // on the UI thread once the app runs, not inside Run's setup

        var window = started.OpenMainWindow();
        var host = Assert.IsType<ReactorHostControl>(window.Content);
        var (configuredHost, rendersThen, childThen) = Assert.Single(configured);
        Assert.Same(host, configuredHost);
        Assert.Equal(0, rendersThen);
        Assert.Null(childThen);
        Assert.Same(handler, host.RenderErrorHandler);
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.Same(icon, window.Icon);
        Pump();
        Assert.Equal(1, renders);
        Assert.Equal(["root"], Texts(host));
        window.Close();
    }

    [AvaloniaFact]
    public void The_builder_overloads_start_the_app_builder_with_its_arguments()
    {
        var builder = AppBuilder.Configure<TestApp>();
        int code = 0;
        var started = Capture(() => code = ReactorApp.Run<CounterApp>(builder, "Own builder", ["--flag"], width: 200));
        Assert.Equal(ExitCode, code);
        Assert.Same(builder, started.Builder);
        Assert.Equal(["--flag"], started.Args);
        var window = started.OpenMainWindow();
        Assert.Equal("Own builder", window.Title);
        Assert.Equal(200, window.Width);
        Assert.True(double.IsNaN(window.Height));
        AssertHostsTheCounter(window);

        started = Capture(() => ReactorApp.Run(builder, "Function", Examples.Counter));
        Assert.Same(builder, started.Builder);
        Assert.Empty(started.Args);
        window = started.OpenMainWindow();
        Assert.Equal("Function", window.Title);
        AssertHostsTheCounter(window);
    }

    [AvaloniaFact]
    public void The_default_builder_runs_ReactorApplication_on_the_detected_platform_with_the_Fluent_theme()
    {
        AssertDefaultBuilder(ReactorApp.CreateDefaultAppBuilder());
        var app = new ReactorApplication();
        app.Initialize();
        Assert.Single(app.Styles.OfType<FluentTheme>());
    }

    // An MTA caller, as Main is under top-level statements.
    private static T OnMtaThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception e) { error = e; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return result;
    }

    [Fact]
    public void RunOnSta_moves_an_MTA_caller_to_an_STA_thread_on_Windows_and_returns_its_result_or_exception()
    {
        var (callerThread, ranOn, apartment, result) = OnMtaThread(() =>
        {
            int thread = 0;
            var state = ApartmentState.Unknown;
            var value = ReactorAppDesktop.RunOnSta(() =>
            {
                thread = Environment.CurrentManagedThreadId;
                state = Thread.CurrentThread.GetApartmentState();
                return 42;
            });
            return (Environment.CurrentManagedThreadId, thread, state, value);
        });
        Assert.Equal(42, result);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(ApartmentState.STA, apartment);
            Assert.NotEqual(callerThread, ranOn);
        }
        else
        {
            Assert.Equal(callerThread, ranOn); // inline: apartments are a Windows notion
        }

        var ex = OnMtaThread(() => Record.Exception(() => ReactorAppDesktop.RunOnSta(() => throw new InvalidOperationException("startup failed"))));
        Assert.Equal("startup failed", Assert.IsType<InvalidOperationException>(ex).Message);
    }

    [Fact]
    public void RunOnSta_runs_an_STA_caller_inline()
    {
        if (!OperatingSystem.IsWindows()) return; // STA threads exist on Windows only
        int caller = 0, ranOn = -1;
        var thread = new Thread(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            ReactorAppDesktop.RunOnSta(() => { ranOn = Environment.CurrentManagedThreadId; return 0; });
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Equal(caller, ranOn);
    }
}

#region counter-app
public sealed class CounterApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(
            TextBlock($"Clicked {count} times"),
            Button("Click", () => setCount(count + 1)));
    }
}
#endregion
