namespace Acrux.Core.Network;

/// <summary>
/// Cookie management system - stores, retrieves, and manages HTTP cookies.
/// Inspired by Chromium's CookieMonster architecture.
/// </summary>
public class CookieJar
{
    private readonly Dictionary<string, List<Cookie>> _cookies = new();
    private readonly object _lock = new();

    public void SetCookie(string url, string setCookieHeader)
    {
        var uri = ParseUrl(url);
        if (uri == null) return;

        var cookie = ParseSetCookie(setCookieHeader, uri);
        if (cookie == null) return;

        lock (_lock)
        {
            var domain = cookie.Domain ?? uri.Host;
            if (!_cookies.ContainsKey(domain))
                _cookies[domain] = new List<Cookie>();

            var existing = _cookies[domain].FindAll(c => c.Name == cookie.Name && c.Path == cookie.Path);
            foreach (var c in existing)
                _cookies[domain].Remove(c);

            _cookies[domain].Add(cookie);
            EvictExpired();
        }
    }

    public string GetCookieHeader(string url)
    {
        var uri = ParseUrl(url);
        if (uri == null) return string.Empty;

        lock (_lock)
        {
            var result = new List<string>();

            foreach (var (domain, cookies) in _cookies)
            {
                if (DomainMatches(domain, uri.Host))
                {
                    foreach (var cookie in cookies)
                    {
                        if (PathMatches(cookie.Path, uri.Path) && !cookie.IsExpired())
                        {
                            if (cookie.Secure && uri.Scheme != "https") continue;
                            result.Add($"{cookie.Name}={cookie.Value}");
                        }
                    }
                }
            }

            return string.Join("; ", result);
        }
    }

    public void Clear()
    {
        lock (_lock)
            _cookies.Clear();
    }

    public void ClearForDomain(string domain)
    {
        lock (_lock)
            _cookies.Remove(domain);
    }

    private void EvictExpired()
    {
        foreach (var (domain, cookies) in _cookies.ToList())
        {
            cookies.RemoveAll(c => c.IsExpired());
            if (cookies.Count == 0)
                _cookies.Remove(domain);
        }
    }

    private Cookie? ParseSetCookie(string header, ParsedUrl uri)
    {
        var parts = header.Split(';').Select(p => p.Trim()).ToList();
        if (parts.Count == 0) return null;

        var nameValue = parts[0].Split('=', 2);
        if (nameValue.Length != 2) return null;

        var cookie = new Cookie
        {
            Name = nameValue[0].Trim(),
            Value = nameValue[1].Trim(),
            Domain = uri.Host,
            Path = "/",
            HttpOnly = false,
            Secure = false
        };

        for (int i = 1; i < parts.Count; i++)
        {
            var attr = parts[i];
            var eqIdx = attr.IndexOf('=');
            if (eqIdx < 0)
            {
                switch (attr.ToLowerInvariant())
                {
                    case "httponly": cookie.HttpOnly = true; break;
                    case "secure": cookie.Secure = true; break;
                }
            }
            else
            {
                var name = attr[..eqIdx].Trim().ToLowerInvariant();
                var value = attr[(eqIdx + 1)..].Trim();

                switch (name)
                {
                    case "domain":
                        cookie.Domain = value.StartsWith(".") ? value[1..] : value;
                        break;
                    case "path":
                        cookie.Path = value;
                        break;
                    case "expires":
                        if (DateTime.TryParse(value, out var exp))
                            cookie.Expires = exp;
                        break;
                    case "max-age":
                        if (int.TryParse(value, out var maxAge))
                            cookie.Expires = DateTime.UtcNow.AddSeconds(maxAge);
                        break;
                    case "samesite":
                        cookie.SameSite = value.ToLowerInvariant() switch
                        {
                            "strict" => SameSite.Strict,
                            "lax" => SameSite.Lax,
                            _ => SameSite.None
                        };
                        break;
                }
            }
        }

        return cookie;
    }

    private bool DomainMatches(string cookieDomain, string requestHost)
    {
        if (cookieDomain == requestHost) return true;
        if (requestHost.EndsWith("." + cookieDomain)) return true;
        return false;
    }

    private bool PathMatches(string cookiePath, string requestPath)
    {
        if (cookiePath == requestPath) return true;
        if (requestPath.StartsWith(cookiePath + "/")) return true;
        if (cookiePath == "/" && requestPath.StartsWith("/")) return true;
        return false;
    }

    private ParsedUrl? ParseUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return new ParsedUrl
            {
                Scheme = uri.Scheme,
                Host = uri.Host,
                Port = uri.Port,
                Path = uri.AbsolutePath
            };
        }
        catch
        {
            return null;
        }
    }
}

public class Cookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string Path { get; set; } = "/";
    public DateTime? Expires { get; set; }
    public bool HttpOnly { get; set; }
    public bool Secure { get; set; }
    public SameSite SameSite { get; set; }

    public bool IsExpired()
    {
        if (!Expires.HasValue) return false;
        return Expires.Value < DateTime.UtcNow;
    }
}

public enum SameSite { None, Lax, Strict }

internal class ParsedUrl
{
    public string Scheme { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// CORS (Cross-Origin Resource Sharing) policy checker.
/// Implements the CORS specification for preflight and simple requests.
/// </summary>
public class CorsPolicy
{
    private readonly string _origin;
    private readonly HashSet<string> _allowedOrigins = new();
    private readonly HashSet<string> _allowedMethods = new() { "GET", "HEAD", "POST" };
    private readonly HashSet<string> _allowedHeaders = new();

    public CorsPolicy(string origin)
    {
        _origin = origin;
    }

    public void AddAllowedOrigin(string origin) => _allowedOrigins.Add(origin);
    public void AddAllowedMethod(string method) => _allowedMethods.Add(method.ToUpperInvariant());
    public void AddAllowedHeader(string header) => _allowedHeaders.Add(header.ToLowerInvariant());

    public bool IsSimpleRequest(string method, IEnumerable<string> headers)
    {
        if (!_allowedMethods.Contains(method.ToUpperInvariant()))
            return false;

        foreach (var header in headers)
        {
            var lower = header.ToLowerInvariant();
            if (!IsCorsSafeHeader(lower))
                return false;
        }

        return true;
    }

    public bool IsCorsSafeHeader(string header)
    {
        return header switch
        {
            "accept" or "accept-language" or "content-language" or "content-type" => true,
            _ => false
        };
    }

    public bool IsContentTypeSafe(string contentType)
    {
        var lower = contentType.ToLowerInvariant();
        return lower.StartsWith("application/x-www-form-urlencoded") ||
               lower.StartsWith("multipart/form-data") ||
               lower.StartsWith("text/plain");
    }

    public Dictionary<string, string> GetPreflightHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = _allowedOrigins.Contains("*") ? "*" : _origin,
            ["Access-Control-Allow-Methods"] = string.Join(", ", _allowedMethods),
            ["Access-Control-Allow-Headers"] = string.Join(", ", _allowedHeaders),
            ["Access-Control-Max-Age"] = "86400"
        };
    }

    public Dictionary<string, string> GetResponseHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = _allowedOrigins.Contains("*") ? "*" : _origin
        };
    }

    public bool CheckOrigin(string origin)
    {
        return _allowedOrigins.Contains("*") || _allowedOrigins.Contains(origin);
    }
}

/// <summary>
/// HTTP redirect handler - follows redirects up to a maximum count.
/// </summary>
public class RedirectHandler
{
    private const int MaxRedirects = 10;
    private int _redirectCount;

    public string? LastRedirectUrl { get; private set; }

    public bool ShouldRedirect(int statusCode)
    {
        return statusCode is 301 or 302 or 303 or 307 or 308;
    }

    public string? GetRedirectLocation(Dictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (key.Equals("Location", StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return null;
    }

    public bool CanRedirect()
    {
        return _redirectCount < MaxRedirects;
    }

    public void RecordRedirect()
    {
        _redirectCount++;
    }

    public void Reset()
    {
        _redirectCount = 0;
        LastRedirectUrl = null;
    }

    public string ResolveRedirectUrl(string currentUrl, string location)
    {
        if (location.StartsWith("http://") || location.StartsWith("https://"))
            return location;

        try
        {
            var uri = new Uri(currentUrl);
            var baseUri = new Uri(uri.GetLeftPart(UriPartial.Authority));
            var resolved = new Uri(baseUri, location);
            return resolved.ToString();
        }
        catch
        {
            return location;
        }
    }
}
