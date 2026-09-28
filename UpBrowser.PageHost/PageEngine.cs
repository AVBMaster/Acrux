using SkiaSharp;
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

    private Document? _document;
    private string _baseUrl = "";
    private string _title = "";
    private float _viewportW = 1024, _viewportH = 768;
    private float _scrollX, _scrollY;
    private float _contentW, _contentH;
    // Three-level dirty chain: layout ⇒ dl ⇒ raster; scroll/hover-free changes take cheaper paths.
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
    public bool PipelineWanted => _layoutDirty || _dlDirty;

    /// <summary>Milliseconds until the next JS timer is due, or <see cref="int.MaxValue"/>.</summary>
    public int NextTimerDelayMs => _js.IntegrationService?.NextTimerDelayMs() ?? int.MaxValue;

    // Diagnostics snapshot (see the heartbeat in the shell's tab host).
    public long RelayoutRequestCount => _stRelayout;
    public long LayoutPassCount => _stLayout;
    public long DisplayListCount => _stDl;

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
            PaintVisitor.InstallReplacedIntrinsicSizes(_imageCache, baseUrl);

            var load = _docManager.LoadHtmlAsync(html, baseUrl, _viewportW, _viewportH, _dpi)
                .GetAwaiter().GetResult();

            _document = load.Document;
            _baseUrl = baseUrl;
            _styleComputer = load.StyleComputer;
            _incremental = null; // fresh node tree — drop the layout cache
            _layoutCache.Clear();
            _scrollX = _scrollY = 0;
            _hoveredElement = null;
            _hoverRulesDoc = null;
            _focused = null;
            _pressedButton = null;
            _selStart = -1;
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
        if (!TryEditable(out var value, out var readOnly))
        {
            // Not editing: pages may still want keypress events on document.
            try { if (_document?.Body != null) DispatchSimple(_document.Body, "keypress"); } catch { }
            return;
        }
        if (readOnly || _focused == null) return;
        _caret = Math.Clamp(_caret, 0, value.Length);
        if (_selStart >= 0 && _selStart != _caret)
        {
            int a = Math.Min(_selStart, _caret), b = Math.Max(_selStart, _caret);
            value = value[..a] + c + value[b..];
            _caret = a + 1;
            _selStart = -1;
        }
        else
        {
            value = value[.._caret] + c + value[_caret..];
            _caret++;
        }
        _focused.Value = value;
        _showCursor = true;
        _blinkTick = Environment.TickCount64;
        try { DispatchSimple(_focused, "input"); } catch { }
        MarkPaint();
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
    }

    private void HandleMove(float x, float y)
    {
        if (_document == null) return;

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

    private static void FindFirstLastTextNodes(Node? node, ref TextNode? first, ref TextNode? last)
    {
        if (node == null) return;
        if (node is TextNode tn)
        {
            first ??= tn;
            last = tn;
        }
        foreach (var child in node.Children)
            FindFirstLastTextNodes(child, ref first, ref last);
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
                    if (CollectTextBetweenRecursive(el, startNode, endNode, ref collecting, sb))
                        return true;
                }
                continue;
            }

            if (child is TextNode tn)
            {
                var text = tn.TextContent;
                if (!string.IsNullOrEmpty(text))
                    sb.Append(text);
            }
            else if (child is Element el)
            {
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

    // ==================== render ====================

    private void MarkPaint() { _dlDirty = true; _sink.RequestFrame(); }
    private void MarkLayout() { _layoutDirty = true; _dlDirty = true; _sink.RequestFrame(); }

    /// <summary>
    /// Run the layout and display-list stages once. Returns true when either rebuilt,
    /// which is when the presenter must expect a new display list to rasterize.
    /// Exceptions propagate to the host loop — it owns the retry pacing.
    /// </summary>
    public bool UpdatePipeline()
    {
        if (_document == null) return false;
        bool rebuilt = false;

        if (_layoutDirty)
        {
            _stLayout++;
            if (_styleComputer == null)
            {
                _styleComputer = new StyleComputer();
                _styleComputer.AddStylesheet(_docManager.GetUaStylesheet(), UpBrowser.Core.Css.Resolver.CascadeOrigin.UserAgent);
            }
            _styleComputer.ComputeStyles(_document, _viewportW, _viewportH);
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
            rebuilt = true;
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
            if (_focused != null && _focused.IsTextEditable)
                visitor.SetInputState(_caret, _selStart, _showCursor, false, "", 0);
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
            _sink.RequestFrame();
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
