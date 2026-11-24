using Xunit;

namespace Isotope.UI.Migration.Tests;

/// <summary>Reactor's Theme tokens (#41): Isotope has them, so they migrate as a namespace rename and then compile.</summary>
public class ThemeTokenTests
{
    [Fact]
    public void Theme_tokens_migrate_and_compile()
    {
        const string reactor = """
            using Microsoft.UI.Reactor;
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;
            using static Microsoft.UI.Reactor.Core.Theme;

            static class C
            {
                public static Element Card() => Border(
                        VStack(
                            TextBlock("Title").Foreground(Theme.PrimaryText),
                            TextBlock("Body").Foreground(SecondaryText)))
                    .Background(Theme.CardBackground)
                    .WithBorder(Theme.Ref("CardStrokeColorDefaultBrush"));
            }
            """;
        Assert.Equal([
            "ISOMIG001: 'Microsoft.UI.Reactor' is 'Isotope.UI' in Isotope",
            "ISOMIG001: 'Microsoft.UI.Reactor.Core' is 'Isotope.UI.Core' in Isotope",
            "ISOMIG001: 'Microsoft.UI.Reactor.Factories' is 'Isotope.UI.Factories' in Isotope",
            "ISOMIG001: 'Microsoft.UI.Reactor.Core.Theme' is 'Isotope.UI.Core.Theme' in Isotope",
        ], Migrate.Analyze(reactor).Describe());

        var fixedText = Migrate.Fix(reactor);
        Assert.Contains("using static Isotope.UI.Core.Theme;", fixedText);
        Assert.Empty(Migrate.Errors(fixedText));
        Assert.Empty(Migrate.Analyze(fixedText));
    }
}
