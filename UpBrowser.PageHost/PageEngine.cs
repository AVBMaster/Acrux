using SkiaSharp;
using System.Diagnostics;
using UpBrowser.Core;
using UpBrowser.Core.Css;
using UpBrowser.Core.Dom;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance.Rendering;
using UpBrowser.Core.Performance.Resources;
using UpBrowser.Rendering;

namespace UpBrowser.PageHost;

/// <summary>
/// The page pipeline: document ownership plus parse → style → layout → paint, the JS
/// engine that mutates it, hit-tested input against it, and navigation. It knows
/// nothing about who hosts it — no transport, no window, no pixels. The host asks for
/// work once per loop pass through <see cref="UpdatePipeline"/> and reads the result
/// (<see cref="DisplayList"/>, scroll and content extents) to present it however it
/// likes; anything the engine cannot do for itself goes through <see cref="IPageEngineSink"/>.
/// All state is single-threaded: one host thread drives every method except the
/// document fetch, which is answered back through the sink.
/// </summary>
public sealed class PageEngine : IDisposable
{
    private readonly int _id;
    private readonly float _dpi, _res;
    private readonly IPageEngineSink _sink;

    // Pipeline state
    private readonly DocumentManager _docManager = new();
    private readonly JavaScriptEngine _js;
    private readonly LayoutEngine _layoutEngine = new();
    private IncrementalLayoutEngine? _incremental;
    private readonly LayoutCache _layoutCache = new();
    private readonly Dictionary<string, SKTypeface> _typefaceCache = new();
    private readonly ImageCache _imageCache = new();
    private StyleComputer? _styleComputer;

    /// <summary>
    /// CSS animations and transitions for this page. Sampled once per pipeline
    /// pass, between style resolution and layout, exactly as the in-process shell
    /// does, so a tab renders the same frame here as it does there.
    /// </summary>
    private readonly UpBrowser.Core.Dom.Animations.CssAnimationEngine _animations = new();
    /// <summary>True while the last sample left a live effect behind.</summary>
    private bool _animationsRunning;
    // Whether the last animation sample changed something layout consumes, as opposed
    // to something only paint consumes. Decides the dirt level of the next stage.
    private bool _animNeedsLayout;
    // Whether the last animation sample changed a painted value, and which boxes it
    // touched — an animation entirely outside the visible area still needs its display
    // list refreshed (the list is scroll-invariant) but must not cost a viewport raster.
    private bool _animNeedsRepaint;
    private IReadOnlyList<Element>? _animTargets;
    // True when this tick's animated boxes are all outside the rasterized area: the
    // sample still advances (effects must complete, fill states and events still fire)
    // but neither the paint walk nor the viewport raster can show it, so both are
    // skipped and the sampling drops to a slow keep-alive rate until the boxes scroll
    // back in.
    private bool _animOffscreen;
    // A shadow, outline or blur paints outside the box by an amount the geometry does
    // not carry, so the estimate below has nothing to test against.
    private bool _animBleeds;
    // Set by the sample that observed the last effect retire, which is what asks the
    // pipeline for its extra style round (see RunPipeline).
    private bool _animEffectsEnded;
    /// <summary>Resolves replaced-element intrinsic sizes against this document's own image
    /// cache and base URL. Installed for the duration of every pipeline pass.</summary>
    private Func<string?, UpBrowser.Core.Layout.Geometry.PhysicalSize?>? _replacedResolver;
    private bool _paintVisible = true;
    /// <summary>Per-pass animation diagnostics (UPBROWSER_ANIM_LIVE=1).</summary>
    private readonly bool _animTrace = Environment.GetEnvironmentVariable("UPBROWSER_ANIM_LIVE") == "1";

    private Document? _document;
    private string _baseUrl = "";
    private string _title = "";
    private float _viewportW = 1024, _viewportH = 768;
    private float _scrollX, _scrollY;
    private float _contentW, _contentH;
    // Three-level dirty chain: layout ⇒ dl ⇒ raster; scroll/hover-free changes take cheaper paths.
    // Style is tracked separately from layout because an animation that only moves paint
    // properties still needs the cascade re-resolved (the engine compares cascaded values
    // to find transition start conditions) but does not need layout re-run.
    private bool _styleDirty = true;
    private bool _layoutDirty = true;
    private bool _dlDirty = true;
    private DisplayList? _cachedDl;
    private SKColor _cachedBg = SKColors.White;
    private long _dlSerial;
    private int _navSeq;
    private bool _active = true;

    // DOM metrics feed tooltips/task manager only — recompute at 1s,
    // not on every animation-frame layout.
    private int _cachedDomCount, _cachedBoxCount;
    private long _lastCountTick;

    // Idle diagnostics counters (the host prints them; reading them is free).
    private long _stRelayout, _stLayout, _stDl;
    // Diagnostics: how many animation samples were parked as off-screen and how many
    // were taken as visible (see AnimDamageVisible).
    private long _stAnimPark, _stAnimVisible;
    private long _stStylePass;
    private long _stSampleMs, _stSampleN;
    private List<UpBrowser.Core.Css.Rules.StyleRuleKeyframes>? _keyframeRules;
    private double _stStyleMs, _stAnimMs, _stDlMs;

    private bool _docHasHoverRules;
    private Document? _hoverRulesDoc;
    private Element? _hoveredElement;
    private float _mouseVX = -1, _mouseVY = -1; // last known pointer in viewport coords
    // Hover refresh deferred while the viewport scrolls; settled by the host loop.
    private bool _hoverScrollPending;
    private long _lastScrollMoveTick;

    // Page-text selection (node+offset caret points, the shell's character-precision model):
    // the engine owns selection like it owns hover/focus/caret, so an out-of-process tab
    // selects exactly like an in-process one and the host only reads the result.
    private struct SelPoint
    {
        public TextNode? Node;
        public int Offset;
    }
    private bool _isSelecting;
    private bool _hasSelection;
    private SelPoint _selAnchor;
    private SelPoint _selFocus;
    // Word/line click granularity: the transport carries only pointer x/y, so the engine
    // times successive presses itself instead of relying on a window-manager click count.
    private int _clickCount;
    private long _lastDownTick;
    private float _lastDownX, _lastDownY;
    private const int DoubleClickMs = 500;
    private const float ClickJitterPx = 4f;
    private const float HitToleranceY = 8f;      // hit area tolerance above/below line
    private const float HitToleranceX = 4f;      // hit area tolerance left/right of run

    // Form editing state (the engine owns focus/caret like an in-process tab does)
    private Element? _focused;
    private int _caret;
    private int _selStart = -1;
    private bool _showCursor = true;
    private long _blinkTick = Environment.TickCount64;
    private Element? _pressedButton;
    /// <summary>The open <c>&lt;select&gt;</c> and its list. Options are not laid out as boxes
    /// of their own, so the dropdown is drawn from rects the engine computes and hands to the
    /// paint walk — in document space, like every other box.</summary>
    private Element? _activeSelect;
    private SKRect _dropdownRect;
    private readonly List<(Element Option, SKRect Rect)> _optionRects = new();
    private int _hoverOption = -1;

    // IME composition state (CJK): the engine owns it exactly like the shell's
    // in-process fields — an out-of-process tab composes through these, never
    // through shell-side copies.
    private bool _imeComposing;
    private string _imeComposition = "";
    private int _imeCursorPos;

    /// <param name="id">Engine identity, forwarded to the JS engine (logging and, in
    /// multi-engine builds, its host process slot).</param>
    /// <param name="dpiScale">System DPI scale.</param>
    /// <param name="resolutionScale">Additional resolution scale of the surface the host presents into.</param>
    public PageEngine(int id, float dpiScale, float resolutionScale, IPageEngineSink sink)
    {
        _id = id;
        _dpi = dpiScale;
        _res = resolutionScale;
        _sink = sink;
        // Without the Skia measurer the layout engine falls back to per-character
        // width guesses, which silently lays out differently from an in-process tab.
        PageEnvironment.Initialize();
        _js = new JavaScriptEngine(id);
        _js.ShowDialog = (msg, type) => _sink.RequestDialog(msg ?? "", type ?? "");
        _animations.EventSink = DispatchAnimationEvent;

        // Page-compat: window metrics + JS-driven scrolling live with the document.
        if (_js.Builtins != null)
        {
            var b = _js.Builtins;
            b.GetInnerWidth = () => (int)_viewportW;
            b.GetInnerHeight = () => (int)_viewportH;
            b.GetDevicePixelRatio = () => _dpi * _res;
            b.GetScrollX = () => (int)_scrollX;
            b.GetScrollY = () => (int)_scrollY;
            b.OnScrollTo = (x, y) => SetScroll(x, y);
            b.OnScrollBy = (x, y) => SetScroll(_scrollX + x, _scrollY + y);
        }

        if (_js.LocationHost != null)
        {
            _js.LocationHost.OnNavigate = url =>
            {
                if (!string.IsNullOrWhiteSpace(url))
                    _sink.RequestNavigate(url);
            };
            _js.LocationHost.OnReload = () => _sink.RequestNavigate(_baseUrl);
        }
    }

    // ==================== what the host may read ====================

    public Document? Document => _document;

    /// <summary>Display list built by the last <see cref="UpdatePipeline"/> pass, or null before the first.</summary>
    public DisplayList? DisplayList => _cachedDl;

    /// <summary>Page background colour the presenter clears with.</summary>
    public SKColor ViewBackgroundColor => _cachedBg;

    /// <summary>Bumped every time the display list is rebuilt, so a presenter can tell
    /// a scroll-only frame from a content change.</summary>
    public long DisplayListSerial => _dlSerial;

    public float ViewportWidth => _viewportW;
    public float ViewportHeight => _viewportH;
    public float ScrollX => _scrollX;
    public float ScrollY => _scrollY;
    public float ContentWidth => _contentW;
    public float ContentHeight => _contentH;

    public string BaseUrl => _baseUrl;
    public string Title => _title;

    public int DomCount => _cachedDomCount;
    public int BoxCount => _cachedBoxCount;

    /// <summary>False for a page the host is not showing: layout and display list stay
    /// warm, but the presenter is expected to skip rasterisation.</summary>
    public bool IsActive => _active;

    /// <summary>True while page text is selected — the host needs this to decide whether
    /// a copy/context action has anything to act on.</summary>
    public bool HasTextSelection => _hasSelection;

    /// <summary>True while a select's list is open: the host paints no scrollbar affordance
    /// for it, but it needs to know so a click is not read as a page click.</summary>
    public bool HasOpenSelect => _activeSelect != null;

    /// <summary>Plain text of the current page selection in DOM order; empty when none.
    /// Reads live state, so the host must call it on the engine-loop thread.</summary>
    public string SelectedText
    {
        get
        {
            if (!_hasSelection || _selAnchor.Node == null || _selFocus.Node == null) return "";
            var sb = new System.Text.StringBuilder();
            CollectSelectedTextRange(_selAnchor.Node, _selAnchor.Offset, _selFocus.Node, _selFocus.Offset, sb);
            return sb.ToString();
        }
    }

    /// <summary>True when <see cref="UpdatePipeline"/> has work queued.</summary>
    public bool PipelineWanted => _styleDirty || _layoutDirty || _dlDirty || _animationsRunning;

    /// <summary>
    /// Nominal interval between animation frames. The host sleeps until the next
    /// frame boundary rather than polling on its idle cadence, which is what keeps
    /// a running animation at the display's rate instead of at the loop's poll rate.
    /// </summary>
    public double AnimationFrameIntervalMs { get; set; } = 1000.0 / 60.0;

    /// <summary>Timeline time the last animation sample was taken at.</summary>
    private double _lastSampleTimelineMs;

    /// <summary>
    /// How long the host may sleep before calling <see cref="UpdatePipeline"/>
    /// again: the sooner of the next JS timer and the next animation frame.
    ///
    /// This is the difference between an animation that runs at the display rate
    /// and one that runs at whatever the host's idle poll happens to be. An idle
    /// page with no timers and no effects waits the full idle interval; a page
    /// with a live effect waits only until its next frame boundary.
    /// </summary>
    public int NextWorkDelayMs
    {
        get
        {
            int due = NextTimerDelayMs;
            int waitMs = due == int.MaxValue ? IdleWaitMs : Math.Clamp(due, 1, IdleWaitMs);

            if (!_animationsRunning) return waitMs;

            // An animation nothing can see still has to be sampled — effects have to
            // reach their end state, fill modes have to apply, animation events have to
            // fire — but it does not have to be sampled at the display rate. A quarter
            // of a second keeps all of that correct and costs a fraction of the CPU.
            if (_animOffscreen)
                return due == int.MaxValue ? OffscreenAnimSampleMs : Math.Min(due, OffscreenAnimSampleMs);

            double next = _lastSampleTimelineMs + AnimationFrameIntervalMs;
            double ahead = next - _animations.Timeline.CurrentTimeMs;
            int animWait = (int)Math.Ceiling(ahead);
            if (animWait < 1) animWait = 1;
            return animWait < waitMs ? animWait : waitMs;
        }
    }

    /// <summary>Sampling interval for an animation parked as off-screen.</summary>
    private const int OffscreenAnimSampleMs = 250;

    /// <summary>Idle poll interval when nothing is scheduled (see the host loop).</summary>
    private const int IdleWaitMs = 60;

    /// <summary>Milliseconds until the next JS timer is due, or <see cref="int.MaxValue"/>.</summary>
    public int NextTimerDelayMs => _js.IntegrationService?.NextTimerDelayMs() ?? int.MaxValue;

    // Diagnostics snapshot (see the heartbeat in the shell's tab host).
    public long RelayoutRequestCount => _stRelayout;
    public long LayoutPassCount => _stLayout;
    public long DisplayListCount => _stDl;
    public long AnimParkCount => _stAnimPark;
    public long AnimPaintCount => _stAnimVisible;
    public long AnimSampleMs => _stSampleMs;
    public long AnimSampleCount => _stSampleN;
    public long StylePassCount => _stStylePass;
    public bool AnimationsRunning => _animationsRunning;
    public bool AnimationsParked => _animOffscreen;

    // ==================== lifecycle ====================

    public void SetViewport(float w, float h)
    {
        if (w > 50 && h > 50 && (Math.Abs(w - _viewportW) > 0.5f || Math.Abs(h - _viewportH) > 0.5f))
        {
            _viewportW = w; _viewportH = h;
            MarkLayout();
        }
    }

    public void SetActive(bool active)
    {
        if (active == _active) return;
        _active = active;
        // Hidden: clamp the timer cadence, the host keeps the display lists warm.
        if (!_active) _js.IntegrationService?.ThrottleInactiveTabs();
    }

    public void Navigate(string url)
    {
        // Internal browser pages are owned by the host: report the URL and
        // wait for it to push the page content back via LoadHtml.
        if (url.StartsWith("upbrowser://", StringComparison.OrdinalIgnoreCase))
        {
            _sink.ReportUrl(url);
            return;
        }

        int seq = ++_navSeq;
        _sink.ReportLoading(true);
        Task.Run(() =>
        {
            string html;
            try
            {
                html = FetchHtml(url);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PageEngine {_id}] fetch {url}: {ex.Message}");
                html = ErrorHtml(url, ex.Message);
            }
            if (seq != _navSeq) return; // superseded
            // Engine/DOM work must land back on the host main loop thread.
            _sink.RequestLoadHtml(html, url);
        });
    }

    private static string FetchHtml(string url)
    {
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = new Uri(url).LocalPath;
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var fetcher = new StreamingHttpFetcher();
            var resp = fetcher.FetchAsync(new ResourceRequest
            {
                Url = url,
                Kind = ResourceKind.Document,
                Priority = ResourcePriority.High,
                Timeout = TimeSpan.FromSeconds(15),
            }).GetAwaiter().GetResult();
            return System.Text.Encoding.UTF8.GetString(resp.Body);
        }
        return DocumentManager.DefaultHtml;
    }

    private static string ErrorHtml(string url, string error) =>
        $"<!DOCTYPE html><html><head><title>Error</title></head><body style='font-family:Arial;padding:40px'>" +
        $"<h1 style='color:#d32f2f'>Unable to connect</h1><p>{System.Net.WebUtility.HtmlEncode(url)}</p>" +
        $"<p style='color:#666'>{System.Net.WebUtility.HtmlEncode(error)}</p></body></html>";

    public void LoadHtml(string html, string baseUrl)
    {
        try
        {
            _imageCache.Clear();
            _typefaceCache.Clear();
            PaintOpPool.Clear(); // recycle the previous page's pooled ops
            // This document's images decode through this engine's cache, not whichever
            // engine installed last into the shared seam (see RunPipeline's scope).
            _replacedResolver = UpBrowser.Rendering.PaintVisitor.BuildReplacedIntrinsicResolver(_imageCache, baseUrl);
            UpBrowser.Core.Layout.ReplacedIntrinsicSizes.Resolver = _replacedResolver;

            var load = _docManager.LoadHtmlAsync(html, baseUrl, _viewportW, _viewportH, _dpi)
                .GetAwaiter().GetResult();

            _document = load.Document;
            _baseUrl = baseUrl;
            _styleComputer = load.StyleComputer;
            _incremental = null; // fresh node tree — drop the layout cache
            _layoutCache.Clear();
            // A new document gets a new document timeline, so this page's delays
            // and negative delays are measured from this navigation.
            _animations.Reset();
            _animationsRunning = false;
            _scrollX = _scrollY = 0;
            _hoveredElement = null;
            _hoverRulesDoc = null;
            _focused = null;
            _pressedButton = null;
            _activeSelect = null;
            _optionRects.Clear();
            _hoverOption = -1;
            _dropdownRect = default;
            _selStart = -1;
            ClearImeComposition();
            // A new document invalidates the old one's text nodes: drop the selection
            // and any in-flight drag with it (the shell clears the same way on load).
            _hasSelection = false;
            _isSelecting = false;
            _selAnchor = default;
            _selFocus = default;
            _clickCount = 0;

            _js.LoadDocument(load.Document);
            RunPageScripts(load.Document, baseUrl);
            _js.IntegrationService?.FireDOMContentLoaded();

            MarkLayout();
            _sink.InvalidateFrameBaseline();
            _title = load.Document.Title ?? "";
            _sink.ReportTitle(_title);
            _sink.ReportUrl(baseUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PageEngine {_id}] load: {ex.Message}");
        }
        finally
        {
            _sink.ReportLoading(false);
        }
    }

    private void RunPageScripts(Document doc, string baseUrl)
    {
        var scripts = new List<Element>();
        var queue = new Queue<Element>();
        if (doc.DocumentElement != null) queue.Enqueue(doc.DocumentElement);
        while (queue.Count > 0)
        {
            var el = queue.Dequeue();
            if (string.Equals(el.TagName, "SCRIPT", StringComparison.OrdinalIgnoreCase)) scripts.Add(el);
            foreach (var c in el.Children)
                if (c is Element ce) queue.Enqueue(ce);
        }

        foreach (var s in scripts)
        {
            var src = s.GetAttribute("src");
            try
            {
                if (!string.IsNullOrEmpty(src))
                {
                    var url = ResolveUrl(src, baseUrl);
                    if (url == null) continue;
                    var code = FetchHtml(url);
                    _js.Execute(code, url);
                }
                else
                {
                    var code = s.TextContent;
                    if (!string.IsNullOrWhiteSpace(code))
                        _js.Execute(code);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PageEngine {_id}] script: {ex.Message}");
            }
        }
    }

    private static string? ResolveUrl(string url, string baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("file://") || url.StartsWith("data:"))
            return url;
        if (string.IsNullOrEmpty(baseUrl)) return null;
        try { return new Uri(new Uri(baseUrl), url).AbsoluteUri; }
        catch { return null; }
    }

    // ==================== input ====================

    /// <summary>
    /// Dispatch through the identity-mapped ElementHost — addEventListener
    /// listeners live on the same wrapper instance JS originally received
    /// (Core's DispatchEvent resolves it; GetDispatchHost builds custom events).
    /// </summary>
    private void DispatchDomEvent(Element? el, Func<ElementHost, ScriptEvent> make)
    {
        if (el == null) return;
        try { _js.DispatchEvent(el, make(_js.GetDispatchHost(el))); }
        catch { }
    }

    private void DispatchSimple(Element? el, string type)
    {
        try { DispatchDomEvent(el, h => new ScriptEvent(type, h)); } catch { }
    }

    /// <summary>
    /// Deliver an animation or transition event to script. Wired once at
    /// construction so a page's own <c>animationstart</c> listeners fire in this
    /// host exactly as they do in the in-process shell.
    /// </summary>
    private void DispatchAnimationEvent(Element element, UpBrowser.Core.Dom.Event evt)
    {
        try
        {
            if (evt is UpBrowser.Core.Dom.AnimationEvent anim)
            {
                DispatchDomEvent(element, h => new ScriptEvent(anim.Type, h)
                {
                    bubbles = false,
                    cancelable = false,
                    animationName = anim.AnimationName,
                    elapsedTime = anim.ElapsedTime,
                    pseudoElement = anim.PseudoElement ?? "",
                    currentTime = anim.CurrentTime,
                });
            }
            else if (evt is UpBrowser.Core.Dom.TransitionEvent trans)
            {
                DispatchDomEvent(element, h => new ScriptEvent(trans.Type, h)
                {
                    bubbles = false,
                    cancelable = false,
                    propertyName = trans.PropertyName,
                    elapsedTime = trans.ElapsedTime,
                    pseudoElement = trans.PseudoElement ?? "",
                    currentTime = trans.CurrentTime,
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PageEngine {_id}] anim event: {ex.Message}");
        }
    }

    private ElementHost WrapHost(Element el) => _js.GetDispatchHost(el);

    public void HandlePointerDown(float x, float y) => HandleClick(x, y, true);
    public void HandlePointerUp(float x, float y) => HandleClick(x, y, false);
    public void HandlePointerMove(float x, float y) => HandleMove(x, y);

    private void HandleClick(float x, float y, bool down)
    {
        if (_document == null) return;
        var el = PageHitTest.HitTest(_document, x + _scrollX, y + _scrollY);

        try { DispatchSimple(el, down ? "mousedown" : "mouseup"); } catch { }

        var itype = el?.InputType?.ToLowerInvariant();
        bool isControl = itype == "checkbox" || itype == "radio" || el?.TagName == "BUTTON";

        if (down)
        {
            // An open dropdown owns the next click: it picks an option or closes itself.
            if (_activeSelect != null && HandleDropdownClick(x + _scrollX, y + _scrollY)) return;

            try { DispatchSimple(el, "click"); } catch { }

            if (el != null && el.IsTextEditable)
            {
                SetFocus(el);
                _caret = (el.Value ?? "").Length;
                _selStart = -1;
            }
            else if (el != null && !isControl)
            {
                SetFocus(null);
            }

            // <summary> toggles the <details> it belongs to. Layout and paint already hide a
            // closed one; this is the whole of the interaction, and it changes the content's
            // height, so it is a layout change rather than a repaint.
            for (var a = el; a != null; a = a.ParentElement)
            {
                if (!string.Equals(a.TagName, "SUMMARY", StringComparison.OrdinalIgnoreCase)) continue;
                for (var d = a.ParentElement; d != null; d = d.ParentElement)
                {
                    if (!string.Equals(d.TagName, "DETAILS", StringComparison.OrdinalIgnoreCase)) continue;
                    if (d.HasAttribute("open")) d.RemoveAttribute("open");
                    else d.SetAttribute("open", "");
                    try { DispatchSimple(d, "toggle"); } catch { }
                    MarkLayout();
                    break;
                }
                break;
            }

            // A <select> toggles its dropdown ahead of the generic form handling — but a row
            // the page actually placed is picked where it is drawn: this engine still lays a
            // single select's options out as rows (its width is the widest one of them), so
            // those rows are where the pointer lands. Not page text, but still clickable.
            var option = OptionAncestor(el);
            if (option?.LayoutBox != null)
            {
                var owner = option.ParentElement;
                if (owner != null && !owner.HasAttribute("disabled"))
                {
                    ChooseOption(owner, option);
                    return;
                }
            }

            var select = SelectAncestor(el);
            if (select != null && !select.HasAttribute("disabled"))
            {
                if (ReferenceEquals(_activeSelect, select)) CloseDropdown();
                else { SetFocus(select); OpenDropdown(select); }
                return;
            }

            if (el != null && (itype == "checkbox" || itype == "radio"))
            {
                SetFocus(el);
                if (itype == "checkbox")
                {
                    if (el.HasAttribute("checked")) el.RemoveAttribute("checked");
                    else el.SetAttribute("checked", "");
                    try { DispatchSimple(el, "change"); } catch { }
                }
                else if (!el.HasAttribute("checked"))
                {
                    UncheckRadioGroup(el);
                    el.SetAttribute("checked", "");
                    try { DispatchSimple(el, "change"); } catch { }
                }
            }

            if (el?.TagName == "BUTTON")
            {
                _pressedButton = el;
                SetFocus(el);
            }

            // Link navigation
            for (var a = el; a != null; a = a.ParentElement)
            {
                if (string.Equals(a.TagName, "A", StringComparison.OrdinalIgnoreCase))
                {
                    var href = a.GetAttribute("href");
                    var target = ResolveUrl(href, _baseUrl);
                    if (!string.IsNullOrEmpty(target))
                        Navigate(target);
                    break;
                }
            }

            // Selection start, mirroring the shell rule: every press drops the old
            // selection; only a non-interactive target begins a new drag.
            if (el == null)
            {
                _hasSelection = false;
            }
            else if (!IsInteractiveElement(el))
            {
                StartSelection(x, y);
            }
            else
            {
                _isSelecting = false;
                _hasSelection = false;
            }
        }
        else
        {
            // Selection finish on release: the drag is over, a settled selection repaints
            // once. A plain click never grew one, so it simply leaves nothing selected.
            if (_isSelecting)
            {
                _isSelecting = false;
                if (_hasSelection) MarkPaint();
            }

            if (_pressedButton != null)
            {
                var btn = _pressedButton;
                _pressedButton = null;
                var btype = (btn.GetAttribute("type") ?? "submit").ToLowerInvariant();
                var form = FindForm(btn) as UpBrowser.Core.Dom.Html.HTMLFormElement;
                if (form != null)
                {
                    if (btype == "submit") form.Submit();
                    else if (btype == "reset") form.Reset();
                }
            }
        }
        MarkPaint();
    }

    /// <summary>
    /// Begin a page-text selection at the press point: anchor collapses onto the caret
    /// position under the pointer (drag extends it), with word granularity on the second
    /// click in the burst and whole-node (single-line) granularity on the third.
    /// </summary>
    private void StartSelection(float x, float y)
    {
        long now = Environment.TickCount64;
        bool repeat = now - _lastDownTick <= DoubleClickMs &&
                      Math.Abs(x - _lastDownX) <= ClickJitterPx &&
                      Math.Abs(y - _lastDownY) <= ClickJitterPx;
        _clickCount = repeat ? Math.Min(_clickCount + 1, 3) : 1;
        _lastDownTick = now;
        _lastDownX = x; _lastDownY = y;

        var pt = HitTestTextPosition(x + _scrollX, y + _scrollY);
        _isSelecting = true;
        _hasSelection = false;

        if (_clickCount >= 2 && pt.Node != null)
        {
            string text = pt.Node.TextContent ?? "";
            int lo, hi;
            if (_clickCount == 2)
                (lo, hi) = WordRange(text, pt.Offset);
            else
                // Line granularity: the engine has no line-walk for arbitrary text yet,
                // so a triple click takes the whole text node (exact for one-line nodes).
                (lo, hi) = (0, text.Length);
            _selAnchor = new SelPoint { Node = pt.Node, Offset = lo };
            _selFocus = new SelPoint { Node = pt.Node, Offset = hi };
            _hasSelection = lo < hi;
        }
        else
        {
            _selAnchor = pt;
            _selFocus = pt;
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>Expand a caret offset to the letter/digit run around it.</summary>
    private static (int lo, int hi) WordRange(string text, int offset)
    {
        int lo = Math.Clamp(offset, 0, text.Length), hi = lo;
        while (lo > 0 && IsWordChar(text[lo - 1])) lo--;
        while (hi < text.Length && IsWordChar(text[hi])) hi++;
        return (lo, hi);
    }

    private static bool IsInteractiveElement(Element element)
    {
        return element.TagName is "A" or "BUTTON" or "INPUT" or "TEXTAREA" or "SELECT"
            || element.GetAttribute("onclick") != null
            || element.GetAttribute("role") == "button";
    }

    private void UncheckRadioGroup(Element radio)
    {
        if (_document == null) return;
        var name = radio.GetAttribute("name");
        var queue = new Queue<Node>();
        if (_document.DocumentElement != null) queue.Enqueue(_document.DocumentElement);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var c in n.Children)
            {
                if (c is Element ce)
                {
                    if (string.Equals(ce.TagName, "INPUT", StringComparison.OrdinalIgnoreCase) &&
                        ce.GetAttribute("type") == "radio" &&
                        ce.GetAttribute("name") == name &&
                        !ReferenceEquals(ce, radio))
                        ce.RemoveAttribute("checked");
                    queue.Enqueue(ce);
                }
            }
        }
    }

    private static Node? FindForm(Element el)
    {
        for (var p = el.ParentElement; p != null; p = (p as Element)?.ParentElement)
            if (string.Equals(p.TagName, "FORM", StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    private void SetFocus(Element? next)
    {
        if (ReferenceEquals(_focused, next)) return;
        var old = _focused;
        _focused = next;
        _caret = 0;
        _selStart = -1;
        _showCursor = true;
        _blinkTick = Environment.TickCount64;
        ClearImeComposition();
        try { if (old != null) DispatchSimple(old, "blur"); } catch { }
        try { if (next != null) DispatchSimple(next, "focus"); } catch { }
        MarkPaint();
    }

    private bool TryEditable(out string value, out bool readOnly)
    {
        value = "";
        readOnly = false;
        var el = _focused;
        if (el == null || !el.IsTextEditable) return false;
        var t = el.InputType?.ToLowerInvariant();
        bool isText = t == null || t == "text" || t == "password" || t == "email" ||
                      t == "search" || t == "tel" || t == "url" || t == "number";
        if (!isText) return false;
        readOnly = el.HasAttribute("readonly");
        value = el.Value ?? "";
        return true;
    }

    public void HandleChar(ushort charCode)
    {
        var c = (char)charCode;
        if (c < 32 || c == 127) return;
        if (!TryEditable(out _, out _))
        {
            // Not editing: pages may still want keypress events on document.
            try { if (_document?.Body != null) DispatchSimple(_document.Body, "keypress"); } catch { }
            return;
        }
        InsertAtCaret(c.ToString());
    }

    /// <summary>
    /// The single text-insertion path: replace the pending selection at the caret,
    /// advance it, restart the blink clock, fire the input event and repaint. Typed
    /// characters and IME commits run through here so the two can never diverge.
    /// </summary>
    private void InsertAtCaret(string text)
    {
        if (!TryEditable(out var value, out var readOnly) || readOnly || _focused == null) return;
        _caret = Math.Clamp(_caret, 0, value.Length);
        if (_selStart >= 0 && _selStart != _caret)
        {
            int a = Math.Min(_selStart, _caret), b = Math.Max(_selStart, _caret);
            value = value[..a] + text + value[b..];
            _caret = a + text.Length;
            _selStart = -1;
        }
        else
        {
            value = value[.._caret] + text + value[_caret..];
            _caret += text.Length;
        }
        _focused.Value = value;
        _showCursor = true;
        _blinkTick = Environment.TickCount64;
        try { DispatchSimple(_focused, "input"); } catch { }
        MarkPaint();
    }

    // ==================== IME composition ====================

    private void ClearImeComposition()
    {
        _imeComposing = false;
        _imeComposition = "";
        _imeCursorPos = 0;
    }

    /// <summary>Composition began: the pending string is empty until the first update.</summary>
    public void ImeCompositionStart()
    {
        if (_imeComposing) return;
        _imeComposing = true;
        _imeComposition = "";
        _imeCursorPos = 0;
        MarkPaint();
    }

    /// <summary>
    /// New composition text with the caret offset inside it. Repaints only when the text
    /// or the offset actually moved — an idle focused field must not burn a frame per tick.
    /// </summary>
    public void ImeCompositionUpdate(string? text, int cursor)
    {
        text ??= "";
        if (!_imeComposing) ImeCompositionStart();
        if (text == _imeComposition && cursor == _imeCursorPos) return;
        _imeComposition = text;
        _imeCursorPos = cursor;
        MarkPaint();
    }

    /// <summary>
    /// Ends composition: a non-empty result is inserted through the same editing path as
    /// typed characters (value, caret, selection-clear, blink clock, input event); a
    /// null/empty result is a plain dismissal that only drops the pending text.
    /// </summary>
    public void ImeCompositionCommit(string? text)
    {
        var wasComposing = _imeComposing;
        var hadPending = _imeComposition.Length > 0;
        ClearImeComposition();
        if (!string.IsNullOrEmpty(text))
            InsertAtCaret(text);
        else if (wasComposing || hadPending)
            MarkPaint();
    }

    /// <summary>Discard the pending composition without inserting anything.</summary>
    public void ImeCompositionCancel() => ImeCompositionCommit(null);

    /// <summary>
    /// Caret geometry of the focused editable in DOCUMENT space (no scroll, no chrome
    /// offset — the shell scrolls speculatively and transforms these itself): same maths
    /// as the shell's in-process IME host (LayoutBox + measured text before the caret),
    /// plus the pending composition so the candidate window tracks the drawn caret.
    /// </summary>
    public void GetImeCaretState(out float caretX, out float caretY, out float caretH,
        out bool hasEditableFocus, out bool isPassword)
    {
        caretX = caretY = caretH = 0;
        hasEditableFocus = false;
        isPassword = false;
        var el = _focused;
        if (el == null || !el.IsTextEditable) return;
        hasEditableFocus = true;
        isPassword = el.InputType?.ToLowerInvariant() == "password";
        var box = el.LayoutBox;
        if (box == null) return;

        float fontSize = el.ComputedStyle?.FontSize > 0 ? el.ComputedStyle.FontSize : 14;
        string fontFamily = el.ComputedStyle?.FontFamily ?? "Arial";
        string value = el.Value ?? "";
        int cursor = Math.Clamp(_caret, 0, value.Length);
        string before = value[..cursor];
        if (_imeComposing && _imeComposition.Length > 0)
            before += _imeComposition[..Math.Clamp(_imeCursorPos, 0, _imeComposition.Length)];
        float textBeforeWidth = TextMeasurer.Instance?.MeasureText(before, fontFamily, fontSize)
            ?? before.Length * fontSize * 0.55f;
        caretX = box.ContentBox.Left + 2 + textBeforeWidth;
        caretY = box.BorderBox.Top;
        caretH = box.ContentBox.Height > 0 ? box.ContentBox.Height : fontSize * 1.5f;
    }

    public void HandleKey(ushort charCode, ushort key, bool repeat)
    {
        var k = (UpBrowser.Platform.Key)key;

        if (_focused != null && TryEditable(out var value, out var readOnly))
        {
            bool edited = false;
            switch (k)
            {
                case UpBrowser.Platform.Key.Backspace:
                    if (!readOnly && _caret > 0)
                    {
                        if (_selStart >= 0 && _selStart != _caret)
                        {
                            int a = Math.Min(_selStart, _caret), b = Math.Max(_selStart, _caret);
                            value = value[..a] + value[b..];
                            _caret = a; _selStart = -1;
                        }
                        else
                        {
                            value = value[..(_caret - 1)] + value[_caret..];
                            _caret--;
                        }
                        edited = true;
                    }
                    break;
                case UpBrowser.Platform.Key.Delete:
                    if (!readOnly && _caret < value.Length)
                    {
                        value = value[.._caret] + value[(_caret + 1)..];
                        edited = true;
                    }
                    break;
                case UpBrowser.Platform.Key.Left:
                    if (_caret > 0) _caret--;
                    _selStart = -1; edited = false; break;
                case UpBrowser.Platform.Key.Right:
                    if (_caret < value.Length) _caret++;
                    _selStart = -1; edited = false; break;
                case UpBrowser.Platform.Key.Home:
                    _caret = 0; _selStart = -1; break;
                case UpBrowser.Platform.Key.End:
                    _caret = value.Length; _selStart = -1; break;
                case UpBrowser.Platform.Key.Escape:
                case UpBrowser.Platform.Key.Tab:
                    SetFocus(null);
                    return;
                case UpBrowser.Platform.Key.Enter:
                    if (_focused.TagName == "TEXTAREA" && !readOnly)
                    {
                        value = value[.._caret] + '\n' + value[_caret..];
                        _caret++;
                        _focused.Value = value;
                        MarkPaint();
                    }
                    else
                    {
                        var form = FindForm(_focused) as UpBrowser.Core.Dom.Html.HTMLFormElement;
                        try { DispatchSimple(_focused, "keydown"); } catch { }
                        form?.Submit();
                    }
                    return;
            }
            if (edited && _focused != null)
            {
                _focused.Value = value;
                try { DispatchSimple(_focused, "input"); } catch { }
            }
            if (edited || k is UpBrowser.Platform.Key.Left or UpBrowser.Platform.Key.Right
                or UpBrowser.Platform.Key.Home or UpBrowser.Platform.Key.End)
            {
                _showCursor = true;
                _blinkTick = Environment.TickCount64;
                MarkPaint();
                return;
            }
        }

        // Non-editable: surface keydown to the page (games/listeners).
        try
        {
            var target = _focused ?? _document?.Body;
            if (target != null)
                DispatchSimple(target, "keydown");
        }
        catch { }
    }

    public void HandleWheel(float dx, float dy, float x, float y)
    {
        bool consumed = false;
        if (_document != null)
        {
            var el = PageHitTest.HitTest(_document, x + _scrollX, y + _scrollY);
            bool vertical = Math.Abs(dy) >= Math.Abs(dx);
            float delta = vertical ? dy : dx;
            var root = _document.Body;
            for (var p = el; p != null && !consumed; p = p.ParentElement)
            {
                // body/html is the ROOT scroller: its overflow is the window
                // scroll, which the host owns. Passing it as "consumed" would
                // move an invisible box and leave the page frozen.
                if (p == root || string.Equals(p.TagName, "HTML", StringComparison.OrdinalIgnoreCase))
                    continue;
                var box = p.LayoutBox;
                if (box == null || !box.IsScrollContainer) continue;
                float max = vertical
                    ? box.ScrollContentHeight - box.ContentBox.Height
                    : box.ScrollContentWidth - box.ContentBox.Width;
                if (max <= 0.5f) continue;
                float cur = vertical ? box.ScrollY : box.ScrollX;
                float next = Math.Clamp(cur + delta, 0, max);
                if (Math.Abs(next - cur) < 0.01f) continue; // at edge → page scrolls
                if (vertical) box.ScrollY = next; else box.ScrollX = next;
                consumed = true;
                MarkPaint();
            }
        }
        _sink.ReportWheelConsumed(consumed);
    }

    private void NoteScrollMoved()
    {
        _lastScrollMoveTick = Environment.TickCount64;
        if (_mouseVX >= 0) _hoverScrollPending = true;

        // Scrolling is the only way an animation parked as off-screen becomes visible
        // without anything else changing, so it is also where the park has to be lifted:
        // the paint walk runs again with the current animated values and the sampling
        // returns to frame rate.
        if (_animOffscreen && AnimDamageVisible())
        {
            _animOffscreen = false;
            MarkPaint();
        }
    }

    // ==================== <select> dropdown ====================

    /// <summary>The SELECT an element belongs to: a click lands on the control or on its text.</summary>
    private static Element? SelectAncestor(Element? el)
    {
        for (var p = el; p != null; p = p.ParentElement)
            if (string.Equals(p.TagName, "SELECT", StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private static bool IsOption(Node? node) => node is Element e &&
        string.Equals(e.TagName, "OPTION", StringComparison.OrdinalIgnoreCase);

    /// <summary>The OPTION an element sits in, when that option is a child of a SELECT.</summary>
    private static Element? OptionAncestor(Element? el)
    {
        for (var p = el; p != null; p = p.ParentElement)
        {
            if (!IsOption(p)) continue;
            var select = p.ParentElement;
            while (select != null && !string.Equals(select.TagName, "SELECT", StringComparison.OrdinalIgnoreCase))
                select = string.Equals(select.TagName, "OPTGROUP", StringComparison.OrdinalIgnoreCase) ? select.ParentElement : null;
            return select != null ? p : null;
        }
        return null;
    }

    /// <summary>Make one option the selected one. A single-valued select has exactly one
    /// option carrying the attribute, so picking rewrites all of them, and both the option and
    /// the control announce the change the way a browser does.</summary>
    private void ChooseOption(Element select, Element chosen)
    {
        foreach (var child in select.Children)
        {
            if (child is not Element opt || !IsOption(opt)) continue;
            if (ReferenceEquals(opt, chosen)) opt.SetAttribute("selected", "");
            else opt.RemoveAttribute("selected");
        }
        try
        {
            DispatchSimple(chosen, "change");
            DispatchSimple(select, "change");
            DispatchSimple(select, "input");
        }
        catch { }
        if (ReferenceEquals(_activeSelect, select)) CloseDropdown();
        // The control's own text is the selected option's, so the choice changes pixels
        // whether or not a list was open.
        else MarkLayout();
    }

    private void OpenDropdown(Element select)
    {
        _activeSelect = select;
        ComputeDropdownGeometry(select);
        MarkLayout();
    }

    private void CloseDropdown()
    {
        if (_activeSelect == null) return;
        _activeSelect = null;
        _optionRects.Clear();
        _hoverOption = -1;
        _dropdownRect = default;
        MarkLayout();
    }

    /// <summary>
    /// Lay the list out by hand. A closed select renders only its selected option, so the
    /// options have no boxes for layout to place: the list is as wide as its widest option
    /// (never narrower than the control), one row per option, and it flips above the control
    /// when the space below runs out of the viewport.
    /// </summary>
    private void ComputeDropdownGeometry(Element select)
    {
        _optionRects.Clear();
        _dropdownRect = default;
        _hoverOption = -1;

        var cb = select.LayoutBox?.ContentBox ?? default;
        float fontSize = select.ComputedStyle?.FontSize > 0 ? select.ComputedStyle.FontSize : 14f;
        float rowHeight = Math.Max(22f, fontSize + 8f);
        var measurer = UpBrowser.Core.Layout.TextMeasurer.Instance;

        float widest = cb.Width - 8f;
        foreach (var child in select.Children)
        {
            if (!IsOption(child)) continue;
            string text = child.TextContent?.Trim() ?? "";
            float w = measurer != null
                ? measurer.MeasureText(text, "Segoe UI, Arial, sans-serif", fontSize)
                : text.Length * fontSize * 0.55f;
            _optionRects.Add(((Element)child, default));
            if (w > widest) widest = w;
        }
        if (widest <= 0f || _optionRects.Count == 0) { _optionRects.Clear(); return; }

        float dropW = widest + 24f;
        float dropH = _optionRects.Count * rowHeight;
        float dropX = cb.Left, dropY = cb.Bottom;
        float viewportRight = _scrollX + _viewportW, viewportBottom = _scrollY + _viewportH;
        if (dropY + dropH > viewportBottom) dropY = Math.Max(cb.Top - dropH, _scrollY);
        if (dropX + dropW > viewportRight) dropX = Math.Max(_scrollX, viewportRight - dropW);

        _dropdownRect = new SKRect(dropX, dropY, dropX + dropW, dropY + dropH);
        for (int i = 0; i < _optionRects.Count; i++)
        {
            float rowTop = dropY + i * rowHeight;
            _optionRects[i] = (_optionRects[i].Option,
                new SKRect(dropX, rowTop, dropX + dropW, rowTop + rowHeight));
        }
    }

    /// <summary>Consumes a click that belongs to the open list. Returns false when the click
    /// landed outside, in which case the list closes and the click is the page's again.</summary>
    private bool HandleDropdownClick(float docX, float docY)
    {
        var select = _activeSelect;
        if (select == null) return false;

        // Pressing the control again closes the list it opened.
        var sb = select.LayoutBox?.BorderBox ?? default;
        if (docX >= sb.Left && docX <= sb.Right && docY >= sb.Top && docY <= sb.Bottom)
        {
            CloseDropdown();
            return true;
        }

        var dr = _dropdownRect;
        if (docX < dr.Left || docX > dr.Right || docY < dr.Top || docY > dr.Bottom)
        {
            CloseDropdown();
            return false;
        }

        foreach (var (chosen, rect) in _optionRects)
        {
            if (docX < rect.Left || docX > rect.Right || docY < rect.Top || docY > rect.Bottom) continue;
            ChooseOption(select, chosen);
            return true;
        }
        return true;   // padding inside the list: the page must not see this click
    }

    private void UpdateDropdownHover(float docX, float docY)
    {
        int hover = -1;
        for (int i = 0; i < _optionRects.Count; i++)
        {
            var r = _optionRects[i].Rect;
            if (docX >= r.Left && docX <= r.Right && docY >= r.Top && docY <= r.Bottom) { hover = i; break; }
        }
        if (hover == _hoverOption) return;
        _hoverOption = hover;
        MarkPaint();
    }

    private void HandleMove(float x, float y)
    {
        if (_document == null) return;

        // The highlighted option follows the pointer while the list is open.
        if (_activeSelect != null) UpdateDropdownHover(x + _scrollX, y + _scrollY);

        // Selection drag: extend the focus to the caret under the pointer, but only when
        // the position actually changed — a repaint per unchanged move would defeat the
        // child's idle-frame suppression.
        if (_isSelecting)
        {
            var pt = HitTestTextPosition(x + _scrollX, y + _scrollY);
            if (pt.Node != null && (pt.Node != _selFocus.Node || pt.Offset != _selFocus.Offset))
            {
                _selFocus = pt;
                _hasSelection = true;
                MarkPaint();
            }
        }

        var el = PageHitTest.HitTest(_document, x + _scrollX, y + _scrollY);

        // mousemove fires on every move regardless of hover transitions.
        try
        {
            if (el != null)
                DispatchDomEvent(el, h => new ScriptEvent("mousemove", h) { clientX = x, clientY = y });
        }
        catch { }

        _mouseVX = x; _mouseVY = y;
        UpdateHoverAt(x, y);
    }

    /// <summary>
    /// Recompute :hover for the viewport point (x, y). Also called after scroll:
    /// content moves under a stationary pointer, so hover must follow the new
    /// document position (real browsers fire mouseout/mouseover there too).
    /// </summary>
    private void UpdateHoverAt(float x, float y)
    {
        if (_document == null) return;
        var el = PageHitTest.HitTest(_document, x + _scrollX, y + _scrollY);
        if (ReferenceEquals(el, _hoveredElement)) return;

        // mouseout / mouseover with relatedTarget, mirroring the UI-thread model.
        try
        {
            if (_hoveredElement != null)
                DispatchDomEvent(_hoveredElement, h => new ScriptEvent("mouseout", h)
                {
                    clientX = x, clientY = y,
                    relatedTarget = el != null ? WrapHost(el) : null,
                });
            if (el != null)
                DispatchDomEvent(el, h => new ScriptEvent("mouseover", h)
                {
                    clientX = x, clientY = y,
                    relatedTarget = _hoveredElement != null ? WrapHost(_hoveredElement) : null,
                });
        }
        catch { }

        // CSS :hover — toggle the ancestor chains, then only relayout when the
        // document actually carries :hover rules.
        for (var p = _hoveredElement; p != null; p = p.ParentElement) p.IsHovered = false;
        _hoveredElement = el;
        for (var p = el; p != null; p = p.ParentElement) p.IsHovered = true;

        if (!ReferenceEquals(_hoverRulesDoc, _document))
        {
            _docHasHoverRules = _styleComputer?.HasHoverRules() ?? false;
            _hoverRulesDoc = _document;
        }
        if (_docHasHoverRules)
            MarkLayout();
        else
            MarkPaint(); // form-control hover visuals live in the paint state
    }

    /// <summary>
    /// Hover recompute deferred from scroll settles once the wheel stops:
    /// running it per scroll tick fires mouseout/mouseover JS + a full
    /// style/layout rebuild every frame, saturating the engine so stale
    /// offset frames linger on screen for hundreds of ms.
    /// </summary>
    /// <param name="commandsIdle">True when the host has no queued input/navigation left.</param>
    public void SettleDeferredHover(bool commandsIdle)
    {
        if (_hoverScrollPending && commandsIdle &&
            Environment.TickCount64 - _lastScrollMoveTick >= 110)
        {
            _hoverScrollPending = false;
            if (_mouseVX >= 0) UpdateHoverAt(_mouseVX, _mouseVY);
        }
    }

    // ==================== selection geometry ====================

    /// <summary>Select every text node on the page (the shell's page-level Ctrl+A branch):
    /// first text node start through last text node end.</summary>
    public void SelectAllText()
    {
        if (_document == null) return;
        TextNode? first = null, last = null;
        FindFirstLastTextNodes(_document.DocumentElement ?? _document.Body, ref first, ref last);
        if (first != null && last != null)
        {
            _selAnchor = new SelPoint { Node = first, Offset = 0 };
            _selFocus = new SelPoint { Node = last, Offset = (last.TextContent ?? "").Length };
            _hasSelection = true;
            _isSelecting = false;
            MarkPaint();
        }
    }

    /// <summary>First and last <em>rendered</em> text nodes of a subtree. Nothing below a
    /// <c>display:none</c> box is selectable, which is what keeps a page's own stylesheet and
    /// scripts out of Ctrl+A and the clipboard. Shared with the shell so both answer
    /// identically whichever side owns the document.</summary>
    public static void FindFirstLastTextNodes(Node? node, ref TextNode? first, ref TextNode? last,
        bool rendered = true)
    {
        if (node == null) return;
        if (node is Element element)
        {
            if (!rendered) return;
            if (element.ComputedStyle?.Display == DisplayType.None ||
                element is { TagName: "OPTION" or "OPTGROUP" } &&
                UpBrowser.Core.Css.ElementStyles.FormElements.BelongsToClosedSelect(element))
                rendered = false;
        }
        else if (node is TextNode tn && rendered)
        {
            first ??= tn;
            last = tn;
        }
        foreach (var child in node.Children)
            FindFirstLastTextNodes(child, ref first, ref last, rendered);
    }

    /// <summary>Resolve a document-space point to the (text node, character offset) caret
    /// position under it, walking the laid-out line/run geometry of the tree.</summary>
    private SelPoint HitTestTextPosition(float dlX, float dlY)
    {
        var doc = _document!;
        var result = new SelPoint { Node = null, Offset = 0 };
        HitTestTextPositionRecursive(doc.DocumentElement ?? doc.Body, dlX, dlY, ref result);
        if (result.Node == null && doc.Body != null)
            HitTestTextPositionRecursive(doc.Body, dlX, dlY, ref result);
        return result;
    }

    // Coordinates here are document-space (viewport point + engine scroll), so unlike the
    // shell's version there is no content-offset term: the page origin IS the layout origin.
    private void HitTestTextPositionRecursive(Element? element, float dlX, float dlY, ref SelPoint result)
    {
        if (element == null) return;
        var box = element.LayoutBox;
        if (box != null)
        {
            float boxTop = box.ContentBox.Top;
            float boxBottom = boxTop + box.ContentBox.Height;

            // Only check Lines/LineRuns if hit point is within element's Y range (with large tolerance)
            bool boxInRange = dlY >= boxTop - HitToleranceY * 4 && dlY < boxBottom + HitToleranceY * 4;

            if (boxInRange && box.Lines != null && box.Lines.Count > 0)
            {
                float boxLeft = box.ContentBox.Left;
                TextNode? lastTextNode = null;
                int runCharOffset = 0;
                foreach (var line in box.Lines)
                {
                    float lineTop = line.Y;
                    float lineBottom = lineTop + line.Height;
                    bool lineMatches = dlY >= lineTop - HitToleranceY && dlY < lineBottom + HitToleranceY;
                    if (!lineMatches) continue;
                    float runX = boxLeft + line.TextAlignOffsetX;
                    foreach (var run in line.Runs)
                    {
                        if (!run.IsText || run.Node is not TextNode tn)
                        {
                            lastTextNode = null;
                            runCharOffset = 0;
                            runX += run.Width;
                            continue;
                        }

                        if (tn != lastTextNode)
                        {
                            runCharOffset = 0;
                            lastTextNode = tn;
                        }

                        if (dlX < runX + run.Width + HitToleranceX || run == line.Runs[^1])
                        {
                            float localX = Math.Max(0, dlX - runX);
                            int localOffset = GetCharOffsetAtX(run, run.Text ?? "", localX);
                            result = new SelPoint { Node = tn, Offset = runCharOffset + localOffset };
                            return;
                        }

                        runCharOffset += (run.Text ?? "").Length;
                        runX += run.Width;
                    }
                    // Not found in this line's runs, continue to next line
                }
            }

            if (boxInRange && box.LineRuns != null && box.LineRuns.Count > 0)
            {
                float boxTopLR = box.ContentBox.Top;
                float x = box.ContentBox.Left;
                float lineHeight = 0;
                foreach (var run in box.LineRuns) lineHeight = Math.Max(lineHeight, run.Height);
                if (lineHeight <= 0) lineHeight = box.ContentBox.Height;
                if (dlY >= boxTopLR - HitToleranceY && dlY < boxTopLR + lineHeight + HitToleranceY)
                {
                    TextNode? lastTextNode = null;
                    int runCharOffset = 0;
                    foreach (var run in box.LineRuns)
                    {
                        if (!run.IsText || run.Node is not TextNode tn)
                        {
                            lastTextNode = null;
                            runCharOffset = 0;
                            x += run.Width;
                            continue;
                        }

                        if (tn != lastTextNode)
                        {
                            runCharOffset = 0;
                            lastTextNode = tn;
                        }

                        if (dlX < x + run.Width + HitToleranceX || run == box.LineRuns[^1])
                        {
                            float localX = Math.Max(0, dlX - x);
                            int localOffset = GetCharOffsetAtX(run, run.Text ?? "", localX);
                            result = new SelPoint { Node = tn, Offset = runCharOffset + localOffset };
                            return;
                        }

                        runCharOffset += (run.Text ?? "").Length;
                        x += run.Width;
                    }
                }
            }
            // Fallback for elements with text but no Lines/LineRuns (e.g. table cells)
            if (boxInRange && (box.Lines == null || box.Lines.Count == 0) && (box.LineRuns == null || box.LineRuns.Count == 0))
            {
                var style = element.ComputedStyle;
                if (style != null)
                {
                    float fontSize = style.FontSize;
                    string fontFamily = style.FontFamily ?? "Arial";
                    var fontWeight = style.FontWeight;
                    var textAlign = style.TextAlign;
                    float boxLeft = box.ContentBox.Left;
                    float boxRight = box.ContentBox.Right;
                    float boxWidth = box.ContentBox.Width;
                    float boxTop2 = box.ContentBox.Top;

                    // Tight Y bounds: text occupies [boxTop2, boxTop2 + fontSize]
                    float textTop2 = boxTop2 - HitToleranceY;
                    float textBottom2 = boxTop2 + fontSize + HitToleranceY;
                    if (dlY >= textTop2 && dlY <= textBottom2)
                    {
                        // Quick X rejection before any text measurement
                        if (dlX >= boxLeft - HitToleranceX && dlX <= boxRight + HitToleranceX)
                        {
                            foreach (var child in element.Children)
                            {
                                if (child is TextNode tn)
                                {
                                    string text = tn.TextContent ?? "";
                                    if (string.IsNullOrEmpty(text)) continue;

                                    // Lightweight width approximation for fast rejection
                                    float approxWidth = text.Length * fontSize * 0.45f;
                                    float startX = boxLeft;
                                    if (textAlign == TextAlignType.Center)
                                        startX = boxLeft + (boxWidth - approxWidth) / 2;
                                    else if (textAlign == TextAlignType.Right || textAlign == TextAlignType.End)
                                        startX = boxLeft + boxWidth - approxWidth;

                                    if (dlX >= startX - HitToleranceX && dlX <= startX + approxWidth + HitToleranceX)
                                    {
                                        // Accurate measurement
                                        float textWidth;
                                        if (TextMeasurer.Instance != null)
                                            textWidth = TextMeasurer.Instance.MeasureText(text, fontFamily, fontSize, fontWeight);
                                        else
                                            textWidth = approxWidth;

                                        if (textAlign == TextAlignType.Center)
                                            startX = boxLeft + (boxWidth - textWidth) / 2;
                                        else if (textAlign == TextAlignType.Right || textAlign == TextAlignType.End)
                                            startX = boxLeft + boxWidth - textWidth;
                                        else
                                            startX = boxLeft;

                                        if (dlX >= startX - HitToleranceX && dlX < startX + textWidth + HitToleranceX)
                                        {
                                            float localX = Math.Max(0, dlX - startX);
                                            int offset = GetCharOffsetAtX(text, fontSize, fontFamily, fontWeight, textWidth, localX);
                                            result = new SelPoint { Node = tn, Offset = offset };
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Only recurse into children if the hit point could be within this element's area
        // (avoid walking entire DOM tree for every mouse move)
        foreach (var child in element.Children.OfType<Element>())
        {
            HitTestTextPositionRecursive(child, dlX, dlY, ref result);
            if (result.Node != null) return;
        }
    }

    private static int GetCharOffsetAtX(string text, float fontSize, string fontFamily, FontWeight weight, float textWidth, float localX)
    {
        if (string.IsNullOrEmpty(text) || localX <= 0) return 0;
        if (localX >= textWidth) return text.Length;

        int len = text.Length;
        float[] cumWidths = new float[len];
        float acc = 0;
        for (int i = 0; i < len; i++)
        {
            string chStr = text[i].ToString();
            float cw;
            if (TextMeasurer.Instance != null)
                cw = TextMeasurer.Instance.MeasureText(chStr, fontFamily, fontSize, weight);
            else
                cw = fontSize * 0.45f;
            acc += cw;
            cumWidths[i] = acc;
        }

        int lo = 0, hi = len;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (cumWidths[mid - 1] <= localX) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private static int GetCharOffsetAtX(InlineRun run, string runText, float localX)
    {
        float fontSize = run.FontSize ?? 16;
        string fontFamily = run.FontFamily ?? "Arial";
        var weight = run.FontWeight;
        return GetCharOffsetAtX(runText, fontSize, fontFamily, weight, run.Width, localX);
    }

    // ==================== selection text extraction ====================

    /// <summary>Plain text between two caret points, normalised to DOM order — the exact
    /// collector the shell's copy path uses.</summary>
    private static void CollectSelectedTextRange(TextNode startNode, int startOff,
        TextNode endNode, int endOff, System.Text.StringBuilder sb)
    {
        // Normalize: ensure start is before end in DOM order
        int cmp = CompareDomPosition(startNode, endNode);
        if (cmp > 0)
        {
            (startNode, endNode) = (endNode, startNode);
            (startOff, endOff) = (endOff, startOff);
        }
        else if (cmp == 0)
        {
            // Same node
            int lo = Math.Min(startOff, endOff);
            int hi = Math.Max(startOff, endOff);
            var text = startNode.TextContent ?? "";
            if (lo < 0) lo = 0;
            if (hi > text.Length) hi = text.Length;
            if (lo < hi)
                sb.Append(text.AsSpan(lo, hi - lo));
            return;
        }

        // Different nodes: collect from start node to its end,
        // then all text nodes between them in DOM order, then from beginning of end node
        var startText = startNode.TextContent ?? "";
        if (startOff < startText.Length)
            sb.Append(startText.AsSpan(startOff));

        CollectTextBetween(startNode, endNode, sb);

        var endText = endNode.TextContent ?? "";
        if (endOff > 0 && endOff <= endText.Length)
            sb.Append(endText.AsSpan(0, endOff));
    }

    private static void CollectTextBetween(Node startNode, Node endNode, System.Text.StringBuilder sb)
    {
        // Find common ancestor by building ancestor paths
        var startPath = new List<Node>();
        var n = startNode;
        while (n != null) { startPath.Add(n); n = n.ParentNode; }

        var endPath = new List<Node>();
        n = endNode;
        while (n != null) { endPath.Add(n); n = n.ParentNode; }

        startPath.Reverse();
        endPath.Reverse();

        int depth = Math.Min(startPath.Count, endPath.Count);
        Node? commonAncestor = null;
        for (int i = 0; i < depth; i++)
        {
            if (startPath[i] == endPath[i])
                commonAncestor = startPath[i];
            else
                break;
        }

        if (commonAncestor == null) return;

        // DFS from common ancestor, collecting text between startNode and endNode
        bool collecting = false;
        CollectTextBetweenRecursive(commonAncestor, startNode, endNode, ref collecting, sb);
    }

    /// <summary>True when nothing above this element hides it: a display:none subtree is not
    /// rendered, so its text is neither a selection anchor nor part of the selected text.</summary>
    private static bool IsRendered(Element? element)
    {
        // The options of a single select are not page text: the control paints one line and
        // the rows only exist while it is open, so selecting the page must not copy them.
        if (UpBrowser.Core.Css.ElementStyles.FormElements.BelongsToClosedSelect(element)) return false;
        for (var el = element; el != null; el = el.ParentElement)
            if (el.ComputedStyle?.Display == DisplayType.None) return false;
        return true;
    }

    private static bool CollectTextBetweenRecursive(Node current, Node startNode, Node endNode,
        ref bool collecting, System.Text.StringBuilder sb)
    {
        foreach (var child in current.Children)
        {
            if (child == endNode)
                return true;

            if (!collecting)
            {
                if (child == startNode)
                {
                    collecting = true;
                    continue;
                }
                if (child is Element el)
                {
                    if (!IsRendered(el)) continue;
                    if (CollectTextBetweenRecursive(el, startNode, endNode, ref collecting, sb))
                        return true;
                }
                continue;
            }

            if (child is TextNode tn)
            {
                // The nodes between the anchors are walked in DOM order, which passes through
                // subtrees the page does not render at all; selecting what is on screen is the
                // whole point of the extraction, so those contribute nothing.
                if (!IsRendered(tn.ParentElement)) continue;
                var text = tn.TextContent;
                if (!string.IsNullOrEmpty(text))
                    sb.Append(text);
            }
            else if (child is Element el)
            {
                if (!IsRendered(el)) continue;
                if (CollectTextBetweenRecursive(el, startNode, endNode, ref collecting, sb))
                    return true;
            }
        }
        return false;
    }

    private static int CompareDomPosition(Node a, Node b)
    {
        if (a == b) return 0;
        // Walk ancestors to find common ancestor and compare position
        var aPath = new List<Node>();
        var bPath = new List<Node>();
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
                // Find sibling index
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

    // ==================== DevTools inspection & find (host-facing, transport-agnostic) ====================

    // DevTools node-id space: preorder indexes over the current document, valid only
    // while DomMutationTracker.Version holds. The cache is rebuilt on demand at the
    // first request after a version move — never polled, never copied per request.
    private readonly UpBrowser.PageHost.Inspection.DevToolsTree _dtTree = new();
    private List<(UpBrowser.Core.Dom.TextNode Node, int Start, int Length)> _findSpans = new();

    /// <summary>DOM version DevTools ids are minted under; moves invalidate every cached id.</summary>
    public static long DevToolsVersion => UpBrowser.PageHost.Inspection.DevToolsInspector.CurrentVersion;

    /// <summary>Stream a preorder chunk of the node tree starting at <paramref name="nodeId"/>
    /// (0 = document root). <paramref name="knownVersion"/> is the version the client's ids
    /// were minted under (-1 = fresh); a moved version answers
    /// <see cref="UpBrowser.PageContract.DtNodeBatch.VersionMoved"/> instead of wrong data.</summary>
    public UpBrowser.PageContract.DtNodeBatch CollectNodeTree(int nodeId, long knownVersion, int maxDepth, int limit)
    {
        var doc = _document;
        long current = UpBrowser.PageHost.Inspection.DevToolsInspector.CurrentVersion;
        if (doc == null) return UpBrowser.PageContract.DtNodeBatch.Failed(current);
        if (knownVersion >= 0 && knownVersion != current)
            return UpBrowser.PageContract.DtNodeBatch.Moved(current);
        if (!_dtTree.IsCurrent(doc, current)) _dtTree.Rebuild(doc, current);
        return UpBrowser.PageHost.Inspection.DevToolsInspector.CollectChunk(_dtTree, current, nodeId, maxDepth, limit);
    }

    /// <summary>Matched rules + inline style + computed subset for one preorder node id.
    /// Null when the id is unknown; VersionMoved when the id is stale.</summary>
    public UpBrowser.PageContract.DtStyleBatch? StylesForNode(int nodeId, long knownVersion)
    {
        var doc = _document;
        long current = UpBrowser.PageHost.Inspection.DevToolsInspector.CurrentVersion;
        if (doc == null) return null;
        if (knownVersion >= 0 && knownVersion != current)
            return new UpBrowser.PageContract.DtStyleBatch(nodeId, current,
                Array.Empty<UpBrowser.PageContract.DtStyleRule>(),
                Array.Empty<UpBrowser.PageContract.DtDeclaration>(),
                Array.Empty<UpBrowser.PageContract.DtDeclaration>(), true);
        if (!_dtTree.IsCurrent(doc, current)) _dtTree.Rebuild(doc, current);
        return UpBrowser.PageHost.Inspection.DevToolsInspector.BuildStyles(_dtTree, current, nodeId, _styleComputer);
    }

    /// <summary>Console evaluation: same formatting the in-process console used, errors
    /// folded into the result so a throwing script is data, not a fault.</summary>
    public UpBrowser.PageContract.EvalResult EvaluateScript(string script)
    {
        try
        {
            return new UpBrowser.PageContract.EvalResult(false,
                UpBrowser.PageHost.Inspection.DevToolsInspector.FormatEvalValue(_js.Evaluate(script)));
        }
        catch (Exception ex)
        {
            return new UpBrowser.PageContract.EvalResult(true, ex.Message);
        }
    }

    /// <summary>Find-in-page: walks text nodes for <paramref name="query"/>, returns
    /// match count + document-space (CSS px) rects, and when
    /// <paramref name="activateIndex"/> names a match, highlights it through the engine's
    /// existing selection overlay and scrolls it into view (echoed by
    /// <see cref="IPageEngineSink.ReportScrollChanged"/> like any other engine scroll).
    /// <paramref name="forward"/> shapes the wrap-around of the active index.</summary>
    public UpBrowser.PageContract.FindResult FindMatches(string query, bool caseSensitive, bool forward, int activateIndex)
    {
        var doc = _document;
        if (doc == null || string.IsNullOrEmpty(query))
        {
            _findSpans = new List<(UpBrowser.Core.Dom.TextNode, int, int)>();
            return new UpBrowser.PageContract.FindResult
            {
                TotalCount = 0,
                ActiveIndex = -1,
                Rects = Array.Empty<UpBrowser.PageContract.PageRect>(),
            };
        }

        _findSpans = UpBrowser.PageHost.Inspection.DevToolsInspector.FindSpans(doc, query, caseSensitive);
        var rects = new UpBrowser.PageContract.PageRect[_findSpans.Count];
        for (int i = 0; i < _findSpans.Count; i++)
            rects[i] = UpBrowser.PageHost.Inspection.DevToolsInspector.SpanRect(_findSpans[i].Node, _findSpans[i].Start, _findSpans[i].Length);

        int active = -1;
        if (activateIndex >= 0 && _findSpans.Count > 0)
        {
            // Wrap so "next past the end" restarts at the first match, backwards wraps up.
            int n = _findSpans.Count;
            active = ((activateIndex % n) + n) % n;
            ActivateFindMatch(active);
        }
        return new UpBrowser.PageContract.FindResult { TotalCount = _findSpans.Count, ActiveIndex = active, Rects = rects };
    }

    private void ActivateFindMatch(int index)
    {
        var (node, start, length) = _findSpans[index];
        // The existing selection overlay is the page highlight: reuse it rather than
        // painting a second one — the paint walk already tints exactly this node/offset
        // range, and copy/context keep working against the found text.
        _selAnchor = new SelPoint { Node = node, Offset = start };
        _selFocus = new SelPoint { Node = node, Offset = Math.Min(start + length, (node.TextContent ?? "").Length) };
        _hasSelection = true;
        _isSelecting = false;
        MarkPaint();

        var rect = UpBrowser.PageHost.Inspection.DevToolsInspector.SpanRect(node, start, length);
        ScrollToShow(rect);
    }

    /// <summary>Scroll the window so the rect is visible — via <see cref="SetScroll"/>,
    /// so an engine-initiated find scroll surfaces through the same ScrollChanged sync
    /// as scrollTo and the host's scrollbar stays correct.</summary>
    private void ScrollToShow(UpBrowser.PageContract.PageRect r)
    {
        if (r.Width <= 0 && r.Height <= 0) return;
        float x = _scrollX, y = _scrollY;
        if (r.Y < y) y = r.Y;
        else if (r.Y + r.Height > y + _viewportH) y = r.Y + r.Height - _viewportH;
        if (r.X < x) x = r.X;
        else if (r.X + r.Width > x + _viewportW) x = r.X + r.Width - _viewportW;
        SetScroll(x, y);
    }

    // ==================== render ====================

    private void MarkPaint() { _dlDirty = true; _paintVisible = true; _animOffscreen = false; _sink.RequestFrame(); }
    private void MarkLayout() { _styleDirty = true; _layoutDirty = true; _dlDirty = true; _paintVisible = true; _animOffscreen = false; _sink.RequestFrame(); }

    /// <summary>
    /// Sample every CSS animation and transition for this pass and write the
    /// results into the elements' computed styles.
    ///
    /// Runs between style resolution and layout, because both halves depend on the
    /// order: the engine compares cascaded values against their before-change
    /// style to find transition start conditions (so it must not be fed its own
    /// previous output), and layout has to read the values the frame will paint.
    /// </summary>
    private bool AdvanceAnimations(bool styleRecomputed = true)
    {
        if (_document == null || _styleComputer == null) return false;
        var root = _document.DocumentElement ?? _document.Body;
        if (root == null) return false;

        try
        {
            // Keyframe rules come from the cascade, so they can only change on a pass
            // whose styles were re-resolved; collecting them walks every rule group.
            if (styleRecomputed || _keyframeRules == null)
                _keyframeRules = _styleComputer.CollectKeyframeRules();

            long sT = Stopwatch.GetTimestamp();
            var result = _animations.Update(root, _keyframeRules, styleRecomputed: styleRecomputed);
            _stSampleMs += (Stopwatch.GetTimestamp() - sT) * 1000 / Stopwatch.Frequency;
            _stSampleN++;

            bool wasRunning = _animationsRunning;
            _animationsRunning = result.HasActiveAnimations;
            if (wasRunning && !_animationsRunning)
            {
                // The last effect just ended, and it leaves its final values written into
                // the computed styles. Only the cascade can take them back away, so an
                // animation ending is a style change: without this an element that
                // animated away and back would stay where the animation left it.
                _styleDirty = true;
                _dlDirty = true;
                _paintVisible = true;
                _animOffscreen = false;
                _animEffectsEnded = true;
            }
            _animNeedsLayout = result.NeedsLayout;
            _animNeedsRepaint = result.NeedsRepaint;
            _animTargets = result.RepaintTargets;
            _animBleeds = result.RepaintBleeds;
            _lastSampleTimelineMs = _animations.Timeline.CurrentTimeMs;
            if (_animTrace)
            {
                try
                {
                    File.AppendAllText("upbrowser_anim_live.log",
                        $"tab{_id} t={_animations.Timeline.CurrentTimeMs:F1}ms " +
                        $"kf={_styleComputer.CollectKeyframeRules().Count} " +
                        $"active={result.HasActiveAnimations} effects={_animations.ActiveEffectCount} " +
                        $"animated={result.AnimatedElements} layout={result.NeedsLayout}\n");
                }
                catch { }
            }
            return result.HasActiveAnimations;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PageEngine {_id}] anim: {ex.Message}");
            _animationsRunning = false;
            return false;
        }
    }

    /// <summary>
    /// Can this tick's animated pixels reach the area the host rasterizes?
    ///
    /// The painted area of an animated element is its own box plus everything laid out
    /// inside it (a child that overflows the parent still paints with the parent's
    /// animated values), mapped through the transforms along that path: an animated
    /// <c>translate</c> can carry an off-screen box on screen, so testing the untransformed
    /// box would cull a visible animation. Anything the walk cannot bound — a fixed or
    /// sticky box, a scroll container whose descendants' coordinates it cannot trust, a
    /// subtree over budget — counts as visible, because the answer only ever skips work and
    /// must never be wrong in the direction that drops pixels.
    /// </summary>
    private bool AnimDamageVisible()
    {
        // Shadow spread, outline offset and blur put pixels outside the box by an amount
        // the geometry does not carry, so there is nothing to test against.
        if (_animBleeds) return true;

        var targets = _animTargets;
        if (targets == null || targets.Count == 0) return true;

        float x0 = _scrollX, y0 = _scrollY, x1 = _scrollX + _viewportW, y1 = _scrollY + _viewportH;
        for (int i = 0; i < targets.Count; i++)
        {
            var el = targets[i];
            if (el == null) return true;

            var walk = new DamageWalk { Budget = DamageBoxBudget };
            if (!PaintedArea(el, ref walk)) return true;
            if (!walk.Any) return true;

            walk.Area.Inflate(DamageBleed, DamageBleed);
            var r = walk.Area;
            if (r.Right > x0 && r.Left < x1 && r.Bottom > y0 && r.Top < y1) return true;
        }
        return false;
    }

    /// <summary>Accumulates one animated element's painted area (see AnimDamageVisible).</summary>
    private struct DamageWalk
    {
        public SKRect Area;
        public bool Any;
        public int Budget;
    }

    private bool PaintedArea(Element el, ref DamageWalk walk)
    {
        var style = el.ComputedStyle;
        var box = el.LayoutBox;
        if (style == null || box == null) return false;
        if (style.Position is PositionType.Fixed or PositionType.Sticky) return false;
        if (box.IsSticky || box.IsScrollContainer) return false;
        if (--walk.Budget <= 0) return false;

        var self = box.BorderBox;
        if (!walk.Any) { walk.Area = self; walk.Any = true; }
        else walk.Area = SKRect.Union(walk.Area, self);

        foreach (var node in el.Children)
        {
            if (node is not Element child) continue;
            var inner = new DamageWalk { Budget = walk.Budget };
            if (!PaintedArea(child, ref inner)) return false;
            walk.Budget = inner.Budget;
            if (!inner.Any) continue;
            walk.Area = walk.Any ? SKRect.Union(walk.Area, inner.Area) : inner.Area;
            walk.Any = true;
        }

        walk.Area = Transformed(walk.Area, style, box);
        if (walk.Area.Left > walk.Area.Right) return false;   // a transform the parser rejected

        // The boxes are laid out untransformed, so an ancestor transform moves this
        // subtree's pixels without moving its box. Walk up and apply each one.
        for (var ancestor = el.ParentElement; ancestor != null; ancestor = ancestor.ParentElement)
        {
            var astyle = ancestor.ComputedStyle;
            var abox = ancestor.LayoutBox;
            if (astyle == null || abox == null) return false;
            if (astyle.Position is PositionType.Fixed or PositionType.Sticky ||
                abox.IsSticky || abox.IsScrollContainer)
                return false;   // its painted position is not the box's laid-out one
            if (!astyle.HasAnyTransform) continue;
            walk.Area = Transformed(walk.Area, astyle, abox);
            if (walk.Area.Left > walk.Area.Right) return false;
        }
        return true;
    }

    /// <summary>
    /// <paramref name="area"/> as this box paints it: the union of the area and the area
    /// mapped through the box's own transform. The matrix turns about the origin in the
    /// box's own space, so the rect travels to local coordinates and back; an origin
    /// other than the default is not modelled and is absorbed by growing the result by
    /// the box's own size, which is where the error can reach at most.
    /// </summary>
    private static SKRect Transformed(SKRect area, ComputedStyle style, LayoutBox box)
    {
        var border = box.BorderBox;
        if (!style.HasAnyTransform) return area;

        var ops = UpBrowser.Core.Css.TransformParser.Parse(
            style.EffectiveTransform(border.Width, border.Height));
        if (ops.Count == 0) return new SKRect(float.MaxValue, float.MaxValue, 0, 0);

        var m = UpBrowser.Core.Css.TransformParser.ToMatrix(ops, border.Width / 2f, border.Height / 2f);
        var local = area;
        local.Offset(-border.Left, -border.Top);
        var mapped = m.MapRect(local);
        mapped.Offset(border.Left, border.Top);
        if (!IsDefaultTransformOrigin(style.TransformOrigin))
            mapped.Inflate(border.Width, border.Height);
        return SKRect.Union(area, mapped);
    }

    private static bool IsDefaultTransformOrigin(string? origin) =>
        string.IsNullOrEmpty(origin) ||
        origin.Trim() is "50% 50% 0" or "50% 50%" or "center center";

    /// <summary>Boxes one damage estimate may walk before it gives up and paints.</summary>
    private const int DamageBoxBudget = 256;

    /// <summary>CSS px grown around the estimate for borders, decoration lines and rounding.</summary>
    private const float DamageBleed = 8f;

    /// <summary>
    /// Run the layout and display-list stages once. Returns true when either rebuilt,
    /// which is when the presenter must expect a new display list to rasterize.
    /// Exceptions propagate to the host loop — it owns the retry pacing.
    /// </summary>
    public bool UpdatePipeline()
    {
        if (_document == null) return false;
        // Layout and paint below ask the seam for replaced-element sizes; claim it for this
        // document, so a second engine in the same process cannot answer with its own cache.
        using IDisposable? replacedScope = _replacedResolver == null
            ? null : UpBrowser.Core.Layout.ReplacedIntrinsicSizes.Use(_replacedResolver);
        bool rebuilt = false;
        long t0 = _animTrace ? Stopwatch.GetTimestamp() : 0;
        long t1 = t0;
        long tStyle = t0;

        // A live effect is itself a reason to run the pipeline again: nothing else
        // about the page has changed, but its style has. Asking for the next pass
        // is what keeps an animation running after the first frame. An effect that
        // is entirely off-screen is the exception — its sample advances, but its
        // output cannot be seen, so neither the paint walk nor the raster runs for
        // it (see AnimDamageVisible).
        if (_animationsRunning && !_animOffscreen)
        {
            _dlDirty = true;
        }

        // Style is re-resolved when the document changed. It is NOT re-resolved for
        // an animation frame where nothing else is dirty: the animation overwrites
        // the same properties every frame and the cascade cannot have changed
        // underneath it, so a full style pass per frame is pure cost — and on a
        // real page it is the single most expensive thing in the frame. The engine
        // is told which case this is, so it knows a transition may not start.
        //
        // The two stages may need one extra round together, for exactly one case: the
        // last effect ending. That re-dirties style, because restoring the values the
        // animation had overridden is something only the cascade can do — and the paint
        // walk must not run before that restore, or it bakes in the animated value and
        // the element keeps the look of a frame that no longer exists.
        for (int round = 0; ; round++)
        {
            bool styleRecomputed = _styleDirty;
            if (styleRecomputed)
            {
                if (_styleComputer == null)
                {
                    _styleComputer = new StyleComputer();
                    _styleComputer.AddStylesheet(_docManager.GetUaStylesheet(), UpBrowser.Core.Css.Resolver.CascadeOrigin.UserAgent);
                }
                _styleComputer.ComputeStyles(_document, _viewportW, _viewportH);
                _stStylePass++;
                _styleDirty = false;
                tStyle = _animTrace ? Stopwatch.GetTimestamp() : t0;
            }

            // "Update animations and send events" sits here, between style and layout.
            // When style did run, the engine compares the cascaded values to find
            // transition start conditions.
            _animEffectsEnded = false;
            if (AdvanceAnimations(styleRecomputed))
            {
                // Only animations that move something layout consumes may re-run the whole
                // tree; an opacity or colour pulse is a paint change, and a full relayout
                // per animation frame costs more than everything else in that frame.
                if (_animNeedsLayout)
                {
                    _layoutDirty = true;
                    _paintVisible = true;
                    _animOffscreen = false;
                }
                else if (_animNeedsRepaint)
                {
                    // The display list bakes the animated values in, so a tick whose damage
                    // is on screen has to rebuild it and then raster. When every animated box
                    // is outside the rasterized area neither helps: the list keeps the last
                    // values it was walked with, and NoteScrollMoved invalidates it again the
                    // moment one of those boxes comes back into view.
                    if (AnimDamageVisible())
                    {
                        _stAnimVisible++;
                        _dlDirty = true;
                        _paintVisible = true;
                        _animOffscreen = false;
                    }
                    else
                    {
                        _stAnimPark++;
                        _animOffscreen = true;
                    }
                }
            }

            if (round == 0 && _animEffectsEnded && _styleDirty) continue;
            break;
        }

        if (_layoutDirty)
        {
            _stLayout++;
            _incremental ??= new IncrementalLayoutEngine(_layoutEngine, _layoutCache);
            _incremental.Layout(_document, _viewportW, _viewportH, _dpi, 16f);
            _layoutDirty = false;

            var bodyBox = _document.Body?.LayoutBox;
            _contentW = bodyBox?.BorderBox.Width ?? _viewportW;
            _contentH = bodyBox?.BorderBox.Height ?? _viewportH;
            // DOM metrics feed tooltips/task manager only — recompute at 1s,
            // not on every animation-frame layout.
            if (_cachedDomCount == 0 || Environment.TickCount64 - _lastCountTick >= 1000)
            {
                _lastCountTick = Environment.TickCount64;
                _cachedDomCount = CountDocNodes(_document);
                _cachedBoxCount = CountLayoutBoxes(_document);
            }
            _dlDirty = true;
            // A relayout can always move pixels, whatever the animation sample said.
            _paintVisible = true;
            rebuilt = true;
        }

        if (_animTrace)
        {
            t1 = Stopwatch.GetTimestamp();
            _stStyleMs = 0.875 * _stStyleMs + 0.125 * ((tStyle - t0) * 1000.0 / Stopwatch.Frequency);
            _stAnimMs = 0.875 * _stAnimMs + 0.125 * ((t1 - tStyle) * 1000.0 / Stopwatch.Frequency);
        }

        if (_dlDirty)
        {
            _stDl++;
            var visitor = new PaintVisitor(0, _typefaceCache, _imageCache,
                PageEnvironment.FontFamilies, _baseUrl, _viewportW, _viewportH);
            visitor.PhysicalScale = _dpi * _res;
            // No culling: the display list is scroll-invariant, so pure
            // scroll frames skip the paint walk entirely (raster only).
            visitor.SetFocusedElement(_focused);
            visitor.SetPressedButton(_pressedButton);
            visitor.SetSelectDropdown(_activeSelect, _dropdownRect, _optionRects, _hoverOption);
            if (_focused != null && _focused.IsTextEditable)
                visitor.SetInputState(_caret, _selStart, _showCursor, _imeComposing, _imeComposition, _imeCursorPos);
            // Page selection is a document-level concept, independent of the input caret
            // state above — the paint walk tints the anchored node/offset range.
            if (_hasSelection && _selAnchor.Node != null && _selFocus.Node != null)
                visitor.SetSelectionRange(_selAnchor.Node, _selAnchor.Offset, _selFocus.Node, _selFocus.Offset);
            visitor.VisitDocumentStacking(_document);
            var dl = visitor.GetDisplayList();
            dl.SortByZIndex();
            _cachedDl = dl;
            _cachedBg = visitor.ViewBackgroundColor;
            _dlSerial++;
            _dlDirty = false;
            bool visible = _paintVisible;
            _paintVisible = false;
            if (visible) _sink.RequestFrame();
            if (_animTrace)
            {
                long t2 = Stopwatch.GetTimestamp();
                _stDlMs = 0.875 * _stDlMs + 0.125 * ((t2 - t1) * 1000.0 / Stopwatch.Frequency);
                try
                {
                    // Per-stage frame cost, so a slow animation can be attributed to
                    // style, to sampling, to the paint walk or to the raster instead
                    // of guessed at. style reads 0 on an animation frame, which is the
                    // point: the host skips it when only an animation changed.
                    File.AppendAllText("upbrowser_anim_live.log",
                        $"tab{_id} PIPE style={_stStyleMs:F1} anim={_stAnimMs:F1} dl={_stDlMs:F1}ms\n");
                }
                catch { }
            }
            rebuilt = true;
        }

        return rebuilt;
    }

    /// <summary>Drain JS timers/microtasks, fold engine-driven dirt, report page metadata and blink the caret.</summary>
    public void PumpJs()
    {
        try
        {
            _js.SetWindowSize((int)_viewportW, (int)_viewportH);
            var jsInt = _js.IntegrationService;
            jsInt?.ProcessTimers();
            jsInt?.MicrotaskQueue.DrainMicrotasks();
            if (_js.HasTimers)
                _js.TickTimers();

            if (_js.NeedsReLayout)
            {
                _stRelayout++;
                if (_js.DirtyTrace != null) _sink.ReportDirtyTrace(_js.DirtyTrace);
                _js.ClearDirty();
                MarkLayout();
            }

            var t = _document?.Title ?? "";
            if (t != _title)
            {
                _title = t;
                _sink.ReportTitle(t);
            }

            // Caret blink for the focused field.
            if (_focused != null && Environment.TickCount64 - _blinkTick >= 530)
            {
                _blinkTick = Environment.TickCount64;
                _showCursor = !_showCursor;
                MarkPaint();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PageEngine {_id}] pump: {ex.Message}");
        }
    }

    /// <summary>Scroll requested by the page (scrollTo/scrollBy): clamped at the origin and
    /// echoed to the host so its scrollbar follows.</summary>
    public void SetScroll(float x, float y)
    {
        float nx = Math.Max(0, x);
        float ny = Math.Max(0, y);
        if (Math.Abs(nx - _scrollX) < 0.5f && Math.Abs(ny - _scrollY) < 0.5f) return;
        _scrollX = nx; _scrollY = ny;
        _sink.RequestFrame();
        NoteScrollMoved();
        // Keep the host's scrollbar in sync with engine-initiated scrolls.
        _sink.ReportScrollChanged(_scrollX, _scrollY);
    }

    /// <summary>Window scroll pushed by the host (its own scrollbar): taken as-is, no clamp
    /// and no echo — the host is already there.</summary>
    public void SetScrollFromHost(float x, float y)
    {
        if (Math.Abs(x - _scrollX) > 0.5f || Math.Abs(y - _scrollY) > 0.5f)
        {
            _scrollX = x; _scrollY = y;
            _sink.RequestFrame(); // cull band follows the viewport
            NoteScrollMoved();
        }
    }

    private static int CountDocNodes(Node node)
    {
        int count = node is Element ? 1 : 0;
        foreach (var child in node.Children)
            count += CountDocNodes(child);
        return count;
    }

    private static int CountLayoutBoxes(Node node)
    {
        int count = (node is Element el && el.LayoutBox != null) ? 1 : 0;
        foreach (var child in node.Children)
            count += CountLayoutBoxes(child);
        return count;
    }

    public void Dispose()
    {
        _js.Dispose();
    }
}
