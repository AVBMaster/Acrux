using Acrux.Core.Dom;

using System.Collections.Generic;
using System.Text;

namespace Acrux.Core.JavaScript;

/// <summary>The URL interface (URL §2.2, §7): the parts read back out of the parsed address, each one
/// writable through the whole it belongs to. The host declares its interface so the page sees a URL and
/// not the fields behind it (measured: '[object URL]' with no own keys).</summary>
public class URLHost : IDomPrototypeHost
{
    private WhatwgUrl _url;
    private URLSearchParamsHost? _searchParams;

    /// <summary>URL §2.2, §4.1: the constructor IS the parser, and a parse that fails is a TypeError —
    /// there is no blank url to fall back to. A relative input needs a base, and the two refusals name
    /// the piece that failed (measured: 'Invalid URL' for a scheme-less input with no base, 'Invalid
    /// base URL' for a base that is not itself absolute).</summary>
    public URLHost(string url, string? baseUrl = null)
    {
        var made = WhatwgUrl.Parse(url ?? "", string.IsNullOrEmpty(baseUrl) ? null : baseUrl, out var baseBad);
        if (made == null)
        {
            throw Invalid(baseBad
                ? "Failed to construct 'URL': Invalid base URL."
                : "Failed to construct 'URL': Invalid URL.");
        }
        _url = made;
    }

    private static DOMException Invalid(string message) => new(message, "TypeError");

    public string href => _url.Href();

    /// <summary>URL §2.6, §6.5: only the schemes that can name an origin do — a file, data or about
    /// address has an opaque one, and an opaque origin is serialised as the string 'null'.</summary>
    public string origin => _url.Origin();

    public string protocol
    {
        get => _url.Scheme + ":";
        set => _url.SetProtocol(value);
    }

    public string username
    {
        get => _url.Username;
        set => _url.SetUsername(value);
    }

    public string password
    {
        get => _url.Password;
        set => _url.SetPassword(value);
    }

    /// <summary>§7.4: the host and the port the url carries, the port left out when the scheme already
    /// implies it.</summary>
    public string host
    {
        get => _url.Host + (_url.Port != null ? ":" + _url.Port.Value : "");
        set => _url.SetHost(value);
    }

    /// <summary>§7.5: the host alone — brackets and all for an IPv6 address, which is how the standard
    /// serialises one — and never the port beside it.</summary>
    public string hostname
    {
        get => _url.Host;
        set => _url.SetHostname(value);
    }

    public string port
    {
        get => _url.Port != null ? _url.Port.Value.ToString() : "";
        set => _url.SetPort(value);
    }

    public string pathname
    {
        get => _url.Pathname();
        set => _url.SetPathname(value);
    }

    public string search
    {
        get => _url.Query != null ? "?" + _url.Query : "";
        set
        {
            _url.SetSearch(value);
            _searchParams?.Reload(_url.QueryForSearchParams());
        }
    }

    public string hash
    {
        get => _url.Fragment != null ? "#" + _url.Fragment : "";
        set => _url.SetHash(value);
    }

    /// <summary>URL §2.7: the params view is the query and not a copy of it — a pair appended through
    /// it is in the address the page reads back, and a query written over the whole is what the view
    /// answers afterwards.</summary>
    public URLSearchParamsHost searchParams
    {
        get
        {
            if (_searchParams == null)
                _searchParams = new URLSearchParamsHost(_url.QueryForSearchParams(), this);
            return _searchParams;
        }
    }

    /// <summary>The write the params view makes back into the address it belongs to (§7.8).</summary>
    internal void WriteQuery(string serialized) => _url.SetQueryFromSearchParams(serialized);

    /// <summary>URL §4.6: the serialisation the interface gives — every component of the address the
    /// page holds, under the names the interface reads them by.</summary>
    public Dictionary<string, object?> toJSON() => new()
    {
        ["href"] = href, ["origin"] = origin, ["protocol"] = protocol, ["username"] = username,
        ["password"] = password, ["host"] = host, ["hostname"] = hostname, ["port"] = port,
        ["pathname"] = pathname, ["search"] = search, ["hash"] = hash
    };

    public void assign(string url) { }
    public void replace(string url) { }
    public void reload() { }

    public string __domType => "URL";
    public string[] __domTypeChain => new[] { "URL" };

    public override string ToString() => href;

    public static URLHost Create(string url, string? baseUrl = null) => new URLHost(url, baseUrl);
}

/// <summary>The query of a URL, read and written as a list of name-value pairs (URL §4, §7.1). The
/// list is the interface the page walks, so it declares itself as one (measured: '[object
/// URLSearchParams]' with its members on the prototype and no own keys of its own).</summary>
public class URLSearchParamsHost : IDomPrototypeHost
{
    public string __domType => "URLSearchParams";
    public string[] __domTypeChain => new[] { "URLSearchParams" };
    private readonly List<KeyValuePair<string, string>> _pairs = new();
    private readonly URLHost? _owner;

    public URLSearchParamsHost(string? queryString, URLHost? owner = null)
    {
        _owner = owner;
        Parse(queryString);
    }

    /// <summary>§5.3, the form-urlencoded parser: the pairs divided by ampersands, each name and value
    /// percent-decoded with the plus already read as a space, and a pair without an equals sign
    /// keeping the empty string as its value.</summary>
    private void Parse(string? queryString)
    {
        _pairs.Clear();
        if (string.IsNullOrEmpty(queryString)) return;
        var text = queryString!;
        if (text[0] == '?') text = text[1..];
        foreach (var pair in text.Split('&'))
        {
            if (pair.Length == 0) continue;
            var idx = pair.IndexOf('=');
            var key = idx < 0 ? pair : pair[..idx];
            var value = idx < 0 ? "" : pair[(idx + 1)..];
            // The plus is a space before anything is decoded, so an escape that reads as a plus
            // stays the character the page wrote (§5.3).
            _pairs.Add(new KeyValuePair<string, string>(
                WhatwgUrl.Decode(key.Replace('+', ' ')), WhatwgUrl.Decode(value.Replace('+', ' '))));
        }
    }

    internal void Reload(string? queryString) => Parse(queryString);

    /// <summary>Every write to the view is a write to the address that holds it (§2.7).</summary>
    private void Flush() => _owner?.WriteQuery(toString());

    public string? get(string name)
    {
        foreach (var pair in _pairs) if (pair.Key == name) return pair.Value;
        return null;
    }

    public string[] getAll(string name)
    {
        var made = new List<string>();
        foreach (var pair in _pairs) if (pair.Key == name) made.Add(pair.Value);
        return made.ToArray();
    }

    public void append(string name, string value)
    {
        _pairs.Add(new KeyValuePair<string, string>(name, value));
        Flush();
    }

    public void set(string name, string value)
    {
        var kept = new List<KeyValuePair<string, string>>();
        var written = false;
        foreach (var pair in _pairs)
        {
            if (pair.Key != name) { kept.Add(pair); continue; }
            if (written) continue;
            kept.Add(new KeyValuePair<string, string>(name, value));
            written = true;
        }
        if (!written) kept.Add(new KeyValuePair<string, string>(name, value));
        _pairs.Clear();
        _pairs.AddRange(kept);
        Flush();
    }

    public void delete(string name)
    {
        _pairs.RemoveAll(pair => pair.Key == name);
        Flush();
    }

    public bool has(string name)
    {
        foreach (var pair in _pairs) if (pair.Key == name) return true;
        return false;
    }

    /// <summary>§5.5: sorted by the name, and the run of pairs that share one name keeps the order it
    /// was written in — the standard asks for a stable sort.</summary>
    public void sort()
    {
        var sorted = new List<KeyValuePair<string, string>>(_pairs);
        sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        _pairs.Clear();
        _pairs.AddRange(sorted);
        Flush();
    }

    /// <summary>§5.3, the serializer: every name and value encoded with the form-urlencoded set, which
    /// writes a space as a plus — the reason a round trip through this string is not the string that
    /// came in.</summary>
    public string toString()
    {
        var sb = new StringBuilder();
        foreach (var pair in _pairs)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(WhatwgUrl.FormUrlencode(pair.Key)).Append('=').Append(WhatwgUrl.FormUrlencode(pair.Value));
        }
        return sb.ToString();
    }

    public int size => _pairs.Count;

    // keys/values/entries are the iterator methods the interface defines (URL §5), each answering the
    // pairs/keys/values the page enumerates. The iterator is built on the script side — the interface
    // it belongs to is script data — so the host hands the items over and gets the object back.
    public object keys() => AsIterator("URLSearchParams", Names());
    public object values() => AsIterator("URLSearchParams", Values());
    public object entries() => AsIterator("URLSearchParams", Pairs());

    private List<object> Names()
    {
        var list = new List<object>();
        foreach (var pair in _pairs) list.Add(pair.Key);
        return list;
    }

    private List<object> Values()
    {
        var list = new List<object>();
        foreach (var pair in _pairs) list.Add(pair.Value);
        return list;
    }

    private List<object> Pairs()
    {
        var list = new List<object>();
        foreach (var pair in _pairs) list.Add(new object[] { pair.Key, pair.Value });
        return list;
    }

    private static object AsIterator(string interfaceName, List<object> items)
    {
        // The iterator an interface names is built on the script side, which owns that interface; the
        // host only has the items to hand over (URL §5). A host with no script side keeps the array.
        var made = JavaScriptEngine.Current?.IntegrationService?.CallGlobal(
            "__acruxMakeIterator", interfaceName, items.ToArray());
        return made ?? items.ToArray();
    }
}
