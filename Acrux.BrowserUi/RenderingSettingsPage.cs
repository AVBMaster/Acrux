using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Acrux.Core.JavaScript;
using SkiaSharp;

namespace Acrux.Rendering;

public class RenderingSettingsPage
{
    private readonly RenderingSettings _settings;
    private bool _visible;
    private float _scrollOffset;
    private float _panelWidth = 320;
    private float _contentHeight;

    private readonly SKTypeface _typeface;
    private readonly SKFont _smallFont;
    private readonly SKFont _nameFont;
    private int _hoveredActionButton; // 1 or 2 within the hovered action item
    private readonly float _dpiScale;

    // Cached reusable paints for rendering (avoid GC pressure from per-frame allocations)
    private readonly SKPaint _cardBgPaint;
    private readonly SKPaint _cardBorderPaint;
    private readonly SKPaint _statusPaint;
    private readonly SKPaint _hintPaint;
    private readonly SKPaint _namePaint;
    private readonly SKPaint _detailPaint;

    // Resize handle state
    private const float ResizeHandleWidth = 6;
    private bool _resizingPanel;
    private float _resizeStartX;
    private float _resizeStartWidth;

    // Cached reusable paints/fonts for render loop (avoid per-frame GC pressure)
    private readonly SKPaint _bgPaint;
    private readonly SKPaint _borderPaint;
    private readonly SKPaint _headerBgPaint;
    private readonly SKFont _headerFont;
    private readonly SKPaint _headerPaint;
    private readonly SKFont _labelFont;
    private readonly SKFont _valueFont;
    private readonly SKPaint _valuePaint;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _catPaint;
    private readonly SKFont _catFont;
    private readonly SKPaint _hoverPaint;
    private readonly SKPaint _trackPaint;
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _thumbPaint;
    private readonly SKPaint _thumbBorderPaint;
    private readonly SKPaint _arrowPaint;
    private readonly SKPaint _sliderBgPaint;
    private readonly SKPaint _sliderFillPaint;
    private readonly SKPaint _sliderThumbPaint;
    private readonly SKPaint _sliderThumbBorderPaint;
    private readonly SKPaint _btnBgPaint;
    private readonly SKPaint _btnBorderPaint;
    private readonly SKPaint _btnTextPaint;
    private readonly SKPaint _radioOuterPaint;
    private readonly SKPaint _radioInnerPaint;
    private readonly SKPaint _toggleBgPaint;
    private readonly SKPaint _toggleThumbPaint;
    private readonly SKPaint _toggleGlowPaint;
    private readonly SKPaint _pctPaint;

    private int _hoveredItem = -1;
    private bool _draggingSlider;
    private int _draggingSliderIndex = -1;
    private bool _rebuilding;

    // JS engine tracking
    private static readonly ConcurrentDictionary<string, int> _downloadProgress = new();
    private static string _downloadError = "";

    public event Action<string>? OnBrowseEngine; // engineName → user picks folder

    private struct SettingItem
    {
        public string Label;
        public string Value;
        public string Category;
        public Action? OnClick;
        public Func<bool>? IsOn;
        public int Index;
        public bool IsSlider;
        public float SliderValue;
        public float SliderMin;
        public float SliderMax;
        public Action<float>? OnSliderChange;
        public string[]? Options;
        public int SelectedOption;
        public Action<int>? OnOptionChange;
        public Func<string>? DynamicValue;
        public bool IsEngineDetail;
        public bool IsEngineAction;
        public bool IsEngineProgress;
        public int ProgressValue;
        public string? ActionLabel2;
        public Action? ActionOnClick2;
        public bool IsEngineCard;
        public string EngineName;
        public EngineCardStatus EngineCardStatus;
        public bool EngineIsBuiltIn;
        public bool EngineIsDownloading;
        public int EngineDownloadPct;
    }

    private List<SettingItem> _items = new();
    private int _categoryCount;

    public bool Visible => _visible;
    public float PanelWidth => _panelWidth;

    public event Action? OnChanged;

    public RenderingSettingsPage(RenderingSettings settings, float dpiScale)
    {
        _settings = settings;
        _dpiScale = dpiScale;
        _typeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _smallFont = FontHelper.CrispHintedFont(_typeface, 10);
        _nameFont = FontHelper.CrispHintedFont(_typeface, 14);

        // Pre-create reusable paints (avoid GC pressure)
        _cardBgPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        _cardBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
        _statusPaint = new SKPaint { IsAntialias = true };
        _hintPaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        _namePaint = new SKPaint { Color = new SKColor(40, 44, 52), IsAntialias = true };
        _detailPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true };

        // Render loop cached paints (avoid per-frame GC pressure)
        _bgPaint = new SKPaint { Color = new SKColor(255, 255, 255, 240), Style = SKPaintStyle.Fill, IsAntialias = true };
        _borderPaint = new SKPaint { Color = new SKColor(200, 200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        _headerBgPaint = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Fill };
        _headerFont = FontHelper.CrispHintedFont(_typeface, 15);
        _headerPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _labelFont = FontHelper.CrispHintedFont(_typeface, 12);
        _valueFont = FontHelper.CrispHintedFont(_typeface, 12);
        _valuePaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        _labelPaint = new SKPaint { Color = new SKColor(60, 64, 67), IsAntialias = true };
        _catPaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        _catFont = FontHelper.CrispHintedFont(_typeface, 11);
        _hoverPaint = new SKPaint { Color = new SKColor(232, 240, 254), Style = SKPaintStyle.Fill };
        _trackPaint = new SKPaint { Color = new SKColor(218, 220, 224), Style = SKPaintStyle.Fill };
        _fillPaint = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Fill };
        _thumbPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        _thumbBorderPaint = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        _arrowPaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        _sliderBgPaint = new SKPaint { Color = new SKColor(218, 220, 224), Style = SKPaintStyle.Fill };
        _sliderFillPaint = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Fill };
        _sliderThumbPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        _sliderThumbBorderPaint = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        _btnBgPaint = new SKPaint { Color = new SKColor(248, 249, 250), Style = SKPaintStyle.Fill, IsAntialias = true };
        _btnBorderPaint = new SKPaint { Color = new SKColor(218, 220, 224), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        _btnTextPaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        _radioOuterPaint = new SKPaint { Color = new SKColor(128, 134, 139), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        _radioInnerPaint = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Fill, IsAntialias = true };
        _toggleBgPaint = new SKPaint { Color = new SKColor(189, 193, 198), Style = SKPaintStyle.Fill, IsAntialias = true };
        _toggleThumbPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        _toggleGlowPaint = new SKPaint { Color = new SKColor(255, 255, 255, 60), Style = SKPaintStyle.Fill, IsAntialias = true };
        _pctPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true };

        _settings.OnChanged += RebuildItems;
        RebuildItems();
    }

    public void Toggle()
    {
        _visible = !_visible;
        _scrollOffset = 0;
    }

    public void Show() => _visible = true;
    public void Hide() => _visible = false;

    private void RebuildItems()
    {
        if (_rebuilding) return;
        _rebuilding = true;

        _items.Clear();
        _categoryCount = 0;
        int idx = 0;

        AddCategory("性能预设", ref idx);
        AddOption("预设方案", RenderingSettings.PresetName(_settings.Preset), () =>
        {
            CyclePreset();
        }, ref idx);

        AddCategory("渲染引擎", ref idx);
        AddToggle("GPU 加速", _settings.GpuAcceleration, () => _settings.ToggleGpu(), ref idx);
        AddToggle("垂直同步", _settings.VSync, () => _settings.ToggleVsync(), ref idx);
        AddToggle("脏区域渲染", _settings.DirtyRegions, () => _settings.ToggleDirtyRegions(), ref idx);
        AddToggle("画面缓存", _settings.PictureCaching, () => _settings.TogglePictureCaching(), ref idx);

        AddCategory("画质", ref idx);
        AddOptions("抗锯齿", new[] { "无", "普通", "高", "次像素" },
            (int)_settings.AntiAliasing, (i) => _settings.AntiAliasing = (AntiAliasMode)i, ref idx);
        AddSlider("渲染缩放", _settings.ResolutionScale, 0.25f, 3.0f, (v) => _settings.ResolutionScale = v, ref idx);

        AddCategory("帧率", ref idx);
        AddOptions("目标帧率", new[] { "30", "60", "120", "不限" },
            _settings.TargetFps switch { 30 => 0, 60 => 1, 120 => 2, _ => 3 },
            (i) => _settings.TargetFps = i switch { 0 => 30, 1 => 60, 2 => 120, _ => 0 }, ref idx);

        AddCategory("显示", ref idx);
        AddToggle("显示 FPS", _settings.ShowFps, () => _settings.ToggleFps(), ref idx);
        AddToggle("平滑滚动", _settings.SmoothScrolling, () => _settings.ToggleSmoothScrolling(), ref idx);

        AddCategory("HTML 解析器", ref idx);
        AddToggle("自定义解析器", _settings.UseCustomHtmlParser, () => _settings.UseCustomHtmlParser = !_settings.UseCustomHtmlParser, ref idx);

        AddCategory("标签页模式 (重启生效)", ref idx);
        AddOptions("并发模型", new[] { "单线程", "多线程", "多进程 (默认)" },
            _settings.TabMode switch { "threaded" => 1, "process" => 2, _ => 0 },
            (i) => _settings.TabMode = i switch { 1 => "threaded", 2 => "process", _ => "single" }, ref idx);

        AddCategory("JavaScript 引擎", ref idx);
        BuildJsEngineSection(ref idx);

        _rebuilding = false;
    }

    private void CyclePreset()
    {
        var values = (PerformancePreset[])Enum.GetValues(typeof(PerformancePreset));
        int current = Array.IndexOf(values, _settings.Preset);
        current = (current + 1) % values.Length;
        _settings.Preset = values[current];
    }

    private void AddCategory(string name, ref int idx)
    {
        _items.Add(new SettingItem { Label = name, Category = name, Index = idx++ });
        _categoryCount++;
    }

    private Func<bool> MakeGetter(string label)
    {
        if (label.Contains("GPU")) return () => _settings.GpuAcceleration;
        if (label.Contains("垂直同步")) return () => _settings.VSync;
        if (label.Contains("脏区域")) return () => _settings.DirtyRegions;
        if (label.Contains("画面缓存")) return () => _settings.PictureCaching;
        if (label.Contains("平滑")) return () => _settings.SmoothScrolling;
        if (label.Contains("FPS")) return () => _settings.ShowFps;
        if (label.Contains("自定义解析器")) return () => _settings.UseCustomHtmlParser;
        return () => false;
    }

    private void AddToggle(string label, bool isOn, Action onClick, ref int idx)
    {
        var getter = MakeGetter(label);
        _items.Add(new SettingItem
        {
            Label = label,
            Value = isOn ? "ON" : "OFF",
            Category = "",
            OnClick = onClick,
            IsOn = getter,
            Index = idx++
        });
    }

    private void AddOption(string label, string value, Action onClick, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = label,
            Value = value,
            Category = "",
            OnClick = onClick,
            Index = idx++
        });
    }

    private void AddOptions(string label, string[] options, int selected, Action<int> onChange, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = label,
            Value = options[selected],
            Category = "",
            OnClick = () =>
            {
                int next = (selected + 1) % options.Length;
                onChange(next);
            },
            Options = options,
            SelectedOption = selected,
            OnOptionChange = onChange,
            Index = idx++
        });
    }

    public enum EngineCardStatus
    {
        Active,       // 当前正在使用的引擎
        Downloaded,   // 已下载但未使用
        NotDownloaded, // 未下载
        Downloading,  // 正在下载
    }

    private struct EngineCardItem
    {
        public string Name;
        public EngineCardStatus Status;
        public bool IsBuiltIn; // Jint
    }

    private void BuildJsEngineSection(ref int idx)
    {
        string[] engines = { "Jint", "V8", "Jurassic" };
        string selected = _settings.JsEngine ?? "Jint";

        // Hint
        AddEngineDetail("下载和切换是分离的：先下载，再选择「应用」生效。", ref idx);
        AddEngineDetail("也可访问 acrux://js 使用 HTML 管理页面。", ref idx);

        foreach (var name in engines)
        {
            var type = JsEngineConfig.GetEngineTypeByName(name);
            bool isBuiltIn = name == "Jint";
            bool isActive = name == selected;
            bool downloaded = isBuiltIn || JsEngineDownloader.IsEngineDownloaded(type!.Value);
            bool isDownloading = _downloadProgress.TryGetValue(name, out int pct) && pct >= 0 && pct < 100;

            EngineCardStatus cardStatus;
            if (isActive) cardStatus = EngineCardStatus.Active;
            else if (isDownloading) cardStatus = EngineCardStatus.Downloading;
            else if (downloaded) cardStatus = EngineCardStatus.Downloaded;
            else cardStatus = EngineCardStatus.NotDownloaded;

            _items.Add(new SettingItem
            {
                Label = "",
                Value = "",
                Category = "",
                IsEngineCard = true,
                Index = idx++,
                EngineName = name,
                EngineCardStatus = cardStatus,
                EngineIsBuiltIn = isBuiltIn,
                EngineIsDownloading = isDownloading,
                EngineDownloadPct = pct,
                OnClick = () =>
                {
                    // 点击卡片：如果是已下载但非当前的引擎，应用它
                    if (cardStatus == EngineCardStatus.Downloaded)
                    {
                        _settings.JsEngine = name;
                        ApplyEngineChoice();
                    }
                }
            });

            // ── Details for the card ──
            if (isBuiltIn)
            {
                AddEngineDetail("类型: 纯 C# 引擎（内建）", ref idx);
                AddEngineDetail("平台: 跨平台通用，无需下载", ref idx);
                continue;
            }

            // Non-Jint engines
            if (isDownloading)
            {
                AddEngineProgress(pct, ref idx);
            }
            else if (!downloaded)
            {
                AddEngineDetail("尚未下载，点击「下载」按钮获取引擎。", ref idx);
            }
            else
            {
                string dir = JsEngineDownloader.EngineDir(type!.Value);
                long size = GetDirectorySize(dir);
                string sizeStr = size > 1024 * 1024 ? $"{size / (1024.0 * 1024.0):F1} MB" : $"{size / 1024.0:F1} KB";
                AddEngineDetail($"大小: {sizeStr}", ref idx);
                AddEngineDetail($"位置: {dir}", ref idx);
            }

            // Action buttons
            if (!isDownloading)
            {
                if (downloaded)
                {
                    if (!isActive)
                    {
                        // 已下载但未使用 → 显示「应用」和「从本地选择」
                        AddEngineActions(
                            "应用此引擎", () =>
                            {
                                _settings.JsEngine = name;
                                ApplyEngineChoice();
                            },
                            "从本地选择…", () => OnBrowseEngine?.Invoke(name), ref idx);
                    }
                    else
                    {
                        // 当前正在使用的引擎 → 显示「重新下载」和「从本地选择」
                        AddEngineActions(
                            "重新下载", () => TriggerEngineDownload(name),
                            "从本地选择…", () => OnBrowseEngine?.Invoke(name), ref idx);
                    }
                }
                else
                {
                    // 未下载 → 显示「下载」和「从本地选择」
                    AddEngineActions(
                        "下载", () => TriggerEngineDownload(name),
                        "从本地选择…", () => OnBrowseEngine?.Invoke(name), ref idx);
                }
            }
        }
    }

    public void Invalidate() => RebuildItems();

    private static long GetDirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch { return 0; }
    }

    private static string GetEngineStatusText(string engineName)
    {
        var type = JsEngineConfig.GetEngineTypeByName(engineName);
        if (type == null) return "";
        if (type == JsEngineType.Jint) return "内置";
        return JsEngineDownloader.IsEngineDownloaded(type.Value) ? "已就绪" : "未下载";
    }

    private void AddEngineDetail(string text, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = text,
            Value = "",
            Category = "",
            IsEngineDetail = true,
            Index = idx++
        });
    }

    private void AddEngineProgress(int pct, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = "",
            Value = "",
            Category = "",
            IsEngineProgress = true,
            ProgressValue = Math.Clamp(pct, 0, 100),
            Index = idx++
        });
    }

    private void AddEngineActions(string btn1, Action onClick1, string? btn2, Action? onClick2, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = btn1,
            Value = "",
            Category = "",
            IsEngineAction = true,
            OnClick = onClick1,
            ActionLabel2 = btn2,
            ActionOnClick2 = onClick2,
            Index = idx++
        });
    }

    public event Action<JsEngineType>? OnEngineApplied;

    private void ApplyEngineChoice()
    {
        var type = JsEngineConfig.GetEngineTypeByName(_settings.JsEngine ?? "Jint") ?? JsEngineType.Jint;
        JsEngineConfig.DefaultEngineType = type;
        OnEngineApplied?.Invoke(type);
    }

    private void TriggerEngineDownload(string engineName)
    {
        var type = JsEngineConfig.GetEngineTypeByName(engineName);
        if (type is not { } t || t == JsEngineType.Jint) return;

        if (JsEngineDownloader.IsEngineDownloaded(t))
        {
            try { Directory.Delete(JsEngineDownloader.EngineDir(t), true); } catch { }
        }

        _downloadProgress[engineName] = 0;
        _downloadError = "";

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<int>(p =>
                {
                    _downloadProgress[engineName] = p;
                    OnChanged?.Invoke();
                });
                await JsEngineDownloader.DownloadEngineAsync(t, progress);
                _downloadProgress[engineName] = 100;
                Console.WriteLine($"[JS] {engineName} engine downloaded");
                OnChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _downloadError = ex.Message;
                _downloadProgress.TryRemove(engineName, out _);
                Console.WriteLine($"[JS] Failed to download {engineName}: {ex.Message}");
                // 错误会在设置面板中显示（通过 RebuildItems 刷新）
                OnChanged?.Invoke();
            }
        });
    }

    private void AddSlider(string label, float value, float min, float max, Action<float> onChange, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = label,
            Value = $"{value:F2}x",
            Category = "",
            IsSlider = true,
            SliderValue = (value - min) / (max - min),
            SliderMin = min,
            SliderMax = max,
            OnSliderChange = onChange,
            Index = idx++
        });
    }

    public void Render(SKCanvas canvas, float windowWidth, float windowHeight, float contentOffset)
    {
        if (!_visible) return;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;
        float panelBottom = windowHeight;

        canvas.Save();

        // Panel background — use cached paint
        _bgPaint.Color = new SKColor(255, 255, 255, 240);
        _bgPaint.Style = SKPaintStyle.Fill;
        canvas.DrawRoundRect(panelLeft, panelTop, _panelWidth, panelBottom - panelTop, 8, 8, _bgPaint);

        // Panel border — use cached paint
        _borderPaint.Color = new SKColor(200, 200, 200, 200);
        _borderPaint.Style = SKPaintStyle.Stroke;
        _borderPaint.StrokeWidth = 1;
        canvas.DrawRoundRect(panelLeft, panelTop, _panelWidth, panelBottom - panelTop, 8, 8, _borderPaint);

        float headerHeight = 40;
        _headerBgPaint.Color = new SKColor(26, 115, 232);
        _headerBgPaint.Style = SKPaintStyle.Fill;
        var pb = new SKPathBuilder();
        pb.AddRoundRect(new SKRect(panelLeft, panelTop, panelLeft + _panelWidth, panelTop + headerHeight), 8, 8);
        using var headerPath = pb.Detach();
        canvas.DrawPath(headerPath, _headerBgPaint);
        canvas.DrawRect(panelLeft, panelTop + 4, _panelWidth, headerHeight - 4, _headerBgPaint);

        canvas.DrawText("渲染设置", panelLeft + 16, panelTop + 26, SKTextAlign.Left, _headerFont, _headerPaint);

        // ── Resize handle (left edge of panel) ──
        DrawResizeHandle(canvas, panelLeft, panelTop, panelBottom);

        float xPos = panelLeft + 12;
        float yPos = panelTop + headerHeight + 10 - _scrollOffset;
        float itemHeight = 38;
        float labelWidth = _panelWidth - 60;

        canvas.Save();
        var clipRect = new SKRect(panelLeft, panelTop + headerHeight, panelLeft + _panelWidth, panelBottom);
        canvas.ClipRect(clipRect);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            float itemY = yPos;

            if (itemY + itemHeight < panelTop + headerHeight)
            {
                yPos += itemHeight;
                continue;
            }
            if (itemY > panelBottom) break;

            if (!string.IsNullOrEmpty(item.Category))
            {
                canvas.DrawText(item.Label, xPos + 4, itemY + 22, SKTextAlign.Left, _catFont, _catPaint);
                yPos += itemHeight + 4;
                continue;
            }

            bool isHovered = i == _hoveredItem;
            bool isCategory = !string.IsNullOrEmpty(item.Category);

            // 只有可交互的项才显示高亮（有 OnClick、IsSlider 等）
            bool isInteractive = item.IsSlider || item.IsEngineAction || item.IsEngineCard || item.IsOn != null || item.OnClick != null || item.Options != null;
            if (!isCategory && isHovered && isInteractive)
            {
                _hoverPaint.Color = new SKColor(232, 240, 254);
                canvas.DrawRoundRect(xPos, itemY, _panelWidth - 24, itemHeight, 4, 4, _hoverPaint);
            }

            if (item.IsSlider)
            {
                canvas.DrawText(item.Label, xPos + 8, itemY + 16, SKTextAlign.Left, _labelFont, _labelPaint);

                float sliderLeft = xPos + 8;
                float sliderWidth = _panelWidth - 72;
                float sliderY = itemY + itemHeight - 8;
                float sliderTrackH = 4;

                string val = $"{_settings.ResolutionScale:F1}x";
                float vw = _valueFont.MeasureText(val);
                float valX = xPos + _panelWidth - 24 - vw - 6;
                canvas.DrawText(val, valX, itemY + 16, SKTextAlign.Left, _valueFont, _valuePaint);

                using var trackPaint = new SKPaint
                {
                    Color = new SKColor(218, 220, 224),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRoundRect(sliderLeft, sliderY - sliderTrackH / 2, sliderWidth, sliderTrackH, 2, 2, trackPaint);

                float fillWidth = item.SliderValue * sliderWidth;
                using var fillPaint = new SKPaint
                {
                    Color = new SKColor(26, 115, 232),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRoundRect(sliderLeft, sliderY - sliderTrackH / 2, fillWidth, sliderTrackH, 2, 2, fillPaint);

                float thumbX = sliderLeft + fillWidth;
                float thumbR = 6;
                using var thumbPaint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(thumbX, sliderY, thumbR, thumbPaint);
                using var thumbBorder = new SKPaint
                {
                    Color = new SKColor(26, 115, 232),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2,
                    IsAntialias = true
                };
                canvas.DrawCircle(thumbX, sliderY, thumbR, thumbBorder);
            }
            else if (item.Options != null)
            {
                canvas.DrawText(item.Label, xPos + 8, itemY + 21, SKTextAlign.Left, _labelFont, _labelPaint);

                string val = item.DynamicValue != null ? item.DynamicValue() : item.Value;
                float vw = _valueFont.MeasureText(val);
                canvas.DrawText(val, xPos + _panelWidth - 24 - vw - 8, itemY + 21, SKTextAlign.Left, _valueFont, _valuePaint);

                float arrowX = xPos + _panelWidth - 24;
                using var arrowPaint = new SKPaint
                {
                    Color = new SKColor(26, 115, 232),
                    IsAntialias = true
                };
                canvas.DrawText("›", arrowX - 4, itemY + 21, SKTextAlign.Left, _valueFont, arrowPaint);
            }
            else if (item.IsEngineDetail)
            {
                using var detailPaint = new SKPaint
                {
                    Color = new SKColor(95, 99, 104),
                    IsAntialias = true
                };
                canvas.DrawText(item.Label, xPos + 20, itemY + 21, SKTextAlign.Left, _smallFont, detailPaint);
            }
            else if (item.IsEngineProgress)
            {
                float barX = xPos + 20;
                float barW = _panelWidth - 64;
                float barY = itemY + (itemHeight - 8) / 2f;
                float barH = 8;
                using var track = new SKPaint { Color = new SKColor(224, 226, 230), Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawRoundRect(barX, barY, barW, barH, 4, 4, track);
                float fillW = barW * item.ProgressValue / 100f;
                if (fillW > 1)
                {
                    using var fill = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Fill, IsAntialias = true };
                    canvas.DrawRoundRect(barX, barY, fillW, barH, 4, 4, fill);
                }
                using var pctPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true };
                string pctStr = $"{item.ProgressValue}%";
                float pctW = _valueFont.MeasureText(pctStr);
                canvas.DrawText(pctStr, xPos + _panelWidth - 24 - pctW, itemY + 21, SKTextAlign.Left, _smallFont, pctPaint);
            }
            else if (item.IsEngineAction)
            {
                var (b1x, b1w, b2x, b2w) = ComputeActionRects(item, xPos);
                DrawActionButton(canvas, item.Label, b1x, itemY, b1w, itemHeight, isHovered && _hoveredActionButton == 1);
                if (!string.IsNullOrEmpty(item.ActionLabel2))
                    DrawActionButton(canvas, item.ActionLabel2!, b2x, itemY, b2w, itemHeight, isHovered && _hoveredActionButton == 2);
            }
            else if (item.IsEngineCard)
            {
                DrawEngineCard(canvas, item, xPos, itemY, itemHeight, isHovered);
            }
            else
            {
                if (item.IsOn != null)
                {
                    canvas.DrawText(item.Label, xPos + 8, itemY + 21, SKTextAlign.Left, _labelFont, _labelPaint);
                    DrawToggle(canvas, xPos + _panelWidth - 52, itemY + 6, 36, 20, item.IsOn(), isHovered);
                }
                else
                {
                    string val = item.Value;
                    float vw = _valueFont.MeasureText(val);
                    canvas.DrawText(val, xPos + _panelWidth - 24 - vw, itemY + 21, SKTextAlign.Left, _valueFont, _valuePaint);
                    canvas.DrawText("›", xPos + _panelWidth - 20, itemY + 21, SKTextAlign.Left, _valueFont, _valuePaint);
                }
            }

            yPos += itemHeight;
        }

        _contentHeight = yPos - (panelTop + headerHeight + 10 - _scrollOffset);

        canvas.Restore();
        canvas.Restore();
    }



    private void DrawRadioButton(SKCanvas canvas, float x, float y, bool isOn)
    {
        float r = 8;
        float cx = x + r;
        float cy = y + r;

        using var outer = new SKPaint
        {
            Color = isOn ? new SKColor(26, 115, 232) : new SKColor(128, 134, 139),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawCircle(cx, cy, r, outer);

        if (isOn)
        {
            using var inner = new SKPaint
            {
                Color = new SKColor(26, 115, 232),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, r - 4, inner);
        }
    }

    private void DrawEngineCard(SKCanvas canvas, SettingItem item, float xPos, float itemY, float itemHeight, bool isHovered)
    {
        float cardLeft = xPos - 4;
        float cardTop = itemY - 2;
        float cardRight = xPos + _panelWidth - 8;
        float cardBottom = itemY + itemHeight + 2;
        float cardW = cardRight - cardLeft;
        float cardH = cardBottom - cardTop;

        // Card background — use cached paint
        _cardBgPaint.Color = item.EngineCardStatus == EngineCardStatus.Active
            ? new SKColor(240, 248, 255, 255)
            : (isHovered ? new SKColor(232, 240, 254, 255) : new SKColor(250, 250, 250, 255));
        canvas.DrawRoundRect(cardLeft, cardTop, cardW, cardH, 6, 6, _cardBgPaint);

        // Card border — use cached paint
        _cardBorderPaint.Color = item.EngineCardStatus == EngineCardStatus.Active
            ? new SKColor(26, 115, 232)
            : new SKColor(218, 220, 224);
        _cardBorderPaint.StrokeWidth = item.EngineCardStatus == EngineCardStatus.Active ? 2 : 1;
        canvas.DrawRoundRect(cardLeft, cardTop, cardW, cardH, 6, 6, _cardBorderPaint);

        float padX = 12;
        float padY = 8;

        // Engine name — use cached paint
        canvas.DrawText(item.EngineName, cardLeft + padX, cardTop + padY + 16, SKTextAlign.Left, _nameFont, _namePaint);

        // Status indicator
        string statusText;
        SKColor statusColor;
        if (item.EngineCardStatus == EngineCardStatus.Active)
        {
            statusText = "● 当前使用";
            statusColor = new SKColor(26, 115, 232);
        }
        else if (item.EngineCardStatus == EngineCardStatus.Downloaded)
        {
            statusText = "✅ 已就绪";
            statusColor = new SKColor(52, 124, 69);
        }
        else if (item.EngineCardStatus == EngineCardStatus.Downloading)
        {
            statusText = $"⏳ 下载中 {item.EngineDownloadPct}%";
            statusColor = new SKColor(255, 183, 43);
        }
        else
        {
            statusText = "⬇ 未下载";
            statusColor = new SKColor(128, 134, 139);
        }

        // Status text — use cached paint
        _statusPaint.Color = statusColor;
        float statusW = _nameFont.MeasureText(statusText);
        canvas.DrawText(statusText, cardLeft + cardW - padX - statusW, cardTop + padY + 16, SKTextAlign.Left, _nameFont, _statusPaint);

        // Downloaded engine hint (click to apply)
        if (item.EngineCardStatus == EngineCardStatus.Downloaded)
        {
            canvas.DrawText("点击卡片或「应用此引擎」按钮即可切换", cardLeft + padX, cardTop + padY + 32, SKTextAlign.Left, _smallFont, _hintPaint);
        }
    }

    /// <summary>
    /// 绘制面板左侧的拖拽缩放手柄。
    /// 鼠标在 panelLeft 附近时可拖拽改变 _panelWidth。
    /// </summary>
    private void DrawResizeHandle(SKCanvas canvas, float panelLeft, float panelTop, float panelBottom)
    {
        float handleX = panelLeft;
        float handleWidth = ResizeHandleWidth;
        float handleY = panelTop + 8;
        float handleH = panelBottom - panelTop - 16;

        // 手柄颜色：hovered 时高亮，否则半透明灰色
        using var handlePaint = new SKPaint
        {
            Color = _resizingPanel ? new SKColor(26, 115, 232) : new SKColor(200, 200, 200),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(handleX, handleY, handleWidth, handleH, 3, 3, handlePaint);

        // 手柄上的两条横线装饰
        using var gripPaint = new SKPaint
        {
            Color = new SKColor(150, 150, 150),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        float gripY = panelTop + panelBottom / 2;
        canvas.DrawLine(handleX + 1.5f, gripY - 4f, handleX + handleWidth - 1.5f, gripY - 4f, gripPaint);
        canvas.DrawLine(handleX + 1.5f, gripY + 4f, handleX + handleWidth - 1.5f, gripY + 4f, gripPaint);
    }

    private void DrawToggle(SKCanvas canvas, float x, float y, float w, float h, bool isOn, bool hovered)
    {
        float radius = h / 2;

        using var bg = new SKPaint
        {
            Color = isOn ? new SKColor(26, 115, 232) : new SKColor(189, 193, 198),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(x, y, w, h, radius, radius, bg);

        float thumbX = isOn ? x + w - h : x;
        using var thumb = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(thumbX + radius, y + radius, radius - 2, thumb);

        if (hovered)
        {
            using var glow = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 60),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(thumbX + radius, y + radius, radius, glow);
        }
    }

    private (float b1x, float b1w, float b2x, float b2w) ComputeActionRects(SettingItem item, float xPos)
    {
        const float pad = 12f;
        const float gap = 8f;
        float w1 = _smallFont.MeasureText(item.Label) + pad * 2;
        float b1x = xPos + 20;
        if (string.IsNullOrEmpty(item.ActionLabel2))
            return (b1x, w1, 0, 0);
        float w2 = _smallFont.MeasureText(item.ActionLabel2) + pad * 2;
        float b2x = b1x + w1 + gap;
        return (b1x, w1, b2x, w2);
    }

    private void DrawActionButton(SKCanvas canvas, string text, float bx, float itemY, float bw, float itemHeight, bool hovered)
    {
        float by = itemY + 2;
        float bh = itemHeight - 4;
        using var btnBg = new SKPaint
        {
            Color = hovered ? new SKColor(232, 240, 254) : new SKColor(248, 249, 250),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(bx, by, bw, bh, 6, 6, btnBg);
        using var btnBorder = new SKPaint
        {
            Color = new SKColor(218, 220, 224),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawRoundRect(bx, by, bw, bh, 6, 6, btnBorder);
        using var btnText = new SKPaint
        {
            Color = new SKColor(26, 115, 232),
            IsAntialias = true
        };
        float textX = bx + (bw - _smallFont.MeasureText(text)) / 2;
        canvas.DrawText(text, textX, by + 17, SKTextAlign.Left, _smallFont, btnText);
    }

    public bool HandleClick(float x, float y, float windowWidth, float contentOffset)
    {
        if (!_visible) return false;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;
        float panelBottom = contentOffset + windowWidth; // approximate

        // ── Check resize handle (left edge) ──
        float handleLeft = panelLeft - 5;
        float handleRight = panelLeft + ResizeHandleWidth;
        if (x >= handleLeft && x <= handleRight && y >= panelTop && y <= panelBottom)
        {
            _resizingPanel = true;
            _resizeStartX = x;
            _resizeStartWidth = _panelWidth;
            return true;
        }

        if (x < panelLeft || x > panelLeft + _panelWidth || y < panelTop) return false;

        float headerHeight = 40;
        float itemY = panelTop + headerHeight + 10 - _scrollOffset;
        float itemHeight = 38;
        float xPos = panelLeft + 12;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (!string.IsNullOrEmpty(item.Category))
            {
                itemY += itemHeight + 4;
                continue;
            }

            float y0 = itemY;
            float y1 = itemY + itemHeight;

            if (y >= y0 && y <= y1)
            {
                if (item.IsOn != null)
                {
                    item.OnClick?.Invoke();
                    return true;
                }
                if (item.IsSlider)
                {
                    _draggingSlider = true;
                    _draggingSliderIndex = i;
                    UpdateSliderValue(i, x, xPos, panelLeft);
                    return true;
                }
                if (item.IsEngineAction)
                {
                    var (b1x, b1w, b2x, b2w) = ComputeActionRects(item, xPos);
                    if (x >= b1x && x <= b1x + b1w)
                        item.OnClick?.Invoke();
                    else if (b2w > 0 && x >= b2x && x <= b2x + b2w)
                        item.ActionOnClick2?.Invoke();
                    OnChanged?.Invoke();
                    return true;
                }
                if (item.IsEngineCard)
                {
                    // 点击卡片：如果已下载且非当前引擎，则应用它
                    if (item.OnClick != null)
                        item.OnClick();
                    return true;
                }
                if (item.OnClick != null)
                {
                    item.OnClick();
                    OnChanged?.Invoke();
                    return true;
                }
            }

            itemY += itemHeight;
        }

        return true;
    }

    public bool HandleMouseMove(float x, float y, float windowWidth, float contentOffset)
    {
        if (!_visible) return false;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;

        // ── Handle panel resizing ──
        if (_resizingPanel)
        {
            float delta = x - _resizeStartX;
            float newWidth = _resizeStartWidth - delta;
            newWidth = Math.Clamp(newWidth, 200f, 500f); // min 200, max 500
            if (Math.Abs(newWidth - _panelWidth) > 2)
            {
                _panelWidth = newWidth;
                OnChanged?.Invoke();
            }
            return true;
        }

        if (_draggingSlider && _draggingSliderIndex >= 0)
        {
            UpdateSliderValue(_draggingSliderIndex, x, panelLeft + 12, panelLeft);
            return true;
        }

        int oldHovered = _hoveredItem;

        if (x < panelLeft || x > panelLeft + _panelWidth || y < panelTop)
        {
            _hoveredItem = -1;
            _hoveredActionButton = 0;
            return false;
        }

        float headerHeight = 40;
        float itemY = panelTop + headerHeight + 10 - _scrollOffset;
        float itemHeight = 38;
        float xPos = panelLeft + 12;

        int hovered = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (!string.IsNullOrEmpty(_items[i].Category))
            {
                itemY += itemHeight + 4;
                continue;
            }

            if (y >= itemY && y <= itemY + itemHeight)
            {
                hovered = i;
                break;
            }
            itemY += itemHeight;
        }

        int newHoveredAction = 0;
        if (hovered != -1 && _items[hovered].IsEngineAction)
        {
            var item = _items[hovered];
            var (b1x, b1w, b2x, b2w) = ComputeActionRects(item, xPos);
            if (x >= b1x && x <= b1x + b1w)
                newHoveredAction = 1;
            else if (b2w > 0 && x >= b2x && x <= b2x + b2w)
                newHoveredAction = 2;
        }

        if (_hoveredItem != hovered || _hoveredActionButton != newHoveredAction)
        {
            _hoveredItem = hovered;
            _hoveredActionButton = newHoveredAction;
            // 触发重绘，让 hover 高亮即时更新
            OnChanged?.Invoke();
        }
        return true;
    }

    public void HandleMouseUp()
    {
        if (_draggingSlider && _pendingSliderValue >= 0 && _draggingSliderIndex >= 0)
        {
            var item = _items[_draggingSliderIndex];
            item.OnSliderChange?.Invoke(_pendingSliderValue);
            _pendingSliderValue = -1f;
        }
        _draggingSlider = false;
        _draggingSliderIndex = -1;
        _resizingPanel = false;
    }

    public bool HandleWheel(float delta, float windowHeight, float contentOffset)
    {
        if (!_visible) return false;

        _scrollOffset = Math.Clamp(_scrollOffset - delta * 0.5f, 0, Math.Max(0, _contentHeight - (windowHeight - contentOffset - 40) + 40));
        return true;
    }

    private float _pendingSliderValue = -1f;

    private void UpdateSliderValue(int index, float mouseX, float xPos, float panelLeft)
    {
        var item = _items[index];
        float sliderLeft = xPos + 8;
        float sliderWidth = _panelWidth - 64;

        float t = (mouseX - sliderLeft) / sliderWidth;
        t = Math.Clamp(t, 0, 1);

        float val = item.SliderMin + t * (item.SliderMax - item.SliderMin);
        val = MathF.Round(val / 0.25f) * 0.25f;

        // Store pending value, don't trigger heavy operations yet
        _pendingSliderValue = val;

        // Update local display value only
        item.SliderValue = (val - item.SliderMin) / (item.SliderMax - item.SliderMin);
        _items[index] = item;
    }

    public int GetScrollOffset() => (int)_scrollOffset;
}
