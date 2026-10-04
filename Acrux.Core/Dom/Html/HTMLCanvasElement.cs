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

    public HTMLCanvasElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "canvas") { }

    public ulong Width
    {
        get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 150;
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
                _context2d ??= new CanvasRenderingContext2D();
                _currentContextType = "2d";
                return _context2d;
            case "webgl":
            case "webgl2":
            case "experimental-webgl":
                _webglContext ??= new WebGLRenderingContext();
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

public class CanvasRenderingContext2D
{
    private SKSurface? _surface;

    /// <summary>Publish a backing surface (from the JS canvas plumbing).</summary>
    public void AttachSurface(SKSurface surface) => _surface = surface;

    /// <summary>Current pixels for painting; null until a surface is attached.</summary>
    public SKImage? SnapshotImage() => _surface?.Snapshot();

    public CanvasElement? Canvas { get; set; }
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
    public void Save() { }
    public void Restore() { }

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
    public object? FillStyle { get; set; } = "#000";
    public object? StrokeStyle { get; set; } = "#000";
    public double? CreateLinearGradient(double x0, double y0, double x1, double y1) => null;
    public double? CreateRadialGradient(double x0, double y0, double r0, double x1, double y1, double r1) => null;
    public double? CreateConicGradient(double startAngle, double x, double y) => null;
    public Pattern? CreatePattern(CanvasImageSource image, string repetition) => null;

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

    // Drawing Images
    public void DrawImage(CanvasImageSource image, double dx, double dy) { }
    public void DrawImage(CanvasImageSource image, double dx, double dy, double dw, double dh) { }
    public void DrawImage(CanvasImageSource image, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh) { }

    // Pixel Manipulation
    public ImageData CreateImageData(double sw, double sh) => new(sw, sh);
    public ImageData CreateImageData(ImageData imagedata) => new(imagedata.Width, imagedata.Height);
    public ImageData GetImageData(double sx, double sy, double sw, double sh) => new(sw, sh);
    public void PutImageData(ImageData imagedata, double dx, double dy) { }
    public void PutImageData(ImageData imagedata, double dx, double dy, double dirtyX, double dirtyY, double dirtyWidth, double dirtyHeight) { }

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

public class CanvasGradient { }
public class CanvasPattern { }

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

public class ImageData
{
    public double Width { get; }
    public double Height { get; }
    public byte[] Data { get; } = Array.Empty<byte>();
    public ColorSpace ColorSpace { get; set; } = ColorSpace.SRgb;

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

public class TextMetrics
{
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

public class ImageBitmapRenderingContext
{
    public void TransferFromImageBitmap(ImageBitmap? bitmap) { }
}

public class WebGLRenderingContext { }

public interface CanvasImageSource { }

public class CanvasElement : CanvasImageSource { }

public enum ImageSmoothingQuality { Low, Medium, High }

public class Pattern
{
    public void SetTransform(DomMatrix? transform) { }
}


