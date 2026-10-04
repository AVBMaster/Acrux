using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Rendering;

public class MediaEngine : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private string? _sourceUrl;
    private bool _isPlaying;
    private float _currentTime;
    private float _duration;
    private float _volume = 1.0f;
    private bool _muted;
    private Task? _playbackTask;
    private CancellationTokenSource? _cts;
    private SKImage? _currentFrame;
    private object _frameLock = new();
    private bool _loop;
    private float _playbackRate = 1.0f;
    private bool _hasMetadata;
    private bool _ended;

    public string? SourceUrl => _sourceUrl;
    public bool IsPlaying => _isPlaying;
    public float CurrentTime => _currentTime;
    public float Duration => _duration;
    public bool Ended => _ended;
    public bool HasMetadata => _hasMetadata;

    public event Action<float>? TimeUpdated;
    public event Action? PlaybackStarted;
    public event Action? PlaybackPaused;
    public event Action? PlaybackEnded;
    public event Action? MetadataLoaded;
    public event Action? ErrorOccurred;
    public event Action<SKImage?>? FrameUpdated;

    public async Task LoadAsync(string url, CancellationToken ct = default)
    {
        _sourceUrl = url;
        _hasMetadata = false;
        _ended = false;
        _currentTime = 0;
        _duration = 0;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!headResponse.IsSuccessStatusCode)
            {
                ErrorOccurred?.Invoke();
                return;
            }
            var contentLength = headResponse.Content.Headers.ContentLength;
            var contentType = headResponse.Content.Headers.ContentType?.MediaType;
            _duration = EstimateDurationFromContent(contentLength, contentType);
            _hasMetadata = true;
            MetadataLoaded?.Invoke();
        }
        catch
        {
            _hasMetadata = true;
            _duration = 120;
            MetadataLoaded?.Invoke();
        }
    }

    public void Play()
    {
        if (_isPlaying) return;
        if (_ended)
        {
            _currentTime = 0;
            _ended = false;
        }
        _isPlaying = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _playbackTask = Task.Run(async () =>
        {
            var lastFrameTime = Environment.TickCount64;
            float lastReportedTime = _currentTime;

            while (!token.IsCancellationRequested && _isPlaying)
            {
                var now = Environment.TickCount64;
                float elapsedSec = (now - lastFrameTime) / 1000f * _playbackRate;
                lastFrameTime = now;

                lock (_frameLock)
                {
                    _currentTime += elapsedSec;
                    if (_currentTime >= _duration)
                    {
                        if (_loop)
                        {
                            _currentTime = 0;
                        }
                        else
                        {
                            _currentTime = _duration;
                            _isPlaying = false;
                            _ended = true;
                            PlaybackEnded?.Invoke();
                            break;
                        }
                    }
                }

                if (MathF.Abs(_currentTime - lastReportedTime) >= 0.25f)
                {
                    lastReportedTime = _currentTime;
                    TimeUpdated?.Invoke(_currentTime);
                }

                try { await Task.Delay(50, token); }
                catch (TaskCanceledException) { break; }
            }
        }, token);

        PlaybackStarted?.Invoke();
    }

    public void Pause()
    {
        _isPlaying = false;
        _cts?.Cancel();
        PlaybackPaused?.Invoke();
    }

    public void Seek(float time)
    {
        lock (_frameLock)
        {
            _currentTime = Math.Clamp(time, 0, _duration);
            _ended = false;
        }
        TimeUpdated?.Invoke(_currentTime);
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
    }

    public void SetLoop(bool loop) => _loop = loop;

    public void SetPlaybackRate(float rate) => _playbackRate = rate;

    public SKImage? GetCurrentFrame()
    {
        lock (_frameLock) { return _currentFrame; }
    }

    public string? CanPlayType(string type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        var lower = type.ToLowerInvariant();
        if (lower.Contains("mp4") || lower.Contains("webm") || lower.Contains("ogg") ||
            lower.Contains("mp3") || lower.Contains("wav") || lower.Contains("aac") ||
            lower.Contains("flac") || lower.Contains("opus") || lower.Contains("m4a"))
            return "probably";
        if (lower.Contains("video") || lower.Contains("audio"))
            return "maybe";
        return null;
    }

    private static float EstimateDurationFromContent(long? contentLength, string? contentType)
    {
        if (contentLength == null) return 120;
        var lower = contentType?.ToLowerInvariant() ?? "";
        if (lower.Contains("audio/mpeg") || lower.Contains("audio/mp3"))
            return contentLength.Value / 16000f;
        if (lower.Contains("audio/ogg") || lower.Contains("audio/flac"))
            return contentLength.Value / 8000f;
        if (lower.Contains("video/"))
            return contentLength.Value / 50000f;
        return 120;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _httpClient.Dispose();
        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }
    }
}
