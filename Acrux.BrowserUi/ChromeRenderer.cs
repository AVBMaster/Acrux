using SkiaSharp;
using Acrux.Core;

namespace Acrux.Rendering;

public class ChromeRenderer : IImeSupport
{
    // Thread synchronization for tab data
    private readonly object _tabLock = new();
    private readonly ReaderWriterLockSlim _tabRwLock = new();

    private const float UrlBarHeight = 30;
    private const float TabBarHeight = 36;
    private const float ToolbarHeight = 44;
    private const float StatusBarHeight = 22;
    private const float BorderRadius = 6;

    private SKTypeface? _chineseTypeface;
    private SKFont _font12 = null!;
    private SKFont _font13 = null!;
    private SKFont _font14 = null!;
    private SKFont _font22 = null!;
    private SKFont _font11 = null!;
    private SKFont _fontClose = null!;

    private SKPaint _tabBgPaint = null!;
    private SKPaint _tabActivePaint = null!;
    private SKPaint _tabHoverPaint = null!;
    private SKPaint _toolbarBgPaint = null!;
    private SKPaint _statusBgPaint = null!;
    private SKPaint _urlBgPaint = null!;
    private SKPaint _urlBorderPaint = null!;
    private SKPaint _urlFocusBorderPaint = null!;
    private SKPaint _textPrimary = null!;
    private SKPaint _textSecondary = null!;
    private SKPaint _textBlue = null!;
    private SKPaint _iconPaint = null!;
    private SKPaint _iconDisabledPaint = null!;
    private SKPaint _btnHoverPaint = null!;
    private SKPaint _btnActivePaint = null!;
    private SKPaint _closeBtnBgPaint = null!;
    private SKPaint _closeBtnX = null!;
    private SKPaint _cursorPaint = null!;
    private SKPaint _lockPaint = null!;
    private SKPaint _infoPaint = null!;
    private SKPaint _newTabBgPaint = null!;
    private SKPaint _newTabHoverBgPaint = null!;
    private SKPaint _newTabPlusPaint = null!;
    private SKPaint _statusTextPaint = null!;
    private SKPaint _separatorPaint = null!;
    private SKPaint _progressPaint = null!;
    private SKPaint _progressBgPaint = null!;
    private SKPaint _shadowPaint = null!;

    private bool _backHovered;
    private bool _forwardHovered;
    private bool _refreshHovered;
    private bool _homeHovered;
    private bool _settingsHovered;
    private bool _taskManagerHovered;
    private bool _urlBarFocused;

    private SKRect _backButtonRect;
    private SKRect _forwardButtonRect;
    private SKRect _refreshButtonRect;
    private SKRect _homeButtonRect;
    private SKRect _settingsButtonRect;
    private SKRect _taskManagerButtonRect;
    private SKRect _urlBarRect;

    private List<TabInfo> _tabs = new();
    private int _activeTabIndex = 0;
    private List<SKRect> _tabRects = new();
    private List<SKRect> _tabCloseRects = new();
    private SKRect _newTabButtonRect;
    private int _hoveredTabIndex = -1;
    private int _hoveredCloseIndex = -1;
    private bool _newTabHovered;

    private int _selStart = -1;
    private SKPaint _selectionPaint = null!;
    private bool _urlBarMouseDown;

    private List<string> _history = new();
    private int _historyIndex = -1;

    private string _currentUrl = "";
    private string _urlBarText = "";
    private int _cursorPosition = 0;
    private bool _showCursor = true;
    private long _lastCursorBlinkTick = Environment.TickCount64;

    private bool _isImeComposing;
    private string _imeCompositionString = "";
    private int _imeCompositionCursor = 0;

    private bool _isLoading;
    private float _loadingProgress;
    private long _loadingStartTime;
    private bool _progressDirty; // 标记进度条需要渲染一次 100% 后消失
    private bool _progressJustCleared; // 进度条刚被清除，需要一帧来清理残留

    public Action<string>? OnNavigate { get; set; }
    public Action? OnRefresh { get; set; }
    public Action? OnHome { get; set; }
    public Action<string>? OnTabChanged { get; set; }
    public Action? OnNewTab { get; set; }
    public Action<int>? OnCloseTab { get; set; }
    public Action? OnUrlBarFocus { get; set; }
    public Action? OnUrlBarBlur { get; set; }
    public Action? OnSettingsClick { get; set; }
    public Action? OnTaskManagerClick { get; set; }
    public Action? OnChanged { get; set; }
    public Action<int, int>? OnTabReordered { get; set; }

    // Tab drag-reorder state
    private int _dragTabIndex = -1;
    private bool _isDraggingTab;
    private float _dragStartX;
    private float _dragCurrentX;
    private int _dropTargetIndex = -1;

    public class TabInfo
    {
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "acrux://newtab";
        public bool IsActive { get; set; }
        public bool IsLoading { get; set; }
        public float LoadingProgress { get; set; }
        public string TooltipText { get; set; } = "";
    }

    public void Initialize()
    {
        _chineseTypeface = FontHelper.GetChineseTypeface();
        _font11 = FontHelper.CreateChineseFont(11);
        _font12 = FontHelper.CreateChineseFont(12);
        _font13 = FontHelper.CreateChineseFont(13);
        _font14 = FontHelper.CreateChineseFont(14);
        _font22 = FontHelper.CreateChineseFont(22);
        _fontClose = FontHelper.CreateChineseFont(10);

        _tabBgPaint = new SKPaint { Color = SKColor.Parse("#F1F3F4"), Style = SKPaintStyle.Fill };
        _tabActivePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        _tabHoverPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill, IsAntialias = true };

        _toolbarBgPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        _statusBgPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };

        _urlBgPaint = new SKPaint { Color = SKColor.Parse("#F1F3F4"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _urlBorderPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        _urlFocusBorderPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };

        _textPrimary = new SKPaint { Color = SKColor.Parse("#202124"), IsAntialias = true };
        _textSecondary = new SKPaint { Color = SKColor.Parse("#5F6368"), IsAntialias = true };
        _textBlue = new SKPaint { Color = SKColor.Parse("#1A73E8"), IsAntialias = true };

        _iconPaint = new SKPaint { Color = SKColor.Parse("#5F6368"), IsAntialias = true, Style = SKPaintStyle.Fill, StrokeWidth = 0 };
        _iconDisabledPaint = new SKPaint { Color = SKColor.Parse("#BDC1C6"), IsAntialias = true, Style = SKPaintStyle.Fill };
        _btnHoverPaint = new SKPaint { Color = SKColor.Parse("#E8F0FE"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _btnActivePaint = new SKPaint { Color = SKColor.Parse("#D2E3FC"), Style = SKPaintStyle.Fill, IsAntialias = true };

        _closeBtnBgPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _closeBtnX = new SKPaint { Color = SKColor.Parse("#5F6368"), IsAntialias = true };
        _cursorPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Fill, StrokeWidth = 1.5f };
        _selectionPaint = new SKPaint { Color = SKColor.Parse("#D2E3FC"), Style = SKPaintStyle.Fill };

        _lockPaint = new SKPaint { Color = SKColor.Parse("#34A853"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        _infoPaint = new SKPaint { Color = SKColor.Parse("#F9A0"), IsAntialias = true };

        _newTabBgPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _newTabHoverBgPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _newTabPlusPaint = new SKPaint { Color = SKColor.Parse("#5F6368"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

        _statusTextPaint = new SKPaint { Color = SKColor.Parse("#5F6368"), IsAntialias = true };
        _separatorPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };

        _progressPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Fill };
        _progressBgPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };

        _shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 12),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
        };

        _tabs.Add(new TabInfo { Title = "New Tab", Url = "acrux://newtab", IsActive = true });
    }

    // ==================== 渲染方法 ====================

    /// <summary>Update a background tab's title without making it active.</summary>
    public void SetTabTitle(int index, string title)
    {
        _tabRwLock.EnterWriteLock();
        try
        {
            if (index >= 0 && index < _tabs.Count && !string.IsNullOrEmpty(title))
                _tabs[index].Title = title;
        }
        finally { _tabRwLock.ExitWriteLock(); }
    }

    /// <summary>Update a background tab's URL without making it active.</summary>
    public void SetTabUrl(int index, string url)
    {
        _tabRwLock.EnterWriteLock();
        try
        {
            if (index >= 0 && index < _tabs.Count)
                _tabs[index].Url = url;
        }
        finally { _tabRwLock.ExitWriteLock(); }
    }

    public void RenderChrome(SKCanvas canvas, float width, float height, string url, string title)
    {        _tabRwLock.EnterWriteLock();
        try
        {
            _currentUrl = url;
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
            {
                _tabs[_activeTabIndex].Title = title;
                _tabs[_activeTabIndex].Url = url;
            }
        }
        finally { _tabRwLock.ExitWriteLock(); }
        RenderTabBar(canvas, width);
        RenderToolbar(canvas, width, url);
        // 进度条画在工具栏下方（避免被工具栏覆盖）
        RenderLoadingProgress(canvas, width);
        RenderStatusBar(canvas, width, height);
    }

    public struct TabSnapshot
    {
        public string Title;
        public string Url;
        public bool IsActive;
        public bool IsLoading;
        public float LoadingProgress;
    }

    private void RenderTabBar(SKCanvas canvas, float width)
    {
        _tabRects.Clear();
        _tabCloseRects.Clear();

        float tabTop = 0;
        float tabHeight = TabBarHeight;
        float newTabBtnSize = 28;

        // Tab area background
        canvas.DrawRect(0, 0, width, tabHeight, _tabBgPaint);

        // Right side: window control area placeholder + new tab button
        float rightArea = 135;
        float tabsMaxX = width - rightArea - newTabBtnSize - 8;

        // Take a snapshot of tabs for thread safety
        var snapshot = SnapshotTabs();
        int tabCount = snapshot.Length;
        float tabStartX = 0;
        float tabGap = 2;
        float maxTabWidth = 200;
        float tabWidth = Math.Min(maxTabWidth, (tabsMaxX - tabStartX - tabGap * (tabCount - 1)) / Math.Max(1, tabCount));
        tabWidth = Math.Max(60, tabWidth);

        float tabX = tabStartX;
        float tabY = 6;
        float tabH = tabHeight - tabY;

        // Track insertion indicator position
        float insertionX = -1;

        for (int i = 0; i < tabCount; i++)
        {
            bool isDragTab = _isDraggingTab && i == _dragTabIndex;

            float actualWidth = tabWidth;
            if (tabX + actualWidth > tabsMaxX - 8)
                actualWidth = Math.Max(40, tabsMaxX - tabX - 8);
            if (actualWidth < 40) break;

            var tabRect = new SKRect(tabX, tabY, tabX + actualWidth, tabY + tabH);
            _tabRects.Add(tabRect);

            bool isActive = i == _activeTabIndex;
            bool isHovered = i == _hoveredTabIndex;

            // Draw tab background with rounded top corners
            var tabPb = new SKPathBuilder();
            float r = 8;
            tabPb.MoveTo(tabRect.Left + r, tabRect.Top);
            tabPb.LineTo(tabRect.Right - r, tabRect.Top);
            tabPb.QuadTo(tabRect.Right, tabRect.Top, tabRect.Right, tabRect.Top + r);
            tabPb.LineTo(tabRect.Right, tabRect.Bottom);
            tabPb.LineTo(tabRect.Left, tabRect.Bottom);
            tabPb.LineTo(tabRect.Left, tabRect.Top + r);
            tabPb.QuadTo(tabRect.Left, tabRect.Top, tabRect.Left + r, tabRect.Top);
            tabPb.Close();
            using var tabPath = tabPb.Detach();

            if (!isDragTab)
            {
                if (isActive)
                    canvas.DrawPath(tabPath, _tabActivePaint);
                else if (isHovered)
                    canvas.DrawPath(tabPath, _tabHoverPaint);
            }

            // Active tab indicator underline
            if (isActive && !isDragTab)
            {
                using var indicatorPaint = new SKPaint
                {
                    Color = SKColor.Parse("#1A73E8"),
                    Style = SKPaintStyle.Fill,
                    StrokeWidth = 0
                };
                canvas.DrawRoundRect(tabRect.Left + 8, tabRect.Bottom - 3, tabRect.Width - 16, 3, 1.5f, 1.5f, indicatorPaint);
            }

            // Tab title
            string tabTitle = snapshot[i].Title;
            float closeBtnW = 20;
            float textMaxW = tabRect.Width - 16 - closeBtnW;

            _font12.Size = 12;
            while (tabTitle.Length > 1 && _font12.MeasureText(tabTitle + "…") > textMaxW)
                tabTitle = tabTitle[..^1];
            if (tabTitle.Length < snapshot[i].Title.Length)
                tabTitle += "…";

            if (!isDragTab)
            {
                var titleColor = isActive ? _textPrimary : _textSecondary;
                float textX = tabRect.Left + 8;
                float textY = tabRect.Top + tabH * 0.62f;
                canvas.DrawText(tabTitle, textX, textY, SKTextAlign.Left, _font12, titleColor);
            }

            // Close button
            float closeS = 16;
            float closeX = tabRect.Right - closeS - 5;
            float closeY = tabRect.Top + (tabH - closeS) / 2;
            var closeRect = new SKRect(closeX, closeY, closeX + closeS, closeY + closeS);
            _tabCloseRects.Add(closeRect);

            if (!isDragTab)
            {
                bool closeHovered = i == _hoveredCloseIndex;
                if (closeHovered)
                    canvas.DrawRoundRect(closeRect, 3, 3, _closeBtnBgPaint);

                // Draw × (SKPath)
                float xPad = 5;
                using (var xPaint = new SKPaint { Color = closeHovered ? SKColor.Parse("#202124") : SKColor.Parse("#80868B"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true })
                {
                    var xPb = new SKPathBuilder();
                    xPb.MoveTo(closeRect.Left + xPad, closeRect.Top + xPad);
                    xPb.LineTo(closeRect.Right - xPad, closeRect.Bottom - xPad);
                    xPb.MoveTo(closeRect.Right - xPad, closeRect.Top + xPad);
                    xPb.LineTo(closeRect.Left + xPad, closeRect.Bottom - xPad);
                    using var xPath = xPb.Detach();
                    canvas.DrawPath(xPath, xPaint);
                }
            }

            // Separator between tabs (only inactive, non-hovered)
            if (!isActive && !isHovered && i > 0)
            {
                var prevTab = _tabRects[i - 1];
                using var sepPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
                canvas.DrawLine(tabRect.Left, tabRect.Top + 8, tabRect.Left, tabRect.Bottom - 4, sepPaint);
            }

            tabX += actualWidth + tabGap;
        }

        // Compute insertion indicator position
        if (_isDraggingTab && _dropTargetIndex >= 0)
        {
            if (_dropTargetIndex < _tabRects.Count)
                insertionX = _tabRects[_dropTargetIndex].Left;
            else if (_tabRects.Count > 0)
                insertionX = _tabRects[^1].Right;
            else
                insertionX = 0;
        }

        // Ghost tab for dragging
        if (_isDraggingTab && _dragTabIndex >= 0 && _dragTabIndex < snapshot.Length)
        {
            float ghostW = tabWidth;
            float ghostH = tabH;
            float ghostX = _dragCurrentX - ghostW / 2;
            float ghostY = tabY;
            var ghostRect = new SKRect(ghostX, ghostY, ghostX + ghostW, ghostY + ghostH);
            using var ghostBg = new SKPaint { Color = new SKColor(255, 255, 255, 200), Style = SKPaintStyle.Fill, IsAntialias = true };
            var ghostPb = new SKPathBuilder();
            float gr = 8;
            ghostPb.MoveTo(ghostRect.Left + gr, ghostRect.Top);
            ghostPb.LineTo(ghostRect.Right - gr, ghostRect.Top);
            ghostPb.QuadTo(ghostRect.Right, ghostRect.Top, ghostRect.Right, ghostRect.Top + gr);
            ghostPb.LineTo(ghostRect.Right, ghostRect.Bottom);
            ghostPb.LineTo(ghostRect.Left, ghostRect.Bottom);
            ghostPb.LineTo(ghostRect.Left, ghostRect.Top + gr);
            ghostPb.QuadTo(ghostRect.Left, ghostRect.Top, ghostRect.Left + gr, ghostRect.Top);
            ghostPb.Close();
            using var ghostPath = ghostPb.Detach();
            canvas.DrawPath(ghostPath, ghostBg);
            using var ghostBorder = new SKPaint { Color = new SKColor(0, 0, 0, 30), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
            canvas.DrawPath(ghostPath, ghostBorder);
            string ghostTitle = snapshot[_dragTabIndex].Title;
            _font12.Size = 12;
            while (_font12.MeasureText(ghostTitle + "…") > ghostW - 16)
                ghostTitle = ghostTitle[..^1];
            if (ghostTitle.Length < snapshot[_dragTabIndex].Title.Length)
                ghostTitle += "…";
            canvas.DrawText(ghostTitle, ghostRect.Left + 8, ghostRect.Top + ghostH * 0.62f, SKTextAlign.Left, _font12, _textSecondary);
        }

        // Insertion indicator
        if (_isDraggingTab && insertionX >= 0)
        {
            using var indicatorPaint = new SKPaint
            {
                Color = SKColor.Parse("#1A73E8"),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(insertionX - 2, tabY + 4, 4, tabH - 8, 2, 2, indicatorPaint);
        }

        // New tab button
        float newTabX = Math.Max(tabX, tabsMaxX - newTabBtnSize) + 4;
        float newTabY = tabY + (tabH - newTabBtnSize) / 2;
        _newTabButtonRect = new SKRect(newTabX, newTabY, newTabX + newTabBtnSize, newTabY + newTabBtnSize);

        var ntBg = _newTabHovered ? _newTabHoverBgPaint : _newTabBgPaint;
        canvas.DrawRoundRect(_newTabButtonRect, 6, 6, ntBg);

        // Draw + with SKPath
        float plusPad = 7;
        float plusCx = _newTabButtonRect.MidX;
        float plusCy = _newTabButtonRect.MidY;
        float plusHalf = (_newTabButtonRect.Width - plusPad * 2) / 2;
        var plusPb = new SKPathBuilder();
        plusPb.MoveTo(plusCx - plusHalf, plusCy);
        plusPb.LineTo(plusCx + plusHalf, plusCy);
        plusPb.MoveTo(plusCx, plusCy - plusHalf);
        plusPb.LineTo(plusCx, plusCy + plusHalf);
        using var plusPath = plusPb.Detach();
        canvas.DrawPath(plusPath, _newTabPlusPaint);
    }

    public void RenderLoadingProgress(SKCanvas canvas, float width)
    {
        if (!_isLoading && !_progressDirty)
            return;

        // 确保进度条在加载中始终被渲染
        _progressDirty = true;

        UpdateLoadingProgress();

        float progressY = TabBarHeight + ToolbarHeight;
        float barHeight = 2;

        canvas.DrawRect(0, progressY, width, barHeight, _progressBgPaint);
        float pct = _loadingProgress > 0 ? _loadingProgress : 0.05f;
        canvas.DrawRect(0, progressY, width * pct, barHeight, _progressPaint);

        if (!_isLoading && _loadingProgress >= 1f)
        {
            _progressDirty = false;
            _progressJustCleared = true;
        }
    }

    private void RenderToolbar(SKCanvas canvas, float width, string url)
    {
        float tTop = TabBarHeight;
        float tH = ToolbarHeight;
        float tCenter = tTop + tH / 2;

        // Toolbar background
        canvas.DrawRect(0, tTop, width, tH, _toolbarBgPaint);

        // Subtle separator below toolbar
        using var sep = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, tTop + tH - 1, width, 1, sep);

        // Navigation buttons
        float btnY = tTop + (tH - 28) / 2;
        float btnSize = 28;

        _backButtonRect = new SKRect(8, btnY, 8 + btnSize, btnY + btnSize);
        DrawNavButton(canvas, _backButtonRect, _backHovered, CanGoBack());
        DrawArrowLeft(canvas, _backButtonRect, CanGoBack());

        _forwardButtonRect = new SKRect(40, btnY, 40 + btnSize, btnY + btnSize);
        DrawNavButton(canvas, _forwardButtonRect, _forwardHovered, CanGoForward());
        DrawArrowRight(canvas, _forwardButtonRect, CanGoForward());

        _refreshButtonRect = new SKRect(72, btnY, 72 + btnSize, btnY + btnSize);
        DrawNavButton(canvas, _refreshButtonRect, _refreshHovered, true);
        DrawRefresh(canvas, _refreshButtonRect);

        _homeButtonRect = new SKRect(104, btnY, 104 + btnSize, btnY + btnSize);
        DrawNavButton(canvas, _homeButtonRect, _homeHovered, true);
        DrawHome(canvas, _homeButtonRect);

        // Settings
        _settingsButtonRect = new SKRect(136, btnY, 136 + btnSize, btnY + btnSize);
        DrawNavButton(canvas, _settingsButtonRect, _settingsHovered, true);
        DrawSettings(canvas, _settingsButtonRect);

        // Task Manager
        _taskManagerButtonRect = new SKRect(168, btnY, 168 + btnSize, btnY + btnSize);
        DrawNavButton(canvas, _taskManagerButtonRect, _taskManagerHovered, true);
        DrawTaskManager(canvas, _taskManagerButtonRect);

        // URL bar
        float urlLeft = 204;
        float urlW = Math.Max(60, width - urlLeft - 10);
        float urlTop = tTop + 6;
        float urlH = tH - 12;
        _urlBarRect = new SKRect(urlLeft, urlTop, urlLeft + urlW, urlTop + urlH);

        // URL bar shadow
        canvas.DrawRoundRect(_urlBarRect, BorderRadius, BorderRadius, _shadowPaint);
        canvas.DrawRoundRect(_urlBarRect, BorderRadius, BorderRadius, _urlBgPaint);

        if (_urlBarFocused)
            canvas.DrawRoundRect(_urlBarRect, BorderRadius, BorderRadius, _urlFocusBorderPaint);
        else
            canvas.DrawRoundRect(_urlBarRect, BorderRadius, BorderRadius, _urlBorderPaint);

        // Security icon
        float iconX = urlLeft + 10;
        float iconY = tCenter + 2;
        if (url.StartsWith("https"))
            DrawLockIcon(canvas, iconX, iconY);
        else if (!url.StartsWith("acrux://"))
            DrawInfoIcon(canvas, iconX, iconY);

        // URL text
        float textX = url.StartsWith("https") ? iconX + 20 : (url.StartsWith("acrux://") ? iconX : iconX + 20);
        float textY = tCenter + 2;

        string displayUrl = _urlBarFocused ? _urlBarText : url;
        if (!_urlBarFocused && _font14.MeasureText(displayUrl) > urlW - (textX - urlLeft) - 16)
        {
            while (_font14.MeasureText(displayUrl + "…") > urlW - (textX - urlLeft) - 16 && displayUrl.Length > 4)
                displayUrl = displayUrl[..^1];
            if (displayUrl.Length < url.Length)
                displayUrl += "…";
        }

        // Selection highlight
        if (_urlBarFocused && _selStart >= 0 && _selStart != _cursorPosition)
        {
            int len = _urlBarText.Length;
            int selMin = Math.Clamp(Math.Min(_selStart, _cursorPosition), 0, len);
            int selMax = Math.Clamp(Math.Max(_selStart, _cursorPosition), 0, len);
            if (selMax > selMin)
            {
                string beforeSel = _urlBarText[..selMin];
                float selX = textX + _font14.MeasureText(beforeSel);
                float selW = _font14.MeasureText(_urlBarText[selMin..selMax]);
                canvas.DrawRect(selX, textY - 11, selW, 15, _selectionPaint);
            }
        }

        _textPrimary.Color = _urlBarFocused ? SKColor.Parse("#1A73E8") : SKColor.Parse("#3C4043");
        canvas.DrawText(displayUrl, textX, textY, SKTextAlign.Left, _font14, _textPrimary);

        // Cursor
        if (_urlBarFocused && _showCursor)
        {
            string beforeCursor = _urlBarText[..Math.Min(_cursorPosition, _urlBarText.Length)];
            float cursorX = textX + _font14.MeasureText(beforeCursor);
            canvas.DrawLine(cursorX, iconY - 10, cursorX, iconY + 4, _cursorPaint);
        }
    }

    private void DrawNavButton(SKCanvas canvas, SKRect rect, bool hovered, bool enabled)
    {
        if (!enabled) return;
        if (hovered)
        {
            canvas.DrawRoundRect(rect, 6, 6, _btnHoverPaint);
        }
    }

    private void DrawArrowLeft(SKCanvas canvas, SKRect rect, bool enabled)
    {
        var paint = enabled ? _iconPaint : _iconDisabledPaint;
        float cx = rect.MidX;
        float cy = rect.MidY;
        float s = 6;
        var pb = new SKPathBuilder();
        pb.MoveTo(cx + s * 0.5f, cy - s);
        pb.LineTo(cx - s * 0.5f, cy);
        pb.LineTo(cx + s * 0.5f, cy + s);
        using var path = pb.Detach();
        canvas.DrawPath(path, paint);
    }

    private void DrawArrowRight(SKCanvas canvas, SKRect rect, bool enabled)
    {
        var paint = enabled ? _iconPaint : _iconDisabledPaint;
        float cx = rect.MidX;
        float cy = rect.MidY;
        float s = 6;
        var pb = new SKPathBuilder();
        pb.MoveTo(cx - s * 0.5f, cy - s);
        pb.LineTo(cx + s * 0.5f, cy);
        pb.LineTo(cx - s * 0.5f, cy + s);
        using var path = pb.Detach();
        canvas.DrawPath(path, paint);
    }

    private void DrawRefresh(SKCanvas canvas, SKRect rect)
    {
        float cx = rect.MidX;
        float cy = rect.MidY;
        float r = 5;
        var pb = new SKPathBuilder();
        // Arrow circle
        pb.AddCircle(cx, cy, r);
        // Arrow head
        float ax = cx;
        float ay = cy - r - 2;
        pb.MoveTo(ax, ay);
        pb.LineTo(ax - 3, ay + 3);
        pb.MoveTo(ax, ay);
        pb.LineTo(ax + 3, ay + 3);
        using var path = pb.Detach();

        using var refreshPaint = new SKPaint
        {
            Color = _iconPaint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f
        };
        canvas.DrawPath(path, refreshPaint);
    }

    private void DrawHome(SKCanvas canvas, SKRect rect)
    {
        float cx = rect.MidX;
        float cy = rect.MidY;
        float s = 7;
        var pb = new SKPathBuilder();
        // House shape
        pb.MoveTo(cx, cy - s);
        pb.LineTo(cx - s, cy - 1);
        pb.LineTo(cx - s + 2, cy - 1);
        pb.LineTo(cx - s + 2, cy + s - 2);
        pb.LineTo(cx + s - 2, cy + s - 2);
        pb.LineTo(cx + s - 2, cy - 1);
        pb.LineTo(cx + s, cy - 1);
        pb.Close();
        // Chimney
        pb.MoveTo(cx + 2, cy - s);
        pb.LineTo(cx + 4, cy - s + 3);
        pb.LineTo(cx + 4, cy - s + 1);
        pb.LineTo(cx + 2, cy - s + 1);
        pb.Close();
        using var path = pb.Detach();

        using var homePaint = new SKPaint
        {
            Color = _iconPaint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(path, homePaint);
    }

    private void DrawSettings(SKCanvas canvas, SKRect rect)
    {
        float cx = rect.MidX;
        float cy = rect.MidY;
        float r = 5;
        var pb = new SKPathBuilder();
        pb.AddCircle(cx, cy, r);
        // Gear teeth (simplified as dots)
        for (int i = 0; i < 6; i++)
        {
            float angle = i * MathF.PI / 3 - MathF.PI / 6;
            float tx = cx + MathF.Cos(angle) * (r + 2);
            float ty = cy + MathF.Sin(angle) * (r + 2);
            pb.AddCircle(tx, ty, 1.5f);
        }
        using var path = pb.Detach();

        using var settingsPaint = new SKPaint
        {
            Color = _iconPaint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(path, settingsPaint);
    }

    private void DrawTaskManager(SKCanvas canvas, SKRect rect)
    {
        float cx = rect.MidX;
        float cy = rect.MidY;
        var pb = new SKPathBuilder();
        // Grid icon - 3x2 dots representing processes/tasks
        float spacing = 4.5f;
        float startX = cx - spacing;
        float startY = cy - spacing * 0.5f;
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                float dotX = startX + col * spacing;
                float dotY = startY + row * spacing;
                pb.AddCircle(dotX, dotY, 2);
            }
        }
        using var path = pb.Detach();
        using var tmPaint = new SKPaint
        {
            Color = _iconPaint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(path, tmPaint);
    }

    private void DrawLockIcon(SKCanvas canvas, float x, float y)
    {
        float s = 5;
        var pb = new SKPathBuilder();
        // Lock body
        pb.MoveTo(x - s * 0.7f, y + s * 0.5f);
        pb.LineTo(x + s * 0.7f, y + s * 0.5f);
        pb.LineTo(x + s * 0.7f, y - s * 0.1f);
        pb.LineTo(x - s * 0.7f, y - s * 0.1f);
        pb.Close();
        // Lock shackle
        pb.MoveTo(x - s * 0.45f, y - s * 0.1f);
        pb.LineTo(x - s * 0.45f, y - s * 0.6f);
        pb.ArcTo(x + s * 0.45f, y - s * 0.1f, x + s * 0.45f, y - s * 0.6f, s * 0.45f);
        pb.LineTo(x + s * 0.45f, y - s * 0.1f);
        using var path = pb.Detach();

        _lockPaint.Style = SKPaintStyle.Stroke;
        canvas.DrawPath(path, _lockPaint);
        // Keyhole
        canvas.DrawCircle(x, y + s * 0.15f, 1.2f, _lockPaint);
    }

    private void DrawInfoIcon(SKCanvas canvas, float x, float y)
    {
        float r = 5;
        canvas.DrawCircle(x, y, r, _infoPaint);
        _infoPaint.Color = SKColors.White;
        _infoPaint.Style = SKPaintStyle.Fill;
        canvas.DrawText("i", x - 2, y + 3.5f, SKTextAlign.Left, _font11, _infoPaint);
        _infoPaint.Style = SKPaintStyle.Stroke;
        _infoPaint.Color = SKColor.Parse("#F9AB00");
    }

    private void RenderStatusBar(SKCanvas canvas, float width, float height)
    {
        float sTop = height - StatusBarHeight;

        canvas.DrawRect(0, sTop, width, StatusBarHeight, _statusBgPaint);
        using var topLine = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, sTop, width, 1, topLine);

        string statusText = _isLoading ? "Loading..." : (string.IsNullOrEmpty(_currentUrl) ? "Ready" : _currentUrl);
        if (_font12.MeasureText(statusText) > width - 150)
        {
            while (_font12.MeasureText(statusText + "…") > width - 150 && statusText.Length > 4)
                statusText = statusText[..^1];
            if (statusText.Length < (string.IsNullOrEmpty(_currentUrl) ? 5 : _currentUrl.Length))
                statusText += "…";
        }

        canvas.DrawText(statusText, 12, sTop + StatusBarHeight * 0.68f, SKTextAlign.Left, _font12, _statusTextPaint);

        string tabInfo = $"{TabCount} tabs";
        float tiW = _font12.MeasureText(tabInfo);
        canvas.DrawText(tabInfo, width - tiW - 12, sTop + StatusBarHeight * 0.68f, SKTextAlign.Left, _font12, _statusTextPaint);
    }

    public void RenderTabTooltip(SKCanvas canvas, float width, float height)
    {
        var snap = SnapshotTabs();
        if (_hoveredTabIndex < 0 || _hoveredTabIndex >= snap.Length) return;
        var tab = snap[_hoveredTabIndex];
        if (string.IsNullOrEmpty(tab.TooltipText)) return;

        using var tooltipFont = new SKFont(_chineseTypeface ?? SKTypeface.Default, 12);
        // The tooltip sits on a semi-transparent dark box, so LCD subpixel
        // filtering would fringe the glyph edges; use grayscale AA instead.
        tooltipFont.Edging = SKFontEdging.Antialias;
        tooltipFont.Subpixel = false;
        tooltipFont.Hinting = FontHelper.CrispHinting(tooltipFont.Typeface);
        using var tipBg = new SKPaint { Color = new SKColor(50, 50, 50, 230), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var tipText = new SKPaint { Color = SKColors.White, IsAntialias = true };

        float lineH = 18;
        int lineCount = tab.TooltipText.Count(c => c == '\n') + 1;
        float tipW = Math.Min(360, width - 40);
        float tipH = lineCount * lineH + 12;

        // position below the hovered tab
        var tabRect = _hoveredTabIndex < _tabRects.Count ? _tabRects[_hoveredTabIndex] : _tabRects[^1];
        float tipX = Math.Max(4, Math.Min(tabRect.Left, width - tipW - 4));
        float tipY = tabRect.Bottom + 4;

        // clamp to viewport
        if (tipY + tipH > height - 24)
            tipY = tabRect.Top - tipH - 4;
        if (tipY < 4) tipY = 4;

        canvas.DrawRoundRect(tipX, tipY, tipW, tipH, 6, 6, tipBg);

        float textX = tipX + 8;
        float textY = tipY + lineH;
        string[] lines = tab.TooltipText.Split('\n');
        foreach (var line in lines)
        {
            canvas.DrawText(line, textX, textY, SKTextAlign.Left, tooltipFont, tipText);
            textY += lineH;
        }
    }

    // ==================== 输入处理方法 ====================

    private float GetUrlBarTextStart()
    {
        float iconX = _urlBarRect.Left + 10;
        if (_currentUrl.StartsWith("https")) return iconX + 20;
        if (_currentUrl.StartsWith("acrux://")) return iconX;
        return iconX + 20;
    }

    private int GetCharIndexAtX(float x)
    {
        float textStart = GetUrlBarTextStart();
        float rx = x - textStart;
        if (rx <= 0) return 0;
        // binary search for character index
        int lo = 0, hi = _urlBarText.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            string sub = _urlBarText[..Math.Min(mid, _urlBarText.Length)];
            float w = _font14.MeasureText(sub);
            if (w <= rx) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    public string? UrlBarSelectedText
    {
        get
        {
            if (_selStart < 0 || _selStart == _cursorPosition) return null;
            int a = Math.Min(_selStart, _cursorPosition);
            int b = Math.Max(_selStart, _cursorPosition);
            return _urlBarText[a..b];
        }
    }

    public void SelectAllInUrlBar()
    {
        if (!_urlBarFocused) return;
        _selStart = 0;
        _cursorPosition = _urlBarText.Length;
    }

    public void HandleMouseMove(float x, float y)
    {
        // Selection drag in URL bar
        if (_urlBarMouseDown && _urlBarFocused)
        {
            int newPos = GetCharIndexAtX(x);
            if (newPos != _cursorPosition)
            {
                if (_selStart < 0) _selStart = _cursorPosition;
                _cursorPosition = newPos;
                OnChanged?.Invoke();
            }
        }

        bool oldBack = _backHovered;
        bool oldForward = _forwardHovered;
        bool oldRefresh = _refreshHovered;
        bool oldHome = _homeHovered;
        bool oldSettings = _settingsHovered;
        bool oldTaskManager = _taskManagerHovered;
        bool oldNewTab = _newTabHovered;
        int oldTab = _hoveredTabIndex;
        int oldClose = _hoveredCloseIndex;

        _backHovered = _backButtonRect.Contains(x, y);
        _forwardHovered = _forwardButtonRect.Contains(x, y);
        _refreshHovered = _refreshButtonRect.Contains(x, y);
        _homeHovered = _homeButtonRect.Contains(x, y);
        _settingsHovered = _settingsButtonRect.Contains(x, y);
        _taskManagerHovered = _taskManagerButtonRect.Contains(x, y);
        _newTabHovered = _newTabButtonRect.Contains(x, y);

        _hoveredTabIndex = -1;
        _hoveredCloseIndex = -1;

        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(x, y))
            {
                _hoveredTabIndex = i;
                break;
            }
        }

        for (int i = 0; i < _tabCloseRects.Count; i++)
        {
            if (_tabCloseRects[i].Contains(x, y))
            {
                _hoveredCloseIndex = i;
                break;
            }
        }

        // Tab drag tracking
        if (_dragTabIndex >= 0)
        {
            float dx = Math.Abs(x - _dragStartX);
            if (dx > 8)
                _isDraggingTab = true;
            if (_isDraggingTab)
            {
                _dragCurrentX = x;
                // Compute drop target
                _dropTargetIndex = TabCount;
                for (int i = 0; i < _tabRects.Count; i++)
                {
                    float mid = _tabRects[i].Left + _tabRects[i].Width / 2;
                    if (x < mid)
                    {
                        _dropTargetIndex = i;
                        break;
                    }
                }
            }
        }

        if (oldBack != _backHovered || oldForward != _forwardHovered ||
            oldRefresh != _refreshHovered || oldHome != _homeHovered ||
            oldSettings != _settingsHovered || oldTaskManager != _taskManagerHovered ||
            oldNewTab != _newTabHovered || oldTab != _hoveredTabIndex || oldClose != _hoveredCloseIndex)
        {
            OnChanged?.Invoke();
        }
    }

    public bool HandleMouseClick(float x, float y)
    {
        if (_newTabButtonRect.Contains(x, y))
        {
            AddTab();
            return true;
        }

        for (int i = 0; i < _tabCloseRects.Count; i++)
        {
            if (_tabCloseRects[i].Contains(x, y))
            {
                CloseTab(i);
                return true;
            }
        }

        // Record tab press for potential drag
        _dragTabIndex = -1;
        _isDraggingTab = false;
        _dropTargetIndex = -1;
        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(x, y))
            {
                _dragTabIndex = i;
                _dragStartX = x;
                _dragCurrentX = x;
                return true;
            }
        }

        if (_urlBarRect.Contains(x, y))
        {
            if (!_urlBarFocused)
            {
                _urlBarFocused = true;
                _urlBarText = _currentUrl;
                _urlBarMouseDown = true;
                OnUrlBarFocus?.Invoke();
            }
            _selStart = -1;
            _cursorPosition = GetCharIndexAtX(x);
            _urlBarMouseDown = true;
            return true;
        }

        if (_backButtonRect.Contains(x, y) && CanGoBack())
        {
            BlurUrlBar();
            GoBack();
            return true;
        }

        if (_forwardButtonRect.Contains(x, y) && CanGoForward())
        {
            BlurUrlBar();
            GoForward();
            return true;
        }

        if (_refreshButtonRect.Contains(x, y))
        {
            BlurUrlBar();
            OnRefresh?.Invoke();
            return true;
        }

        if (_homeButtonRect.Contains(x, y))
        {
            BlurUrlBar();
            OnHome?.Invoke();
            return true;
        }

        if (_settingsButtonRect.Contains(x, y))
        {
            BlurUrlBar();
            OnSettingsClick?.Invoke();
            return true;
        }

        if (_taskManagerButtonRect.Contains(x, y))
        {
            BlurUrlBar();
            OnTaskManagerClick?.Invoke();
            return true;
        }

        BlurUrlBar();
        return false;
    }

    public void HandleMouseUp()
    {
        _urlBarMouseDown = false;

        if (_isDraggingTab && _dragTabIndex >= 0 && _dropTargetIndex >= 0 &&
            _dropTargetIndex != _dragTabIndex && _dropTargetIndex != _dragTabIndex + 1)
        {
            int from = _dragTabIndex;
            int to = _dropTargetIndex;
            if (to > from) to--; // adjust after removal
            var tab = _tabs[from];
            _tabs.RemoveAt(from);
            _tabs.Insert(to, tab);
            if (_activeTabIndex == from)
                _activeTabIndex = to;
            else if (from < _activeTabIndex && to >= _activeTabIndex)
                _activeTabIndex--;
            else if (from > _activeTabIndex && to <= _activeTabIndex)
                _activeTabIndex++;
            OnTabReordered?.Invoke(from, to);
            _dragTabIndex = -1;
            _isDraggingTab = false;
            _dropTargetIndex = -1;
            OnChanged?.Invoke();
            return;
        }

        // If we pressed on a tab but didn't drag, switch to it
        if (_dragTabIndex >= 0 && _dragTabIndex < _tabs.Count && !_isDraggingTab)
        {
            if (_dragTabIndex != _activeTabIndex)
                SwitchToTab(_dragTabIndex);
            _dragTabIndex = -1;
            OnChanged?.Invoke();
            return;
        }

        _dragTabIndex = -1;
        _isDraggingTab = false;
        _dropTargetIndex = -1;
    }

    private void BlurUrlBar()
    {
        if (_urlBarFocused)
        {
            _urlBarFocused = false;
            _selStart = -1;
            _urlBarMouseDown = false;
            OnUrlBarBlur?.Invoke();
        }
    }

    public bool HandleKeyPress(char keyChar, SKKey key, bool shift = false)
    {
        if (!_urlBarFocused) return false;

        // Normalize cursor and selection state (they can drift when _urlBarText changes externally)
        if (_cursorPosition > _urlBarText.Length)
            _cursorPosition = _urlBarText.Length;
        if (_selStart > _urlBarText.Length)
            _selStart = _urlBarText.Length;
        if (_cursorPosition < 0) _cursorPosition = 0;
        if (_selStart < -1) _selStart = -1;

        if (_isImeComposing)
        {
            if (key == SKKey.Escape)
            {
                _isImeComposing = false;
                _imeCompositionString = "";
                _imeCompositionCursor = 0;
                return true;
            }
            return true;
        }

        // Clear selection on non-shift navigation, or set anchor on first shift
        if (!shift && _selStart >= 0 && _selStart != _cursorPosition)
        {
            if (key == SKKey.Left || key == SKKey.Right || key == SKKey.Home || key == SKKey.End)
                _selStart = -1;
        }

        switch (key)
        {
            case SKKey.Enter:
                _selStart = -1;
                NavigateToUrl(_urlBarText);
                _urlBarFocused = false;
                return true;

            case SKKey.Escape:
                _selStart = -1;
                _urlBarFocused = false;
                _urlBarText = _currentUrl;
                return true;

            case SKKey.Left:
                if (shift)
                {
                    if (_selStart < 0) _selStart = _cursorPosition;
                    if (_cursorPosition > 0) _cursorPosition--;
                }
                else
                {
                    if (_cursorPosition > 0) _cursorPosition--;
                }
                return true;

            case SKKey.Right:
                if (shift)
                {
                    if (_selStart < 0) _selStart = _cursorPosition;
                    if (_cursorPosition < _urlBarText.Length) _cursorPosition++;
                }
                else
                {
                    if (_cursorPosition < _urlBarText.Length) _cursorPosition++;
                }
                return true;

            case SKKey.Home:
                if (shift)
                {
                    if (_selStart < 0) _selStart = _cursorPosition;
                    _cursorPosition = 0;
                }
                else
                {
                    _cursorPosition = 0;
                }
                return true;

            case SKKey.End:
                if (shift)
                {
                    if (_selStart < 0) _selStart = _cursorPosition;
                    _cursorPosition = _urlBarText.Length;
                }
                else
                {
                    _cursorPosition = _urlBarText.Length;
                }
                return true;

            case SKKey.Backspace:
                if (_selStart >= 0 && _selStart != _cursorPosition)
                {
                    int a = Math.Min(_selStart, _cursorPosition);
                    int b = Math.Max(_selStart, _cursorPosition);
                    _urlBarText = _urlBarText[..a] + _urlBarText[b..];
                    _cursorPosition = a;
                    _selStart = -1;
                }
                else if (_cursorPosition > 0)
                {
                    _urlBarText = _urlBarText[..(_cursorPosition - 1)] + _urlBarText[_cursorPosition..];
                    _cursorPosition--;
                }
                return true;

            case SKKey.Delete:
                if (_selStart >= 0 && _selStart != _cursorPosition)
                {
                    int a = Math.Min(_selStart, _cursorPosition);
                    int b = Math.Max(_selStart, _cursorPosition);
                    _urlBarText = _urlBarText[..a] + _urlBarText[b..];
                    _cursorPosition = a;
                    _selStart = -1;
                }
                else if (_cursorPosition < _urlBarText.Length)
                    _urlBarText = _urlBarText[.._cursorPosition] + _urlBarText[(_cursorPosition + 1)..];
                return true;

            default:
                if (!char.IsControl(keyChar))
                {
                    if (_selStart >= 0 && _selStart != _cursorPosition)
                    {
                        int a = Math.Min(_selStart, _cursorPosition);
                        int b = Math.Max(_selStart, _cursorPosition);
                        _urlBarText = _urlBarText[..a] + keyChar + _urlBarText[b..];
                        _cursorPosition = a + 1;
                        _selStart = -1;
                    }
                    else
                    {
                        _urlBarText = _urlBarText[.._cursorPosition] + keyChar + _urlBarText[_cursorPosition..];
                        _cursorPosition++;
                    }
                }
                return true;
        }
    }

    // ==================== 导航方法 ====================

    public void NavigateToUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.Contains("://") && !url.StartsWith("acrux://"))
        {
            if (url.Contains('.') || url.Contains('/'))
                url = "https://" + url;
            else
                url = "https://www.baidu.com/search?q=" + Uri.EscapeDataString(url);
        }

        _tabRwLock.EnterWriteLock();
        try
        {
            _currentUrl = url;
            _urlBarText = "";

            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(url);
            _historyIndex = _history.Count - 1;

            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
            {
                _tabs[_activeTabIndex].Url = url;
                _tabs[_activeTabIndex].Title = url;
            }
        }
        finally { _tabRwLock.ExitWriteLock(); }

        OnNavigate?.Invoke(url);
    }

    public void UpdateUrl(string url)
    {
        _tabRwLock.EnterWriteLock();
        try
        {
            _currentUrl = url;
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                _tabs[_activeTabIndex].Url = url;
            _urlBarText = "";
        }
        finally { _tabRwLock.ExitWriteLock(); }
    }

    public void GoBack()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            _currentUrl = _history[_historyIndex];
            OnNavigate?.Invoke(_currentUrl);
        }
    }

    public void GoForward()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            _currentUrl = _history[_historyIndex];
            OnNavigate?.Invoke(_currentUrl);
        }
    }

    public bool CanGoBack() => _historyIndex > 0;
    public bool CanGoForward() => _historyIndex < _history.Count - 1;

    // ==================== 标签页管理 ====================

    public void AddTab(string url = "acrux://newtab")
    {
        _tabRwLock.EnterWriteLock();
        try
        {
            _tabs.Add(new TabInfo { Title = "New Tab", Url = url });
            _activeTabIndex = _tabs.Count - 1;
            _urlBarFocused = false;
            _currentUrl = url;
        }
        finally { _tabRwLock.ExitWriteLock(); }
        OnTabChanged?.Invoke(url);
    }

    public void CloseTab(int index)
    {
        string? newUrl = null;
        _tabRwLock.EnterWriteLock();
        try
        {
            if (_tabs.Count <= 1)
            {
                _tabs[0] = new TabInfo { Title = "New Tab", Url = "acrux://newtab" };
                _activeTabIndex = 0;
                _currentUrl = "acrux://newtab";
                newUrl = "acrux://newtab";
            }
            else
            {
                if (index < 0 || index >= _tabs.Count) return;

                _tabs.RemoveAt(index);

                if (index == _activeTabIndex)
                {
                    if (_activeTabIndex >= _tabs.Count)
                        _activeTabIndex = _tabs.Count - 1;
                    _currentUrl = _tabs[_activeTabIndex].Url;
                    newUrl = _currentUrl;
                }
                else if (index < _activeTabIndex)
                {
                    _activeTabIndex--;
                }
            }
        }
        finally { _tabRwLock.ExitWriteLock(); }

        // 在锁外调用事件，避免与 OnCloseTab 中读取 _chrome.ActiveTabIndex 时发生死锁
        OnCloseTab?.Invoke(index);

        if (newUrl != null) OnTabChanged?.Invoke(newUrl);
    }

    public void SwitchToTab(int index)
    {
        string? url = null;
        _tabRwLock.EnterWriteLock();
        try
        {
            if (index < 0 || index >= _tabs.Count || index == _activeTabIndex) return;
            _tabs[_activeTabIndex].IsActive = false;
            _activeTabIndex = index;
            _tabs[_activeTabIndex].IsActive = true;
            BlurUrlBar();
            _currentUrl = _tabs[_activeTabIndex].Url;
            url = _currentUrl;
        }
        finally { _tabRwLock.ExitWriteLock(); }
        if (url != null) OnTabChanged?.Invoke(url);
    }

    public void NextTab()
    {
        _tabRwLock.EnterReadLock();
        int count = _tabs.Count;
        int active = _activeTabIndex;
        _tabRwLock.ExitReadLock();
        if (count <= 1) return;
        int next = (active + 1) % count;
        SwitchToTab(next);
    }

    public void PreviousTab()
    {
        _tabRwLock.EnterReadLock();
        int count = _tabs.Count;
        int active = _activeTabIndex;
        _tabRwLock.ExitReadLock();
        if (count <= 1) return;
        int prev = (active - 1 + count) % count;
        SwitchToTab(prev);
    }

    // ==================== 工具方法 ====================

    public bool UpdateCursorBlink()
    {
        if (Environment.TickCount64 - _lastCursorBlinkTick > 500)
        {
            _showCursor = !_showCursor;
            _lastCursorBlinkTick = Environment.TickCount64;
            return true;
        }
        return false;
    }

    public string GetCurrentUrl() => _currentUrl;
    public bool IsUrlBarFocused() => _urlBarFocused;
    public int TabCount
    {
        get { _tabRwLock.EnterReadLock(); try { return _tabs.Count; } finally { _tabRwLock.ExitReadLock(); } }
    }
    public int ActiveTabIndex
    {
        get { _tabRwLock.EnterReadLock(); try { return _activeTabIndex; } finally { _tabRwLock.ExitReadLock(); } }
    }
    public bool IsLoading => _isLoading;
    public float GetLoadingProgress() => _loadingProgress;
    public bool IsProgressDirty => _progressDirty;
    public bool IsProgressJustCleared
    {
        get
        {
            if (_progressJustCleared)
            {
                _progressJustCleared = false;
                return true;
            }
            return false;
        }
    }
    public IReadOnlyList<TabInfo> Tabs
    {
        get { _tabRwLock.EnterReadLock(); try { return _tabs.ToArray(); } finally { _tabRwLock.ExitReadLock(); } }
    }

    public void SetLoadingState(bool loading)
    {
        // Idempotent when already settled: PumpRemoteTabs calls this every frame, and a
        // no-op 'false' must not re-dirty the progress bar — that re-fires
        // OnChanged → NeedsRedraw → repaint, a self-sustaining 60Hz redraw loop.
        if (!loading && !_isLoading)
            return;
        _isLoading = loading;
        if (loading)
        {
            _loadingProgress = 0;
            _loadingStartTime = Environment.TickCount64;
        }
        else
        {
            _loadingProgress = 1.0f;
        }
        _progressDirty = true;
        OnChanged?.Invoke();
    }

    public void UpdateLoadingProgress()
    {
        if (!_isLoading) return;
        var elapsed = Environment.TickCount64 - _loadingStartTime;
        _loadingProgress = Math.Min(0.95f, elapsed / 2000f);
        _progressDirty = true;
        OnChanged?.Invoke();
    }

    public float GetContentOffset() => TabBarHeight + ToolbarHeight;
    public float GetStatusBarHeight() => StatusBarHeight;
    public float GetTabBarHeight() => TabBarHeight;
    public float GetToolbarHeight() => ToolbarHeight;

    public void RenderScrollbars(SKCanvas canvas, float width, float height, ScrollManager scrollManager)
    {
        float contentTop = GetContentOffset();
        float vH = scrollManager.ViewportHeight;
        float vW = scrollManager.ViewportWidth;

        // Opaque white backdrop under the translucent scrollbar paints. The page
        // surface is reused across retained frames (the page-area clear is
        // skipped while the deferred tile rasterizer has backlog), so a purely
        // translucent track/thumb would accumulate on every frame until the strip
        // turned black. The backdrop keeps the strip clean every frame no matter
        // which compositing path is active.
        using var backdropPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };

        if (scrollManager.CanScrollY && vH > 0)
        {
            float sbL = width - ScrollManager.ScrollbarWidth;
            float tTop = contentTop;
            float tH = vH;
            float cH = scrollManager.ContentHeight;
            if (cH <= vH) return;

            canvas.DrawRect(sbL, tTop, ScrollManager.ScrollbarWidth, tH, backdropPaint);

            using var trackPaint = new SKPaint { Color = new SKColor(0, 0, 0, 8), Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(sbL + 2, tTop, ScrollManager.ScrollbarWidth - 4, tH, 3, 3, trackPaint);

            float thumbH = Math.Max(ScrollManager.ScrollbarMinThumbSize, tH * vH / cH);
            float maxS = cH - vH;
            float thumbT = tTop;
            if (maxS > 0)
                thumbT += (scrollManager.ScrollY / maxS) * (tH - thumbH);

            using var thumbPaint = new SKPaint { Color = new SKColor(0, 0, 0, 80), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(sbL + 3, thumbT, ScrollManager.ScrollbarWidth - 6, thumbH, 3, 3, thumbPaint);
        }

        if (scrollManager.CanScrollX && vW > 0)
        {
            float cB = height - StatusBarHeight;
            float sbT = cB - ScrollManager.ScrollbarWidth;
            float tL = 0;
            float tW = scrollManager.CanScrollY ? vW - ScrollManager.ScrollbarWidth : vW;
            float cW = scrollManager.ContentWidth;
            if (cW <= vW) return;

            canvas.DrawRect(tL, sbT, tW, ScrollManager.ScrollbarWidth, backdropPaint);

            using var trackPaint = new SKPaint { Color = new SKColor(0, 0, 0, 8), Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(tL, sbT + 2, tW, ScrollManager.ScrollbarWidth - 4, 3, 3, trackPaint);

            float thumbW = Math.Max(ScrollManager.ScrollbarMinThumbSize, tW * vW / cW);
            float maxS = cW - vW;
            float thumbL = tL;
            if (maxS > 0)
                thumbL += (scrollManager.ScrollX / maxS) * (tW - thumbW);

            using var thumbPaint = new SKPaint { Color = new SKColor(0, 0, 0, 80), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(thumbL, sbT + 3, thumbW, ScrollManager.ScrollbarWidth - 6, 3, 3, thumbPaint);
        }
    }

    // Thread-safe event invocations
    private void InvokeOnChanged()
    {
        Interlocked.Exchange(ref _lastChangedTick, Environment.TickCount64);
        OnChanged?.Invoke();
    }

    private long _lastChangedTick;

    public object SyncRoot => _tabLock;

    public T ReadTabData<T>(Func<T> reader)
    {
        _tabRwLock.EnterReadLock();
        try { return reader(); }
        finally { _tabRwLock.ExitReadLock(); }
    }

    public void WriteTabData(Action writer)
    {
        _tabRwLock.EnterWriteLock();
        try { writer(); }
        finally { _tabRwLock.ExitWriteLock(); }
    }

    /// <summary>
    /// Atomically snapshot all tab info for rendering or other read-only operations.
    /// </summary>
    public TabInfo[] SnapshotTabs()
    {
        _tabRwLock.EnterReadLock();
        try
        {
            var arr = new TabInfo[_tabs.Count];
            for (int i = 0; i < _tabs.Count; i++)
            {
                arr[i] = new TabInfo
                {
                    Title = _tabs[i].Title,
                    Url = _tabs[i].Url,
                    IsActive = _tabs[i].IsActive,
                    IsLoading = _tabs[i].IsLoading,
                    LoadingProgress = _tabs[i].LoadingProgress,
                    TooltipText = _tabs[i].TooltipText,
                };
            }
            return arr;
        }
        finally { _tabRwLock.ExitReadLock(); }
    }

    public void Dispose()
    {
        _tabRwLock.Dispose();
        _font11.Dispose(); _font12.Dispose(); _font13.Dispose(); _font14.Dispose();
        _font22.Dispose(); _fontClose.Dispose();
        _tabBgPaint.Dispose(); _tabActivePaint.Dispose(); _tabHoverPaint.Dispose();
        _toolbarBgPaint.Dispose(); _statusBgPaint.Dispose();
        _urlBgPaint.Dispose(); _urlBorderPaint.Dispose(); _urlFocusBorderPaint.Dispose();
        _textPrimary.Dispose(); _textSecondary.Dispose(); _textBlue.Dispose();
        _iconPaint.Dispose(); _iconDisabledPaint.Dispose();
        _btnHoverPaint.Dispose(); _btnActivePaint.Dispose();
        _closeBtnBgPaint.Dispose(); _closeBtnX.Dispose();
        _cursorPaint.Dispose(); _lockPaint.Dispose(); _infoPaint.Dispose();
        _newTabBgPaint.Dispose(); _newTabHoverBgPaint.Dispose(); _newTabPlusPaint.Dispose();
        _statusTextPaint.Dispose(); _separatorPaint.Dispose();
        _progressPaint.Dispose(); _progressBgPaint.Dispose(); _shadowPaint.Dispose(); _selectionPaint.Dispose();
    }

    #region IImeSupport

    public Point GetImeCaretPosition()
    {
        if (!_urlBarFocused)
            return new Point(0, 0);

        float textX = 172 + (string.IsNullOrEmpty(_currentUrl) || _currentUrl.StartsWith("acrux://") ? 10 : 30);
        float textY = TabBarHeight + ToolbarHeight / 2 + 2;
        string beforeCursor = _urlBarText[..Math.Min(_cursorPosition, _urlBarText.Length)];
        float cursorX = textX + _font14.MeasureText(beforeCursor);

        return new Point(cursorX, textY);
    }

    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = "";
        _imeCompositionCursor = 0;
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursor = cursorPosition;
        _isImeComposing = true;
    }

    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = "";
        _imeCompositionCursor = 0;

        if (resultString != null)
        {
            _urlBarText = _urlBarText[.._cursorPosition] + resultString + _urlBarText[_cursorPosition..];
            _cursorPosition += resultString.Length;
        }
    }

    public bool IsImeComposing => _isImeComposing;
    public string ImeCompositionString => _imeCompositionString;

    #endregion
}

public enum SKKey
{
    None,
    Enter,
    Escape,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Backspace,
    Delete,
    Tab,
    Space
}
