using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Dom.Parser;
using Acrux.Core.Css;
using Acrux.Core.Input;
using Acrux.Core.Layout;
using Acrux.Rendering;

// ©¤©¤ Load the real test page through the same pipeline as the app shell ©¤©¤
string htmlPath = Path.GetFullPath("test_scroll_interactive.html");
if (!File.Exists(htmlPath))
    htmlPath = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test_scroll_interactive.html"));
string html = File.ReadAllText(htmlPath);
string baseUrl = new Uri(htmlPath).AbsoluteUri;

RenderSnapshot.EnsureInitialized();
var dm = new DocumentManager();
var load = dm.LoadHtmlAsync(html, baseUrl, 1009, 730, 1f).GetAwaiter().GetResult();
new LayoutEngine().Layout(load.Document, 1009, 730);

Element? target = null;
void Walk(Element el)
{
    if (target != null) return;
    if (el.GetAttribute("class") == "default-scroll") { target = el; return; }
    foreach (var c in el.Children) if (c is Element ce) Walk(ce);
}
if (load.Document.DocumentElement != null) Walk(load.Document.DocumentElement);
if (target == null) { Console.WriteLine("[FAIL] .default-scroll not found"); return; }

var box = target!.LayoutBox!;
var bb = box.BorderBox;
var pb = box.PaddingBox;
Console.WriteLine($"[box] border=({bb.Left:F0},{bb.Top:F0} {bb.Width:F0}x{bb.Height:F0}) padding=({pb.Left:F0},{pb.Top:F0} {pb.Width:F0}x{pb.Height:F0})");
Console.WriteLine($"[box] IsScrollContainer={box.IsScrollContainer} contentH={box.ContentBox.Height:F0} scrollContentH={box.ScrollContentHeight:F0}");

// Scrollbar placeholder: content box must shrink by the bar thickness and
// children must not extend under the reserved strip.
float reserved = pb.Right - box.ContentBox.Right;
float reservedBottom = pb.Bottom - box.ContentBox.Bottom;
float maxChildRight = 0;
foreach (var c in box.Children) maxChildRight = Math.Max(maxChildRight, c.MarginBox.Right);
Console.WriteLine($"[reserve] contentRight={box.ContentBox.Right:F1} paddingRight={pb.Right:F1} reserved={reserved:F1} reservedBottom={reservedBottom:F1} maxChildRight={maxChildRight:F1} underBar={maxChildRight > box.ContentBox.Right + 0.5f}");

var firstItem = box.Children.FirstOrDefault();
Console.WriteLine($"[children] count={box.Children.Count} first=({(firstItem?.BorderBox.Left ?? -1):F1},{(firstItem?.BorderBox.Top ?? -1):F1} {(firstItem?.BorderBox.Width ?? -1):F1}x{(firstItem?.BorderBox.Height ?? -1):F1}) scrollContentW={box.ScrollContentWidth:F1}");

var itemEls = target.Children.OfType<Element>().FirstOrDefault(e => e.GetAttribute("class") == "item");
if (itemEls is Element ie)
{
    Console.WriteLine($"[item] display={ie.ComputedStyle?.Display} widthStyle={ie.ComputedStyle?.Width} boxW={ie.LayoutBox?.BorderBox.Width:F1} boxRight={ie.LayoutBox?.MarginBox.Right:F1}");
}
Console.WriteLine($"[thickness] ScrollbarMetrics={Acrux.Core.Dom.ScrollbarMetrics.ThicknessFor(target.ComputedStyle!):F1}");

// ©¤©¤ Layout a fresh document with the single modern pipeline ©¤©¤
var dm2 = new DocumentManager();
var load2 = dm2.LoadHtmlAsync(html, baseUrl, 1009, 730, 1f).GetAwaiter().GetResult();
var layoutEngine2 = new LayoutEngine();
layoutEngine2.Layout(load2.Document, 1009, 730);

// ©¤©¤ Container ¢Ý both-scroll: horizontal + vertical scrollbars ©¤©¤
static IEnumerable<Element> AllElements(Document doc)
{
    if (doc.DocumentElement != null)
    {
        var q = new Queue<Element>();
        q.Enqueue(doc.DocumentElement);
        while (q.Count > 0)
        {
            var e = q.Dequeue();
            yield return e;
            foreach (var c in e.Children) if (c is Element ce) q.Enqueue(ce);
        }
    }
}
Element? bothEl = null;
foreach (var e in AllElements(load.Document))
    if (e.GetAttribute("class") == "both-scroll") { bothEl = e; break; }
if (bothEl?.LayoutBox is { } bb2)
{
    var b2 = bothEl.LayoutBox;
    Console.WriteLine($"[both] border=({b2.BorderBox.Left:F0},{b2.BorderBox.Top:F0} {b2.BorderBox.Width:F0}x{b2.BorderBox.Height:F0})");
    Console.WriteLine($"[both] content=({b2.ContentBox.Left:F0},{b2.ContentBox.Top:F0} {b2.ContentBox.Width:F0}x{b2.ContentBox.Height:F0}) paddingH={b2.PaddingBox.Height:F0} scrollW={b2.ScrollContentWidth:F0} scrollH={b2.ScrollContentHeight:F0}");
    Console.WriteLine($"[both] bottomReserved={b2.PaddingBox.Bottom - b2.ContentBox.Bottom:F1} rightReserved={b2.PaddingBox.Right - b2.ContentBox.Right:F1}");
    float maxChildBottomBoth = 0, maxChildRightBoth = 0;
    foreach (var c in b2.Children)
    {
        maxChildBottomBoth = Math.Max(maxChildBottomBoth, c.MarginBox.Bottom);
        maxChildRightBoth = Math.Max(maxChildRightBoth, c.MarginBox.Right);
    }
    Console.WriteLine($"[both] maxChildRight={maxChildRightBoth:F1} maxChildBottom={maxChildBottomBoth:F1} underHbar={maxChildBottomBoth > b2.ContentBox.Bottom + 0.5f} underVbar={maxChildRightBoth > b2.ContentBox.Right + 0.5f}");
}

Element? target2 = null;
void Walk2(Element el) { if (target2 != null) return; if (el.GetAttribute("class") == "default-scroll") { target2 = el; return; } foreach (var c in el.Children) if (c is Element ce) Walk2(ce); }
if (load2.Document.DocumentElement != null) Walk2(load2.Document.DocumentElement);
if (target2 != null)
{
    var b2 = target2.LayoutBox!;
    var item2 = target2.Children.OfType<Element>().FirstOrDefault(e => e.GetAttribute("class") == "item");
    Console.WriteLine($"[legacy] container={b2.BorderBox.Width:F0}x{b2.BorderBox.Height:F0} contentRight={b2.ContentBox.Right:F1} scrollW={b2.ScrollContentWidth:F1} firstItemW={item2?.LayoutBox?.BorderBox.Width:F1} underBar2={item2?.LayoutBox?.BorderBox.Right > b2.ContentBox.Right + 0.5f}");
}

// What would overflow look like WITHOUT the scrollbar shrink? Re-derive from child extents.
float ch = box.ScrollContentHeight;
Console.WriteLine($"[overflow] vOverflow={ch > pb.Height + 0.5f} (scrollContentH {ch:F1} vs paddingH {pb.Height:F1}) contentBoxH={box.ContentBox.Height:F1}");

Console.WriteLine($"[style] overflow={target.ComputedStyle?.Overflow} overflowX={target.ComputedStyle?.OverflowX} overflowY={target.ComputedStyle?.OverflowY}");
Console.WriteLine($"[styles] scrollbarWidth={target.ComputedStyle?.ScrollbarWidth} thumb=({box.ContentBox.Right:F1}..{pb.Right:F1})");

float thickness = 12f;
float sbLeft = pb.Right - thickness;

// Thumb geometry exactly like ScrollInteraction.BeginVerticalDrag
float trackH = pb.Height;
float thumbRatio = box.ContentBox.Height / Math.Max(1, box.ScrollContentHeight);
float thumbH = Math.Max(20f, trackH * Math.Min(1, thumbRatio));
float maxScroll = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);
float thumbTop = (trackH - thumbH) * (Math.Clamp(box.ScrollY, 0, maxScroll) / maxScroll);
float grabY = pb.Top + thumbTop + thumbH / 2;   // center of thumb
float grabX = sbLeft + thickness / 2;

Console.WriteLine($"[geom] thumbTop={thumbTop:F1} thumbH={thumbH:F1} maxScroll={maxScroll:F0} grab=({grabX:F0},{grabY:F0})");

var scrollInt = new ScrollInteraction();
scrollInt.SetDocument(load.Document);
bool scrollFired = false;
scrollInt.OnScrollChanged = (_) => scrollFired = true;

// ©¤©¤ Simulate drag: mousedown on thumb, move up 40px ©¤©¤
bool grabbed = scrollInt.HandleMouseDown(grabX, grabY);
Console.WriteLine($"[down] grabbed={grabbed}");
scrollInt.HandleMouseMove(grabX, grabY + 40);
Console.WriteLine($"[move] scrollY={box.ScrollY:F1} (expect ~{maxScroll * (40f / (trackH - thumbH)):F1}) eventFired={scrollFired}");

// ©¤©¤ Rebuild display list like BrowserApp B1 branch and rasterize ©¤©¤
SKBitmap Rasterize(float contentOffsetY)
{
    var visitor = new PaintVisitor(contentOffsetY, null, null,
        SKFontManager.Default.FontFamilies.ToArray(), baseUrl, 1009, 730);
    visitor.SetSkipInputTextOverlay(true);
    visitor.VisitDocumentStacking(load.Document);
    var dl = visitor.GetDisplayList();
    dl.SortByZIndex();
    var info = new SKImageInfo(1009, 730, SKColorType.Rgba8888, SKAlphaType.Premul);
    var bmp = new SKBitmap(info);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.White);
    // mimic SkiaRenderer.RenderWithScroll page translate (page scroll = 0 here)
    dl.Execute(canvas);
    canvas.Flush();
    return bmp;
}

using var before = Rasterize(0);

// Now scroll back via wheel path and compare again
scrollInt.HandleWheel(0, -120 * 10, (float)(bb.Left + 20), (float)(bb.Top + 20));
Console.WriteLine($"[wheel] scrollY={box.ScrollY:F1}");
using var after = Rasterize(0);

// Compare ONLY the container's content region (exclude scrollbar strip on the right)
int rx0 = (int)pb.Left, ry0 = (int)pb.Top + 1;
int rx1 = (int)(pb.Right - thickness - 2), ry1 = (int)pb.Bottom - 1;
int diffPx = 0, total = 0;
for (int y = ry0; y < ry1; y++)
    for (int x = rx0; x < rx1; x++)
    {
        total++;
        var a = before.GetPixel(x, y);
        var b = after.GetPixel(x, y);
        if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 10)
            diffPx++;
    }
Console.WriteLine($"[render] content region ({rx0},{ry0})-({rx1},{ry1}): {diffPx}/{total} px differ ({100.0 * diffPx / total:F1}%)");

Console.WriteLine($"[verdict] grabbed={grabbed} scrolled={box.ScrollY > 0} contentMoved={diffPx > total * 0.05} -> {(grabbed && box.ScrollY > 0 && diffPx > total * 0.05 ? "PASS" : "FAIL")}");

// ©¤©¤ Horizontal scrollbar placeholder on container ¢Ý (both-scroll) ©¤©¤
if (bothEl?.LayoutBox is { } bb3)
{
    var b3 = bothEl.LayoutBox;
    var si = new ScrollInteraction();
    si.SetDocument(load.Document);
    // Scroll horizontally to max
    var bothInt = new ScrollInteraction();
    bothInt.SetDocument(load.Document);
    bool hFired = false;
    bothInt.OnScrollChanged = (_) => hFired = true;
    bothInt.HandleWheel(240 * 8, 0, b3.BorderBox.Left + 5, b3.BorderBox.Top + 5); // scroll far right

    var vis = new PaintVisitor(0, null, null, SKFontManager.Default.FontFamilies.ToArray(), baseUrl, 1009, 730);
    vis.SetSkipInputTextOverlay(true);
    vis.VisitDocumentStacking(load.Document);
    var dlv = vis.GetDisplayList();
    dlv.SortByZIndex();
    var ib = new SKImageInfo(1009, 1000, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var bmpH = new SKBitmap(ib);
    using (var c2 = new SKCanvas(bmpH))
    {
        c2.Clear(SKColors.White);
        dlv.Execute(c2);
        c2.Flush();
    }

    // Sample the horizontal scrollbar strip (bottom strip): content must NOT
    // paint there (should be track background), and the content area above it.
    int stripY = Math.Min(999, (int)(b3.PaddingBox.Bottom - 6));
    int contentY = Math.Min(999, (int)(b3.ContentBox.Bottom - 2));
    int x0 = (int)(b3.ContentBox.Left + 1), x1 = (int)(b3.ContentBox.Right - 2);
    int ySample = Math.Min(999, (int)(b3.PaddingBox.Bottom - 6));
    int midX = (x0 + x1) / 2;
    var p0 = bmpH.GetPixel(x0, ySample);
    var pm = bmpH.GetPixel(midX, ySample);
    var pc = bmpH.GetPixel(midX, Math.Min(999, contentY));
    Console.WriteLine($"[hstrip] xrange={x0}..{x1} width={x1 - x0} scrollX={b3.ScrollX:F1}");
    Console.WriteLine($"[hpix] stripY={ySample} x={x0}=#{p0.Red:X2}{p0.Green:X2}{p0.Blue:X2} x{midX}=#{pm.Red:X2}{pm.Green:X2}{pm.Blue:X2} | contentY={contentY}: #{pc.Red:X2}{pc.Green:X2}{pc.Blue:X2}");
}
