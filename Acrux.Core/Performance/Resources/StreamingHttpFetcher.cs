using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Acrux.Core.Performance;

namespace Acrux.Core.Performance.Resources;

/// <summary>
/// Discovered resource and its download priority. Mirrors the "ResourceLoadPriority"
/// concept in Chromium's resource loader.
/// </summary>
public enum ResourcePriority : byte
{
    /// <summary>Critical path: HTML, blocking CSS, blocking JS. Cannot be reprioritised lower.</summary>
    VeryHigh = 0,
    High = 1,        // First-viewport images, fonts
    Medium = 2,      // Below-the-fold images
    Low = 3,         // Preload hints
    VeryLow = 4,     // Prefetch, telemetry
}

public enum ResourceKind : byte
{
    Document,
    Stylesheet,
    Script,
    Image,
    Font,
    Media,
    XHR,
    Other
}

public sealed class ResourceRequest
{
    public required string Url { get; init; }
    public required ResourceKind Kind { get; init; }
    public ResourcePriority Priority { get; set; } = ResourcePriority.Medium;
    public string? Referrer { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public int MaxRedirects { get; init; } = 10;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class ResourceResponse
{
    public int StatusCode { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public byte[] Body { get; init; } = Array.Empty<byte>();
    public string? ContentType { get; init; }
    public long ContentLength { get; init; }
    public bool FromCache { get; init; }
    public TimeSpan Duration { get; init; }
    public string? FinalUrl { get; init; }
}

/// <summary>
/// Streaming HTTP fetcher. Supports cancellation, priority queuing, and on-the-fly
/// consumption of response chunks via <see cref="IAsyncEnumerable{T}"/>. Designed for
/// the browser's HTML parser to begin work before the body is fully downloaded.
/// </summary>
public sealed class StreamingHttpFetcher
{
    private readonly HttpClient _client;
    private readonly ResourceCache _cache;
    private readonly PriorityResourceQueue _queue;
    private readonly ConcurrentDictionary<string, Task<ResourceResponse>> _inflight = new();

    public PriorityResourceQueue Queue => _queue;
    public ResourceCache Cache => _cache;

    public StreamingHttpFetcher(HttpClient? client = null, ResourceCache? cache = null, PriorityResourceQueue? queue = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _cache = cache ?? new ResourceCache();
        _queue = queue ?? new PriorityResourceQueue();
    }

    public Task<ResourceResponse> FetchAsync(ResourceRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (_cache.TryGet(request.Url, out var cached))
            return Task.FromResult(cached!);

        return _inflight.GetOrAdd(request.Url, _ => FetchCoreAsync(request, cancellationToken));
    }

    public async IAsyncEnumerable<byte[]> FetchStreamingAsync(ResourceRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(request.Timeout);

        using var httpRequest = BuildHttpRequest(request);
        using var response = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false)) > 0)
            {
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                yield return chunk;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            PipelineTimings.NetworkWait.AddSample(sw.ElapsedTicks * 1_000_000L / System.Diagnostics.Stopwatch.Frequency);
        }
    }

    private async Task<ResourceResponse> FetchCoreAsync(ResourceRequest request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(request.Timeout);

            using var httpRequest = BuildHttpRequest(request);
            using var response = await _client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            var body = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            var result = new ResourceResponse
            {
                StatusCode = (int)response.StatusCode,
                Headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                Body = body,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                ContentLength = body.LongLength,
                Duration = sw.Elapsed,
                FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? request.Url,
            };
            _cache.Put(request.Url, result);
            return result;
        }
        finally
        {
            _inflight.TryRemove(request.Url, out _);
            PipelineTimings.NetworkWait.AddSample(sw.ElapsedTicks * 1_000_000L / System.Diagnostics.Stopwatch.Frequency);
        }
    }

    private static HttpRequestMessage BuildHttpRequest(ResourceRequest request)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, request.Url);
        foreach (var (k, v) in request.Headers) msg.Headers.TryAddWithoutValidation(k, v);
        if (!string.IsNullOrEmpty(request.Referrer)) msg.Headers.Referrer = new Uri(request.Referrer);
        msg.Headers.Accept.Clear();
        msg.Headers.Accept.Add(request.Kind switch
        {
            ResourceKind.Stylesheet => new MediaTypeWithQualityHeaderValue("text/css"),
            ResourceKind.Script => new MediaTypeWithQualityHeaderValue("application/javascript"),
            ResourceKind.Image => new MediaTypeWithQualityHeaderValue("image/*"),
            ResourceKind.Font => new MediaTypeWithQualityHeaderValue("font/*"),
            ResourceKind.Media => new MediaTypeWithQualityHeaderValue("video/*"),
            ResourceKind.Document => new MediaTypeWithQualityHeaderValue("text/html"),
            _ => new MediaTypeWithQualityHeaderValue("*/*"),
        });
        return msg;
    }
}
