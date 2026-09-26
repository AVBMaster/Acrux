using System;
using UpBrowser.Core.JavaScript;

namespace UpBrowser.Rendering;

public enum AntiAliasMode
{
    None,
    Normal,
    High,
    Subpixel
}

public enum PerformancePreset
{
    Custom,
    LowPower,
    Balanced,
    HighQuality,
    Ultra
}

public class RenderingSettings
{
    public event Action? OnChanged;
    public event Action<bool>? OnGpuChanged;

    private PerformancePreset _preset = PerformancePreset.Balanced;
    private bool _gpuAcceleration = true;
    private bool _vSync;
    private int _targetFps = 60;
    private AntiAliasMode _antiAliasing = AntiAliasMode.High;
    private bool _dirtyRegions = true;
    private bool _pictureCaching = true;
    private bool _tileCompositor = true;
    private int _tileSize = TiledCompositor.DefaultTileSize;
    private int _overscanRings = 0;
    private bool _adaptiveTileSize;
    private bool _predictiveRasterization = true;
    private bool _compositorRecording;
    private bool _smoothScrolling = true;
    private float _resolutionScale = 1.0f;
    private bool _showFps;
    private bool _showSettingsButton = true;
    private string _jsEngine = "Jint";
    private bool _useCustomHtmlParser = true;
    private string _tabMode = "single";

    /// <summary>
    /// Tab concurrency model, applied at browser startup:
    /// "single" — one UI thread owns all tabs (legacy);
    /// "threaded" — each tab has its own render/JS thread, background tabs keep running;
    /// "process" — each tab runs in its own child process; the browser composites
    ///            rendered frames and forwards input (cross-platform via named pipes).
    /// </summary>
    public string TabMode
    {
        get => _tabMode;
        set
        {
            if (_tabMode != value)
            {
                _tabMode = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public string JsEngine
    {
        get => _jsEngine;
        set
        {
            if (_jsEngine != value)
            {
                _jsEngine = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool UseCustomHtmlParser
    {
        get => _useCustomHtmlParser;
        set
        {
            if (_useCustomHtmlParser != value)
            {
                _useCustomHtmlParser = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public PerformancePreset Preset
    {
        get => _preset;
        set
        {
            if (_preset != value)
            {
                _preset = value;
                ApplyPreset(value);
                NotifyChanged();
            }
        }
    }

    public bool GpuAcceleration
    {
        get => _gpuAcceleration;
        set
        {
            if (_gpuAcceleration != value)
            {
                _gpuAcceleration = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
                OnGpuChanged?.Invoke(value);
            }
        }
    }

    public bool VSync
    {
        get => _vSync;
        set
        {
            if (_vSync != value)
            {
                _vSync = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public int TargetFps
    {
        get => _targetFps;
        set
        {
            var clamped = Math.Clamp(value, 15, 360);
            if (_targetFps != clamped)
            {
                _targetFps = clamped;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public AntiAliasMode AntiAliasing
    {
        get => _antiAliasing;
        set
        {
            if (_antiAliasing != value)
            {
                _antiAliasing = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool DirtyRegions
    {
        get => _dirtyRegions;
        set
        {
            if (_dirtyRegions != value)
            {
                _dirtyRegions = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool PictureCaching
    {
        get => _pictureCaching;
        set
        {
            if (_pictureCaching != value)
            {
                _pictureCaching = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool TileCompositor
    {
        get => _tileCompositor;
        set
        {
            if (_tileCompositor != value)
            {
                _tileCompositor = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public int TileSize
    {
        get => _tileSize;
        set
        {
            var clamped = Math.Clamp(value, 64, 2048);
            if (_tileSize != clamped)
            {
                _tileSize = clamped;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// How many tile-rings around the viewport the compositor should pre-rasterise
    /// in the background. 0 disables overscan, 1 is the default (one tile in every
    /// direction), 4 is the maximum. Each ring costs O(8n) tiles in memory.
    /// </summary>
    public int OverscanRings
    {
        get => _overscanRings;
        set
        {
            var clamped = Math.Clamp(value, 0, 4);
            if (_overscanRings != clamped)
            {
                _overscanRings = clamped;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// When the page is small (single-screen), drop the tile size; when it's
    /// large, raise it. The compositor picks between <see cref="MinAdaptiveTileSize"/>
    /// and <see cref="MaxAdaptiveTileSize"/> based on the page's reported
    /// bounding rect. Default: false.
    /// </summary>
    public bool AdaptiveTileSize
    {
        get => _adaptiveTileSize;
        set
        {
            if (_adaptiveTileSize != value)
            {
                _adaptiveTileSize = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// Track scroll velocity and pre-rasterise tiles in the direction of travel.
    /// Significantly reduces checkerboarding on fast scrolls.
    /// </summary>
    public bool PredictiveRasterization
    {
        get => _predictiveRasterization;
        set
        {
            if (_predictiveRasterization != value)
            {
                _predictiveRasterization = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// When true, the compositor records paint operations to a
    /// <c>CompositorDisplayList</c> as it rasterises, so subsequent dirty-rect
    /// invalidation can replay only the affected commands. Off by default because
    /// the recording overhead can dominate the raster work on small pages.
    /// </summary>
    public bool CompositorRecording
    {
        get => _compositorRecording;
        set
        {
            if (_compositorRecording != value)
            {
                _compositorRecording = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool SmoothScrolling
    {
        get => _smoothScrolling;
        set
        {
            if (_smoothScrolling != value)
            {
                _smoothScrolling = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public float ResolutionScale
    {
        get => _resolutionScale;
        set
        {
            var clamped = Math.Clamp(value, 0.25f, 3.0f);
            if (Math.Abs(_resolutionScale - clamped) > 0.01f)
            {
                _resolutionScale = clamped;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool ShowFps
    {
        get => _showFps;
        set
        {
            if (_showFps != value)
            {
                _showFps = value;
                NotifyChanged();
            }
        }
    }

    public bool ShowSettingsButton
    {
        get => _showSettingsButton;
        set
        {
            if (_showSettingsButton != value)
            {
                _showSettingsButton = value;
                NotifyChanged();
            }
        }
    }

    public double FrameTimeMs => _targetFps > 0 ? 1000.0 / _targetFps : 0;

    private void ApplyPreset(PerformancePreset preset)
    {
        switch (preset)
        {
            case PerformancePreset.LowPower:
                _gpuAcceleration = false;
                _vSync = false;
                _targetFps = 30;
                _antiAliasing = AntiAliasMode.None;
                _dirtyRegions = false;
                _pictureCaching = false;
                _tileCompositor = true;
                _tileSize = 256;
                _overscanRings = 0;
                _adaptiveTileSize = false;
                _predictiveRasterization = false;
                _compositorRecording = false;
                _resolutionScale = 0.75f;
                break;

            case PerformancePreset.Balanced:
                _gpuAcceleration = false;
                _vSync = false;
                _targetFps = 60;
                _antiAliasing = AntiAliasMode.High;
                _dirtyRegions = true;
                _pictureCaching = true;
                _tileCompositor = true;
                _tileSize = TiledCompositor.DefaultTileSize;
                _overscanRings = 1;
                _adaptiveTileSize = false;
                _predictiveRasterization = true;
                _compositorRecording = false;
                _resolutionScale = 1.0f;
                break;

            case PerformancePreset.HighQuality:
                _gpuAcceleration = true;
                _vSync = true;
                _targetFps = 120;
                _antiAliasing = AntiAliasMode.High;
                _dirtyRegions = true;
                _pictureCaching = true;
                _tileCompositor = true;
                _tileSize = TiledCompositor.DefaultTileSize;
                _overscanRings = 2;
                _adaptiveTileSize = true;
                _predictiveRasterization = true;
                _compositorRecording = true;
                _resolutionScale = 1.0f;
                break;

            case PerformancePreset.Ultra:
                _gpuAcceleration = true;
                _vSync = true;
                _targetFps = 0;
                _antiAliasing = AntiAliasMode.Subpixel;
                _dirtyRegions = true;
                _pictureCaching = true;
                _tileCompositor = true;
                _tileSize = 1024;
                _overscanRings = 3;
                _adaptiveTileSize = true;
                _predictiveRasterization = true;
                _compositorRecording = true;
                _resolutionScale = 1.5f;
                break;
        }
    }

    private void NotifyChanged()
    {
        OnChanged?.Invoke();
    }

    public void ToggleGpu() => GpuAcceleration = !GpuAcceleration;
    public void ToggleVsync() => VSync = !VSync;
    public void ToggleDirtyRegions() => DirtyRegions = !DirtyRegions;
    public void TogglePictureCaching() => PictureCaching = !PictureCaching;
    public void ToggleTileCompositor() => TileCompositor = !TileCompositor;
    public void ToggleSmoothScrolling() => SmoothScrolling = !SmoothScrolling;
    public void ToggleFps() => ShowFps = !ShowFps;

    public static string PresetName(PerformancePreset p) => p switch
    {
        PerformancePreset.Custom => "自定义",
        PerformancePreset.LowPower => "低功耗",
        PerformancePreset.Balanced => "均衡",
        PerformancePreset.HighQuality => "高质量",
        PerformancePreset.Ultra => "极致",
        _ => "未知"
    };

    public static string AaName(AntiAliasMode m) => m switch
    {
        AntiAliasMode.None => "无",
        AntiAliasMode.Normal => "普通",
        AntiAliasMode.High => "高",
        AntiAliasMode.Subpixel => "次像素",
        _ => "未知"
    };
}
