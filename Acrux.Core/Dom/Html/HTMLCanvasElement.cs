namespace Acrux.Core.Dom.Html;

using SkiaSharp;

public class HTMLCanvasElement : HtmlElement
{
    private CanvasRenderingContext2D? _context2d;
    private WebGLRenderingContext? _webglContext;
    private string? _currentContextType;

    /// <summary>
    /// Backing-store snapshot for painting. Null until a rendering context
    /// attaches a surface (JS canvas plumbing); a context-less canvas paints
    /// transparent per spec.
    /// </summary>
    public SKImage? SnapshotSurface() => _context2d?.SnapshotImage();

    /// <summary>HTML §4.8.3: the pixels an export is made from. A canvas nobody drew on is not
    /// empty — it is transparent black at its own size — so an export always has an image, and
    /// only a platform with no bitmap backend at all is left without one.</summary>
    public SKImage? SnapshotForExport()
    {
        var drawn = _context2d?.SnapshotImage();
        if (drawn != null) return drawn;
        try
        {
            int w = (int)Math.Clamp(Width, 1u, 32_768u);
            int h = (int)Math.Clamp(Height, 1u, 32_768u);
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null) return null;
            surface.Canvas.Clear(SKColors.Transparent);
            return surface.Snapshot();
        }
        catch
        {
            // Skia's native library is not part of the headless harness; no pixels, no export.
            return null;
        }
    }

    public HTMLCanvasElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "canvas") { }

    public ulong Width
    {
        get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 300;
        set => SetAttribute("width", value.ToString());
    }
    public ulong Height
    {
        get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 150;
        set => SetAttribute("height", value.ToString());
    }

    public object? GetContext(string type, object? options = null)
    {
        if (_currentContextType != null && _currentContextType != type)
            return null;

        switch (type.ToLowerInvariant())
        {
            case "2d":
                _context2d ??= new CanvasRenderingContext2D(this);
                _currentContextType = "2d";
                return _context2d;
            case "webgl":
            case "webgl2":
            case "experimental-webgl":
                // HTML §4.8.3: a WebGL context is served as an object of the interface named, but
                // the engine has no GL command surface behind it (measured: gl.createBuffer is
                // undefined) — the object is what a page that checks the type reads.
                _webglContext ??= new WebGLRenderingContext(this);
                _currentContextType = type;
                return _webglContext;
            case "bitmaprenderer":
                return new ImageBitmapRenderingContext();
            default:
                return null;
        }
    }

    public string? ToDataURL(string? type = "image/png", object? quality = null) => "";
    public Blob? ToBlob(string? type = "image/png", object? quality = null) => null;

    public Task<Blob> ConvertToBlob(object? options = null)
        => Task.FromResult(new Blob(new BlobPropertyBag { Type = "image/png" }));
}

public class CanvasRenderingContext2D : Acrux.Core.JavaScript.IDomPrototypeHost
{
    private SKSurface? _surface;

    /// <summary>HTML §4.8.10.2: a context is an object of the interface the page named, and it
    /// knows the canvas it was made from. The chain names the interface for the prototype fix-up,
    /// and the tag is what a page reads when it asks what it holds.</summary>
    private readonly HTMLCanvasElement? _owner;

    public CanvasRenderingContext2D(HTMLCanvasElement? owner = null)
    {
        _owner = owner;
        Acrux.Core.JavaScript.JsIntegrationService.FixProto(this);
    }

    public string[] __domTypeChain => new[] { "CanvasRenderingContext2D" };

    /// <summary>The canvas is the one object the page already holds, so it goes back through the
    /// engine's identity map rather than as a second wrapper for the same node.</summary>
    public object? canvas => _owner is null ? null
        : Acrux.Core.JavaScript.JsIntegrationService.WrapAny(_owner) ?? _owner;
    public override string ToString() => "[object CanvasRenderingContext2D]";

    /// <summary>Publish a backing surface (from the JS canvas plumbing).</summary>
    public void AttachSurface(SKSurface surface) => _surface = surface;

    /// <summary>Current pixels for painting; null until a surface is attached.</summary>
    public SKImage? SnapshotImage() => _surface?.Snapshot();

    public double GlobalAlpha { get; set; } = 1.0;
    public string GlobalCompositeOperation { get; set; } = "source-over";
    public string? Filter { get; set; }
    public bool ImageSmoothingEnabled { get; set; } = true;
    public ImageSmoothingQuality ImageSmoothingQuality { get; set; } = ImageSmoothingQuality.Low;
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; }
    public double ShadowBlur { get; set; }
    public string ShadowColor { get; set; } = "rgba(0, 0, 0, 0)";
    public string LineCap { get; set; } = "butt";
    public string LineJoin { get; set; } = "miter";
    public double LineWidth { get; set; } = 1.0;
    public double MiterLimit { get; set; } = 10.0;
    public double[] LineDashOffset { get; set; } = Array.Empty<double>();
    public double LineDashOffsetStart { get; set; }
    public string Font { get; set; } = "10px sans-serif";
    public string TextAlign { get; set; } = "start";
    public string TextBaseline { get; set; } = "alphabetic";
    public string Direction { get; set; } = "inherit";
    public string LetterSpacing { get; set; } = "normal";
    public string FontKerning { get; set; } = "auto";
    public string FontStretch { get; set; } = "normal";
    public string FontVariantCaps { get; set; } = "normal";
    public string TextRendering { get; set; } = "auto";
    public string WordSpacing { get; set; } = "normal";

    // State
    private readonly Stack<object?> _savedFillStyles = new();
    public void Save()
    {
        _savedFillStyles.Push(FillStyle);
    }
    public void Restore()
    {
        if (_savedFillStyles.Count > 0)
            FillStyle = _savedFillStyles.Pop();
    }

    // Transforms
    public void Scale(double x, double y) { }
    public void Rotate(double angle) { }
    public void Translate(double x, double y) { }
    public void Transform(double a, double b, double c, double d, double e, double f) { }
    public void SetTransform(double a, double b, double c, double d, double e, double f) { }
    public void SetTransform(DomMatrix? transform) { }
    public DomMatrix GetTransform() => new();

    // Compositing
    public void Clip() { }
    public void Clip(Path2D? path, string? fillRule = null) { }

    // Colors & Styles
    public object? FillStyle { get; set; } = "#000000";
    public object? StrokeStyle { get; set; } = "#000000";
    // The gradients and the pattern are answered from the bootstrap, where the CanvasGradient and
    // CanvasPattern prototype objects live — a host return here would marshal to an object with no
    // interface behind it.

    // Shadows
    // (properties above)

    // Paths
    public void BeginPath() { }
    public void ClosePath() { }
    public void MoveTo(double x, double y) { }
    public void LineTo(double x, double y) { }
    public void QuadraticCurveTo(double cpx, double cpy, double x, double y) { }
    public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y) { }
    public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise = false) { }
    public void ArcTo(double x1, double y1, double x2, double y2, double radius) { }
    public void Ellipse(double x, double y, double radiusX, double radiusY, double rotation, double startAngle, double endAngle, bool counterclockwise = false) { }
    public void Rect(double x, double y, double w, double h) { }
    public void RoundRect(double x, double y, double w, double h, object? radii = null) { }

    // Drawing Rectangles
    public void ClearRect(double x, double y, double w, double h) { }
    public void FillRect(double x, double y, double w, double h) { }
    public void StrokeRect(double x, double y, double w, double h) { }

    // Drawing Text
    public void FillText(string text, double x, double y, double? maxWidth = null) { }
    public void StrokeText(string text, double x, double y, double? maxWidth = null) { }
    public TextMetrics MeasureText(string text) => new();

    // Fill and Stroke
    public void Fill() { }
    public void Stroke() { }

    // Drawing Images
    public void DrawImage(CanvasImageSource image, double dx, double dy) { }
    public void DrawImage(CanvasImageSource image, double dx, double dy, double dw, double dh) { }
    public void DrawImage(CanvasImageSource image, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh) { }

    // Pixel Manipulation
    // createImageData / getImageData / createLinearGradient / createRadialGradient /
    // createConicGradient / createPattern are answered from the bootstrap, on the
    // CanvasRenderingContext2D prototype: a reflector method here becomes a non-configurable own
    // property on the context that hands back a byte array and an enum ordinal instead of the
    // ImageData, gradient and pattern objects the page reads through.

    // Line styles
    public double GetLineDash() => 0;
    public void SetLineDash(double[] segments) { }

    // Image smoothing
    // (properties above)

    // Path2D
    public bool IsPointInPath(double x, double y, string? fillRule = null) => false;
    public bool IsPointInPath(Path2D path, double x, double y, string? fillRule = null) => false;
    public bool IsPointInStroke(double x, double y) => false;
    public bool IsPointInStroke(Path2D path, double x, double y) => false;

    // Filters
    // (property above)

    // Reset
    public void Reset() { }
    public void ResetTransform() { }

    // Layers
    public void BeginLayer() { }
    public void EndLayer() { }

    // Clip
}

public class CanvasGradient : Acrux.Core.JavaScript.IDomPrototypeHost
{
    public CanvasGradient() => Acrux.Core.JavaScript.JsIntegrationService.FixProto(this);
    public string[] __domTypeChain => new[] { "CanvasGradient" };
    public override string ToString() => "[object CanvasGradient]";
    // 2D Context §5: the stops a page adds are the gradient's own state, held here so a later
    // paint could read them even though the engine rasterises through its own display list.
    public List<(double Offset, string Color)> Stops { get; } = new();
    public void AddColorStop(double offset, string color)
    {
        if (!double.IsFinite(offset) || offset < 0 || offset > 1)
            throw new IndexOutOfRangeException("addColorStop");
        Stops.Add((offset, color));
    }
}

public class CanvasPattern : Acrux.Core.JavaScript.IDomPrototypeHost
{
    public CanvasPattern() => Acrux.Core.JavaScript.JsIntegrationService.FixProto(this);
    public string[] __domTypeChain => new[] { "CanvasPattern" };
    public override string ToString() => "[object CanvasPattern]";
    public void SetTransform(DomMatrix? transform) { }
}

public class Path2D
{
    public void AddPath(Path2D path, DomMatrix? transform = null) { }
    public void ClosePath() { }
    public void MoveTo(double x, double y) { }
    public void LineTo(double x, double y) { }
    public void QuadraticCurveTo(double cpx, double cpy, double x, double y) { }
    public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y) { }
    public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise = false) { }
    public void ArcTo(double x1, double y1, double x2, double y2, double radius) { }
    public void Ellipse(double x, double y, double radiusX, double radiusY, double rotation, double startAngle, double endAngle, bool counterclockwise = false) { }
    public void Rect(double x, double y, double w, double h) { }
    public void RoundRect(double x, double y, double w, double h, object? radii = null) { }
}

public class ImageData : Acrux.Core.JavaScript.IDomPrototypeHost
{
    public ImageData() => Acrux.Core.JavaScript.JsIntegrationService.FixProto(this);
    public string[] __domTypeChain => new[] { "ImageData" };
    public override string ToString() => "[object ImageData]";
    public double Width { get; }
    public double Height { get; }
    public byte[] Data { get; } = Array.Empty<byte>();
    public ColorSpace ColorSpace { get; set; } = ColorSpace.SRgb;
    // HTML §4.8.10.15: the page reads 'colorSpace' as the string naming the colour space, not the
    // enum ordinal the CLR hands back.
    public string ColorSpaceName => ColorSpace switch
    {
        ColorSpace.DisplayP3 => "display-p3",
        ColorSpace.LinearSRgb => "srgb-linear",
        _ => "srgb",
    };

    public ImageData(double width, double height)
    {
        Width = width;
        Height = height;
        Data = new byte[(int)(width * height * 4)];
    }

    public ImageData(byte[] data, double width, double height)
    {
        Data = data;
        Width = width;
        Height = height;
    }
}

public enum ColorSpace { SRgb, DisplayP3, LinearSRgb }

public class TextMetrics : Acrux.Core.JavaScript.IDomPrototypeHost
{
    public string[] __domTypeChain => new[] { "TextMetrics" };
    public override string ToString() => "[object TextMetrics]";
    public double Width { get; }
    public double ActualBoundingBoxLeft { get; }
    public double ActualBoundingBoxRight { get; }
    public double FontBoundingBoxAscent { get; }
    public double FontBoundingBoxDescent { get; }
    public double ActualBoundingBoxAscent { get; }
    public double ActualBoundingBoxDescent { get; }
    public double EmHeightAscent { get; }
    public double EmHeightDescent { get; }
    public double HangingBaseline { get; }
    public double AlphabeticBaseline { get; }
    public double IdeographicBaseline { get; }
}

public class ImageBitmapRenderingContext : Acrux.Core.JavaScript.IDomPrototypeHost
{
    public ImageBitmapRenderingContext() => Acrux.Core.JavaScript.JsIntegrationService.FixProto(this);
    public string[] __domTypeChain => new[] { "ImageBitmapRenderingContext" };
    public override string ToString() => "[object ImageBitmapRenderingContext]";
    public void TransferFromImageBitmap(ImageBitmap? bitmap) { }
}

/// <summary>HTML §4.8.10.2: the engine paints through its own pipeline and has no command surface
/// behind a GL context yet, but the object a page holds is still one of the interface the page named
/// — the two questions a page asks any object answer the same way they do in the reference.</summary>
public class WebGLRenderingContext : Acrux.Core.JavaScript.IDomPrototypeHost
{
    private readonly HTMLCanvasElement? _owner;

    public WebGLRenderingContext(HTMLCanvasElement? owner = null)
    {
        _owner = owner;
        Acrux.Core.JavaScript.JsIntegrationService.FixProto(this);
    }

    public string[] __domTypeChain => new[] { "WebGLRenderingContext" };
    public object? canvas => _owner is null ? null
        : Acrux.Core.JavaScript.JsIntegrationService.WrapAny(_owner) ?? _owner;
    public double drawingBufferWidth => _owner is null ? 300 : _owner.Width;
    public double drawingBufferHeight => _owner is null ? 150 : _owner.Height;
    public override string ToString() => "[object WebGLRenderingContext]";
}

public interface CanvasImageSource { }

public class CanvasElement : CanvasImageSource { }

public enum ImageSmoothingQuality { Low, Medium, High }

public class Pattern
{
    public void SetTransform(DomMatrix? transform) { }
}


