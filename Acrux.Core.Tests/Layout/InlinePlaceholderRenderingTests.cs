using System.Linq;
using Acrux.Core.Css;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Parser;
using Acrux.Core.Layout;
using Acrux.Core.Layout.Inline;
using Acrux.Rendering;
using SkiaSharp;
using Xunit;

namespace Acrux.Core.Tests.Layout;

/// <summary>
/// Regression coverage for internal object-replacement (U+FFFC) placeholders
/// and inline item bookkeeping:
///  - The block's internal placeholder char (used by floats / out-of-flow /
///    atomic inline items) must never be emitted as paintable text; it is
///    consumed as a replaced / control unit.
///  - Open/close tag offsets must sit at the current text cursor so a nested
///    inline span block still breaks lines without aborting layout.
///  - A real U+FFFC authored inside a text node must be preserved (no global
///    strip of user text).
/// </summary>
public class InlinePlaceholderRenderingTests
{
    private static Element ParseSingleBlock(string html)
    {
        var doc = HtmlDocumentParserIntegration.ParseHtml(html);
        var styleComputer = new StyleComputer();
        styleComputer.ComputeStyles(doc, 800, 600);
        var block = doc.Body!.Children.OfType<Element>().First();
        Assert.NotNull(block.ComputedStyle);
        return block;
    }

    private static BoxFragment LayOut(Element block)
    {
        var space = ConstraintSpace.Builder(800, 600).ToConstraintSpace();
        return new InlineLayoutAlgorithm(block, space, null!).Layout().Fragment;
    }

    private static string[] RunTexts(BoxFragment frag) =>
        frag.Lines.SelectMany(l => l.Runs).Select(r => r.Text ?? "").ToArray();

    [Fact]
    public void NestedInlineSpans_OffsetBookkeepingDoesNotAbortLayout()
    {
        // Regression: OpenTag/CloseTag items recorded _text.Length instead of the
        // current text cursor, leaving a gap ("offset not in item") that threw in
        // LineBreaker and aborted layout of the whole block/page.
        var block = ParseSingleBlock(
            "<html><body><div style='font-size:16px'>" +
            "<span style='color:red'>Red text</span> and " +
            "<span style='color:blue'>blue text</span> in same line." +
            "</div></body></html>");

        var frag = LayOut(block);

        var texts = RunTexts(frag);
        Assert.Contains("Red", texts);
        Assert.Contains("text", texts);
        Assert.Contains("blue", texts);
        Assert.Contains("line.", texts);
        Assert.DoesNotContain(texts, t => t.Contains('\uFFFC'));
    }

    [Fact]
    public void BlockLeadingAtomicInline_PlaceholderIsNotPaintedAsText()
    {
        var block = ParseSingleBlock(
            "<html><body><div style='font-size:16px'><img src='x.png'>after img</div></body></html>");

        var frag = LayOut(block);

        var texts = RunTexts(frag);
        Assert.NotEmpty(texts);
        Assert.DoesNotContain(texts, t => t.Contains('\uFFFC'));
        Assert.Contains(texts, t => t.Contains("after"));

        // The internal placeholder may stay in the flat text used for hit
        // testing/offsets, but never in a paintable text run.
        var textFragments = frag.FragmentItems!.Items.Where(i => i.Type == FragmentItem.ItemType.Text);
        Assert.DoesNotContain(textFragments, i => i.Text.Contains('\uFFFC'));
    }

    [Fact]
    public void BlockLeadingFloat_PlaceholderIsNotPaintedAsText()
    {
        var block = ParseSingleBlock(
            "<html><body><div style='font-size:16px'><span style='float:left'>x</span>after float</div></body></html>");

        var frag = LayOut(block);

        var texts = RunTexts(frag);
        Assert.DoesNotContain(texts, t => t.Contains('\uFFFC'));
        Assert.Contains(texts, t => t.Contains("after"));
    }

    [Fact]
    public void BlockLeadingOutOfFlow_PlaceholderIsNotPaintedAsText()
    {
        var block = ParseSingleBlock(
            "<html><body><div style='font-size:16px'>" +
            "<span style='position:absolute'>abs</span>after oof</div></body></html>");

        var frag = LayOut(block);

        var texts = RunTexts(frag);
        Assert.DoesNotContain(texts, t => t.Contains('\uFFFC'));
        Assert.Contains(texts, t => t.Contains("after"));
    }

    [Fact]
    public void UserAuthoredObjectReplacementChar_IsPreservedInTextRuns()
    {
        // Only placeholder chars owned by non-text inline items are internal.
        // A literal U+FFFC inside a real text node is user content and must not
        // be stripped globally.
        var block = ParseSingleBlock(
            "<html><body><div style='font-size:16px'>before \uFFFC after</div></body></html>");

        var frag = LayOut(block);

        var texts = RunTexts(frag);
        Assert.Contains(texts, t => t.Contains('\uFFFC'));
        Assert.Contains(texts, t => t.Contains("before"));
        Assert.Contains(texts, t => t.Contains("after"));
    }

    [Fact]
    public async System.Threading.Tasks.Task NgTextRuns_CarryTheirSourceTextNode()
    {
        // Regression: NG text runs were emitted without a Node, so the renderer
        // (DrawInlineRuns, gated on run.Node is TextNode) painted no block text.
        var load = await new DocumentManager().LoadHtmlAsync(
            "<html><body style='font-size:16px'><h1>Hello World</h1>" +
            "<p>This is a test paragraph.</p><div>flexible content</div></body></html>",
            "acrux://local", 1000, 800);

        var textRuns = CollectTextRuns(load.Document!).Where(r => r.Text.Any(ch => char.IsLetter(ch))).ToList();
        Assert.NotEmpty(textRuns);
        foreach (var run in textRuns)
        {
            Assert.NotNull(run.Node);
            var textNode = Assert.IsType<TextNode>(run.Node);
            Assert.Contains(run.Text, textNode.TextContent ?? "");
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task NgLineBaselines_AreAnchoredToTheAbsoluteBoxPosition()
    {
        // Regression: line Y/baseline were computed from the raw fragment offset,
        // which is zero for nested containers (flex/grid items), so their text
        // painted at the top of the page.
        var load = await new DocumentManager().LoadHtmlAsync(
            "<html><body style='font-size:16px'><div style='display:flex;margin-top:400px'>" +
            "<div>Flex 1</div><div>Flex 2</div></div></body></html>",
            "acrux://local", 1000, 800);

        int checkedBoxes = 0;
        foreach (var box in GetAllLayoutBoxes(load.Document!))
        {
            if (box.Lines == null || box.Lines.Count == 0) continue;
            foreach (var line in box.Lines)
            {
                if (line.Runs.Count == 0) continue;
                Assert.True(line.Y >= box.ContentBox.Top - 0.01f,
                    $"line.Y {line.Y} should be at/inside box content top {box.ContentBox.Top}");
                Assert.True(line.Baseline > box.ContentBox.Top,
                    $"line.Baseline {line.Baseline} should sit inside box (top {box.ContentBox.Top})");
                checkedBoxes++;
            }
        }
        Assert.NotEqual(0, checkedBoxes);
    }

    private static List<Dom.InlineRun> CollectTextRuns(Acrux.Core.Dom.Node node)
    {
        var runs = new List<Dom.InlineRun>();
        if (node is Element el && el.LayoutBox != null)
        {
            foreach (var line in el.LayoutBox.Lines ?? new List<Dom.LineBox>())
                runs.AddRange(line.Runs);
        }
        foreach (var child in node.Children)
            runs.AddRange(CollectTextRuns(child));
        return runs;
    }

    private static List<Dom.LayoutBox> GetAllLayoutBoxes(Acrux.Core.Dom.Node node)
    {
        var boxes = new List<Dom.LayoutBox>();
        if (node is Element el && el.LayoutBox != null)
            boxes.Add(el.LayoutBox);
        foreach (var child in node.Children)
            boxes.AddRange(GetAllLayoutBoxes(child));
        return boxes;
    }

    [Fact]
    public void BlockWithInheritedPlaceholderKeepsFollowingRealText()
    {
        var block = ParseSingleBlock(
            "<html><body><div style='font-size:16px'>" +
            "<img src='x.png'><img src='y.png'>trailing text</div></body></html>");

        var frag = LayOut(block);

        var texts = RunTexts(frag);
        Assert.DoesNotContain(texts, t => t.Contains('\uFFFC'));
        Assert.Contains(texts, t => t.Contains("trailing"));
    }

    [Fact]
    public async System.Threading.Tasks.Task WhitespaceOnlyTextNodes_DoNotPaintTofuBoxAtBlockStarts()
    {
        // Regression: block containers (div/flex) with whitespace + newline text
        // nodes between block children were painted verbatim by the no-run
        // fallback. Skia renders '\n' as a missing-glyph tofu box, showing a
        // box character at the start of each block.
        var html =
            "<html><body style='padding:8px;font-size:16px'>" +
            "<div style='display:flex'>" +
            "  <div>one</div>\n" +
            "  <div>two</div>\n" +
            "</div>\n" +
            "<p>\n  paragraph\n</p>\n" +
            "</body></html>";

        var load = await new DocumentManager().LoadHtmlAsync(html, "acrux://local", 1000, 1000);
        var visitor = new PaintVisitor(0, new Dictionary<string, SKTypeface>(), new ImageCache(),
            new[] { "Arial" }, "acrux://local", 1000, 1000);
        visitor.VisitDocumentStacking(load.Document!);
        var displayList = visitor.GetDisplayList();

        var dirty = new List<string>();
        for (int i = 0; i < displayList.Count; i++)
        {
            if (displayList[i] is DrawTextOp t)
            {
                var text = t.Text ?? "";
                if (string.IsNullOrWhiteSpace(text) || text.Any(c => c is '\n' or '\r' or '\t'))
                    dirty.Add(text);
            }
        }
        Assert.Empty(dirty);
    }
}