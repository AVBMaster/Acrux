namespace Acrux.Core.Dom.Html;

public static class FullscreenExtensions
{
    public static async Task RequestFullscreen(this Element element, FullscreenOptions? options = null)
    {
        element.DispatchEvent(new Event("fullscreenchange", new EventInit { Bubbles = true }));
    }

    public static async Task ExitFullscreen(this Document document)
    {
        document.DispatchEvent(new Event("fullscreenchange", new EventInit { Bubbles = true }));
    }

    public static Element? GetFullscreenElement(this Document document) => null;

    public static bool FullscreenEnabled(this Document document) => true;
}

public class FullscreenOptions
{
    public NavigationUI NavigationUI { get; set; } = NavigationUI.Auto;
}

public enum NavigationUI { Auto, Show, Hide }

public static class PointerLockExtensions
{
    public static void RequestPointerLock(this Element element) { }
    public static void ExitPointerLock(this Document document) { }
    public static Element? GetPointerLockElement(this Document document) => null;
}

public class ScreenOrientation
{
    public string Type { get; set; } = "landscape-primary";
    public int Angle { get; set; }

    public async Task Lock(string orientation) { }
    public void Unlock() { }

    public event Action? OnChange;
}

public class Navigator
{
    public string UserAgent { get; set; } = "Acrux/1.0";
    public bool CookieEnabled { get; set; } = true;
    public bool OnLine { get; set; } = true;
    public string Platform { get; set; } = "Win32";
    public string Language { get; set; } = "en-US";
    public string[] Languages { get; set; } = new[] { "en-US" };
    public string AppName { get; set; } = "Acrux";
    public string AppVersion { get; set; } = "1.0";
    public string AppCodeName { get; set; } = "Mozilla";
    public string Product { get; set; } = "Gecko";
    public string ProductSub { get; set; } = "20100101";
    public string Vendor { get; set; } = "";
    public string VendorSub { get; set; } = "";
    public string BuildID { get; set; } = "";
    public string Oscpu { get; set; } = "Windows NT 10.0";
    public bool JavaEnabled { get; set; }
    public bool DoNotTrack { get; set; }
    public int MaxTouchPoints { get; set; }
    public bool HardwareConcurrency { get; set; }
    public int DeviceMemory { get; set; } = 8;
    public Geolocation? Geolocation { get; set; }
    public MediaDevices? MediaDevices { get; set; }
    public StorageManager? Storage { get; set; }
    public ServiceWorkerContainer? ServiceWorker { get; set; }
    public Permissions? Permissions { get; set; }
    public CredentialsContainer? Credentials { get; set; }
    public NetworkInformation? Connection { get; set; }
    public BatteryManager? Battery { get; set; }
    public Clipboard? Clipboard { get; set; }
    public MediaSession? MediaSession { get; set; }
    public ShareData? CanShare { get; set; }
    public Serial? Serial { get; set; }
    public USB? Usb { get; set; }
    public Bluetooth? Bluetooth { get; set; }
    public NFC? Nfc { get; set; }
    public WakeLock? WakeLock { get; set; }

    public async Task<Clipboard> GetClipboard() => Clipboard ??= new Clipboard();
    public async Task<MediaStream> GetUserMedia(MediaStreamConstraints constraints) => new();
    public async Task<MediaStream> GetDisplayMedia(MediaStreamConstraints constraints) => new();
    public async Task Share(ShareData data) { }
    public async Task Vibrate(params int[] pattern) { }
    public string SendBeacon(string url, object? data = null) => "";
    public void RegisterProtocolHandler(string scheme, string url, string title) { }
    public void UnregisterProtocolHandler(string scheme, string url) { }
    public async Task<StandaloneMediaStream> GetGamepads() => new();
}

public class Geolocation
{
    public void GetCurrentPosition(GeolocationCallback callback, PositionErrorCallback? errorCallback = null, PositionOptions? options = null) { }
    public int WatchPosition(GeolocationCallback callback, PositionErrorCallback? errorCallback = null, PositionOptions? options = null) => 0;
    public void ClearWatch(int id) { }
}

public class GeolocationCallback { }
public class PositionErrorCallback { }
public class PositionOptions { }
public class MediaDevices
{
    public event Action<MediaStream>? OnDeviceChange;
    public async Task<MediaDeviceInfo[]> EnumerateDevices() => Array.Empty<MediaDeviceInfo>();
}

public class MediaDeviceInfo { }
public class StorageManager
{
    public async Task<StorageEstimate> Estimate() => new();
    public async Task<bool> Persist() => false;
    public async Task<bool> Persisted() => false;
}

public class StorageEstimate
{
    public ulong Quota { get; set; }
    public ulong Usage { get; set; }
}

public class ServiceWorkerContainer { }
public class Permissions { }
public class CredentialsContainer { }
public class NetworkInformation { }
public class BatteryManager { }
public class Clipboard
{
    public async Task<string> ReadText() => "";
    public async Task WriteText(string text) { }
}
public class MediaSession { }
public class ShareData
{
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Url { get; set; }
    public DomFile[]? Files { get; set; }
}
public class Serial { }
public class USB { }
public class Bluetooth { }
public class NFC { }
public class WakeLock
{
    public async Task<WakeLockSentinel> Request(string type = "screen") => new();
}

public class WakeLockSentinel
{
    public bool Released { get; set; }
    public string Type { get; set; } = "screen";
    public async Task Release() { Released = true; }
    public event Action? OnRelease;
}

public class MediaStream
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Active { get; set; } = true;
    public MediaStreamTrack[] GetTracks() => Array.Empty<MediaStreamTrack>();
    public MediaStreamTrack[] GetVideoTracks() => Array.Empty<MediaStreamTrack>();
    public MediaStreamTrack[] GetAudioTracks() => Array.Empty<MediaStreamTrack>();
    public MediaStreamTrack? GetTrackById(string id) => null;
    public void AddTrack(MediaStreamTrack track) { }
    public void RemoveTrack(MediaStreamTrack track) { }
    public MediaStream Clone() => new();
}

public class MediaStreamTrack
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Muted { get; set; }
    public MediaStreamTrackState ReadyState { get; set; } = MediaStreamTrackState.Live;
    public event Action? OnEnded;
    public event Action? OnMute;
    public event Action? OnUnmute;

    public MediaStreamTrack Clone() => new();
    public void Stop()
    {
        ReadyState = MediaStreamTrackState.Ended;
        OnEnded?.Invoke();
    }
    public void ApplyConstraints(MediaTrackConstraints? constraints = null) { }
}

public enum MediaStreamTrackState { Live, Ended }

public class MediaTrackConstraints { }
public class MediaStreamConstraints { }
public class StandaloneMediaStream { }
public class ServiceWorker { }
public class ServiceWorkerRegistration { }


