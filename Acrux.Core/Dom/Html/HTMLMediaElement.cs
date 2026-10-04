namespace Acrux.Core.Dom.Html;

public enum CanPlayTypeResult { Empty, Maybe, Probably }
public enum MediaNetworkState { Empty, Idle, Loading, NoSource }
public enum MediaReadyState { HaveNothing, HaveMetadata, HaveCurrentData, HaveFutureData, HaveEnoughData }

public class HTMLMediaElement : HtmlElement
{
    public HTMLMediaElement(string? name = null)
        : base(name ?? "media") { }

    public HTMLMediaElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "media") { }

    public string? Src { get => GetAttribute("src"); set => SetAttribute("src", value); }
    public MediaProvider? SrcObject { get; set; }
    public string? CrossOrigin { get => GetAttribute("crossorigin"); set => SetAttribute("crossorigin", value); }
    public string? CurrentSrc => Src;
    public ulong? CurrentTime { get; set; }
    public ulong Duration { get; set; }
    public bool Ended { get; set; }
    public bool Autoplay { get => HasAttribute("autoplay"); set => SetBoolAttr("autoplay", value); }
    public bool Loop { get => HasAttribute("loop"); set => SetBoolAttr("loop", value); }
    public bool Controls { get => HasAttribute("controls"); set => SetBoolAttr("controls", value); }
    public bool DefaultMuted { get => HasAttribute("muted"); set => SetBoolAttr("muted", value); }
    public bool Muted { get; set; }
    public double DefaultPlaybackRate { get; set; } = 1.0;
    public double PlaybackRate { get; set; } = 1.0;
    public double Volume { get; set; } = 1.0;
    public bool PreservesPitch { get; set; } = true;
    public bool PlaysInline { get => HasAttribute("playsinline"); set => SetBoolAttr("playsinline", value); }
    public string? Preload { get => GetAttribute("preload") ?? "metadata"; set => SetAttribute("preload", value); }
    public MediaNetworkState NetworkState { get; set; }
    public MediaReadyState ReadyState { get; set; }
    public bool Seeking { get; set; }
    public bool Paused { get; set; } = true;
    public double? Seekable { get; set; }
    public double? Played { get; set; }
    public double? Buffered { get; set; }
    public TimeRanges BufferedRanges { get; } = new();
    public AudioTrackList? AudioTracks { get; set; }
    public VideoTrackList? VideoTracks { get; set; }
    public TextTrackList? TextTracks { get; set; }
    public TextTrack? CaptioningPreferences { get; set; }
    public bool DisableRemotePlayback { get; set; }

    public CanPlayTypeResult CanPlayType(string type) => CanPlayTypeResult.Empty;

    public async Task Play()
    {
        Paused = false;
        var evt = new Event("play");
        DispatchEvent(evt);
    }

    public void Pause()
    {
        Paused = true;
        var evt = new Event("pause");
        DispatchEvent(evt);
    }

    public void Load()
    {
        var evt = new Event("loadstart");
        DispatchEvent(evt);
    }

    public TextTrack AddTextTrack(string kind, string? label = null, string? language = null) => new();
    public MediaController? Controller { get; set; }

    public void CaptureStream() { }
    public void SetSinkId(string sinkId) { }

    private void SetBoolAttr(string name, bool value)
    {
        if (value) SetAttribute(name, "");
        else RemoveAttribute(name);
    }
}

public class HTMLVideoElement : HTMLMediaElement
{
    public HTMLVideoElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "video") { }

    public ulong Width { get => ulong.TryParse(GetAttribute("width"), out var v) ? v : 0; set => SetAttribute("width", value.ToString()); }
    public ulong Height { get => ulong.TryParse(GetAttribute("height"), out var v) ? v : 0; set => SetAttribute("height", value.ToString()); }
    public ulong VideoWidth { get; set; }
    public ulong VideoHeight { get; set; }
    public string? Poster { get => GetAttribute("poster"); set => SetAttribute("poster", value); }
    public bool UsesWirelessDisplay { get; set; }
    public bool DisablePictureInPicture { get; set; }
    public bool DisableRemotePlayback { get; set; }

    public void CancelVideoFrameCallback() { }
    public ulong RequestVideoFrameCallback() => 0;

    public Task RequestPictureInPicture() => Task.CompletedTask;
}

public class HTMLAudioElement : HTMLMediaElement
{
    public HTMLAudioElement(Document document, string? name = null, string? namespaceUri = null)
        : base(name ?? "audio") { }
}

public class MediaProvider { }

public class TimeRanges
{
    public int Length => 0;
    public double Start(int index) => 0;
    public double End(int index) => 0;
}

public class AudioTrackList { }
public class VideoTrackList { }
public class TextTrackList { }
public class TextTrack
{
    public string Kind { get; set; } = "subtitles";
    public string? Label { get; set; }
    public string? Language { get; set; }
}
public class MediaController { }


