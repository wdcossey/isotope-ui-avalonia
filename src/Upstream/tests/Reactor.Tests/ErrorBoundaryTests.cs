// Vendored by tools/sync from microsoft/microsoft-ui-reactor (MIT): tests/Reactor.Tests/ErrorBoundaryTests.cs, last changed upstream in 58c2f12a13d4; transforms: drop-test-ErrorBoundary-RegisterType, test-textblock-text, winui-usings, namespace-isotope. Do not edit: change the tools/sync rules.
using Isotope.UI.Core;

using static Isotope.UI.Factories;
using Xunit;

namespace Isotope.UI.Tests;

/// <summary>
/// Tests for ErrorBoundary element — creation, DSL, and reconciler dispatch.
/// Reconciler-level tests use RegisterType to avoid requiring XAML Application context.
/// </summary>
public class ErrorBoundaryTests
{
    private static readonly Action NoOp = () => { };

    // Custom element whose mount handler throws
    private record ThrowingElement(string ErrorMessage) : Element;

    // ════════════════════════════════════════════════════════════════
    //  Element creation and DSL
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ErrorBoundaryElement_Has_Child_And_Fallback()
    {
        var child = new TextBlockElement("Hello");
        Func<Exception, Element> fallback = ex => new TextBlockElement("Error");
        var eb = new ErrorBoundaryElement(child, fallback);

        Assert.Same(child, eb.Child);
        Assert.Same(fallback, eb.Fallback);
    }

    [Fact]
    public void DSL_ErrorBoundary_With_Func_Fallback()
    {
        var eb = ErrorBoundary(
            TextBlock("child"),
            ex => TextBlock($"Error: {ex.Message}"));

        Assert.IsType<ErrorBoundaryElement>(eb);
        Assert.IsType<TextBlockElement>(eb.Child);
    }

    [Fact]
    public void DSL_ErrorBoundary_With_Static_Fallback()
    {
        var eb = ErrorBoundary(
            TextBlock("child"),
            TextBlock("fallback"));

        Assert.IsType<ErrorBoundaryElement>(eb);
        var fallbackResult = eb.Fallback(new Exception("test"));
        Assert.Equal("fallback", ((TextBlockElement)fallbackResult).Text);
    }

    [Fact]
    public void Fallback_Receives_Correct_Exception()
    {
        Exception? received = null;
        var eb = ErrorBoundary(
            TextBlock("child"),
            ex => { received = ex; return TextBlock("err"); });

        var testEx = new InvalidOperationException("specific");
        eb.Fallback(testEx);

        Assert.Same(testEx, received);
    }

    [Fact]
    public void ShallowEquals_Returns_False_For_ErrorBoundary()
    {
        var child = TextBlock("Hello");
        Func<Exception, Element> fallback = ex => TextBlock("err");
        var a = new ErrorBoundaryElement(child, fallback);
        var b = new ErrorBoundaryElement(child, fallback);
        Assert.False(Element.ShallowEquals(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  Reconciler dispatch — ErrorBoundary is recognized
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void CanUpdate_Matches_ErrorBoundary_Elements()
    {
        var reconciler = new Reconciler();
        var a = new ErrorBoundaryElement(TextBlock("a"), ex => TextBlock("err"));
        var b = new ErrorBoundaryElement(TextBlock("b"), ex => TextBlock("err2"));
        Assert.True(reconciler.CanUpdate(a, b));
    }

    [Fact]
    public void CanUpdate_Rejects_Different_Types()
    {
        var reconciler = new Reconciler();
        var eb = new ErrorBoundaryElement(TextBlock("a"), ex => TextBlock("err"));
        var text = TextBlock("a");
        Assert.False(reconciler.CanUpdate(eb, text));
    }















    [Fact]
    public void ErrorBoundaryDepth_Prevents_Local_Catch_For_Component_Errors()
    {
        // This tests the conceptual behavior: when _errorBoundaryDepth > 0,
        // the per-component catch filter (when _errorBoundaryDepth == 0) does NOT match,
        // allowing the error to propagate to the ErrorBoundary.
        // Verified structurally by the conditional catch filter in the code.
        var reconciler = new Reconciler();
        var a = new ErrorBoundaryElement(TextBlock("a"), ex => TextBlock("err"));
        var b = new ErrorBoundaryElement(TextBlock("b"), ex => TextBlock("err2"));
        // Both are ErrorBoundaryElement — CanUpdate should match
        Assert.True(reconciler.CanUpdate(a, b));
    }
}
