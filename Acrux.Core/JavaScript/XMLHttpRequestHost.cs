namespace Acrux.Core.JavaScript;

public class XMLHttpRequestHost
{
    private readonly JavaScriptEngine _engine;
    private readonly HttpClient _httpClient = new();
    
    private string? _method;
    private string? _url;
    private bool _async = true;
    private string? _responseText;
    private object? _responseXml;
    private int _status = 0;
    private string? _statusText;
    private readonly Dictionary<string, string> _requestHeaders = new();
    private readonly Dictionary<string, string> _responseHeaders = new();
    
    private int _readyState = 0;
    public int readyState => _readyState;
    
    public int status => _status;
    public string? statusText => _statusText;
    public string? responseText => _responseText;
    public object? responseXML => _responseXml;
    public string? response => _responseText;
    public string responseType { get; set; } = "text";
    public int timeout { get; set; } = 0;
    public bool withCredentials { get; set; } = false;
    
    public object? onreadystatechange;
    public object? onload;
    public object? onerror;
    public object? onprogress;
    public object? onabort;
    public object? ontimeout;
    public object? onloadstart;
    public object? onloadend;
    
    public XMLHttpRequestHost(JavaScriptEngine engine)
    {
        _engine = engine;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
    
    public void open(string method, string url, bool async = true, string? user = null, string? password = null)
    {
        _method = method;
        _url = url;
        _async = async;
        _readyState = 1;
        InvokeReadyStateChange();
    }
    
    public void setRequestHeader(string header, string value)
    {
        _requestHeaders[header] = value;
    }
    
    public void send(object? body = null)
    {
        if (_method == null || _url == null)
            throw new InvalidOperationException("open() must be called first");
        
        _readyState = 2;
        InvokeReadyStateChange();
        InvokeEvent("loadstart");
        
        Task.Run(async () =>
        {
            try
            {
                var request = new HttpRequestMessage(new HttpMethod(_method!), _url);
                
                foreach (var header in _requestHeaders)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                
                if (body != null && (_method == "POST" || _method == "PUT" || _method == "PATCH"))
                {
                    request.Content = new StringContent(body.ToString() ?? "");
                }
                
                var response = await _httpClient.SendAsync(request);
                _status = (int)response.StatusCode;
                _statusText = response.ReasonPhrase ?? "";
                
                foreach (var header in response.Headers)
                {
                    _responseHeaders[header.Key] = string.Join(", ", header.Value);
                }
                
                _responseText = await response.Content.ReadAsStringAsync();
                _readyState = 4;
                
                _engine.Execute($"__g_invoke({GetCallbackId("onload")}, {{ status: {_status}, response: '{EscapeJs(_responseText ?? "")}' }})");
                InvokeReadyStateChange();
                InvokeEvent("load");
                InvokeEvent("loadend");
            }
            catch (Exception ex)
            {
                _status = 0;
                _statusText = ex.Message;
                _readyState = 4;
                
                InvokeReadyStateChange();
                InvokeEvent("error");
                InvokeEvent("loadend");
            }
        });
    }
    
    public void abort()
    {
        _readyState = 0;
        InvokeReadyStateChange();
        InvokeEvent("abort");
        InvokeEvent("loadend");
    }
    
    public string? getAllResponseHeaders()
    {
        return string.Join("\r\n", _responseHeaders.Select(kv => $"{kv.Key}: {kv.Value}"));
    }
    
    public string? getResponseHeader(string name)
    {
        return _responseHeaders.GetValueOrDefault(name);
    }
    
    public void overrideMimeType(string mimeType) { }
    
    private void InvokeReadyStateChange()
    {
        InvokeEvent("readystatechange");
    }
    
    private void InvokeEvent(string eventName)
    {
        var callbackId = GetCallbackId($"on{eventName}");
        if (callbackId > 0)
        {
            _engine.InvokeCallback(callbackId);
        }
    }
    
    private int GetCallbackId(string propertyName)
    {
        try
        {
            var result = _engine.Evaluate($"typeof {propertyName} === 'function' ? {propertyName} : null");
            if (result != null)
            {
                return _engine.StoreCallbackRef(result);
            }
        }
        catch { }
        return 0;
    }
    
    private static string EscapeJs(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }
    
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
