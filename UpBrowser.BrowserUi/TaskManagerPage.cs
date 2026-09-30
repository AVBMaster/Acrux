using System;
using System.Collections.Generic;
using System.Diagnostics;
using SkiaSharp;

namespace UpBrowser.Rendering;

public struct TmRowData
{
    public string Name;
    public string Detail;
    public string Memory;
    public string Cpu;
    public int DomNodes;
    public int LayoutBoxes;
    public string Status;
    public int Pid;
    public int TabIndex;

    // Performance pipeline timings (milliseconds)
    public double StyleTimingMs;
    public double LayoutTimingMs;
    public double PaintTimingMs;
    public double ScriptTimingMs;
    public double CompositeTimingMs;
    public double ImageDecodeTimingMs;
    public double TileRasterTimingMs;
    public double NetworkWaitTimingMs;

    // Memory breakdown
    public double WorkingSetMB;
    public double ManagedHeapMB;
    public double ImageCacheMB;
    public double TileMemoryMB;

    // Rendering stats
    public int TilesRasterized;
    public int TilesReused;
    public int ImagesDecoded;
    public int ImageCacheHits;
    public int ResourceCacheHits;

    // JavaScript stats
    public int JsHeapSizeKB;
    public int JsCallbackCount;

    // Frame timing
    public double FrameTimeMs;
    public double Fps;
}

public class TaskManagerPage
{
    private bool _visible;
    private float _dialogWidth = 620;
    private float _dialogHeight = 460;
    private float _scrollOffset;
    private float _contentHeight;
    private readonly SKTypeface _typeface;
    private int _hoveredRow = -1;
    private int _selectedRow = -1;
    private bool _closeHovered;
    private bool _endProcessHovered;

    private long _lastRefreshTick;
    private const long RefreshIntervalMs = 1000;

    private DateTime _lastCpuTime;
    private TimeSpan _lastProcessorTime;
    private double _currentCpuPercent;
    private float _headerHeight = 40;
    private float _colHeaderHeight = 30;
    private float _rowHeight = 38;
    private float _footerHeight = 44;
    private float _detailPanelHeight = 80;

    private List<TmRowData> _rows = new();

    public bool Visible => _visible;

    public event Action? OnChanged;
    public event Action<int>? OnEndProcess;

    public TaskManagerPage()
    {
        _typeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _lastCpuTime = DateTime.UtcNow;
        var p = Process.GetCurrentProcess();
        _lastProcessorTime = p.TotalProcessorTime;
    }

    public void Toggle()
    {
        _visible = !_visible;
        _scrollOffset = 0;
        _selectedRow = -1;
        if (_visible)
            RefreshProcessData();
    }

    public void Hide()
    {
        _visible = false;
        _scrollOffset = 0;
        _selectedRow = -1;
    }

    private void RefreshProcessData()
    {
        var now = DateTime.UtcNow;
        var process = Process.GetCurrentProcess();
        process.Refresh();

        var currentProcessorTime = process.TotalProcessorTime;
        var cpuDelta = (currentProcessorTime - _lastProcessorTime).TotalSeconds;
        var timeDelta = (now - _lastCpuTime).TotalSeconds;
        if (timeDelta > 0.01 && cpuDelta >= 0)
        {
            _currentCpuPercent = Math.Round((cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount, 1);
        }
        _lastCpuTime = now;
        _lastProcessorTime = currentProcessorTime;
    }

    public void Render(SKCanvas canvas, float windowWidth, float windowHeight, float contentOffset, List<TmRowData> rows)
    {
        if (!_visible) return;

        if (Environment.TickCount64 - _lastRefreshTick > RefreshIntervalMs)
        {
            _lastRefreshTick = Environment.TickCount64;
            RefreshProcessData();
        }

        _rows = rows;

        // Fill CPU for rows with placeholder (Browser + active tab)
        string cpuStr = _currentCpuPercent >= 0 ? $"{_currentCpuPercent:F1}%" : "";
        for (int idx = 0; idx < _rows.Count; idx++)
        {
            var r = _rows[idx];
            if (string.IsNullOrEmpty(r.Cpu))
            {
                r.Cpu = cpuStr;
                _rows[idx] = r;
            }
        }

        if (_selectedRow >= _rows.Count)
            _selectedRow = -1;

        // Compute aggregated performance stats for footer
        double totalStyleMs = 0, totalLayoutMs = 0, totalPaintMs = 0, totalScriptMs = 0;
        double totalCompositeMs = 0, totalImageDecodeMs = 0, totalTileRasterMs = 0, totalNetworkMs = 0;
        int totalTilesRast = 0, totalTilesReused = 0, totalImagesDecoded = 0, totalImageHits = 0, totalCacheHits = 0;
        int totalDomNodes = 0, totalLayoutBoxes = 0;
        double totalWsMb = 0;

        foreach (var r in rows)
        {
            totalStyleMs += r.StyleTimingMs;
            totalLayoutMs += r.LayoutTimingMs;
            totalPaintMs += r.PaintTimingMs;
            totalScriptMs += r.ScriptTimingMs;
            totalCompositeMs += r.CompositeTimingMs;
            totalImageDecodeMs += r.ImageDecodeTimingMs;
            totalTileRasterMs += r.TileRasterTimingMs;
            totalNetworkMs += r.NetworkWaitTimingMs;
            totalTilesRast += r.TilesRasterized;
            totalTilesReused += r.TilesReused;
            totalImagesDecoded += r.ImagesDecoded;
            totalImageHits += r.ImageCacheHits;
            totalCacheHits += r.ResourceCacheHits;
            totalDomNodes += r.DomNodes;
            totalLayoutBoxes += r.LayoutBoxes;
            totalWsMb += r.WorkingSetMB;
        }

        float cx = windowWidth / 2;
        float cy = windowHeight / 2;
        float dlgX = cx - _dialogWidth / 2;
        float dlgY = Math.Max(40, cy - _dialogHeight / 2);
        var dlgRect = new SKRect(dlgX, dlgY, dlgX + _dialogWidth, dlgY + _dialogHeight);

        // Backdrop
        using (var backdrop = new SKPaint { Color = new SKColor(0, 0, 0, 96), Style = SKPaintStyle.Fill })
            canvas.DrawRect(0, 0, windowWidth, windowHeight, backdrop);

        // Shadow
        using (var shadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 32),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12)
        })
            canvas.DrawRoundRect(dlgX + 3, dlgY + 5, _dialogWidth, _dialogHeight, 10, 10, shadow);

        // Dialog background
        using (var bg = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawRoundRect(dlgRect, 10, 10, bg);

        // Border
        using (var border = new SKPaint
        {
            Color = new SKColor(189, 193, 198),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        })
            canvas.DrawRoundRect(dlgRect, 10, 10, border);

        // Header
        using (var headerBg = new SKPaint { Color = new SKColor(32, 33, 36), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            var pb = new SKPathBuilder();
            pb.AddRoundRect(new SKRect(dlgX, dlgY, dlgX + _dialogWidth, dlgY + _headerHeight + 4), 10, 10);
            using var headerPath = pb.Detach();
            canvas.DrawPath(headerPath, headerBg);
            canvas.DrawRect(dlgX, dlgY + 6, _dialogWidth, _headerHeight - 6, headerBg);
        }

        using (var headerFont = FontHelper.CrispHintedFont(_typeface, 14))
        using (var headerPaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
            canvas.DrawText("Task Manager", dlgX + 16, dlgY + 26, SKTextAlign.Left, headerFont, headerPaint);

        // Close button
        _closeHovered = DrawCloseButton(canvas, dlgX + _dialogWidth - 38, dlgY + 8, 24);

        // Column headers
        float colY = dlgY + _headerHeight;
        using (var colBg = new SKPaint { Color = new SKColor(248, 249, 250), Style = SKPaintStyle.Fill })
            canvas.DrawRect(dlgX, colY, _dialogWidth, _colHeaderHeight, colBg);

        string[] columnNames = { "Process", "Memory", "CPU", "DOM/Layout", "Pipeline(ms)", "Status" };
        float[] colStarts = { dlgX + 12, dlgX + 260, dlgX + 340, dlgX + 400, dlgX + 480, dlgX + 560 };

        using (var colFont = FontHelper.CrispHintedFont(_typeface, 12))
        using (var colPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true })
            for (int i = 0; i < columnNames.Length; i++)
                canvas.DrawText(columnNames[i], colStarts[i], colY + 20, SKTextAlign.Left, colFont, colPaint);

        // Separator under column headers
        using (var sepLine = new SKPaint { Color = new SKColor(218, 220, 224), Style = SKPaintStyle.Fill })
            canvas.DrawRect(dlgX, colY + _colHeaderHeight, _dialogWidth, 1, sepLine);

        // Content area
        float contentTop = colY + _colHeaderHeight + 1;
        float contentBottom = dlgY + _dialogHeight - _footerHeight;
        float contentAreaH = contentBottom - contentTop;

        canvas.Save();
        canvas.ClipRect(new SKRect(dlgX + 1, contentTop, dlgX + _dialogWidth - 1, contentBottom));

        using var rowFont = FontHelper.CrispHintedFont(_typeface, 13);
        using var smallFont = FontHelper.CrispHintedFont(_typeface, 11);
        using var detailFont = FontHelper.CrispHintedFont(_typeface, 11);
        using var namePaint = new SKPaint { Color = new SKColor(32, 33, 36), IsAntialias = true };
        using var detailPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true };
        using var microPaint = new SKPaint { Color = new SKColor(120, 124, 128), IsAntialias = true };
        using var altPaint = new SKPaint { Color = new SKColor(248, 249, 250), Style = SKPaintStyle.Fill };
        using var selPaint = new SKPaint { Color = new SKColor(210, 227, 252), Style = SKPaintStyle.Fill };
        using var hovPaint = new SKPaint { Color = new SKColor(232, 240, 254), Style = SKPaintStyle.Fill };
        using var perfBgPaint = new SKPaint { Color = new SKColor(240, 244, 255), Style = SKPaintStyle.Fill };
        using var perfSepPaint = new SKPaint { Color = new SKColor(200, 210, 230), Style = SKPaintStyle.Fill };

        float yPos = contentTop - _scrollOffset;
        float expandedOffset = 0;

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];

            // Shift this row down if previous row is expanded
            bool prevExpanded = (i > 0 && (i - 1) == _selectedRow && _rows[i - 1].TabIndex >= 0 && _selectedRow >= 0);
            if (prevExpanded)
                expandedOffset += _detailPanelHeight;

            float rowBase = yPos + i * _rowHeight + expandedOffset;
            float rowTop = rowBase;
            if (rowTop + _rowHeight < contentTop) continue;
            if (rowTop > contentBottom) break;

            bool isSelected = i == _selectedRow;
            bool isHovered = i == _hoveredRow;

            if (isSelected)
                canvas.DrawRect(dlgX + 1, rowTop, _dialogWidth - 2, _rowHeight, selPaint);
            else if (isHovered)
                canvas.DrawRect(dlgX + 1, rowTop, _dialogWidth - 2, _rowHeight, hovPaint);
            else if (i % 2 == 1)
                canvas.DrawRect(dlgX + 1, rowTop, _dialogWidth - 2, _rowHeight, altPaint);

            if (row.TabIndex >= 0)
            {
                using (var dot = new SKPaint { Color = new SKColor(26, 115, 232), Style = SKPaintStyle.Fill, IsAntialias = true })
                    canvas.DrawCircle(colStarts[0] + 6, rowTop + 14, 4, dot);

                string displayName = TruncateText(rowFont, row.Name, 200);
                canvas.DrawText(displayName, colStarts[0] + 16, rowTop + 16, SKTextAlign.Left, rowFont, namePaint);

                if (!string.IsNullOrEmpty(row.Detail))
                {
                    string detail = TruncateText(smallFont, row.Detail, 200);
                    canvas.DrawText(detail, colStarts[0] + 16, rowTop + 30, SKTextAlign.Left, smallFont, detailPaint);
                }
            }
            else
            {
                string displayName = TruncateText(rowFont, row.Name, 200);
                canvas.DrawText(displayName, colStarts[0] + 6, rowTop + 24, SKTextAlign.Left, rowFont, namePaint);

                if (!string.IsNullOrEmpty(row.Detail))
                {
                    string detail = TruncateText(smallFont, row.Detail, 200);
                    canvas.DrawText(detail, colStarts[0] + 6, rowTop + 36, SKTextAlign.Left, smallFont, detailPaint);
                }
            }

            float cellCenterY = rowTop + 24;
            canvas.DrawText(string.IsNullOrEmpty(row.Memory) ? "-" : row.Memory, colStarts[1], cellCenterY, SKTextAlign.Left, rowFont, detailPaint);
            canvas.DrawText(string.IsNullOrEmpty(row.Cpu) ? "-" : row.Cpu, colStarts[2], cellCenterY, SKTextAlign.Left, rowFont, detailPaint);

            // Combined DOM/Layout
            string nodeStr = (row.DomNodes > 0 || row.LayoutBoxes > 0) ? $"{row.DomNodes}/{row.LayoutBoxes}" : "-";
            canvas.DrawText(nodeStr, colStarts[3], cellCenterY, SKTextAlign.Left, rowFont, detailPaint);

            // Pipeline timing summary (style + layout + paint)
            double totalPipeMs = row.StyleTimingMs + row.LayoutTimingMs + row.PaintTimingMs;
            string pipeStr = totalPipeMs > 0 ? $"{totalPipeMs:F1}" : "-";
            canvas.DrawText(pipeStr, colStarts[4], cellCenterY, SKTextAlign.Left, rowFont, detailPaint);

            using var statusPaint = new SKPaint
            {
                Color = row.Status == "Loading" ? new SKColor(26, 115, 232) : new SKColor(95, 99, 104),
                IsAntialias = true
            };
            canvas.DrawText(string.IsNullOrEmpty(row.Status) ? "-" : row.Status, colStarts[5], cellCenterY, SKTextAlign.Left, rowFont, statusPaint);

            // --- Detail performance panel for selected row ---
            if (isSelected && row.TabIndex >= 0)
            {
                float detailY = rowTop + _rowHeight;
                float detailH = _detailPanelHeight;
                float detailW = _dialogWidth - 4;
                float detailX = dlgX + 2;

                // Ensure detail panel fits
                if (detailY + detailH < contentBottom)
                {
                    canvas.DrawRect(detailX, detailY, detailW, detailH, perfBgPaint);
                    canvas.DrawRect(detailX, detailY, detailW, 1, perfSepPaint);

                    // First line: Pipeline timings
                    float line1Y = detailY + 16;
                    canvas.DrawText($"Style:{row.StyleTimingMs:F1}ms  Layout:{row.LayoutTimingMs:F1}ms  Paint:{row.PaintTimingMs:F1}ms  Script:{row.ScriptTimingMs:F1}ms  Composite:{row.CompositeTimingMs:F1}ms",
                        detailX + 8, line1Y, SKTextAlign.Left, detailFont, microPaint);

                    // Second line: Memory breakdown
                    float line2Y = detailY + 32;
                    canvas.DrawText($"WS:{row.WorkingSetMB:F0}MB  Heap:{row.ManagedHeapMB:F0}MB  Images:{row.ImageCacheMB:F0}MB  Tiles:{row.TileMemoryMB:F0}MB  ImageDecode:{row.ImageDecodeTimingMs:F1}ms  Network:{row.NetworkWaitTimingMs:F1}ms",
                        detailX + 8, line2Y, SKTextAlign.Left, detailFont, microPaint);

                    // Third line: Rendering stats
                    float line3Y = detailY + 48;
                    canvas.DrawText($"Tiles Rast:{row.TilesRasterized}  Reused:{row.TilesReused}  ImgDec:{row.ImagesDecoded}  ImgHit:{row.ImageCacheHits}  CacheHit:{row.ResourceCacheHits}  FPS:{row.Fps:F0}",
                        detailX + 8, line3Y, SKTextAlign.Left, detailFont, microPaint);

                    // Fourth line: JS stats
                    float line4Y = detailY + 64;
                    canvas.DrawText($"JS Heap:{row.JsHeapSizeKB}KB  JS Callbacks:{row.JsCallbackCount}  Frame:{row.FrameTimeMs:F1}ms  TileRast:{row.TileRasterTimingMs:F1}ms",
                        detailX + 8, line4Y, SKTextAlign.Left, detailFont, microPaint);
                }
            }
        }

        // Account for expanded selected row height in content height calculation
        float expandedExtra = 0;
        if (_selectedRow >= 0 && _selectedRow < _rows.Count && _rows[_selectedRow].TabIndex >= 0)
            expandedExtra = _detailPanelHeight;
        _contentHeight = _rows.Count * _rowHeight + expandedExtra;
        canvas.Restore();

        // Scroll indicator
        if (_contentHeight > contentAreaH)
        {
            float trackH = contentAreaH;
            float thumbH = Math.Max(24, trackH * contentAreaH / _contentHeight);
            float maxScroll = _contentHeight - contentAreaH;
            float thumbY = contentTop + (_scrollOffset / Math.Max(1, maxScroll)) * (trackH - thumbH);
            using (var scrollBg = new SKPaint { Color = new SKColor(0, 0, 0, 16), Style = SKPaintStyle.Fill })
                canvas.DrawRoundRect(dlgX + _dialogWidth - 8, contentTop, 4, trackH, 2, 2, scrollBg);
            using (var scrollThumb = new SKPaint { Color = new SKColor(0, 0, 0, 48), Style = SKPaintStyle.Fill, IsAntialias = true })
                canvas.DrawRoundRect(dlgX + _dialogWidth - 8, thumbY, 4, thumbH, 2, 2, scrollThumb);
        }

        // Footer
        float footerY = dlgY + _dialogHeight - _footerHeight;
        using (var footerBg = new SKPaint { Color = new SKColor(248, 249, 250), Style = SKPaintStyle.Fill })
            canvas.DrawRect(dlgX, footerY, _dialogWidth, _footerHeight, footerBg);
        using (var footerSep = new SKPaint { Color = new SKColor(218, 220, 224), Style = SKPaintStyle.Fill })
            canvas.DrawRect(dlgX, footerY, _dialogWidth, 1, footerSep);

        // End Process button
        float btnW = 100;
        float btnH = 28;
        float btnX = dlgX + _dialogWidth - btnW - 12;
        float btnY = footerY + (_footerHeight - btnH) / 2;
        bool canEnd = _selectedRow >= 0 && _selectedRow < _rows.Count && _rows[_selectedRow].TabIndex >= 0;
        using (var btnPaint = new SKPaint
        {
            Color = canEnd ? (_endProcessHovered ? new SKColor(183, 28, 28) : new SKColor(211, 47, 47))
                          : new SKColor(200, 200, 200),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
            canvas.DrawRoundRect(btnX, btnY, btnW, btnH, 4, 4, btnPaint);
        using (var btnFont = FontHelper.CrispHintedFont(_typeface, 12))
        using (var btnText = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            string label = "End Process";
            float tw = btnFont.MeasureText(label);
            canvas.DrawText(label, btnX + (btnW - tw) / 2, btnY + 19, SKTextAlign.Left, btnFont, btnText);
        }

        // Footer info with aggregated performance data
        var p = Process.GetCurrentProcess();
        p.Refresh();
        double m = p.WorkingSet64 / (1024.0 * 1024.0);
        using (var infoFont = FontHelper.CrispHintedFont(_typeface, 10))
        using (var infoPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true })
        {
            string info = $"Mem:{m:F0}MB CPU:{_currentCpuPercent:F1}% Tabs:{Math.Max(0, _rows.Count - 1)} " +
                $"Pipeline:{totalStyleMs + totalLayoutMs + totalPaintMs + totalScriptMs:F0}ms " +
                $"Nodes:{totalDomNodes} " +
                $"Tiles:{totalTilesRast + totalTilesReused} " +
                $"Images:{totalImagesDecoded}";
            canvas.DrawText(info, dlgX + 16, footerY + 26, SKTextAlign.Left, infoFont, infoPaint);
        }
    }

    private static string TruncateText(SKFont font, string text, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth)
            return text;
        while (text.Length > 1 && font.MeasureText(text + "…") > maxWidth)
            text = text[..^1];
        return text + "…";
    }

    private bool DrawCloseButton(SKCanvas canvas, float x, float y, float size)
    {
        var btnRect = new SKRect(x, y, x + size, y + size);
        bool hovering = btnRect.Contains(_lastMouseX, _lastMouseY);
        if (hovering)
        {
            using var bg = new SKPaint { Color = new SKColor(255, 255, 255, 40), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(btnRect, 4, 4, bg);
        }
        using var xPaint = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        float pad = 6;
        var pb = new SKPathBuilder();
        pb.MoveTo(x + pad, y + pad);
        pb.LineTo(x + size - pad, y + size - pad);
        pb.MoveTo(x + size - pad, y + pad);
        pb.LineTo(x + pad, y + size - pad);
        using var path = pb.Detach();
        canvas.DrawPath(path, xPaint);
        return hovering;
    }

    private float _lastMouseX, _lastMouseY;

    public bool HandleClick(float x, float y, float windowWidth, float windowHeight)
    {
        if (!_visible) return false;
        _lastMouseX = x;
        _lastMouseY = y;

        float cx = windowWidth / 2;
        float cy = windowHeight / 2;
        float dlgX = cx - _dialogWidth / 2;
        float dlgY = Math.Max(40, cy - _dialogHeight / 2);
        var dlgRect = new SKRect(dlgX, dlgY, dlgX + _dialogWidth, dlgY + _dialogHeight);

        if (!dlgRect.Contains(x, y))
        {
            Hide();
            OnChanged?.Invoke();
            return true;
        }

        // Close button
        if (new SKRect(dlgX + _dialogWidth - 38, dlgY + 8, dlgX + _dialogWidth - 38 + 24, dlgY + 8 + 24).Contains(x, y))
        {
            Hide();
            OnChanged?.Invoke();
            return true;
        }

        // End Process
        float footerY = dlgY + _dialogHeight - _footerHeight;
        float btnW = 100;
        float btnH = 28;
        float btnX = dlgX + _dialogWidth - btnW - 12;
        float btnY = footerY + (_footerHeight - btnH) / 2;
        if (new SKRect(btnX, btnY, btnX + btnW, btnY + btnH).Contains(x, y))
        {
            if (_selectedRow >= 0 && _selectedRow < _rows.Count)
            {
                int tabIdx = _rows[_selectedRow].TabIndex;
                if (tabIdx >= 0)
                {
                    OnEndProcess?.Invoke(tabIdx);
                    OnChanged?.Invoke();
                }
            }
            return true;
        }

        // Row selection (with expanded panel offset)
        float colY = dlgY + _headerHeight;
        float contentTop = colY + _colHeaderHeight + 1;
        float contentBottom = dlgY + _dialogHeight - _footerHeight;
        if (y >= contentTop && y < contentBottom)
        {
            int newSel = GetRowIndexAtY(y, contentTop, _scrollOffset);
            if (newSel >= 0 && newSel < _rows.Count)
            {
                // Toggle selection off if clicking the same row
                if (newSel == _selectedRow)
                    _selectedRow = -1;
                else
                    _selectedRow = newSel;
                OnChanged?.Invoke();
            }
        }
        return true;
    }

    public bool HandleMouseMove(float x, float y, float windowWidth, float windowHeight)
    {
        if (!_visible) return false;
        _lastMouseX = x;
        _lastMouseY = y;

        float cx = windowWidth / 2;
        float cy = windowHeight / 2;
        float dlgX = cx - _dialogWidth / 2;
        float dlgY = Math.Max(40, cy - _dialogHeight / 2);
        var dlgRect = new SKRect(dlgX, dlgY, dlgX + _dialogWidth, dlgY + _dialogHeight);

        bool oldClose = _closeHovered;
        bool oldEnd = _endProcessHovered;
        int oldRow = _hoveredRow;

        if (!dlgRect.Contains(x, y))
        {
            _closeHovered = false;
            _endProcessHovered = false;
            _hoveredRow = -1;
        }
        else
        {
            _closeHovered = new SKRect(dlgX + _dialogWidth - 38, dlgY + 8, dlgX + _dialogWidth - 38 + 24, dlgY + 8 + 24).Contains(x, y);

            float footerY = dlgY + _dialogHeight - _footerHeight;
            float btnW = 100;
            float btnH = 28;
            float btnX = dlgX + _dialogWidth - btnW - 12;
            float btnY = footerY + (_footerHeight - btnH) / 2;
            _endProcessHovered = new SKRect(btnX, btnY, btnX + btnW, btnY + btnH).Contains(x, y);

            float colY = dlgY + _headerHeight;
            float contentTop = colY + _colHeaderHeight + 1;
            float contentBottom = dlgY + _dialogHeight - _footerHeight;
            if (y >= contentTop && y < contentBottom)
            {
                int n = GetRowIndexAtY(y, contentTop, _scrollOffset);
                _hoveredRow = (n >= 0 && n < _rows.Count) ? n : -1;
            }
            else
            {
                _hoveredRow = -1;
            }
        }

        if (oldRow != _hoveredRow || oldClose != _closeHovered || oldEnd != _endProcessHovered)
            OnChanged?.Invoke();

        return true;
    }

    public bool HandleWheel(float delta, float windowHeight)
    {
        if (!_visible) return false;
        float contentAreaH = _dialogHeight - _headerHeight - _colHeaderHeight - _footerHeight;
        float maxScroll = Math.Max(0, _contentHeight - contentAreaH);
        _scrollOffset = Math.Clamp(_scrollOffset - delta * 0.5f, 0, maxScroll);
        OnChanged?.Invoke();
        return true;
    }

    public void HandleMouseUp() { }

    /// <summary>Compute the actual Y position of a row accounting for the expanded detail panel offset.</summary>
    private float GetRowBaseY(int rowIndex, float contentTop, float scrollOffset)
    {
        float yPos = contentTop - scrollOffset;
        float offset = 0;
        for (int i = 1; i <= rowIndex; i++)
        {
            if ((i - 1) == _selectedRow && _rows[i - 1].TabIndex >= 0 && _selectedRow >= 0)
                offset += _detailPanelHeight;
        }
        return yPos + rowIndex * _rowHeight + offset;
    }

    /// <summary>Find the row index at a given Y position, accounting for expanded panel offsets.</summary>
    private int GetRowIndexAtY(float y, float contentTop, float scrollOffset)
    {
        float yInContent = y - contentTop + scrollOffset;
        if (yInContent < 0) return -1;

        float offset = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i > 0 && (i - 1) == _selectedRow && _rows[i - 1].TabIndex >= 0 && _selectedRow >= 0)
                offset += _detailPanelHeight;

            float rowStart = i * _rowHeight + offset;
            float rowEnd = rowStart + _rowHeight;
            if (yInContent >= rowStart && yInContent < rowEnd)
                return i;

            // Also check the detail panel area of the selected row
            if (i == _selectedRow && _selectedRow >= 0 && _rows[_selectedRow].TabIndex >= 0)
            {
                float detailEnd = rowStart + _rowHeight + _detailPanelHeight;
                if (yInContent >= rowEnd && yInContent < detailEnd)
                    return i; // still the same row (detail panel)
            }
        }
        return -1;
    }
}
