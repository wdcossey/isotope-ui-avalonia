using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Isotope.UI.Core;
using Isotope.UI.Hosting;
using Xunit;

namespace Vendor.Controls.Tests;

/// <summary>
/// A root component mounted as an app mounts one: a <see cref="ReactorHostControl"/> in a shown (headless) window, so
/// templates are applied and controls attach to a visual tree. <see cref="Renders"/> counts the root's renders.
/// </summary>
internal sealed class Mounted : IDisposable
{
    public Window Window { get; }
    public ReactorHostControl Host { get; }
    public int Renders { get; private set; }

    public Mounted(Func<RenderContext, Element> render, ThemeVariant? theme = null)
    {
        Window = new Window { Width = 600, Height = 400 };
        if (theme is not null) Window.RequestedThemeVariant = theme;
        Host = new ReactorHostControl(ctx =>
        {
            Renders++;
            return render(ctx);
        });
        Window.Content = Host;
        Window.Show();
        Pump();
    }

    /// <summary>Runs posted jobs (the host's renders, Loaded) and a render tick (Avalonia 12 lays out on it).</summary>
    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The controls of type <typeparamref name="T"/> in the window's visual tree, templates included.</summary>
    public List<T> All<T>() where T : Control => [.. Host.GetVisualDescendants().OfType<T>()];

    public T One<T>() where T : Control => Assert.Single(All<T>());

    public void Dispose()
    {
        Host.Dispose();
        Window.Close();
        Pump();
    }
}
