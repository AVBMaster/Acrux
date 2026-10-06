using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Layout;
using Acrux.Core.Layout.Geometry;
using Acrux.Core.Css;
using Acrux.Core.Performance;
using Acrux.Core.Performance.Resources;
using System.Globalization;
using System.Text;

namespace Acrux.Rendering;

public class PaintVisitor
{
    private readonly DisplayList _displayList = new();
    private readonly BoxPainterBase _boxPainter;
    private readonly OutlinePainter _outlinePainter;
    private readonly InlineBoxFragmentPainter _inlinePainter;
    private readonly PrePaintTreeWalk _prePaintTreeWalk = new();
    private readonly PaintLayerClipper _layerClipper;
    private readonly FramePainter _framePainter;
private readonly ScrollableAreaPainter _scrollableAreaPainter;
    private readonly ImagePainter _imagePainter;
    private readonly ReplacedPainter _replacedPainter;
    private readonly HighlightPainter _highlightPainter;
    private readonly MaskPainter _maskPainter;
    private readonly LinkHighlightImpl _linkHighlight;
    public DisplayList OverlayList => _overlayList;
    private readonly DisplayList _overlayList = new();
    private SKTypeface _defaultTypeface = SKTypeface.Default;
    private Dictionary<string, SKTypeface> _typefaceCache;     // cached typefaces per family:weight
    private ImageCache _imageCache;
    private float _contentOffsetY;
    private float _viewportWidth;
    private float _viewportHeight;
    private Core.Dom.Document? _currentDocument;
    private string[]? _fontFamilies;
    private string? _baseUrl;
    private Core.Dom.Element? _focusedElement;
    private int _inputCursorPos;
    private int _inputSelStart = -1;
    private bool _inputShowCursor = true;
    private bool _inputImeComposing;
    private string _inputImeComposition = "";
    private int _inputImeCursor;
    private Core.Dom.TextNode? _selAnchorNode;
    private int _selAnchorOffset;
    private Core.Dom.TextNode? _selFocusNode;
    private int _selFocusOffset;
    private bool _hasSelection;
    private bool _selStartIsAnchor;
    private bool _skipInputTextOverlay;
    private bool _passwordRevealed;
    private float _mouseX = float.MinValue;
    private float _mouseY = float.MinValue;
    private bool _mouseDown;
    private string? _pressedControl;
    private Core.Dom.Element? _pressedButton;
    private Core.Dom.Element? _activeSelect;
    private SKRect _selectDropdownRect;
    private List<(Core.Dom.Element Option, SKRect Rect)>? _selectOptionRects;
    private int _selectHoverIndex = -1;
    // Persistent horizontal scroll offset of the focused single-line input and
    // vertical scroll offset of the focused textarea. They are owned/updated by
    // BrowserApp so click->caret mapping and the painter always agree; otherwise
    // the caret drifts increasingly as the text grows.
    private float _inputScrollOffset;
    private float _textareaScrollY;
    private bool _textareaUserScroll;
    /// <summary>When building a scroll-layer raster, do not bake the container's
    /// scroll translate (content is emitted at scroll = 0).</summary>
    private bool _skipScrollBake;
    /// <summary>When building a scroll-layer raster, do not paint the container's
    /// scrollbar (the live DrawScrollLayerOp draws it at the current offset).</summary>
    private bool _skipScrollbar;
    /// <summary>The scroll container whose own background/border/outline must be
    /// excluded from the layer raster (its decorations are painted statically by the
    /// main pass).</summary>
    private Element? _skipSelfDecorationsRoot;
    /// <summary>
    /// Device pixel ratio (DPR × resolution scale) the compositor rasterizes at.
    /// Scroll layers are rasterized at this resolution so they are pixel-sharp
    /// instead of being upscaled (blurry) like a logical-resolution bitmap.
    /// </summary>
    public float PhysicalScale { get; set; } = 1f;

    /// <summary>
    /// When true the visitor never registers/disposes shared scroll layers.
    /// Background tab workers set this so they don't mutate the UI thread's
    /// <see cref="ScrollLayerCache"/> (which the UI clears + disposes on its own
    /// rebuilds). Their scrollers fall back to the inline repaint path.
    /// </summary>
    public bool DisableScrollLayers { get; set; }

    public string? BaseUrl => _baseUrl;

    public PaintVisitor(float contentOffsetY = 0,
        Dictionary<string, SKTypeface>? sharedTypefaceCache = null,
        ImageCache? sharedImageCache = null,
        string[]? fontFamilies = null,
        string? baseUrl = null,
        float viewportWidth = 0,
        float viewportHeight = 0)
    {
        _contentOffsetY = contentOffsetY;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _boxPainter = new BoxPainterBase(_displayList);
        _outlinePainter = new OutlinePainter(_displayList);
        _inlinePainter = new InlineBoxFragmentPainter(this);
        _layerClipper = new PaintLayerClipper(_displayList);
        _framePainter = new FramePainter(_displayList);
_scrollableAreaPainter = new ScrollableAreaPainter(_displayList);
        _imageCache = sharedImageCache ?? new ImageCache();
        _imagePainter = new ImagePainter(_displayList, _imageCache, baseUrl);
        _replacedPainter = new ReplacedPainter(_displayList, _imageCache, baseUrl);
        InstallReplacedIntrinsicSizes(_imageCache, baseUrl);
        _highlightPainter = new HighlightPainter(_displayList);
        _maskPainter = new MaskPainter(_imageCache, baseUrl);
        _linkHighlight = new LinkHighlightImpl(_displayList);
        _defaultTypeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _typefaceCache = sharedTypefaceCache ?? new Dictionary<string, SKTypeface>();
        _fontFamilies = fontFamilies;
        _baseUrl = baseUrl;
    }

    public void SetFocusedElement(Core.Dom.Element? element) => _focusedElement = element;

    /// <summary>
    /// Wire the layout-side seam that asks the decoder for a replaced element's
    /// intrinsic size. Layout runs before painting, so the host must install this
    /// before the first layout pass of a document.
    /// </summary>
    public static void InstallReplacedIntrinsicSizes(ImageCache imageCache, string? baseUrl) =>
        Core.Layout.ReplacedIntrinsicSizes.Resolver = BuildReplacedIntrinsicResolver(imageCache, baseUrl);

    /// <summary>
    /// The layout seam for one document: sources resolve against <paramref name="baseUrl"/>
    /// and decode through <paramref name="imageCache"/>. Hosts that own more than one engine
    /// in a process keep one of these per engine and bracket their layout pass with
    /// <see cref="Acrux.Core.Layout.ReplacedIntrinsicSizes.Use"/> so a tab never measures an
    /// image with another tab's cache.
    /// </summary>
    public static Func<string?, Core.Layout.Geometry.PhysicalSize?> BuildReplacedIntrinsicResolver(
        ImageCache imageCache, string? baseUrl) => source =>
    {
        var resolved = UrlResolver.Resolve(source, baseUrl);
        if (resolved == null)
            return null;
        var task = imageCache.GetImageAsync(resolved);
        if (!task.Wait(TimeSpan.FromSeconds(2)) || task.Result == null)
            return null;
        return new Core.Layout.Geometry.PhysicalSize(task.Result.Width, task.Result.Height);
    };

    /// <summary>
    /// Opt-in link tap-flash overlay. NOT invoked by the default paint pipeline;
    /// the app/input layer calls this (typically during pointer-down on an
    /// anchor) to paint a translucent rounded flash around the box. The default
    /// look can be tuned via the optional parameters.
    /// </summary>
    public void PaintLinkHighlight(Core.Dom.Element element, float radius = LinkHighlightImpl.DefaultRadius,
        float outset = LinkHighlightImpl.DefaultOutset, SkiaSharp.SKColor? color = null)
    {
        var box = element.LayoutBox;
        if (box == null) return;
        var bounds = box.MarginBox;
        bounds.Left += TotalOffsetX;
        bounds.Top += TotalOffsetY;
        _linkHighlight.Paint(bounds, radius, outset, color);
    }
    public void SetSkipInputTextOverlay(bool skip) => _skipInputTextOverlay = skip;
    public void SetPasswordRevealed(bool revealed) => _passwordRevealed = revealed;
    public void SetMouseState(float x, float y, bool isDown, string? pressedControl = null)
    {
        _mouseX = x;
        _mouseY = y;
        _mouseDown = isDown;
        _pressedControl = pressedControl;
    }
    public void SetPressedButton(Core.Dom.Element? element) => _pressedButton = element;
    public void SetInputScrollOffset(float offset) => _inputScrollOffset = offset;
    public void SetTextAreaScrollY(float scrollY) => _textareaScrollY = scrollY;
    public void SetTextAreaUserScroll(bool value) => _textareaUserScroll = value;

    public void SetSelectDropdown(Core.Dom.Element? select, SKRect dropdownRect,
        List<(Core.Dom.Element Option, SKRect Rect)>? optionRects, int hoverIndex)
    {
        _activeSelect = select;
        _selectDropdownRect = dropdownRect;
        _selectOptionRects = optionRects;
        _selectHoverIndex = hoverIndex;
    }

    public void SetInputState(int cursorPos, int selStart, bool showCursor,
        bool isImeComposing, string imeComposition, int imeCursor)
    {
        _inputCursorPos = cursorPos;
        _inputSelStart = selStart;
        _inputShowCursor = showCursor;
        _inputImeComposing = isImeComposing;
        _inputImeComposition = imeComposition;
        _inputImeCursor = imeCursor;
    }

    public void SetSelectionRange(Core.Dom.TextNode? anchorNode, int anchorOffset, Core.Dom.TextNode? focusNode, int focusOffset)
    {
        _selAnchorNode = anchorNode;
        _selAnchorOffset = anchorOffset;
        _selFocusNode = focusNode;
        _selFocusOffset = focusOffset;
        _hasSelection = anchorNode != null && focusNode != null;
        if (_hasSelection)
        {
            int cmp = CompareDomPosition(anchorNode!, focusNode!);
            _selStartIsAnchor = cmp <= 0;
        }
        _highlightPainter.SetSelectionRange(anchorNode, anchorOffset, focusNode, focusOffset);
    }

    private SKRect? GetSelHighlight(Core.Dom.TextNode? runNode, string runText, SKRect runBounds,
        float fontSize, string fontFamily, Core.Dom.FontWeight fontWeight, int runStartOffset = 0)
    {
        if (!_hasSelection || runNode == null) return null;

        int startOff, endOff;

        if (_selAnchorNode == _selFocusNode)
        {
            if (runNode != _selAnchorNode) return null;
            startOff = Math.Min(_selAnchorOffset, _selFocusOffset);
            endOff = Math.Max(_selAnchorOffset, _selFocusOffset);
        }
        else
        {
            Core.Dom.TextNode? startNode, endNode;
            if (_selStartIsAnchor)
            {
                startNode = _selAnchorNode;
                startOff = _selAnchorOffset;
                endNode = _selFocusNode;
                endOff = _selFocusOffset;
            }
            else
            {
                startNode = _selFocusNode;
                startOff = _selFocusOffset;
                endNode = _selAnchorNode;
                endOff = _selAnchorOffset;
            }

            if (runNode == startNode)
            {
                int localStart = Math.Max(0, startOff - runStartOffset);
                if (localStart >= runText.Length) return null;
                return GetSubRunBounds(runText, runBounds, localStart, runText.Length, fontSize, fontFamily, fontWeight);
            }
            if (runNode == endNode)
            {
                int localEnd = Math.Max(0, Math.Min(runText.Length, endOff - runStartOffset));
                if (localEnd <= 0) return null;
                return GetSubRunBounds(runText, runBounds, 0, localEnd, fontSize, fontFamily, fontWeight);
            }
            if (IsNodeBetween(runNode, startNode, endNode))
                return runBounds;
            return null;
        }

        // Single-node: convert global offsets to local offsets for this run
        int snLocalStart = Math.Max(0, startOff - runStartOffset);
        int snLocalEnd = Math.Max(0, Math.Min(runText.Length, endOff - runStartOffset));
        if (snLocalStart >= snLocalEnd) return null;
        return GetSubRunBounds(runText, runBounds, snLocalStart, snLocalEnd, fontSize, fontFamily, fontWeight);
    }

    private SKRect? GetSubRunBounds(string text, SKRect runBounds, int startOff, int endOff,
        float fontSize, string fontFamily, Core.Dom.FontWeight fontWeight)
    {
        if (startOff >= endOff || string.IsNullOrEmpty(text)) return null;
        int clampedStart = Math.Clamp(startOff, 0, text.Length);
        int clampedEnd = Math.Clamp(endOff, clampedStart, text.Length);
        if (clampedStart >= clampedEnd) return null;

        float left = runBounds.Left;
        if (clampedStart > 0)
        {
            string before = text[..clampedStart];
            left += MeasureTextWidth(before, fontSize, fontFamily, fontWeight);
        }
        float right = runBounds.Left;
        if (clampedEnd <= text.Length)
        {
            string upToEnd = text[..clampedEnd];
            right += MeasureTextWidth(upToEnd, fontSize, fontFamily, fontWeight);
        }
        else
        {
            right = runBounds.Right;
        }

        return new SKRect(left, runBounds.Top, right, runBounds.Bottom);
    }

    private static float MeasureTextWidth(string text, float fontSize, string fontFamily, Core.Dom.FontWeight weight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (Core.Layout.TextMeasurer.Instance != null)
            return Core.Layout.TextMeasurer.Instance.MeasureText(text, fontFamily, fontSize, weight);
        return text.Length * fontSize * 0.45f;
    }

    private static bool IsNodeBetween(Core.Dom.Node? target, Core.Dom.Node? a, Core.Dom.Node? b)
    {
        return CompareDomPosition(target, a) > 0 && CompareDomPosition(target, b) < 0;
    }

    private static int CompareDomPosition(Core.Dom.Node? a, Core.Dom.Node? b)
    {
        if (a == null || b == null) return a == b ? 0 : (a == null ? -1 : 1);
        if (a == b) return 0;
        var aPath = new List<Core.Dom.Node>();
        var bPath = new List<Core.Dom.Node>();
        var cur = a;
        while (cur != null) { aPath.Add(cur); cur = cur.ParentNode; }
        cur = b;
        while (cur != null) { bPath.Add(cur); cur = cur.ParentNode; }
        aPath.Reverse();
        bPath.Reverse();
        int depth = Math.Min(aPath.Count, bPath.Count);
        for (int i = 0; i < depth; i++)
        {
            if (aPath[i] != bPath[i])
            {
                var parent = aPath[i].ParentNode;
                if (parent != null)
                {
                    int ai = parent.Children.IndexOf(aPath[i]);
                    int bi = parent.Children.IndexOf(bPath[i]);
                    return ai.CompareTo(bi);
                }
                return 0;
            }
        }
        return aPath.Count.CompareTo(bPath.Count);
    }
    private float TotalOffsetY => _contentOffsetY;
    private float TotalOffsetX => 0;

    public DisplayList GetDisplayList() => _displayList;

    internal BoxPainterBase BoxPainter => _boxPainter;
    internal Document? GetCurrentDocument() => _currentDocument;
    internal ImageCache ImageCache => _imageCache;

    internal void PaintLayerBackgroundFill(Element element, ComputedStyle style, SKRect borderRect)
    {
        bool transfersToView = _currentDocument != null &&
            ViewPainter.BackgroundTransfersToView(element, _currentDocument);
        DrawElementBackground(element, element.LayoutBox!, style, borderRect, transfersToView);
    }

    internal void PaintLayerBorder(Element element, ComputedStyle style, SKRect borderRect)
    {
        DrawElementBorder(element, element.LayoutBox!, style, borderRect);
    }

    internal void PaintLayerOutline(Element element, ComputedStyle style, SKRect borderRect)
    {
        DrawElementOutline(element, element.LayoutBox!, style, borderRect);
    }

    internal void PaintLayerContent(Element element, ComputedStyle style, LayoutBox box, SKRect offsetBorderBox)
    {
        DrawElementContent(element, box, style);
    }

    internal void PaintLayerScrollbar(LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        DrawScrollbar(box, style);
    }

    public void RebuildOverlay()
    {
        // Cheap: clear overlay list and rebuild only the focused input's text/cursor/selection ops.
        // This is ~O(1) — a single element — vs the O(n) DOM walk of a full BuildDisplayList.
        _overlayList.Clear();
        if (_focusedElement == null || !_focusedElement.IsTextEditable) return;
        if (_focusedElement.LayoutBox == null) return;
        var style = _focusedElement.ComputedStyle;
        if (style == null) return;
        var box = _focusedElement.LayoutBox;
        // Override skipContent: route directly into overlay list
        bool savedSkip = _skipInputTextOverlay;
        _skipInputTextOverlay = true;
        if (_focusedElement.TagName.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase))
            DrawTextAreaElement(_focusedElement, box, style);
        else
            DrawInputElement(_focusedElement, box, style);
        _skipInputTextOverlay = savedSkip;
    }

    public void RenderOverlay(SKCanvas canvas)
    {
        // Composite overlay on top of the already-drawn page skeleton.
        // The overlay list contains only the focused input's text, cursor, and selection.
        _overlayList.Execute(canvas);
    }

    private SKTypeface GetTypeface(string family, FontWeight weight)
    {
        var key = $"{family}:{weight}";
        if (!_typefaceCache.TryGetValue(key, out var typeface))
        {
            // family may be a CSS font-family LIST — resolve the first installed
            // entry so the whole list doesn't fail the family lookup.
            var fontName = PrimaryFamily(family);
            var families = _fontFamilies ?? SKFontManager.Default.FontFamilies.ToArray();
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var style = SKFontManager.Default.GetFontStyles(index);
                typeface = style.CreateTypeface(0) ?? _defaultTypeface;
            }
            else
                typeface = _defaultTypeface;
            _typefaceCache[key] = typeface ?? _defaultTypeface ?? SKTypeface.Default;
        }
        return typeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    /// <summary>First entry of a CSS font-family list (quotes/whitespace stripped).</summary>
    private static string PrimaryFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return "sans-serif";
        int comma = fontFamily.IndexOf(',');
        var first = comma >= 0 ? fontFamily[..comma] : fontFamily;
        first = first.Trim().Trim('"', '\'');
        return first.Length == 0 ? "sans-serif" : first;
    }

    /// <summary>
    /// Effective view (canvas) background color after propagation/blending. The
    /// tile compositor fills its dead-viewport strips with this so overscrolled
    /// blank areas blend seamlessly with the page background instead of a hard
    /// white that may not match a tinted page.
    /// </summary>
    public SKColor ViewBackgroundColor { get; private set; } = SKColors.White;

    /// <summary>
    /// Transliteration of ViewPainter::PaintBoxDecorationBackground (view_painter.cc).
    /// Paints the canvas (viewport/document) background: the base background color
    /// (white, matching SkiaRenderer's canvas.Clear) blended with the propagated
    /// root element background. The paint rect covers the union of the visible
    /// viewport content area and the whole document so no gap appears while scrolling.
    /// </summary>
    private void PaintViewBackground(Document document)
    {
        var background = _framePainter.PaintBackground(
            document, _viewportWidth, _viewportHeight, _contentOffsetY);
        if (background == null) return;

        var bgStyle = background.Style;
        var effective = ViewPainter.Blend(SKColors.White, bgStyle.BackgroundColor ?? SKColors.Transparent);
        if (effective.Alpha > 0)
            ViewBackgroundColor = effective;

        // Propagated background-image layers paint over the same rect. CSS
        // background-clip is ignored for the canvas — layers expand to cover the
        // whole canvas (see view_painter.cc PaintRootElementGroup).
        var bgRect = background.CanvasRect;
        bool hasImage = bgStyle.BackgroundImage is { Count: > 0 } && bgStyle.BackgroundImage!.Any(s => s != "none");
        if (hasImage)
        {
            if (bgStyle.BackgroundImage!.Any(s => s.Contains("gradient", StringComparison.OrdinalIgnoreCase)))
                DrawGradientBackground(bgStyle, bgRect);
            else
                DrawBackgroundImage(background.SourceElement, bgStyle, bgRect);
        }
    }

    public void VisitDocument(Document document)
    {
        _currentDocument = document;
        PaintViewBackground(document);
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;
        VisitElement(root);
        foreach (var child in root.Children)
            if (child is Element element)
                VisitElement(element);

        // Select dropdown draws last so it appears above all page content.
        if (_activeSelect != null)
            DrawSelectDropdown();
    }

    /// <summary>
    /// Paints the document using the CSS stacking-context paint order.
    /// Builds a PaintLayer tree and paints each layer in CSS stacking order
    /// (background → negative z → block bg → float → inline → auto z → positive z).
    /// </summary>
    public void VisitDocumentStacking(Document document)
    {
        _currentDocument = document;
        PaintViewBackground(document);
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;

        // Rebuild paint properties before collecting paint layers. This mirrors
        // the browser lifecycle: layout -> pre-paint property walk -> paint.
        _prePaintTreeWalk.Walk(root);

        _stackingPaint = true;
        _stackingLayers = new Dictionary<Element, bool>();
        _stackingLayerTree = new PaintLayerTree();
        _stackingLayerTree.Build(document);

        // Paint the root element (establishes the base stacking context)
        VisitElement(root);

// Paint remaining layers in stacking order
        var processed = new HashSet<Element> { root };
        foreach (var layer in _stackingLayerTree.GetPaintOrder())
        {
            if (layer.Element == root || processed.Contains(layer.Element)) continue;

            // Layers nested inside a layered scroll container are already baked
            // into its cached image — painting them again would double it.
            if (IsInsideLayeredScroller(layer.Element))
            {
                processed.Add(layer.Element);
                continue;
            }

            // P2-2b: viewport culling at paint-layer granularity — a stacking
            // context whose subtree lies entirely outside the cull rect emits no
            // ops at all (mirrors what CullRectUpdater feeds into layer
            // painting). Fixed/sticky descendants establish their own layers and
            // are tested independently, so they are never lost.
            if (_cullRect.HasValue)
            {
                var b = layer.Element.LayoutBox?.BorderBox ?? SKRect.Empty;
                if (b.Width <= 0 && b.Height <= 0 && !LayerHasPaintableContent(layer))
                {
                    processed.Add(layer.Element);
                    continue;
                }
                b.Inflate(CullMarginPx, CullMarginPx);
                b.Offset(0, TotalOffsetY);
                if (!_cullRect.Value.IntersectsWith(b))
                {
                    processed.Add(layer.Element);
                    continue;
                }
            }

            var layerPainter = new PaintLayerPainter(this, layer);
            layerPainter.Paint(TotalOffsetY);
            processed.Add(layer.Element);
        }

        _stackingPaint = false;
        _stackingLayers = null;
        _stackingLayerTree = null;

        if (_activeSelect != null)
            DrawSelectDropdown();
    }

    /// <summary>Cull-margin for shadows / glyph bleed / image overscan.</summary>
    private const float CullMarginPx = 300f;
    private SKRect? _cullRect;

    /// <summary>
    /// Page-space rectangle that needs painting (typically the viewport plus a
    /// margin). Null disables culling — headless snapshots paint the full page.
    /// </summary>
    public void SetCullRect(SKRect? pageSpaceRect) => _cullRect = pageSpaceRect;

    /// <summary>
    /// Paint a single element subtree into this visitor's display list (page
    /// space, current scroll offsets). Used by the element-scroll fast path to
    /// re-paint one scroll container in isolation instead of walking the whole
    /// document — bounded to the subtree, never the page.
    /// </summary>
    public void PaintElementSubtree(Element element, Document? document, SKRect? cullRect = null)
    {
        if (document != null)
            _currentDocument = document;
        var savedCull = _cullRect;
        _cullRect = cullRect;
        try
        {
            VisitElement(element);
        }
        finally
        {
            _cullRect = savedCull;
        }
    }

    private static bool LayerHasPaintableContent(PaintLayer layer) => false;

    private bool _stackingPaint;
    private Dictionary<Element, bool>? _stackingLayers;
    private PaintLayerTree? _stackingLayerTree;

    internal void VisitElement(Element element)
    {
        var layoutBox = element.LayoutBox;

        // CSS Content Distribution 1 §4: an element below a 'content-visibility: hidden'
        // ancestor paints nothing at all — not even its own decoration, which is why this
        // test is separate from the contents-only one in DrawElementContent.
        if (layoutBox is { InHiddenSubtree: true })
            return;

        // If no layout box (e.g. <tr>, <thead>, <tbody>), descendants still need
        // to be painted when the layer tree is driving the traversal.
        if (layoutBox == null)
        {
            foreach (var child in element.Children)
            {
                if (child is Element childElement && childElement.ComputedStyle != null && childElement.ComputedStyle.Display != DisplayType.None)
                    VisitElement(childElement);
            }
            return;
        }
        var style = element.ComputedStyle;
        if (style == null) return;
        if (style.Display == DisplayType.None)
            return;

        bool isVisibilityHidden = style.Visibility == VisibilityType.Hidden;

        var offsetBorderBox = new SKRect(
            layoutBox.BorderBox.Left,
            layoutBox.BorderBox.Top + TotalOffsetY,
            layoutBox.BorderBox.Right,
            layoutBox.BorderBox.Bottom + TotalOffsetY);

        // Compute sticky offset
        float stickyOffsetX = 0, stickyOffsetY = 0;
        if (layoutBox.IsSticky && layoutBox.Parent is LayoutBox parentBox && parentBox.IsScrollContainer)
        {
            float normalTop = layoutBox.BorderBox.Top;
            if (parentBox.ScrollY > normalTop - layoutBox.StickyTop)
                stickyOffsetY = parentBox.ScrollY - normalTop + layoutBox.StickyTop;
            if (parentBox.ScrollX > layoutBox.BorderBox.Left - layoutBox.StickyLeft)
                stickyOffsetX = parentBox.ScrollX - layoutBox.BorderBox.Left + layoutBox.StickyLeft;
        }

        var viewportCullRect = new SKRect(0, TotalOffsetY, _viewportWidth, TotalOffsetY + _viewportHeight);
        if (viewportCullRect.Width <= 0 || viewportCullRect.Height <= 0)
            viewportCullRect = SKRect.Create(float.MinValue / 2, float.MinValue / 2, float.MaxValue, float.MaxValue);
        using var objectPaintState = new ScopedPaintState(
            _displayList, new SKPoint(TotalOffsetX, TotalOffsetY), viewportCullRect);

        {
            PushObjectEffects(style, layoutBox, offsetBorderBox, objectPaintState);

            bool isInline = style.Display == DisplayType.Inline;
            bool skipSelfDecorations = ReferenceEquals(element, _skipSelfDecorationsRoot);
            bool hideCell = TablePainter.ShouldHideEmptyCell(element, style);
            if (!isInline && !isVisibilityHidden && !skipSelfDecorations && !hideCell)
            {
                bool transfersToView = _currentDocument != null &&
                    ViewPainter.BackgroundTransfersToView(element, _currentDocument);
                DrawElementBackground(element, layoutBox, style, offsetBorderBox, transfersToView);
                DrawElementBorder(element, layoutBox, style, offsetBorderBox);
                DrawElementOutline(element, layoutBox, style, offsetBorderBox);
            }

            if (!isVisibilityHidden && element.TagName.Equals("HR", StringComparison.OrdinalIgnoreCase))
            {
                float y = layoutBox.ContentBox.Top + TotalOffsetY + layoutBox.ContentBox.Height / 2;
                float x1 = layoutBox.ContentBox.Left;
                float x2 = layoutBox.ContentBox.Right;
                var lineOp = PaintOpPool.GetDrawLineOp();
                lineOp.X1 = x1;
                lineOp.Y1 = y;
                lineOp.X2 = x2;
                lineOp.Y2 = y;
                lineOp.Color = style.BorderTopColor;
                lineOp.StrokeWidth = style.BorderTopWidth;
                lineOp.Bounds = new SKRect(x1, y - 1, x2, y + 1);
                _displayList.Add(lineOp);
            }

            // Draw disclosure triangle for <summary> elements
            if (!isVisibilityHidden && element.TagName.Equals("SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                float arrowSize = Math.Min(10, layoutBox.ContentBox.Height * 0.6f);
                float arrowX = layoutBox.ContentBox.Left + 4;
                float arrowY = layoutBox.ContentBox.Top + TotalOffsetY + (layoutBox.ContentBox.Height - arrowSize) / 2;

                // Check if parent <details> has 'open' attribute
                bool isOpen = false;
                var detailsParent = element.ParentElement;
                while (detailsParent != null && detailsParent.TagName != "DETAILS")
                    detailsParent = detailsParent.ParentElement;
                if (detailsParent != null && detailsParent.HasAttribute("open"))
                    isOpen = true;

                using var arrowPath = new SKPathBuilder();
                if (isOpen)
                {
                    // Downward-pointing triangle
                    arrowPath.MoveTo(arrowX, arrowY);
                    arrowPath.LineTo(arrowX + arrowSize, arrowY);
                    arrowPath.LineTo(arrowX + arrowSize * 0.5f, arrowY + arrowSize);
                    arrowPath.Close();
                }
                else
                {
                    // Rightward-pointing triangle
                    arrowPath.MoveTo(arrowX, arrowY);
                    arrowPath.LineTo(arrowX, arrowY + arrowSize);
                    arrowPath.LineTo(arrowX + arrowSize, arrowY + arrowSize * 0.5f);
                    arrowPath.Close();
                }

                var arrowPathFinal = arrowPath.Detach();
                var arrowOp = PaintOpPool.GetDrawPathOp();
                arrowOp.Path = arrowPathFinal;
                arrowOp.FillPaint = new SKPaint { Color = style.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
                arrowOp.Bounds = new SKRect(arrowX, arrowY, arrowX + arrowSize, arrowY + arrowSize);
                _displayList.Add(arrowOp);
            }

            bool hasOverflowHidden = style.Overflow == OverflowType.Hidden || style.OverflowX == OverflowType.Hidden || style.OverflowY == OverflowType.Hidden;
            bool isScrollContainer = layoutBox.IsScrollContainer &&
                (layoutBox.ScrollContentHeight > layoutBox.ContentBox.Height || layoutBox.ScrollContentWidth > layoutBox.ContentBox.Width);

            // ── Scroll-layer fast path ─────────────────────────────────────────
            // Eligible scroll containers are rasterized once into a cached layer
            // (scroll = 0) and drawn via DrawScrollLayerOp at the live scroll
            // offset. Their content is therefore NOT emitted into the page's
            // display list, so element scrolling never re-paints the document.
            bool layerEligible = isScrollContainer && !_skipScrollBake && !_skipScrollbar
                && ScrollLayerEligible(element, layoutBox, style);
            if (layerEligible && !ScrollLayerCache.IsLayered(layoutBox))
                BuildScrollLayer(element, layoutBox, style);
            if (layerEligible && ScrollLayerCache.IsLayered(layoutBox))
            {
                // Layered container: emit the scroll-layer op (used by direct /
                // snapshot renderers) and leave a hole in the tiled page — the tile
                // compositor draws the cached layer LIVE at the current scroll.
                EmitScrollLayerOp(layoutBox);
            }
            else
            {
            bool needsClip = hasOverflowHidden || isScrollContainer;
            using (var contentsPaintState = new ScopedPaintState(
                _displayList, objectPaintState.PaintOffset, objectPaintState.CullRect))
            {
            if (needsClip)
            {
                // Scroll containers reserve scrollbar space (the content box was
                // shrunken by the bar thickness during layout), so clip contents
                // to the content box — content must never paint underneath the
                // scrollbar strip. Plain overflow:hidden clips at the padding box,
                // and that box is rounded by border-radius (CSS Backgrounds 3 4).
                var clipRect = isScrollContainer
                    ? new SKRect(
                        layoutBox.ContentBox.Left,
                        layoutBox.ContentBox.Top + TotalOffsetY,
                        layoutBox.ContentBox.Right,
                        layoutBox.ContentBox.Bottom + TotalOffsetY)
                    : new SKRect(
                        layoutBox.PaddingBox.Left,
                        layoutBox.PaddingBox.Top + TotalOffsetY,
                        layoutBox.PaddingBox.Right,
                        layoutBox.PaddingBox.Bottom + TotalOffsetY);
                bool clippedToRadius = false;
                bool clipShapeIsRounded = false;
                PhysicalBoxStrut clipOutsets = default;
                {
                    // The clip box's own corner radii: the border radii inset by whatever
                    // lies between the border box and this box. The deltas come from the
                    // laid-out boxes so resolved paddings are used as-is.
                    var clipBorderRect = new SKRect(
                        layoutBox.BorderBox.Left, layoutBox.BorderBox.Top + TotalOffsetY,
                        layoutBox.BorderBox.Right, layoutBox.BorderBox.Bottom + TotalOffsetY);
                    SKRect target = isScrollContainer ? layoutBox.ContentBox : layoutBox.PaddingBox;
                    clipOutsets = new PhysicalBoxStrut(
                        target.Top - layoutBox.BorderBox.Top,
                        layoutBox.BorderBox.Right - target.Right,
                        layoutBox.BorderBox.Bottom - target.Bottom,
                        target.Left - layoutBox.BorderBox.Left);
                    var clipShape = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(
                        style, clipBorderRect, clipOutsets);
                    clipShapeIsRounded = clipShape.IsRounded;
                    if (clipShape.IsRounded && !clipShape.IsEmpty)
                    {
                        using var clipPath = clipShape.ToPath(true);
                        clippedToRadius = contentsPaintState.PushClipPath(clipPath);
                    }
                }
                if (!clippedToRadius)
                    contentsPaintState.PushClip(clipRect);
            }

            if (isScrollContainer && !_skipScrollBake)
            {
                // Snap the scroll translate to the device pixel grid (logical
                // offset = round(offset × scale) / scale) so a sub-pixel scroll
                // position doesn't rasterize text at fractional device rows. While
                // the container is smooth-scrolling keep the fractional offset so
                // re-rasterized content moves continuously (no 1px judder).
                float scale = PhysicalScale <= 0.01f ? 1f : PhysicalScale;
                float scrollOffsetX = layoutBox.IsSmoothScrollingX
                    ? -layoutBox.ScrollX
                    : -MathF.Round(layoutBox.ScrollX * scale) / scale;
                float scrollOffsetY = layoutBox.IsSmoothScrollingY
                    ? -layoutBox.ScrollY
                    : -MathF.Round(layoutBox.ScrollY * scale) / scale;
                if (scrollOffsetX != 0 || scrollOffsetY != 0)
                {
                    var scrollBounds = new SKRect(
                        layoutBox.ContentBox.Left,
                        layoutBox.ContentBox.Top + TotalOffsetY,
                        layoutBox.ContentBox.Right,
                        layoutBox.ContentBox.Bottom + TotalOffsetY);
                    contentsPaintState.PushTransform(
                        SKMatrix.CreateTranslation(scrollOffsetX, scrollOffsetY), scrollBounds);
                }
            }

            bool hasStickyOffset = (stickyOffsetX != 0 || stickyOffsetY != 0);
            if (hasStickyOffset)
            {
                // Snap the sticky translate to the device grid too, so a sticky
                // header stays crisp while its scrolled container moves.
                float scale = PhysicalScale <= 0.01f ? 1f : PhysicalScale;
                stickyOffsetX = MathF.Round(stickyOffsetX * scale) / scale;
                stickyOffsetY = MathF.Round(stickyOffsetY * scale) / scale;
                contentsPaintState.PushTransform(
                    SKMatrix.CreateTranslation(stickyOffsetX, stickyOffsetY), offsetBorderBox);
            }

            if (!isInline && !isVisibilityHidden)
                DrawInlineChildrenDecorations(element);

            DrawElementContent(element, layoutBox, style);

            }

            // Draw scrollbar for scroll containers:
            //  overflow: scroll → always show; overflow: auto → only when content overflows
            if (layoutBox.IsScrollContainer && !_skipScrollbar)
            {
                bool overflowXScroll = style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
                bool overflowYScroll = style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
                bool needsScrollY = overflowYScroll || layoutBox.ScrollContentHeight > layoutBox.ContentBox.Height;
                bool needsScrollX = overflowXScroll || layoutBox.ScrollContentWidth > layoutBox.ContentBox.Width;
                if (needsScrollY || needsScrollX)
                {
                    DrawScrollbar(layoutBox, style);
                }
            }

        }

        // In stacking paint mode, the PaintLayerTree controls child ordering.
        // In normal mode, use z-index sorting for correct stacking within each parent.
        if (_stackingPaint)
        {
            // Let the layer tree handle child ordering; don't recurse here.
        }
        else
        {
            // Sort children for correct stacking within each parent: CSS 2.1 §E.2
            // paints the in-flow, non-positioned descendants (step 3), then the
            // floats (step 4), then the positioned descendants (step 8), with
            // negative z-index first (step 2).
            var children = element.Children
                .OfType<Element>()
                .Where(c => c.ComputedStyle != null && c.ComputedStyle.Display != DisplayType.None)
                .ToList();

            foreach (var childElement in ChildrenInStackingOrder(children))
                VisitElement(childElement);
        }

            } // end else (inline scroll-container paint path)

    }

    /// <summary>
    /// Children of one parent in paint order (CSS 2.1 §E.2): negative z-index first,
    /// then the in-flow non-positioned boxes, then the floats, then the positioned
    /// boxes; each group keeps its document order (or z-index order where the spec
    /// sorts by it).
    /// </summary>
    private static IEnumerable<Element> ChildrenInStackingOrder(List<Element> children)
    {
        List<Element>? negative = null, flow = null, floats = null, positioned = null;
        foreach (var child in children)
        {
            var style = child.ComputedStyle!;
            int z = style.ZIndex ?? 0;
            if (z < 0)
                (negative ??= new List<Element>()).Add(child);
            else if (style.Position != PositionType.Static)
                (positioned ??= new List<Element>()).Add(child);
            else if (style.Float != FloatType.None)
                (floats ??= new List<Element>()).Add(child);
            else
                (flow ??= new List<Element>()).Add(child);
        }

        return EnumerateGroups(negative, positioned, floats, flow);

        static IEnumerable<Element> EnumerateGroups(List<Element>? negative, List<Element>? positioned,
            List<Element>? floats, List<Element>? flow)
        {
            if (negative != null)
                foreach (var e in negative.OrderBy(c => c.ComputedStyle!.ZIndex ?? 0))
                    yield return e;
            if (flow != null)
                foreach (var e in flow)
                    yield return e;
            if (floats != null)
                foreach (var e in floats)
                    yield return e;
            if (positioned != null)
                foreach (var e in positioned.OrderBy(c => c.ComputedStyle!.ZIndex ?? 0))
                    yield return e;
        }
    }

    /// <summary>
    /// Rasterize the scrollable content of an eligible scroll container once per
    /// layout into a cached layer image (content-box local, scroll = 0). Runs a
    /// dedicated sub-painter over the container's subtree so the page's display
    /// list never contains the scrolling content; on element scroll the compositor
    /// re-bakes this layer at the live scroll offset instead of re-painting.
    /// </summary>
    private void BuildScrollLayer(Element element, LayoutBox box, ComputedStyle style)
    {
        try
        {
            // Containers with sticky / z-index children cannot encode their live
            // offset at scroll = 0, so their layer bakes the CURRENT scroll and is
            // rebuilt each scroll frame (bounded to this subtree, never the page).
            bool bakeScroll = ScrollLayerCache.SubtreeNeedsBake(box);
            var sub = new PaintVisitor(_contentOffsetY, _typefaceCache, _imageCache,
                _fontFamilies, _baseUrl, _viewportWidth, _viewportHeight)
            {
                _skipScrollBake = !bakeScroll,
                _skipScrollbar = true,
                _skipSelfDecorationsRoot = element,
                _currentDocument = _currentDocument,
                PhysicalScale = PhysicalScale,
            };
            sub.VisitElement(element);
            var scratch = sub.GetDisplayList();
            scratch.SortByZIndex();

            // Raster at DEVICE resolution so the cached layer is not upscaled
            // (blurry) on high-DPI displays.
            float scale = PhysicalScale <= 0.01f ? 1f : PhysicalScale;
            float contentW = box.ScrollContentWidth;
            float contentH = box.ScrollContentHeight;
            int pw = Math.Max(1, (int)MathF.Ceiling(contentW * scale));
            int ph = Math.Max(1, (int)MathF.Ceiling(contentH * scale));
            var contentBox = new SKRect(
                box.ContentBox.Left, box.ContentBox.Top + TotalOffsetY,
                box.ContentBox.Right, box.ContentBox.Bottom + TotalOffsetY);

            var info = new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bmp = new SKBitmap(info);
            using var tc = new SKCanvas(bmp);
            tc.Clear(SKColors.Transparent);
            tc.Scale(scale, scale);
            // Snap the raster origin to the device pixel grid: content at integer
            // layout positions then lands on whole device pixels inside the bitmap
            // (crisp). The compositor draws the layer back at the same snapped
            // origin (DrawLiveScrollLayers uses round(contentBox*scale)), so each
            // baked texel maps to a whole screen pixel — no up-to-half-pixel offset
            // against the surrounding page tiles/borders that reads as seam/shimmer.
            tc.Translate(-MathF.Round(contentBox.Left * scale) / scale,
                         -MathF.Round(contentBox.Top * scale) / scale);
            // LCD (subpixel) text needs an OPAQUE backdrop in the same raster or it
            // fringes color when composited. If the container has a solid opaque
            // background (no image/gradient), bake it into the layer and raster crisp
            // subpixel text; otherwise fall back to grayscale AA so glyphs composite
            // cleanly over the (transparent) backdrop.
            bool solidOpaqueBg = style.BackgroundColor is { Alpha: >= 255 }
                && (style.BackgroundImage == null || style.BackgroundImage.Count == 0
                    || style.BackgroundImage!.All(s => s == "none"));
            bool savedGrayscale = DrawTextOp.LayerBakeGrayscale;
            DrawTextOp.LayerBakeGrayscale = !solidOpaqueBg;
            try
            {
                if (solidOpaqueBg && style.BackgroundColor.HasValue)
                {
                    using var fill = new SKPaint { Color = style.BackgroundColor.Value, Style = SKPaintStyle.Fill, IsAntialias = true };
                    tc.DrawRect(new SKRect(0, 0, Math.Max(1, contentW), Math.Max(1, contentH)), fill);
                }
                scratch.Execute(tc);
            }
            finally
            {
                DrawTextOp.LayerBakeGrayscale = savedGrayscale;
            }
            var img = SKImage.FromBitmap(bmp);

            // Scrollbar appearance (drawn LIVE by the compositor over the layer).
            float thickness = Core.Dom.ScrollbarMetrics.ThicknessFor(style);
            var paddingBox = new SKRect(
                box.PaddingBox.Left, box.PaddingBox.Top + TotalOffsetY,
                box.PaddingBox.Right, box.PaddingBox.Bottom + TotalOffsetY);
            bool hasV = box.ScrollContentHeight > box.ContentBox.Height
                || style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
            bool hasH = box.ScrollContentWidth > box.ContentBox.Width
                || style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
            bool widthNone = style.ScrollbarWidth == ScrollbarWidthType.None;
            // Custom ::-webkit-scrollbar-track/thumb colors take precedence over the
            // standard scrollbar-color, so a layered container's LIVE scrollbar looks
            // identical to the classic inline one.
            var track = style.ScrollbarCustom?.Track?.Background
                        ?? style.ScrollbarTrackColor ?? new SKColor(240, 240, 240);
            var thumb = style.ScrollbarCustom?.Thumb?.Background
                        ?? style.ScrollbarThumbColor ?? new SKColor(180, 180, 180);
            float radius = style.ScrollbarCustom?.Thumb?.BorderRadius ?? MathF.Max(0f, thickness / 2f - 1f);

            ScrollLayerCache.Register(box, img, contentBox, pw, ph,
                paddingBox, thickness, track, thumb, radius,
                widthNone ? false : hasV, widthNone ? false : hasH, bakeScroll);
        }
        catch
        {
            // Layering failed (e.g. allocation) — drop the whole layer set so every
            // container falls back to the inline repaint path next walk.
            ScrollLayerCache.ClearAll();
        }
    }

    /// <summary>
    /// Emit the scroll-layer op so DIRECT rendering paths (non-tile picture,
    /// snapshots) also see the container's content. The tile compositor ignores the
    /// op's baked offset and draws the layer LIVE every frame instead.
    /// </summary>
    internal void EmitScrollLayerOp(LayoutBox box)
    {
        if (!ScrollLayerCache.TryGetInfo(box, out var info)) return;
        var op = new DrawScrollLayerOp
        {
            Image = info.Image,
            Box = box,
            ContentBox = info.ContentBox,
            PaddingBox = info.PaddingBox,
            ScrollbarThickness = info.ScrollbarThickness,
            TrackColor = info.TrackColor,
            ThumbColor = info.ThumbColor,
            ThumbRadius = info.ThumbRadius,
            ShowVertical = info.ShowVertical,
            ShowHorizontal = info.ShowHorizontal,
            IsBaked = info.IsBaked,
            PhysicalScale = PhysicalScale,
            Bounds = info.PaddingBox,
        };
        _displayList.Add(op);
    }

    /// <summary>
    /// Ensure an eligible scroll container has a cached layer image, building one
    /// (once per layout) when it does not. Containers that cannot be layered keep
    /// the inline repaint path. The document root / body (page scroller) and any
    /// element without an explicit overflow:auto/scroll are never layered.
    /// </summary>
    internal void EnsureScrollLayer(Element element, LayoutBox box, ComputedStyle style, bool forceRebuild = false)
    {
        if (_skipScrollBake) return;
        if (!ScrollLayerEligible(element, box, style)) return;
        if (!forceRebuild && ScrollLayerCache.IsLayered(box)) return;
        BuildScrollLayer(element, box, style);
    }

    /// <summary>
    /// Rebuild a baked-mode scroll layer (sticky / z-index children) at its CURRENT
    /// scroll offset. Called by the host on every element-scroll frame; bounded to
    /// this container's subtree, never the whole page.
    /// </summary>
    public void RebuildScrollLayer(LayoutBox box)
    {
        if (box == null || !ScrollLayerCache.IsLayered(box) || !ScrollLayerCache.TryGetInfo(box, out var info) || !info.IsBaked)
            return;
        var element = box.Dimensions?.Element;
        if (element == null) return;
        var style = element.ComputedStyle;
        if (style == null) return;
        BuildScrollLayer(element, box, style);
    }

    /// <summary>Shared eligibility gate for the scroll-layer fast path.</summary>
    private bool ScrollLayerEligible(Element element, LayoutBox box, ComputedStyle style)
    {
        if (DisableScrollLayers) return false;
        if (element == _currentDocument?.DocumentElement || element == _currentDocument?.Body)
            return false;
        bool explicitScroller = style.OverflowY == OverflowType.Auto || style.OverflowY == OverflowType.Scroll
            || style.OverflowX == OverflowType.Auto || style.OverflowX == OverflowType.Scroll
            || style.Overflow == OverflowType.Auto || style.Overflow == OverflowType.Scroll;
        if (!explicitScroller) return false;
        // Custom ::-webkit-scrollbar styles are supported: BuildScrollLayer extracts
        // the bar thickness + track/thumb colors + radius into the layer entry, and
        // the compositor draws the bar LIVE with them. (The live bar renders a flat
        // track + rounded thumb — equivalent look to the classic inline theme.)
        return ScrollLayerCache.Enabled
            && ScrollLayerCache.IsLayerable(box)
            && ScrollLayerCache.SubtreeIsSimple(box);
    }

    /// <summary>True when <paramref name="element"/> lies inside a layered scroll container.</summary>
    private static bool IsInsideLayeredScroller(Element element)
    {
        if (!ScrollLayerCache.HasAny) return false;
        for (var ancestor = element.ParentElement; ancestor != null; ancestor = ancestor.ParentElement)
        {
            var b = ancestor.LayoutBox;
            if (b != null && ScrollLayerCache.IsLayered(b))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Paint layout boxes that were produced by layout but have no DOM element of
    /// their own (anonymous boxes such as multicol columns). Their inline content
    /// is drawn relative to each box's own content rectangle.
    /// </summary>
    private void PaintAnonymousChildBoxes(LayoutBox parent)
    {
        foreach (var child in parent.Children)
        {
            // Skip boxes that belong to a real DOM element: those are painted
            // through VisitElement. Layout-only anonymous boxes either have no
            // element at all (multicol column fragmentainers) or a synthetic
            // element that is not attached to the DOM (anonymous flex items
            // wrapping bare text).
            if (child.Dimensions?.Element is Element el && el.ParentNode != null)
                continue;
            if (child.Lines == null && child.LineRuns == null)
                continue;
            DrawInlineRuns(child);
        }
    }

    private void DrawListMarker(Element element, LayoutBox box, ComputedStyle style)
    {
        bool hasMarkerImage = ExtractListStyleImageUrl(style.ListStyleImage) != null;
        if (style.ListStyleType == ListStyleType.None && !hasMarkerImage) return;

        bool markerInside = style.ListStylePosition == ListStylePosition.Inside;
        LayoutBox? parentBox = null;
        // The ordinal comes from the HTML numbering model, not from a plain
        // sibling count: <ol start>/<ol reversed>/<li value> all move it.
        int ordinal = Core.Layout.List.ListItemNumbering.Ordinal(element);
        if (element.Parent is Element parent)
            parentBox = parent.LayoutBox;
        // Outside markers hang into the parent's padding area, so they need the
        // parent's content edge; inside markers flow with the item's own content.
        if (!markerInside && parentBox == null) return;

        float markerWidth = Acrux.Core.Layout.List.ListMarker.MarkerWidth(
            style.ListStyleType, style.ListStylePosition, style.FontSize);
        float markerHeight = style.FontSize;

        // list-style-image wins over the type glyph; the type is only the
        // fallback when the image is missing or fails to load (CSS Lists 3 §4.1).
        string? markerImageSource = ExtractListStyleImageUrl(style.ListStyleImage);
        SKImage? markerImage = null;
        if (markerImageSource != null)
        {
            var resolved = UrlResolver.Resolve(markerImageSource, _baseUrl);
            if (resolved != null)
            {
                var task = _imageCache.GetImageAsync(resolved);
                if (task.Wait(TimeSpan.FromSeconds(2)))
                    markerImage = task.Result;
            }
            if (markerImage != null)
            {
                markerWidth = markerImage.Width;
                markerHeight = markerImage.Height;
            }
        }

        // The marker sits on the list item's INLINE-START side, which is the right
        // one when the item's own 'direction' is rtl (CSS Lists 3 §4.1 — it is the
        // item's direction, not its parent list's, that governs).
        bool markerRtl = string.Equals(style.Direction, "rtl", StringComparison.OrdinalIgnoreCase);
        // An outside marker's box is right-aligned with the item's content edge: in
        // ltr it hangs to the LEFT of the content edge, in rtl to the RIGHT of it.
        // The text branch below re-places it using the measured box width (which
        // includes the suffix space); this is the image/estimate position.
        float markerX = markerInside
            ? (markerRtl ? box.ContentBox.Right - markerWidth : box.ContentBox.Left)
            : (markerRtl ? box.ContentBox.Right
                         : parentBox!.ContentBox.Left - markerWidth);

        float markerY;
        if (box.Lines != null && box.Lines.Count > 0 && box.Lines[0].Baseline > 0)
            markerY = box.Lines[0].Baseline;
        else
            markerY = box.ContentBox.Top + Core.Fonts.LineBoxMetrics.GetBaseline(style);

        if (markerImage != null)
        {
            // An image marker is centred on the first line box rather than sitting
            // on the text baseline like a glyph.
            float lineTop = box.Lines != null && box.Lines.Count > 0
                ? box.Lines[0].Y
                : box.ContentBox.Top;
            float lineHeight = box.Lines != null && box.Lines.Count > 0
                ? box.Lines[0].Height
                : Core.Fonts.LineBoxMetrics.GetLineHeight(style);
            float imageTop = lineTop + MathF.Max(0, (lineHeight - markerHeight) / 2);
            var imageOp = PaintOpPool.GetDrawImageOp();
            imageOp.Image = markerImage;
            imageOp.SourceRect = new SKRect(0, 0, markerImage.Width, markerImage.Height);
            imageOp.DestRect = new SKRect(markerX, imageTop, markerX + markerWidth, imageTop + markerHeight);
            imageOp.Fit = ImageFit.Fill;
            imageOp.Bounds = imageOp.DestRect;
            _displayList.Add(imageOp);
            return;
        }

        // ::marker may restyle the generated marker (CSS Lists 3 §7.1); only a
        // small set of properties apply, so they are read from the side-car. This
        // runs before the label is measured, because the label's advance is taken
        // in the marker's own font.
        SKColor markerColor = style.Color;
        float markerFontSize = style.FontSize;
        string markerFamily = style.FontFamily ?? Core.Fonts.FontManager.StandardFontFamily;
        if (element.MarkerStyles != null)
        {
            if (element.MarkerStyles.TryGetValue("color", out var colorText))
                markerColor = ColorParser.Parse(colorText);
            if (element.MarkerStyles.TryGetValue("font-size", out var sizeText))
            {
                float resolved = Length.Parse(sizeText.Trim()).ToPixels(style.FontSize, style.FontSize, 0, 0);
                if (!float.IsNaN(resolved) && resolved > 0)
                    markerFontSize = resolved;
            }
            if (element.MarkerStyles.TryGetValue("font-family", out var familyText))
                markerFamily = familyText.Trim();
        }

        // '::marker { content }' replaces the counter label verbatim; 'none' and
        // '""' take the marker away altogether (CSS Lists 3 §5).
        string? markerContent = Core.Layout.List.ListMarker.MarkerContentOf(element);
        var markerDocument = element.OwnerDocument;
        string markerText = Core.Layout.List.ListMarker.ResolveMarkerContent(markerContent, ordinal)
            ?? Core.Layout.List.ListMarkerFormatter.MarkerLabel(style, ordinal, markerDocument);
        if (markerText.Length == 0) return;

        // The advance the marker reserves is its real box: the label measured in
        // the marker's own font (::marker may restyle it), plus the suffix space
        // CSS Counter Styles puts after non-ideographic separators. The estimate
        // alone lets long labels ("MMMCMXCVIII.") overlap the item text.
        float measuredMarkerWidth = MeasureTextWidth(markerText, markerFontSize, markerFamily, style.FontWeight);
        if (measuredMarkerWidth > 0)
        {
            markerWidth = measuredMarkerWidth;
            if (!markerInside)
            {
                string boxText = Core.Layout.List.ListMarker.MarkerBoxText(
                    style, ordinal, markerContent, markerDocument);
                float boxWidth = MeasureTextWidth(boxText, markerFontSize, markerFamily, style.FontWeight);
                float extent = boxWidth > 0 ? boxWidth : measuredMarkerWidth;
                markerX = markerRtl
                    ? box.ContentBox.Right
                    : parentBox!.ContentBox.Left - extent;
            }
            else if (markerRtl)
            {
                // The reserved inside slot hangs off the item's right edge, and the
                // glyph itself is at that slot's inline-start (right) end.
                markerX = box.ContentBox.Right - markerWidth;
            }
        }

        var op = PaintOpPool.GetDrawTextOp();
        op.Text = markerText;
        op.X = markerX;
        op.Y = markerY + TotalOffsetY;
        op.Color = markerColor;
        op.FontSize = markerFontSize;
        op.FontFamily = markerFamily;
        op.Bounds = new SKRect(markerX, markerY, markerX + markerWidth, markerY + markerFontSize);
        _displayList.Add(op);
    }

    /// <summary>Pull the URL out of a list-style-image value ('url(...)' or 'none').</summary>
    private static string? ExtractListStyleImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var trimmed = value.Trim();
        int open = trimmed.IndexOf('(');
        int close = trimmed.LastIndexOf(')');
        if (open < 0 || close <= open)
            return trimmed;
        return trimmed[(open + 1)..close].Trim().Trim('"', '\'');
    }

    private void DrawElementBackground(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect, bool skipBackgroundLayers = false)
    {
        var paddingRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                     borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);
        if (paddingRect.Width <= 0 || paddingRect.Height <= 0) return;

        // Determine the background clip rect based on background-clip property
        SKRect bgClipRect;
        switch (style.BackgroundClip?.ToLowerInvariant())
        {
            case "border-box":
                bgClipRect = borderRect;
                break;
            case "content-box":
                float padL = style.PaddingLeft is PixelLength pl ? pl.Value : 0;
                float padT = style.PaddingTop is PixelLength pt ? pt.Value : 0;
                float padR = style.PaddingRight is PixelLength pr ? pr.Value : 0;
                float padB = style.PaddingBottom is PixelLength pb ? pb.Value : 0;
                bgClipRect = new SKRect(paddingRect.Left + padL, paddingRect.Top + padT,
                                        paddingRect.Right - padR, paddingRect.Bottom - padB);
                if (bgClipRect.Width <= 0) bgClipRect.Right = bgClipRect.Left;
                if (bgClipRect.Height <= 0) bgClipRect.Bottom = bgClipRect.Top;
                break;
            case "padding-box":
                bgClipRect = paddingRect;
                break;
            default:
                // The initial value of background-clip is border-box, so the fill has to
                // reach the outer rounded rect; clipping it to the padding box leaves
                // white slivers between the fill and a rounded border.
                bgClipRect = borderRect;
                break;
        }

        // Push clip to background clip rect
        bool needsClip = style.BackgroundClip != null && style.BackgroundClip != "" &&
                         style.BackgroundClip.ToLowerInvariant() != "padding-box";

        // background-clip: text paints the fill through the element's glyph
        // outlines only (css-backgrounds-3 §4.7). Without text there is nothing
        // to reveal, so the whole background is skipped.
        SKPath? textClipPath = null;
        if (needsClip && style.BackgroundClip!.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            textClipPath = BuildTextClipPath(box);
            if (textClipPath == null)
                return;
        }

        bool rectClip = textClipPath == null && needsClip;
        if (textClipPath != null)
        {
            var layerOp = PaintOpPool.GetPushLayerOp();
            layerOp.ClipPath = textClipPath;
            layerOp.Bounds = borderRect;
            _displayList.Add(layerOp);
        }
        else if (rectClip)
        {
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipRect = bgClipRect;
            clipOp.Bounds = borderRect;
            _displayList.Add(clipOp);
        }

        // Normal box shadows paint first (under the background); inset shadows
        // paint after the background (over it), mirroring box_painter_base.cc.
        _boxPainter.PaintNormalBoxShadow(borderRect, style);

        if (skipBackgroundLayers)
        {
            if (textClipPath != null)
                _displayList.Add(PaintOpPool.GetPopLayerOp());
            else if (rectClip)
                _displayList.Add(PaintOpPool.GetPopClipOp());
            return;
        }

        bool hasBackgroundColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasBackgroundImage = style.BackgroundImage is { Count: > 0 };

        // 'background-blend-mode' mixes the element's own layers together and only
        // the result reaches the page, so the group must be isolated
        // (CSS Backgrounds 4 §6).
        bool blendedBackground = hasBackgroundImage && style.BackgroundBlendMode != BackgroundBlendModeType.Normal;
        if (blendedBackground)
        {
            var blendLayer = PaintOpPool.GetPushLayerOp();
            blendLayer.HasClipRect = true;
            blendLayer.ClipRect = bgClipRect;
            blendLayer.Bounds = borderRect;
            // The backgrounds inside blend against each other and must not reach
            // the page below — that is isolation, which the neutral paint fields
            // alone would not reveal.
            blendLayer.ForceIsolation = true;
            _displayList.Add(blendLayer);
        }

        if (hasBackgroundColor)
        {
            SKColor bgColor = style.BackgroundColor.Value;

            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = bgClipRect;
            op.FillColor = bgColor;
            op.BorderRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius, Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
            op.CornerRadii = ResolveBackgroundCornerRadii(style, bgClipRect);
            op.Bounds = borderRect;
            // The BODY background belongs at the very bottom of the page z-order
            // (just above the canvas), so negative-z-index content such as outset
            // box shadows paints on top of it (CSS 2.1 §E.2).
            if (_currentDocument != null && ReferenceEquals(element, _currentDocument.Body))
                op.ZIndex = int.MinValue + 1;
            _displayList.Add(op);
        }

        if (hasBackgroundImage && style.BackgroundImage is { Count: > 0 })
        {
            if (style.BackgroundImage!.Any(s => s.Contains("gradient", StringComparison.OrdinalIgnoreCase)))
                DrawGradientBackground(style, paddingRect);
            else
                DrawBackgroundImage(element, style, paddingRect);
        }

        if (blendedBackground)
        {
            var popBlend = PaintOpPool.GetPopLayerOp();
            popBlend.Bounds = borderRect;
            _displayList.Add(popBlend);
        }

        _boxPainter.PaintInsetBoxShadowWithBorderRect(borderRect, style);

        // Pop clip if we pushed one
        if (textClipPath != null)
        {
            var popOp = PaintOpPool.GetPopLayerOp();
            popOp.Bounds = borderRect;
            _displayList.Add(popOp);
        }
        else if (rectClip)
        {
            var popOp = PaintOpPool.GetPopClipOp();
            popOp.Bounds = borderRect;
            _displayList.Add(popOp);
        }
    }

    /// <summary>
    /// Union of the glyph outlines painted by |box| and its descendants, in page
    /// coordinates. Used as the clip for background-clip: text.
    /// </summary>
    private SKPath? BuildTextClipPath(LayoutBox box)
    {
        SKPath? path = null;
        CollectTextClipPath(box, ref path);
        return path;
    }

    private void CollectTextClipPath(LayoutBox box, ref SKPath? path)
    {
        if (box.Lines != null)
        {
            foreach (var line in box.Lines)
            {
                float currentX = line.X;
                foreach (var run in line.Runs)
                {
                    if (run.IsText && run.Node is TextNode textNode)
                    {
                        var runStyle = textNode.ParentElement?.ComputedStyle;
                        if (runStyle != null && runStyle.Visibility != VisibilityType.Hidden)
                        {
                            string text = Acrux.Core.Layout.Inline.InlineItemsBuilder.ApplyTextTransform(run.Text, runStyle.TextTransform);
                            float size = run.FontSize ?? runStyle.FontSize;
                            float baselineY = (run.Baseline > 0 ? run.Baseline : line.Baseline) + TotalOffsetY;
                            AppendRunGlyphPath(ref path, text, currentX + line.TextAlignOffsetX, baselineY,
                                size, run.FontFamily ?? runStyle.FontFamily, run.FontWeight);
                        }
                    }
                    currentX += run.Width;
                }
            }
        }
        else if (box.LineRuns != null)
        {
            float x = box.ContentBox.Left;
            float fallbackBaseline = box.ContentBox.Top + TotalOffsetY +
                Core.Fonts.LineBoxMetrics.GetBaselineForLineHeight(box.Dimensions?.Style, box.LineHeight);
            foreach (var run in box.LineRuns)
            {
                if (run.IsText && run.Node is TextNode textNode)
                {
                    var runStyle = textNode.ParentElement?.ComputedStyle;
                    if (runStyle != null && runStyle.Visibility != VisibilityType.Hidden)
                    {
                        string text = Acrux.Core.Layout.Inline.InlineItemsBuilder.ApplyTextTransform(run.Text, runStyle.TextTransform);
                        float size = run.FontSize ?? runStyle.FontSize;
                        float baselineY = run.Baseline > 0 ? run.Baseline + TotalOffsetY : fallbackBaseline;
                        AppendRunGlyphPath(ref path, text, x, baselineY, size,
                            run.FontFamily ?? runStyle.FontFamily, run.FontWeight);
                    }
                }
                x += run.Width;
            }
        }

        foreach (var child in box.Children)
            CollectTextClipPath(child, ref path);
    }

    private static void AppendRunGlyphPath(ref SKPath? target, string text, float x, float baselineY,
        float size, string? family, FontWeight weight)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var typeface = Core.Fonts.FontManager.GetOrCreateTypeface(
            string.IsNullOrEmpty(family) ? Core.Fonts.FontManager.StandardFontFamily : family, weight);
        if (!Core.Fonts.FontManager.HasCharacter(typeface, text[0]))
            typeface = Core.Fonts.FontManager.GetFallbackTypeface(text[0]) ?? typeface;

        var font = new SKFont(typeface, size);
        var glyphPath = font.GetTextPath(text, new SKPoint(x, baselineY));
        if (glyphPath == null || glyphPath.IsEmpty)
        {
            glyphPath?.Dispose();
            return;
        }

        target ??= new SKPath();
        target.AddPath(glyphPath);
        glyphPath.Dispose();
    }

    /// <summary>
    /// Paint the background fill layers (background-color + background-image)
    /// of an element honoring background-clip. Shared fill portion used by the
    /// inline painter (InlineBoxFragmentPainter.PaintFillLayer) so inline boxes
    /// render background images through the same pipeline as block boxes.
    /// </summary>
    internal void PaintBackgroundFill(Element element, ComputedStyle style, SKRect borderRect)
    {
        var paddingRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                     borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);
        if (paddingRect.Width <= 0 || paddingRect.Height <= 0) return;

        // Determine the background clip rect based on background-clip property
        SKRect bgClipRect;
        switch (style.BackgroundClip?.ToLowerInvariant())
        {
            case "border-box":
                bgClipRect = borderRect;
                break;
            case "content-box":
                float padL = style.PaddingLeft is PixelLength pl ? pl.Value : 0;
                float padT = style.PaddingTop is PixelLength pt ? pt.Value : 0;
                float padR = style.PaddingRight is PixelLength pr ? pr.Value : 0;
                float padB = style.PaddingBottom is PixelLength pb ? pb.Value : 0;
                bgClipRect = new SKRect(paddingRect.Left + padL, paddingRect.Top + padT,
                                        paddingRect.Right - padR, paddingRect.Bottom - padB);
                if (bgClipRect.Width <= 0) bgClipRect.Right = bgClipRect.Left;
                if (bgClipRect.Height <= 0) bgClipRect.Bottom = bgClipRect.Top;
                break;
            case "padding-box":
                bgClipRect = paddingRect;
                break;
            default:
                // The initial value of background-clip is border-box, so the fill has to
                // reach the outer rounded rect; clipping it to the padding box leaves
                // white slivers between the fill and a rounded border.
                bgClipRect = borderRect;
                break;
        }

        // Push clip to background clip rect
        bool needsClip = style.BackgroundClip != null && style.BackgroundClip != "" &&
                         style.BackgroundClip.ToLowerInvariant() != "padding-box";
        if (needsClip)
        {
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipRect = bgClipRect;
            clipOp.Bounds = borderRect;
            _displayList.Add(clipOp);
        }

        bool hasBackgroundColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasBackgroundImage = style.BackgroundImage is { Count: > 0 };

        if (hasBackgroundColor)
        {
            SKColor bgColor = style.BackgroundColor.Value;

            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = bgClipRect;
            op.FillColor = bgColor;
            op.BorderRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius, Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
            op.CornerRadii = ResolveBackgroundCornerRadii(style, bgClipRect);
            op.Bounds = borderRect;
            _displayList.Add(op);
        }

        if (hasBackgroundImage && style.BackgroundImage is { Count: > 0 })
        {
            if (style.BackgroundImage!.Any(s => s.Contains("gradient", StringComparison.OrdinalIgnoreCase)))
                DrawGradientBackground(style, paddingRect);
            else
                DrawBackgroundImage(element, style, paddingRect);
        }

        // Pop clip if we pushed one
        if (needsClip)
        {
            var popOp = PaintOpPool.GetPopClipOp();
            popOp.Bounds = borderRect;
            _displayList.Add(popOp);
        }
    }

    private void DrawBoxShadow(SKRect rect, ComputedStyle style)
    {
        if (style.BoxShadow == null || style.BoxShadow.Count == 0) return;

        // CSS Backgrounds §box-shadow paints the FIRST-listed shadow on top, so
        // iterate in reverse (last drawn first, i.e. furthest back).
        for (int si = style.BoxShadow.Count - 1; si >= 0; si--)
        {
            var shadow = style.BoxShadow[si];
            if (shadow.Inset)
            {
                float blur = Math.Max(1, shadow.BlurRadius);
                using var innerPath = new SKPath();
                AddCornerRadiiToPath(innerPath, rect, ResolveBackgroundCornerRadii(style, rect));
                var clipOp = PaintOpPool.GetPushClipOp();
                clipOp.ClipPath = new SKPath(innerPath);
                clipOp.AntiAlias = true;
                clipOp.Bounds = rect;
                _displayList.Add(clipOp);
                var shadowOp = PaintOpPool.GetDrawShadowOp();
                shadowOp.Path = new SKPath(innerPath);
                shadowOp.Color = shadow.Color;
                shadowOp.BlurRadius = blur;
                shadowOp.OffsetX = shadow.OffsetX;
                shadowOp.OffsetY = shadow.OffsetY;
                shadowOp.Inset = true;
                shadowOp.ZIndex = 0;
                shadowOp.Bounds = new SKRect(rect.Left - Math.Abs(shadow.OffsetX) - blur, rect.Top - Math.Abs(shadow.OffsetY) - blur,
                                             rect.Right + Math.Abs(shadow.OffsetX) + blur, rect.Bottom + Math.Abs(shadow.OffsetY) + blur);
                _displayList.Add(shadowOp);
                var popClipOp = PaintOpPool.GetPopClipOp();
                popClipOp.Bounds = rect;
                _displayList.Add(popClipOp);
                continue;
            }

            var path = new SKPath();
            AddCornerRadiiToPath(path, rect, ResolveBackgroundCornerRadii(style, rect));
            var shadowOutsetOp = PaintOpPool.GetDrawShadowOp();
            shadowOutsetOp.Path.Dispose();
            shadowOutsetOp.Path = path;
            shadowOutsetOp.Color = shadow.Color;
            shadowOutsetOp.BlurRadius = Math.Max(1, shadow.BlurRadius);
            shadowOutsetOp.OffsetX = shadow.OffsetX;
            shadowOutsetOp.OffsetY = shadow.OffsetY;
            shadowOutsetOp.ZIndex = 0;
            shadowOutsetOp.Bounds = new SKRect(rect.Left + shadow.OffsetX - shadow.BlurRadius, rect.Top + shadow.OffsetY - shadow.BlurRadius,
                                         rect.Right + shadow.OffsetX + shadow.BlurRadius, rect.Bottom + shadow.OffsetY + shadow.BlurRadius);
            _displayList.Add(shadowOutsetOp);
        }
    }

private static SKBlendMode MixBlendModeToSkBlendMode(MixBlendModeType mode) => mode switch
    {
        MixBlendModeType.Multiply => SKBlendMode.Multiply,
        MixBlendModeType.Screen => SKBlendMode.Screen,
        MixBlendModeType.Overlay => SKBlendMode.Overlay,
        MixBlendModeType.Darken => SKBlendMode.Darken,
        MixBlendModeType.Lighten => SKBlendMode.Lighten,
        MixBlendModeType.ColorDodge => SKBlendMode.ColorDodge,
        MixBlendModeType.ColorBurn => SKBlendMode.ColorBurn,
        MixBlendModeType.HardLight => SKBlendMode.HardLight,
        MixBlendModeType.SoftLight => SKBlendMode.SoftLight,
        MixBlendModeType.Difference => SKBlendMode.Difference,
        MixBlendModeType.Exclusion => SKBlendMode.Exclusion,
        MixBlendModeType.Hue => SKBlendMode.Hue,
        MixBlendModeType.Saturation => SKBlendMode.Saturation,
        MixBlendModeType.Color => SKBlendMode.Color,
        MixBlendModeType.Luminosity => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };

    /// <summary>'background-blend-mode' shares the blend list of 'mix-blend-mode'
    /// (CSS Backgrounds 4 §6); it only differs in what forms the backdrop: the
    /// element's own lower background layers, never the page.</summary>
    /// <summary>
    /// Map 'font-style' onto a text run. A plain 'oblique' asks the family for its own
    /// slanted face; 'oblique &lt;angle&gt;' names the slant, which is synthesized by
    /// shearing the upright glyphs - asking for the italic face as well would double
    /// the slant (CSS Fonts 4 §3.2.2). That shear is invented by the engine, so
    /// 'font-synthesis-style: none' switches it off and the run stays upright.
    /// </summary>
    private static void SetFontSlant(DrawTextOp op, ComputedStyle? style)
    {
        float? degrees = style != null && style.FontStyle == FontStyleType.Oblique
            ? style.FontStyleObliqueDegrees
            : null;
        bool slanted = style?.FontStyle is FontStyleType.Italic or FontStyleType.Oblique;
        op.Italic = degrees == null && slanted;
        bool allowSynthesis = style == null
            || (style.FontSynthesis & Acrux.Core.Dom.FontSynthesisType.Style) != 0;
        // An authored angle is always drawn as a shear (no such face exists to ask for);
        // a bare 'italic'/'oblique' asks the family for a slanted face first and only
        // shears if the face that came back is upright — see DrawTextOp.SynthesizeStyle.
        op.SynthesizeStyle = op.Italic && allowSynthesis;
        op.ObliqueSkewX = degrees is { } d && allowSynthesis
            ? -MathF.Tan(d * MathF.PI / 180f)
            : 0f;
    }

    /// <summary>
    /// Fills the decoration fields of a text op. The style passed in is the one
    /// of the box that originated the run's own line (its element, or that
    /// element's ::first-line override); everything above that box reaches the
    /// text by <i>propagation</i>, not inheritance, so the ancestors are walked
    /// and recorded as extra layers, each keeping the originating box's own line
    /// list, style, color and metrics (CSS Text Decoration 4 §5.1).
    /// </summary>
    private static void SetDecorations(DrawTextOp op, ComputedStyle? style, Element? origin)
    {
        op.Underline = style?.TextDecorationLine.HasUnderline() == true || style?.TextDecoration == TextDecorationType.Underline;
        op.LineThrough = style?.TextDecorationLine.HasLineThrough() == true || style?.TextDecoration == TextDecorationType.LineThrough;
        op.Overline = style?.TextDecorationLine.HasOverline() == true || style?.TextDecoration == TextDecorationType.Overline;
        // 'auto' decoration color means the originating box's own text color; an
        // authored 'transparent' is explicit ink and must draw nothing.
        op.UnderlineColor = style == null ? default : style.ResolvedTextDecorationColor;
        op.DecorationStyle = style?.TextDecorationStyle ?? TextDecorationStyleType.Solid;
        op.DecorationThickness = style?.TextDecorationThickness ?? float.NaN;
        op.DecorationThicknessFromFont = style?.TextDecorationThicknessFromFont ?? false;
        op.DecorationUnderlineOffset = style == null || style.TextUnderlineOffsetIsAuto ? float.NaN : style.TextUnderlineOffset;
        op.DecorationUnderlinePosition = style?.TextUnderlinePosition ?? TextUnderlinePositionType.Auto;
        op.DecorationSkipInk = style?.TextDecorationSkipInk ?? true;
        op.AncestorDecorations = CollectPropagatedDecorations(origin);
    }

    /// <summary>
    /// The decorations of <paramref name="origin"/>'s ancestors, ordered
    /// outermost first, or null when the text carries no propagated line.
    /// An out-of-flow box is a propagation barrier: the decorations above it
    /// never reach its content, while its own decorations do.
    /// </summary>
    private static List<AppliedTextDecoration>? CollectPropagatedDecorations(Element? origin)
    {
        List<AppliedTextDecoration>? list = null;
        for (var current = origin; current != null; current = current.ParentElement)
        {
            var style = current.ComputedStyle;
            if (style == null)
                continue;
            if (style.Position is PositionType.Absolute or PositionType.Fixed)
                break;

            var ancestor = current.ParentElement;
            var ancestorStyle = ancestor?.ComputedStyle;
            if (ancestorStyle == null || ancestorStyle.TextDecorationLine == TextDecorationLineType.None)
                continue;

            list ??= new List<AppliedTextDecoration>();
            list.Insert(0, ancestorStyle.AppliedTextDecorations()[0]);
        }
        return list;
    }

    private static SKBlendMode BackgroundBlendModeToSkBlendMode(BackgroundBlendModeType mode) => mode switch
    {
        BackgroundBlendModeType.Multiply => SKBlendMode.Multiply,
        BackgroundBlendModeType.Screen => SKBlendMode.Screen,
        BackgroundBlendModeType.Overlay => SKBlendMode.Overlay,
        BackgroundBlendModeType.Darken => SKBlendMode.Darken,
        BackgroundBlendModeType.Lighten => SKBlendMode.Lighten,
        BackgroundBlendModeType.ColorDodge => SKBlendMode.ColorDodge,
        BackgroundBlendModeType.ColorBurn => SKBlendMode.ColorBurn,
        BackgroundBlendModeType.HardLight => SKBlendMode.HardLight,
        BackgroundBlendModeType.SoftLight => SKBlendMode.SoftLight,
        BackgroundBlendModeType.Difference => SKBlendMode.Difference,
        BackgroundBlendModeType.Exclusion => SKBlendMode.Exclusion,
        BackgroundBlendModeType.Hue => SKBlendMode.Hue,
        BackgroundBlendModeType.Saturation => SKBlendMode.Saturation,
        BackgroundBlendModeType.Color => SKBlendMode.Color,
        BackgroundBlendModeType.Luminosity => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver,
    };

    private static bool IsMixBlendModeRequired(ComputedStyle style) => style.MixBlendMode != MixBlendModeType.Normal;

    private void DrawScrollbar(LayoutBox box, ComputedStyle style)
    {
        bool canResize = style.Resize != ResizeType.None;
        _scrollableAreaPainter.Paint(box, style, TotalOffsetY, canResize);
    }

    private SKPoint ParseTransformOrigin(string? origin, LayoutBox box)
    {
        // The origin is resolved against the border box (CSS Transforms 1 §4: the
        // reference box is the border box unless box-sizing says otherwise).
        float width = box.BorderBox.Width;
        float height = box.BorderBox.Height;
        float left = box.BorderBox.Left;
        float top = box.BorderBox.Top;
        if (string.IsNullOrEmpty(origin))
            return new SKPoint(left + width / 2f, top + height / 2f);

        var parts = origin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // The grammar is [<x>&<y>?] <length>?, where the trailing bare <length> is the
        // z-offset that a flat engine ignores. Keywords may appear in either order, so
        // each token is filed into the axis it names rather than by position.
        if (parts.Length >= 3 && IsLengthToken(parts[^1]))
            parts = parts[..^1];

        string? xToken = null, yToken = null;
        foreach (var part in parts)
        {
            string token = part.Trim().ToLowerInvariant();
            switch (token)
            {
                case "left":
                case "right":
                    xToken ??= token;
                    continue;
                case "top":
                case "bottom":
                    yToken ??= token;
                    continue;
            }
            if (token == "center")
            {
                if (xToken == null) xToken = token;
                else yToken ??= token;
            }
            else if (xToken == null)
            {
                xToken = token;
            }
            else
            {
                yToken ??= token;
            }
        }

        return new SKPoint(
            xToken == null ? left + width / 2f : ParseOriginValue(xToken, width, left),
            yToken == null ? top + height / 2f : ParseOriginValue(yToken, height, top));
    }

    /// <summary>True for a bare or unit-bearing <length>, as opposed to a keyword or percentage.</summary>
    private static bool IsLengthToken(string token)
    {
        string text = token.Trim();
        if (text.Length == 0 || text.EndsWith('%')) return false;
        int unit = 0;
        while (unit < text.Length && (char.IsDigit(text[unit]) || text[unit] is '.' or '-' or '+'))
            unit++;
        if (unit == 0) return false;
        if (unit == text.Length) return true;   // unitless number
        return Length.TryParse(text, out _);
    }

    /// <summary>
    /// Resolve one axis of 'transform-origin' to a page coordinate. Keywords name
    /// the edge (or the middle) of the border box; a length is measured from the
    /// box's own start edge and a percentage from its size (CSS Transforms 1 §4).
    /// </summary>
    private float ParseOriginValue(string value, float size, float offset)
    {
        string text = value.Trim();
        switch (text.ToLowerInvariant())
        {
            case "left":
            case "top":
                return offset;
            case "right":
            case "bottom":
                return offset + size;
            case "center":
                // "center" as the second token means 50% of that axis; as the first
                // token of a two-value list it is the 50% position too.
                return offset + size / 2f;
        }

        if (text.EndsWith("%"))
        {
            if (float.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                return offset + size * pct / 100f;
        }
        var length = Length.Parse(text);
        if (length != null)
            return offset + length.ToPixels(size, size, size, size);
        return offset + size / 2f;
    }

    internal void PushObjectEffects(ComputedStyle style, LayoutBox layoutBox, SKRect offsetBorderBox, ScopedPaintState objectPaintState)
    {
        bool hasClipPath = ClipPathClipper.HasClipPath(style.ClipPath);
        bool hasOpacityLayer = style.Opacity < 1.0f && style.Opacity >= 0f;
        bool hasBlendMode = style.MixBlendMode != MixBlendModeType.Normal;
        bool hasMask = _maskPainter.HasMask(style);
        SKImage? maskImage = null;
        if (hasMask)
            // The laid-out box is passed so the mask can be positioned and clipped against
        // the origin/clip boxes, not just the border box (CSS Masking 1 §7.1).
        maskImage = _maskPainter.TryBuildMaskImage(style, offsetBorderBox, layoutBox);

        bool hasFilter = !string.IsNullOrEmpty(style.Filter) && style.Filter != "none";
        SKImageFilter? elementFilter = null;
        float filterInflation = 0f;
        if (hasFilter)
            elementFilter = FilterRenderer.ParseAndChain(style.Filter, out filterInflation);

        if (hasFilter || hasOpacityLayer || hasBlendMode || hasClipPath || hasMask)
        {
            SKPath? clipPath = null;
            if (hasClipPath)
            {
                clipPath = ClipPathClipper.Parse(style.ClipPath, layoutBox);
                // Every other geometry in this method is expressed in the offset space
                // the caller passes in (scroll/tile offset already added), and the ops
                // the clip has to intersect are built in that space too — a path left
                // in raw layout coordinates would drift as soon as contentOffsetY != 0.
                if (clipPath != null)
                    clipPath.Offset(offsetBorderBox.Left - layoutBox.BorderBox.Left,
                                    offsetBorderBox.Top - layoutBox.BorderBox.Top);
            }
            objectPaintState.PushLayer(hasOpacityLayer ? style.Opacity : 1.0f,
                elementFilter, clipPath, offsetBorderBox, maskImage,
                hasBlendMode ? MixBlendModeToSkBlendMode(style.MixBlendMode) : SKBlendMode.SrcOver,
                filterInflation);
        }

        // Apply CSS transform BEFORE background so the entire element (including background) is transformed
        if (style.HasAnyTransform)
        {
            var transformOrigin = ParseTransformOrigin(style.TransformOrigin, layoutBox);
            var operations = TransformParser.Parse(style.EffectiveTransform(
                layoutBox.BorderBox.Width, layoutBox.BorderBox.Height));
            if (operations.Count > 0)
            {
                var transformMatrix = TransformParser.ToMatrix(operations, transformOrigin.X, transformOrigin.Y);
                objectPaintState.PushTransform(transformMatrix, offsetBorderBox);
            }
        }
    }

    private void DrawBackgroundImage(Element element, ComputedStyle style, SKRect rect)
    {
        var images = style.BackgroundImage;
        if (images == null || images.Count == 0) return;
        if (string.IsNullOrEmpty(images[0])) return;
        var url = UrlResolver.Resolve(images[0], _baseUrl);
        if (url == null) return;
        var task = _imageCache.GetImageAsync(url);
        task.Wait();
        var image = task.Result;
        if (image == null) return;

        var fillLayer = BackgroundImageGeometry.FromStyle(style);
        if (fillLayer == null) return;

        float padL = style.PaddingLeft is PixelLength pl ? pl.Value : 0;
        float padT = style.PaddingTop is PixelLength pt ? pt.Value : 0;
        float padR = style.PaddingRight is PixelLength pr ? pr.Value : 0;
        float padB = style.PaddingBottom is PixelLength pb ? pb.Value : 0;

        var paintContext = new BoxBackgroundPaintContext(
            new SKSize(rect.Width, rect.Height),
            style.BorderTopWidth, style.BorderRightWidth, style.BorderBottomWidth, style.BorderLeftWidth,
            padT, padR, padB, padL);

        var geometry = new BackgroundImageGeometry();
        geometry.Calculate(fillLayer, paintContext, rect, style, new SKSize(image.Width, image.Height));
        DrawBackgroundTiles(image, geometry, BackgroundBlendModeToSkBlendMode(style.BackgroundBlendMode));
    }

    /// <summary>
    /// Tile an image over the snapped destination rect using the precomputed
    /// tile size, phase and repeat spacing from <see cref="BackgroundImageGeometry"/>.
    /// </summary>
    private void DrawBackgroundTiles(SKImage image, BackgroundImageGeometry geometry,
        SKBlendMode blend = SKBlendMode.SrcOver)
    {
        float imgW = image.Width, imgH = image.Height;

        ForEachBackgroundTile(geometry, (tileRect, clipRect) =>
        {
            float srcLeft = (clipRect.Left - tileRect.Left) / tileRect.Width * imgW;
            float srcTop = (clipRect.Top - tileRect.Top) / tileRect.Height * imgH;
            float srcRight = srcLeft + (clipRect.Width / tileRect.Width * imgW);
            float srcBottom = srcTop + (clipRect.Height / tileRect.Height * imgH);

            var op = PaintOpPool.GetDrawImageOp();
            op.Image = image;
            op.SourceRect = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
            op.DestRect = clipRect;
            op.BlendMode = blend;
            // Background tiles are already sized/positioned by the
            // geometry; stretch the (possibly clipped) source into dest.
            op.Fit = ImageFit.Fill;
            op.Bounds = geometry.SnappedDestRect;
            _displayList.Add(op);
        });
    }

    /// <summary>
    /// Walk the tile grid of a computed background geometry and hand each visible
    /// tile to <paramref name="paintTile"/> as its full tile rect plus the part of
    /// it that falls inside the destination rect. Shared by image and gradient
    /// backgrounds so that 'repeat', 'space', 'round' and 'no-repeat' behave the
    /// same for both.
    /// </summary>
    private static void ForEachBackgroundTile(BackgroundImageGeometry geometry,
        Action<SKRect, SKRect> paintTile)
    {
        var destRect = geometry.SnappedDestRect;
        if (destRect.Width <= 0 || destRect.Height <= 0) return;
        var tileSize = geometry.TileSize;
        if (tileSize.Width <= 0 || tileSize.Height <= 0) return;

        var phase = geometry.ComputePhase();
        float stepX = tileSize.Width + geometry.SpaceSize.Width;
        float stepY = tileSize.Height + geometry.SpaceSize.Height;
        if (stepX <= 0 || stepY <= 0) return;

        for (float y = destRect.Top + phase.Y; y < destRect.Bottom; y += stepY)
        {
            for (float x = destRect.Left + phase.X; x < destRect.Right; x += stepX)
            {
                var tileRect = new SKRect(x, y, x + tileSize.Width, y + tileSize.Height);
                var clipRect = tileRect;
                clipRect.Left = Math.Max(clipRect.Left, destRect.Left);
                clipRect.Top = Math.Max(clipRect.Top, destRect.Top);
                clipRect.Right = Math.Min(clipRect.Right, destRect.Right);
                clipRect.Bottom = Math.Min(clipRect.Bottom, destRect.Bottom);
                if (clipRect.Width > 0 && clipRect.Height > 0)
                    paintTile(tileRect, clipRect);
            }
        }
    }

    /// <summary>
    /// Paint a background-size'd gradient as a grid of tiles. A gradient has no
    /// intrinsic size, so each tile gets its own shader spanning that tile; this is
    /// what makes background-repeat: space / round / no-repeat visible at all.
    /// </summary>
    private void DrawSizedGradientTiles(ComputedStyle style, SKRect rect, SKPoint[]? cornerRadii)
    {
        var fillLayer = BackgroundImageGeometry.FromStyle(style);
        if (fillLayer == null) return;

        float padL = style.PaddingLeft is PixelLength pl ? pl.Value : 0;
        float padT = style.PaddingTop is PixelLength pt ? pt.Value : 0;
        float padR = style.PaddingRight is PixelLength pr ? pr.Value : 0;
        float padB = style.PaddingBottom is PixelLength pb ? pb.Value : 0;

        var paintContext = new BoxBackgroundPaintContext(
            new SKSize(rect.Width, rect.Height),
            style.BorderTopWidth, style.BorderRightWidth, style.BorderBottomWidth, style.BorderLeftWidth,
            padT, padR, padB, padL);

        var blend = BackgroundBlendModeToSkBlendMode(style.BackgroundBlendMode);

        for (var layer = fillLayer; layer != null; layer = layer.Next)
        {
            string? imageCss = layer.Image;
            if (string.IsNullOrEmpty(imageCss)) continue;

            var geometry = new BackgroundImageGeometry();
            geometry.Calculate(layer, paintContext, rect, style, intrinsicSize: SKSize.Empty);

            ForEachBackgroundTile(geometry, (tileRect, clipRect) =>
            {
                var shader = GradientRenderer.CreateGradient(imageCss, tileRect);
                if (shader == null) return;

                SKPath path = new();
                if (cornerRadii != null)
                {
                    // The corner rounding belongs to the element, not to a tile,
                    // so clip each tile against the rounded outline.
                    using var rounded = new SKPath();
                    AddCornerRadiiToPath(rounded, rect, cornerRadii);
                    using var tile = new SKPath();
                    tile.AddRect(clipRect);
                    var clipped = new SKPath();
                    if (!rounded.Op(tile, SKPathOp.Intersect, clipped))
                    {
                        clipped.Dispose();
                        clipped = new SKPath();
                        clipped.AddRect(clipRect);
                    }
                    path.Dispose();
                    path = clipped;
                }
                else
                {
                    path.AddRect(clipRect);
                }

                var op = PaintOpPool.GetDrawPathOp();
                op.Path.Dispose();
                op.Path = path;
                op.FillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Shader = shader,
                    IsAntialias = true,
                    BlendMode = blend,
                };
                op.Bounds = clipRect;
                _displayList.Add(op);
            });
        }
    }

    private void DrawGradientBackground(ComputedStyle style, SKRect rect)
    {
        var images = style.BackgroundImage;
        if (images == null || images.Count == 0) return;

        // Percentages and the two halves of an elliptical corner are decoded against the
        // clip box, so the gradient outline matches the element's rounded corner shape.
        var cornerRadii = ResolveBackgroundCornerRadii(style, rect);

        // Resolve background-size into a concrete image size, then background-position
        // into an offset within the element box. The gradient is painted into that
        // sized/positioned rectangle, clipped to the element bounds.
        float fs = style.FontSize > 0 ? style.FontSize : 16;
        var (gw, gh) = ResolveBackgroundImageSize(style, rect.Width, rect.Height, fs);
        bool sized = style.BackgroundSize != BackgroundSizeType.Auto || gw < rect.Width || gh < rect.Height;

        // A sized gradient repeats (or is spaced/rounded/positioned), so it needs the
        // same tile geometry an image background gets.
        if (sized)
        {
            DrawSizedGradientTiles(style, rect, cornerRadii);
            return;
        }

        // CSS paints background layers from last (bottom) to first (top).
        for (int i = images.Count - 1; i >= 0; i--)
        {
            var shader = GradientRenderer.CreateGradient(images[i], rect);
            if (shader == null) continue;

            var path = new SKPath();
            AddCornerRadiiToPath(path, rect, cornerRadii);

            var op = PaintOpPool.GetDrawPathOp();
            op.Path.Dispose();
            op.Path = path;
            op.FillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Shader = shader,
                IsAntialias = true,
                BlendMode = BackgroundBlendModeToSkBlendMode(style.BackgroundBlendMode),
            };
            op.Bounds = rect;
            _displayList.Add(op);
        }
    }

    // Resolve background-size into concrete pixel dimensions for the image.
    private static (float w, float h) ResolveBackgroundImageSize(ComputedStyle style, float boxW, float boxH, float fs)
    {
        float w = boxW, h = boxH;
        bool hasW = style.BackgroundSizeWidth is not null and not AutoLength;
        bool hasH = style.BackgroundSizeHeight is not null and not AutoLength;

        switch (style.BackgroundSize)
        {
            case BackgroundSizeType.Length:
                if (hasW) w = style.BackgroundSizeWidth!.ToPixels(fs, fs, boxW, boxH);
                if (hasH) h = style.BackgroundSizeHeight!.ToPixels(fs, fs, boxW, boxH);
                if (!hasW && hasH) w = h;           // auto width scales with height
                else if (hasW && !hasH) h = w;      // auto height scales with width
                break;
            case BackgroundSizeType.Cover:
            {
                float s = Math.Max(boxW / w, boxH / h);
                w *= s; h *= s;
                break;
            }
            case BackgroundSizeType.Contain:
            {
                float s = Math.Min(boxW / w, boxH / h);
                w *= s; h *= s;
                break;
            }
        }
        return (Math.Max(1, w), Math.Max(1, h));
    }

    /// <summary>
    /// Resolve the four corner radii for a background fill. Percentages are stored
    /// negated by the style parser (50% -&gt; -50) and resolve against the border-box
    /// dimensions; the shared helper also applies the over-constrained corner scaling
    /// of CSS Backgrounds 3 5.3.
    /// </summary>
    internal static SKPoint[]? ResolveBackgroundCornerRadii(ComputedStyle style, SKRect borderRect)
    {
        var (tl, tr, br, bl) = RoundedBorderGeometry.ResolveRadii(style, borderRect);
        if (tl.X <= 0 && tr.X <= 0 && br.X <= 0 && bl.X <= 0) return null;
        return new[] { tl, tr, br, bl };
    }

    /// <summary>Add <paramref name="rect"/> to <paramref name="path"/>, rounded by the resolved corner radii.</summary>
    private static void AddCornerRadiiToPath(SKPath path, SKRect rect, SKPoint[]? radii)
    {
        if (radii == null)
        {
            path.AddRect(rect);
            return;
        }
        var rrect = new SKRoundRect();
        rrect.SetRectRadii(rect, radii);
        path.AddRoundRect(rrect);
    }

    internal void DrawElementBorder(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect)
    {
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
            return;
        if (style.BorderTopWidth <= 0 && style.BorderRightWidth <= 0 && style.BorderBottomWidth <= 0 && style.BorderLeftWidth <= 0)
            return;

        if (element.TagName.Equals("FIELDSET", StringComparison.OrdinalIgnoreCase))
        {
            FieldsetPainter.PaintFieldsetBorder(_displayList, element, style, borderRect, TotalOffsetY);
            return;
        }

        if (NinePieceImagePainter.HasBorderImage(style) &&
            NinePieceImagePainter.Paint(_displayList, _imageCache, style, borderRect, _baseUrl))
            return;

        // border-collapse: collapse — the cell's borders are resolved against the
        // facing borders of its neighbours and centered on the shared grid line.
        if (TablePainter.IsTableCell(element) && TablePainter.InCollapsedTable(element))
        {
            TablePainter.PaintCollapsedCellBorders(_displayList, element, style, borderRect, TotalOffsetY);
            return;
        }

        var borderPainter = new BoxBorderPainter(_displayList, borderRect, style);
        borderPainter.Paint();
    }

    private void DrawFocusRing(Element element, LayoutBox box, DisplayList? target = null)
    {
        if (_focusedElement != element) return;
        var borderBox = box.BorderBox;
        var ringRect = new SKRect(borderBox.Left - 1, borderBox.Top - 1 + TotalOffsetY,
            borderBox.Right + 1, borderBox.Bottom + 1 + TotalOffsetY);
        var ringOp = PaintOpPool.GetDrawPathOp();
        ringOp.Path = CreateRoundedRectPath(ringRect, 4);
        ringOp.StrokePaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        ringOp.Bounds = ringRect;
        (target ?? _displayList).Add(ringOp);
    }

    private void DrawElementOutline(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect)
    {
        if (style.OutlineWidth <= 0 || style.OutlineStyle == BorderStyle.None) return;

        float offset = style.OutlineOffset;
        _outlinePainter.PaintOutline(borderRect, style, offset);
    }

    private void DrawInlineChildrenDecorations(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is Element childElement && childElement.LayoutBox != null &&
                childElement.ComputedStyle?.Display == DisplayType.Inline)
            {
                var childBox = childElement.LayoutBox;
                var childStyle = childElement.ComputedStyle;
                var childBorderRect = new SKRect(
                    childBox.BorderBox.Left,
                    childBox.BorderBox.Top + TotalOffsetY,
                    childBox.BorderBox.Right,
                    childBox.BorderBox.Bottom + TotalOffsetY);

                bool childVisHidden = childStyle.Visibility == VisibilityType.Hidden;
                if (!childVisHidden && InlineBoxFragmentPainter.HasBoxDecorationBackground(childStyle))
                {
                    // Port of InlineBoxFragmentPainter::PaintBackgroundBorderShadow:
                    // paints normal shadow → background fill layers → inset shadow → border.
                    _inlinePainter.PaintBackgroundBorderShadow(childElement, childStyle, childBorderRect);
                }
                if (!childVisHidden)
                {
                    DrawElementOutline(childElement, childBox, childStyle, childBorderRect);
                }
            }
        }
    }

    private void DrawRoundedBorder(SKRect rect, float tl, float tr, float br, float bl, ComputedStyle style)
    {
        bool hasFill = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasStroke = style.BorderTopWidth > 0;
        if (!hasFill && !hasStroke) return;

        using var borderPath = new SKPath();
        float x = rect.Left, y = rect.Top, w = rect.Width, h = rect.Height;
        borderPath.MoveTo(x + tl, y);
        borderPath.LineTo(x + w - tr, y);
        borderPath.QuadTo(x + w, y, x + w, y + tr);
        borderPath.LineTo(x + w, y + h - br);
        borderPath.QuadTo(x + w, y + h, x + w - br, y + h);
        borderPath.LineTo(x + bl, y + h);
        borderPath.QuadTo(x, y + h, x, y + h - bl);
        borderPath.LineTo(x, y + tl);
        borderPath.QuadTo(x, y, x + tl, y);
        borderPath.Close();

        if (hasFill)
        {
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = new SKPath(borderPath);
            bgOp.FillPaint = new SKPaint { Color = style.BackgroundColor.Value, Style = SKPaintStyle.Fill };
            bgOp.Bounds = rect;
            _displayList.Add(bgOp);
        }

        if (hasStroke)
        {
            var strokeOp = PaintOpPool.GetDrawPathOp();
            strokeOp.Path = new SKPath(borderPath);
            strokeOp.StrokePaint = new SKPaint
            {
                Color = style.BorderTopColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = style.BorderTopWidth,
                IsAntialias = true
            };
            strokeOp.Bounds = rect;
            _displayList.Add(strokeOp);
        }
    }

    /// <summary>
    /// A5: draws the column-rule separator lines between the multicol columns.
    /// Rule i sits centered in the gap between column i-1 and column i:
    /// x = contentLeft + i * (colWidth + gap) - gap / 2.
    /// </summary>
    private void DrawColumnRules(LayoutBox box, ComputedStyle style)
    {
        float gap = Math.Max(0, box.ColumnGapSize);
        float progression = box.ColumnWidth + gap;
        if (progression <= 0) return;

        float ruleColor = 0;
        var color = style.ColumnRuleColor ?? style.Color;
        float left = box.ContentBox.Left;
        float top = box.ContentBox.Top + TotalOffsetY;
        float height = box.ContentBox.Height;

        for (int i = 1; i < box.ColumnCount; i++)
        {
            float cx = left + i * progression - gap / 2f;
            var op = PaintOpPool.GetDrawLineOp();
            op.X1 = cx;
            op.Y1 = top;
            op.X2 = cx;
            op.Y2 = top + height;
            op.Color = color;
            op.StrokeWidth = style.ColumnRuleWidth;
            op.Bounds = new SKRect(cx - style.ColumnRuleWidth / 2f - 1, top,
                                   cx + style.ColumnRuleWidth / 2f + 1, top + height);
            _displayList.Add(op);
        }
    }

    private void DrawElementContent(Element element, LayoutBox box, ComputedStyle style)
    {
        // CSS Content Distribution 1 §4: 'content-visibility: hidden' paints the box's own
        // background and border (the caller does that) but none of its contents — no text,
        // no replaced content, no pseudo-element content.
        if (box.ContentsNotRendered) return;

        // visibility:hidden hides the element's OWN content but not descendants
        // that re-declare visibility:visible. Text runs are filtered per run in
        // DrawInlineRuns (a run paints only when its owning node's nearest
        // styleable ancestor chain up to this box is visible); everything below
        // is self-only content and is skipped for hidden elements.
        bool selfHidden = style.Visibility == VisibilityType.Hidden;

        // List markers paint with the item's content so both the element-walk and
        // the layer-tree traversal emit them exactly once.
        if (style.Display == DisplayType.ListItem && !selfHidden)
            DrawListMarker(element, box, style);

        // A5: multicol column rules — vertical separators between columns.
        if (box.IsMultiColumn && box.ColumnCount > 1 &&
            style.ColumnRuleStyle != BorderStyle.None && style.ColumnRuleWidth > 0)
        {
            DrawColumnRules(box, style);
        }

        if (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
        {
            var inputType = element.InputType?.ToLowerInvariant();
            if (inputType == "checkbox" || inputType == "radio")
            {
                DrawCheckRadioElement(element, box, style, inputType);
                return;
            }
            if (inputType == "range")
            {
                DrawRangeElement(element, box, style);
                return;
            }
            if (inputType == "color")
            {
                DrawColorInputElement(element, box, style);
                return;
            }
            if (inputType == "file")
            {
                DrawFileInputElement(element, box, style);
                return;
            }
            if (inputType is "date" or "datetime-local" or "month" or "time" or "week")
            {
                DrawDateInputElement(element, box, style, inputType);
                return;
            }
            DrawInputElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase))
        {
            DrawTextAreaElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            DrawSelectElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            DrawButtonElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
        {
            _imagePainter.PaintImage(element, style, box);
            return;
        }
        // Replaced-element content painting (video/canvas/object/embed).
        // Background & border phases already ran; this paints the CONTENT layer.
        if (_replacedPainter.TryPaint(element, style, box))
            return;
        if (element.TagName.Equals("PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            DrawProgressElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("METER", StringComparison.OrdinalIgnoreCase))
        {
            DrawMeterElement(element, box, style);
            return;
        }
        if (style.Display == DisplayType.Inline)
        {
            // Inline containers that were laid out via LayoutInlineChildren (e.g. a
            // <label> holding a form control plus text) carry their own line runs;
            // draw them here. Plain text-only inline elements have no lines and are
            // drawn by the parent's run list, so this is a no-op for them.
            if (box.LineRuns != null && box.LineRuns.Count > 0)
            {
                DrawInlineRuns(box);
                return;
            }
            if (box.Lines != null && box.Lines.Count > 0)
            {
                DrawInlineRuns(box);
                return;
            }
            return;
        }
        if (box.LineRuns != null && box.LineRuns.Count > 0)
        {
            DrawInlineRuns(box);
            return;
        }
        if (box.Lines != null && box.Lines.Count > 0)
        {
            DrawInlineRuns(box);
            return;
        }
        // Anonymous child boxes (multicol column fragmentainers) hold this
        // element's flow content, distributed across columns. Paint them instead
        // of the raw text fallback below, which would redraw all the text as a
        // single full-width run.
        if (HasAnonymousContentChildren(box))
        {
            PaintAnonymousChildBoxes(box);
            return;
        }
        foreach (var child in element.Children)
        {
            if (child is TextNode textNode)
                DrawTextNode(textNode, box, style);
        }
    }

    private static bool HasAnonymousContentChildren(LayoutBox box)
    {
        foreach (var child in box.Children)
        {
            if (child.Lines == null && child.LineRuns == null)
                continue;
            var el = child.Dimensions?.Element;
            if (el == null || el.ParentNode == null)
                return true;
        }
        return false;
    }

    private void CollectTextNodes(Node node, StringBuilder sb)
    {
        if (node is TextNode textNode)
        {
            var text = textNode.TextContent ?? "";
            if (!string.IsNullOrEmpty(text))
                sb.Append(text);
        }
        foreach (var child in node.Children)
            CollectTextNodes(child, sb);
    }

    private string GetButtonText(Element button)
    {
        var textBuilder = new StringBuilder();
        CollectTextNodes(button, textBuilder);
        string result = textBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(result))
            return result;
        var valueAttr = button.GetAttribute("value");
        return !string.IsNullOrEmpty(valueAttr) ? valueAttr : "Button";
    }

    private static SKColor DarkenColor(SKColor c, float factor)
    {
        return new SKColor((byte)Math.Min(255, c.Red * factor),
            (byte)Math.Min(255, c.Green * factor),
            (byte)Math.Min(255, c.Blue * factor),
            c.Alpha);
    }

    private void DrawButtonElement(Element element, LayoutBox box, ComputedStyle style)
    {
        string buttonText = GetButtonText(element);
        if (string.IsNullOrEmpty(buttonText)) buttonText = "Button";

        var borderBox = box.BorderBox;
        float btnFontSize = style.FontSize > 0 ? style.FontSize : 13.3333f;
        float btnLineHeightValue = style.LineHeight > 0 ? style.LineHeight : 1.2f;

        float borderTopWidth = style.BorderTopWidth;
        float borderBottomWidth = style.BorderBottomWidth;
        float borderLeftWidth = style.BorderLeftWidth;
        float borderRightWidth = style.BorderRightWidth;

        float padTop = GetPixelLengthFromStyle(style.PaddingTop, 6);
        float padBottom = GetPixelLengthFromStyle(style.PaddingBottom, 6);
        float padLeft = GetPixelLengthFromStyle(style.PaddingLeft, 12);
        float padRight = GetPixelLengthFromStyle(style.PaddingRight, 12);

        var bgRect = new SKRect(
            borderBox.Left + TotalOffsetX,
            borderBox.Top + TotalOffsetY,
            borderBox.Right + TotalOffsetX,
            borderBox.Bottom + TotalOffsetY
        );

        bool isDisabled = element.HasAttribute("disabled");
        bool isFocused = _focusedElement == element;
        bool isPressed = _pressedButton == element;
        SKColor btnBgColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0 ? style.BackgroundColor.Value : SKColor.Parse("#E1E1E1");
        SKColor btnBorderColor = style.BorderTopColor.Alpha > 0 ? style.BorderTopColor : new SKColor(0x80, 0x80, 0x80);
        SKColor textColor = style.Color.Alpha > 0 ? style.Color : SKColors.Black;
        if (isDisabled)
        {
            btnBgColor = new SKColor(240, 240, 240);
            textColor = new SKColor(160, 160, 160);
            btnBorderColor = new SKColor(200, 200, 200);
        }
        else if (isPressed)
        {
            // Pressed: darker background (as if the face is pushed in)
            if (style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0)
                btnBgColor = DarkenColor(style.BackgroundColor.Value, 0.85f);
            else
                btnBgColor = new SKColor(0xC9, 0xC9, 0xC9);
            btnBorderColor = new SKColor(0x66, 0x66, 0x66);
        }
        else if (isFocused)
        {
            btnBorderColor = new SKColor(0x1A, 0x73, 0xE8);
            borderTopWidth = Math.Max(2, style.BorderTopWidth);
            borderBottomWidth = Math.Max(2, style.BorderBottomWidth);
            borderLeftWidth = Math.Max(2, style.BorderLeftWidth);
            borderRightWidth = Math.Max(2, style.BorderRightWidth);
        }

        float borderRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
        if (borderRadius <= 0) borderRadius = 4;

        if (borderRadius > 0)
        {
            var path = CreateRoundedRectPath(bgRect, borderRadius);
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = path;
            bgOp.FillPaint = new SKPaint { Color = btnBgColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            bgOp.Bounds = bgRect;
            _displayList.Add(bgOp);

            if (borderTopWidth > 0)
            {
                var borderOp = PaintOpPool.GetDrawPathOp();
                borderOp.Path = path;
                borderOp.StrokePaint = new SKPaint { Color = btnBorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = borderTopWidth, IsAntialias = true };
                borderOp.Bounds = bgRect;
                _displayList.Add(borderOp);
            }
        }
        else
        {
            var bgOp = PaintOpPool.GetDrawRectOp();
            bgOp.Rect = bgRect;
            bgOp.FillColor = btnBgColor;
            bgOp.Bounds = bgRect;
            _displayList.Add(bgOp);

            if (borderTopWidth > 0)
            {
                var borderOp = PaintOpPool.GetDrawRectOp();
                borderOp.Rect = bgRect;
                borderOp.BorderTopWidth = borderTopWidth;
                borderOp.BorderBottomWidth = borderBottomWidth;
                borderOp.BorderLeftWidth = borderLeftWidth;
                borderOp.BorderRightWidth = borderRightWidth;
                borderOp.BorderTopColor = btnBorderColor;
                borderOp.BorderBottomColor = btnBorderColor;
                borderOp.BorderLeftColor = btnBorderColor;
                borderOp.BorderRightColor = btnBorderColor;
                borderOp.Bounds = bgRect;
                _displayList.Add(borderOp);
            }
        }

        // 精确测量文本宽度
        float textWidth = Core.Layout.TextMeasurer.Instance?.MeasureText(buttonText, style.FontFamily ?? "Arial", btnFontSize) ?? 0;
        float maxContentWidth = bgRect.Width - padLeft - padRight - borderLeftWidth - borderRightWidth;
        if (textWidth > maxContentWidth) textWidth = maxContentWidth;

        float contentLeft = bgRect.Left + borderLeftWidth + padLeft;
        float contentTop = bgRect.Top + borderTopWidth + padTop;
        float contentRight = bgRect.Right - borderRightWidth - padRight;
        float contentBottom = bgRect.Bottom - borderBottomWidth - padBottom;
        float contentWidth = contentRight - contentLeft;
        float contentHeight = contentBottom - contentTop;

        float textX = contentLeft + Math.Max(0, (contentWidth - textWidth) / 2);
        float textY = contentTop + Math.Max(0, (contentHeight - btnFontSize) / 2) + Core.Fonts.LineBoxMetrics.GetTextAscent(btnFontSize, style.FontFamily, style.FontWeight);
        textX = Math.Max(contentLeft, Math.Min(textX, contentRight - textWidth));
        textY = Math.Max(contentTop, Math.Min(textY, contentBottom));
        if (isPressed)
        {
            // Pressed: shift text down 1px for a tactile feel
            textY += 1;
        }

        var textOp = PaintOpPool.GetDrawTextOp();
        textOp.Text = buttonText;
        textOp.X = textX;
        textOp.Y = textY;
        textOp.Color = textColor;
        textOp.FontSize = btnFontSize;
        textOp.FontFamily = style.FontFamily ?? "Segoe UI, Arial, sans-serif";
        textOp.FontWeight = style.FontWeight;
        SetFontSlant(textOp, style);
        textOp.TextAlign = TextAlignType.Left;
        textOp.Bounds = new SKRect(textX, textY, textX + textWidth, textY + btnFontSize);
        _displayList.Add(textOp);
    }

    private void DrawTextAreaElement(Element element, LayoutBox box, ComputedStyle style)
    {
        string? value = element.Value;
        string? placeholder = element.GetAttribute("placeholder");
        bool isFocused = _focusedElement == element;
        bool isDisabled = element.HasAttribute("disabled");
        bool isReadOnly = element.HasAttribute("readonly");

        string text;
        bool showPlaceholder = false;
        if (!string.IsNullOrEmpty(value))
            text = value;
        else if (isFocused)
            text = "";
        else if (!string.IsNullOrEmpty(placeholder))
        {
            text = placeholder;
            showPlaceholder = true;
        }
        else
            text = "";

        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        float lineH = fontSize * (style.LineHeight > 0 ? style.LineHeight : 1.2f);
        var contentBox = box.ContentBox;
        float textX = contentBox.Left + 2;
        float textTop = contentBox.Top + 2;
        float usableW = Math.Max(1, contentBox.Width - 4);
        float usableH = Math.Max(1, contentBox.Height - 4);

        bool skipContent = _skipInputTextOverlay && isFocused;
        var targetList = skipContent ? _overlayList : _displayList;

        var clipRect = new SKRect(contentBox.Left, contentBox.Top + TotalOffsetY,
            contentBox.Right, contentBox.Bottom + TotalOffsetY);
        if (clipRect.Width > 0 && clipRect.Height > 0)
        {
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipRect = clipRect;
            targetList.Add(clipOp);
            if (skipContent)
            {
                var overlayClip = PaintOpPool.GetPushClipOp();
                overlayClip.ClipRect = clipRect;
                _overlayList.Add(overlayClip);
            }
        }

        if (skipContent)
        {
            var clearRect = new SKRect(contentBox.Left, contentBox.Top + TotalOffsetY,
                contentBox.Right, contentBox.Bottom + TotalOffsetY);
            var bgColor = style.BackgroundColor ?? new SKColor(255, 255, 255);
            var clearOp = PaintOpPool.GetDrawRectOp();
            clearOp.Rect = clearRect;
            clearOp.FillColor = bgColor;
            clearOp.Bounds = clearRect;
            _overlayList.Add(clearOp);
        }

        // Include any IME composition; the caret is drawn at the (line, column)
        // derived from the flat cursor offset.
        string effectText = isFocused && _inputImeComposing
            ? text[..Math.Min(_inputCursorPos, text.Length)] + _inputImeComposition +
              text[Math.Min(_inputCursorPos, text.Length)..]
            : text;
        int caretFlat = isFocused && _inputImeComposing
            ? Math.Min(_inputCursorPos, text.Length) + Math.Min(_inputImeCursor, _inputImeComposition.Length)
            : isFocused ? _inputCursorPos : 0;

        var visualLines = Core.Layout.TextWrapHelper.WrapToLines(effectText, style.FontFamily ?? "Arial", fontSize, usableW);
        if (showPlaceholder && visualLines.Count == 0)
            visualLines = Core.Layout.TextWrapHelper.WrapToLines(placeholder ?? "", style.FontFamily ?? "Arial", fontSize, usableW);

        float totalH = visualLines.Count * lineH;
        float maxScrollY = Math.Max(0, totalH - usableH);

        int caretLine = 0;
        if (isFocused && visualLines.Count > 0)
            caretLine = Math.Min(Core.Layout.TextWrapHelper.GetLineColumn(visualLines, caretFlat).line, visualLines.Count - 1);
        // Use the persistent vertical scroll kept by BrowserApp (updated on every
        // caret move) so clicking a line in a scrolled textarea places the caret
        // exactly where clicked instead of re-deriving the viewport.
        float scrollY = isFocused ? _textareaScrollY : 0;
        if (maxScrollY > 0)
        {
            if (_textareaUserScroll)
            {
                // User wheel/thumb scrolled: keep the viewport, only clamp.
                scrollY = Math.Clamp(scrollY, 0, maxScrollY);
            }
            else
            {
                const float margin = 4;
                float caretLineY = caretLine * lineH;
                if (caretLineY < scrollY + margin)
                    scrollY = Math.Max(0, caretLineY - margin);
                else if (caretLineY + lineH > scrollY + usableH - margin)
                    scrollY = Math.Min(maxScrollY, caretLineY + lineH - (usableH - margin));
            }
        }
        else
        {
            scrollY = 0;
        }

        SKColor textColor = showPlaceholder ? new SKColor(160, 160, 160) : (style.Color.Alpha > 0 ? style.Color : SKColors.Black);
        if (isDisabled)
            textColor = new SKColor(160, 160, 160);

        int selA = -1, selB = -1;
        if (isFocused && _inputSelStart >= 0 && _inputSelStart != caretFlat)
        {
            selA = Math.Min(_inputSelStart, caretFlat);
            selB = Math.Max(_inputSelStart, caretFlat);
        }

        for (int i = 0; i < visualLines.Count; i++)
        {
            var ln = visualLines[i];
            float y = textTop + i * lineH - scrollY;
            if (y + lineH < textTop || y > contentBox.Bottom) continue;

            int lineStart = ln.Start;
            int lineEnd = ln.Start + ln.Length;
            string lineText = effectText.Substring(lineStart, ln.Length);

            // Selection highlight for the portion of this line inside the selection.
            if (selA >= 0 && selB > lineStart && selA < lineEnd)
            {
                int a = Math.Max(selA, lineStart);
                int b = Math.Min(selB, lineEnd);
                float selX = textX + MeasureTextWidth(effectText[lineStart..a], fontSize, style.FontFamily);
                float selW = MeasureTextWidth(effectText[a..b], fontSize, style.FontFamily);
                var selOp = PaintOpPool.GetDrawRectOp();
                selOp.Rect = new SKRect(selX, y + TotalOffsetY, selX + selW, y + TotalOffsetY + lineH);
                selOp.FillColor = new SKColor(0x1A, 0x73, 0xE8);
                selOp.Bounds = selOp.Rect;
                targetList.Add(selOp);
            }

            DrawTextAreaLine(effectText[lineStart..lineEnd], textX, y, fontSize, style, textColor,
                selA >= 0 ? selA : -1, selB >= 0 ? selB : -1, lineStart, contentBox, targetList);
        }

        // Caret at the (line, column) of the caret offset.
        if (isFocused && _inputShowCursor && !_inputImeComposing && !isReadOnly && !isDisabled && visualLines.Count > 0)
        {
            var (cline, ccol) = Core.Layout.TextWrapHelper.GetLineColumn(visualLines, caretFlat);
            cline = Math.Min(cline, visualLines.Count - 1);
            var cln = visualLines[cline];
            float caretX = textX + MeasureTextWidth(effectText[cln.Start..(cln.Start + Math.Min(ccol, cln.Length))], fontSize, style.FontFamily);
            caretX = Math.Clamp(caretX, textX, textX + usableW);
            float caretY = textTop + cline * lineH - scrollY;
            var caretColor = style.CaretColor ?? new SKColor(0, 0, 0);
            var cursorOp = PaintOpPool.GetDrawLineOp();
            cursorOp.X1 = caretX;
            cursorOp.Y1 = caretY + 1 + TotalOffsetY;
            cursorOp.X2 = caretX;
            cursorOp.Y2 = caretY + lineH - 1 + TotalOffsetY;
            cursorOp.Color = caretColor;
            cursorOp.StrokeWidth = 1.5f;
            cursorOp.Bounds = new SKRect(caretX - 1, cursorOp.Y1, caretX + 1, cursorOp.Y2);
            targetList.Add(cursorOp);
        }

        if (clipRect.Width > 0 && clipRect.Height > 0)
        {
            targetList.Add(PaintOpPool.GetPopClipOp());
            if (skipContent) _overlayList.Add(PaintOpPool.GetPopClipOp());
        }

        // Vertical overlay scrollbar when the content overflows the focused textarea.
        if (isFocused && maxScrollY > 0)
        {
            const float scrollbarWidth = 12f;
            float trackX = contentBox.Right - scrollbarWidth;
            float trackY = contentBox.Top;
            float trackH = contentBox.Height;
            float thumbHeight = Math.Max(20, trackH * Math.Min(1, usableH / Math.Max(1, totalH)));
            var trackOp = PaintOpPool.GetDrawRectOp();
            trackOp.Rect = new SKRect(trackX, trackY + TotalOffsetY, trackX + scrollbarWidth, trackY + trackH + TotalOffsetY);
            trackOp.FillColor = new SKColor(240, 240, 240);
            trackOp.Bounds = trackOp.Rect;
            targetList.Add(trackOp);
            float thumbY = trackY + (trackH - thumbHeight) * (scrollY / maxScrollY);
            var thumbOp = PaintOpPool.GetDrawRectOp();
            thumbOp.Rect = new SKRect(trackX + 2, thumbY + 1 + TotalOffsetY, trackX + scrollbarWidth - 2, thumbY + thumbHeight - 1 + TotalOffsetY);
            thumbOp.FillColor = new SKColor(180, 180, 180);
            thumbOp.Bounds = thumbOp.Rect;
            targetList.Add(thumbOp);
        }

        // Resize grip at the bottom-right corner (drawn outside the content clip).
        if (style.Resize != ResizeType.None && !isDisabled)
        {
            var bBox = box.BorderBox;
            float gx = bBox.Right - 8;
            float gy = bBox.Bottom - 8;
            bool gripHover = Math.Abs(_mouseX - gx) <= 10 && Math.Abs(_mouseY - gy) <= 10;
            bool gripPressed = _pressedControl == "textarea-resize";
            SKColor gColor = gripHover || gripPressed ? new SKColor(0x1A, 0x73, 0xE8) : new SKColor(120, 120, 120);
            float yOff = TotalOffsetY;
            var gripPath = new SKPath();
            gripPath.MoveTo(gx - 8, gy + 3 + yOff);
            gripPath.LineTo(gx + 3, gy - 8 + yOff);
            gripPath.MoveTo(gx - 5, gy + 3 + yOff);
            gripPath.LineTo(gx + 3, gy - 5 + yOff);
            var gripOp = PaintOpPool.GetDrawPathOp();
            gripOp.Path = gripPath;
            gripOp.StrokePaint = new SKPaint { Color = gColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            gripOp.Bounds = new SKRect(gx - 10, gy - 10 + yOff, gx + 6, gy + 6 + yOff);
            targetList.Add(gripOp);
        }
    }

    // Draws one textarea line at an explicit baseline Y, splitting it into
    // selected (white on blue) / unselected segments.
    private void DrawTextAreaLine(string text, float x, float lineY, float fontSize, ComputedStyle style, SKColor textColor,
        int selA, int selB, int lineStart, SKRect contentBox, DisplayList targetList)
    {
        if (string.IsNullOrEmpty(text)) return;
        int absStart = lineStart;
        int absEnd = lineStart + text.Length;

        void Draw(string seg, float segX, SKColor color)
        {
            var op = PaintOpPool.GetDrawTextOp();
            op.Text = seg;
            op.X = segX;
            op.Y = lineY + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight) + TotalOffsetY;
            op.Color = color;
            op.FontSize = fontSize;
            op.FontFamily = style.FontFamily ?? Core.Fonts.FontManager.StandardFontFamily;
            op.FontWeight = style.FontWeight;
            SetFontSlant(op, style);
            op.Bounds = new SKRect(segX, lineY + TotalOffsetY, segX + MeasureTextWidth(seg, fontSize, style.FontFamily), lineY + TotalOffsetY + fontSize);
            targetList.Add(op);
        }

        if (selA < 0 || selB <= absStart || selA >= absEnd)
        {
            Draw(text, x, textColor);
            return;
        }

        int a = Math.Max(selA, absStart);
        int b = Math.Min(selB, absEnd);
        float dx = x;
        if (a > absStart)
        {
            string pre = text[..(a - absStart)];
            Draw(pre, dx, textColor);
            dx += MeasureTextWidth(pre, fontSize, style.FontFamily);
        }
        if (b > a)
        {
            string sel = text[(a - absStart)..(b - absStart)];
            Draw(sel, dx, SKColors.White);
            dx += MeasureTextWidth(sel, fontSize, style.FontFamily);
        }
        if (b < absEnd)
            Draw(text[(b - absStart)..], dx, textColor);
    }

    private void DrawInputElement(Element element, LayoutBox box, ComputedStyle style)
    {
        string? value = element.Value;
        string? placeholder = element.GetAttribute("placeholder");
        string? inputType = element.InputType?.ToLowerInvariant();
        bool isPassword = inputType == "password";
        string displayText;
        bool isFocused = _focusedElement == element;
        bool isDisabled = element.HasAttribute("disabled");
        bool isReadOnly = element.HasAttribute("readonly");
        bool showPlaceholder = false;
        if (!string.IsNullOrEmpty(value))
            displayText = isPassword && !_passwordRevealed ? new string('●', value.Length) : value;
        else if (isFocused)
            displayText = "";
        else if (!string.IsNullOrEmpty(placeholder))
        {
            displayText = placeholder;
            showPlaceholder = true;
        }
        else
            return;

        // When _skipInputTextOverlay is active, the focused input's text/cursor/selection
        // will be drawn as a separate overlay after the page render. This allows the main
        // display list to be cached — only the overlay (a few paint ops) is rebuilt
        // on every keystroke, eliminating the O(n) DOM walk + display list rebuild cost.
        bool skipContent = _skipInputTextOverlay && isFocused;
        var targetList = skipContent ? _overlayList : _displayList;

        // Clip to padding box to prevent text overflow
        var paddingBox = box.PaddingBox;
        var clipRect = new SKRect(paddingBox.Left, paddingBox.Top + TotalOffsetY,
            paddingBox.Right, paddingBox.Bottom + TotalOffsetY);
        if (clipRect.Width > 0 && clipRect.Height > 0)
        {
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipRect = clipRect;
            targetList.Add(clipOp);
            // When skipContent is active, the overlay list is rendered separately outside
            // the main display list's clip stack. Add the same clip to the overlay list.
            if (skipContent && !ReferenceEquals(targetList, _overlayList))
            {
                var overlayClip = PaintOpPool.GetPushClipOp();
                overlayClip.ClipRect = clipRect;
                _overlayList.Add(overlayClip);
            }
        }

        // When overlay is active, clear the content area to cover any placeholder or old text
        // from the cached _displayList picture. Use the input's background color if available.
        if (skipContent)
        {
            var contentBox2 = box.ContentBox;
            var clearRect = new SKRect(contentBox2.Left, contentBox2.Top + TotalOffsetY,
                contentBox2.Right, contentBox2.Bottom + TotalOffsetY);
            var bgColor = style.BackgroundColor ?? new SKColor(255, 255, 255);
            var clearOp = PaintOpPool.GetDrawRectOp();
            clearOp.Rect = clearRect;
            clearOp.FillColor = bgColor;
            clearOp.Bounds = clearRect;
            _overlayList.Add(clearOp);
        }

        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        var contentBox = box.ContentBox;
        float textY = contentBox.Top + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight);
        SKColor textColor = showPlaceholder ? new SKColor(160, 160, 160) : (style.Color.Alpha > 0 ? style.Color : SKColors.Black);
        if (isDisabled)
            textColor = new SKColor(160, 160, 160);
        float textX = contentBox.Left + 2;

        // Reserve right-side space for internal controls (clear button, spin buttons,
        // password reveal) so typed/displayed text never overlaps them.
        bool hasClearButton = inputType == "search" && isFocused && !isDisabled && !string.IsNullOrEmpty(value);
        bool hasSpinButtons = inputType == "number" && isFocused && !isDisabled;
        bool hasRevealButton = isPassword && isFocused && !isDisabled;
        float reservedRight = 0;
        if (hasClearButton) reservedRight = 24;
        else if (hasSpinButtons) reservedRight = 22;
        else if (hasRevealButton) reservedRight = 22;

        float usableWidth = contentBox.Width - 4 - reservedRight;

        // Text and cursor rendering (always rendered; when overlay is active, goes to overlay list)
        // Determine the effective text to display and cursor/selection positions
        string effectText = isFocused && _inputImeComposing
            ? displayText[..Math.Min(_inputCursorPos, displayText.Length)] + _inputImeComposition +
              displayText[Math.Min(_inputCursorPos, displayText.Length)..]
            : displayText;

        int cursorPos = isFocused && _inputImeComposing
            ? Math.Min(_inputCursorPos, displayText.Length) + Math.Min(_inputImeCursor, _inputImeComposition.Length)
            : isFocused ? _inputCursorPos : 0;

        int selStart = isFocused ? _inputSelStart : -1;

        // Measure widths
        float fullTextWidth = MeasureTextWidth(effectText, fontSize, style.FontFamily);

        // Horizontal scroll offset: scroll only far enough to keep the caret visible
        // (matches BrowserApp.UpdateInputScrollOffset), so the caret stays where the
        // user clicked instead of snapping to a fixed fraction of the usable width.
        // The current scroll comes from the shared state owned by BrowserApp so the
        // painter and the click->caret mapping converge as text grows.
        float scrollOffset = 0;
        if (isFocused && fullTextWidth > usableWidth)
        {
            float cursorWidth = MeasureTextWidth(effectText[..Math.Min(cursorPos, effectText.Length)], fontSize, style.FontFamily);
            float maxScroll = Math.Max(0, fullTextWidth - usableWidth);
            scrollOffset = KeepCaretVisibleOffset(cursorWidth, _inputScrollOffset, usableWidth, maxScroll);
        }

        // Draw selection background
        if (isFocused && selStart >= 0 && selStart != cursorPos)
        {
            int a = Math.Min(selStart, cursorPos);
            int b = Math.Max(selStart, cursorPos);
            a = Math.Min(a, effectText.Length);
            b = Math.Min(b, effectText.Length);
            string beforeSel = effectText[..a];
            string selStr = effectText[a..b];
            float selX = textX + MeasureTextWidth(beforeSel, fontSize, style.FontFamily) - scrollOffset;
            float selW = MeasureTextWidth(selStr, fontSize, style.FontFamily);
            float clampLeft = Math.Max(textX, selX);
            float clampRight = Math.Min(textX + usableWidth, selX + selW);
            if (clampRight > clampLeft)
            {
                var selOp = PaintOpPool.GetDrawRectOp();
                selOp.Rect = new SKRect(clampLeft, contentBox.Top + TotalOffsetY + 1,
                    clampRight, contentBox.Bottom + TotalOffsetY - 1);
                selOp.FillColor = new SKColor(0x1A, 0x73, 0xE8);
                selOp.Bounds = selOp.Rect;
                targetList.Add(selOp);
            }
        }

        // Draw text in segments (supports selection highlight with inverted text color)
        float drawTextX = textX - scrollOffset;
        int selA = -1, selB = -1;
        if (isFocused && selStart >= 0 && selStart != cursorPos)
        {
            selA = Math.Min(selStart, cursorPos);
            selB = Math.Max(selStart, cursorPos);
        }
        if (selA >= 0)
        {
            // Before selection
            if (selA > 0 && selA <= effectText.Length)
                DrawTextSegment(effectText[..selA], drawTextX, fontSize, style, textColor,
                    contentBox, TotalOffsetY, targetList);
            drawTextX += MeasureTextWidth(effectText[..Math.Min(selA, effectText.Length)], fontSize, style.FontFamily ?? "Arial");
            // Selected text (white on blue)
            if (selB > selA && selB <= effectText.Length)
                DrawTextSegment(effectText[selA..selB], drawTextX, fontSize, style, SKColors.White,
                    contentBox, TotalOffsetY, targetList);
            drawTextX += MeasureTextWidth(effectText[Math.Min(selA, effectText.Length)..Math.Min(selB, effectText.Length)], fontSize, style.FontFamily ?? "Arial");
            // After selection
            if (selB < effectText.Length)
                DrawTextSegment(effectText[selB..], drawTextX, fontSize, style, textColor,
                    contentBox, TotalOffsetY, targetList);
        }
        else
        {
            DrawTextSegment(effectText, drawTextX, fontSize, style, textColor,
                contentBox, TotalOffsetY, targetList);
        }

        // IME composition underline — P2-2: routed through StyleableMarkerPainter
        // (thick solid = the default for the ACTIVE composition clause).
        if (isFocused && _inputImeComposing && _inputImeComposition.Length > 0)
        {
            float compStartX = textX + MeasureTextWidth(effectText[..Math.Min(_inputCursorPos, displayText.Length)], fontSize, style.FontFamily) - scrollOffset;
            float compWidth = MeasureTextWidth(_inputImeComposition, fontSize, style.FontFamily);
            compStartX = Math.Max(contentBox.Left, compStartX);
            compWidth = Math.Min(compWidth, contentBox.Right - compStartX);

            var marker = new StyleableMarker
            {
                Thickness = TextMarkerThickness.Thick,
                UnderlineColor = style.CaretColor ?? new SKColor(0, 0, 0),
                UnderlineStyle = ImeTextSpanUnderlineStyle.Solid,
                IsComposition = true,
            };
            if (compWidth > 1 && StyleableMarkerPainter.ShouldPaintUnderline(marker))
            {
                var origin = new PhysicalOffset(contentBox.Left, contentBox.Top + TotalOffsetY);
                var markerRect = LineRelativeRect.Create(
                    new PhysicalRect(compStartX - origin.Left, 0, compWidth, contentBox.Height),
                    rotation: null);
                var strokes = new List<DrawRectOp>();
                var squiggles = new List<DrawPathOp>();
                StyleableMarkerPainter.PaintUnderline(marker, strokes, squiggles, origin, style,
                    markerRect, contentBox.Height, realZoom: 1f, inDarkMode: false, fillColorOverride: default);
                foreach (var op in strokes) { op.Bounds = op.Rect; targetList.Add(op); }
                foreach (var p in squiggles)
                {
                    p.Bounds = p.Path.Bounds;
                    targetList.Add(p);
                }
            }
        }

        // Draw cursor (hidden for readonly/disabled inputs)
        if (isFocused && _inputShowCursor && !_inputImeComposing && !isReadOnly && !isDisabled)
        {
            float cursorWidth = MeasureTextWidth(effectText[..Math.Min(cursorPos, effectText.Length)], fontSize, style.FontFamily);
            float cursorX = textX + cursorWidth - scrollOffset;
            cursorX = Math.Clamp(cursorX, textX, textX + usableWidth);
            float cursorTop = contentBox.Top + TotalOffsetY + 2;
            float cursorBottom = contentBox.Bottom + TotalOffsetY - 2;
            var caretColor = style.CaretColor ?? new SKColor(0, 0, 0);
            var cursorOp = PaintOpPool.GetDrawLineOp();
            cursorOp.X1 = cursorX;
            cursorOp.Y1 = cursorTop;
            cursorOp.X2 = cursorX;
            cursorOp.Y2 = cursorBottom;
            cursorOp.Color = caretColor;
            cursorOp.StrokeWidth = 1.5f;
            cursorOp.Bounds = new SKRect(cursorX - 1, cursorTop, cursorX + 1, cursorBottom);
            targetList.Add(cursorOp);
        }

        // IME composition cursor
        if (isFocused && _inputImeComposing)
        {
            float imeCursorX = textX + MeasureTextWidth(effectText[..Math.Min(cursorPos, effectText.Length)], fontSize, style.FontFamily) - scrollOffset;
            imeCursorX = Math.Clamp(imeCursorX, textX, textX + usableWidth);
            float cursorTop = contentBox.Top + TotalOffsetY + 2;
            float cursorBottom = contentBox.Bottom + TotalOffsetY - 2;
            var caretColor = style.CaretColor ?? new SKColor(0, 0, 0);
            var cursorOp = PaintOpPool.GetDrawLineOp();
            cursorOp.X1 = imeCursorX;
            cursorOp.Y1 = cursorTop;
            cursorOp.X2 = imeCursorX;
            cursorOp.Y2 = cursorBottom;
            cursorOp.Color = caretColor;
            cursorOp.StrokeWidth = 1.5f;
            cursorOp.Bounds = new SKRect(imeCursorX - 1, cursorTop, imeCursorX + 1, cursorBottom);
            targetList.Add(cursorOp);
        }

        // Pop clip(s)
        if (clipRect.Width > 0 && clipRect.Height > 0)
        {
            targetList.Add(PaintOpPool.GetPopClipOp());
            if (skipContent && !ReferenceEquals(targetList, _overlayList))
                _overlayList.Add(PaintOpPool.GetPopClipOp());
        }

        // Search input clear button (shown on focus/hover when there is a value)
        if (hasClearButton)
        {
            var clearX = contentBox.Right - 14;
            var clearY = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
            bool hover = Math.Abs(_mouseX - (contentBox.Right - 14)) <= 8 &&
                         Math.Abs(_mouseY - (contentBox.Top + contentBox.Height / 2)) <= 8;
            bool pressed = _pressedControl == "search-clear";
            SKColor bgColor = pressed ? new SKColor(120, 120, 120) : hover ? new SKColor(120, 120, 120) : new SKColor(170, 170, 170);
            var clearBg = PaintOpPool.GetDrawPathOp();
            clearBg.Path = new SKPath();
            clearBg.Path.AddCircle(clearX, clearY, 7);
            clearBg.FillPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            clearBg.Bounds = new SKRect(clearX - 7, clearY - 7, clearX + 7, clearY + 7);
            targetList.Add(clearBg);

            var xPath = new SKPath();
            xPath.MoveTo(clearX - 2.5f, clearY - 2.5f);
            xPath.LineTo(clearX + 2.5f, clearY + 2.5f);
            xPath.MoveTo(clearX + 2.5f, clearY - 2.5f);
            xPath.LineTo(clearX - 2.5f, clearY + 2.5f);
            var xOp = PaintOpPool.GetDrawPathOp();
            xOp.Path = xPath;
            xOp.StrokePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            xOp.Bounds = new SKRect(clearX - 7, clearY - 7, clearX + 7, clearY + 7);
            targetList.Add(xOp);
        }

        // Number spin buttons (shown on focus/hover). Each half is a press target.
        if (hasSpinButtons)
        {
            // Draw coords include the chrome/content Y offset; hit coords are in doc space.
            float spinLeft = contentBox.Right - 20;
            float spinRight = contentBox.Right - 1;
            float spinTopDraw = contentBox.Top + TotalOffsetY + 1;
            float spinBottomDraw = contentBox.Bottom + TotalOffsetY - 1;
            float midYDraw = (spinTopDraw + spinBottomDraw) / 2;
            float spinTopHit = contentBox.Top + 1;
            float spinBottomHit = contentBox.Bottom - 1;
            float midYHit = (spinTopHit + spinBottomHit) / 2;
            bool hoverUp = _mouseX >= spinLeft - 1 && _mouseX <= spinRight + 1 &&
                           _mouseY >= spinTopHit && _mouseY < midYHit;
            bool hoverDown = _mouseX >= spinLeft - 1 && _mouseX <= spinRight + 1 &&
                             _mouseY >= midYHit && _mouseY <= spinBottomHit;
            bool pressedUp = _pressedControl == "number-up";
            bool pressedDown = _pressedControl == "number-down";

            // Spin button background (visible on hover/press for interactivity feedback)
            if (hoverUp || hoverDown || pressedUp || pressedDown)
            {
                var spinBg = PaintOpPool.GetDrawRectOp();
                spinBg.FillColor = new SKColor(0, 0, 0, (byte)(hoverUp || hoverDown ? 8 : 0));
                spinBg.Rect = new SKRect(spinLeft, spinTopDraw, spinRight, spinBottomDraw);
                spinBg.Bounds = spinBg.Rect;
                targetList.Add(spinBg);
            }

            float spinX = (spinLeft + spinRight) / 2;
            float arrowW = 7;
            float arrowH = 4.5f;
            float upC = spinTopDraw + (midYDraw - spinTopDraw) * 0.5f;
            float downC = midYDraw + (spinBottomDraw - midYDraw) * 0.5f;

            // Up arrow
            var upPath = new SKPath();
            upPath.MoveTo(spinX - arrowW / 2, upC + 1);
            upPath.LineTo(spinX + arrowW / 2, upC + 1);
            upPath.LineTo(spinX, upC - arrowH);
            upPath.Close();
            var upOp = PaintOpPool.GetDrawPathOp();
            upOp.Path = upPath;
            SKColor upColor = pressedUp ? new SKColor(0, 0, 255) : hoverUp ? new SKColor(60, 60, 60) : new SKColor(110, 110, 110);
            upOp.FillPaint = new SKPaint { Color = upColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            upOp.Bounds = new SKRect(spinX - arrowW, upC - arrowH, spinX + arrowW, upC + 1);
            targetList.Add(upOp);

            // Down arrow
            var downPath = new SKPath();
            downPath.MoveTo(spinX - arrowW / 2, downC - 1);
            downPath.LineTo(spinX + arrowW / 2, downC - 1);
            downPath.LineTo(spinX, downC + arrowH);
            downPath.Close();
            var downOp = PaintOpPool.GetDrawPathOp();
            downOp.Path = downPath;
            SKColor downColor = pressedDown ? new SKColor(0, 0, 255) : hoverDown ? new SKColor(60, 60, 60) : new SKColor(110, 110, 110);
            downOp.FillPaint = new SKPaint { Color = downColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            downOp.Bounds = new SKRect(spinX - arrowW, downC - 1, spinX + arrowW, downC + arrowH);
            targetList.Add(downOp);

            // Separator line between up/down
            var sepOp = PaintOpPool.GetDrawLineOp();
            sepOp.X1 = spinLeft;
            sepOp.Y1 = midYDraw;
            sepOp.X2 = spinRight;
            sepOp.Y2 = midYDraw;
            sepOp.Color = new SKColor(0, 0, 0, 25);
            sepOp.StrokeWidth = 1;
            sepOp.Bounds = new SKRect(spinLeft, midYDraw - 1, spinRight, midYDraw + 1);
            targetList.Add(sepOp);
        }

        // Password reveal toggle (eye icon shown on focus)
        if (hasRevealButton)
        {
            float eyeX = contentBox.Right - 14;
            float eyeCy = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
            bool hover = Math.Abs(_mouseX - (contentBox.Right - 14)) <= 10 &&
                         Math.Abs(_mouseY - (contentBox.Top + contentBox.Height / 2)) <= 10;
            bool pressed = _pressedControl == "password-reveal";

            // Eye outline: a rounded eye lens + pupil; slashed when revealed
            SKColor eyeColor = pressed ? new SKColor(0x1A, 0x73, 0xE8) : hover ? new SKColor(0x1A, 0x73, 0xE8) : new SKColor(110, 110, 110);
            var lensPath = new SKPath();
            float r = 5.5f;
            lensPath.MoveTo(eyeX - r, eyeCy);
            lensPath.CubicTo(eyeX - r, eyeCy - r * 1.35f, eyeX + r, eyeCy - r * 1.35f, eyeX + r, eyeCy);
            lensPath.CubicTo(eyeX + r, eyeCy + r * 1.35f, eyeX - r, eyeCy + r * 1.35f, eyeX - r, eyeCy);
            lensPath.Close();
            var lensOp = PaintOpPool.GetDrawPathOp();
            lensOp.Path = lensPath;
            lensOp.StrokePaint = new SKPaint { Color = eyeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
            lensOp.Bounds = new SKRect(eyeX - r, eyeCy - r - 1, eyeX + r, eyeCy + r + 1);
            targetList.Add(lensOp);

            var pupilOp = PaintOpPool.GetDrawPathOp();
            pupilOp.Path = new SKPath();
            pupilOp.Path.AddCircle(eyeX, eyeCy, 2);
            pupilOp.FillPaint = new SKPaint { Color = eyeColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            pupilOp.Bounds = new SKRect(eyeX - 3, eyeCy - 3, eyeX + 3, eyeCy + 3);
            targetList.Add(pupilOp);

            if (_passwordRevealed)
            {
                // Slash through the eye when password is shown in plain text
                var slashOp = PaintOpPool.GetDrawLineOp();
                slashOp.X1 = eyeX - r - 1;
                slashOp.Y1 = eyeCy + r + 0.5f;
                slashOp.X2 = eyeX + r + 1;
                slashOp.Y2 = eyeCy - r - 0.5f;
                slashOp.Color = eyeColor;
                slashOp.StrokeWidth = 1.4f;
                slashOp.Bounds = new SKRect(eyeX - r - 1, eyeCy - r - 1, eyeX + r + 1, eyeCy + r + 1);
                targetList.Add(slashOp);
            }
        }

        // Disabled overlay drawn outside clip so it covers the entire border area
        if (isDisabled)
        {
            var borderBox = box.BorderBox;
            var disableOp = PaintOpPool.GetDrawRectOp();
            disableOp.FillColor = new SKColor(200, 200, 200, 100);
            disableOp.Rect = new SKRect(borderBox.Left, borderBox.Top + TotalOffsetY,
                borderBox.Right, borderBox.Bottom + TotalOffsetY);
            targetList.Add(disableOp);
        }
        else if (isFocused)
        {
            DrawFocusRing(element, box, targetList);
        }
    }

    private void DrawTextSegment(string text, float x, float fontSize, ComputedStyle style, SKColor color, SKRect contentBox, float yOffset, DisplayList? targetList = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var op = PaintOpPool.GetDrawTextOp();
        op.Text = text;
        op.X = x;
        op.Y = contentBox.Top + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight) + yOffset;
        op.Color = color;
        op.FontSize = fontSize;
        op.FontFamily = style.FontFamily ?? Core.Fonts.FontManager.StandardFontFamily;
        op.FontWeight = style.FontWeight;
        SetFontSlant(op, style);
        op.Bounds = new SKRect(contentBox.Left, contentBox.Top + yOffset,
            contentBox.Right, contentBox.Bottom + yOffset);
        (targetList ?? _displayList).Add(op);
    }

    private void DrawSelectElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var optionTexts = new List<string>();
        foreach (var child in element.Children)
        {
            if (child is Element optEl && optEl.TagName is "OPTION" or "OPTGROUP")
            {
                var optText = optEl.TextContent?.Trim();
                optionTexts.Add(string.IsNullOrEmpty(optText) ? " " : optText);
            }
        }
        if (optionTexts.Count == 0)
            optionTexts.Add("Select...");

        // A listbox (<select multiple> or size > 1) stacks every option and has no
        // menu-list arrow; Chrome also keeps a classic scrollbar gutter for it even
        // when the rows fit (offsetWidth - clientWidth = 16px).
        bool isListBox = element.HasAttribute("multiple")
            || (int.TryParse(element.GetAttribute("size"), out int sizeAttr) && sizeAttr > 1);
        if (isListBox)
        {
            DrawSelectListBox(box, style, optionTexts, element.HasAttribute("disabled"));
            return;
        }

        string displayText = optionTexts[0];
        foreach (var child in element.Children)
        {
            if (child is Element childEl && childEl.TagName == "OPTION" && childEl.HasAttribute("selected"))
            {
                var optText = childEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(optText))
                    displayText = optText;
                break;
            }
        }
        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        float textWidth = MeasureTextWidth(displayText, fontSize, style.FontFamily);
        var contentBox = box.ContentBox;
        bool isDisabled = element.HasAttribute("disabled");
        float textX = contentBox.Left + 4;
        float textY = contentBox.Top + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight);
        var op = PaintOpPool.GetDrawTextOp();
        op.Text = displayText;
        op.X = textX;
        op.Y = textY + TotalOffsetY;
        op.Color = isDisabled ? new SKColor(160, 160, 160) : (style.Color.Alpha > 0 ? style.Color : SKColors.Black);
        op.FontSize = fontSize;
        op.FontFamily = style.FontFamily ?? Core.Fonts.FontManager.StandardFontFamily;
        op.Bounds = new SKRect(textX, contentBox.Top + TotalOffsetY, textX + textWidth, contentBox.Bottom + TotalOffsetY);
        _displayList.Add(op);

        float arrowSize = 6;
        float arrowX = contentBox.Right - 16;
        float arrowY = contentBox.Top + (contentBox.Height - arrowSize) / 2 + TotalOffsetY;
        SKColor arrowColor = isDisabled ? new SKColor(180, 180, 180) : new SKColor(120, 120, 120);
        var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY);
        arrowPath.LineTo(arrowX + arrowSize, arrowY);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize);
        arrowPath.Close();
        var arrowOp = PaintOpPool.GetDrawPathOp();
        arrowOp.Path = arrowPath;
        arrowOp.FillPaint = new SKPaint { Color = arrowColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        arrowOp.Bounds = new SKRect(arrowX, arrowY, arrowX + arrowSize, arrowY + arrowSize);
        _displayList.Add(arrowOp);

        if (isDisabled)
        {
            var borderBox = box.BorderBox;
            var disableOp = PaintOpPool.GetDrawRectOp();
            disableOp.FillColor = new SKColor(230, 230, 230, 120);
            disableOp.Rect = new SKRect(borderBox.Left, borderBox.Top + TotalOffsetY,
                borderBox.Right, borderBox.Bottom + TotalOffsetY);
            _displayList.Add(disableOp);
        }
        else if (_focusedElement == element)
        {
            DrawFocusRing(element, box);
        }
    }

    /// <summary>
    /// Paint a listbox: one option per row, clipped by simply not drawing the rows
    /// that fall outside the content box, plus the classic scrollbar gutter Chrome
    /// keeps on the right even when the rows fit.
    /// </summary>
    private void DrawSelectListBox(LayoutBox box, ComputedStyle style, List<string> options, bool isDisabled)
    {
        var content = box.ContentBox;
        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        float rowHeight = Math.Max(Core.Fonts.LineBoxMetrics.GetLineHeight(style), fontSize * 1.2f);
        float gutter = Math.Min(16, Math.Max(0, content.Width / 2));
        int visibleRows = Math.Max(1, (int)(content.Height / rowHeight));

        // The option elements are laid out and painted as real boxes (one row each),
        // so only the gutter is drawn here - repainting the labels would double-strike
        // them and make the lower rows look bold.

        if (gutter <= 0)
            return;

        float gutterLeft = content.Right - gutter;
        float trackTop = content.Top + TotalOffsetY;
        var trackOp = PaintOpPool.GetDrawRectOp();
        trackOp.FillColor = new SKColor(247, 247, 247);
        trackOp.Rect = new SKRect(gutterLeft, trackTop, content.Right, content.Bottom + TotalOffsetY);
        _displayList.Add(trackOp);

        // The thumb only appears when there is something to scroll, but Chrome still
        // spends the gutter width on it - so the track is always painted.
        if (options.Count > visibleRows)
        {
            float trackHeight = content.Height;
            float thumbHeight = Math.Max(gutter, trackHeight * visibleRows / options.Count);
            var thumbOp = PaintOpPool.GetDrawRectOp();
            thumbOp.FillColor = new SKColor(193, 193, 193);
            thumbOp.Rect = new SKRect(gutterLeft + 1, trackTop + 1, content.Right - 1, trackTop + thumbHeight - 1);
            _displayList.Add(thumbOp);
        }
    }

    private void DrawSelectDropdown()
    {
        var rect = _selectDropdownRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        float dy = TotalOffsetY;
        float fontSize = 14;

        var shadowOp = PaintOpPool.GetDrawRectOp();
        shadowOp.FillColor = new SKColor(0, 0, 0, 40);
        shadowOp.Rect = new SKRect(rect.Left + 2, rect.Top + dy + 2, rect.Right + 2, rect.Bottom + dy + 2);
        _displayList.Add(shadowOp);

        var bgOp = PaintOpPool.GetDrawRectOp();
        bgOp.FillColor = SKColors.White;
        bgOp.Rect = new SKRect(rect.Left, rect.Top + dy, rect.Right, rect.Bottom + dy);
        _displayList.Add(bgOp);

        if (_selectOptionRects != null)
        {
            for (int i = 0; i < _selectOptionRects.Count; i++)
            {
                var (option, orect) = _selectOptionRects[i];
                bool isSelected = option.HasAttribute("selected");
                bool isHovered = i == _selectHoverIndex;

                if (isHovered || isSelected)
                {
                    var hlOp = PaintOpPool.GetDrawRectOp();
                    hlOp.FillColor = isSelected ? new SKColor(0x1A, 0x73, 0xE8) : new SKColor(0, 0, 0, 16);
                    hlOp.Rect = new SKRect(orect.Left, orect.Top + dy, orect.Right, orect.Bottom + dy);
                    _displayList.Add(hlOp);
                }

                var textOp = PaintOpPool.GetDrawTextOp();
                textOp.Text = option.TextContent?.Trim() ?? "";
                textOp.X = orect.Left + 8;
                textOp.Y = orect.Top + dy + (orect.Height - fontSize) / 2 + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, "Segoe UI, Arial, sans-serif");
                textOp.Color = isSelected ? SKColors.White : new SKColor(50, 50, 50);
                textOp.FontSize = fontSize;
                textOp.FontFamily = "Segoe UI, Arial, sans-serif";
                float tw = MeasureTextWidth(textOp.Text, fontSize, textOp.FontFamily);
                textOp.Bounds = new SKRect(textOp.X, orect.Top + dy, textOp.X + tw, orect.Bottom + dy);
                _displayList.Add(textOp);
            }
        }

        var borderOp = PaintOpPool.GetDrawPathOp();
        var path = new SKPath();
        path.MoveTo(rect.Left, rect.Top + dy);
        path.LineTo(rect.Right, rect.Top + dy);
        path.LineTo(rect.Right, rect.Bottom + dy);
        path.LineTo(rect.Left, rect.Bottom + dy);
        path.Close();
        borderOp.Path = path;
        borderOp.StrokePaint = new SKPaint { Color = new SKColor(180, 180, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        borderOp.Bounds = new SKRect(rect.Left, rect.Top + dy, rect.Right, rect.Bottom + dy);
        _displayList.Add(borderOp);
    }

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (Core.Layout.TextMeasurer.Instance != null)
            return Core.Layout.TextMeasurer.Instance.MeasureText(text, fontFamily ?? "Arial", fontSize);
        float avgCharWidth = fontSize * 0.55f;
        return text.Length * avgCharWidth;
    }

    // Scrolls horizontally only far enough to keep the caret visible (with a small
    // margin) instead of snapping it to a fixed fraction of the width. Must match
    // BrowserApp.KeepCaretVisibleOffset so click->caret mapping stays consistent.
    private static float KeepCaretVisibleOffset(float caretWidth, float currentScroll, float usableWidth, float maxScroll)
    {
        const float margin = 8;
        float caretX = caretWidth - currentScroll;
        if (caretX < margin)
            return Math.Clamp(caretWidth - margin, 0, maxScroll);
        if (caretX > usableWidth - margin)
            return Math.Clamp(caretWidth - (usableWidth - margin), 0, maxScroll);
        return Math.Clamp(currentScroll, 0, maxScroll);
    }

    private void DrawCheckRadioElement(Element element, LayoutBox box, ComputedStyle style, string inputType)
    {
        bool isChecked = element.HasAttribute("checked");
        bool isDisabled = element.HasAttribute("disabled");
        bool isHovered = element.IsHovered;
        bool isPressed = _mouseDown && isHovered;
        var contentBox = box.ContentBox;
        float size = Math.Min(contentBox.Width, contentBox.Height);
        float cx = contentBox.Left + contentBox.Width / 2;
        float cy = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
        float boxSize = Math.Min(size, 16);
        float halfBox = boxSize / 2;

        SKColor accentColor = style.AccentColor ?? new SKColor(0x1A, 0x73, 0xE8);
        SKColor borderColor = isDisabled ? new SKColor(180, 180, 180) : new SKColor(120, 120, 120);
        SKColor fillColor = isChecked ? accentColor : SKColors.White;
        if (isDisabled)
        {
            if (isChecked)
                fillColor = new SKColor(180, 180, 180);
            else
                fillColor = new SKColor(230, 230, 230);
        }
        else
        {
            // Hover feedback: light tint + darker border
            if (isHovered)
            {
                borderColor = new SKColor(0x1A, 0x73, 0xE8);
                if (!isChecked)
                    fillColor = new SKColor(0xE8, 0xF0, 0xFE);
            }
            if (isPressed)
            {
                borderColor = new SKColor(0x15, 0x5D, 0xC8);
                if (!isChecked)
                    fillColor = new SKColor(0xD6, 0xE6, 0xFD);
            }
        }

        if (inputType == "checkbox")
        {
            var rect = new SKRect(cx - halfBox, cy - halfBox, cx + halfBox, cy + halfBox);
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = CreateRoundedRectPath(rect, 3);
            bgOp.FillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            bgOp.StrokePaint = new SKPaint { Color = borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = isHovered ? 2f : 1.5f, IsAntialias = true };
            bgOp.Bounds = rect;
            _displayList.Add(bgOp);

        if (isChecked)
        {
            var checkPath = new SKPath();
            checkPath.MoveTo(cx - halfBox * 0.5f, cy);
            checkPath.LineTo(cx - halfBox * 0.1f, cy + halfBox * 0.4f);
            checkPath.LineTo(cx + halfBox * 0.5f, cy - halfBox * 0.35f);
            var checkOp = PaintOpPool.GetDrawPathOp();
            checkOp.Path = checkPath;
            SKColor checkColor = isDisabled ? SKColors.White : SKColors.White;
            checkOp.StrokePaint = new SKPaint { Color = checkColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            checkOp.Bounds = rect;
            _displayList.Add(checkOp);
        }
        if (!isDisabled && _focusedElement == element)
            DrawFocusRing(element, box);
    }
        else // radio
        {
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = new SKPath();
            bgOp.Path.AddCircle(cx, cy, halfBox);
            bgOp.FillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            bgOp.StrokePaint = new SKPaint { Color = borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = isHovered ? 2f : 1.5f, IsAntialias = true };
            bgOp.Bounds = new SKRect(cx - halfBox, cy - halfBox, cx + halfBox, cy + halfBox);
            _displayList.Add(bgOp);

            if (isChecked)
            {
                SKColor dotColor = isDisabled ? new SKColor(150, 150, 150) : accentColor;
                var dotOp = PaintOpPool.GetDrawPathOp();
                dotOp.Path = new SKPath();
                dotOp.Path.AddCircle(cx, cy, halfBox * 0.45f);
                dotOp.FillPaint = new SKPaint { Color = dotColor, Style = SKPaintStyle.Fill, IsAntialias = true };
                dotOp.Bounds = new SKRect(cx - halfBox, cy - halfBox, cx + halfBox, cy + halfBox);
                _displayList.Add(dotOp);
            }
            if (!isDisabled && _focusedElement == element)
                DrawFocusRing(element, box);
        }
    }

    private void DrawRangeElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        bool isDisabled = element.HasAttribute("disabled");
        float trackY = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
        float trackLeft = contentBox.Left + 4;
        float trackRight = contentBox.Right - 4;

        SKColor trackColor = isDisabled ? new SKColor(230, 230, 230) : new SKColor(200, 200, 200);
        SKColor accentColor = style.AccentColor ?? new SKColor(0x1A, 0x73, 0xE8);
        if (isDisabled) accentColor = new SKColor(200, 200, 200);

        var trackOp = PaintOpPool.GetDrawLineOp();
        trackOp.X1 = trackLeft;
        trackOp.Y1 = trackY;
        trackOp.X2 = trackRight;
        trackOp.Y2 = trackY;
        trackOp.Color = trackColor;
        trackOp.StrokeWidth = 4;
        trackOp.Bounds = new SKRect(trackLeft, trackY - 2, trackRight, trackY + 2);
        _displayList.Add(trackOp);

        float min = 0, max = 100, val = 50;
        float.TryParse(element.GetAttribute("min") ?? "0", out min);
        float.TryParse(element.GetAttribute("max") ?? "100", out max);
        float.TryParse(element.GetAttribute("value") ?? "50", out val);
        float ratio = max > min ? (val - min) / (max - min) : 0.5f;
        float thumbX = trackLeft + (trackRight - trackLeft) * ratio;

        var filledOp = PaintOpPool.GetDrawLineOp();
        filledOp.X1 = trackLeft;
        filledOp.Y1 = trackY;
        filledOp.X2 = thumbX;
        filledOp.Y2 = trackY;
        filledOp.Color = accentColor;
        filledOp.StrokeWidth = 4;
        filledOp.Bounds = new SKRect(trackLeft, trackY - 2, thumbX, trackY + 2);
        _displayList.Add(filledOp);

        float thumbRadius = 8;
        var thumbOp = PaintOpPool.GetDrawPathOp();
        thumbOp.Path = new SKPath();
        thumbOp.Path.AddCircle(thumbX, trackY, thumbRadius);
        thumbOp.FillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        thumbOp.StrokePaint = new SKPaint { Color = accentColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        thumbOp.Bounds = new SKRect(thumbX - thumbRadius, trackY - thumbRadius, thumbX + thumbRadius, trackY + thumbRadius);
        _displayList.Add(thumbOp);

        if (!isDisabled && _focusedElement == element)
            DrawFocusRing(element, box);
    }

    private void DrawColorInputElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        string colorStr = element.GetAttribute("value") ?? "#000000";
        SKColor color;
        try { color = SKColor.Parse(colorStr); } catch { color = SKColors.Black; }

        float swatchSize = Math.Min(contentBox.Width, contentBox.Height) - 4;
        float x = contentBox.Left + (contentBox.Width - swatchSize) / 2;
        float y = contentBox.Top + (contentBox.Height - swatchSize) / 2 + TotalOffsetY;

        var borderOp = PaintOpPool.GetDrawPathOp();
        borderOp.Path = CreateRoundedRectPath(new SKRect(x - 1, y - 1, x + swatchSize + 1, y + swatchSize + 1), 3);
        borderOp.StrokePaint = new SKPaint { Color = new SKColor(120, 120, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        borderOp.Bounds = new SKRect(x - 1, y - 1, x + swatchSize + 1, y + swatchSize + 1);
        _displayList.Add(borderOp);

        var fillOp = PaintOpPool.GetDrawPathOp();
        fillOp.Path = CreateRoundedRectPath(new SKRect(x, y, x + swatchSize, y + swatchSize), 2);
        fillOp.FillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        fillOp.Bounds = new SKRect(x, y, x + swatchSize, y + swatchSize);
        _displayList.Add(fillOp);
    }

    private void DrawFileInputElement(Element element, LayoutBox box, ComputedStyle style)
    {
        bool isDisabled = element.HasAttribute("disabled");
        var borderBox = box.BorderBox;
        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        var contentBox = box.ContentBox;

        float btnW = 90;
        float btnH = contentBox.Height;
        var btnRect = new SKRect(contentBox.Left, contentBox.Top + TotalOffsetY,
            contentBox.Left + btnW, contentBox.Top + btnH + TotalOffsetY);
        if (btnH < 18) btnH = 18;

        SKColor btnBg = isDisabled ? new SKColor(239, 239, 239) : SKColor.Parse("#E1E1E1");
        SKColor btnBorder = isDisabled ? new SKColor(200, 200, 200) : new SKColor(0x80, 0x80, 0x80);
        var btnOp = PaintOpPool.GetDrawRectOp();
        btnOp.Rect = btnRect;
        btnOp.FillColor = btnBg;
        btnOp.BorderTopWidth = 2;
        btnOp.BorderBottomWidth = 2;
        btnOp.BorderLeftWidth = 2;
        btnOp.BorderRightWidth = 2;
        btnOp.BorderTopColor = btnBorder;
        btnOp.BorderBottomColor = btnBorder;
        btnOp.BorderLeftColor = btnBorder;
        btnOp.BorderRightColor = btnBorder;
        btnOp.Bounds = btnRect;
        _displayList.Add(btnOp);

        string btnLabel = "Choose File";
        var labelOp = PaintOpPool.GetDrawTextOp();
        labelOp.Text = btnLabel;
        labelOp.X = btnRect.Left + 6;
        labelOp.Y = btnRect.Top + (btnH - fontSize) / 2 + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight);
        labelOp.Color = isDisabled ? new SKColor(160, 160, 160) : SKColors.Black;
        labelOp.FontSize = fontSize;
        labelOp.FontFamily = style.FontFamily ?? "Segoe UI, Arial, sans-serif";
        labelOp.Bounds = new SKRect(btnRect.Left, btnRect.Top, btnRect.Right, btnRect.Bottom);
        _displayList.Add(labelOp);

        string fileName = element.GetAttribute("value") ?? "";
        if (string.IsNullOrEmpty(fileName))
            fileName = "No file chosen";
        var fileOp = PaintOpPool.GetDrawTextOp();
        fileOp.Text = fileName;
        fileOp.X = btnRect.Right + 6;
        fileOp.Y = btnRect.Top + (btnH - fontSize) / 2 + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight);
        fileOp.Color = isDisabled ? new SKColor(160, 160, 160) : new SKColor(80, 80, 80);
        fileOp.FontSize = fontSize;
        fileOp.FontFamily = style.FontFamily ?? "Segoe UI, Arial, sans-serif";
        fileOp.Bounds = new SKRect(btnRect.Right, btnRect.Top, borderBox.Right, btnRect.Bottom);
        _displayList.Add(fileOp);
    }

    private void DrawDateInputElement(Element element, LayoutBox box, ComputedStyle style, string inputType)
    {
        string? value = element.GetAttribute("value");
        bool isDisabled = element.HasAttribute("disabled");
        bool isFocused = _focusedElement == element;
        string displayText;
        if (!string.IsNullOrEmpty(value))
        {
            displayText = value;
            if (inputType == "date" && value.Length >= 10)
                displayText = $"{value[8..10]}/{value[5..7]}/{value[..4]}";
            else if (inputType == "month" && value.Length >= 7)
                displayText = $"{value[5..7]}/{value[..4]}";
        }
        else if (isFocused)
            displayText = "";
        else
        {
            string ph = inputType switch
            {
                "date" => "yyyy/mm/dd",
                "datetime-local" => "yyyy/mm/dd --:--",
                "month" => "yyyy/mm",
                "time" => "--:--",
                "week" => "yyyy-Www",
                _ => ""
            };
            displayText = ph;
        }

        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        var contentBox = box.ContentBox;
        float textY = contentBox.Top + Core.Fonts.LineBoxMetrics.GetTextAscent(fontSize, style.FontFamily, style.FontWeight);
        SKColor textColor = string.IsNullOrEmpty(value) ? new SKColor(160, 160, 160) : (style.Color.Alpha > 0 ? style.Color : SKColors.Black);
        if (isDisabled)
            textColor = new SKColor(160, 160, 160);
        float textX = contentBox.Left + 2;

        var clipRect = new SKRect(contentBox.Left, contentBox.Top + TotalOffsetY,
            contentBox.Right, contentBox.Bottom + TotalOffsetY);
        if (clipRect.Width > 0 && clipRect.Height > 0)
            _displayList.Add(PaintOpPool.GetPushClipOp());

        var textOp = PaintOpPool.GetDrawTextOp();
        textOp.Text = displayText;
        textOp.X = textX;
        textOp.Y = textY + TotalOffsetY;
        textOp.Color = textColor;
        textOp.FontSize = fontSize;
        textOp.FontFamily = style.FontFamily ?? "Segoe UI, Arial, sans-serif";
        textOp.Bounds = new SKRect(textX, contentBox.Top + TotalOffsetY, contentBox.Right, contentBox.Bottom + TotalOffsetY);
        _displayList.Add(textOp);

        if (clipRect.Width > 0 && clipRect.Height > 0)
            _displayList.Add(PaintOpPool.GetPopClipOp());

        // Date picker calendar indicator on the right
        var calX = contentBox.Right - 16;
        var calY = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
        var calOp = PaintOpPool.GetDrawPathOp();
        calOp.Path = CreateRoundedRectPath(new SKRect(calX - 5, calY - 5, calX + 5, calY + 5), 1);
        calOp.StrokePaint = new SKPaint { Color = new SKColor(120, 120, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        calOp.Bounds = new SKRect(calX - 5, calY - 5, calX + 5, calY + 5);
        _displayList.Add(calOp);
        var calLine1 = PaintOpPool.GetDrawLineOp();
        calLine1.X1 = calX - 5; calLine1.Y1 = calY - 2.5f; calLine1.X2 = calX + 5; calLine1.Y2 = calY - 2.5f;
        calLine1.Color = new SKColor(120, 120, 120); calLine1.StrokeWidth = 1;
        calLine1.Bounds = new SKRect(calX - 5, calY - 3, calX + 5, calY - 2);
        _displayList.Add(calLine1);

        if (!isDisabled && isFocused)
            DrawFocusRing(element, box);
    }

    private void DrawProgressElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        float radius = contentBox.Height / 2;
        float barY = contentBox.Top + TotalOffsetY;

        var bgPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height), radius);
        var bgOp = PaintOpPool.GetDrawPathOp();
        bgOp.Path = bgPath;
        bgOp.FillPaint = new SKPaint { Color = new SKColor(220, 220, 220), Style = SKPaintStyle.Fill, IsAntialias = true };
        bgOp.Bounds = new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height);
        _displayList.Add(bgOp);

        float max = 1, val = 0;
        float.TryParse(element.GetAttribute("max") ?? "1", out max);
        float.TryParse(element.GetAttribute("value") ?? "0", out val);
        float ratio = max > 0 ? Math.Clamp(val / max, 0, 1) : 0;
        float fillRight = contentBox.Left + contentBox.Width * ratio;

        if (ratio > 0)
        {
            var fillPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height), radius);
            var fillOp = PaintOpPool.GetDrawPathOp();
            fillOp.Path = fillPath;
            fillOp.FillPaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8), Style = SKPaintStyle.Fill, IsAntialias = true };
            fillOp.Bounds = new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height);
            _displayList.Add(fillOp);
        }
    }

    private void DrawMeterElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        float radius = contentBox.Height / 2;
        float barY = contentBox.Top + TotalOffsetY;

        var bgPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height), radius);
        var bgOp = PaintOpPool.GetDrawPathOp();
        bgOp.Path = bgPath;
        bgOp.FillPaint = new SKPaint { Color = new SKColor(220, 220, 220), Style = SKPaintStyle.Fill, IsAntialias = true };
        bgOp.Bounds = new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height);
        _displayList.Add(bgOp);

        float min = 0, max = 1, low = float.NaN, high = float.NaN, optimum = float.NaN;
        float.TryParse(element.GetAttribute("min") ?? "0", out min);
        float.TryParse(element.GetAttribute("max") ?? "1", out max);
        float val = 0;
        float.TryParse(element.GetAttribute("value") ?? "0", out val);
        float ratio = max > min ? Math.Clamp((val - min) / (max - min), 0, 1) : 0;
        float fillRight = contentBox.Left + contentBox.Width * ratio;

        SKColor fillColor = new SKColor(0x1A, 0x73, 0xE8);
        if (!float.TryParse(element.GetAttribute("low") ?? "", out float lowVal)) lowVal = min;
        if (!float.TryParse(element.GetAttribute("high") ?? "", out float highVal)) highVal = max;
        if (!float.TryParse(element.GetAttribute("optimum") ?? "", out float optVal)) optVal = (min + max) / 2;

        if (val < lowVal || val > highVal)
            fillColor = new SKColor(220, 50, 50);
        else if ((optVal >= lowVal && val >= optVal) || (optVal <= highVal && val <= optVal))
            fillColor = new SKColor(0x0B, 0x80, 0x43);
        else
            fillColor = new SKColor(0xF4, 0xB4, 0x00);

        if (ratio > 0)
        {
            var fillPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height), radius);
            var fillOp = PaintOpPool.GetDrawPathOp();
            fillOp.Path = fillPath;
            fillOp.FillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            fillOp.Bounds = new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height);
            _displayList.Add(fillOp);
        }
    }

    private void DrawTextNode(TextNode textNode, LayoutBox box, ComputedStyle parentStyle)
    {
        var rawText = textNode.TextContent;
        if (string.IsNullOrEmpty(rawText)) return;
        // Whitespace-only text nodes (e.g. the "\n    " between block children)
        // contribute no visible text; painting them renders a missing-glyph tofu
        // box at the start of block containers.
        if (string.IsNullOrWhiteSpace(rawText)) return;
        var text = NormalizeFallbackText(rawText);
        text = Acrux.Core.Layout.Inline.InlineItemsBuilder.ApplyTextTransform(text, parentStyle?.TextTransform);
        var contentBox = box.ContentBox;
        float y = contentBox.Top + (parentStyle?.FontSize ?? 16) + TotalOffsetY;
        var textColor = parentStyle?.Color ?? SKColors.Black;
        if (parentStyle != null && parentStyle.Opacity < 1.0f)
            textColor = textColor.WithAlpha((byte)(textColor.Alpha * parentStyle.Opacity));
        float textWidth = MeasureTextWidth(text, parentStyle?.FontSize ?? 16, parentStyle?.FontFamily ?? "Arial", parentStyle?.FontWeight ?? FontWeight.Normal);
        float textX = contentBox.Left;
        if (parentStyle?.TextAlign == TextAlignType.Center)
            textX = contentBox.Left + contentBox.Width / 2;
        else if (parentStyle?.TextAlign == TextAlignType.Right || parentStyle?.TextAlign == TextAlignType.End)
            textX = contentBox.Right;
        var op = PaintOpPool.GetDrawTextOp();
        op.Text = text;
        op.X = textX;
        op.Y = y;
        op.Color = textColor;
        op.FontSize = parentStyle?.FontSize ?? 16;
        op.FontFamily = parentStyle?.FontFamily ?? Core.Fonts.FontManager.StandardFontFamily;
        op.FontWeight = parentStyle?.FontWeight ?? FontWeight.Normal;
        op.TextAlign = parentStyle?.TextAlign ?? TextAlignType.Start;
        SetDecorations(op, parentStyle, textNode.ParentElement);
        op.LetterSpacing = parentStyle?.LetterSpacing ?? 0;
        SetFontSlant(op, parentStyle);
        if (parentStyle?.TextShadow != null && parentStyle.TextShadow.Count > 0)
            op.TextShadows = parentStyle.TextShadow;
        float boundTop = y - (parentStyle?.FontSize ?? 16);
        float boundBottom = y;
        op.Bounds = new SKRect(textX, boundTop, textX + textWidth, boundBottom);

        // Add selection highlight clipped to the overlapping region
        _highlightPainter.PaintHighlight(textNode, text, op.Bounds,
            op.FontSize, op.FontFamily, op.FontWeight);

        _displayList.Add(op);
    }

    private float GetPixelLengthFromStyle(Length length, float defaultValue)
    {
        return length is PixelLength pixelLength ? pixelLength.Value : defaultValue;
    }

    /// <summary>
    /// Collapse '\r' / '\n' / '\t' occurring in text painted through the no-run
    /// fallback path. Such containers have no line-break data and paint a single
    /// run; handing a raw newline to Skia's DrawText renders a missing-glyph
    /// tofu box.
    /// </summary>
    private static string NormalizeFallbackText(string text)
    {
        if (text.IndexOf('\r') < 0 && text.IndexOf('\n') < 0 && text.IndexOf('\t') < 0)
            return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n' or '\t')
            {
                if (sb.Length == 0 || sb[^1] != ' ') sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private SKPath CreateRoundedRectPath(SKRect rect, float radius)
    {
        var path = new SKPath();
        float x = rect.Left, y = rect.Top, w = rect.Width, h = rect.Height;
        path.MoveTo(x + radius, y);
        path.LineTo(x + w - radius, y);
        path.QuadTo(x + w, y, x + w, y + radius);
        path.LineTo(x + w, y + h - radius);
        path.QuadTo(x + w, y + h, x + w - radius, y + h);
        path.LineTo(x + radius, y + h);
        path.QuadTo(x, y + h, x, y + h - radius);
        path.LineTo(x, y + radius);
        path.QuadTo(x, y, x + radius, y);
        path.Close();
        return path;
    }

    private static string GetTextEmphasisMarkString(ComputedStyle? style) =>
        Acrux.Core.Css.TextEmphasisMarks.GetMarkGlyph(style);

    /// <summary>
    /// Paint the box decoration of one fragment of an inline box behind the
    /// glyphs of its line. Inline elements have no box of their own in this
    /// engine, so the decoration is drawn over the font content area plus
    /// padding (CSS 2.1 §10.6.1), and a wrapped element paints one fragment per
    /// line. <paramref name="sides"/> carries the box-decoration-break rule: the
    /// sides facing a break are not decorated (CSS Fragmentation 3 §4.2).
    /// </summary>
    private void PaintInlineRunDecorations(Element? owner, ComputedStyle? style,
        float left, float right, float baselineY, PhysicalBoxSides sides = PhysicalBoxSides.All)
    {
        // Only inline boxes are decorated per fragment: a block's own background and
        // borders already come from its box painting phase, and inline-level
        // boxes with their own layout box (inline-block) paint through that path.
        if (owner == null || style == null || style.Display != DisplayType.Inline)
            return;
        bool hasColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasImage = style.BackgroundImage is { Count: > 0 } &&
                        style.BackgroundImage!.Any(s => !string.IsNullOrEmpty(s) && s != "none");
        bool hasBorder = (style.BorderTopWidth > 0 && style.BorderTopStyle != BorderStyle.None) ||
                         (style.BorderRightWidth > 0 && style.BorderRightStyle != BorderStyle.None) ||
                         (style.BorderBottomWidth > 0 && style.BorderBottomStyle != BorderStyle.None) ||
                         (style.BorderLeftWidth > 0 && style.BorderLeftStyle != BorderStyle.None);
        bool hasOutline = style.OutlineWidth > 0 && style.OutlineStyle != BorderStyle.None;
        if (!hasColor && !hasImage && !hasBorder && !hasOutline)
            return;

        bool includeLeft = (sides & PhysicalBoxSides.Left) != 0;
        bool includeRight = (sides & PhysicalBoxSides.Right) != 0;

        float fs = style.FontSize;
        float padL = ResolveInlinePadding(style.PaddingLeft, fs);
        float padR = ResolveInlinePadding(style.PaddingRight, fs);
        float padT = ResolveInlinePadding(style.PaddingTop, fs);
        float padB = ResolveInlinePadding(style.PaddingBottom, fs);
        float bt = style.BorderTopWidth, br = style.BorderRightWidth;
        float bb = style.BorderBottomWidth, bl = style.BorderLeftWidth;

        var metrics = Core.Fonts.LineBoxMetrics.GetFontMetrics(style);
        // A sliced fragment is a cut through a box that would have been continuous,
        // so the side facing the break has neither a border line nor the padding area
        // behind it: the fragment box ends at the content edge there (CSS
        // Fragmentation 3 §4.2). 'clone' keeps the full box on every fragment.
        var borderBox = new SKRect(
            includeLeft ? left - padL - bl : left,
            baselineY - metrics.FloatAscent - padT - bt,
            includeRight ? right + padR + br : right,
            baselineY + metrics.FloatDescent + padB + bb);
        if (borderBox.Width <= 0 || borderBox.Height <= 0)
            return;
        var fillRect = new SKRect(
            includeLeft ? borderBox.Left + bl : borderBox.Left,
            borderBox.Top + bt,
            includeRight ? borderBox.Right - br : borderBox.Right,
            borderBox.Bottom - bb);

        if (hasColor)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = fillRect;
            op.FillColor = style.BackgroundColor.Value;
            op.Bounds = borderBox;
            var radii = InlineFragmentCornerRadii(style, borderBox, includeLeft, includeRight);
            if (radii != null)
                op.CornerRadii = radii;
            _displayList.Add(op);
        }
        if (hasImage)
            DrawBackgroundImage(owner, style, fillRect);

        if (hasBorder && sides != PhysicalBoxSides.None)
        {
            // The shared border painter squares off the corners facing an omitted
            // side, which is exactly what a sliced fragment needs.
            var borderPainter = new BoxBorderPainter(_displayList, borderBox, style, sides);
            borderPainter.Paint();
        }
        if (hasOutline)
        {
            // An outline is drawn around every fragment of the inline box, and unlike a
            // border it lives outside the border box, so a sliced break edge still gets
            // a closed ring on each line (CSS UI 3 §4).
            _outlinePainter.PaintOutline(borderBox, style, style.OutlineOffset);
        }
    }

    /// <summary>
    /// The fill (padding-box) corner radii of an inline fragment. A corner is only
    /// round where both of its sides are decorated, and it shrinks by the border it
    /// sits behind.
    /// </summary>
    private SKPoint[]? InlineFragmentCornerRadii(ComputedStyle style, SKRect borderBox,
        bool includeLeft, bool includeRight)
    {
        var radii = ResolveBackgroundCornerRadii(style, borderBox);
        if (radii == null)
            return null;

        static SKPoint Corner(SKPoint r, bool painted, float borderTop, float borderSide) =>
            painted ? new SKPoint(MathF.Max(0, r.X - borderSide), MathF.Max(0, r.Y - borderTop)) : SKPoint.Empty;

        var corners = new[]
        {
            Corner(radii[0], includeLeft, style.BorderTopWidth, style.BorderLeftWidth),
            Corner(radii[1], includeRight, style.BorderTopWidth, style.BorderRightWidth),
            Corner(radii[2], includeRight, style.BorderBottomWidth, style.BorderRightWidth),
            Corner(radii[3], includeLeft, style.BorderBottomWidth, style.BorderLeftWidth),
        };
        if (corners[0] == SKPoint.Empty && corners[1] == SKPoint.Empty &&
            corners[2] == SKPoint.Empty && corners[3] == SKPoint.Empty)
            return null;
        return corners;
    }

    private static float ResolveInlinePadding(Length? length, float fontSize)
    {
        if (length == null)
            return 0;
        float px = length.ToPixels(fontSize, fontSize, 0, 0);
        return float.IsNaN(px) || px < 0 ? 0 : px;
    }

    /// <summary>One continuation box of an inline element on a single line.</summary>
    private struct InlineFragment
    {
        public Element? Owner;
        public ComputedStyle? Style;
        public float Left;
        public float Right;
        public float BaselineY;
    }

    private readonly List<InlineFragment> _inlineFragments = new();

    // The elements the lines around the one being painted carry. box-decoration-break:
    // slice needs to know whether an element continues across a fragment's side, and a
    // fragment's background has to be emitted before that line's glyphs, so the next
    // line's owners are gathered with a one-line lookahead.
    private readonly List<Element> _inlineOwnersA = new();
    private readonly List<Element> _inlineOwnersB = new();
    private readonly List<Element> _inlineNextOwners = new();

    /// <summary>
    /// Grow the line's fragment list with one more run of <paramref name="owner"/>.
    /// A wrapped inline box is split into one box per line (CSS 2.1 §10.6.1), and
    /// its background/border belongs to that whole fragment — including the
    /// inter-word spaces — not to each text run inside it.
    /// </summary>
    private static void AppendInlineFragment(List<InlineFragment> fragments, Element? owner,
        ComputedStyle? style, float left, float right, float baselineY)
    {
        if (owner == null || style == null) return;
        for (int i = 0; i < fragments.Count; i++)
        {
            if (!ReferenceEquals(fragments[i].Owner, owner)) continue;
            if (MathF.Abs(fragments[i].BaselineY - baselineY) > 0.5f) continue;
            var f = fragments[i];
            f.Left = MathF.Min(f.Left, left);
            f.Right = MathF.Max(f.Right, right);
            fragments[i] = f;
            return;
        }
        fragments.Add(new InlineFragment { Owner = owner, Style = style, Left = left, Right = right, BaselineY = baselineY });
    }

    /// <summary>Paint every fragment box collected for the current line, then forget them.</summary>
    private void FlushInlineFragments(List<Element>? prevOwners = null, List<Element>? nextOwners = null)
    {
        foreach (var f in _inlineFragments)
        {
            // An element whose text carries on across one of this fragment's sides
            // faces a break there: box-decoration-break: slice drops the decoration
            // on those sides, clone keeps it on every fragment.
            var sides = PhysicalBoxSides.All;
            if (f.Style!.BoxDecorationBreak == BoxDecorationBreakType.Slice)
            {
                if (HasOwner(prevOwners, f.Owner)) sides &= ~PhysicalBoxSides.Left;
                if (HasOwner(nextOwners, f.Owner)) sides &= ~PhysicalBoxSides.Right;
            }
            PaintInlineRunDecorations(f.Owner, f.Style, f.Left, f.Right, f.BaselineY, sides);
        }
        _inlineFragments.Clear();
    }

    private static bool HasOwner(List<Element>? owners, Element? owner)
    {
        if (owners == null) return false;
        for (int i = 0; i < owners.Count; i++)
            if (ReferenceEquals(owners[i], owner)) return true;
        return false;
    }

    /// <summary>
    /// The elements a line carries that break their decoration by slicing. Only
    /// those matter for the break-edge rule, so the lists stay empty for ordinary text.
    /// </summary>
    private static void CollectInlineOwners(LineBox? line, List<Element> owners)
    {
        if (line == null) return;
        foreach (var run in line.Runs)
        {
            if (!run.IsText || run.Node is not TextNode runNode) continue;
            var owner = runNode.ParentElement;
            var ownerStyle = owner?.ComputedStyle;
            if (owner == null || ownerStyle == null) continue;
            if (ownerStyle.BoxDecorationBreak != BoxDecorationBreakType.Slice) continue;
            if (HasOwner(owners, owner)) continue;
            owners.Add(owner);
        }
    }

    private void DrawInlineRuns(LayoutBox box)
    {
        if (box.LineRuns == null && (box.Lines == null || box.Lines.Count == 0))
            return;

        float boxTop = box.ContentBox.Top + TotalOffsetY;

        // ::first-line was already applied to measurement by the line breaker, so
        // painting has to resolve the same merged style for the first line's runs.
        ComputedStyle? firstLineOverride = null;
        var boxOwner = box.Dimensions?.Element;
        if (boxOwner?.FirstLineStyles is { Count: > 0 } && boxOwner.ComputedStyle != null)
            firstLineOverride = Core.Css.PseudoStyleMerger.Merge(boxOwner.ComputedStyle, boxOwner.FirstLineStyles);

        if (box.Lines != null)
        {
            TextNode? lastTextNode = null;
            int runStartOffset = 0;
            int lineIndex = -1;
            // Only elements with box-decoration-break: slice look at their neighbours'
            // lines, so these owner lists stay empty for ordinary content.
            var prevOwners = _inlineOwnersA;
            var curOwners = _inlineOwnersB;
            prevOwners.Clear();
            curOwners.Clear();
            foreach (var line in box.Lines)
            {
                lineIndex++;
                ComputedStyle? lineOverride = lineIndex == 0 ? firstLineOverride : null;
                float lineY = line.Y + TotalOffsetY;
                float baseline = line.Baseline + TotalOffsetY;
                float lineOffsetX = line.TextAlignOffsetX;
                float currentX = line.X;
                bool slicedOnLine = false;

                // Pass 1: the inline boxes this line carries. Painting their
                // backgrounds before any of the line's glyphs keeps a parent's
                // box behind its child's text, and merges the runs of one
                // element into a single continuation box.
                foreach (var run in line.Runs)
                {
                    if (!run.IsText || run.Node is not TextNode runNode) continue;
                    var owner = runNode.ParentElement;
                    var ownerStyle = owner?.ComputedStyle;
                    if (owner == null || ownerStyle == null) continue;
                    if (ownerStyle.Visibility == VisibilityType.Hidden) continue;
                    float runLeft = run.X + lineOffsetX;
                    float runBaseline = run.Baseline > 0 ? run.Baseline + TotalOffsetY : baseline;
                    AppendInlineFragment(_inlineFragments, owner, ownerStyle, runLeft, runLeft + run.Width, runBaseline);
                    if (ownerStyle.BoxDecorationBreak == BoxDecorationBreakType.Slice && !HasOwner(curOwners, owner))
                    {
                        slicedOnLine = true;
                        curOwners.Add(owner);
                    }
                }
                // Whether the element carries on past this line decides the fragment's
                // right side, and that is only known after the next line — hence the
                // lookahead, needed because a background must precede its own glyphs.
                _inlineNextOwners.Clear();
                if (slicedOnLine && lineIndex + 1 < box.Lines.Count)
                    CollectInlineOwners(box.Lines[lineIndex + 1], _inlineNextOwners);
                FlushInlineFragments(prevOwners, _inlineNextOwners);
                (prevOwners, curOwners) = (curOwners, prevOwners);
                curOwners.Clear();

                foreach (var run in line.Runs)
                {
                    // The run carries its own resolved x (the converter computed it from
                    // the line box origin plus the run's inline offset), which is the only
                    // position that survives tabs, floated ::first-letter boxes and atomic
                    // inlines; accumulating widths from line.X dropped those gaps.
                    float runLeft = run.X + lineOffsetX;
                    if (run.IsText && run.Node is TextNode textNode)
                    {
                        if (textNode != lastTextNode)
                        {
                            runStartOffset = 0;
                            lastTextNode = textNode;
                        }

                        var parentStyle = textNode.ParentElement?.ComputedStyle;
                        if (parentStyle?.Visibility == VisibilityType.Hidden)
                        {
                            // Hidden text keeps its layout space but paints nothing.
                            currentX += run.Width;
                            continue;
                        }
                        var effectiveStyle = lineOverride ?? parentStyle;
                        var actualFontSize = run.FontSize ?? effectiveStyle?.FontSize ?? 16;
                        var runText = Acrux.Core.Layout.Inline.InlineItemsBuilder.ApplyTextTransform(run.Text, effectiveStyle?.TextTransform);
                        float runY = run.Baseline > 0 ? run.Baseline + TotalOffsetY : baseline;
                        var op = PaintOpPool.GetDrawTextOp();
                        op.Text = runText;
                        op.X = runLeft;
                        op.Y = runY;
                        // The run is already at its laid-out x: 'text-align' was applied
                        // to the line box (lineOffsetX above), so the op must not shift
                        // the glyphs again.
                        op.TextAlign = TextAlignType.Start;
                        op.Color = run.Color ?? effectiveStyle?.Color ?? SKColors.Black;
                        op.FontSize = actualFontSize;
                        op.FontFamily = run.FontFamily ?? effectiveStyle?.FontFamily ?? "Arial";
                        op.FontWeight = run.FontWeight;
                        SetDecorations(op, effectiveStyle, textNode.ParentElement);
                        op.LetterSpacing = effectiveStyle?.LetterSpacing ?? 0;
                        SetFontSlant(op, effectiveStyle);
                        if (effectiveStyle?.TextShadow != null && effectiveStyle.TextShadow.Count > 0)
                            op.TextShadows = effectiveStyle.TextShadow;
                        op.EmphasisMark = GetTextEmphasisMarkString(parentStyle);
                        if (!string.IsNullOrEmpty(op.EmphasisMark))
                        {
                            op.EmphasisOver = parentStyle == null || parentStyle.TextEmphasisPosition.Contains("over", StringComparison.OrdinalIgnoreCase);
                            var emphasisColorStr = parentStyle?.TextEmphasisColor;
                            if (string.IsNullOrEmpty(emphasisColorStr) || emphasisColorStr.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                                op.EmphasisColor = op.Color;
                            else
                                op.EmphasisColor = ColorParser.Parse(emphasisColorStr);
                        }
                        op.Bounds = new SKRect(runLeft, lineY, runLeft + run.Width, lineY + line.Height);

                        // Add selection highlight clipped to the overlapping region
                        _highlightPainter.PaintHighlight(textNode, runText, op.Bounds,
                            op.FontSize, op.FontFamily, op.FontWeight, runStartOffset);

                        _displayList.Add(op);
                        runStartOffset += runText.Length;
                    }
                    else
                    {
                        lastTextNode = null;
                        runStartOffset = 0;
                    }
                    currentX += run.Width;
                }
            }
        }

        else if (box.LineRuns != null)
        {
            TextNode? lastTextNode = null;
            int runStartOffset = 0;
            float x = box.ContentBox.Left;
            float fontSize = box.LineRuns.FirstOrDefault()?.FontSize ?? 16;
            float baseline = boxTop + Core.Fonts.LineBoxMetrics.GetBaselineForLineHeight(box.Dimensions?.Style, box.LineHeight);
            foreach (var run in box.LineRuns)
            {
                if (run.IsText && run.Node is TextNode runNode)
                {
                    var owner = runNode.ParentElement;
                    var ownerStyle = owner?.ComputedStyle;
                    float runBaseline = run.Baseline > 0 ? run.Baseline + TotalOffsetY : baseline;
                    if (owner != null && ownerStyle != null && ownerStyle.Visibility != VisibilityType.Hidden)
                        AppendInlineFragment(_inlineFragments, owner, ownerStyle, x, x + run.Width, runBaseline);
                }
                // Keep pace with the painting pass: every run occupies its advance,
                // hidden and atomic ones too.
                x += run.Width;
            }
            FlushInlineFragments();

            foreach (var run in box.LineRuns)
            {
                if (run.IsText && run.Node is TextNode textNode)
                {
                    if (textNode != lastTextNode)
                    {
                        runStartOffset = 0;
                        lastTextNode = textNode;
                    }

                    var parentStyle = textNode.ParentElement?.ComputedStyle;
                    if (parentStyle?.Visibility == VisibilityType.Hidden)
                    {
                        // Hidden text keeps its layout space but paints nothing.
                        x += run.Width;
                        continue;
                    }
                    var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;
                    var runText = Acrux.Core.Layout.Inline.InlineItemsBuilder.ApplyTextTransform(run.Text, parentStyle?.TextTransform);
                    float runY = run.Baseline > 0 ? run.Baseline + TotalOffsetY : baseline;
                    var op = PaintOpPool.GetDrawTextOp();
                    op.Text = runText;
                    op.X = run.X;
                    op.Y = runY;
                    op.Color = run.Color ?? parentStyle?.Color ?? SKColors.Black;
                    op.FontSize = actualFontSize;
                    op.FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial";
                    op.FontWeight = run.FontWeight;
                    SetDecorations(op, parentStyle, textNode.ParentElement);
                    op.LetterSpacing = parentStyle?.LetterSpacing ?? 0;
                    SetFontSlant(op, parentStyle);
                    if (parentStyle?.TextShadow != null && parentStyle.TextShadow.Count > 0)
                        op.TextShadows = parentStyle.TextShadow;
                    op.EmphasisMark = GetTextEmphasisMarkString(parentStyle);
                    if (!string.IsNullOrEmpty(op.EmphasisMark))
                    {
                        op.EmphasisOver = parentStyle == null || parentStyle.TextEmphasisPosition.Contains("over", StringComparison.OrdinalIgnoreCase);
                        var emphasisColorStr = parentStyle?.TextEmphasisColor;
                        if (string.IsNullOrEmpty(emphasisColorStr) || emphasisColorStr.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                            op.EmphasisColor = op.Color;
                        else
                            op.EmphasisColor = ColorParser.Parse(emphasisColorStr);
                    }
                    op.Bounds = new SKRect(x, boxTop, x + run.Width, boxTop + run.Height);

                    // Add selection highlight clipped to the overlapping region
                    _highlightPainter.PaintHighlight(textNode, runText, op.Bounds,
                        op.FontSize, op.FontFamily, op.FontWeight, runStartOffset);

                    _displayList.Add(op);
                    runStartOffset += runText.Length;
                }
                else
                {
                    lastTextNode = null;
                    runStartOffset = 0;
                }
                x += run.Width;
            }
        }
    }
}

public class ImageCache
{
    private readonly Dictionary<string, SKImage> _cache = new();
    private readonly Dictionary<string, Task<SKImage?>> _pendingLoads = new();
    private readonly LinkedList<string> _accessOrder = new();
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();
    private const int MaxCacheSize = 200;

    // Performance-integrated storage layers:
    //  - DecodedImagePool: byte-budgeted LRU of decoded SKImages (the hot path)
    //  - ResourceCache:    byte-budgeted LRU of raw HTTP bodies (re-decode source)
    //  - StreamingHttpFetcher: priority-aware HTTP client with inflight dedup
    private readonly DecodedImagePool _decodedPool = new();
    private readonly ResourceCache _resourceCache = new();
    private readonly StreamingHttpFetcher _fetcher;

    public DecodedImagePool DecodedPool => _decodedPool;
    public ResourceCache ResourceCache => _resourceCache;
    public StreamingHttpFetcher Fetcher => _fetcher;

    public ImageCache()
    {
        _decodedPool.SetCapacity(64L * 1024 * 1024); // 64 MB decoded budget
        _resourceCache.SetCapacity(32L * 1024 * 1024); // 32 MB raw body budget
        _fetcher = new StreamingHttpFetcher(_httpClient, _resourceCache, new PriorityResourceQueue());
    }

    public async Task<SKImage?> GetImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Hot path: decoded pool hit
        var pooled = _decodedPool.Get(url);
        if (pooled != null)
        {
            PipelineTimings.ImageCacheHits.AddSample(1);
            return pooled;
        }

        // Fallback: legacy dictionary cache hit (kept for stability)
        SKImage? cachedImage = null;
        Task<SKImage?>? pendingTask = null;

        lock (_lock)
        {
            if (_cache.TryGetValue(url, out cachedImage))
            {
                _accessOrder.Remove(url);
                _accessOrder.AddFirst(url);
            }
            else if (!_pendingLoads.TryGetValue(url, out pendingTask))
            {
                pendingTask = LoadImageAsync(url);
                _pendingLoads[url] = pendingTask;
            }
        }

        if (cachedImage != null)
        {
            // Promote into the byte-budgeted pool
            _decodedPool.Put(url, cachedImage, cachedImage.Width, cachedImage.Height);
            return cachedImage;
        }
        if (pendingTask == null) return null;

        try
        {
            var image = await pendingTask;
            lock (_lock)
            {
                _pendingLoads.Remove(url);
                if (image != null)
                {
                    _cache[url] = image;
                    _accessOrder.AddFirst(url);
                    EvictIfNeeded();

                    // Promote into the byte-budgeted pool with actual decoded size
                    _decodedPool.Put(url, image, image.Width, image.Height);
                    PipelineTimings.ImagesDecoded.AddSample(1);
                }
            }
            return image;
        }
        catch
        {
            lock (_lock) _pendingLoads.Remove(url);
            return null;
        }
    }

    private async Task<SKImage?> LoadImageAsync(string url)
    {
        var sw = Clock.NowNanos();
        try
        {
            if (url.StartsWith("data:"))
            {
                var comma = url.IndexOf(',');
                if (comma < 0) return null;
                var meta = url[5..comma];
                var payload = url[(comma + 1)..];
                if (!meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    return null;
                var bytes = Convert.FromBase64String(payload);
                PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                return SKImage.FromEncodedData(bytes);
            }
            if (url.StartsWith("file://"))
                url = new Uri(url).LocalPath;
            if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                // Fast path: check the resource cache for the raw body first
                ResourceResponse? resp = null;
                if (_resourceCache.TryGet(url, out resp) && resp != null)
                {
                    PipelineTimings.ResourceCacheHits.AddSample(1);
                    PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                    return SKImage.FromEncodedData(resp.Body);
                }

                // Slow path: go through the streaming fetcher (which also caches)
                var request = new ResourceRequest
                {
                    Url = url,
                    Kind = ResourceKind.Image,
                    Priority = ResourcePriority.Medium,
                };
                var fetched = await _fetcher.FetchAsync(request);
                if (fetched.Body != null && fetched.Body.Length > 0)
                {
                    PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                    return SKImage.FromEncodedData(fetched.Body);
                }
                return null;
            }
            else
            {
                var ext = Path.GetExtension(url).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp" || ext == ".bmp" || ext == ".ico")
                {
                    var data = await File.ReadAllBytesAsync(url);
                    PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                    return SKImage.FromEncodedData(data);
                }
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private void EvictIfNeeded()
    {
        while (_cache.Count > MaxCacheSize)
        {
            var last = _accessOrder.Last;
            if (last == null) break;
            if (_cache.TryGetValue(last.Value, out var oldImage))
                oldImage?.Dispose();
            _cache.Remove(last.Value);
            _accessOrder.RemoveLast();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var img in _cache.Values)
                img?.Dispose();
            _cache.Clear();
            _accessOrder.Clear();
            _pendingLoads.Clear();
        }
        _decodedPool.Clear();
        _resourceCache.Clear();
    }

}
