using System.Runtime.InteropServices;
using System.Reflection;
using SkiaSharp;
using Acrux.Core.Dom;
using Acrux.Core.Performance;

namespace Acrux.Rendering;

public class SkiaRenderer : IDisposable
{
    private SKSurface? _gpuSurface;
    private GRContext? _grContext;
    private bool _useGpu;
    private bool _gpuFailed;

    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private bool _disposed;
    private int _width;
    private int _height;
    private float _dpiScale = 1.0f;
    private float _resolutionScale = 1.0f;

    private DisplayList? _currentDisplayList;

    private SKPicture? _cachedPicture;
    private bool _pictureDirty = true;

    private TiledCompositor? _tiledCompositor;
    private bool _useTileCompositor;

    /// <summary>True when the tile compositor is active for the page pipeline.</summary>
    public bool TileCompositorActive => _useTileCompositor && _tiledCompositor != null;

    /// <summary>
    /// True when the tile compositor still has deferred raster work queued after
    /// this frame. BrowserApp must request another frame so the finishing tiles
    /// get composited; consumed (reset) by BrowserApp after each frame.
    /// </summary>
    public bool PendingTileRepaint { get; set; }

    private DirtyRegionManager? _dirtyManager;
    private bool _useDirtyRegions;

    // Performance-hub integration (optional). When set, the compositor routes its
    // tile state and predictive pre-raster work through the shared infrastructure
    // and reports its byte usage to the memory pressure monitor.
    private Acrux.Core.Performance.PerformanceHub? _perfHub;
    private Acrux.Core.Performance.Memory.MemoryBudget? _memoryBudget;
    private long _lastMemoryReportNanos;
    private float _lastScrollVx;
    private float _lastScrollVy;
    private long _lastScrollVelocityNanos;

    // OpenGL context for GPU backend
    private IntPtr _glDummyWindow;
    private IntPtr _glDC;
    private IntPtr _glRC;

    // EGL state (Linux)
    private IntPtr _eglDisplay = IntPtr.Zero;
    private IntPtr _eglConfig = IntPtr.Zero;
    private IntPtr _eglContext = IntPtr.Zero;
    private IntPtr _eglSurface = IntPtr.Zero;
    private GRGlInterface? _glInterface;

    // FPS counter
    private long _lastFrameTick = Environment.TickCount64;
    private double _currentFps;
    private SKTypeface? _fpsTypeface;

    // Rendering settings
    private RenderingSettings? _settings;
    private AntiAliasMode _currentAaMode = AntiAliasMode.Normal;

    public SKCanvas Canvas => _canvas!;
    public DisplayList? CurrentDisplayList => _currentDisplayList;
    public float DpiScale { get => _dpiScale; set => _dpiScale = value; }
    public float ResolutionScale
    {
        get => _resolutionScale;
        set
        {
            if (Math.Abs(_resolutionScale - value) > 0.01f)
            {
                _resolutionScale = Math.Clamp(value, 0.25f, 3.0f);
                _pictureDirty = true;
            }
        }
    }
    public int Width => _width;
    public int Height => _height;
    public int PhysicalWidth => (int)(_width * _dpiScale);
    public int PhysicalHeight => (int)(_height * _dpiScale);
    public bool UseGpu => _useGpu;
    public bool IsPageCacheValid => !_pictureDirty && _cachedPicture != null;
    public TiledCompositor? Compositor => _tiledCompositor;
    public RenderingSettings? Settings
    {
        get => _settings;
        set
        {
            if (_settings != null)
                _settings.OnChanged -= ApplySettings;
            _settings = value;
            if (_settings != null)
            {
                _settings.OnChanged += ApplySettings;
                ApplySettings();
            }
        }
    }

    private void ApplySettings()
    {
        if (_settings == null) return;

        bool resolutionChanged = Math.Abs(_resolutionScale - _settings.ResolutionScale) > 0.01f;
        _resolutionScale = _settings.ResolutionScale;
        _currentAaMode = _settings.AntiAliasing;
        _useDirtyRegions = _settings.DirtyRegions;

        // Text raster quality. Ambient (single-threaded render), read by
        // DrawTextOp.CreateFont at record/replay time.
        DrawTextOp.AntiAlias = _settings.AntiAliasing;
        DrawTextOp.UseSubpixelAA = _settings.AntiAliasing == AntiAliasMode.Subpixel;

        if (!_settings.DirtyRegions)
            _dirtyManager?.ClearDirtyRegions();

        if (!_settings.PictureCaching)
        {
            _cachedPicture?.Dispose();
            _cachedPicture = null;
        }

        _useTileCompositor = _settings.TileCompositor;
        if (_useTileCompositor)
        {
            if (_tiledCompositor == null || _tiledCompositor.TileSize != _settings.TileSize)
            {
                _tiledCompositor?.Dispose();
                _tiledCompositor = BuildCompositor(_settings);
            }
        }
        else
        {
            _tiledCompositor?.Dispose();
            _tiledCompositor = null;
        }

        // Invalidate page cache on any setting change that affects rendering
        _pictureDirty = true;

        // Handle GPU toggle via settings
        if (_settings.GpuAcceleration && !_useGpu && !_gpuFailed)
        {
            if (TryEnableGpu())
            {
                Console.WriteLine("[Settings] GPU acceleration enabled");
            }
            else
            {
                _gpuFailed = true;
                Console.WriteLine("[Settings] GPU acceleration failed, staying on CPU");
            }
        }
        else if (!_settings.GpuAcceleration && _useGpu)
        {
            DisableGpu();
            Console.WriteLine("[Settings] GPU acceleration disabled");
        }

        // Only recreate surface for GPU toggle or AA mode change (AA needs surface format)
        if (resolutionChanged) return;
        RecreateSurface();
    }

    public bool TrySetGpu(bool enable)
    {
        if (enable == _useGpu) return true;
        if (!enable)
        {
            DisableGpu();
            return true;
        }
        return TryEnableGpu();
    }

    private void DisableGpu()
    {
        if (!_useGpu) return;

        _gpuSurface?.Dispose();
        _gpuSurface = null;
        _grContext?.Dispose();
        _grContext = null;
        CleanupGlContext();
        _useGpu = false;

        _canvas?.Dispose();
        _canvas = null;
        CreateCpuBitmap(_width, _height);
        _pictureDirty = true;
        Console.WriteLine("[GPU] GPU acceleration disabled, switched to CPU");
    }

    /// <summary>
    /// Attempt to enable GPU acceleration via SkiaSharp GRContext.
    /// Creates a platform-appropriate OpenGL context, then uses GRContext.CreateGl().
    /// Falls back to CPU software rendering if GPU init fails.
    /// </summary>
    public bool TryEnableGpu()
    {
        if (_useGpu) return true;
        try
        {
            bool enabled;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                enabled = TryEnableGpuWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                enabled = TryEnableGpuLinux();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                enabled = TryEnableGpuMac();
            else
            {
                Console.WriteLine("[GPU] No GPU support for this platform");
                return false;
            }

            // The render surface belongs to the GL context, so build it now that one is
            // current — otherwise a re-enable would keep drawing into the CPU bitmap.
            if (enabled) RecreateSurface();
            return enabled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPU] Init failed: {ex.GetType().Name}: {ex.Message}");
            CleanupGlContext();
            return false;
        }
    }

    private bool TryEnableGpuWindows()
    {
        const uint WS_POPUP = 0x80000000;
        var hInstance = GetModuleHandleW(null);
        _glDummyWindow = CreateWindowExW(0, "STATIC", "",
            WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_glDummyWindow == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] Failed to create dummy window");
            return false;
        }

        _glDC = GetDC(_glDummyWindow);
        if (_glDC == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] Failed to get DC");
            CleanupGlWindow();
            return false;
        }

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = PFD_TYPE_RGBA,
            cColorBits = 32,
            cAlphaBits = 8,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = 0
        };

        var pixelFormat = ChoosePixelFormat(_glDC, ref pfd);
        if (pixelFormat == 0)
        {
            Console.WriteLine("[GPU] ChoosePixelFormat failed");
            CleanupGlWindow();
            return false;
        }

        if (!SetPixelFormat(_glDC, pixelFormat, ref pfd))
        {
            Console.WriteLine("[GPU] SetPixelFormat failed");
            CleanupGlWindow();
            return false;
        }

        _glRC = wglCreateContext(_glDC);
        if (_glRC == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] wglCreateContext failed");
            CleanupGlWindow();
            return false;
        }

        if (!wglMakeCurrent(_glDC, _glRC))
        {
            Console.WriteLine("[GPU] wglMakeCurrent failed");
            CleanupGlWindow();
            return false;
        }

        _grContext = GRContext.CreateGl();
        if (_grContext == null)
        {
            Console.WriteLine("[GPU] GRContext.CreateGl returned null");
            CleanupGlContext();
            return false;
        }

        _useGpu = true;
        Console.WriteLine("[GPU] OpenGL GPU acceleration enabled (Windows)");
        return true;
    }

    private bool TryEnableGpuLinux()
    {
        // Offscreen GL context: the renderer always reads the GPU surface back into a
        // BGRA buffer for the shell blit, so a 1x1 EGL pbuffer is enough — no X window
        // is needed, so this works on X11 and Wayland alike and in headless hosts.
        if (_eglHandle == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] libEGL not available on this system, using CPU");
            return false;
        }

        _eglDisplay = eglGetDisplay(EGL_DEFAULT_DISPLAY);
        if (_eglDisplay == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] eglGetDisplay failed, using CPU");
            return false;
        }
        if (!eglInitialize(_eglDisplay, out int major, out int minor))
        {
            Console.WriteLine($"[GPU] eglInitialize failed (err 0x{(long)eglGetError():X}), using CPU");
            _eglDisplay = IntPtr.Zero;
            return false;
        }
        Console.WriteLine($"[GPU] EGL {major}.{minor} display initialised");

        // Skia's own native GL interface is GLX-based and finds no context under EGL, so
        // assemble it ourselves from eglGetProcAddress. Desktop GL first, then GLES: some
        // drivers (ARM boards, Wayland-only stacks) expose ES configs only.
        if (TryCreateEglGlContext(EGL_OPENGL_API, GRGlInterface.CreateOpenGl)
            || TryCreateEglGlContext(EGL_OPENGL_ES_API, GRGlInterface.CreateGles))
        {
            _useGpu = true;
            Console.WriteLine("[GPU] OpenGL GPU acceleration enabled (Linux/EGL)");
            return true;
        }

        Console.WriteLine("[GPU] No usable EGL GL context, using CPU");
        CleanupGlContext();
        return false;
    }

    private bool TryCreateEglGlContext(int api, Func<GRGlGetProcedureAddressDelegate, GRGlInterface?> assembleInterface)
    {
        // The API must be bound before the config query: eglChooseConfig filters by the
        // currently bound client API. EGL_RENDERABLE_TYPE is left out because Mesa answers
        // it with EGL_BAD_ATTRIBUTE on several drivers.
        if (!eglBindAPI(api)) return false;

        int[] configAttribs =
        {
            EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
            EGL_RED_SIZE, 8,
            EGL_GREEN_SIZE, 8,
            EGL_BLUE_SIZE, 8,
            EGL_ALPHA_SIZE, 8,
            EGL_DEPTH_SIZE, 16,
            EGL_STENCIL_SIZE, 8,
            EGL_NONE
        };
        if (!eglChooseConfig(_eglDisplay, configAttribs, out IntPtr config, 1, out int numConfig)
            || config == IntPtr.Zero || numConfig == 0)
        {
            Console.WriteLine($"[GPU] eglChooseConfig found no pbuffer config for api 0x{api:X} (err 0x{(long)eglGetError():X})");
            return false;
        }

        IntPtr context = eglCreateContext(_eglDisplay, config, IntPtr.Zero, new[] { EGL_NONE });
        if (context == IntPtr.Zero)
        {
            Console.WriteLine($"[GPU] eglCreateContext failed for api 0x{api:X} (err 0x{(long)eglGetError():X})");
            return false;
        }

        int[] pbufferAttribs = { EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE };
        IntPtr surface = eglCreatePbufferSurface(_eglDisplay, config, pbufferAttribs);
        if (surface == IntPtr.Zero)
        {
            Console.WriteLine($"[GPU] eglCreatePbufferSurface failed (err 0x{(long)eglGetError():X})");
            eglDestroyContext(_eglDisplay, context);
            return false;
        }

        if (!eglMakeCurrent(_eglDisplay, surface, surface, context))
        {
            Console.WriteLine($"[GPU] eglMakeCurrent failed (err 0x{(long)eglGetError():X})");
            eglDestroySurface(_eglDisplay, surface);
            eglDestroyContext(_eglDisplay, context);
            return false;
        }

        _eglConfig = config;
        _eglContext = context;
        _eglSurface = surface;

        _glInterface = assembleInterface(GetGlProcAddress);
        if (_glInterface == null || !_glInterface.Validate())
        {
            Console.WriteLine($"[GPU] GL interface assembly failed for api 0x{api:X}");
            _glInterface?.Dispose();
            _glInterface = null;
            return false;
        }

        // The context refs the interface, but keep it alive for the lifetime of the
        // GRContext rather than relying on SkiaSharp's ownership here.
        _grContext = GRContext.CreateGl(_glInterface);
        if (_grContext == null)
        {
            Console.WriteLine($"[GPU] GRContext.CreateGl returned null for api 0x{api:X}");
            _glInterface.Dispose();
            _glInterface = null;
            return false;
        }
        return true;
    }

    private bool TryEnableGpuMac()
    {
        Console.WriteLine("[GPU] GPU acceleration on macOS not yet implemented, using CPU");
        return false;
    }

    public void Initialize(int width, int height, bool enableDirtyRegions = true)
    {
        _width = width;
        _height = height;
        _pictureDirty = true;

        if (_useGpu)
        {
            MakeGlCurrent();
            CreateGpuSurface(width, height);
        }
        else
        {
            CreateCpuBitmap(width, height);
        }

        if (enableDirtyRegions)
        {
            _dirtyManager = new DirtyRegionManager();
            _useDirtyRegions = true;
        }
    }

    public void Resize(int width, int height)
    {
        if (_width == width && _height == height) return;
        _width = width;
        _height = height;
        _pictureDirty = true;

        if (_useGpu)
        {
            MakeGlCurrent();
            _gpuSurface?.Dispose();
            _gpuSurface = null;
            CreateGpuSurface(width, height);
        }
        else
        {
            _bitmap?.Dispose();
            _bitmap = null;
            CreateCpuBitmap(width, height);
        }

        if (_dirtyManager != null)
            _dirtyManager.Invalidate(new SKRect(0, 0, width, height));
    }

    private void RecreateSurface()
    {
        if (_width <= 0 || _height <= 0) return;

        if (_useGpu)
        {
            MakeGlCurrent();
            _gpuSurface?.Dispose();
            _gpuSurface = null;
            CreateGpuSurface(_width, _height);
        }
        else
        {
            _bitmap?.Dispose();
            _bitmap = null;
            CreateCpuBitmap(_width, _height);
        }

        _pictureDirty = true;
    }

    private void CreateGpuSurface(int width, int height)
    {
        int pw = (int)(width * _dpiScale);
        int ph = (int)(height * _dpiScale);
        var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
        _gpuSurface = SKSurface.Create(_grContext!, false, info);
        if (_gpuSurface == null)
        {
            // No current GL context on this thread (or a lost device): keep drawing on
            // the CPU bitmap instead of taking the whole renderer down.
            Console.WriteLine("[GPU] GPU surface creation failed, falling back to CPU");
            CreateCpuBitmap(width, height);
            return;
        }
        _canvas = _gpuSurface.Canvas;
        _canvas.Scale(_dpiScale, _dpiScale);
    }

    private void CreateCpuBitmap(int width, int height)
    {
        int pw = (int)(width * _dpiScale);
        int ph = (int)(height * _dpiScale);
        _bitmap = new SKBitmap(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
        _canvas = new SKCanvas(_bitmap);
        _canvas.Scale(_dpiScale, _dpiScale);
    }

    private void MakeGlCurrent()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (_eglDisplay != IntPtr.Zero && _eglContext != IntPtr.Zero && _eglSurface != IntPtr.Zero)
                eglMakeCurrent(_eglDisplay, _eglSurface, _eglSurface, _eglContext);
            return;
        }
        if (_glRC != IntPtr.Zero && _glDC != IntPtr.Zero)
            wglMakeCurrent(_glDC, _glRC);
    }

    public void SetDisplayList(DisplayList displayList)
    {
        _currentDisplayList = displayList;
        _tiledCompositor?.SetDisplayList(displayList);
    }

    /// <summary>
    /// Adopt a rebuilt display list whose change is confined to rects already
    /// region-invalidated on the compositor. The cache generation is preserved
    /// and the recorded picture is refreshed so synchronous/deferred tiles
    /// rasterize the new content. Returned a no-op without the tile compositor.
    /// </summary>
    public void AdoptRebuiltDisplayList(DisplayList displayList)
    {
        _currentDisplayList = displayList;
        _tiledCompositor?.AdoptRebuiltDisplayList(displayList);
    }

    /// <summary>
    /// Optional integration with the shared performance hub. When set, the
    /// tile compositor routes its tile-state through <c>hub.Tiles</c>, the
    /// predictive pre-raster work through <c>hub.PredictiveScheduler</c>, and
    /// its byte budget through <paramref name="memoryBudget"/>.
    /// </summary>
    public void AttachPerformanceHub(
        Acrux.Core.Performance.PerformanceHub hub,
        Acrux.Core.Performance.Memory.MemoryBudget? memoryBudget = null)
    {
        _perfHub = hub ?? throw new ArgumentNullException(nameof(hub));
        _memoryBudget = memoryBudget;
        if (_tiledCompositor != null)
        {
            _tiledCompositor.Dispose();
            _tiledCompositor = BuildCompositor(_settings);
        }
    }

    /// <summary>
    /// Update the compositor's notion of scroll velocity. The compositor feeds the
    /// vector into the predictive tile scheduler so tiles in the direction of
    /// travel are pre-rasterised while the user is still scrolling.
    /// </summary>
    public void ReportScrollVelocity(float vx, float vy)
    {
        _lastScrollVx = vx;
        _lastScrollVy = vy;
        _lastScrollVelocityNanos = Clock.NowNanos();
        // The predictive scheduler computes its look-ahead band from the viewport
        // handed to it, which is in physical device pixels. Scale the CSS-pixel
        // velocity so the extrapolated band lands in the same space.
        float ps = _dpiScale * (_settings?.ResolutionScale ?? 1.0f);
        _tiledCompositor?.UpdateScrollVelocity(vx * ps, vy * ps);
    }

    /// <summary>
    /// Invalidate a page-space rectangle. The compositor drops only the tiles
    /// that intersect this rect, so unaffected tiles (e.g. stable chrome, off-
    /// screen content) stay cached.
    /// </summary>
    public void InvalidatePageRect(SKRect pageRect)
    {
        _pictureDirty = true;
        if (_tiledCompositor != null)
            _tiledCompositor.InvalidateRect(pageRect);
        if (_dirtyManager != null && _useDirtyRegions)
            _dirtyManager.Invalidate(pageRect);
    }

    /// <summary>
    /// Synchronously rasterize all the tiles covering <paramref name="pageRect"/>
    /// so the region is cached before the next <see cref="RenderWithScroll"/> call
    /// and composites on the same frame. Used for region-scoped element-scroll
    /// repaints: deferring the scrolled container's tiles would push their update
    /// 1+ frames late and expose tearing/seams between updated and retained tiles
    /// mid-scroll.
    /// </summary>
    public void PrerasterizePageRect(SKRect pageRect)
    {
        if (!TileCompositorActive) return;
        float resScale = _settings?.ResolutionScale ?? 1.0f;
        float physicalScale = _dpiScale * resScale;
        _tiledCompositor.RasterizeRectSynchronous(pageRect, physicalScale);
    }

    /// <summary>
    /// Declare the rect that will be re-rasterized this frame so the recorded
    /// picture culls to the change instead of the whole viewport. See
    /// <see cref="TiledCompositor.SetChangeRegion"/>.
    /// </summary>
    public void SetChangeRegion(SKRect pageRect)
    {
        _tiledCompositor?.SetChangeRegion(pageRect);
    }

    public void InvalidatePageCache()
    {
        _pictureDirty = true;
        _tiledCompositor?.InvalidateAll();
    }

    public void Invalidate(SKRect rect)
    {
        if (_dirtyManager != null && _useDirtyRegions)
            _dirtyManager.Invalidate(rect);
        _pictureDirty = true;
        if (_tiledCompositor != null)
        {
            // Invalidate the tile grid only for tiles that intersect the dirty
            // rect.  In page-space coordinates the caller has already given us
            // a page rect, so we forward directly.
            _tiledCompositor.InvalidateRect(rect);
        }
    }

    public void InvalidateFull()
    {
        if (_dirtyManager != null)
            _dirtyManager.Invalidate(new SKRect(0, 0, _width, _height));
        _pictureDirty = true;
        _tiledCompositor?.InvalidateAll();
    }

    public void Render(DisplayList displayList)
    {
        _currentDisplayList = displayList;

        if (_useGpu) MakeGlCurrent();

        if (_useDirtyRegions && _dirtyManager != null)
        {
            var dirtyRects = _dirtyManager.GetDirtyRegions();
            if (dirtyRects.Count > 0)
            {
                RenderDirtyRegions(displayList, dirtyRects);
                return;
            }
        }

        Canvas.Clear(SKColors.White);
        _pictureDirty = true;
        CacheAndDrawPicture(displayList);
    }

    public void RenderWithResolutionScale(DisplayList displayList, float contentOffsetY, float viewportWidth, float viewportHeight)
    {
        _currentDisplayList = displayList;
        if (_useGpu) MakeGlCurrent();

        float resScale = _settings?.ResolutionScale ?? 1.0f;

        Canvas.Save();
        Canvas.ClipRect(new SKRect(0, contentOffsetY, viewportWidth, contentOffsetY + viewportHeight));
        Canvas.Scale(resScale, resScale);
        Canvas.Translate(0, contentOffsetY * (1f / resScale - 1f));

        Canvas.Clear(SKColors.White);
        CacheAndDrawPicture(displayList);

        Canvas.Restore();
    }

    public void RenderWithScroll(DisplayList displayList, float contentOffsetY, float scrollX, float scrollY, float viewportWidth, float viewportHeight, DisplayList? overlayList = null, SKColor? backgroundFill = null, bool interactiveScrollFrame = false, bool directDraw = false)
    {
        _currentDisplayList = displayList;

        if (displayList == null) return;

        if (_useGpu) MakeGlCurrent();

        var sw = Clock.NowNanos();

        float resScale = _settings?.ResolutionScale ?? 1.0f;

        Canvas.Save();

        float physicalScale = _dpiScale * resScale;

        // Clip to content area (logical coords in DPI-scaled space)
        Canvas.ClipRect(new SKRect(0, contentOffsetY, viewportWidth, contentOffsetY + viewportHeight));

        // Apply resolution scale only to page content, anchored at content origin
        Canvas.Scale(resScale, resScale);
        Canvas.Translate(0, contentOffsetY * (1f / resScale - 1f));
        Canvas.Translate(-scrollX, -scrollY);

        // directDraw (interactive resize drag): skip the tile compositor entirely
        // and take the single-picture path below, so every resize tick presents a
        // complete page instead of paying full tile-cache invalidation + raster.
        if (_useTileCompositor && _tiledCompositor != null && !directDraw)
        {
            // Tile compositor owns the transform stack and composites in physical
            // (device-pixel) space. The page-space viewport plus the page origin
            // (scroll + DPR + resolution anchoring) fully describe the mapping.
            //
            // The origin is kept EXACTLY fractional — never snapped. Snapping it
            // makes the whole page jump by up to half a device pixel every frame
            // the scroll velocity crosses the settle threshold, which reads as
            // tearing. The compositor blits tiles with Nearest sampling, so a
            // fractional origin shifts the texel grid instead of resampling it:
            // text stays hard-edged while sliding and needs no second raster.
            float originX = scrollX * physicalScale;
            float originY = scrollY * physicalScale + contentOffsetY * _dpiScale * (resScale - 1f);

            var physicalViewport = new SKRect(
                scrollX * physicalScale,
                (scrollY + contentOffsetY) * physicalScale,
                (scrollX + viewportWidth / resScale) * physicalScale,
                (scrollY + contentOffsetY + viewportHeight / resScale) * physicalScale);

            _tiledCompositor.DeferRasterization = true;
            _tiledCompositor.Render(Canvas, physicalViewport, physicalScale, originX, originY, displayList, backgroundFill);

            // Pre-rasterize the band in the direction of travel so fast scrolling
            // finds its tiles already cached (no white pop-in). The band is
            // velocity-driven; when the viewport is not moving it matches the
            // viewport and the pump is skipped.
            var band = _tiledCompositor.ComputePredictiveBand(physicalViewport);
            if (band != physicalViewport)
            {
                _tiledCompositor.SetPredictedViewport(band);
                _tiledCompositor.PumpPredictiveTiles(Canvas, TiledCompositor.MaxBackgroundTilesPerFrame);
            }
            else
            {
                _tiledCompositor.SetPredictedViewport(default);
            }

            // Fill the deferred-raster queue under a time budget (no synchronous
            // tile raster on the main thread), then request another frame while
            // the backlog remains or a tile finished so it gets composited. On an
            // interactive element-scroll frame the scrolled container was already
            // re-rasterized synchronously above, so skip the drain entirely — it
            // would only stall the frame; the retained surface plus the deferred
            // drain on subsequent frames composites the leftover page-scroll band.
            int filled = 0;
            if (!interactiveScrollFrame)
                filled = _tiledCompositor.RasterDeferred(Canvas);
            PendingTileRepaint = _tiledCompositor.HasPendingRasterWork || filled > 0;
        }
        else
        {
            bool useCaching = _settings?.PictureCaching ?? true;
            if (useCaching && !_pictureDirty && _cachedPicture != null)
            {
                Canvas.DrawPicture(_cachedPicture);
            }
            else
            {
                var contentRect = new SKRect(0, 0, Math.Max(viewportWidth, 10000f), Math.Max(viewportHeight, 10000f));
                var recorder = new SKPictureRecorder();
                var recordCanvas = recorder.BeginRecording(contentRect);
                displayList.Execute(recordCanvas, contentRect);
                _cachedPicture?.Dispose();
                _cachedPicture = recorder.EndRecording();
                _pictureDirty = false;

                Canvas.DrawPicture(_cachedPicture);
            }
        }

        // Composite input overlay (text/cursor/selection) in the same transformed context
        if (overlayList != null)
            overlayList.Execute(Canvas);

        Canvas.Restore();

        PipelineTimings.Composite.AddSample(Clock.NowNanos() - sw);
        ReportMemoryUsage();
    }

    private void ReportMemoryUsage()
    {
        if (_perfHub == null) return;
        long now = Clock.NowNanos();
        if (now - _lastMemoryReportNanos < Clock.NanosToMillis(250) * 1_000_000L) return;
        _lastMemoryReportNanos = now;
        long tileBytes = _tiledCompositor?.CachedBytes ?? 0;
        // We piggy-back on the pressure monitor's "current bytes" so the budget
        // responder can react to tile cache pressure. The full managed-bytes
        // report happens elsewhere; this is a tile-only signal.
        _perfHub.Registry.MemoryPressure.ReportUsage(tileBytes);
    }

    /// <summary>
    /// Build a tile compositor with the current settings. Wires the optional
    /// PerformanceHub integration so the cache can use the shared tile manager
    /// and predictive scheduler.
    /// </summary>
    private TiledCompositor BuildCompositor(RenderingSettings? settings)
    {
        int tileSize = settings?.TileSize ?? TiledCompositor.DefaultTileSize;
        int overscanRings = settings?.OverscanRings ?? 1;
        bool adaptive = settings?.AdaptiveTileSize ?? false;
        bool record = settings?.CompositorRecording ?? false;
        var dl = _currentDisplayList;
        return new TiledCompositor(
            tileSize: tileSize,
            overscanRings: overscanRings,
            adaptiveTileSize: adaptive,
            minTileSize: 128,
            maxTileSize: Math.Max(tileSize, 1024),
            recordCommands: record,
            displayList: dl,
            hubTiles: _perfHub?.Tiles,
            hubPredictor: _perfHub?.PredictiveScheduler,
            memoryBudget: _memoryBudget);
    }

    private void CacheAndDrawPicture(DisplayList displayList)
    {
        var contentRect = new SKRect(0, 0, 10000f, 10000f);
        var recorder = new SKPictureRecorder();
        var recordCanvas = recorder.BeginRecording(contentRect);
        displayList.Execute(recordCanvas, contentRect);
        _cachedPicture?.Dispose();
        _cachedPicture = recorder.EndRecording();
        Canvas.DrawPicture(_cachedPicture);
    }

    private void RenderDirtyRegions(DisplayList displayList, List<SKRect> dirtyRects)
    {
        Canvas.Clear(SKColors.White);

        foreach (var dirtyRect in dirtyRects)
        {
            Canvas.Save();
            Canvas.ClipRect(dirtyRect);

            var opsInRegion = displayList.GetOpsInRect(dirtyRect);
            foreach (var op in opsInRegion)
            {
                op.Execute(Canvas);
            }

            Canvas.Restore();
        }

        _dirtyManager!.ClearDirtyRegions();
        _pictureDirty = true;
    }

    public void TickFrame()
    {
        long now = Environment.TickCount64;
        double elapsed = now - _lastFrameTick;
        _lastFrameTick = now;

        if (elapsed <= 0 || elapsed > 1000) return;

        // Exponential smoothing for stable readings
        double instantFps = 1000.0 / elapsed;
        const double alpha = 0.05;
        _currentFps = _currentFps > 0
            ? alpha * instantFps + (1 - alpha) * _currentFps
            : instantFps;
    }

    public void RenderFpsCounter(SKCanvas canvas, float windowWidth, float windowHeight)
    {
        if (_settings == null || !_settings.ShowFps) return;

        _fpsTypeface ??= FontHelper.GetChineseTypeface() ?? SKTypeface.Default;

        using var font = new SKFont(_fpsTypeface, 13);
        // Drawn over a translucent black box, so grayscale AA avoids LCD fringing.
        font.Edging = SKFontEdging.Antialias;
        font.Subpixel = false;
        font.Hinting = FontHelper.CrispHinting(font.Typeface);
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 180),
            Style = SKPaintStyle.Fill
        };
        using var textPaint = new SKPaint
        {
            Color = _currentFps >= 55 ? SKColor.Parse("#4CAF50") :
                    _currentFps >= 30 ? SKColor.Parse("#FF07") :
                    SKColor.Parse("#F44336"),
            IsAntialias = true
        };

        string fpsText = $"FPS: {_currentFps:F0}";
        if (_useGpu) fpsText += " GPU";
        else fpsText += " CPU";

        string resText = $"{_resolutionScale:F1}×";
        string info = $"{fpsText} | {resText}";

        // Append the live pipeline timings so the HUD doubles as a quick perf readout.
        if (PipelineTimings.Style.Count > 0 || PipelineTimings.Layout.Count > 0)
        {
            info += $" | S:{PipelineTimings.Style.MeanMillis:F1}ms L:{PipelineTimings.Layout.MeanMillis:F1}ms";
        }

        float pad = 6;
        float textW = font.MeasureText(info);
        float boxW = textW + pad * 2 + 4;
        float boxH = 22;
        float boxX = 8;
        float boxY = windowHeight - boxH - 8;

        canvas.DrawRoundRect(boxX, boxY, boxW, boxH, 4, 4, bgPaint);
        canvas.DrawText(info, boxX + pad + 2, boxY + boxH * 0.72f, SKTextAlign.Left, font, textPaint);
    }

    private byte[] _pixelBuffer = Array.Empty<byte>();

    /// <summary>
    /// Reads the current surface into a reusable BGRA buffer. The array is
    /// overwritten by the next call — consumers must use it before then
    /// (the shell hands it straight to the window blit).
    /// </summary>
    public byte[] GetPixelData()
    {
        int pw = PhysicalWidth;
        int ph = PhysicalHeight;
        if (pw <= 0 || ph <= 0) return Array.Empty<byte>();

        int stride = pw * 4;
        // A full-window byte[] per frame is an LOH allocation every redraw —
        // at 60Hz that alone keeps the GC busy. Reuse until the size changes.
        if (_pixelBuffer.Length != ph * stride)
            Array.Resize(ref _pixelBuffer, ph * stride);
        byte[] pixels = _pixelBuffer;

        if (_useGpu && _gpuSurface != null)
        {
            MakeGlCurrent();
            using var image = _gpuSurface.Snapshot();
            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            unsafe
            {
                fixed (byte* p = pixels)
                {
                    image.ReadPixels(info, (IntPtr)p, stride);
                }
            }
        }
        else if (_bitmap != null)
        {
            var src = _bitmap.Bytes;
            var len = Math.Min(pixels.Length, src.Length);
            Buffer.BlockCopy(src, 0, pixels, 0, len);
        }

        return pixels;
    }

    // ── OpenGL P/Invoke ──

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const byte PFD_TYPE_RGBA = 0;

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("gdi32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // ── EGL P/Invoke (Linux) ──

    // .NET probes "EGL" as EGL.so / libEGL.so only, while Mesa installs just libEGL.so.1,
    // so the SONAME is resolved explicitly and handed to the runtime by the resolver below.
    private const string EglLibraryName = "EGL";
    private static readonly IntPtr _eglHandle = OpenNativeLibrary("libEGL.so.1", "libEGL.so", "EGL");
    private static IntPtr _glHandle;

    static SkiaRenderer()
    {
        NativeLibrary.SetDllImportResolver(typeof(SkiaRenderer).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => libraryName == EglLibraryName ? _eglHandle : IntPtr.Zero;

    private static IntPtr OpenNativeLibrary(params string[] names)
    {
        foreach (var name in names)
            if (NativeLibrary.TryLoad(name, out var handle)) return handle;
        return IntPtr.Zero;
    }

    /// <summary>
    /// GL entry point lookup for Skia's GRGlInterface: EGL returns modern and extension
    /// functions, GL 1.x ones only live in libGL, and small non-zero values are EGL's
    /// "not supported" markers rather than addresses.
    /// </summary>
    private static IntPtr GetGlProcAddress(string name)
    {
        var addr = eglGetProcAddress(name);
        if ((long)addr > 0x7FFF) return addr;

        if (_glHandle == IntPtr.Zero && !NativeLibrary.TryLoad("libGL.so.1", out _glHandle))
            NativeLibrary.TryLoad("libGL.so", out _glHandle);
        return _glHandle != IntPtr.Zero
            && NativeLibrary.TryGetExport(_glHandle, name, out var sym) ? sym : IntPtr.Zero;
    }

    private const nint EGL_DEFAULT_DISPLAY = 0;
    private const int EGL_NONE = 0x3038;
    private const int EGL_SURFACE_TYPE = 0x3033;
    private const int EGL_PBUFFER_BIT = 0x0001;
    private const int EGL_OPENGL_API = 0x30A2;
    private const int EGL_OPENGL_ES_API = 0x30A0;
    private const int EGL_RED_SIZE = 0x3024;
    private const int EGL_GREEN_SIZE = 0x3023;
    private const int EGL_BLUE_SIZE = 0x3022;
    private const int EGL_ALPHA_SIZE = 0x3021;
    private const int EGL_DEPTH_SIZE = 0x3025;
    private const int EGL_STENCIL_SIZE = 0x3026;
    private const int EGL_WIDTH = 0x3057;
    private const int EGL_HEIGHT = 0x3056;

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr eglGetDisplay(IntPtr nativeDisplayType);

    // EGLBoolean is an 8-bit unsigned char, not a 4-byte int.
    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglInitialize(IntPtr dpy, out int major, out int minor);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr eglGetError();

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr eglGetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string procName);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglBindAPI(int api);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglChooseConfig(IntPtr dpy, int[] attribList, out IntPtr config,
        int configSize, out int numConfig);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr eglCreateContext(IntPtr dpy, IntPtr config, IntPtr shareCtx, int[] attribList);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr eglCreatePbufferSurface(IntPtr dpy, IntPtr config, int[] attribList);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglMakeCurrent(IntPtr dpy, IntPtr draw, IntPtr read, IntPtr ctx);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglDestroySurface(IntPtr dpy, IntPtr surface);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglDestroyContext(IntPtr dpy, IntPtr ctx);

    [DllImport(EglLibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool eglTerminate(IntPtr dpy);

    // ── Cleanup helpers ──

    private void CleanupGlContext()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            CleanupEglContext();
            return;
        }
        if (_glRC != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(_glRC);
            _glRC = IntPtr.Zero;
        }
        CleanupGlWindow();
    }

    private void CleanupEglContext()
    {
        // The GRContext references the interface, so it is disposed by the caller first.
        _glInterface?.Dispose();
        _glInterface = null;

        if (_eglDisplay != IntPtr.Zero && _eglContext != IntPtr.Zero)
            eglMakeCurrent(_eglDisplay, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_eglDisplay != IntPtr.Zero && _eglSurface != IntPtr.Zero)
            eglDestroySurface(_eglDisplay, _eglSurface);
        if (_eglDisplay != IntPtr.Zero && _eglContext != IntPtr.Zero)
            eglDestroyContext(_eglDisplay, _eglContext);
        if (_eglDisplay != IntPtr.Zero)
            eglTerminate(_eglDisplay);

        _eglSurface = IntPtr.Zero;
        _eglContext = IntPtr.Zero;
        _eglConfig = IntPtr.Zero;
        _eglDisplay = IntPtr.Zero;
    }

    private void CleanupGlWindow()
    {
        if (_glDC != IntPtr.Zero && _glDummyWindow != IntPtr.Zero)
        {
            ReleaseDC(_glDummyWindow, _glDC);
            _glDC = IntPtr.Zero;
        }
        if (_glDummyWindow != IntPtr.Zero)
        {
            DestroyWindow(_glDummyWindow);
            _glDummyWindow = IntPtr.Zero;
        }
    }

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;

        _cachedPicture?.Dispose();

        if (_useGpu)
        {
            _gpuSurface?.Dispose();
            _grContext?.Dispose();
            CleanupGlContext();
        }
        else
        {
            _canvas?.Dispose();
            _bitmap?.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public class DirtyRegionManager
{
    private readonly List<SKRect> _dirtyRects = new();
    private readonly object _lock = new();

    public void Invalidate(SKRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        lock (_lock)
        {
            _dirtyRects.Add(rect);
        }
    }

    public void Invalidate(IEnumerable<SKRect> rects)
    {
        lock (_lock)
        {
            _dirtyRects.AddRange(rects);
        }
    }

    public List<SKRect> GetDirtyRegions()
    {
        lock (_lock)
        {
            return MergeRegions(_dirtyRects);
        }
    }

    private List<SKRect> MergeRegions(List<SKRect> rects)
    {
        if (rects.Count <= 1) return rects;

        var merged = new List<SKRect>();
        var toProcess = new List<SKRect>(rects);

        while (toProcess.Count > 0)
        {
            var current = toProcess[0];
            toProcess.RemoveAt(0);

            bool mergedAny = false;
            for (int i = 0; i < toProcess.Count; i++)
            {
                if (current.IntersectsWith(toProcess[i]))
                {
                    current = SKRect.Union(current, toProcess[i]);
                    toProcess.RemoveAt(i);
                    mergedAny = true;
                    i--;
                }
            }

            if (mergedAny)
            {
                toProcess.Add(current);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    public void ClearDirtyRegions()
    {
        lock (_lock)
        {
            _dirtyRects.Clear();
        }
    }
}
