using Acrux.Core.Dom;
using Acrux.Core.Css;
using SkiaSharp;
using System.Text;

namespace Acrux.Core.Layout;

public class LayoutDevTool
{
    private readonly StringBuilder _sb = new();

    public string GenerateReport(Document document, float viewportWidth, float viewportHeight)
    {
        _sb.Clear();

        _sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        _sb.AppendLine("║                      Acrux Layout Debug Report                          ║");
        _sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        _sb.AppendLine();

        // === Section 1: Viewport & Document Info ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 1. DOCUMENT INFO");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine($"    Viewport:     {viewportWidth:F0} x {viewportHeight:F0} px");
        _sb.AppendLine($"    Document URL: {document.Url}");
        _sb.AppendLine($"    Title:        {document.Title}");
        _sb.AppendLine($"    Root Element: {document.DocumentElement?.TagName ?? "(none)"}");
        _sb.AppendLine($"    Body Element: {document.Body?.TagName ?? "(none)"}");

        var root = document.DocumentElement ?? document.Body;
        if (root != null)
        {
            CountAllElements(root, out int total, out int withBox, out int withoutBox,
                out int block, out int @inline, out int flex, out int grid, out int table, out int list, out int none);
            _sb.AppendLine($"    Total Elements:    {total}");
            _sb.AppendLine($"    With LayoutBox:    {withBox}");
            _sb.AppendLine($"    Without LayoutBox: {withoutBox}");
            _sb.AppendLine($"    Display breakdown: block={block} inline={@inline} flex={flex} grid={grid} table={table} list-item={list} none={none}");
        }
        _sb.AppendLine();

        if (root == null) return _sb.ToString();

        // === Section 2: Full DOM Tree with Computed Styles ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 2. DOM TREE + COMPUTED STYLES (Full Detail)");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpFullElementTree(root, 0);
        _sb.AppendLine();

        // === Section 3: Layout Tree (Box Model) ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 3. LAYOUT TREE (Box Model Details)");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpLayoutTree(root, 0);
        _sb.AppendLine();

        // === Section 4: Inline/Text Runs ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 4. INLINE / TEXT RUNS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpInlineRuns(root);
        _sb.AppendLine();

        // === Section 5: Stacking Contexts ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 5. STACKING CONTEXTS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpStackingContexts(root, 0);
        _sb.AppendLine();

        // === Section 6: Float & Clear ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 6. FLOAT & CLEAR");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpFloatElements(root);
        _sb.AppendLine();

        // === Section 7: Position (absolute/relative/fixed/sticky) ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 7. POSITIONED ELEMENTS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpPositionedElements(root);
        _sb.AppendLine();

        // === Section 8: Flex Layout ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 8. FLEX LAYOUT");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpFlexElements(root);
        _sb.AppendLine();

        // === Section 9: Grid Layout ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 9. GRID LAYOUT");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpGridElements(root);
        _sb.AppendLine();

        // === Section 10: Table Layout ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 10. TABLE LAYOUT");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpTableElements(root);
        _sb.AppendLine();

        // === Section 11: Overflow & Scroll ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 11. OVERFLOW & SCROLL");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpOverflowElements(root);
        _sb.AppendLine();

        // === Section 12: Transform & Filter ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 12. TRANSFORM, FILTER, OPACITY");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpVisualEffects(root);
        _sb.AppendLine();

        // === Section 13: Background & Border ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 13. BACKGROUND & BORDER");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpBackgroundBorder(root);
        _sb.AppendLine();

        // === Section 14: Font & Text ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 14. FONT & TEXT");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpFontText(root);
        _sb.AppendLine();

        // === Section 15: Pseudo-elements ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 15. PSEUDO-ELEMENTS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpPseudoElements(root);
        _sb.AppendLine();

        // === Section 16: Visibility & Display ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 16. VISIBILITY & DISPLAY ISSUES");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpVisibilityIssues(root);
        _sb.AppendLine();

        // === Section 17: Z-Index Map ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 17. Z-INDEX MAP");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpZIndexMap(root);
        _sb.AppendLine();

        // === Section 18: Box Shadow & Text Shadow ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 18. SHADOWS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpShadows(root);
        _sb.AppendLine();

        // === Section 19: Animations & Transitions ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 19. ANIMATIONS & TRANSITIONS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpAnimations(root);
        _sb.AppendLine();

        // === Section 20: Errors & Warnings ===
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _sb.AppendLine(" 20. ERRORS & WARNINGS");
        _sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        DumpErrors(root);
        _sb.AppendLine();

        _sb.AppendLine("══════════════════════════════════════════════════════════════════════════════");
        _sb.AppendLine(" END OF REPORT");
        _sb.AppendLine("══════════════════════════════════════════════════════════════════════════════");

        return _sb.ToString();
    }

    // ==================== Section 2: Full Element Tree ====================

    private void DumpFullElementTree(Element element, int depth)
    {
        var indent = IndentStr(depth);
        var style = element.ComputedStyle;
        var box = element.LayoutBox;

        string id = string.IsNullOrEmpty(element.Id) ? "" : $" #{element.Id}";
        string cls = string.IsNullOrEmpty(element.ClassName) ? "" : $" .{element.ClassName.Replace(' ', '.')}";

        _sb.AppendLine($"{indent}<{element.TagName.ToLowerInvariant()}{id}{cls}>");

        if (style != null)
        {
            _sb.AppendLine($"{indent}  [display]       {style.Display}");
            _sb.AppendLine($"{indent}  [position]      {style.Position}" + (style.Position != PositionType.Static ? $" (top={FmtL(style.Top, style)} left={FmtL(style.Left, style)} right={FmtL(style.Right, style)} bottom={FmtL(style.Bottom, style)})" : ""));
            _sb.AppendLine($"{indent}  [float]         {style.Float}  clear={style.Clear}");
            _sb.AppendLine($"{indent}  [box-sizing]    {style.BoxSizing}");
            _sb.AppendLine($"{indent}  [width]         {FmtL(style.Width, style)}  min={FmtL(style.MinWidth, style)}  max={FmtL(style.MaxWidth, style)}");
            _sb.AppendLine($"{indent}  [height]        {FmtL(style.Height, style)}  min={FmtL(style.MinHeight, style)}  max={FmtL(style.MaxHeight, style)}");
            _sb.AppendLine($"{indent}  [margin]        T={FmtL(style.MarginTop, style)} R={FmtL(style.MarginRight, style)} B={FmtL(style.MarginBottom, style)} L={FmtL(style.MarginLeft, style)}");
            _sb.AppendLine($"{indent}  [padding]       T={FmtL(style.PaddingTop, style)} R={FmtL(style.PaddingRight, style)} B={FmtL(style.PaddingBottom, style)} L={FmtL(style.PaddingLeft, style)}");
            _sb.AppendLine($"{indent}  [border]        T={style.BorderTopWidth:F1}/{style.BorderTopStyle} R={style.BorderRightWidth:F1}/{style.BorderRightStyle} B={style.BorderBottomWidth:F1}/{style.BorderBottomStyle} L={style.BorderLeftWidth:F1}/{style.BorderLeftStyle}");
            _sb.AppendLine($"{indent}  [border-radius] TL={style.BorderTopLeftRadius:F1} TR={style.BorderTopRightRadius:F1} BR={style.BorderBottomRightRadius:F1} BL={style.BorderBottomLeftRadius:F1}");
            _sb.AppendLine($"{indent}  [color]         #{style.Color.Red:X2}{style.Color.Green:X2}{style.Color.Blue:X2}{style.Color.Alpha:X2}  opacity={style.Opacity:F2}");
            _sb.AppendLine($"{indent}  [font]          {style.FontFamily ?? "(none)"} size={style.FontSize:F1}px weight={style.FontWeight} style={style.FontStyle} lh={style.LineHeight:F2}");
            _sb.AppendLine($"{indent}  [text]          align={style.TextAlign} decorate={style.TextDecoration} transform={style.TextTransform} overflow={style.TextOverflow} wrap={style.WhiteSpace} indent={style.TextIndent:F1}");
            _sb.AppendLine($"{indent}  [bg]            color={FmtColor(style.BackgroundColor)} image={(style.BackgroundImage != null ? string.Join(", ", style.BackgroundImage.Select(s => s.Truncate(60))) : "none")} size={style.BackgroundSize} repeat={style.BackgroundRepeat} attach={style.BackgroundAttachment}");
            _sb.AppendLine($"{indent}  [overflow]      {style.Overflow} x={style.OverflowX} y={style.OverflowY}  visibility={style.Visibility}");
            _sb.AppendLine($"{indent}  [z-index]       {(style.ZIndex.HasValue ? style.ZIndex.Value.ToString() : "auto")}");

            if (style.FlexDirection != FlexDirectionType.Row || style.FlexGrow != 0 || style.FlexShrink != 1 || style.FlexBasis is not AutoLength)
                _sb.AppendLine($"{indent}  [flex]          dir={style.FlexDirection} wrap={style.FlexWrap} grow={style.FlexGrow} shrink={style.FlexShrink} basis={FmtL(style.FlexBasis, style)}");
            if (style.JustifyContent != JustifyContentType.FlexStart)
                _sb.AppendLine($"{indent}  [justify]       {style.JustifyContent}  align={style.AlignItems}  self={style.AlignSelf}");

            if (!string.IsNullOrEmpty(style.GridTemplateColumns) || !string.IsNullOrEmpty(style.GridTemplateRows))
                _sb.AppendLine($"{indent}  [grid]          cols={style.GridTemplateColumns ?? "none"}  rows={style.GridTemplateRows ?? "none"}  flow={style.GridAutoFlow}");
            if (!string.IsNullOrEmpty(style.GridColumnStart) || !string.IsNullOrEmpty(style.GridColumnEnd))
                _sb.AppendLine($"{indent}  [grid-pos]      col={style.GridColumn ?? $"{style.GridColumnStart} / {style.GridColumnEnd}"}  row={style.GridRow ?? $"{style.GridRowStart} / {style.GridRowEnd}"}");

            if (style.Transform != null && style.Transform != "none")
                _sb.AppendLine($"{indent}  [transform]     {style.Transform}  origin={style.TransformOrigin}");
            if (style.Filter != null && style.Filter != "none")
                _sb.AppendLine($"{indent}  [filter]        {style.Filter}");
            if (style.BackdropFilter != null && style.BackdropFilter != "none")
                _sb.AppendLine($"{indent}  [backdrop]      {style.BackdropFilter}");
            if (style.ClipPath != null && style.ClipPath != "none")
                _sb.AppendLine($"{indent}  [clip-path]     {style.ClipPath}");

            if (style.Transition != null && style.Transition != "none")
                _sb.AppendLine($"{indent}  [transition]    {style.Transition}");
            if (style.AnimationName != null && style.AnimationName != "none")
                _sb.AppendLine($"{indent}  [animation]     name={style.AnimationName} dur={style.AnimationDuration} iter={style.AnimationIterationCount} fill={style.AnimationFillMode}");

            if (style.BoxShadow != null && style.BoxShadow.Count > 0)
                foreach (var bs in style.BoxShadow)
                    _sb.AppendLine($"{indent}  [box-shadow]    {FmtBoxShadow(bs)}");
            if (style.TextShadow.Count > 0)
                _sb.AppendLine($"{indent}  [text-shadow]   {style.TextShadow.Count} shadow(s)");

            if (style.AspectRatio > 0)
                _sb.AppendLine($"{indent}  [aspect-ratio]  {style.AspectRatio:F3}");
            if (style.ListStyleType != ListStyleType.None)
                _sb.AppendLine($"{indent}  [list-style]    type={style.ListStyleType} pos={style.ListStylePosition}");

            if (!string.IsNullOrEmpty(style.Content))
                _sb.AppendLine($"{indent}  [content]       {style.Content}");
            if (!string.IsNullOrEmpty(style.WillChange) && style.WillChange != "auto")
                _sb.AppendLine($"{indent}  [will-change]   {style.WillChange}");
            if (style.Contain != ContainType.None)
                _sb.AppendLine($"{indent}  [contain]       {Acrux.Core.Css.Resolver.CssPropertyApplier.FormatContain(style.Contain)}");
            if (style.ContentVisibility != ContentVisibilityType.Visible)
                _sb.AppendLine($"{indent}  [content-visibility] {style.ContentVisibility}");
            if (style.ContainIntrinsicWidth != null || style.ContainIntrinsicHeight != null)
                _sb.AppendLine($"{indent}  [contain-intrinsic] "
                    + $"{Acrux.Core.Css.Resolver.CssPropertyApplier.FormatContainIntrinsic(style)}");
        }
        else
        {
            _sb.AppendLine($"{indent}  (no ComputedStyle)");
        }

        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                DumpFullElementTree(childEl, depth + 1);
            else if (child is TextNode tn && !tn.IsWhitespaceOnly)
                _sb.AppendLine($"{indent}  \"{tn.TextContent?.Truncate(80)}\"");
        }
    }

    // ==================== Section 3: Layout Tree ====================

    private void DumpLayoutTree(Element element, int depth)
    {
        var box = element.LayoutBox;
        if (box == null)
        {
            _sb.AppendLine($"{IndentStr(depth)}<{element.TagName.ToLowerInvariant()}> — NO LAYOUT BOX");
            foreach (var child in element.Children.OfType<Element>())
                DumpLayoutTree(child, depth);
            return;
        }

        var style = element.ComputedStyle;
        string tag = element.TagName.ToLowerInvariant();
        string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
        string cls = string.IsNullOrEmpty(element.ClassName) ? "" : $".{element.ClassName.Split(' ')[0]}";

        _sb.AppendLine($"{IndentStr(depth)}<{tag}{id}{cls}>");
        _sb.AppendLine($"{IndentStr(depth)}  ┌─ MarginBox:   {FmtRect(box.MarginBox)}");
        _sb.AppendLine($"{IndentStr(depth)}  ├─ BorderBox:   {FmtRect(box.BorderBox)}");
        _sb.AppendLine($"{IndentStr(depth)}  ├─ PaddingBox:  {FmtRect(box.PaddingBox)}");
        _sb.AppendLine($"{IndentStr(depth)}  └─ ContentBox:  {FmtRect(box.ContentBox)}");

        if (box.IsFloating)
            _sb.AppendLine($"{IndentStr(depth)}  ⚓ FLOATING ({box.Float})");
        if (box.IsScrollContainer)
            _sb.AppendLine($"{IndentStr(depth)}  📜 SCROLL CONTAINER content={box.ScrollContentWidth:F0}x{box.ScrollContentHeight:F0}");
        if (box.IsSticky)
            _sb.AppendLine($"{IndentStr(depth)}  📌 STICKY top={box.StickyTop:F0} left={box.StickyLeft:F0}");

        if (box.Children.Count > 0)
        {
            _sb.AppendLine($"{IndentStr(depth)}  Children[{box.Children.Count}]:");
            for (int i = 0; i < box.Children.Count; i++)
            {
                var child = box.Children[i];
                var childElem = FindElementByLayoutBox(element, child);
                string childTag = childElem?.TagName.ToLowerInvariant() ?? "(anonymous)";
                string childId = childElem != null && !string.IsNullOrEmpty(childElem.Id) ? $"#{childElem.Id}" : "";
                string marker = i == box.Children.Count - 1 ? "└" : "├";
                _sb.AppendLine($"{IndentStr(depth)}  {marker}─ [{childTag}{childId}] {FmtRect(child.MarginBox)}");
            }
        }

        if (box.Lines != null && box.Lines.Count > 0)
        {
            _sb.AppendLine($"{IndentStr(depth)}  Lines[{box.Lines.Count}]:");
            for (int i = 0; i < box.Lines.Count; i++)
            {
                var line = box.Lines[i];
                string marker = i == box.Lines.Count - 1 ? "└" : "├";
                _sb.AppendLine($"{IndentStr(depth)}  {marker}─ Line[{i}] Y={line.Y:F1} H={line.Height:F1} BL={line.Baseline:F1} offsetX={line.TextAlignOffsetX:F1} runs={line.Runs.Count}");
                for (int j = 0; j < line.Runs.Count; j++)
                {
                    var run = line.Runs[j];
                    string rmarker = j == line.Runs.Count - 1 ? "└" : "├";
                    _sb.AppendLine($"{IndentStr(depth)}  {rmarker}  Run[{j}] \"{run.Text.Truncate(40)}\" W={run.Width:F1} H={run.Height:F1} font={run.FontSize:F1}px");
                }
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpLayoutTree(child, depth + 1);
    }

    // ==================== Section 4: Inline Runs ====================

    private void DumpInlineRuns(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is Element el)
            {
                var box = el.LayoutBox;
                if (box == null) continue;

                bool hasRuns = (box.Lines != null && box.Lines.Count > 0) || (box.LineRuns != null && box.LineRuns.Count > 0);
                if (hasRuns)
                {
                    string tag = el.TagName.ToLowerInvariant();
                    string id = string.IsNullOrEmpty(el.Id) ? "" : $"#{el.Id}";
                    _sb.AppendLine($"  <{tag}{id}>");

                    if (box.Lines != null)
                    {
                        foreach (var line in box.Lines)
                        {
                            float totalW = 0;
                            foreach (var r in line.Runs) totalW += r.Width;
                            _sb.AppendLine($"    Line Y={line.Y:F1} W={totalW:F1} H={line.Height:F1} → {line.Runs.Count} run(s)");
                            foreach (var run in line.Runs)
                                _sb.AppendLine($"      \"{run.Text.Truncate(50)}\" W={run.Width:F1} color={FmtColor(run.Color)} font={run.FontFamily ?? "?"} {run.FontSize:F1}px");
                        }
                    }
                    else if (box.LineRuns != null)
                    {
                        foreach (var run in box.LineRuns)
                            _sb.AppendLine($"    \"{run.Text.Truncate(50)}\" X={run.X:F1} W={run.Width:F1} H={run.Height:F1}");
                    }
                }

                DumpInlineRuns(el);
            }
        }
    }

    // ==================== Section 5: Stacking Contexts ====================

    private void DumpStackingContexts(Element element, int depth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        bool createsContext = style.Position == PositionType.Absolute || style.Position == PositionType.Fixed ||
            (style.Position == PositionType.Relative && style.ZIndex.HasValue) ||
            (style.Position == PositionType.Sticky && style.ZIndex.HasValue) ||
            style.Opacity < 1.0f ||
            (style.Transform != null && style.Transform != "none") ||
            style.Isolation == IsolationType.Isolate;

        if (createsContext)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  STACKING CONTEXT <{tag}{id}> z={style.ZIndex?.ToString() ?? "auto"} pos={style.Position} opacity={style.Opacity:F2} transform={style.Transform != null && style.Transform != "none"}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpStackingContexts(child, depth + 1);
    }

    // ==================== Section 6: Float ====================

    private void DumpFloatElements(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is Element el)
            {
                var style = el.ComputedStyle;
                if (style != null && style.Float != FloatType.None)
                {
                    string tag = el.TagName.ToLowerInvariant();
                    string id = string.IsNullOrEmpty(el.Id) ? "" : $"#{el.Id}";
                    var box = el.LayoutBox;
                    _sb.AppendLine($"  FLOAT <{tag}{id}> float={style.Float} clear={style.Clear} w={FmtL(style.Width, style)} h={FmtL(style.Height, style)}");
                    if (box != null)
                        _sb.AppendLine($"    Box: {FmtRect(box.MarginBox)}");
                }
                DumpFloatElements(el);
            }
        }
    }

    // ==================== Section 7: Positioned ====================

    private void DumpPositionedElements(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is Element el)
            {
                var style = el.ComputedStyle;
                if (style != null && style.Position != PositionType.Static)
                {
                    string tag = el.TagName.ToLowerInvariant();
                    string id = string.IsNullOrEmpty(el.Id) ? "" : $"#{el.Id}";
                    var box = el.LayoutBox;
                    _sb.AppendLine($"  <{tag}{id}> position={style.Position} z={style.ZIndex?.ToString() ?? "auto"}");
                    _sb.AppendLine($"    offset: top={FmtL(style.Top, style)} left={FmtL(style.Left, style)} right={FmtL(style.Right, style)} bottom={FmtL(style.Bottom, style)}");
                    if (box != null)
                        _sb.AppendLine($"    final: {FmtRect(box.MarginBox)}");
                }
                DumpPositionedElements(el);
            }
        }
    }

    // ==================== Section 8: Flex ====================

    private void DumpFlexElements(Element element)
    {
        var style = element.ComputedStyle;
        if (style != null && (style.Display == DisplayType.Flex || style.Display == DisplayType.InlineFlex))
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  FLEX CONTAINER <{tag}{id}>");
            _sb.AppendLine($"    direction={style.FlexDirection} wrap={style.FlexWrap} justify={style.JustifyContent} align={style.AlignItems} align-content={style.AlignContent}");
            _sb.AppendLine($"    gap: row={FmtL(style.RowGap, style)} col={FmtL(style.ColumnGap, style)}");

            foreach (var child in element.Children.OfType<Element>())
            {
                var cs = child.ComputedStyle;
                if (cs == null) continue;
                string ctag = child.TagName.ToLowerInvariant();
                string cid = string.IsNullOrEmpty(child.Id) ? "" : $"#{child.Id}";
                _sb.AppendLine($"    └─ <{ctag}{cid}> grow={cs.FlexGrow} shrink={cs.FlexShrink} basis={FmtL(cs.FlexBasis, cs)} order={cs.Order} alignSelf={cs.AlignSelf}");
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpFlexElements(child);
    }

    // ==================== Section 9: Grid ====================

    private void DumpGridElements(Element element)
    {
        var style = element.ComputedStyle;
        if (style != null && (!string.IsNullOrEmpty(style.GridTemplateColumns) || !string.IsNullOrEmpty(style.GridTemplateRows)))
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  GRID CONTAINER <{tag}{id}>");
            _sb.AppendLine($"    template-columns: {style.GridTemplateColumns ?? "none"}");
            _sb.AppendLine($"    template-rows:    {style.GridTemplateRows ?? "none"}");
            _sb.AppendLine($"    auto-columns:     {style.GridAutoColumns}");
            _sb.AppendLine($"    auto-rows:        {style.GridAutoRows}");
            _sb.AppendLine($"    flow:             {style.GridAutoFlow}");
            _sb.AppendLine($"    gap:              row={FmtL(style.RowGap, style)} col={FmtL(style.ColumnGap, style)}");

            foreach (var child in element.Children.OfType<Element>())
            {
                var cs = child.ComputedStyle;
                if (cs == null) continue;
                string ctag = child.TagName.ToLowerInvariant();
                string colSpec = cs.GridColumn ?? $"{cs.GridColumnStart ?? "auto"} / {cs.GridColumnEnd ?? "auto"}";
                string rowSpec = cs.GridRow ?? $"{cs.GridRowStart ?? "auto"} / {cs.GridRowEnd ?? "auto"}";
                _sb.AppendLine($"    └─ <{ctag}> col=[{colSpec}] row=[{rowSpec}] area={cs.GridArea ?? "none"}");
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpGridElements(child);
    }

    // ==================== Section 10: Table ====================

    private void DumpTableElements(Element element)
    {
        var style = element.ComputedStyle;
        if (style != null && (style.Display == DisplayType.Table || style.Display == DisplayType.TableRow || style.Display == DisplayType.TableCell))
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  TABLE <{tag}{id}> display={style.Display} border-collapse={style.BorderCollapse} border-spacing={style.BorderSpacing} table-layout={style.TableLayout}");

            if (tag == "td" || tag == "th")
            {
                string colspan = element.GetAttribute("colspan") ?? "1";
                string rowspan = element.GetAttribute("rowspan") ?? "1";
                _sb.AppendLine($"    colspan={colspan} rowspan={rowspan}");
            }

            var box = element.LayoutBox;
            if (box != null)
                _sb.AppendLine($"    Box: {FmtRect(box.ContentBox)}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpTableElements(child);
    }

    // ==================== Section 11: Overflow ====================

    private void DumpOverflowElements(Element element)
    {
        var style = element.ComputedStyle;
        if (style != null && (style.Overflow != OverflowType.Visible || style.OverflowX != OverflowType.Visible || style.OverflowY != OverflowType.Visible))
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}> overflow={style.Overflow} x={style.OverflowX} y={style.OverflowY}");
            var box = element.LayoutBox;
            if (box != null)
            {
                _sb.AppendLine($"    ContentBox:  {FmtRect(box.ContentBox)}");
                _sb.AppendLine($"    ScrollContent: {box.ScrollContentWidth:F0}x{box.ScrollContentHeight:F0}");
                if (box.IsScrollContainer)
                    _sb.AppendLine($"    SCROLL CONTAINER");
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpOverflowElements(child);
    }

    // ==================== Section 12: Visual Effects ====================

    private void DumpVisualEffects(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) { foreach (var c in element.Children.OfType<Element>()) DumpVisualEffects(c); return; }

        bool hasEffects = (style.Transform != null && style.Transform != "none") ||
            (style.Filter != null && style.Filter != "none") ||
            (style.BackdropFilter != null && style.BackdropFilter != "none") ||
            (style.ClipPath != null && style.ClipPath != "none") ||
            style.Opacity < 1.0f;

        if (hasEffects)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}>");
            if (style.Transform != null && style.Transform != "none")
                _sb.AppendLine($"    transform:       {style.Transform}  origin={style.TransformOrigin}");
            if (style.Filter != null && style.Filter != "none")
                _sb.AppendLine($"    filter:          {style.Filter}");
            if (style.BackdropFilter != null && style.BackdropFilter != "none")
                _sb.AppendLine($"    backdrop-filter: {style.BackdropFilter}");
            if (style.ClipPath != null && style.ClipPath != "none")
                _sb.AppendLine($"    clip-path:       {style.ClipPath}");
            if (style.Opacity < 1.0f)
                _sb.AppendLine($"    opacity:         {style.Opacity:F3}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpVisualEffects(child);
    }

    // ==================== Section 13: Background & Border ====================

    private void DumpBackgroundBorder(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) { foreach (var c in element.Children.OfType<Element>()) DumpBackgroundBorder(c); return; }

        bool hasBg = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasBgImage = style.BackgroundImage is { Count: > 0 };
        bool hasBorder = style.BorderTopWidth > 0 || style.BorderRightWidth > 0 || style.BorderBottomWidth > 0 || style.BorderLeftWidth > 0;
        bool hasRadius = style.BorderTopLeftRadius > 0 || style.BorderTopRightRadius > 0 || style.BorderBottomRightRadius > 0 || style.BorderBottomLeftRadius > 0;

        if (hasBg || hasBgImage || hasBorder || hasRadius)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}>");
            if (hasBg) _sb.AppendLine($"    background-color: {FmtColor(style.BackgroundColor)}");
            if (hasBgImage) _sb.AppendLine($"    background-image: {string.Join(", ", style.BackgroundImage!.Select(s => s.Truncate(80)))}");
            if (hasBg) _sb.AppendLine($"    background-size:  {style.BackgroundSize}  repeat={style.BackgroundRepeat}  attach={style.BackgroundAttachment}");
            if (hasBorder)
                _sb.AppendLine($"    border: T={style.BorderTopWidth:F1}px/{style.BorderTopStyle}/{FmtColor(style.BorderTopColor)} R={style.BorderRightWidth:F1}px/{style.BorderRightStyle}/{FmtColor(style.BorderRightColor)} B={style.BorderBottomWidth:F1}px/{style.BorderBottomStyle}/{FmtColor(style.BorderBottomColor)} L={style.BorderLeftWidth:F1}px/{style.BorderLeftStyle}/{FmtColor(style.BorderLeftColor)}");
            if (hasRadius)
                _sb.AppendLine($"    border-radius: TL={style.BorderTopLeftRadius:F1} TR={style.BorderTopRightRadius:F1} BR={style.BorderBottomRightRadius:F1} BL={style.BorderBottomLeftRadius:F1}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpBackgroundBorder(child);
    }

    // ==================== Section 14: Font & Text ====================

    private void DumpFontText(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) { foreach (var c in element.Children.OfType<Element>()) DumpFontText(c); return; }

        string tag = element.TagName.ToLowerInvariant();
        string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";

        bool hasTextStyle = style.TextDecoration != TextDecorationType.None ||
            style.TextDecorationLine != TextDecorationLineType.None ||
            style.LetterSpacing != 0 || style.WordSpacing != 0 ||
            style.TextIndent != 0 || style.TextTransform != "none" ||
            style.TextOverflow != TextOverflowType.Clip ||
            style.WhiteSpace != WhiteSpaceMode.Normal ||
            style.WordBreak != WordBreakMode.Normal;

        if (hasTextStyle)
        {
            _sb.AppendLine($"  <{tag}{id}>");
            _sb.AppendLine($"    text-align:      {style.TextAlign}");
            _sb.AppendLine($"    text-decoration: {style.TextDecoration} line={style.TextDecorationLine} style={style.TextDecorationStyle} color={FmtColor(style.ResolvedTextDecorationColor)}");
            _sb.AppendLine($"    text-indent:     {style.TextIndent:F1}px");
            _sb.AppendLine($"    text-transform:  {style.TextTransform}");
            _sb.AppendLine($"    text-overflow:   {style.TextOverflow}");
            _sb.AppendLine($"    letter-spacing:  {style.LetterSpacing:F1}px");
            _sb.AppendLine($"    word-spacing:    {style.WordSpacing:F1}px");
            _sb.AppendLine($"    white-space:     {style.WhiteSpace}");
            _sb.AppendLine($"    word-break:      {style.WordBreak}");
            _sb.AppendLine($"    overflow-wrap:   {style.OverflowWrap}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpFontText(child);
    }

    // ==================== Section 15: Pseudo-elements ====================

    private void DumpPseudoElements(Element element)
    {
        if (element.BeforeStyles != null && element.BeforeStyles.Count > 0)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}>::before");
            foreach (var kv in element.BeforeStyles)
                _sb.AppendLine($"    {kv.Key}: {kv.Value}");
        }

        if (element.AfterStyles != null && element.AfterStyles.Count > 0)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}>::after");
            foreach (var kv in element.AfterStyles)
                _sb.AppendLine($"    {kv.Key}: {kv.Value}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpPseudoElements(child);
    }

    // ==================== Section 16: Visibility Issues ====================

    private void DumpVisibilityIssues(Element element)
    {
        var style = element.ComputedStyle;
        if (style != null)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";

            if (style.Display == DisplayType.None)
                _sb.AppendLine($"  HIDDEN (display:none) <{tag}{id}>");
            else if (style.Visibility == VisibilityType.Hidden)
                _sb.AppendLine($"  INVISIBLE (visibility:hidden) <{tag}{id}> — takes space but not drawn");
            else if (style.Visibility == VisibilityType.Collapse)
                _sb.AppendLine($"  COLLAPSED <{tag}{id}>");

            if (style.Opacity <= 0)
                _sb.AppendLine($"  FULLY TRANSPARENT (opacity={style.Opacity:F3}) <{tag}{id}>");

            if (style.Position == PositionType.Absolute && style.Top is AutoLength && style.Left is AutoLength && style.Right is AutoLength && style.Bottom is AutoLength)
            {
                var box = element.LayoutBox;
                if (box != null && box.ContentBox.Width <= 0 && box.ContentBox.Height <= 0)
                    _sb.AppendLine($"  ⚠ ZERO-SIZE absolute element <{tag}{id}> (top/right/bottom/left all auto)");
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpVisibilityIssues(child);
    }

    // ==================== Section 17: Z-Index Map ====================

    private void DumpZIndexMap(Element element)
    {
        var style = element.ComputedStyle;
        if (style != null && style.ZIndex.HasValue)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            var box = element.LayoutBox;
            string pos = box != null ? $"@ {box.MarginBox.Left:F0},{box.MarginBox.Top:F0}" : "(no box)";
            _sb.AppendLine($"  z={style.ZIndex.Value,4}  <{tag}{id}> {pos}  pos={style.Position} opacity={style.Opacity:F2}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpZIndexMap(child);
    }

    // ==================== Section 18: Shadows ====================

    private void DumpShadows(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) { foreach (var c in element.Children.OfType<Element>()) DumpShadows(c); return; }

        bool hasBoxShadow = style.BoxShadow != null;
        bool hasTextShadow = style.TextShadow.Count > 0;

        if (hasBoxShadow || hasTextShadow)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}>");
            if (hasBoxShadow)
                foreach (var bs in style.BoxShadow!)
                    _sb.AppendLine($"    box-shadow: {FmtBoxShadow(bs)}");
            if (hasTextShadow)
            {
                foreach (var ts in style.TextShadow)
                    _sb.AppendLine($"    text-shadow: {FmtColor(ts.Color)} {ts.OffsetX:F1}px {ts.OffsetY:F1}px {ts.BlurRadius:F1}px");
            }
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpShadows(child);
    }

    // ==================== Section 19: Animations ====================

    private void DumpAnimations(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) { foreach (var c in element.Children.OfType<Element>()) DumpAnimations(c); return; }

        bool hasAnim = !string.IsNullOrEmpty(style.AnimationName) && style.AnimationName != "none";
        bool hasTrans = !string.IsNullOrEmpty(style.Transition) && style.Transition != "none";

        if (hasAnim || hasTrans)
        {
            string tag = element.TagName.ToLowerInvariant();
            string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";
            _sb.AppendLine($"  <{tag}{id}>");
            if (hasAnim)
                _sb.AppendLine($"    animation: name={style.AnimationName} dur={style.AnimationDuration} timing={style.AnimationTimingFunction} delay={style.AnimationDelay} iter={style.AnimationIterationCount} dir={style.AnimationDirection} fill={style.AnimationFillMode} play={style.AnimationPlayState}");
            if (hasTrans)
                _sb.AppendLine($"    transition: {style.Transition} prop={style.TransitionProperty} dur={style.TransitionDuration} timing={style.TransitionTimingFunction} delay={style.TransitionDelay}");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpAnimations(child);
    }

    // ==================== Section 20: Errors ====================

    private void DumpErrors(Element element)
    {
        var style = element.ComputedStyle;
        var box = element.LayoutBox;
        string tag = element.TagName.ToLowerInvariant();
        string id = string.IsNullOrEmpty(element.Id) ? "" : $"#{element.Id}";

        if (style != null)
        {
            // Check for zero-size elements that might be hidden unintentionally
            if (box != null && box.ContentBox.Width <= 0 && box.ContentBox.Height <= 0 && style.Display != DisplayType.None)
            {
                bool isInline = style.Display == DisplayType.Inline || style.Display == DisplayType.InlineBlock;
                if (!isInline)
                    _sb.AppendLine($"  ⚠ ZERO-SIZE block element <{tag}{id}> — may be invisible");
            }

            // Check for negative margins
            if (style.MarginTop is PixelLength mt && mt.Value < 0)
                _sb.AppendLine($"  ℹ Negative margin-top={mt.Value:F1}px on <{tag}{id}>");
            if (style.MarginLeft is PixelLength ml && ml.Value < 0)
                _sb.AppendLine($"  ℹ Negative margin-left={ml.Value:F1}px on <{tag}{id}>");

            // Check for overflow without scroll
            if (style.Overflow == OverflowType.Visible && box != null && box.ContentBox.Width > 0)
            {
                // Could check if content overflows, but that requires layout data
            }

            // Check for percentage height without explicit parent height
            if (style.Height is PercentLength && box != null && box.ContentBox.Height <= 0)
                _sb.AppendLine($"  ⚠ Percentage height on <{tag}{id}> but parent has no explicit height → may collapse to 0");
        }

        foreach (var child in element.Children.OfType<Element>())
            DumpErrors(child);
    }

    // ==================== Helpers ====================

    private Element? FindElementByLayoutBox(Element root, LayoutBox target)
    {
        if (root.LayoutBox == target) return root;
        foreach (var child in root.Children.OfType<Element>())
        {
            var found = FindElementByLayoutBox(child, target);
            if (found != null) return found;
        }
        return null;
    }

    private void CountAllElements(Element element, out int total, out int withBox, out int withoutBox,
        out int block, out int @inline, out int flex, out int grid, out int table, out int list, out int none)
    {
        total = 1;
        withBox = element.LayoutBox != null ? 1 : 0;
        withoutBox = element.LayoutBox == null ? 1 : 0;
        block = 0; @inline = 0; flex = 0; grid = 0; table = 0; list = 0; none = 0;

        var style = element.ComputedStyle;
        if (style != null)
        {
            switch (style.Display)
            {
                case DisplayType.Block: block++; break;
                case DisplayType.Inline: case DisplayType.InlineBlock: case DisplayType.InlineFlex: case DisplayType.InlineGrid: @inline++; break;
                case DisplayType.Flex: flex++; break;
                case DisplayType.Grid: grid++; break;
                case DisplayType.Table: case DisplayType.TableRow: case DisplayType.TableCell: case DisplayType.TableRowGroup: table++; break;
                case DisplayType.ListItem: list++; break;
                case DisplayType.None: none++; break;
            }
        }

        foreach (var child in element.Children.OfType<Element>())
        {
            CountAllElements(child, out int ct, out int cwb, out int cwo,
                out int cb, out int ci, out int cf, out int cg, out int cta, out int cl, out int cn);
            total += ct; withBox += cwb; withoutBox += cwo;
            block += cb; @inline += ci; flex += cf; grid += cg; table += cta; list += cl; none += cn;
        }
    }

    private string IndentStr(int depth) => new string(' ', depth * 2);

    private static string FmtRect(SKRect r) =>
        $"(L={r.Left:F1}, T={r.Top:F1}, R={r.Right:F1}, B={r.Bottom:F1}  W={r.Width:F1}, H={r.Height:F1})";

    private static string FmtL(Length? l, ComputedStyle? s = null)
    {
        if (l == null) return "auto";
        if (l is AutoLength) return "auto";
        if (l is PixelLength px) return $"{px.Value:F1}px";
        if (l is EmLength em) return $"{em.Value:F2}em";
        if (l is RemLength rem) return $"{rem.Value:F2}rem";
        if (l is PercentLength p) return $"{p.Value * 100:F1}%";
        if (l is VwLength vw) return $"{vw.Value:F1}vw";
        if (l is VhLength vh) return $"{vh.Value:F1}vh";
        if (l is MathLength ml) return ml.ToString().Truncate(40);
        return l.ToCssString();
    }

    private static string FmtColor(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}{(c.Alpha < 255 ? $"/{c.Alpha:X2}" : "")}";

    private static string FmtColor(SKColor? c) => c.HasValue ? FmtColor(c.Value) : "none";

    private static string FmtBoxShadow(BoxShadowValue s) =>
        $"{(s.Inset ? "inset " : "")}{FmtColor(s.Color)} {s.OffsetX:F1}px {s.OffsetY:F1}px {s.BlurRadius:F1}px{(s.Spread != 0 ? $" {s.Spread:F1}px" : "")}";

    // ==================== Quick Report ====================

    public string GenerateQuickReport(Document document)
    {
        _sb.Clear();
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return "No root element";

        CountAllElements(root, out int total, out int withBox, out int withoutBox,
            out int block, out int @inline, out int flex, out int grid, out int table, out int list, out int none);

        _sb.AppendLine($"Elements: {total} total, {withBox} with box, {withoutBox} without box");
        _sb.AppendLine($"Display: block={block} inline={@inline} flex={flex} grid={grid} table={table} list={list} none={none}");

        var body = document.Body;
        if (body?.LayoutBox != null)
        {
            _sb.AppendLine($"Body: {FmtRect(body.LayoutBox.ContentBox)}");
        }

        return _sb.ToString();
    }
}

public static class StringExtensions
{
    public static string Truncate(this string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= maxLength ? s : s[..maxLength] + "...";
    }
}
