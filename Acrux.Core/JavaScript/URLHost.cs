namespace Acrux.Core.JavaScript;

public class URLHost
{
    private readonly Uri _uri;
    private URLSearchParamsHost? _searchParams;

    public URLHost(string url, string? baseUrl = null)
    {
        try
        {
            _uri = !string.IsNullOrEmpty(baseUrl)
                ? new Uri(new Uri(baseUrl, UriKind.RelativeOrAbsolute), url)
                : new Uri(url, UriKind.RelativeOrAbsolute);
        }
        catch
        {
            _uri = new Uri("about:blank");
        }
    }

    public string href => _uri.ToString();
    public string origin => _uri.Scheme + "://" + _uri.Host;
    public string protocol => _uri.Scheme + ":";
    public string username => _uri.UserInfo.Split(':').FirstOrDefault() ?? "";
    public string password => _uri.UserInfo.Split(':').ElementAtOrDefault(1) ?? "";
    public string host => _uri.Host + (_uri.IsDefaultPort ? "" : ":" + _uri.Port);
    public string hostname => _uri.Host;
    public string port => _uri.IsDefaultPort ? "" : _uri.Port.ToString();
    public string pathname => _uri.AbsolutePath;
    public string search => _uri.Query;
    public string hash => _uri.Fragment;

    public URLSearchParamsHost searchParams
    {
        get
        {
            _searchParams ??= new URLSearchParamsHost(_uri.Query);
            return _searchParams;
        }
    }

    public void assign(string url) { }
    public void replace(string url) { }
    public void reload() { }

    public override string ToString() => href;

    public static URLHost Create(string url, string? baseUrl = null) => new URLHost(url, baseUrl);
}

public class URLSearchParamsHost
{
    private readonly Dictionary<string, List<string>> _params = new();

    public URLSearchParamsHost(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString)) return;
        
        if (queryString.StartsWith("?"))
            queryString = queryString[1..];

        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx >= 0)
            {
                var key = Uri.UnescapeDataString(pair[..idx]);
                var value = idx < pair.Length - 1 ? Uri.UnescapeDataString(pair[(idx + 1)..]) : "";
                if (!_params.ContainsKey(key))
                    _params[key] = new List<string>();
                _params[key].Add(value);
            }
        }
    }

    public string? get(string name)
    {
        return _params.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;
    }

    public string[] getAll(string name)
    {
        return _params.TryGetValue(name, out var values) ? values.ToArray() : Array.Empty<string>();
    }

    public void append(string name, string value)
    {
        if (!_params.ContainsKey(name))
            _params[name] = new List<string>();
        _params[name].Add(value);
    }

    public void set(string name, string value)
    {
        _params[name] = new List<string> { value };
    }

    public void delete(string name)
    {
        _params.Remove(name);
    }

    public bool has(string name)
    {
        return _params.ContainsKey(name);
    }

    public void sort()
    {
        var sorted = _params.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        _params.Clear();
        foreach (var kv in sorted)
            _params[kv.Key] = kv.Value;
    }

    public void forEach(object callback) { }

    public string toString()
    {
        var parts = new List<string>();
        foreach (var kv in _params)
        {
            foreach (var value in kv.Value)
            {
                parts.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(value)}");
            }
        }
        return string.Join("&", parts);
    }

    public int size => _params.Sum(kv => kv.Value.Count);

    public object[] keys => _params.Keys.ToArray();

    public object[] values => _params.SelectMany(kv => kv.Value).ToArray();

    public object[] entries => _params.SelectMany(kv => kv.Value.Select(v => (object)new[] { kv.Key, v })).ToArray();
}
