using System.Text.Json.Nodes;

namespace Acrux.Core.JavaScript;

internal static class JsonAotHelper
{
    public static JsonNode? ToJsonNode(object? value)
    {
        if (value == null) return null;
        if (value is JsonNode node) return node;
        if (value is int i) return JsonValue.Create(i);
        if (value is long l) return JsonValue.Create(l);
        if (value is double d) return JsonValue.Create(d);
        if (value is float f) return JsonValue.Create(f);
        if (value is decimal m) return JsonValue.Create(m);
        if (value is string s) return JsonValue.Create(s);
        if (value is bool b) return JsonValue.Create(b);
        if (value is short sh) return JsonValue.Create((int)sh);
        if (value is byte by) return JsonValue.Create((int)by);
        if (value is uint ui) return JsonValue.Create((long)ui);
        if (value is ulong ul) return JsonValue.Create(unchecked((long)ul));
        if (value is object[] arr)
        {
            var ja = new JsonArray();
            foreach (var item in arr)
                ja.Add(ToJsonNode(item));
            return ja;
        }
        if (value is System.Collections.IList list)
        {
            var ja = new JsonArray();
            foreach (var item in list)
                ja.Add(ToJsonNode(item));
            return ja;
        }
        return JsonValue.Create(value.ToString());
    }

    public static string ToJson(object?[] args)
    {
        var arr = new JsonArray();
        foreach (var arg in args)
            arr.Add(ToJsonNode(arg));
        return arr.ToJsonString();
    }
}
