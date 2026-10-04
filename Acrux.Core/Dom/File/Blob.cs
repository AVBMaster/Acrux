using Acrux.Core.Dom.Html;

namespace Acrux.Core.Dom;

public class Blob
{
    public ulong Size { get; }
    public string Type { get; }

    private readonly byte[] _data;

    public Blob(BlobPropertyBag? options = null)
    {
        Type = options?.Type ?? "";
        _data = Array.Empty<byte>();
    }

    public Blob(string[]? blobParts, BlobPropertyBag? options = null)
    {
        Type = options?.Type ?? "";
        _data = Array.Empty<byte>();
    }

    public Blob(byte[] data, string type = "")
    {
        _data = data;
        Type = type;
        Size = (ulong)data.Length;
    }

    public async Task<Blob> Slice(long? start = null, long? end = null, string? contentType = null)
    {
        return new Blob(_data, contentType ?? Type);
    }

    public async Task<byte[]> ArrayBuffer()
    {
        return _data;
    }

    public async Task<string> Text()
    {
        return System.Text.Encoding.UTF8.GetString(_data);
    }

    public ReadableStream? Stream()
    {
        return null;
    }
}

public class BlobPropertyBag
{
    public string Type { get; set; } = "";
    public string? Endings { get; set; }
}

public class DomFile : Blob
{
    public string Name { get; }
    public long LastModified { get; }

    public DomFile(string[] fileBits, string name, FilePropertyBag? options = null)
        : base(fileBits, options)
    {
        Name = name;
        LastModified = options?.LastModified ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public DomFile(byte[] data, string name, string type = "")
        : base(data, type)
    {
        Name = name;
        LastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

public class FilePropertyBag : BlobPropertyBag
{
    public long? LastModified { get; set; }
}

public class FileList : IReadOnlyList<DomFile>
{
    private readonly List<DomFile> _files = new();

    public int Length => _files.Count;
    public int Count => _files.Count;
    public DomFile? this[int index] => index >= 0 && index < _files.Count ? _files[index] : null;

    public void Add(DomFile file) => _files.Add(file);
    public void Remove(DomFile file) => _files.Remove(file);

    public IEnumerator<DomFile> GetEnumerator() => _files.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _files.GetEnumerator();
}

public class ReadableStream
{
    public bool Locked { get; }
    public ReadableStreamDefaultReader? GetReader() => null;
    public ReadableStream? PipeThrough(TransformStream transform, StreamPipeOptions? options = null) => null;
    public async Task PipeTo(WritableStream destination, StreamPipeOptions? options = null) { }
    public async Task<ReadableStream[]> Tee() => new[] { new ReadableStream(), new ReadableStream() };
    public void Cancel(string? reason = null) { }
}

public class ReadableStreamDefaultReader
{
    public async Task<ReadableStreamReadResult> Read() => new();
    public void Cancel(string? reason = null) { }
    public void ReleaseLock() { }
}

public class ReadableStreamReadResult
{
    public bool Done { get; }
    public object? Value { get; }
}

public class WritableStream { }
public class TransformStream { }
public class StreamPipeOptions { }

public class ImageBitmap : CanvasImageSource
{
    public ulong Width { get; }
    public ulong Height { get; }

    public ImageBitmap(ulong width = 0, ulong height = 0)
    {
        Width = width;
        Height = height;
    }

    public void Close() { }
}

public static class ImageBitmapFactories
{
    public static async Task<ImageBitmap> CreateImageBitmap(
        ImageBitmapSource image, ImageBitmapOptions? options = null) => new();

    public static async Task<ImageBitmap> CreateImageBitmap(
        ImageBitmapSource image, long sx, long sy, long sw, long sh,
        ImageBitmapOptions? options = null) => new();
}

public interface ImageBitmapSource { }

public class ImageBitmapOptions
{
    public string ImageOrientation { get; set; } = "none";
    public string PremultiplyAlpha { get; set; } = "default";
    public string ColorSpaceConversion { get; set; } = "default";
    public ulong? ResizeWidth { get; set; }
    public ulong? ResizeHeight { get; set; }
    public string ResizeQuality { get; set; } = "low";
}

public class OffscreenCanvas : ImageBitmapSource
{
    public ulong Width { get; set; }
    public ulong Height { get; set; }

    public OffscreenCanvas(ulong width, ulong height)
    {
        Width = width;
        Height = height;
    }

    public CanvasRenderingContext2D? GetContext(string type, object? options = null) => null;
    public ImageBitmap TransferToImageBitmap() => new();
    public async Task<Blob> ConvertToBlob(object? options = null) => new();
}

public class CanvasCaptureMediaStream : MediaStream
{
    public HTMLCanvasElement Canvas { get; }
    public CanvasCaptureMediaStream(HTMLCanvasElement canvas) { Canvas = canvas; }
    public void RequestFrame() { }
}
