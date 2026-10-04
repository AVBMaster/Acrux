using SkiaSharp;
using Acrux.Core.Performance;
using Acrux.Core.Performance.Memory;

namespace Acrux.Core.Performance.Compositor;

/// <summary>
/// Pure render list record. Decouples the compositing pass from the layout/scene
/// graph: the layout stage produces <see cref="CompositorCommand"/>s into a list, and
/// the compositing pass replays them. This is the smallest piece of "display list"
/// infrastructure Acrux needs to enable incremental painting.
/// </summary>
public enum CompositorOp : byte
{
    Save = 0,
    Restore = 1,
    Translate = 2,
    ClipRect = 3,
    DrawRect = 4,
    DrawText = 5,
    DrawImage = 6,
    DrawRoundedRect = 7,
    DrawBoxShadow = 8,
    DrawBorder = 9,
    DrawBackground = 10,
    SetOpacity = 11,
    ConcatMatrix = 12,
}

public readonly struct CompositorCommand
{
    public readonly CompositorOp Op;
    public readonly SKRect Rect;
    public readonly SKColor Color;
    public readonly float A, B, C, D;
    public readonly string? Text;
    public readonly SKImage? Image;
    public readonly float Radius;
    public readonly float Blur;
    public readonly SKPaint? Paint;
    public readonly float TopLeftRadius;
    public readonly float TopRightRadius;
    public readonly float BottomLeftRadius;
    public readonly float BottomRightRadius;

    private CompositorCommand(CompositorOp op, SKRect rect, SKColor color, float a, float b, float c, float d, string? text, SKImage? image, float radius, float blur, SKPaint? paint,
        float tlr, float trr, float blr, float brr)
    {
        Op = op; Rect = rect; Color = color; A = a; B = b; C = c; D = d;
        Text = text; Image = image; Radius = radius; Blur = blur; Paint = paint;
        TopLeftRadius = tlr; TopRightRadius = trr; BottomLeftRadius = blr; BottomRightRadius = brr;
    }

    public static CompositorCommand Save() => new(CompositorOp.Save, default, default, 0, 0, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand Restore() => new(CompositorOp.Restore, default, default, 0, 0, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand Translate(float dx, float dy) => new(CompositorOp.Translate, default, default, dx, dy, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand ClipRect(SKRect r) => new(CompositorOp.ClipRect, r, default, 0, 0, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand DrawRect(SKRect r, SKColor c) => new(CompositorOp.DrawRect, r, c, 0, 0, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand DrawRoundedRect(SKRect r, SKColor c, float radius) => new(CompositorOp.DrawRoundedRect, r, c, 0, 0, 0, 0, null, null, radius, 0, null, 0, 0, 0, 0);
    public static CompositorCommand DrawRoundedRectVariable(SKRect r, SKColor c, float tlr, float trr, float blr, float brr) =>
        new(CompositorOp.DrawRoundedRect, r, c, 0, 0, 0, 0, null, null, 0, 0, null, tlr, trr, blr, brr);
    public static CompositorCommand DrawBoxShadow(SKRect r, float blur, SKColor c) => new(CompositorOp.DrawBoxShadow, r, c, 0, 0, 0, 0, null, null, 0, blur, null, 0, 0, 0, 0);
    public static CompositorCommand DrawBorder(SKRect r, float width, SKColor c) => new(CompositorOp.DrawBorder, r, c, 0, 0, 0, width, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand DrawBackground(SKRect r, SKColor c) => new(CompositorOp.DrawBackground, r, c, 0, 0, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand DrawText(string text, float x, float y, SKColor c, float fontSize) => new(CompositorOp.DrawText, new SKRect(x, y, x, y), c, x, y, 0, fontSize, text, null, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand DrawImage(SKRect r, SKImage image) => new(CompositorOp.DrawImage, r, default, 0, 0, 0, 0, null, image, 0, 0, null, 0, 0, 0, 0);
    public static CompositorCommand SetOpacity(float o) => new(CompositorOp.SetOpacity, default, default, 0, 0, 0, 0, null, null, 0, 0, null, 0, 0, 0, 0) { /* opacity encoded in C? */ };
}

/// <summary>
/// A pool-allocated display list. Commands are stored in a contiguous array for cache
/// locality during replay. Repaint regions (dirty rects) are tracked per list so the
/// compositing pass can skip unaffected tiles.
/// </summary>
public sealed class CompositorDisplayList
{
    private readonly List<CompositorCommand> _commands = new();
    private SKRect _dirtyRect = SKRect.Empty;
    private long _lastUsedNanos;
    private long _version;
    private int _refCount;
    private long _produced;

    public IReadOnlyList<CompositorCommand> Commands => _commands;
    public SKRect DirtyRect => _dirtyRect;
    public long LastUsedNanos => _lastUsedNanos;
    public long Version => _version;
    public int RefCount => _refCount;
    public int CommandCount => _commands.Count;
    public long Produced => Interlocked.Read(ref _produced);

    public int Add(CompositorCommand cmd)
    {
        _commands.Add(cmd);
        Interlocked.Increment(ref _produced);
        return _commands.Count - 1;
    }

    public void AddRange(IEnumerable<CompositorCommand> cmds)
    {
        foreach (var c in cmds) _commands.Add(c);
    }

    public void SetDirtyRect(SKRect rect)
    {
        _dirtyRect = _dirtyRect.IsEmpty ? rect : SKRect.Union(_dirtyRect, rect);
    }

    public void Touch() => _lastUsedNanos = Clock.NowNanos();

    public void Reset()
    {
        _commands.Clear();
        _dirtyRect = SKRect.Empty;
        Interlocked.Increment(ref _version);
    }

    public void Retain() => Interlocked.Increment(ref _refCount);
    public void Release() => Interlocked.Decrement(ref _refCount);
}

/// <summary>
/// Replays a <see cref="CompositorDisplayList"/> onto a Skia canvas. Stateless and
/// reentrant — many layers can be replayed by interleaving calls from different
/// "compositor threads" in the future.
/// </summary>
public sealed class CompositorReplayer
{
    public sealed class Stats
    {
        public long CommandsReplayed;
        public long Replays;
        public long TotalNanos;
        public double MeanMicros => Replays == 0 ? 0 : (double)TotalNanos / Replays / 1_000.0;
    }

    private readonly Stats _stats = new();

    public Stats ReplayStats => _stats;

    public void Replay(CompositorDisplayList list, SKCanvas canvas, SKRect? clip = null)
    {
        if (list is null || canvas is null) return;
        var sw = Clock.NowNanos();
        Interlocked.Increment(ref _stats.Replays);
        list.Touch();

        if (clip.HasValue) canvas.Save(); canvas.ClipRect(clip.Value);
        int saveCount = 0;
        foreach (var c in list.Commands)
        {
            ReplayOne(canvas, c, ref saveCount);
            Interlocked.Increment(ref _stats.CommandsReplayed);
        }
        while (saveCount > 0) { canvas.Restore(); saveCount--; }
        if (clip.HasValue) canvas.Restore();
        Interlocked.Add(ref _stats.TotalNanos, Clock.NowNanos() - sw);
    }

    private static void ReplayOne(SKCanvas canvas, CompositorCommand c, ref int saveCount)
    {
        switch (c.Op)
        {
            case CompositorOp.Save: canvas.Save(); saveCount++; break;
            case CompositorOp.Restore: if (saveCount > 0) { canvas.Restore(); saveCount--; } break;
            case CompositorOp.Translate: canvas.Translate(c.A, c.B); break;
            case CompositorOp.ClipRect: canvas.ClipRect(c.Rect); break;
            case CompositorOp.DrawRect:
                using (var p = new SKPaint { Color = c.Color, Style = SKPaintStyle.Fill, IsAntialias = true })
                    canvas.DrawRect(c.Rect, p);
                break;
            case CompositorOp.DrawRoundedRect:
                if (c.Radius > 0)
                {
                    using var p = new SKPaint { Color = c.Color, Style = SKPaintStyle.Fill, IsAntialias = true, MaskFilter = null };
                    canvas.DrawRoundRect(c.Rect, c.Radius, c.Radius, p);
                }
                else
                {
                    using var p = new SKPaint { Color = c.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
                    // Variable-radius: use single-radius with the largest value
                    float maxR = Math.Max(Math.Max(c.TopLeftRadius, c.TopRightRadius),
                                          Math.Max(c.BottomLeftRadius, c.BottomRightRadius));
                    canvas.DrawRoundRect(c.Rect, maxR, maxR, p);
                }
                break;
            case CompositorOp.DrawBorder:
                using (var p = new SKPaint { Color = c.Color, Style = SKPaintStyle.Stroke, StrokeWidth = c.D, IsAntialias = true })
                    canvas.DrawRect(c.Rect, p);
                break;
            case CompositorOp.DrawBackground:
                using (var p = new SKPaint { Color = c.Color, Style = SKPaintStyle.Fill })
                    canvas.DrawRect(c.Rect, p);
                break;
            case CompositorOp.DrawBoxShadow:
                using (var p = new SKPaint { Color = c.Color, Style = SKPaintStyle.Fill, IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, c.Blur) })
                    canvas.DrawRoundRect(c.Rect, c.Blur, c.Blur, p);
                break;
            case CompositorOp.DrawImage:
                if (c.Image is not null) canvas.DrawImage(c.Image, c.Rect);
                break;
            case CompositorOp.SetOpacity:
                // Use SaveLayer with a paint that has the desired alpha
                using (var lp = new SKPaint { Color = SKColors.White.WithAlpha((byte)(c.A * 255)) })
                {
                    canvas.SaveLayer(lp);
                    saveCount++;
                }
                break;
        }
    }
}
