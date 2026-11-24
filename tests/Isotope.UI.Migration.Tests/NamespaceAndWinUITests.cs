using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>ISOMIG001 to ISOMIG005: Reactor namespaces, and WinUI namespaces and types.</summary>
public class NamespaceAndWinUITests
{
    [Fact]
    public void Reactor_namespaces_become_Isotope_and_the_program_compiles()
    {
        const string reactor = """
            using Microsoft.UI.Reactor;
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            ReactorApp.Run<App>("Hello", width: 480, height: 240);

            internal sealed class App : Component
            {
                public override Element Render() => global::Microsoft.UI.Reactor.Factories.TextBlock("Hello, world!");
            }
            """;
        Assert.Equal([
            "ISOMIG001: 'Microsoft.UI.Reactor' is 'Isotope.UI' in Isotope",
            "ISOMIG001: 'Microsoft.UI.Reactor.Core' is 'Isotope.UI.Core' in Isotope",
            "ISOMIG001: 'Microsoft.UI.Reactor.Factories' is 'Isotope.UI.Factories' in Isotope",
            "ISOMIG001: 'Microsoft.UI.Reactor.Factories' is 'Isotope.UI.Factories' in Isotope",
        ], Migrate.Analyze(reactor).Describe());

        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("using Isotope.UI;", fixedText);
        Assert.Contains("using static Isotope.UI.Factories;", fixedText);
        Assert.Contains("global::Isotope.UI.Factories.TextBlock", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }

    [Fact]
    public void A_Reactor_namespace_Isotope_lacks_is_reported_without_a_fix()
    {
        const string reactor = """
            using Microsoft.UI.Reactor.Charting;
            using static Microsoft.UI.Reactor.Charting.D3Charts;
            class C { }
            """;
        var diagnostics = Migrate.Analyze(reactor);
        Assert.Equal([
            "ISOMIG002: 'Microsoft.UI.Reactor.Charting' has no Isotope counterpart ('Isotope.UI.Charting' doesn't exist)",
            "ISOMIG002: 'Microsoft.UI.Reactor.Charting' has no Isotope counterpart ('Isotope.UI.Charting' doesn't exist)",
        ], diagnostics.Describe());
        Assert.Equal(reactor, Migrate.Fix(reactor));
    }

    [Fact]
    public void A_WinUI_using_becomes_the_Avalonia_namespaces_the_file_uses()
    {
        const string reactor = """
            using Microsoft.UI.Reactor;
            using Microsoft.UI.Reactor.Core;
            using Microsoft.UI.Xaml;
            using Microsoft.UI.Xaml.Input;
            using static Microsoft.UI.Reactor.Factories;

            static class C
            {
                public static Element Render() => TextBlock("x")
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Top)
                    .Margin(new Thickness(4));
            }
            """;
        var winui = Migrate.Analyze(reactor).Where(d => d.Id == "ISOMIG003").Describe();
        Assert.Equal([
            "ISOMIG003: 'Microsoft.UI.Xaml' is a WinUI namespace: replace it with Avalonia's Avalonia, Avalonia.Layout",
            "ISOMIG003: 'Microsoft.UI.Xaml.Input' is a WinUI namespace: remove it",
        ], winui);
        var fixedText = Migrate.Fix(reactor);
        Assert.DoesNotContain("Microsoft.UI.Xaml", fixedText);
        Assert.Contains("using Avalonia;", fixedText);
        Assert.Contains("using Avalonia.Layout;", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void A_global_WinUI_using_becomes_global_Avalonia_usings_for_every_file()
    {
        const string usings = """
            global using Microsoft.UI.Reactor;
            global using Microsoft.UI.Reactor.Core;
            global using Microsoft.UI.Xaml;
            global using static Microsoft.UI.Reactor.Factories;
            """;
        const string page = """
            static class Page
            {
                public static Element Render() => TextBlock("x").HorizontalAlignment(HorizontalAlignment.Right);
            }
            """;
        var fixedTexts = Migrate.Fix([usings, page]);
        Assert.Contains("global using Avalonia.Layout;", fixedTexts[0]);
        Assert.DoesNotContain("Microsoft.UI", fixedTexts[0]);
        Assert.Equal(page, fixedTexts[1]);
        Assert.Empty(Migrate.Errors(fixedTexts));
    }

    [Fact]
    public void Qualified_and_renamed_WinUI_types_become_Avalonia_types()
    {
        const string reactor = """
            using Microsoft.UI.Text;
            using Windows.System;
            using XamlAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
            using WinMedia = Microsoft.UI.Xaml.Media;

            static class C
            {
                static object[] Values() =>
                [
                    new Microsoft.UI.Xaml.Thickness(1),
                    Windows.System.VirtualKey.Number1,
                    global::Windows.UI.Text.TextDecorations.Strikethrough,
                    FontWeights.SemiBold,
                    VirtualKey.Control,
                    VirtualKey.Enter,
                    Microsoft.UI.ColorHelper.FromArgb(255, 1, 2, 3),
                    XamlAlignment.Center,
                ];
                static WinMedia.FontFamily Family() => new("Consolas");
            }
            """;
        var fixedText = Migrate.Fix(reactor);
        foreach (var expected in new[]
                 {
                     "new Avalonia.Thickness(1)", "Avalonia.Input.Key.D1", "global::Avalonia.Media.TextDecorations.Strikethrough",
                     "FontWeight.SemiBold", "Key.LeftCtrl", "Key.Enter", "Avalonia.Media.Color.FromArgb(255, 1, 2, 3)",
                     "using XamlAlignment = Avalonia.Layout.HorizontalAlignment;", "using WinMedia = Avalonia.Media;",
                     "using Avalonia.Media;", "using Avalonia.Input;",
                 })
            Assert.Contains(expected, fixedText);
        Assert.DoesNotContain("Microsoft.UI", fixedText);
        Assert.DoesNotContain("Windows.", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void WinUI_types_and_members_with_no_Avalonia_counterpart_are_reported_without_a_fix()
    {
        const string reactor = """
            using Microsoft.UI.Xaml;
            using Windows.System;

            static class C
            {
                static object[] Values() =>
                [
                    Visibility.Collapsed,
                    Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                    VirtualKey.GamepadA,
                    Windows.System.VirtualKey.NoSuchKey,
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
                    new Microsoft.UI.Xaml.Input.KeyboardAccelerator(),
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG003: 'Microsoft.UI.Xaml' is a WinUI namespace: remove it",
            "ISOMIG003: 'Windows.System' is a WinUI namespace: replace it with Avalonia's Avalonia.Input",
            "ISOMIG005: 'Microsoft.UI.Xaml.Visibility' has no Avalonia counterpart: Avalonia has IsVisible (bool): Visible is true, Collapsed is false.",
            "ISOMIG005: 'Microsoft.UI.Xaml.Controls.ContentDialogButton' has no Avalonia counterpart: Avalonia's core has no ContentDialog: use a Window shown as a dialog, or an overlay.",
            "ISOMIG005: 'VirtualKey.GamepadA' has no Avalonia counterpart",
            "ISOMIG005: 'Windows.System.VirtualKey.NoSuchKey' has no Avalonia counterpart ('NoSuchKey' isn't a member of Avalonia.Input.Key)",
            "ISOMIG016: 'Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error' is a WinUI name the migration's table doesn't list: find its Avalonia counterpart by hand",
            "ISOMIG005: 'Microsoft.UI.Xaml.Input.KeyboardAccelerator' has no Avalonia counterpart: Avalonia uses a KeyGesture (new KeyGesture(Key.S, KeyModifiers.Control)) as a button's or menu item's HotKey; its key and modifiers are set in the constructor.",
        ], Migrate.Analyze(reactor).Describe());
    }

    [Fact]
    public void A_project_namespace_under_Microsoft_UI_Reactor_is_left_alone_but_Reactor_s_own_is_migrated()
    {
        const string own = "namespace Microsoft.UI.Reactor.Demo { public class Thing { } }";
        const string page = """
            using Microsoft.UI.Reactor;
            using Microsoft.UI.Reactor.Demo;
            class C { Thing? T; }
            """;
        Assert.Equal(["ISOMIG001: 'Microsoft.UI.Reactor' is 'Isotope.UI' in Isotope"],
            Migrate.Analyze(Migrate.Project(own, page)).Describe());
    }

    [Fact]
    public void Names_that_bind_are_left_alone()
    {
        // A user's own Visibility type, an Avalonia type already in scope, a field named Windows (with a member that
        // doesn't exist: still the field's), and a project's own shim under a WinUI namespace.
        const string code = """
            using Avalonia.Layout;
            enum Visibility { Shown }
            namespace Microsoft.UI.Xaml { public enum Mode { On } }
            class C
            {
                public int[] Windows = [];
                object[] Values() => [Visibility.Shown, HorizontalAlignment.Center, this.Windows.Length, Microsoft.UI.Xaml.Mode.On];
                int Count() => Windows.Length;
                int Missing() => Windows.NoSuchMember;
            }
            """;
        Assert.Empty(Migrate.Analyze(code));
    }
}
