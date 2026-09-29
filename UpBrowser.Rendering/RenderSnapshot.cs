using System.Diagnostics;
using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Fonts;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance.Resources;

namespace UpBrowser.Rendering;

/// <summary>
/// Result of comparing two rendered bitmaps.
/// </summary>
public sealed class SnapshotDiff
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int TotalPixels => Width * Height;
    public int DifferingPixels { get; init; }
    public int MaxChannelDelta { get; init; }
    public bool SizeMismatch { get; init; }

    public double DifferingRatio => TotalPixels == 0 ? 0 : (double)DifferingPixels / TotalPixels;

    public override string ToString() => SizeMismatch
        ? $"size mismatch ({Width}x{Height})"
        : $"{DifferingPixels}/{TotalPixels} px differ ({DifferingRatio * 100:F3}%), max channel delta {MaxChannelDelta}";
}

/// <summary>
/// Headless rasterization entry point for the visual-regression harness.
///
/// Runs the same pipeline as the interactive browser shell - parse -&gt; style -&gt;
/// layout, then page scripts (inline + external) with a bounded event-loop
/// settle window, a final full layout pass, and only then paint - without
/// creating a window. The resulting display list is rasterized into an
/// off-screen surface so page output is capturable as a deterministic PNG that
/// matches what the on-screen browser shows for the same markup.
/// </summary>
public static class RenderSnapshot
{
    private static bool _initialized;
    private static readonly object InitLock = new();

    /// <summary>
    /// Wall-clock budget for pumping timers/microtasks after the page's scripts
    /// run, mirroring the window the live browser gives a freshly loaded page.
    /// setInterval-style loops are capped by this deadline.
    /// </summary>
    private const int ScriptSettleBudgetMs = 2000;

    /// <summary>
    /// Minimum settle time before early-exit checks kick in, so fetch()-style
    /// continuations that register no timer still get a chance to land.
    /// </summary>
    private const int MinScriptSettleMs = 150;

    /// <summary>
    /// Install the shared text-measurement / font services that the layout and
    /// paint stages require. Normally done by the application shell; the headless
    /// path has to do it explicitly.
    /// </summary>
    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            FontManager.Initialize();
            TextMeasurer.Instance ??= new SkiaTextMeasurer();
            _initialized = true;
        }
    }

    /// <summary>
    /// A document that has been taken all the way to "ready to paint" at a pinned
    /// animation instant: scripts run, style is resolved, animations and
    /// transitions are sampled, and layout reflects the sampled values.
    /// </summary>
    public sealed class PreparedPage
    {
        public required DocumentManager.DocumentLoadResult Load { get; init; }
        public required ImageCache ImageCache { get; init; }
        public required Element Root { get; init; }
        public required UpBrowser.Core.Dom.Animations.CssAnimationEngine? Animations { get; init; }
        /// <summary>What the last <c>Update</c> reported for the captured frame.</summary>
        public required UpBrowser.Core.Dom.Animations.AnimationUpdateResult UpdateResult { get; init; }
        public required double TimeMs { get; init; }
        public required bool HasScripts { get; init; }
    }

    /// <summary>
    /// Load <paramref name="html"/> and run it forward to the point where a frame
    /// could be painted, pinned to <paramref name="animationTimeMs"/> on the
    /// document timeline.
    ///
    /// The order matters and is the whole point of this helper: style resolution
    /// produces the cascaded values, the animation engine samples every effect and
    /// writes the result into those same styles, and only then does layout run.
    /// A page's animation state is therefore a pure function of the markup, so the
    /// PNG, the layout dump and the display-list dump all describe the same frame.
    /// </summary>
    public static PreparedPage Prepare(
        string html,
        int width,
        int height,
        string? baseUrl,
        float dpiScale,
        bool runScripts,
        double animationTimeMs,
        Action<Element, UpBrowser.Core.Dom.Event>? eventSink = null)
    {
        EnsureInitialized();

        // The whole preparation runs under one pinned clock. The document
        // timeline is created inside that scope, so its origin is the pinned
        // instant and a capture is reproducible; freezing for the whole body
        // rather than per-phase also means a timeline can never be built against
        // wall-clock time by accident.
        using var clock = UpBrowser.Core.Dom.Animations.AnimationClock.Scope(animationTimeMs);

        // Layout runs inside LoadHtmlAsync, so the intrinsic-size seam for
        // replaced elements must be wired before the document is laid out.
        var imageCache = new ImageCache();
        PaintVisitor.InstallReplacedIntrinsicSizes(imageCache, baseUrl);

        // Parse -> style -> layout: the exact entry point the browser uses.
        var documentManager = new DocumentManager();
        var load = documentManager
            .LoadHtmlAsync(html, baseUrl, width, height, dpiScale)
            .GetAwaiter()
            .GetResult();

        var styleComputer = load.StyleComputer;
        var root = load.Document.DocumentElement ?? load.Document.Body;
        var keyframes = styleComputer?.CollectKeyframeRules();

        // Style resolution and layout are two separate halves of a frame.
        void ResolveStyles()
        {
            if (styleComputer == null) return;
            // Script execution marks the page dirty (MarkDirty -> NeedsReLayout),
            // which makes the shell recompute styles over the mutated DOM before
            // laying out again (RenderFrame's _pendingRelayout branch).
            try
            {
                styleComputer.ComputeStyles(load.Document, width, height);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[snapshot] Style error: {ex.Message}");
            }
        }

        void RunLayout()
        {
            // The shell always re-runs layout after loading (BuildDisplayListImpl),
            // so boxes reflect any script-driven DOM changes. A fresh engine mirrors
            // that non-incremental pass, including the device scale the raster canvas
            // is drawn at - line-box rounding happens on the device grid, so leaving
            // the scale out would lay the page out for a different display.
            try
            {
                new LayoutEngine().Layout(load.Document, width, height, dpiScale);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[snapshot] Layout error: {ex.Message}");
            }
        }

        // Prime the animation engine on the pre-script style. A transition may
        // not start on an element's very first style, so this tick records the
        // before-change style that a later change is measured against.
        var animations = root != null
            ? new UpBrowser.Core.Dom.Animations.CssAnimationEngine { EventSink = eventSink }
            : null;
        Action<Element, UpBrowser.Core.Dom.Event>? record = eventSink;

        if (animations != null)
        {
            animations.Update(root, keyframes, suppressEvents: true);
        }

        // Execute the page's scripts like ApplyLoadedHtml/RunPageScripts does:
        // inline + external scripts against a fresh engine, DOMContentLoaded,
        // then a bounded event-loop settle so JS-driven DOM mutations are on
        // screen just as they are in the live browser before its first paint.
        // Pages without <script> skip the engine entirely - starting the JS
        // host costs a process spawn we don't need for static markup.
        bool hasScripts = runScripts && CollectScriptElements(load.Document).Count > 0;
        var jsEngine = hasScripts
            ? RunPageScripts(load.Document, baseUrl, width, height, dpiScale)
            : null;

        // Once a page has a script engine, animation events also reach its
        // listeners, exactly as they do in the interactive shell. A caller
        // supplied sink keeps receiving them, so a debug capture can watch the
        // same stream the page's own listeners see.
        if (animations != null && jsEngine != null)
        {
            var captured = record;
            animations.EventSink = (el, evt) =>
            {
                captured?.Invoke(el, evt);
                DispatchAnimationEvent(jsEngine, el, evt);
            };
        }

        ResolveStyles();
        RunLayout();

        // Animations and transitions are part of the frame, not an afterthought.
        var updateResult = default(UpBrowser.Core.Dom.Animations.AnimationUpdateResult);
        if (animations != null && root != null)
        {
            animations.Timeline.SetCurrentTime(0);
            updateResult = animations.Update(root, keyframes);

            // Fast-forward in frame-sized steps, re-resolving style between
            // each so a live transition observes a real before/after sequence
            // rather than one instantaneous jump.
            for (double t = AnimationPipeline.FrameIntervalMs; t < animationTimeMs; t += AnimationPipeline.FrameIntervalMs)
            {
                ResolveStyles();
                animations.Timeline.SetCurrentTime(t);
                updateResult = animations.Update(root, keyframes);
                RunLayout();
            }

            // The frame that gets consumed: style, then animation, then layout.
            ResolveStyles();
            animations.Timeline.SetCurrentTime(animationTimeMs);
            updateResult = animations.Update(root, keyframes);
            RunLayout();
        }

        return new PreparedPage
        {
            Load = load,
            ImageCache = imageCache,
            Root = root!,
            Animations = animations,
            UpdateResult = updateResult,
            TimeMs = animationTimeMs,
            HasScripts = hasScripts,
        };
    }

    /// <summary>
    /// Render <paramref name="html"/> at the given viewport size and return the
    /// rasterized bitmap. The caller owns the returned bitmap.
    ///
    /// <paramref name="animationTimeMs"/> is the instant the document timeline
    /// is evaluated at. It defaults to 0 and the shared clock is frozen for the
    /// duration of the capture, so a page's animation and transition state is a
    /// pure function of the markup: the same file always produces the same
    /// pixels no matter how long the process took to get there.
    /// </summary>
    public static SKBitmap Capture(
        string html,
        int width,
        int height,
        string? baseUrl = null,
        float dpiScale = 1f,
        SKColor? background = null,
        bool runScripts = true,
        double animationTimeMs = 0)
    {
        var page = Prepare(html, width, height, baseUrl, dpiScale, runScripts, animationTimeMs);
        var load = page.Load;

        var visitor = new PaintVisitor(
            contentOffsetY: 0,
            sharedTypefaceCache: null,
            sharedImageCache: page.ImageCache,
            fontFamilies: SKFontManager.Default.FontFamilies.ToArray(),
            baseUrl: baseUrl,
            viewportWidth: width,
            viewportHeight: height);
        visitor.SetSkipInputTextOverlay(true);

        visitor.VisitDocumentStacking(load.Document);

        var displayList = visitor.GetDisplayList();
        displayList.SortByZIndex();

        // Rasterize at device resolution exactly like SkiaRenderer does for the
        // on-screen window: physical-size bitmap with a DPI-scaled canvas.
        int pixelWidth = Math.Max(1, (int)(width * dpiScale));
        int pixelHeight = Math.Max(1, (int)(height * dpiScale));
        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale(dpiScale, dpiScale);
            canvas.Clear(background ?? SKColors.White);
            displayList.Execute(canvas);
            canvas.Flush();
        }

        return bitmap;
    }

    /// <summary>
    /// Headless equivalent of BrowserApp.RunPageScripts: loads the document into
    /// the same JS engine bootstrap the UI process uses, executes scripts in
    /// document order, fires DOMContentLoaded, then pumps timers/microtasks for a
    /// bounded window so setTimeout/fetch-driven DOM mutations land before
    /// rasterization.
    /// </summary>
    /// <remarks>
    /// The shell lets <c>EngineProcessManager</c> start the engine host in
    /// the background because a live page outlives the startup latency anyway.
    /// A headless capture does not have that luxury - scripts would run against
    /// a disconnected engine and their DOM changes would silently vanish. So the
    /// adapter is started synchronously here; on any failure we fall back to the
    /// null adapter, matching how the shell degrades when no host is available.
    /// </remarks>
    private static JavaScriptEngine? RunPageScripts(Document document, string? baseUrl, int width, int height, float dpiScale)
    {
        JavaScriptEngine jsEngine;
        try
        {
            jsEngine = new JavaScriptEngine(CreateReadyAdapter());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[snapshot] JS engine unavailable, skipping scripts: {ex.Message}");
            return null;
        }

        jsEngine.SetWindowSize(width, height);

        // Same window-property wiring the app shell installs so pages querying
        // innerWidth / innerHeight / devicePixelRatio observe the snapshot's
        // viewport instead of engine defaults.
        if (jsEngine.Builtins != null)
        {
            jsEngine.Builtins.GetInnerWidth = () => width;
            jsEngine.Builtins.GetInnerHeight = () => height;
            jsEngine.Builtins.GetDevicePixelRatio = () => dpiScale;
            jsEngine.Builtins.GetScrollX = () => 0;
            jsEngine.Builtins.GetScrollY = () => 0;
        }

        jsEngine.LoadDocument(document);

        foreach (var script in CollectScriptElements(document))
        {
            var type = script.GetAttribute("type");
            if (!string.IsNullOrEmpty(type) && type != "text/javascript" && type != "application/javascript" && type != "module")
                continue;

            var src = script.GetAttribute("src");
            if (!string.IsNullOrEmpty(src))
            {
                var absoluteUrl = ResolveUrl(src, baseUrl);
                if (absoluteUrl == null)
                {
                    Console.WriteLine($"[snapshot] Invalid script URL: {src}");
                    continue;
                }

                try
                {
                    var fetcher = new StreamingHttpFetcher();
                    var resp = fetcher.FetchAsync(new ResourceRequest
                    {
                        Url = absoluteUrl,
                        Kind = ResourceKind.Script,
                        Priority = ResourcePriority.High,
                        Timeout = TimeSpan.FromSeconds(10),
                    }).GetAwaiter().GetResult();
                    jsEngine.Execute(System.Text.Encoding.UTF8.GetString(resp.Body));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[snapshot] Failed to load script '{src}': {ex.Message}");
                }
            }
            else
            {
                var code = script.TextContent;
                if (!string.IsNullOrWhiteSpace(code))
                    jsEngine.Execute(code);
            }
        }

        jsEngine.IntegrationService?.FireDOMContentLoaded();

        SettleEventLoop(jsEngine);

        return jsEngine;
    }

    /// <summary>
    /// Deliver an animation or transition event to the page's script listeners,
    /// using the same <c>ScriptEvent</c> shape the interactive shell dispatches so
    /// a handler written against <c>animationend</c> behaves identically in a
    /// snapshot and on screen.
    /// </summary>
    private static void DispatchAnimationEvent(JavaScriptEngine jsEngine, Element element, UpBrowser.Core.Dom.Event evt)
    {
        try
        {
            var host = jsEngine.GetDispatchHost(element);
            switch (evt)
            {
                case UpBrowser.Core.Dom.AnimationEvent anim:
                    jsEngine.DispatchEvent(element, new ScriptEvent(anim.Type, host)
                    {
                        bubbles = false,
                        cancelable = false,
                        animationName = anim.AnimationName,
                        elapsedTime = anim.ElapsedTime,
                        pseudoElement = anim.PseudoElement ?? "",
                        currentTime = anim.CurrentTime,
                    });
                    break;
                case UpBrowser.Core.Dom.TransitionEvent trans:
                    jsEngine.DispatchEvent(element, new ScriptEvent(trans.Type, host)
                    {
                        bubbles = false,
                        cancelable = false,
                        propertyName = trans.PropertyName,
                        elapsedTime = trans.ElapsedTime,
                        pseudoElement = trans.PseudoElement ?? "",
                        currentTime = trans.CurrentTime,
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[snapshot] animation event dispatch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts a remote JS engine adapter synchronously so it is guaranteed ready
    /// (process up, IPC handshake complete) before any page script executes.
    /// </summary>
    private static IJavaScriptEngineAdapter CreateReadyAdapter()
    {
#if USE_MULTIPLE_JS_ENGINE
        var engineType = JsEngineConfig.EffectiveEngineType;
        int pid = Environment.ProcessId;
        int channel = Interlocked.Increment(ref _snapshotChannelCounter);
        var adapter = new RemoteJsEngineAdapter($"UpBrowser_JS_{pid:x8}_snap_{channel}", engineType.ToString(), -1);
        try
        {
            adapter.StartAsync().GetAwaiter().GetResult();
        }
        catch
        {
            adapter.Dispose();
            throw;
        }
        Console.WriteLine($"[snapshot] JS engine ready ({engineType})");
        return adapter;
#else
        // Single-process mode: the built-in Jint engine is ready immediately.
        return new JintEngineAdapter();
#endif
    }

#if USE_MULTIPLE_JS_ENGINE
    private static int _snapshotChannelCounter;
#endif

    /// <summary>
    /// Quiet-window length: how long the page must stay mutation-free before the
    /// settle loop concedes no more work is coming.
    /// </summary>
    private const int QuietSettleMs = 250;

    /// <summary>
    /// Pumps the event loop until pending timers/microtasks drain or the wall-
    /// clock budget expires. Mirrors the frames between navigation and the first
    /// painted frame in the interactive shell.
    /// </summary>
    /// <remarks>
    /// Remote-engine timers are scheduled inside <c>RemoteJsEngineAdapter</c>
    /// (a fire-and-forget Task.Delay per setTimeout), so they are invisible to the
    /// engine's own timer queues. Activity is therefore tracked via
    /// <see cref="JavaScriptEngine.NeedsReLayout"/> - every JS-driven DOM/style
    /// mutation funnels through MarkDirty - and the loop keeps pumping until the
    /// page has been quiet for <see cref="QuietSettleMs"/>.
    /// </remarks>
    private static void SettleEventLoop(JavaScriptEngine jsEngine)
    {
        var integration = jsEngine.IntegrationService;
        var sw = Stopwatch.StartNew();
        var deadline = TimeSpan.FromMilliseconds(ScriptSettleBudgetMs);
        var minSettle = TimeSpan.FromMilliseconds(MinScriptSettleMs);
        var quietWindow = TimeSpan.FromMilliseconds(QuietSettleMs);
        DateTime lastActivity = DateTime.UtcNow;

        while (sw.Elapsed < deadline)
        {
            // TickTimers also resolves completed fetch() results at engine level.
            jsEngine.TickTimers();
            integration?.ProcessTimers();
            integration?.MicrotaskQueue.DrainMicrotasks();

            bool queuePending =
                jsEngine.HasTimers ||
                integration is not null && (
                    integration.TimerQueue.TimerCount > 0 ||
                    integration.MicrotaskQueue.PendingCount > 0);

            if (queuePending)
                lastActivity = DateTime.UtcNow;

            // Any DOM/style mutation from a late timer callback lands here.
            if (jsEngine.NeedsReLayout)
            {
                lastActivity = DateTime.UtcNow;
                jsEngine.ClearDirty();
            }

            if (!queuePending && sw.Elapsed >= minSettle && DateTime.UtcNow - lastActivity >= quietWindow)
                break;

            Thread.Sleep(10);
        }

        integration?.MicrotaskQueue.DrainMicrotasks();
    }

    private static List<Element> CollectScriptElements(Document document)
    {
        var scripts = new List<Element>();
        if (document.DocumentElement == null) return scripts;

        // Breadth-first from the root, same traversal order as the app shell.
        var queue = new Queue<Element>();
        queue.Enqueue(document.DocumentElement);
        while (queue.Count > 0)
        {
            var el = queue.Dequeue();
            if (el.TagName.Equals("script", StringComparison.OrdinalIgnoreCase))
                scripts.Add(el);
            foreach (var child in el.Children)
                if (child is Element childEl)
                    queue.Enqueue(childEl);
        }
        return scripts;
    }

    private static string? ResolveUrl(string url, string? baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:") || url.StartsWith("blob:"))
            return url;

        if (url.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + url;
            return "http:" + url;
        }

        if (string.IsNullOrEmpty(baseUrl)) return null;
        try
        {
            var baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/');
            return new Uri(baseUri, url).ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// Render <paramref name="html"/> and write the result to <paramref name="outputPath"/> as a PNG.
    /// </summary>
    public static void CaptureToFile(
        string html,
        string outputPath,
        int width,
        int height,
        string? baseUrl = null,
        float dpiScale = 1f,
        double animationTimeMs = 0)
    {
        using var bitmap = Capture(html, width, height, baseUrl, dpiScale, animationTimeMs: animationTimeMs);
        WritePng(bitmap, outputPath);
    }

    /// <summary>
    /// Load an HTML file from disk, render it, and write a PNG next to the requested path.
    /// The file's own directory becomes the base URL so relative resources resolve.
    /// </summary>
    public static void CaptureFileToFile(
        string htmlPath,
        string outputPath,
        int width,
        int height,
        float dpiScale = 1f,
        double animationTimeMs = 0)
    {
        var full = Path.GetFullPath(htmlPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;
        CaptureToFile(html, outputPath, width, height, baseUrl, dpiScale, animationTimeMs);
    }

    public static void WritePng(SKBitmap bitmap, string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Compare two PNG files pixel by pixel. Pixels whose per-channel difference
    /// exceeds <paramref name="tolerance"/> count as differing. When
    /// <paramref name="diffPath"/> is supplied, a visualization is written where
    /// differing pixels are marked in magenta over a dimmed copy of the expected
    /// image.
    /// </summary>
    public static SnapshotDiff Compare(
        string expectedPath,
        string actualPath,
        string? diffPath = null,
        int tolerance = 0)
    {
        using var expected = SKBitmap.Decode(expectedPath)
            ?? throw new InvalidOperationException($"Cannot decode '{expectedPath}'.");
        using var actual = SKBitmap.Decode(actualPath)
            ?? throw new InvalidOperationException($"Cannot decode '{actualPath}'.");

        return Compare(expected, actual, diffPath, tolerance);
    }

    public static SnapshotDiff Compare(
        SKBitmap expected,
        SKBitmap actual,
        string? diffPath = null,
        int tolerance = 0)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new SnapshotDiff
            {
                Width = expected.Width,
                Height = expected.Height,
                SizeMismatch = true,
            };
        }

        int width = expected.Width;
        int height = expected.Height;
        int differing = 0;
        int maxDelta = 0;

        SKBitmap? diff = diffPath != null ? new SKBitmap(width, height) : null;

        try
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var a = expected.GetPixel(x, y);
                    var b = actual.GetPixel(x, y);

                    int dr = Math.Abs(a.Red - b.Red);
                    int dg = Math.Abs(a.Green - b.Green);
                    int db = Math.Abs(a.Blue - b.Blue);
                    int da = Math.Abs(a.Alpha - b.Alpha);
                    int delta = Math.Max(Math.Max(dr, dg), Math.Max(db, da));

                    if (delta > maxDelta) maxDelta = delta;

                    bool differs = delta > tolerance;
                    if (differs) differing++;

                    if (diff != null)
                    {
                        diff.SetPixel(x, y, differs
                            ? new SKColor(255, 0, 255)
                            : new SKColor(
                                (byte)(a.Red / 4 + 191),
                                (byte)(a.Green / 4 + 191),
                                (byte)(a.Blue / 4 + 191)));
                    }
                }
            }

            if (diff != null && diffPath != null)
                WritePng(diff, diffPath);
        }
        finally
        {
            diff?.Dispose();
        }

        return new SnapshotDiff
        {
            Width = width,
            Height = height,
            DifferingPixels = differing,
            MaxChannelDelta = maxDelta,
        };
    }
}
