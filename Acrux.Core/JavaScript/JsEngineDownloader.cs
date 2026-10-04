using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Acrux.Core.JavaScript;

public static class JsEngineDownloader
{
    private static readonly HttpClient _http = new();

    private static string BaseDir => Path.Combine(AppPaths.AppDataDir, "engines");

    public static string EngineDir(JsEngineType type) =>
        Path.Combine(BaseDir, type.ToString().ToLowerInvariant());

    public static string? GetEngineAssemblyPath(JsEngineType type) => type switch
    {
        JsEngineType.V8 => Path.Combine(EngineDir(type), "JavaScriptEngineSwitcher.V8.dll"),
        JsEngineType.Jurassic => Path.Combine(EngineDir(type), "JavaScriptEngineSwitcher.Jurassic.dll"),
        _ => null
    };

    public static bool IsEngineDownloaded(JsEngineType type)
    {
        var path = GetEngineAssemblyPath(type);
        return path != null && File.Exists(path);
    }

    public static string GetEngineStatus(JsEngineType type)
    {
        if (type == JsEngineType.Jint) return "内置";
        if (IsEngineDownloaded(type)) return "已就绪";
        return "未下载";
    }

    public static async Task DownloadEngineAsync(JsEngineType type, IProgress<int>? progress = null)
    {
        var rid = GetRuntimeIdentifier();
        var engineName = type.ToString().ToLowerInvariant();
        var url = $"https://github.com/Acrux/js-engines/releases/download/v1.0/{engineName}-{rid}.zip";
        var destDir = EngineDir(type);
        Directory.CreateDirectory(destDir);

        var zipPath = Path.Combine(Path.GetTempPath(), $"acrux_{engineName}_{rid}.zip");

        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(zipPath);
            var buffer = new byte[81920];
            long readBytes = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                readBytes += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((int)(readBytes * 100 / totalBytes));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EngineDownload] Failed to download {type}: {ex.Message}");
            throw;
        }

        try
        {
            if (File.Exists(zipPath))
            {
                ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
                File.Delete(zipPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EngineDownload] Failed to extract {type}: {ex.Message}");
            throw;
        }
    }

    public static string GetRuntimeIdentifier()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };
        return $"{os}-{arch}";
    }
}
