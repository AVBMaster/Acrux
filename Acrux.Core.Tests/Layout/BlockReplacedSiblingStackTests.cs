using System.Linq;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Xunit;

namespace Acrux.Core.Tests.Layout;

/// <summary>
/// Regression coverage for block-level layout of sibling replaced elements
/// (INPUT / IMG / CANVAS with display:block) inside a normal (non-new-formatting-
/// context) block flow:
///  - Each sibling must be stacked below the previous one by exactly its own block
///    size; two siblings must never share the same border-box rect.
///  - The container's block size must cover every sibling, and the container itself
///    must stay at its own block-start (children cannot push their parent down).
///  - A run of pure whitespace between sibling blocks produces no line box, while a
///    run with real text produces exactly one line box at the right offset.
/// The first two rules used to fail because a replaced child handed back an
/// unresolved BFC block-offset: its parent therefore never resolved its own offset
/// during the child loop (so every sibling was placed at block-offset 0 and only the
/// last one stayed visible) and derived its own offset from the in-flow cursor
/// instead, which displaced the parent by one child's height and collapsed its
/// block size to that same height.
/// </summary>
public class BlockReplacedSiblingStackTests
{
    private static Document LayoutPage(string html, float width = 400, float height = 200)
    {
        var manager = new DocumentManager();
        return manager.LoadHtmlAsync(html, "about:blank", width, height).GetAwaiter().GetResult().Document;
    }

    /// <summary>In-flow element children of <paramref name="parent"/> that got a box.</summary>
    private static List<Element> ChildBoxes(Element? parent) =>
        parent is null
            ? new List<Element>()
            : parent.Children.OfType<Element>().Where(e => e.LayoutBox != null).ToList();

    private static float TopOf(Element e) => e.LayoutBox!.BorderBox.Top;

    private static float HeightOf(Element e) => e.LayoutBox!.BorderBox.Height;

    [Fact]
    public void TwoBlockLevelInputs_StackVertically_AndParentCoversBoth()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0">
            <input type=text style="display:block;width:200px;height:30px;background:#ff0000" value="A">
            <input type=text style="display:block;width:200px;height:30px;background:#0000ff" value="B">
            </body></html>
            """);

        var body = doc.Body!;
        var inputs = ChildBoxes(body).Where(e => e.TagName == "INPUT").ToList();
        Assert.Equal(2, inputs.Count);

        float topA = TopOf(inputs[0]);
        float heightA = HeightOf(inputs[0]);
        float topB = TopOf(inputs[1]);
        float heightB = HeightOf(inputs[1]);

        // The first sibling sits at the body's content start and the body is not
        // displaced by its own children.
        Assert.Equal(0f, TopOf(body), 1);
        Assert.Equal(0f, topA, 1);
        // Each sibling is advanced by the previous sibling's block size.
        Assert.Equal(topA + heightA, topB, 1);
        Assert.Equal(heightA, heightB, 1);
        // The parent covers both.
        Assert.Equal(heightA + heightB, HeightOf(body), 1);
    }

    [Fact]
    public void ThreeBlockLevelSiblings_EachStacksWithoutOverlap()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0">
            <input type=text style="display:block;width:100px;height:20px" value="A">
            <canvas style="display:block;width:100px;height:25px"></canvas>
            <input type=text style="display:block;width:100px;height:30px" value="C">
            </body></html>
            """);

        var body = doc.Body!;
        var kids = ChildBoxes(body);
        Assert.Equal(3, kids.Count);

        float expected = 0;
        foreach (var kid in kids)
        {
            Assert.Equal(expected, TopOf(kid), 1);
            expected = TopOf(kid) + HeightOf(kid);
        }

        Assert.Equal(expected, HeightOf(body), 1);
    }

    [Fact]
    public void WhitespaceOnlyRunBetweenBlocks_ProducesNoLineBox()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0"><div style="height:30px"></div>
            <div style="height:30px"></div></body></html>
            """);

        var body = doc.Body!;
        var divs = ChildBoxes(body);
        Assert.Equal(2, divs.Count);
        Assert.Equal(0f, TopOf(divs[0]), 1);
        Assert.Equal(30f, TopOf(divs[1]), 1);
        Assert.Equal(60f, HeightOf(body), 1);
    }

    [Fact]
    public void TextRunBetweenBlocks_ProducesExactlyOneLineBox()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0"><div style="height:30px"></div>TXT<div style="height:30px"></div></body></html>
            """);

        var body = doc.Body!;
        float lineHeight = body.LayoutBox!.LineHeight;
        var divs = ChildBoxes(body);
        Assert.Equal(2, divs.Count);

        // Exactly one line box between the two blocks, at the right offset.
        Assert.Equal(30f + lineHeight, TopOf(divs[1]), 1);
        Assert.Equal(60f + lineHeight, HeightOf(body), 1);
    }

    [Fact]
    public void ReplacedSiblingMargins_AreCollapsedLikeBlockSiblings()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0">
            <input type=text style="display:block;width:100px;height:20px;margin:10px 0" value="A">
            <input type=text style="display:block;width:100px;height:20px;margin:10px 0" value="B">
            </body></html>
            """);

        var body = doc.Body!;
        var inputs = ChildBoxes(body);
        Assert.Equal(2, inputs.Count);

        // The block-start margin collapses out of the body (no border or padding
        // between them) and the adjoining margins collapse to their maximum — the
        // same numbers a pair of divs produces.
        float topA = TopOf(inputs[0]);
        Assert.Equal(topA, TopOf(body), 1);
        Assert.Equal(topA + HeightOf(inputs[0]) + 10f, TopOf(inputs[1]), 1);
    }

    [Fact]
    public void ReplacedSiblingInsideBorderedContainer_SitsAtContentEdgePlusMargin()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0;padding:20px 0;border:5px solid #000">
            <input type=text style="display:block;width:100px;height:20px;margin-top:10px" value="A">
            </body></html>
            """);

        var input = ChildBoxes(doc.Body!)[0];
        // Border 5 + padding 20 collapse nothing away, so the margin adds to the
        // content edge instead of moving the container.
        Assert.Equal(35f, TopOf(input), 1);
    }
}

/// <summary>
/// The text-control placeholder field (_placeholderChild) is a porting stub
/// (IsTextControlPlaceholder => false), so the placeholder branch in
/// BlockLayoutAlgorithm.LayoutMain is never taken. Layout must therefore work
/// without the placeholder path — no crash, no null boxes, for a body whose
/// children are replaced INPUT elements.
/// </summary>
public class TextControlPlaceholderReflowTests
{
    private static Document LayoutPage(string html)
    {
        var doc = Acrux.Core.Dom.Parser.HtmlDocumentParserIntegration.ParseHtml(html);
        var styleComputer = new Acrux.Core.Css.StyleComputer();
        styleComputer.ComputeStyles(doc, 400, 200);
        var engine = new LayoutEngine();
        engine.Layout(doc, 400, 200);
        return doc;
    }

    [Fact]
    public void LayoutWithoutPlaceholder_ProducesValidBoxes()
    {
        var doc = LayoutPage("""
            <!DOCTYPE html><html><body style="margin:0">
            <input type=text style="display:block;width:200px;height:30px" value="A">
            <input type=text style="display:block;width:200px;height:30px" value="B">
            </body></html>
            """);

        // _placeholderChild is never set (IsTextControlPlaceholder => false), so
        // the placeholder branch of LayoutMain is not taken. Layout must still
        // produce a non-null LayoutBox with valid geometry for both replaced
        // siblings.
        var body = doc.Body!;
        Assert.NotNull(body.ComputedStyle);
        Assert.NotNull(body.LayoutBox);

        var space = ConstraintSpace.Builder(400, 200).ToConstraintSpace();
        var result = new BlockLayoutAlgorithm(body, space).Layout();
        Assert.NotNull(result);
        Assert.NotNull(result.Fragment.Element);
        var kids = result.Fragment.Children.Where(f => f.Element != null).ToList();
        // Both replaced siblings must be positioned distinctly, not at block-offset 0.
        Assert.Equal(2, kids.Count);
        Assert.True(kids[1].BlockOffset > kids[0].BlockOffset,
            $"second sibling should follow the first: {kids[0].BlockOffset} vs {kids[1].BlockOffset}");
    }
}
