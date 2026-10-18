using Acrux.Core.Dom;

namespace Acrux.Core.JavaScript;

/// <summary>XMLHttpRequest §2: the request a page opens, sends and reads back.
/// <para>
/// The object starts at UNSENT and every member that needs a request in flight refuses the way the
/// reference does: 'send()' before 'open()' and 'setRequestHeader()' before 'open()' both answer
/// InvalidStateError naming the state that was required, 'open()' with fewer than two arguments
/// refuses for the count, and a header name that is not a field name answers SyntaxError quoting the
/// spelling it was given (all measured).</para>
/// <para>
/// The defaults are the strings the page reads before anything arrives — 'responseText', 'response' and
/// 'statusText' are the empty string rather than null, and 'responseType' is the empty string, which is
/// the type the response is read as. Reading 'responseText' once the type has moved off the character
/// spellings refuses, naming the type the object carries (measured). A response read as 'json',
/// 'arraybuffer', 'blob' or 'document' is remembered but not built, so 'response' answers null for
/// those — which is also what the reference answers before the body has arrived.</para>
/// <para>
/// Events leave the engine through the interface's own dispatch point: the state machine calls
/// '__acruxXhrEvent' with this host as its argument, and that runs the listener bag and the 'on' plus
/// type member in JavaScript — the same route a custom element reaction takes, so no callback is built
/// in C#. The old route looked a global named 'onload' up, which is not the object's handler.</para>
/// </summary>
public class XMLHttpRequestHost : IDomPrototypeHost
{
    private readonly JavaScriptEngine _engine;
    private readonly HttpClient _httpClient = new();
    private readonly XMLHttpRequestUploadHost _upload = new();

    private string? _method;
    private string? _url;
    private bool _async = true;
    private string? _user;
    private string? _password;
    private string _responseType = "";
    private string _responseText = "";
    private object? _responseXml;
    private int _status;
    private string _statusText = "";
    private int _readyState;
    private bool _sending;
    private bool _terminated;
    private CancellationTokenSource? _cancellation;
    private readonly Dictionary<string, string> _requestHeaders = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _responseHeaders = new(StringComparer.OrdinalIgnoreCase);

    public string? __domType => "XMLHttpRequest";
    public string[] __domTypeChain => new[] { "EventTarget", "XMLHttpRequest" };
    public override string ToString() => DomHostString.Tagged(this);

    // ===== The state the page reads (§2.2, §2.4) =====

    public int readyState => _readyState;
    public int status => _status;
    public string statusText => _statusText;

    /// <summary>XMLHttpRequest §2.4: the address the response finally came from, once redirects have
    /// been followed — the address the request was opened with while it runs, and the empty string
    /// before a request has been opened at all (measured). Nothing is read back from the redirect
    /// chain here, so the answer is the address the page asked for.</summary>
    public string responseURL => _readyState == 0 ? "" : _url ?? "";
    public double timeout { get; set; }
    public bool withCredentials { get; set; }

    /// <summary>§2.4: the upload half of the request is an event target of its own — never null — and it
    /// carries its own 'withCredentials'.</summary>
    public XMLHttpRequestUploadHost upload => _upload;

    /// <summary>§2.4: the type the response is read as. A spelling outside the set the interface allows
    /// is dropped without a word (measured: setting a value it does not know leaves the type the empty
    /// string it was).</summary>
    public string responseType
    {
        get => _responseType;
        set
        {
            if (value is "" or "text" or "arraybuffer" or "blob" or "json" or "document")
                _responseType = value;
        }
    }

    /// <summary>§2.4: the body as characters, readable only while the response is being read as
    /// characters (measured: once the type moves to 'json' the read refuses with InvalidStateError
    /// naming the type the object carries).</summary>
    public string responseText
    {
        get
        {
            if (_responseType is not ("" or "text"))
                throw new DOMException("Failed to read the 'responseText' property from "
                    + "'XMLHttpRequest': The value is only accessible if the object's 'responseType' "
                    + $"is '' or 'text' (was '{_responseType}').", "InvalidStateError");
            return _responseText;
        }
    }

    /// <summary>§2.4: the response in the shape the type asked for — the characters while the type is
    /// one of the two spellings that read characters, and nothing else built yet.</summary>
    public object? response => _responseType switch
    {
        "" or "text" => responseText,
        "document" => _responseXml,
        _ => null
    };

    public object? responseXML => _responseXml;

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

    // ===== Building the request (§2.6.2) =====

    /// <summary>§2.6.2: two arguments are required before anything else is looked at, so 'open()' with
    /// none refuses for the count (measured: "2 arguments required, but only 0 present."). The verb and
    /// the address are read as text whatever the page handed, and the third argument is the flag that
    /// says whether the request runs in the background.</summary>
    public void open(params object?[] args)
    {
        if (args.Length < 2)
        {
            throw new DOMException("Failed to execute 'open' on 'XMLHttpRequest': 2 arguments"
                + $" required, but only {args.Length} present.", "TypeError");
        }
        _method = ToText(args[0]);
        _url = ToText(args[1]);
        _async = args.Length < 3 || ToBool(args[2]);
        _user = args.Length > 3 ? ToText(args[3]) : null;
        _password = args.Length > 4 ? ToText(args[4]) : null;
        // §2.6.2: opening again starts the request over, so what the last one read is dropped.
        _terminated = false;
        _status = 0;
        _statusText = "";
        _responseText = "";
        _responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _readyState = 1;
        InvokeReadyStateChange();
    }

    // An IDL 'DOMString' argument is the value converted to text and an 'optional boolean' is the value
    // made true or false, so a page that hands either as a number or an object still gets the reading
    // the conversion gives.
    private static string ToText(object? value) => value switch
    {
        null => "null",
        string text => text,
        bool flag => flag ? "true" : "false",
        int whole => whole.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long whole => whole.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0,
        double number => number != 0 && !double.IsNaN(number),
        int whole => whole != 0,
        _ => true
    };

    /// <summary>§2.6.3: a header is only settable while the request is open, and the name has to be a
    /// field name — a space in it is refused for the spelling it was given (measured).</summary>
    public void setRequestHeader(string header, string value)
    {
        if (_readyState != 1) throw MustBeOpened("setRequestHeader");
        if (header.Length == 0 || !IsFieldToken(header))
        {
            throw new DOMException("Failed to execute 'setRequestHeader' on 'XMLHttpRequest': "
                + $"'{header}' is not a valid HTTP header field name.", "SyntaxError");
        }
        _requestHeaders[header] = value;
    }

    /// <summary>§2.6.4: the request goes out from OPENED and from nowhere else (measured: a second
    /// 'send()' while the first is in flight refuses for the same state). An asynchronous request reads
    /// its answer on a task and hands the completion back through the microtask checkpoint, because the
    /// engine is not thread-safe and a callback raised from the task corrupts it; a synchronous one
    /// stays on the thread that asked and fires its events inline.</summary>
    public void send(object? body = null)
    {
        if (_readyState != 1 || _sending) throw MustBeOpened("send");

        _readyState = 2;
        _sending = true;
        InvokeReadyStateChange();
        InvokeEvent("loadstart");

        if (_async)
        {
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var token = cancellation.Token;
            _engine.IntegrationService?.NoteBackgroundJob(true);
            Task.Run(async () =>
            {
                var exchange = await ReadAsync(body, token);
                try
                {
                    // The job runs on the JavaScript thread, which is where the page's handlers live.
                    _engine.IntegrationService?.EnqueueMicrotask(() => Finish(exchange));
                }
                finally
                {
                    _engine.IntegrationService?.NoteBackgroundJob(false);
                }
            });
            return;
        }

        var syncToken = (_cancellation = new CancellationTokenSource()).Token;
        Finish(ReadAsync(body, syncToken).GetAwaiter().GetResult());
    }

    // The answer read from the wire, kept as a value so nothing about the response is written to the
    // request's own state from the task that read it.
    private readonly record struct Exchange(int Status, string StatusText, string Body,
        Dictionary<string, string> Headers, string? Error, bool Cancelled);

    private async Task<Exchange> ReadAsync(object? body, CancellationToken token)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var method = _method ?? "GET";
        var url = _url ?? "";
        try
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            foreach (var header in _requestHeaders)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (body != null && method is "POST" or "PUT" or "PATCH")
                request.Content = new StringContent(ToText(body));

            var response = await _httpClient.SendAsync(request, token);
            foreach (var header in response.Headers)
                headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
            foreach (var header in response.Content.Headers)
                headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);

            var text = await response.Content.ReadAsStringAsync(token);
            return new Exchange((int)response.StatusCode, response.ReasonPhrase ?? "", text, headers, null, false);
        }
        catch (OperationCanceledException)
        {
            return new Exchange(0, "", "", headers, null, true);
        }
        catch (Exception ex)
        {
            return new Exchange(0, "", "", headers, ex.Message, false);
        }
    }

    private void Finish(Exchange exchange)
    {
        // A request that was reopened while an answer was in flight must not be written by the answer
        // that came late, and one the page stopped has already reported its 'abort'.
        if (_terminated || _readyState == 0) return;
        _responseHeaders = exchange.Headers;
        _responseText = exchange.Body;
        if (exchange.Cancelled)
        {
            _readyState = 0;
            _sending = false;
            InvokeReadyStateChange();
            InvokeEvent("abort");
            InvokeEvent("loadend");
            return;
        }

        _status = exchange.Status;
        _statusText = exchange.StatusText;
        _readyState = 3;
        InvokeReadyStateChange();
        InvokeEvent("progress");

        _readyState = 4;
        _sending = false;
        InvokeReadyStateChange();
        InvokeEvent(exchange.Error == null ? "load" : "error");
        InvokeEvent("loadend");
    }

    /// <summary>§2.6.5: the request can be dropped at any time — before it was opened it does nothing,
    /// and an in-flight one stops and reports 'abort' inline, which is what the page that called it
    /// reads before its next statement (§2.6.5.1: the answer that arrives afterwards is dropped).</summary>
    public void abort()
    {
        if (_readyState == 0) return;
        _terminated = true;
        _cancellation?.Cancel();
        _requestHeaders.Clear();
        _readyState = 0;
        _sending = false;
        InvokeReadyStateChange();
        InvokeEvent("abort");
        InvokeEvent("loadend");
        // The flag stays raised until the page opens another request, so the answer the task is still
        // waiting for is dropped when it comes — it would otherwise report a second 'load' for a
        // transfer the page already stopped.
    }

    /// <summary>§2.6.7: the headers the answer carried, each on its own line with the name in lower case
    /// and the lines sorted by it. Nothing has arrived for a request that is not receiving yet, so the
    /// read answers the empty string rather than refusing (measured).</summary>
    public string getAllResponseHeaders()
    {
        if (_readyState < 2) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var header in _responseHeaders.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(header.Key.ToLowerInvariant()).Append(": ").Append(header.Value).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>§2.6.7: one header by name, or null when the answer did not carry it — and the read does
    /// not refuse before a response exists (measured).</summary>
    public string? getResponseHeader(string name)
        => _readyState >= 2 ? _responseHeaders.GetValueOrDefault(name.ToLowerInvariant()) : null;

    public void overrideMimeType(string mimeType) { }

    internal void SetResponseXml(object? document) => _responseXml = document;

    private static DOMException MustBeOpened(string api) => new(
        $"Failed to execute '{api}' on 'XMLHttpRequest': The object's state must be OPENED.",
        "InvalidStateError");

    private static bool IsFieldToken(string name)
    {
        foreach (var c in name)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') continue;
            if (c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^'
                or '_' or '`' or '|' or '~') continue;
            return false;
        }
        return true;
    }

    private void InvokeReadyStateChange() => InvokeEvent("readystatechange");

    /// <summary>The event goes out through the interface's own dispatch point, which runs the listener
    /// bag and the 'on' plus type member in JavaScript. A page that replaced the dispatch point with
    /// something that throws must not stop the state machine, so the call is allowed to fail quietly.</summary>
    private void InvokeEvent(string type)
        => _engine.IntegrationService?.CallGlobal("__acruxXhrEvent", this, type);

    public void Dispose()
    {
        _cancellation?.Dispose();
        _httpClient.Dispose();
    }
}

/// <summary>XMLHttpRequest §2.4.1: the upload half of a request. It is an event target that carries the
/// progress of the body being sent and its own 'withCredentials', which is the flag a page sets when
/// the upload — not the request — needs the credentials.</summary>
public class XMLHttpRequestUploadHost : IDomPrototypeHost
{
    public string? __domType => "XMLHttpRequestUpload";
    public string[] __domTypeChain => new[] { "EventTarget", "XMLHttpRequestUpload" };
    public override string ToString() => DomHostString.Tagged(this);

    public bool withCredentials { get; set; }

    public object? onreadystatechange;
    public object? onload;
    public object? onerror;
    public object? onprogress;
    public object? onabort;
    public object? ontimeout;
    public object? onloadstart;
    public object? onloadend;
}
