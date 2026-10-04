using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Layout.Inline;
using Acrux.Core.Dom.Parser;
using Acrux.Core.Css;
using Acrux.Core.Css.Tokenizer;
using Acrux.Core.Css.Matcher;
using System.Linq;

int failures = 0;

void Check(bool ok, string label)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
    if (!ok) failures++;
}

Console.WriteLine("=== 1. OutOfFlowLayoutPart (direct candidate) ===");
{
    var style = new ComputedStyle
    {
        Position = PositionType.Absolute,
        Left = new PixelLength(10),
        Top = new PixelLength(20),
        Width = new PixelLength(100),
        Height = new PixelLength(50),
    };
    var box = new LayoutBox { Dimensions = new BoxDimensions { Style = style, Element = null } };
    var candidate = new OutOfFlowChildCandidate(box,
        new LogicalStaticPosition(new LogicalOffset(0, 0),
            LogicalStaticPosition.StaticInlinePosition.Left,
            LogicalStaticPosition.StaticBlockPosition.Top,
            WritingDirectionMode.HorizontalLtr)) { IsAbsolute = true };

    var builder = new BoxFragmentBuilder { InlineSize = 500, BlockSize = 400, PaddingLeft = 5, PaddingTop = 5 };
    var oof = new OutOfFlowLayoutPart(builder);
    oof.AddCandidate(candidate);
    oof.Run();

    Check(builder.Children.Count == 1, $"OOF children count = {builder.Children.Count}");
    if (builder.Children.Count == 1)
    {
        var f = builder.Children[0];
        // Containing block is the padding box; fragment offsets are content-box
        // relative: left 10 - padding 5 = 5, top 20 - padding 5 = 15.
        Check(f.InlineOffset == 5 && f.BlockOffset == 15, $"pos=({f.InlineOffset:F0},{f.BlockOffset:F0}) expect (5,15)");
        Check(f.IsOutOfFlowPositioned, "IsOutOfFlowPositioned");
    }
}

Console.WriteLine("=== 2. BlockChildIterator order ===");
{
    var doc = new Document();
    var root = new LayoutBox();
    var a = new LayoutBox { Parent = root, Dimensions = new BoxDimensions { Element = doc.CreateElement("div") } };
    var b = new LayoutBox { Parent = root, Dimensions = new BoxDimensions { Element = doc.CreateElement("p") } };
    root.Children.Add(a);
    root.Children.Add(b);

    var iter = new BlockChildIterator(a, null, true);
    var e1 = iter.NextChild(null);
    var e2 = iter.NextChild(null);
    var e3 = iter.NextChild(null);
    Check(e1.Child?.Dimensions?.Element?.TagName == "DIV", $"first={e1.Child?.Dimensions?.Element?.TagName} idx={e1.ChildIndex}");
    Check(e2.Child?.Dimensions?.Element?.TagName == "P", $"second={e2.Child?.Dimensions?.Element?.TagName} idx={e2.ChildIndex}");
    Check(e3.Child == null, $"third is null (idx={e3.ChildIndex})");
}

Console.WriteLine("=== 3. FragmentItems builder (abc def) ===");
{
    var doc = new Document();
    var span = doc.CreateElement("span");
    span.TextContent = "abc def";
    var node = new InlineNode(span, new ComputedStyle { FontSize = 16 });
    node.CollectInlineItems();
    var lines = new LineBreaker().BreakLines(node.ItemsData, 100);
    var itemsBuilder = new FragmentItemsBuilder();
    foreach (var line in lines)
    {
        var logical = new LogicalLineItems();
        new LogicalLineBuilder(node, new ConstraintSpace(), null, new InlineLayoutStateStack())
            .CreateLine(line, logical, null);
        itemsBuilder.AddLogicalLineItems(logical, WritingDirectionMode.HorizontalLtr, null);
    }
    var fragmentItems = itemsBuilder.ToFragmentItems(node.ItemsData.TextContent);
    Check(fragmentItems.Count > 0, $"FragmentItems count={fragmentItems.Count} text='{fragmentItems.NormalText}'");
    int shown = 0;
    foreach (var item in fragmentItems.Items)
    {
        if (shown++ >= 6) break;
        Console.WriteLine($"    {item}");
    }
}

Console.WriteLine("=== 4. LineTruncator ellipsis (overflowing word) ===");
{
    var doc = new Document();
    var span = doc.CreateElement("span");
    span.ComputedStyle = new ComputedStyle
    {
        FontSize = 16,
        TextOverflow = TextOverflowType.Ellipsis,
        WhiteSpace = WhiteSpaceMode.Nowrap,
        Overflow = OverflowType.Hidden,
        Display = DisplayType.Inline,
    };
    span.AppendChild(doc.CreateTextNode("Supercalifragilisticexpialidociousantidisestablishmentarianism"));
    var inlineCs = ConstraintSpace.Builder(120, 100).ToConstraintSpace();
    var inlineR = new InlineLayoutAlgorithm(span, inlineCs, null!).Layout();
    bool sawEllipsis = inlineR.Fragment.Lines.SelectMany(l => l.Runs).Any(run => run.Text != null && run.Text.Contains("\u2026"));
    Check(inlineR.Fragment.Lines.Count == 1, $"lines={inlineR.Fragment.Lines.Count} (expect 1)");
    Check(sawEllipsis, "saw ellipsis run '\u2026'");
    Check(inlineR.Fragment.FragmentItems is { Count: > 0 }, $"FragmentItems attached: {inlineR.Fragment.FragmentItems?.Count ?? 0} items");
}

Console.WriteLine("=== 5. OOF via BlockLayoutAlgorithm ===");
{
    var doc = new Document();
    var container = doc.CreateElement("div");
    container.ComputedStyle = new ComputedStyle
    {
        Position = PositionType.Relative,
        Width = new PixelLength(400),
        Height = new PixelLength(300),
    };
    var absChild = doc.CreateElement("div");
    absChild.ComputedStyle = new ComputedStyle
    {
        Position = PositionType.Absolute,
        Left = new PixelLength(30),
        Top = new PixelLength(40),
        Width = new PixelLength(100),
        Height = new PixelLength(50),
    };
    container.AppendChild(absChild);

    var cs = ConstraintSpace.Builder(400, 300).ToConstraintSpace();
    var r = new BlockLayoutAlgorithm(container, cs).Layout();
    var oof = r.Fragment.Children.FirstOrDefault(c => c.IsOutOfFlowPositioned);
    Check(oof != null, "OOF child found");
    if (oof != null)
        Check(oof.InlineOffset == 30 && oof.BlockOffset == 40, $"offset=({oof.InlineOffset:F0},{oof.BlockOffset:F0}) expect (30,40)");
}

Console.WriteLine("=== 6. BlockNode wrapper ===");
{
    var doc = new Document();
    var container = doc.CreateElement("div");
    container.ComputedStyle = new ComputedStyle { Position = PositionType.Relative };
    var layoutBox = new LayoutBox { Dimensions = new BoxDimensions { Element = container, Style = container.ComputedStyle } };
    var blockNode = new BlockNode(layoutBox);
    Check(blockNode.IsBlock, $"IsBlock={blockNode.IsBlock}");
    Check(!blockNode.IsFloating, $"IsFloating={blockNode.IsFloating}");
}

Console.WriteLine("=== 7. clearance (clear:both) ===");
{
    var doc = new Document();
    var container = doc.CreateElement("div");
    container.ComputedStyle = new ComputedStyle { Width = new PixelLength(300), FontSize = 16 };

    var f1 = doc.CreateElement("div");
    f1.ComputedStyle = new ComputedStyle { Float = FloatType.Left, Width = new PixelLength(100), Height = new PixelLength(50) };
    var f2 = doc.CreateElement("div");
    f2.ComputedStyle = new ComputedStyle { Float = FloatType.Right, Width = new PixelLength(120), Height = new PixelLength(80) };
    var c1 = doc.CreateElement("div");
    c1.ComputedStyle = new ComputedStyle { Height = new PixelLength(20) };
    var cl = doc.CreateElement("div");
    cl.ComputedStyle = new ComputedStyle { Clear = ClearType.Both, Height = new PixelLength(30) };
    var c2 = doc.CreateElement("div");
    c2.ComputedStyle = new ComputedStyle { Height = new PixelLength(20) };

    container.AppendChild(f1); container.AppendChild(f2);
    container.AppendChild(c1); container.AppendChild(cl); container.AppendChild(c2);

    var cs = ConstraintSpace.Builder(300, float.PositiveInfinity).ToConstraintSpace();
    var r = new BlockLayoutAlgorithm(container, cs).Layout();
    foreach (var ch in r.Fragment.Children)
    {
        var el = ch.Element;
        string name = el == f1 ? "floatL" : el == f2 ? "floatR" : el == cl ? "clearB" : el == c1 ? "c1" : el == c2 ? "c2" : "?";
        Console.WriteLine($"    {name,-7} y={ch.BlockOffset:F0} h={ch.BlockSize:F0}");
    }
    // CSS 2.1 §9.5.2: only line boxes avoid floats, so an ordinary block overlaps
    // them (c1 stays at the content origin) and clear:both is what drops a block
    // below the floats. Float bottoms: left=50, right=80 -> c1 at 0, clearB at 80
    // (the tallest float), c2 at 110. Verified against Edge on the same markup
    // (snapshots/probe-clear.html).
    var c1f = r.Fragment.Children.FirstOrDefault(f => f.Element == c1);
    var clearB = r.Fragment.Children.FirstOrDefault(ch => ch.Element == cl);
    var c2f = r.Fragment.Children.FirstOrDefault(ch => ch.Element == c2);
    Check(c1f != null && c1f.BlockOffset == 0, $"c1 y={c1f?.BlockOffset:F0} (expect 0, block boxes overlap floats)");
    Check(clearB != null && clearB.BlockOffset == 80, $"clearB y={clearB?.BlockOffset:F0} (expect 80, cleared below the right float)");
    Check(c2f != null && c2f.BlockOffset == 110, $"c2 y={c2f?.BlockOffset:F0} (expect 110, below the cleared block)");

    // Direct ClearanceUtils checks (the triggurable side of clear).
    Check(BlockLayoutUtils.ClearanceBottom(ClearType.Left, 50, 130) == 50, "ClearanceBottom(Left)");
    Check(BlockLayoutUtils.ClearanceBottom(ClearType.Both, 50, 130) == 130, "ClearanceBottom(Both) = max");
    Check(BlockLayoutUtils.ShouldClearFloat(ClearType.Left, 20, 50, 130), "ShouldClearFloat(Left, content=20 < floatL=50)");
    Check(BlockLayoutUtils.ShouldClearFloat(ClearType.Both, 20, 50, 130), "ShouldClearFloat(Both, content=20 < 130)");
    Check(!BlockLayoutUtils.ShouldClearFloat(ClearType.Both, 150, 50, 130), "ShouldClearFloat(Both, content=edge=150 no-op)");
    Check(!BlockLayoutUtils.ShouldClearFloat(ClearType.None, 20, 50, 130), "ShouldClearFloat(None) always false");
}

Console.WriteLine("=== 8. align-content center / end ===");
{
    var doc = new Document();
    var tall = doc.CreateElement("div");
    tall.ComputedStyle = new ComputedStyle { Height = new PixelLength(200), AlignContent = "center", Width = new PixelLength(100) };
    var a = doc.CreateElement("div");
    a.ComputedStyle = new ComputedStyle { Height = new PixelLength(30) };
    tall.AppendChild(a);

    var r2 = new BlockLayoutAlgorithm(tall, ConstraintSpace.Builder(100, float.PositiveInfinity).ToConstraintSpace()).Layout();
    float yc = r2.Fragment.Children[0].BlockOffset;
    Check(Math.Abs(yc - 85) < 0.01f, $"center y={yc:F0} (expect 85 = (200-30)/2)");

    var end = doc.CreateElement("div");
    end.ComputedStyle = new ComputedStyle { Height = new PixelLength(200), AlignContent = "end", Width = new PixelLength(100) };
    var b = doc.CreateElement("div");
    b.ComputedStyle = new ComputedStyle { Height = new PixelLength(30) };
    end.AppendChild(b);
    var r3 = new BlockLayoutAlgorithm(end, ConstraintSpace.Builder(100, float.PositiveInfinity).ToConstraintSpace()).Layout();
    float ye = r3.Fragment.Children[0].BlockOffset;
    Check(Math.Abs(ye - 170) < 0.01f, $"end y={ye:F0} (expect 170 = 200-30)");
}

Console.WriteLine("=== 9. OofPositionedNode model + writing-mode conversion ===");
{
    var box = new LayoutBox();
    var sp = new LogicalStaticPosition(new LogicalOffset(10, 20),
        LogicalStaticPosition.StaticInlinePosition.Left,
        LogicalStaticPosition.StaticBlockPosition.Top,
        WritingDirectionMode.HorizontalLtr);
    var phys = new PhysicalOofPositionedNode(box, sp, false, false);
    Check(phys.StaticPositionOffset.Left == 10 && phys.StaticPositionOffset.Top == 20,
        $"physical static offset=({phys.StaticPositionOffset.Left},{phys.StaticPositionOffset.Top})");

    var off = new LogicalOffset(30, 40).ConvertToPhysical(WritingDirectionMode.HorizontalRtl, containerInlineSize: 200);
    Check(off.Left == 170 && off.Top == 40, $"RTL mirror=({off.Left},{off.Top}) expect (170,40)");
    var back = LogicalOffset.ConvertToLogical(new PhysicalOffset(170, 40), WritingDirectionMode.HorizontalRtl, 200);
    Check(back.InlineOffset == 30 && back.BlockOffset == 40, $"RTL back inline={back.InlineOffset} block={back.BlockOffset}");

    Check(WritingDirectionMode.HorizontalRtl.Direction == TextDirection.Rtl, "HorizontalRtl exists");
}

Console.WriteLine("=== 10. IntrinsicSizingInfo + ConcreteObjectSize ===");
{
    // Full intrinsic size.
    var full = new IntrinsicSizingInfo(new PhysicalSize(640, 480), new PhysicalSize(640, 480), true, true);
    var fullSize = ReplacedSizeUtils.ConcreteObjectSize(full, new PhysicalSize(100, 100));
    Check(fullSize.Width == 640 && fullSize.Height == 480, $"full intrinsic = {fullSize.Width}x{fullSize.Height} (expect 640x480)");

    // Width only + aspect ratio -> fill height from ratio.
    var widthOnly = new IntrinsicSizingInfo(new PhysicalSize(400, 0), new PhysicalSize(4f, 3f), true, false);
    var wSize = ReplacedSizeUtils.ConcreteObjectSize(widthOnly, new PhysicalSize(100, 100));
    Check(Math.Abs(wSize.Width - 400) < 0.01f && Math.Abs(wSize.Height - 300) < 0.01f,
        $"width-only w/ ratio = {wSize.Width:F0}x{wSize.Height:F0} (expect 400x300)");

    // No intrinsic size, aspect ratio against default (contain).
    var ratioOnly = new IntrinsicSizingInfo(PhysicalSize.Zero, new PhysicalSize(16f, 9f), false, false);
    var rSize = ReplacedSizeUtils.ConcreteObjectSize(ratioOnly, new PhysicalSize(100, 100));
    Check(Math.Abs(rSize.Width - 100) < 0.01f && Math.Abs(rSize.Height - 56.25f) < 0.01f,
        $"ratio-only contain = {rSize.Width:F2}x{rSize.Height:F2} (expect 100x56.25)");

    // None.
    Check(IntrinsicSizingInfo.None.IsNone, "IntrinsicSizingInfo.None.IsNone");

    // LengthUtils.ComputeReplacedSize with min/max clamps.
    var doc10 = new Document();
    var img = doc10.CreateElement("img");
    img.ComputedStyle = new ComputedStyle { Width = new PixelLength(200), Height = new PixelLength(150) };
    var cs10 = ConstraintSpace.Builder(300, 300).ToConstraintSpace();
    var sized = Acrux.Core.Layout.LengthUtils.ComputeReplacedSize(
        new IntrinsicSizingInfo(new PhysicalSize(640, 480), new PhysicalSize(640, 480), true, true),
        cs10, img.ComputedStyle, new BoxStrut(0, 0, 0, 0), 300, 300);
    Check(Math.Abs(sized.Width - 200) < 0.01f && Math.Abs(sized.Height - 150) < 0.01f,
        $"replaced sized by style = {sized.Width:F0}x{sized.Height:F0} (expect 200x150)");
}

Console.WriteLine("=== 11. LineClampData ===");
{
    var disabled = new LineClampData { CurrentState = LineClampData.ClampState.Disabled };
    Check(!disabled.IsLineClampContext && !disabled.ShouldHideForPaint, "disabled: not context, not hidden");

    var clamped = new LineClampData { CurrentState = LineClampData.ClampState.ClampByLines, LinesUntilClamp = 1 };
    Check(clamped.IsLineClampContext && clamped.IsAtClampPoint && !clamped.IsPastClampPoint, "ClampByLines: at clamp point");
    Check(!clamped.ShouldHideForPaint, "at clamp point: not hidden yet");

    var hidden = new LineClampData { CurrentState = LineClampData.ClampState.ClampByLines, LinesUntilClamp = 0 };
    Check(hidden.IsPastClampPoint && hidden.ShouldHideForPaint, "ClampByLines(0): past, hidden");

    var measured = new LineClampData { CurrentState = LineClampData.ClampState.MeasureLinesUntilBfcOffset, LinesUntilClamp = 3 };
    Check(measured.LinesUntilClampCount() == 0, "measure: hidden unless showMeasuredLines");
    Check(measured.LinesUntilClampCount(showMeasuredLines: true) == 3, "measure: visible with showMeasuredLines");
}

Console.WriteLine("=== 12. BfcRect / BfcDelta ===");
{
    var rect = new BfcRect(new BfcOffset(10, 20), new BfcOffset(110, 80));
    Check(rect.LineStartOffset == 10 && rect.LineEndOffset == 110, $"line offsets 10..110");
    Check(rect.BlockStartOffset == 20 && rect.BlockEndOffset == 80, $"block offsets 20..80");
    Check(rect.InlineSize == 100 && rect.BlockSize == 60, $"size {rect.InlineSize}x{rect.BlockSize} (expect 100x60)");

    var moved = rect + new BfcDelta(5, 8);
    Check(moved.LineStartOffset == 15 && moved.BlockStartOffset == 28, $"shifted start=({moved.LineStartOffset},{moved.BlockStartOffset})");

    var bfc = new BfcOffset(0, 0) + new BfcDelta(3, 4);
    Check(bfc.LineOffset == 3 && bfc.BlockOffset == 4, "BfcOffset + BfcDelta");
}

Console.WriteLine("=== 13. ExclusionArea ===");
{
    var area = ExclusionArea.Create(
        new BfcRect(new BfcOffset(0, 0), new BfcOffset(100, 50)),
        FloatType.Left, false);
    Check(area.Type == FloatType.Left && !area.IsForInitialLetterBox, "float exclusion, left");
    Check(!area.IsHiddenForPaint, "not hidden for paint");
    Check(area.Rect.InlineSize == 100 && area.Rect.BlockSize == 50, "exclusion rect 100x50");

    var letter = ExclusionArea.CreateForInitialLetterBox(
        new BfcRect(new BfcOffset(0, 0), new BfcOffset(40, 40)), FloatType.Right, true);
    Check(letter.IsForInitialLetterBox && letter.IsHiddenForPaint, "initial-letter-box, hidden");

    var copied = area.CopyWithOffset(new BfcDelta(0, 0));
    Check(ReferenceEquals(copied, area), "CopyWithOffset(0,0) returns same instance");
    var movedArea = area.CopyWithOffset(new BfcDelta(10, 20));
    Check(movedArea.Rect.BlockStartOffset == 20, $"CopyWithOffset moved block={movedArea.Rect.BlockStartOffset}");
}

Console.WriteLine("=== 14. LayoutOpportunity / LineLayoutOpportunity ===");
{
    var opp = new LayoutOpportunity(new BfcRect(new BfcOffset(0, 0), new BfcOffset(300, 100)));
    Check(!opp.HasShapeExclusions, "no shape exclusions");
    Check(opp.Rect.InlineSize == 300, "opportunity inline size 300");

    var line = opp.ComputeLineLayoutOpportunity(20, 5);
    Check(line.AvailableInlineSize == 300, $"line available inline {line.AvailableInlineSize}");
    Check(line.BfcBlockOffset == 5, $"line block offset {line.BfcBlockOffset}");
    Check(line.IsEqualToAvailableFloatInlineSize(300), "float inline size equals");

    var fixedLine = new LineLayoutOpportunity(120);
    Check(fixedLine.AvailableInlineSize == 120, $"fixed-line inline {fixedLine.AvailableInlineSize}");
}

Console.WriteLine("=== 15. FlexChildIterator (order-sorted) ===");
{
    var doc15 = new Document();
    var flex = doc15.CreateElement("div");
    var a = doc15.CreateElement("a"); a.ComputedStyle = new ComputedStyle { Order = 3, Width = new PixelLength(10) };
    var b = doc15.CreateElement("b"); b.ComputedStyle = new ComputedStyle { Order = 1, Width = new PixelLength(10) };
    var c = doc15.CreateElement("c"); c.ComputedStyle = new ComputedStyle { Order = 2, Width = new PixelLength(10) };
    flex.AppendChild(a); flex.AppendChild(b); flex.AppendChild(c);

    var iter = new FlexChildIterator(flex);
    var items = new List<string>();
    for (var el = iter.NextChild(); el != null; el = iter.NextChild())
        items.Add(el.TagName);
    Check(items.Count == 3 && items[0] == "B" && items[1] == "C" && items[2] == "A",
        $"order: {string.Join("->", items)} (expect B->C->A)");
}

Console.WriteLine("=== 16. FlexData + FlexItemIterator ===");
{
    var doc16 = new Document();
    var img = doc16.CreateElement("img");
    img.ComputedStyle = new ComputedStyle();
    var layoutBox = new LayoutBox { Dimensions = new BoxDimensions { Style = img.ComputedStyle, Element = img } };
    var bn = new BlockNode(layoutBox);
    var line = new FlexLine(3);
    var item1 = new FlexItem(bn) { MainAxisFinalSize = 100, Offset = new FlexOffset(0, 0) };
    var item2 = new FlexItem(bn) { MainAxisFinalSize = 200, Offset = new FlexOffset(100, 0) };
    var item3 = new FlexItem(bn) { MainAxisFinalSize = 150, Offset = new FlexOffset(300, 0) };
    line.Items.Add(item1); line.Items.Add(item2); line.Items.Add(item3);
    line.MainAxisFreeSpace = -50;
    line.LineCrossSize = 80;

    Check(line.Items.Count == 3, "FlexLine has 3 items");
    Check(line.MainAxisFreeSpace == -50, "main axis free space -50");
    Check(line.LineCrossEnd == 80, "line cross end = 80");

    var iter = new FlexItemIterator(new List<FlexLine> { line }, false);
    int count = 0;
    while (true)
    {
        var entry = iter.NextItem();
        if (entry.Item == null) break;
        count++;
    }
    Check(count == 3, $"FlexItemIterator yields {count} items (expect 3)");

    // FlexOffset operators
    Check((new FlexOffset(5, 10) + new FlexOffset(3, 4)).MainAxisOffset == 8, "FlexOffset +");
    Check(new FlexOffset(5, 10).TransposedOffset().MainAxisOffset == 10, "FlexOffset transposed");
}

Console.WriteLine("=== 17. FlexLayoutAlgorithm produces FlexLine ===");
{
    var doc17 = new Document();
    var flex = doc17.CreateElement("div");
    flex.ComputedStyle = new ComputedStyle
    {
        Display = DisplayType.Flex,
        Width = new PixelLength(300),
        Height = new PixelLength(100),
        FlexDirection = FlexDirectionType.Row,
    };
    var a = doc17.CreateElement("a");
    a.ComputedStyle = new ComputedStyle { Width = new PixelLength(50), Height = new PixelLength(30) };
    var b = doc17.CreateElement("b");
    b.ComputedStyle = new ComputedStyle { Width = new PixelLength(80), Height = new PixelLength(30) };
    flex.AppendChild(a); flex.AppendChild(b);

    var flexAlgo = new FlexLayoutAlgorithm(flex, ConstraintSpace.Builder(300, 100).ToConstraintSpace());
    flexAlgo.Layout();
    Check(flexAlgo.Lines.Count == 1, $"Lines count = {flexAlgo.Lines.Count} (expect 1)");
    if (flexAlgo.Lines.Count > 0)
    {
        var line = flexAlgo.Lines[0];
        Check(line.Items.Count == 2, $"line items = {line.Items.Count} (expect 2)");
        var iter = new FlexItemIterator(flexAlgo.Lines, false);
        int cnt = 0;
        while (true)
        {
            var entry = iter.NextItem();
            if (entry.Item == null) break;
            cnt++;
        }
        Check(cnt == 2, $"FlexItemIterator over algo lines yields {cnt} items (expect 2)");
    }
}

Console.WriteLine("=== 18. MeasureCache LRU ===");
{
    var doc18 = new Document();
    var el = doc18.CreateElement("div");
    var cache = new MeasureCache();

    var cs1 = ConstraintSpace.Builder(100, 200).SetBfcBlockOffset(0).ToConstraintSpace();
    var r1 = new LayoutResult { IntrinsicBlockSize = 50, ConstraintSpaceForCaching = cs1 };
    cache.Add(r1);

    var cs2 = ConstraintSpace.Builder(100, 200).SetBfcBlockOffset(10).ToConstraintSpace();
    var r2 = new LayoutResult { IntrinsicBlockSize = 55, ConstraintSpaceForCaching = cs2 };
    cache.Add(r2);

    // Probe with same space as cs1 → should hit.
    var probe = ConstraintSpace.Builder(100, 200).SetBfcBlockOffset(0).ToConstraintSpace();
    var hit = cache.Find(new BlockNode(null), probe);
    Check(hit != null && hit.IntrinsicBlockSize == 50, "MeasureCache hit (cs1)");

    // Probe with different available size → miss.
    var missSpace = ConstraintSpace.Builder(200, 200).SetBfcBlockOffset(0).ToConstraintSpace();
    var miss = cache.Find(new BlockNode(null), missSpace);
    Check(miss == null, "MeasureCache miss (different inline size)");

    // Last entry is cs1 (moved to back by Find).
    var last = cache.GetLastForTesting();
    Check(last != null && last.IntrinsicBlockSize == 50, "MeasureCache LRU: last is cs1 (moved by Find)");
    Check(cache.GetLastForTesting() == last, "MeasureCache GetLastForTesting same reference");
}

Console.WriteLine("=== 19. LogicalLineContainer ===");
{
    var doc19 = new Document();
    var span = doc19.CreateElement("span");
    span.TextContent = "hello world";
    span.ComputedStyle = new ComputedStyle { FontSize = 16 };

    // Build a container from the inline pipeline.
    var inlineNode = new InlineNode(span, span.ComputedStyle);
    inlineNode.CollectInlineItems();
    var lines = new LineBreaker().BreakLines(inlineNode.ItemsData, 200);
    var stateStack = new InlineLayoutStateStack();
    var lineBuilder = new LogicalLineBuilder(inlineNode, new ConstraintSpace(), null, stateStack);

    var container = new LogicalLineContainer();
    foreach (var info in lines)
    {
        info.AvailableInlineSize = 200;
        var logicalLineItems = new LogicalLineItems();
        lineBuilder.CreateLine(info, logicalLineItems, null);
        foreach (var item in logicalLineItems)
            container.BaseLine.AddChild(item);
    }

    Check(container.BaseLine.Count > 0, "LogicalLineContainer has base line items");
    Check(container.EstimatedFragmentItemCount() == container.BaseLine.Count, "EstimatedFragmentItemCount = base line count");
    Check(container.AnnotationLineList.Count == 0, "no annotation lines (no ruby)");

    // Test MoveInBlockDirection.
    float before = container.BaseLine.Count > 0 ? container.BaseLine[0].BlockOffset : 0;
    container.MoveInBlockDirection(10);
    float after = container.BaseLine.Count > 0 ? container.BaseLine[0].BlockOffset : 0;
    Check(Math.Abs(after - before - 10) < 0.01f, "MoveInBlockDirection shifts base line items");

    // Test Shrink and Clear.
    container.Shrink();
    Check(container.BaseLine.Count == 0, "Shrink clears base line");
    container.Clear();
    Check(container.AnnotationLineList.Count == 0, "Clear clears annotations");
}

Console.WriteLine("=== 20. TableGroupedChildren + TableChildIterator ===");
{
    var doc20 = new Document();
    var table = doc20.CreateElement("table");
    var capTop = doc20.CreateElement("caption");
    capTop.ComputedStyle = new ComputedStyle { Display = DisplayType.TableCaption, CaptionSide = "top" };
    var col = doc20.CreateElement("colgroup");
    col.ComputedStyle = new ComputedStyle { Display = DisplayType.TableColumnGroup };
    var thead = doc20.CreateElement("thead");
    thead.ComputedStyle = new ComputedStyle { Display = DisplayType.TableHeaderGroup };
    var tbody = doc20.CreateElement("tbody");
    tbody.ComputedStyle = new ComputedStyle { Display = DisplayType.TableRowGroup };
    var tfoot = doc20.CreateElement("tfoot");
    tfoot.ComputedStyle = new ComputedStyle { Display = DisplayType.TableFooterGroup };
    var capBottom = doc20.CreateElement("caption");
    capBottom.ComputedStyle = new ComputedStyle { Display = DisplayType.TableCaption, CaptionSide = "bottom" };
    table.AppendChild(capTop); table.AppendChild(col);
    table.AppendChild(thead); table.AppendChild(tbody); table.AppendChild(tfoot);
    table.AppendChild(capBottom);

    var grouped = new TableGroupedChildren(table);
    Check(grouped.Captions.Count == 2, $"captions = {grouped.Captions.Count} (expect 2)");
    Check(grouped.Columns.Count == 1, $"columns = {grouped.Columns.Count} (expect 1)");
    Check(grouped.Header == thead, "header = thead");
    Check(grouped.Footer == tfoot, "footer = tfoot");
    Check(grouped.Bodies.Count == 1 && grouped.Bodies[0] == tbody, "bodies = [tbody]");

    // Section iterator order: thead → tbody → tfoot.
    var sections = new List<string>();
    foreach (var s in grouped.Begin().Sections())
        sections.Add(s.TagName);
    Check(sections.Count == 3 && sections[0] == "THEAD" && sections[1] == "TBODY" && sections[2] == "TFOOT",
        $"sections: {string.Join("->", sections)} (expect THEAD->TBODY->TFOOT)");

    // Child iterator order: top caption → sections → bottom caption.
    var iter = new TableChildIterator(table);
    var order = new List<string>();
    while (true)
    {
        var entry = iter.NextChild();
        if (!entry) break;
        order.Add(entry.Element!.TagName);
    }
    Check(order.Count == 5 && order[0] == "CAPTION" && order[1] == "THEAD" && order[2] == "TBODY" && order[3] == "TFOOT" && order[4] == "CAPTION",
        $"child order: {string.Join("->", order)} (expect CAPTION->THEAD->TBODY->TFOOT->CAPTION)");
}

Console.WriteLine("=== 21. AuroraFragmentConverter (modern pipeline → Dom.LayoutBox) ===");
{
    var doc21 = new Document();
    var container = doc21.CreateElement("div");
    container.ComputedStyle = new ComputedStyle
    {
        Width = new PixelLength(300),
        Height = new PixelLength(100),
        PaddingLeft = new PixelLength(10),
        PaddingTop = new PixelLength(5),
        // A border width is only *used* when the edge has a style: per CSS,
        // border-style:none collapses the used width to 0 (LengthUtils.UsedBorderWidth).
        // The fixture used to set the widths alone, so the box came out with no border
        // and the assertions below failed while the engine was right.
        BorderLeftWidth = 2,
        BorderTopWidth = 1,
        BorderLeftStyle = BorderStyle.Solid,
        BorderTopStyle = BorderStyle.Solid,
    };
    var child = doc21.CreateElement("div");
    child.ComputedStyle = new ComputedStyle
    {
        Width = new PixelLength(100),
        Height = new PixelLength(40),
        MarginTop = new PixelLength(8),
    };
    container.AppendChild(child);

    var cs = ConstraintSpace.Builder(300, 100).ToConstraintSpace();
    var result = new BlockLayoutAlgorithm(container, cs).Layout();
    var frag = result.Fragment;
    Console.WriteLine($"  parent frag: offset=({frag.InlineOffset},{frag.BlockOffset}) size=({frag.InlineSize}x{frag.BlockSize})");
    Console.WriteLine($"    border=({frag.BorderLeft},{frag.BorderTop},{frag.BorderRight},{frag.BorderBottom})");
    Console.WriteLine($"    padding=({frag.PaddingLeft},{frag.PaddingTop},{frag.PaddingRight},{frag.PaddingBottom})");
    if (frag.Children.Count > 0)
    {
        var cf = frag.Children[0];
        Console.WriteLine($"  child frag: offset=({cf.InlineOffset},{cf.BlockOffset}) size=({cf.InlineSize}x{cf.BlockSize})");
        Console.WriteLine($"    margin=({cf.MarginLeft},{cf.MarginTop},{cf.MarginRight},{cf.MarginBottom})");
    }

    var layoutBox = AuroraFragmentConverter.ToLayoutBox(frag, container);
    Console.WriteLine($"  parent content: left={layoutBox.ContentBox.Left:F1} top={layoutBox.ContentBox.Top:F1} w={layoutBox.ContentBox.Width:F1} h={layoutBox.ContentBox.Height:F1}");
    if (layoutBox.Children.Count > 0)
    {
        var cb = layoutBox.Children[0];
        Console.WriteLine($"  child: border=({cb.BorderBox.Left:F1},{cb.BorderBox.Top:F1},{cb.BorderBox.Width:F1}x{cb.BorderBox.Height:F1})");
        Console.WriteLine($"  child: margin=({cb.MarginBox.Left:F1},{cb.MarginBox.Top:F1},{cb.MarginBox.Width:F1}x{cb.MarginBox.Height:F1})");
    }

    Check(Math.Abs(layoutBox.ContentBox.Left - 12) < 0.01f, $"content left = {layoutBox.ContentBox.Left:F1} (expect 12 = 2+10)");
    Check(Math.Abs(layoutBox.ContentBox.Top - 6) < 0.01f, $"content top = {layoutBox.ContentBox.Top:F1} (expect 6 = 1+5)");
    Check(layoutBox.Children.Count == 1, $"children count = {layoutBox.Children.Count} (expect 1)");
    if (layoutBox.Children.Count == 1)
    {
        var childBox = layoutBox.Children[0];
        Check(Math.Abs(childBox.BorderBox.Left - 12) < 0.01f, $"child border left = {childBox.BorderBox.Left:F1} (expect 12)");
        // The parent has border+padding, so the child's block-start margin does not
        // collapse through: its margin box starts at the parent's content edge (6)
        // and the border box sits 8 lower (14).
        Check(Math.Abs(childBox.MarginBox.Top - 6) < 0.01f, $"child margin top = {childBox.MarginBox.Top:F1} (expect 6 = parent content top)");
    }
}

Console.WriteLine("=== 22. LayoutEngine.LayoutAurora (end-to-end modern pipeline) ===");
{
    var doc22 = new Document();
    var html = doc22.CreateElement("html");
    var body = doc22.CreateElement("body");
    body.ComputedStyle = new ComputedStyle { FontSize = 16, Width = new PixelLength(400) };
    var div = doc22.CreateElement("div");
    div.ComputedStyle = new ComputedStyle { Width = new PixelLength(200), Height = new PixelLength(50), PaddingLeft = new PixelLength(8) };
    var span = doc22.CreateElement("span");
    span.ComputedStyle = new ComputedStyle { FontSize = 14 };
    var text = doc22.CreateTextNode("Hello NG");

    span.AppendChild(text);
    div.AppendChild(span);
    body.AppendChild(div);
    html.AppendChild(body);
    doc22.AppendChild(html);
    doc22.DocumentElement = html;

    var engine = new LayoutEngine();
    engine.LayoutAurora(doc22, 400, 600);
    Console.WriteLine($"  after LayoutAurora: html.LayoutBox={html.LayoutBox != null} body.LayoutBox={body.LayoutBox != null} div.LayoutBox={div.LayoutBox != null}");
    if (html.LayoutBox != null)
        Console.WriteLine($"    html children={html.LayoutBox.Children.Count}");
    if (body.LayoutBox != null)
        Console.WriteLine($"    body children={body.LayoutBox.Children.Count}");

    // Check that layout boxes were assigned to all elements.
    Check(html.LayoutBox != null, "html has LayoutBox");
    Check(body.LayoutBox != null, "body has LayoutBox");
    Check(div.LayoutBox != null, "div has LayoutBox");
    Check(span.LayoutBox != null, "span has LayoutBox");

    if (div.LayoutBox != null)
    {
        // div has 200 width, 50 height, 8 padding-left.
        Check(Math.Abs(div.LayoutBox.ContentBox.Width - 200) < 0.01f, $"div content width = {div.LayoutBox.ContentBox.Width:F1} (expect 200)");
        // The span should be inside the div's content area.
        Check(span.LayoutBox != null && span.LayoutBox.BorderBox.Left >= div.LayoutBox.ContentBox.Left,
            $"span border left = {span.LayoutBox?.BorderBox.Left:F1} >= div content left = {div.LayoutBox.ContentBox.Left:F1}");
    }

    Check(engine.ContentHeight > 0, $"contentHeight = {engine.ContentHeight:F0} (expect > 0)");
}

Console.WriteLine("=== 23. BlockLayoutAlgorithm dispatches flex/grid/table ===");
{
    var doc23 = new Document();
    var html23 = doc23.CreateElement("html");
    var body23 = doc23.CreateElement("body");
    body23.ComputedStyle = new ComputedStyle { FontSize = 16, Width = new PixelLength(400) };

    var flexContainer = doc23.CreateElement("div");
    flexContainer.ComputedStyle = new ComputedStyle { Display = DisplayType.Flex, Width = new PixelLength(300), Height = new PixelLength(50) };
    var flexItem = doc23.CreateElement("span");
    flexItem.ComputedStyle = new ComputedStyle { Width = new PixelLength(100), Height = new PixelLength(30) };
    flexContainer.AppendChild(flexItem);

    var gridContainer = doc23.CreateElement("div");
    gridContainer.ComputedStyle = new ComputedStyle { Display = DisplayType.Grid, Width = new PixelLength(300), Height = new PixelLength(50) };
    var gridItem = doc23.CreateElement("span");
    gridItem.ComputedStyle = new ComputedStyle { Width = new PixelLength(50), Height = new PixelLength(20) };
    gridContainer.AppendChild(gridItem);

    body23.AppendChild(flexContainer);
    body23.AppendChild(gridContainer);
    html23.AppendChild(body23);
    doc23.AppendChild(html23);
    doc23.DocumentElement = html23;

    var engine23 = new LayoutEngine();
    engine23.LayoutAurora(doc23, 400, 600);
    Check(flexContainer.LayoutBox != null, "flex container has LayoutBox");
    Check(flexItem.LayoutBox != null, "flex item has LayoutBox (was dispatched to FlexLayoutAlgorithm)");
    Check(gridContainer.LayoutBox != null, "grid container has LayoutBox");
    Check(gridItem.LayoutBox != null, "grid item has LayoutBox (was dispatched to GridLayoutAdapter)");
    if (flexItem.LayoutBox != null)
        Check(flexItem.LayoutBox.BorderBox.Width > 0, $"flex item width = {flexItem.LayoutBox.BorderBox.Width:F1} (expect > 0)");
}

Console.WriteLine("=== 23b. Block-level sibling replaced elements stack ===");
{
    // Regression for the block-flow bug where a replaced child (display:block
    // INPUT / TEXTAREA / IMG) handed back an unresolved BFC block-offset: siblings
    // all landed at block-offset 0 (only the last stayed visible) and their parent
    // was displaced by one child height while collapsing to that same height.
    var doc23b = HtmlDocumentParserIntegration.ParseHtml(
        "<!DOCTYPE html><html><body style='margin:0'>" +
        "<input type='text' style='display:block;width:200px;height:30px;background:#ff0000'>" +
        "<textarea style='display:block;width:200px;height:30px;background:#00ff00'></textarea>" +
        "<img style='display:block;width:200px;height:30px' src='missing.png'>" +
        "</body></html>");
    new StyleComputer().ComputeStyles(doc23b, 400, 600);
    new LayoutEngine().LayoutAurora(doc23b, 400, 600);

    var body23b = doc23b.Body!;
    var siblings = body23b.Children.OfType<Element>()
        .Where(e => e.LayoutBox != null && e.TagName is "INPUT" or "TEXTAREA" or "IMG")
        .ToList();
    Check(siblings.Count == 3, $"three block-level siblings boxed (got {siblings.Count})");

    bool stacked = siblings.Count == 3;
    float cursor = 0;
    foreach (var s in siblings)
    {
        var bb = s.LayoutBox!.BorderBox;
        if (Math.Abs(bb.Top - cursor) > 0.5f || bb.Height <= 0) stacked = false;
        cursor = bb.Top + bb.Height;
    }
    Check(stacked, $"block-level siblings stack without sharing a rect (contentEnd={cursor:F1})");

    var bodyBox23b = body23b.LayoutBox!;
    Check(Math.Abs(bodyBox23b.BorderBox.Top) < 0.5f && bodyBox23b.BorderBox.Height >= cursor - 0.5f,
        $"body covers every sibling and stays at y=0: top={bodyBox23b.BorderBox.Top:F1} " +
        $"height={bodyBox23b.BorderBox.Height:F1} contentEnd={cursor:F1}");
}

Console.WriteLine("=== 24. AbsoluteUtils IMCB + OOF dimensions ===");
{
    var doc24 = new Document();
    var style = new ComputedStyle
    {
        Position = PositionType.Absolute,
        Left = new PixelLength(10),
        Top = new PixelLength(20),
        Width = new PixelLength(150),
        Height = new PixelLength(80),
    };
    var available = new LogicalSize(400, 300);

    // Resolve insets.
    var insets = AbsoluteUtils.ResolveOutOfFlowInsets(style, available);
    Check(insets.InlineStart == 10 && insets.BlockStart == 20, "resolved insets (left=10, top=20)");
    Check(insets.InlineEnd == null, "right inset is auto (null)");

    // IMCB with auto right/bottom.
    var sp = new LogicalStaticPosition(new LogicalOffset(5, 5),
        LogicalStaticPosition.StaticInlinePosition.Left,
        LogicalStaticPosition.StaticBlockPosition.Top,
        WritingDirectionMode.HorizontalLtr);
    var imcb = AbsoluteUtils.ComputeInsetModifiedContainingBlock(available, insets, sp);
    Check(Math.Abs(imcb.InlineSize() - 390) < 0.01f, $"IMCB inline = {imcb.InlineSize():F1} (expect 390 = 400-10)");
    Check(imcb.HasAutoInlineInset, "IMCB has auto inline inset (right auto)");

    // OOF inline dimensions.
    var inlineDims = AbsoluteUtils.ComputeOofInlineDimensions(style, imcb, new BoxStrut(0, 0, 0, 0));
    Check(Math.Abs(inlineDims.Size.InlineSize - 150) < 0.01f, $"OOF inline size = {inlineDims.Size.InlineSize:F1} (expect 150)");

    // OOF block dimensions.
    var blockDims = AbsoluteUtils.ComputeOofBlockDimensions(style, imcb, new BoxStrut(0, 0, 0, 0));
    Check(Math.Abs(blockDims.Size.BlockSize - 80) < 0.01f, $"OOF block size = {blockDims.Size.BlockSize:F1} (expect 80)");

    // Percent-based width.
    var pctStyle = new ComputedStyle { Position = PositionType.Absolute, Width = new PercentLength(0.5f) };
    var pctInsets = AbsoluteUtils.ResolveOutOfFlowInsets(pctStyle, available);
    var pctImcb = AbsoluteUtils.ComputeInsetModifiedContainingBlock(available, pctInsets, sp);
    var pctDims = AbsoluteUtils.ComputeOofInlineDimensions(pctStyle, pctImcb, new BoxStrut(0, 0, 0, 0));
    Check(Math.Abs(pctDims.Size.InlineSize - 200) < 0.01f, $"percent inline = {pctDims.Size.InlineSize:F1} (expect 200 = 50% of 400)");

    // Min-width clamp.
    var minStyle = new ComputedStyle { Position = PositionType.Absolute, Width = new PixelLength(50), MinWidth = new PixelLength(100) };
    var minImcb = AbsoluteUtils.ComputeInsetModifiedContainingBlock(available,
        AbsoluteUtils.ResolveOutOfFlowInsets(minStyle, available), sp);
    var minDims = AbsoluteUtils.ComputeOofInlineDimensions(minStyle, minImcb, new BoxStrut(0, 0, 0, 0));
    Check(Math.Abs(minDims.Size.InlineSize - 100) < 0.01f, $"min-width clamp = {minDims.Size.InlineSize:F1} (expect 100)");
}

Console.WriteLine("=== 25. HitTestCache ===");
{
    var cache = new HitTestCache();
    var loc = new HitTestLocation(50, 100);
    var req = new HitTestRequest();
    var res = new HitTestResult(req, loc);
    var doc25 = new Document();
    var root25 = doc25.CreateElement("div");
    res.SetNodeAndPosition(root25);

    // First lookup should miss.
    var probe = new HitTestLocation(50, 100);
    var probeResult = new HitTestResult(req, probe);
    bool found = cache.LookupCachedResult(probe, ref probeResult, 1);
    Check(!found, "first lookup misses (cache empty)");

    // Add and lookup.
    cache.AddCachedResult(loc, res, 1);
    var probe2 = new HitTestLocation(50, 100);
    var probeResult2 = new HitTestResult(req, probe2);
    found = cache.LookupCachedResult(probe2, ref probeResult2, 1);
    Check(found, "second lookup hits after add");
    Check(probeResult2.InnerNode == root25, "cached result has correct node");

    // Different location → miss.
    var probe3 = new HitTestLocation(99, 100);
    var probeResult3 = new HitTestResult(req, probe3);
    found = cache.LookupCachedResult(probe3, ref probeResult3, 1);
    Check(!found, "different location misses");

    // Different DOM version → miss.
    var probe4 = new HitTestLocation(50, 100);
    var probeResult4 = new HitTestResult(req, probe4);
    found = cache.LookupCachedResult(probe4, ref probeResult4, 2);
    Check(!found, "different DOM version misses");

    // Clear.
    cache.Clear();
    var probe5 = new HitTestLocation(50, 100);
    var probeResult5 = new HitTestResult(req, probe5);
    found = cache.LookupCachedResult(probe5, ref probeResult5, 1);
    Check(!found, "after clear, lookup misses");
}

Console.WriteLine("=== 26. ReplacedLayoutAlgorithm (img sizing) ===");
{
    var doc26 = new Document();
    var img = doc26.CreateElement("img");
    var natural = new PhysicalSize(800, 600);
    // Use the algorithm directly (no HTMLImageElement, just check BasicLayout).
    var cs26 = ConstraintSpace.Builder(400, 300).ToConstraintSpace();
    var algo = new ReplacedLayoutAlgorithm(img, cs26);
    var r26 = algo.Layout();
    Check(r26.Fragment.InlineSize > 0, $"replaced frag inline = {r26.Fragment.InlineSize:F0} (expect > 0)");
}

Console.WriteLine("=== 27. PointerEventsHitRules ===");
{
    var req = new HitTestRequest();
    var rules = new PointerEventsHitRules(PointerEventsHitRules.EHitTesting.SvgGeometryHitTesting, req, "none");
    Check(!rules.CanHitFill && !rules.CanHitStroke, "pointer-events: none → no hit");

    var rulesAll = new PointerEventsHitRules(PointerEventsHitRules.EHitTesting.SvgGeometryHitTesting, req, "all");
    Check(rulesAll.CanHitFill && rulesAll.CanHitStroke, "pointer-events: all → can hit");
}

Console.WriteLine("=== 28. HitTestCanvasResult ===");
{
    var doc28 = new Document();
    var el28 = doc28.CreateElement("canvas");
    var result = new HitTestCanvasResult("myCanvas", el28);
    Check(result.Id == "myCanvas", "canvas result id = 'myCanvas'");
    Check(result.Control == el28, "canvas result control = element");
}

Console.WriteLine("=== 29. ListMarker (list items) ===");
{
    var doc29 = new Document();
    var list = doc29.CreateElement("ol");
    list.ComputedStyle = new ComputedStyle { FontSize = 16 };
    var item1 = doc29.CreateElement("li");
    item1.ComputedStyle = new ComputedStyle { Display = DisplayType.ListItem, ListStyleType = ListStyleType.Decimal, FontSize = 16 };
    var item2 = doc29.CreateElement("li");
    item2.ComputedStyle = new ComputedStyle { Display = DisplayType.ListItem, ListStyleType = ListStyleType.Decimal, FontSize = 16 };
    list.AppendChild(item1); list.AppendChild(item2);

    // Direct test of ListMarker utility.
    Check(ListMarker.MarkerText(ListStyleType.Decimal, 1) == "1.", "list marker '1.'");
    Check(ListMarker.MarkerText(ListStyleType.Decimal, 5) == "5.", "list marker '5.'");
    Check(ListMarker.MarkerText(ListStyleType.LowerAlpha, 1) == "a.", "list marker 'a.'");
    Check(ListMarker.MarkerText(ListStyleType.LowerAlpha, 26) == "z.", "list marker 'z.'");
    Check(ListMarker.MarkerText(ListStyleType.LowerRoman, 4) == "iv.", "list marker 'iv.'");
    Check(ListMarker.MarkerText(ListStyleType.Disc, 1) == "•", "list marker bullet");

    // Test via BlockLayoutAlgorithm.
    var cs29 = ConstraintSpace.Builder(300, 200).ToConstraintSpace();
    var r29 = new BlockLayoutAlgorithm(item1, cs29).Layout();
    bool hasMarker = r29.Fragment.Lines.Any(l => l.Runs.Any(r => r.Text == "1."));
    Check(hasMarker, $"list item 1 has marker line: {hasMarker}");

    var r29b = new BlockLayoutAlgorithm(item2, cs29).Layout();
    bool hasMarker2 = r29b.Fragment.Lines.Any(l => l.Runs.Any(r => r.Text == "2."));
    Check(hasMarker2, $"list item 2 has marker line: {hasMarker2}");
}

Console.WriteLine("=== 30. LayoutEngine single modern pipeline ===");
{
    var doc30 = new Document();
    var html30 = doc30.CreateElement("html");
    html30.ComputedStyle = new ComputedStyle { Width = new PixelLength(400) };
    var body30 = doc30.CreateElement("body");
    body30.ComputedStyle = new ComputedStyle { FontSize = 16, Width = new PixelLength(400) };
    var div30 = doc30.CreateElement("div");
    div30.ComputedStyle = new ComputedStyle { Width = new PixelLength(200), Height = new PixelLength(50) };
    body30.AppendChild(div30);
    html30.AppendChild(body30);
    doc30.AppendChild(html30);
    doc30.DocumentElement = html30;

    // There is a single layout path: the modern pipeline is the only one.
    var engine30 = new LayoutEngine();
    engine30.Layout(doc30, 400, 600);
    Check(div30.LayoutBox != null, "modern: div has LayoutBox");
    if (div30.LayoutBox != null)
        Check(div30.LayoutBox.BorderBox.Width > 0, "modern: div has positive width");
}

Console.WriteLine("=== 31. Auto margins block centering ===");
{
    var doc31 = new Document();
    var container31 = doc31.CreateElement("div");
    container31.ComputedStyle = new ComputedStyle { Width = new PixelLength(400) };
    var centered31 = doc31.CreateElement("div");
    centered31.ComputedStyle = new ComputedStyle
    {
        Width = new PixelLength(200),
        Height = new PixelLength(50),
        MarginLeft = new AutoLength(),
        MarginRight = new AutoLength(),
    };
    container31.AppendChild(centered31);

    var cs31 = ConstraintSpace.Builder(400, 100).ToConstraintSpace();
    var result31 = new BlockLayoutAlgorithm(container31, cs31).Layout();
    var childBox = result31.Fragment.Children.FirstOrDefault();
    // Auto margins: (400 - 200) / 2 = 100 each side.
    Check(childBox != null && Math.Abs(childBox.MarginLeft - 100) < 0.01f,
        $"auto margin-left = {childBox?.MarginLeft:F0} (expect 100)");
    if (childBox != null)
        Check(Math.Abs(childBox.MarginRight - 100) < 0.01f, $"auto margin-right = {childBox.MarginRight:F0} (expect 100)");
}

Console.WriteLine("=== 32. FragmentationUtils break precedence ===");
{
    Check(FragmentationUtils.JoinFragmentainerBreakValues("auto", "page") == "page", "join auto+page = page");
    Check(FragmentationUtils.JoinFragmentainerBreakValues("page", "auto") == "page", "join page+auto = page");
    Check(FragmentationUtils.JoinFragmentainerBreakValues("avoid", "column") == "column", "join avoid+column = column (forced wins)");
    Check(FragmentationUtils.JoinFragmentainerBreakValues("avoid-column", "avoid-page") == "avoid-page", "avoid-page > avoid-column");
    Check(FragmentationUtils.JoinFragmentainerBreakValues("page", "left") == "left", "left > page");
    Check(FragmentationUtils.FragmentainerBreakPrecedence("auto") == 0, "auto precedence = 0");
    Check(FragmentationUtils.FragmentainerBreakPrecedence("left") == 7, "left precedence = 7");
}

Console.WriteLine("=== 33. WritingModeConverter ===");
{
    var outer = new PhysicalSize(200, 100);
    // Horizontal LTR: logical = physical.
    var conv = new WritingModeConverter(WritingDirectionMode.HorizontalLtr, outer);
    var log = conv.ToLogical(new PhysicalOffset(10, 20), new PhysicalSize(50, 30));
    Check(log.InlineOffset == 10 && log.BlockOffset == 20, "LTR: logical offset = physical offset");

    // Horizontal RTL: inline is mirrored.
    var convRtl = new WritingModeConverter(WritingDirectionMode.HorizontalRtl, outer);
    var logRtl = convRtl.ToLogical(new PhysicalOffset(10, 20), new PhysicalSize(50, 30));
    Check(Math.Abs(logRtl.InlineOffset - 140) < 0.01f, $"RTL: inline = {logRtl.InlineOffset:F1} (expect 140 = 200-10-50)");
    Check(logRtl.BlockOffset == 20, "RTL: block = 20");

    // Round-trip.
    var phys = convRtl.ToPhysical(logRtl, new PhysicalSize(50, 30));
    Check(Math.Abs(phys.Left - 10) < 0.01f && Math.Abs(phys.Top - 20) < 0.01f, "RTL round-trip");

    // Vertical LTR (VerticalRl): inline maps to y, block maps to mirrored x.
    var convV = new WritingModeConverter(WritingDirectionMode.VerticalLtr, outer);
    var logV = convV.ToLogical(new PhysicalOffset(10, 20), new PhysicalSize(30, 50));
    Check(logV.InlineOffset == 20, $"vertical LTR inline = {logV.InlineOffset:F0} (expect 20 = top)");
    Check(Math.Abs(logV.BlockOffset - 160) < 0.01f, $"vertical LTR block = {logV.BlockOffset:F0} (expect 160 = 200-10-30)");

    // Size conversion.
    var logSize = conv.ToLogical(new PhysicalSize(100, 50));
    Check(logSize.InlineSize == 100 && logSize.BlockSize == 50, "LTR: logical size = physical size");
    var logSizeV = convV.ToLogical(new PhysicalSize(100, 50));
    Check(logSizeV.InlineSize == 50 && logSizeV.BlockSize == 100, "vertical: logical size swapped");
}

Console.WriteLine("=== 34. ScrollOffsetRange ===");
{
    var range = new PhysicalScrollRange(0, 100, 0, 200);
    Check(range.Contains(50, 100), "point (50,100) in range");
    Check(!range.Contains(150, 100), "point (150,100) out of range");
    Check(!range.Contains(50, 300), "point (50,300) out of range");

    var logical = new LogicalScrollRange(0, 100, 0, 200);
    var phys = logical.ToPhysical(WritingDirectionMode.HorizontalLtr);
    Check(phys.XMin == 0 && phys.YMax == 200, "LTR: logical = physical");

    // RTL: x bounds are reversed.
    var physRtl = logical.ToPhysical(WritingDirectionMode.HorizontalRtl);
    Check(physRtl.XMin == -100 && physRtl.XMax == 0, $"RTL x: min={physRtl.XMin} max={physRtl.XMax} (expect -100, 0)");
}

Console.WriteLine("=== 35. Details/summary (closed) ===");
{
    var doc35 = new Document();
    var details = doc35.CreateElement("details");
    details.ComputedStyle = new ComputedStyle { Width = new PixelLength(300) };
    var summary = doc35.CreateElement("summary");
    summary.ComputedStyle = new ComputedStyle { Height = new PixelLength(20) };
    var hidden = doc35.CreateElement("div");
    hidden.ComputedStyle = new ComputedStyle { Height = new PixelLength(50) };
    details.AppendChild(summary);
    details.AppendChild(hidden);

    // Closed details: only summary should be laid out.
    var cs35 = ConstraintSpace.Builder(300, 200).ToConstraintSpace();
    var r35 = new BlockLayoutAlgorithm(details, cs35).Layout();
    Check(r35.Fragment.Children.Count == 1, $"closed details: children={r35.Fragment.Children.Count} (expect 1, only summary)");
    if (r35.Fragment.Children.Count == 1)
        Check(r35.Fragment.Children[0].Element == summary, "first child is summary");

    // Open details: both should be laid out.
    details.SetAttribute("open", "");
    var r35b = new BlockLayoutAlgorithm(details, cs35).Layout();
    Check(r35b.Fragment.Children.Count == 2, $"open details: children={r35b.Fragment.Children.Count} (expect 2)");
}

Console.WriteLine("=== 36. DisableLayoutSideEffectsScope ===");
{
    Check(!DisableLayoutSideEffectsScope.IsDisabled, "scope disabled by default");
    using (var scope = new DisableLayoutSideEffectsScope())
    {
        Check(DisableLayoutSideEffectsScope.IsDisabled, "scope enabled inside using");
        using (var inner = new DisableLayoutSideEffectsScope())
        {
            Check(DisableLayoutSideEffectsScope.IsDisabled, "scope still enabled (nested)");
        }
        Check(DisableLayoutSideEffectsScope.IsDisabled, "scope enabled after inner dispose");
    }
    Check(!DisableLayoutSideEffectsScope.IsDisabled, "scope disabled after outer dispose");
}

Console.WriteLine("=== 37. ExclusionSpace (NG-aligned) ===");
{
    var space = new ExclusionSpace();
    // Add a left float at (10, 0) size 100x50.
    space.Add(ExclusionArea.Create(
        new BfcRect(new BfcOffset(10, 0), new BfcOffset(110, 50)),
        FloatType.Left, false));
    // Add a right float at (200, 0) size 150x80.
    space.Add(ExclusionArea.Create(
        new BfcRect(new BfcOffset(200, 0), new BfcOffset(350, 80)),
        FloatType.Right, false));

    Check(Math.Abs(space.ClearanceOffset(ClearType.Left) - 50) < 0.01f, "left clearance = 50");
    Check(Math.Abs(space.ClearanceOffset(ClearType.Right) - 80) < 0.01f, "right clearance = 80");
    Check(Math.Abs(space.ClearanceOffset(ClearType.Both) - 80) < 0.01f, "both clearance = 80");

    // Line at block offset 0: left float pushes line start to 110.
    var opp = space.FindLayoutOpportunity(new BfcOffset(0, 0), 400);
    Check(Math.Abs(opp.Rect.LineStartOffset - 110) < 0.01f, $"line start = {opp.Rect.LineStartOffset:F0} (expect 110, past left float)");

    // Line below the floats (block offset 100): full width.
    var oppBelow = space.FindLayoutOpportunity(new BfcOffset(0, 100), 400);
    Check(Math.Abs(oppBelow.Rect.LineStartOffset - 0) < 0.01f, "line below floats: start = 0");

    // Initial letter box clearance.
    var space2 = new ExclusionSpace();
    space2.Add(ExclusionArea.CreateForInitialLetterBox(
        new BfcRect(new BfcOffset(0, 0), new BfcOffset(40, 60)), FloatType.Left, false));
    Check(Math.Abs(space2.ClearanceOffsetIncludingInitialLetter(ClearType.Left) - 60) < 0.01f, "initial letter clearance = 60");
}

Console.WriteLine("=== 38. LogicalBoxFragment baselines ===");
{
    var phys = new PhysicalBoxFragment
    {
        Size = new PhysicalSize(200, 100),
        HasBaseline = true,
        FirstBaseline = 80,
        LastBaseline = 20,
    };
    var logical = new LogicalBoxFragment(WritingDirectionMode.HorizontalLtr, phys);
    Check(Math.Abs(logical.InlineSize - 200) < 0.01f, "inline size = 200");
    Check(Math.Abs(logical.BlockSize - 100) < 0.01f, "block size = 100");

    var fb = logical.FirstBaseline();
    Check(fb.HasValue && Math.Abs(fb.Value - 80) < 0.01f, "first baseline = 80");

    var fbSynth = logical.FirstBaselineOrSynthesize(true);
    Check(Math.Abs(fbSynth - 80) < 0.01f, "first baseline (or synth) = 80");

    // Without baseline, synthesized.
    var physNoBase = new PhysicalBoxFragment { Size = new PhysicalSize(200, 100), HasBaseline = false };
    var logicalNoBase = new LogicalBoxFragment(WritingDirectionMode.HorizontalLtr, physNoBase);
    var synth = logicalNoBase.FirstBaselineOrSynthesize(true);
    Check(Math.Abs(synth - 100) < 0.01f, $"synthesized alphabetic baseline = {synth:F0} (expect 100 = blockSize)");

    var synthCtr = logicalNoBase.FirstBaselineOrSynthesize(false);
    Check(Math.Abs(synthCtr - 50) < 0.01f, $"synthesized center baseline = {synthCtr:F0} (expect 50 = blockSize/2)");

    var lb = logical.LastBaseline();
    Check(lb.HasValue && Math.Abs(lb.Value - 20) < 0.01f, "last baseline = 20");
}

Console.WriteLine("=== 39. Form control sizing ===");
{
    var doc39 = new Document();
    var input = doc39.CreateElement("input");
    input.SetAttribute("type", "text");
    input.ComputedStyle = new ComputedStyle { FontSize = 16 };
    var cs39 = ConstraintSpace.Builder(400, 100).ToConstraintSpace();
    var r39 = new ReplacedLayoutAlgorithm(input, cs39).Layout();
    Check(r39.Fragment.InlineSize > 0, $"text input inline size = {r39.Fragment.InlineSize:F0} (expect > 0)");

    var cb = doc39.CreateElement("input");
    cb.SetAttribute("type", "checkbox");
    cb.ComputedStyle = new ComputedStyle { FontSize = 16 };
    var rCb = new ReplacedLayoutAlgorithm(cb, cs39).Layout();
    Check(Math.Abs(rCb.Fragment.InlineSize - 13) < 0.01f, $"checkbox inline size = {rCb.Fragment.InlineSize:F0} (expect 13)");

    var ta = doc39.CreateElement("textarea");
    ta.ComputedStyle = new ComputedStyle { FontSize = 16 };
    ta.SetAttribute("cols", "20");
    ta.SetAttribute("rows", "3");
    var rTa = new ReplacedLayoutAlgorithm(ta, cs39).Layout();
    Check(rTa.Fragment.InlineSize > 0, $"textarea inline size = {rTa.Fragment.InlineSize:F0} (expect > 0)");

    var btn = doc39.CreateElement("button");
    btn.ComputedStyle = new ComputedStyle { FontSize = 16 };
    var rBtn = new ReplacedLayoutAlgorithm(btn, cs39).Layout();
    Check(rBtn.Fragment.InlineSize > 0, $"button inline size = {rBtn.Fragment.InlineSize:F0} (expect > 0)");
}

Console.WriteLine("=== 40. Aspect ratio sizing ===");
{
    var doc40 = new Document();
    // Element with aspect-ratio: 2 (width/height = 2) and width: 200.
    var el = doc40.CreateElement("div");
    el.ComputedStyle = new ComputedStyle
    {
        Width = new PixelLength(200),
        AspectRatio = 2f,
        FontSize = 16,
    };
    var cs40 = ConstraintSpace.Builder(400, 400).ToConstraintSpace();
    var r40 = new BlockLayoutAlgorithm(el, cs40).Layout();
    Check(Math.Abs(r40.Fragment.BlockSize - 100) < 0.01f, $"aspect-ratio 2: block size = {r40.Fragment.BlockSize:F0} (expect 100 = 200/2)");

    // Element with no width, aspect-ratio derived from available inline size.
    var el2 = doc40.CreateElement("div");
    el2.ComputedStyle = new ComputedStyle { AspectRatio = 0.5f, FontSize = 16 };
    var r40b = new BlockLayoutAlgorithm(el2, ConstraintSpace.Builder(400, 400).ToConstraintSpace()).Layout();
    Check(r40b.Fragment.BlockSize > 0, $"aspect-ratio 0.5: block size = {r40b.Fragment.BlockSize:F0} (expect > 0)");
}

Console.WriteLine("=== 41. HTML Parser: basic structure ===");
{
    var html = "<!DOCTYPE html><html><head><title>Test</title></head><body><p>Hello</p></body></html>";
    Console.WriteLine($"  parsing: {html.Substring(0, Math.Min(80, html.Length))}...");
    var dump = HtmlDocumentParserIntegration.ParseHtmlDebug(html);
    Console.WriteLine($"  DOM tree:\n{dump}");
    var doc = HtmlDocumentParserIntegration.ParseHtml(html);
    Check(doc != null, "document created");
    Check(doc.DocumentElement != null, "DocumentElement != null");
    Check(doc.Head != null, "Head != null");
    Check(doc.Body != null, "Body != null");
    if (doc.DocumentElement != null)
        Check(doc.DocumentElement.TagName == "HTML", $"DocumentElement is {doc.DocumentElement.TagName}");
    if (doc.Head != null)
        Check(doc.Head.TagName == "HEAD", $"Head is {doc.Head.TagName}");
    if (doc.Body != null)
        Check(doc.Body.TagName == "BODY", $"Body is {doc.Body.TagName}");
}

Console.WriteLine("=== 42. HTML Parser: nested elements ===");
{
    var doc = HtmlDocumentParserIntegration.ParseHtml("<div class='main'><span>text</span><p>para</p></div>");
    var div = doc.DocumentElement?.Children.FirstOrDefault(c => (c as Element)?.TagName == "BODY")?.Children.FirstOrDefault(c => (c as Element)?.TagName == "DIV");
    Check(div != null, "div found");
    if (div != null)
    {
        Check(div is Element, "div is Element");
        var divEl = (Element)div;
        Check(divEl.GetAttribute("class") == "main", "div has class='main'");
        var span = divEl.Children.FirstOrDefault(c => (c as Element)?.TagName == "SPAN");
        Check(span != null, "span inside div");
        if (span != null)
            Check(((Element)span).TextContent == "text", "span text content");
        var p = divEl.Children.FirstOrDefault(c => (c as Element)?.TagName == "P");
        Check(p != null, "p inside div");
        if (p != null)
            Check(((Element)p).TextContent == "para", "p text content");
    }
}

Console.WriteLine("=== 43. HTML Parser: attributes ===");
{
    var doc = HtmlDocumentParserIntegration.ParseHtml("<a href='http://example.com' id='link1' class='external'>Click</a>");
    var body = doc.DocumentElement?.Children.FirstOrDefault(c => (c as Element)?.TagName == "BODY");
    var link = body?.Children.FirstOrDefault(c => (c as Element)?.TagName == "A");
    Check(link != null, "a element found");
    if (link != null)
    {
        var a = (Element)link;
        Check(a.GetAttribute("href") == "http://example.com", $"href = '{a.GetAttribute("href")}'");
        Check(a.GetAttribute("id") == "link1", $"id = '{a.GetAttribute("id")}'");
        Check(a.GetAttribute("class") == "external", $"class = '{a.GetAttribute("class")}'");
        Check(a.TextContent == "Click", $"text = '{a.TextContent}'");
    }
}

Console.WriteLine("=== 44. HTML Parser: self-closing ===");
{
    var doc = HtmlDocumentParserIntegration.ParseHtml("<br><hr><img src='test.jpg'>");
    var body = doc.DocumentElement?.Children.FirstOrDefault(c => (c as Element)?.TagName == "BODY");
    int brCount = 0, hrCount = 0, imgCount = 0;
    if (body != null)
    {
        foreach (var c in ((Element)body).Children)
        {
            if (c is Element el)
            {
                if (el.TagName == "BR") brCount++;
                if (el.TagName == "HR") hrCount++;
                if (el.TagName == "IMG") imgCount++;
            }
        }
    }
    Check(brCount == 1, $"br count = {brCount} (expect 1)");
    Check(hrCount == 1, $"hr count = {hrCount} (expect 1)");
    Check(imgCount == 1, $"img count = {imgCount} (expect 1)");
    if (imgCount > 0)
    {
        var img = ((Element)body).Children.OfType<Element>().First(e => e.TagName == "IMG");
        Check(img.GetAttribute("src") == "test.jpg", $"img src = '{img.GetAttribute("src")}'");
    }
}

Console.WriteLine("=== 45. CSS: tokenizer serialization (numbers, functions, strings) ===");
{
    var parser = new CssParser();
    var sheet = parser.Parse("div { margin: 10px 2.5em; content: \"a;b\"; background: linear-gradient(to right, red 0%, blue 100%); }");
    Check(sheet.Rules.Count == 1, $"rule count = {sheet.Rules.Count} (expect 1)");
    if (sheet.Rules.Count == 1)
    {
        var props = sheet.Rules[0].Properties;
        Check(props.TryGetValue("margin", out var m) && m == "10px 2.5em", $"margin = '{m}'");
        Check(props.TryGetValue("content", out var c) && c == "\"a;b\"", $"content = '{c}'");
        Check(props.TryGetValue("background", out var bg) &&
              bg == "linear-gradient(to right, red 0%, blue 100%)", $"background = '{bg}'");
    }
}

Console.WriteLine("=== 46. CSS: strings containing ';' must not split declarations ===");
{
    var parser = new CssParser();
    var sheet = parser.Parse("p { content: \"a;b\"; color: red; }");
    Check(sheet.Rules.Count == 1, "p rule parsed");
    if (sheet.Rules.Count == 1)
    {
        Check(sheet.Rules[0].Properties.TryGetValue("color", out var color) && color == "red", $"color = '{color}'");
        Check(sheet.Rules[0].Properties.ContainsKey("content"), "content present");
    }
}

Console.WriteLine("=== 47. CSS: inline style with string semicolon ===");
{
    var parser = new CssParser();
    var inline = parser.ParseInlineStyle("content: \"a;b\"; width: 50px;");
    Check(inline.TryGetValue("width", out var w) && w == "50px", $"inline width = '{w}'");
    Check(inline.TryGetValue("content", out var c) && c == "\"a;b\"", $"inline content = '{c}'");
}

Console.WriteLine("=== 48. CSS: @media parse + nested at-rule ===");
{
    var parser = new CssParser();
    var sheet = parser.Parse("@media (min-width: 600px) { .x { color: red; } }");
    Check(sheet.MediaRules.Count == 1, $"media count = {sheet.MediaRules.Count}");
    if (sheet.MediaRules.Count == 1)
    {
        Check(sheet.MediaRules[0].Condition == "(min-width: 600px)", $"cond = '{sheet.MediaRules[0].Condition}'");
        Check(sheet.MediaRules[0].Rules.Count == 1, $"media rules = {sheet.MediaRules[0].Rules.Count}");
    }
}

Console.WriteLine("=== 49. CSS: MediaQueryEvaluator professional ===");
{
    var env = MediaQueryEnvironment.Default(1024, 768, "light");
    Check(MediaQueryEvaluator.Evaluate("(min-width: 600px)", 800, 600, "light"), "min-width pass");
    Check(!MediaQueryEvaluator.Evaluate("(min-width: 900px)", 800, 600, "light"), "min-width fail");
    Check(MediaQueryEvaluator.Evaluate("screen and (min-width: 600px)", 800, 600, "light"), "screen and min-width");
    Check(MediaQueryEvaluator.Evaluate("(max-width: 1200px) and (orientation: landscape)", 1024, 768, "light"), "landscape");
    Check(MediaQueryEvaluator.Evaluate("(prefers-color-scheme: dark)", 800, 600, "dark"), "dark scheme");
    Check(!MediaQueryEvaluator.Evaluate("(prefers-color-scheme: dark)", 800, 600, "light"), "light scheme");
    Check(MediaQueryEvaluator.Evaluate("(min-width: 500px), (min-height: 900px)", 800, 600, "light"), "or via comma");
    Check(MediaQueryEvaluator.Evaluate("not (min-width: 900px)", 800, 600, "light"), "not");
    Check(MediaQueryEvaluator.Evaluate("(width >= 600px)", 800, 600, "light"), "range syntax");
    Check(MediaQueryEvaluator.Evaluate("(width < 900px)", 800, 600, "light"), "range <");
}

Console.WriteLine("=== 50. CSS: selector matching via modern engine ===");
{
    var doc = HtmlDocumentParserIntegration.ParseHtml("<div id='main' class='box a'><p class='child'>x</p><p class='child b'>y</p></div>");
    var div = doc.DocumentElement?.Children
        .OfType<Element>().SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    var ps = div?.Children.OfType<Element>().ToList() ?? new List<Element>();

    Check(CssSelectorMatcher.Matches("div", div!), "tag match");
    Check(CssSelectorMatcher.Matches("#main", div!), "id match");
    Check(CssSelectorMatcher.Matches(".box", div!), "class match");
    Check(CssSelectorMatcher.Matches(".box.a", div!), "compound class match");
    Check(CssSelectorMatcher.Matches("div.box", div!), "tag+class");
    Check(CssSelectorMatcher.Matches("div > p", ps.Count > 0 ? ps[0] : div!), "child combinator");
    Check(CssSelectorMatcher.Matches("p.child:first-child", ps.Count > 0 ? ps[0] : div!), "pseudo first-child");
    Check(ps.Count > 1 && CssSelectorMatcher.Matches("p:not(.a)", ps[1]), ":not pseudo");
    Check(CssSelectorMatcher.Matches("div:has(.b)", div!), ":has pseudo");
    Check(CssSelectorMatcher.Matches("div p:first-of-type", ps.Count > 0 ? ps[0] : div!), "descendant + first-of-type");
}

Console.WriteLine("=== 51. CSS: cascade specificity + source order ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse(".a { color: rgb(255,0,0); } .b { color: rgb(0,255,0); } .a.b { color: rgb(0,0,255); }");
    resolver.AddStylesheet(sheet);

    var doc = HtmlDocumentParserIntegration.ParseHtml("<div class='a b'>text</div>");
    var div = doc.DocumentElement?.Children
        .OfType<Element>().SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");

    resolver.ResolveStyles(doc, 800, 600);
    if (div?.ComputedStyle != null)
    {
        // .a.b has higher specificity (0,2,0) than .a (0,1,0) and .b (0,1,0)
        Check(div.ComputedStyle.Color.Red == 0 && div.ComputedStyle.Color.Green == 0 && div.ComputedStyle.Color.Blue == 255,
            $"specificity winner = rgba({div.ComputedStyle.Color.Red},{div.ComputedStyle.Color.Green},{div.ComputedStyle.Color.Blue},1)");
    }
    else
    {
        Check(false, "div style resolved");
    }
}

Console.WriteLine("=== 52. CSS: source order tie-break (later rule wins) ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse(".x { width: 10px; } .x { width: 30px; }");
    resolver.AddStylesheet(sheet);

    var doc = HtmlDocumentParserIntegration.ParseHtml("<div class='x'>t</div>");
    var div = doc.DocumentElement?.Children
        .OfType<Element>().SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");

    resolver.ResolveStyles(doc, 800, 600);
    if (div?.ComputedStyle?.Width is PixelLength px)
    {
        Check(px.Value == 30f, $"source order width = {px.Value}px (expect 30)");
    }
    else
    {
        Check(false, "width resolved to pixel length");
    }
}

Console.WriteLine("=== 53. CSS: UA origin vs Author origin ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    // UA sheet: body margin 8px. Author sheet: body margin 20px.
    var ua = parser.Parse("body { margin: 8px; }");
    var author = parser.Parse("body { margin: 20px; }");
    resolver.AddStylesheet(ua, Acrux.Core.Css.Resolver.CascadeOrigin.UserAgent);
    resolver.AddStylesheet(author);

    var doc = HtmlDocumentParserIntegration.ParseHtml("<body><p>x</p></body>");
    var body = doc.DocumentElement?.Children.OfType<Element>()
        .FirstOrDefault(e => e.TagName == "BODY");

    resolver.ResolveStyles(doc, 800, 600);
    if (body?.ComputedStyle != null)
    {
        // Author beats UA when equal specificity.
        var m = body.ComputedStyle.MarginTop;
        Check(m is PixelLength p && p.Value == 20f, $"author margin-top = {m} (expect 20px)");
    }
    else
    {
        Check(false, "body style resolved");
    }
}

Console.WriteLine("=== 54. CSS: custom property parse + var() ===");
{
    var parser = new CssParser();
    var sheet = parser.Parse(":root { --brand: #ff8800; } .card { color: var(--brand, red); }");
    Check(sheet.Rules.Count == 2, $"custom prop rules = {sheet.Rules.Count}");
    var rootProps = sheet.Rules.FirstOrDefault(r => r.Selector == ":root")?.Properties;
    Check(rootProps != null && rootProps.ContainsKey("--brand") && rootProps["--brand"] == "#ff8800",
        rootProps != null && rootProps.ContainsKey("--brand") ? $"custom = {rootProps["--brand"]}" : "custom missing");
}

Console.WriteLine("=== 55. CSS: vendor-prefixed property mapping ===");
{
    var parser = new CssParser();
    var inline = parser.ParseInlineStyle("-webkit-appearance: none; -webkit-line-clamp: 2;");
    // Vendor-prefixed names resolve to their canonical property id ("appearance").
    Check(inline.ContainsKey("appearance"), "webkit-appearance resolved to appearance");
    Check(inline.ContainsKey("line-clamp") || inline.ContainsKey("webkit-line-clamp"), "line-clamp preserved");
    Check(inline.TryGetValue("appearance", out var app) && app == "none", $"appearance = '{app}'");
}

Console.WriteLine("=== 56. CSS: !important handled ===");
{
    var parser = new CssParser();
    var (props, important) = parser.ParsePropertiesWithImportance("color: red !important; width: 10px;");
    Check(props.TryGetValue("color", out var c) && c == "red", $"important color = '{c}'");
    Check(important.Contains("color") && !important.Contains("width"), "important set");
}

Console.WriteLine("=== 57. CSS: regression - not() with attr [i] flag must not swallow following rules ===");
{
    var parser = new CssParser();
    var sheet = parser.Parse(@"select[size]:not([size=""0"" i]):not([size=""1"" i]) { appearance: auto; -webkit-appearance: listbox; } progress { display: inline-block; width: 160px; }");
    Check(sheet.Rules.Count == 2, $"rules = {sheet.Rules.Count} (expect 2): :not([attr i]) must end its block at close-brace");
    if (sheet.Rules.Count == 2)
    {
        Check(sheet.Rules[0].Selector.Contains("listbox") == false || sheet.Rules[0].Properties.ContainsKey("appearance"),
            $"first rule selector='{sheet.Rules[0].Selector}'");
        Check(sheet.Rules[1].Selector == "progress", $"second rule selector='{sheet.Rules[1].Selector}'");
    }
}

Console.WriteLine("=== 58. CSS: regression - incomplete @layer block parses without hang ===");
{
    var parser = new CssParser();
    var t = Task.Run(() => parser.Parse("@layer base { .x { color: red; } }"));
    if (t.Wait(5000))
        Check(parser.Parse("@layer base { .x { color: red; } }").LayerRules.Count == 1, "@layer block parses");
    else
        Check(false, "@layer block parses");
}

Console.WriteLine("=== 59. CSS: regression - UA stylesheet parses fully incl @media ===");
{
    var parser = new CssParser();
    var dm = new DocumentManager();
    var ua = dm.GetUaStylesheet();
    Check(ua.Rules.Count > 100, $"UA rules = {ua.Rules.Count} (expect > 100)");
    Check(ua.MediaRules.Count >= 2, $"UA @media = {ua.MediaRules.Count} (expect >= 2)");
}

Console.WriteLine("=== 60. CSS: var() resolves through the cascade ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse(":root { --brand: #ff8800; } .card { color: var(--brand); background-color: var(--brand, red); }");
    resolver.AddStylesheet(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='card'>x</div></body></html>");
    var card = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    resolver.ResolveStyles(doc, 800, 600);
    if (card?.ComputedStyle != null)
    {
        var c = card.ComputedStyle.Color;
        Check(c.Red == 255 && c.Green == 136 && c.Blue == 0, $"var() color = rgba({c.Red},{c.Green},{c.Blue},1) (expect ff8800)");
        Check(card.ComputedStyle.BackgroundColor is SkiaSharp.SKColor bg && bg.Red == 255 && bg.Green == 136,
            $"var() background-color = {card.ComputedStyle.BackgroundColor}");
    }
    else
    {
        Check(false, "card style resolved");
    }
}

Console.WriteLine("=== 61. CSS: var() with fallback when undefined ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse(".x { color: var(--missing, rgb(10,20,30)); }");
    resolver.AddStylesheet(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='x'>t</div></body></html>");
    var x = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    resolver.ResolveStyles(doc, 800, 600);
    if (x?.ComputedStyle != null)
    {
        var c = x.ComputedStyle.Color;
        Check(c.Red == 10 && c.Green == 20 && c.Blue == 30, $"fallback color = rgba({c.Red},{c.Green},{c.Blue},1)");
    }
    else Check(false, "fallback style resolved");
}

Console.WriteLine("=== 62. CSS: var() inherited from ancestor custom property ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse(".parent { --accent: rgb(1,2,3); } .child { color: var(--accent); }");
    resolver.AddStylesheet(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='parent'><p class='child'>t</p></div></body></html>");
    var child = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "P");
    resolver.ResolveStyles(doc, 800, 600);
    if (child?.ComputedStyle != null)
    {
        var c = child.ComputedStyle.Color;
        Check(c.Red == 1 && c.Green == 2 && c.Blue == 3, $"inherited var() color = rgba({c.Red},{c.Green},{c.Blue},1)");
    }
    else Check(false, "child style resolved");
}

Console.WriteLine("=== 63. CSS: inherit keyword ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    // body color red inherits to .child via normal inheritance; .inherit-me uses explicit inherit
    var sheet = parser.Parse("body { color: rgb(200,10,10); } .child { color: rgb(0,0,255); } .inherit-me { color: inherit; }");
    resolver.AddStylesheet(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='child'><span class='inherit-me'>t</span></div></body></html>");
    var span = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "SPAN");
    resolver.ResolveStyles(doc, 800, 600);
    if (span?.ComputedStyle != null)
    {
        // .child sets blue, .inherit-me overrides with inherit -> should be blue (parent's)
        var c = span.ComputedStyle.Color;
        Check(c.Red == 0 && c.Green == 0 && c.Blue == 255, $"inherit color = rgba({c.Red},{c.Green},{c.Blue},1) (expect parent blue)");
    }
    else Check(false, "inherit span resolved");
}

Console.WriteLine("=== 64. CSS: initial keyword ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse("body { color: rgb(200,10,10); margin: 30px; } .x { margin: initial; color: initial; }");
    resolver.AddStylesheet(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='x'>t</div></body></html>");
    var x = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    resolver.ResolveStyles(doc, 800, 600);
    if (x?.ComputedStyle != null)
    {
        // color: initial -> black (initial value); margin: initial -> 0
        var c = x.ComputedStyle.Color;
        Check(c.Red == 0 && c.Green == 0 && c.Blue == 0, $"initial color = rgba({c.Red},{c.Green},{c.Blue},1)");
        Check(x.ComputedStyle.MarginTop is PixelLength mp && mp.Value == 0, $"initial margin = {x.ComputedStyle.MarginTop}");
    }
    else Check(false, "initial div resolved");
}

Console.WriteLine("=== 65. CSS: unset on inherited property behaves like inherit ===");
{
    var resolver = new Acrux.Core.Css.Resolver.CascadeResolver();
    var parser = new CssParser();
    var sheet = parser.Parse("body { color: rgb(9,8,7); } .x { color: rgb(1,2,3); } .y { color: unset; }");
    resolver.AddStylesheet(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='x'><p class='y'>t</p></div></body></html>");
    var p = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "P");
    resolver.ResolveStyles(doc, 800, 600);
    if (p?.ComputedStyle != null)
    {
        // unset on inherited color -> inherit from .x (rgb(1,2,3))
        var c = p.ComputedStyle.Color;
        Check(c.Red == 1 && c.Green == 2 && c.Blue == 3, $"unset color = rgba({c.Red},{c.Green},{c.Blue},1) (expect inherit)");
    }
    else Check(false, "unset p resolved");
}

Console.WriteLine("=== 66. Prism: StyleResolver specificity + source order ===");
{
    var sheet = Acrux.Core.Css.Tokenizer.CssParserImpl.ParseStyleSheet(
        ".a { color: rgb(255,0,0); } .b { color: rgb(0,255,0); } .a.b { color: rgb(0,0,255); }",
        Acrux.Core.Css.Tokenizer.CssParserContext.Default());
    var resolver = new Acrux.Core.Css.Cascade.StyleResolver(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='a b'>x</div></body></html>");
    var div = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    resolver.SetViewport(800, 600);
    resolver.ResolveDocument(doc);
    if (div?.ComputedStyle != null)
    {
        var c = div.ComputedStyle.Color;
        Check(c.Red == 0 && c.Green == 0 && c.Blue == 255,
            $"Prism specificity = rgba({c.Red},{c.Green},{c.Blue},1) (expect 0000ff)");
    }
    else Check(false, "Prism div resolved");
}

Console.WriteLine("=== 67. Prism: source order tie-break ===");
{
    var sheet = Acrux.Core.Css.Tokenizer.CssParserImpl.ParseStyleSheet(
        ".x { width: 10px; } .x { width: 30px; }",
        Acrux.Core.Css.Tokenizer.CssParserContext.Default());
    var resolver = new Acrux.Core.Css.Cascade.StyleResolver(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='x'>t</div></body></html>");
    var div = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    resolver.SetViewport(800, 600);
    resolver.ResolveDocument(doc);
    if (div?.ComputedStyle?.Width is PixelLength px)
        Check(px.Value == 30f, $"Prism source order width = {px.Value}px");
    else Check(false, "Prism width resolved");
}

Console.WriteLine("=== 68. Prism: var() resolves ===");
{
    var sheet = Acrux.Core.Css.Tokenizer.CssParserImpl.ParseStyleSheet(
        ":root { --brand: rgb(1,2,3); } .card { color: var(--brand); }",
        Acrux.Core.Css.Tokenizer.CssParserContext.Default());
    var resolver = new Acrux.Core.Css.Cascade.StyleResolver(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='card'>x</div></body></html>");
    var div = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "DIV");
    resolver.SetViewport(800, 600);
    resolver.ResolveDocument(doc);
    if (div?.ComputedStyle != null)
    {
        Console.WriteLine($"    [DBG] var() custom brand={div.ComputedStyle.GetCustomProperty("brand") ?? "<null>"}");
        var c = div.ComputedStyle.Color;
        Check(c.Red == 1 && c.Green == 2 && c.Blue == 3, $"Prism var() = rgba({c.Red},{c.Green},{c.Blue},1)");
    }
    else Check(false, "Prism var() div resolved");
}

Console.WriteLine("=== 69. Prism: inherit keyword ===");
{
    var sheet = Acrux.Core.Css.Tokenizer.CssParserImpl.ParseStyleSheet(
        "body { color: rgb(9,8,7); } .child { color: rgb(0,0,255); } .x { color: inherit; }",
        Acrux.Core.Css.Tokenizer.CssParserContext.Default());
    var resolver = new Acrux.Core.Css.Cascade.StyleResolver(sheet);
    var doc = HtmlDocumentParserIntegration.ParseHtml("<html><body><div class='child'><span class='x'>t</span></div></body></html>");
    var span = doc.DocumentElement?.Children.OfType<Element>()
        .SelectMany(e => e.Children.OfType<Element>())
        .SelectMany(e => e.Children.OfType<Element>())
        .FirstOrDefault(e => e.TagName == "SPAN");
    resolver.SetViewport(800, 600);
    resolver.ResolveDocument(doc);
    if (span?.ComputedStyle != null)
    {
        var c = span.ComputedStyle.Color;
        Check(c.Red == 0 && c.Green == 0 && c.Blue == 255, $"Prism inherit = rgba({c.Red},{c.Green},{c.Blue},1)");
    }
    else Check(false, "Prism inherit span resolved");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL SMOKE TESTS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;