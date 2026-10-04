namespace Acrux.Rendering;

/// <summary>
/// Resolves a CSS url() / <img> src / mask-image reference against the
/// document's base URL into a loadable absolute URL (http(s)://, file://,
/// data:, blob:). Consolidated so background-image, border-image, mask-image
/// and <img>/replaced elements all share one resolution path instead of each
/// painter re-implementing it.
/// </summary>
internal static class UrlResolver
{
    public static string? Resolve(string? raw, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var url = raw.Trim();
        if (url.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            url = url[4..^1].Trim('\'', '"').Trim();
        if (string.IsNullOrEmpty(url)) return null;

        if (url.StartsWith("data:") || url.StartsWith("blob:") ||
            url.StartsWith("http://") || url.StartsWith("https://"))
            return url;

        if (url.StartsWith("//"))
            return !string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://")
                ? "https:" + url
                : "http:" + url;

        if (string.IsNullOrEmpty(baseUrl))
            return url;

        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return url;

        if (baseUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var basePath = baseUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(baseUrl).LocalPath
                    : baseUrl.Substring("file:".Length);
                var dir = Directory.Exists(basePath) ? basePath : Path.GetDirectoryName(basePath) ?? "";
                var combined = Path.GetFullPath(Path.Combine(dir,
                    url.Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/')));
                return "file:///" + combined.Replace('\\', '/');
            }
            catch
            {
                return url;
            }
        }

        try
        {
            var baseUri = new Uri(baseUrl);
            return new Uri(baseUri, url).ToString();
        }
        catch
        {
            return url;
        }
    }
}