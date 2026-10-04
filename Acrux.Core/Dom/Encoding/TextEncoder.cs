using System.Text;

namespace Acrux.Core.Dom.Encoding;

public class TextEncoder
{
    public string Encoding => "utf-8";

    public byte[] Encode(string text)
    {
        return global::System.Text.Encoding.UTF8.GetBytes(text ?? "");
    }

    public EncodedIntoResult EncodeInto(string text, Uint8Array destination)
    {
        var bytes = Encode(text);
        var written = Math.Min(bytes.Length, destination.Length);
        Array.Copy(bytes, 0, destination.Buffer, 0, written);
        return new EncodedIntoResult { Read = text.Length, Written = written };
    }
}

public class EncodedIntoResult
{
    public long Read { get; set; }
    public long Written { get; set; }
}

public class TextDecoder
{
    public TextDecoder(string? label = null, TextDecoderOptions? options = null)
    {
        Label = label ?? "utf-8";
        IgnoreBOM = options?.IgnoreBOM ?? false;
        Fatal = options?.Fatal ?? false;
    }

    public string Label { get; }
    public bool IgnoreBOM { get; }
    public bool Fatal { get; }
    public string Encoding => "utf-8";

    public string Decode(byte[]? buffer = null, TextDecodeOptions? options = null)
    {
        if (buffer == null || buffer.Length == 0) return "";
        return global::System.Text.Encoding.UTF8.GetString(buffer);
    }

    public string Decode(ArrayBuffer? buffer, TextDecodeOptions? options = null)
    {
        if (buffer == null) return "";
        return Decode(buffer.ToArray(), options);
    }
}

public class TextDecoderOptions
{
    public bool Fatal { get; set; }
    public bool IgnoreBOM { get; set; }
}

public class TextDecodeOptions
{
    public bool Stream { get; set; }
}

public class Uint8Array
{
    public byte[] Buffer { get; }
    public int Length => Buffer.Length;

    public Uint8Array(int length) { Buffer = new byte[length]; }
    public Uint8Array(byte[] buffer) { Buffer = buffer; }
    public byte this[int index] { get => Buffer[index]; set => Buffer[index] = value; }
}

public class ArrayBuffer
{
    public byte[] Data { get; }
    public int ByteLength => Data.Length;

    public ArrayBuffer(int length) { Data = new byte[length]; }
    public ArrayBuffer(byte[] data) { Data = data; }
    public byte[] ToArray() => Data;
}

public static class Base64
{
    public static string Btoa(string binaryString)
    {
        return Convert.ToBase64String(global::System.Text.Encoding.Latin1.GetBytes(binaryString ?? ""));
    }

    public static string Atob(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return "";
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            return global::System.Text.Encoding.Latin1.GetString(bytes);
        }
        catch
        {
            throw new DOMException("Invalid character", "InvalidCharacterError");
        }
    }
}
