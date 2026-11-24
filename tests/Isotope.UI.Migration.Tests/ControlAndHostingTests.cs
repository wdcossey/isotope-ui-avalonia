using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>ISOMIG011 to ISOMIG015: controls and members Isotope lacks, and hosting.</summary>
public class ControlAndHostingTests
{
    const string Usings = """
        using System;
        using Isotope.UI;
        using Isotope.UI.Core;
        using static Isotope.UI.Factories;

        """;

    [Fact]
    public void WinUI_only_and_unported_controls_are_reported_with_an_alternative()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render() =>
                [
                    TitleBar("Todos"),
                    ProgressRing(),
                    ScrollView(TextBlock("x")),
                ];
            }
            """;
        Assert.Equal([
            "ISOMIG011: 'TitleBar' is a WinUI control with no Isotope counterpart: Avalonia's core has no TitleBar element: the window title comes from ReactorApp.Run's title.",
            "ISOMIG011: 'ProgressRing' is a WinUI control with no Isotope counterpart: Avalonia's core has no ProgressRing: use ProgressIndeterminate() (an indeterminate ProgressBar).",
            "ISOMIG012: 'ScrollView' has no Isotope factory: Not in Isotope yet: use ScrollViewer(...) (Avalonia ScrollViewer).",
        ], Migrate.Analyze(reactor).Describe());
    }

    [Fact]
    public void A_user_method_named_like_a_control_is_left_alone()
    {
        const string code = Usings + """
            static class C
            {
                static Element TitleBar(string title) => TextBlock(title);
                public static Element Render() => TitleBar("Todos");
            }
            """;
        Assert.Empty(Migrate.Analyze(code));
    }

    [Fact]
    public void Members_and_modifiers_Isotope_lacks_are_reported()
    {
        const string reactor = Usings + """
            static class C
            {
                public static Element[] Render() =>
                [
                    TextBox() with { Header = "Name" },
                    TextBlock("x").Backdrop(1),
                ];
                public static object? Window => ReactorApp.PrimaryWindow;
            }
            """;
        Assert.Equal([
            "ISOMIG013: 'TextBoxElement' has no member 'Header' in Isotope",
            "ISOMIG013: 'TextBlockElement' has no member 'Backdrop' in Isotope",
            "ISOMIG013: 'ReactorApp' has no member 'PrimaryWindow' in Isotope",
        ], Migrate.Analyze(reactor).Describe());
    }

    [Fact]
    public void A_member_missing_on_a_type_parameter_names_its_constraint()
    {
        const string reactor = Usings + """
            static class Modifiers
            {
                public static T Glass<T>(this T el) where T : Element => el.Backdrop(1);
            }
            """;
        Assert.Equal(["ISOMIG013: 'Element' has no member 'Backdrop' in Isotope"], Migrate.Analyze(reactor).Describe());
    }

    [Fact]
    public void A_window_icon_path_becomes_an_icon_factory()
    {
        const string reactor = Usings + """
            ReactorApp.Run<App>("Chat", width: 1200, height: 800, icon: WindowIcon.FromPath("Assets/chat.ico"));

            sealed class App : Component
            {
                public override Element Render() => TextBlock("Hello");
            }
            """;
        Assert.Equal([
            "ISOMIG014: Isotope's window icon is a factory, called when the window opens: 'WindowIcon.FromPath(\"Assets/chat.ico\")' is '() => new Avalonia.Controls.WindowIcon(\"Assets/chat.ico\")'",
        ], Migrate.Analyze(reactor).Describe());
        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("icon: () => new Avalonia.Controls.WindowIcon(\"Assets/chat.ico\"))", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
    }

    [Fact]
    public void WinUI_startup_and_multi_window_startup_are_reported()
    {
        const string reactor = Usings + """
            ReactorApp.Run(_ =>
            {
                ReactorApp.OpenWindow(new object());
            });

            class App : Microsoft.UI.Xaml.Application
            {
                protected override void OnLaunched(object args) { }
            }

            static class Other
            {
                public static void Start() => Application.Start(_ => { });
            }
            """;
        var startup = Migrate.Analyze(reactor).Where(d => d.Id == "ISOMIG015").Describe();
        Assert.Equal([
            "ISOMIG015: ReactorApp.Run(startup) (several windows): an Isotope app starts with ReactorApp.Run<TRoot>(title, width, height) (Isotope.UI.Avalonia.Desktop); Isotope has one window per app for now",
            "ISOMIG015: ReactorApp.OpenWindow (another window): an Isotope app starts with ReactorApp.Run<TRoot>(title, width, height) (Isotope.UI.Avalonia.Desktop); Isotope has one window per app for now",
            "ISOMIG015: WinUI Application 'App' with OnLaunched: an Isotope app starts with ReactorApp.Run<TRoot>(title, width, height) (Isotope.UI.Avalonia.Desktop)",
            "ISOMIG015: Application.Start (WinUI startup): an Isotope app starts with ReactorApp.Run<TRoot>(title, width, height) (Isotope.UI.Avalonia.Desktop)",
        ], startup);
    }

    [Fact]
    public void One_line_startup_needs_no_change()
    {
        const string code = Usings + """
            ReactorApp.Run<App>("Hello", width: 480, height: 240);

            sealed class App : Component
            {
                public override Element Render() => TextBlock("Hello");
            }
            """;
        Assert.Empty(Migrate.Errors(code));
        Assert.Empty(Migrate.Analyze(code));
    }
}
