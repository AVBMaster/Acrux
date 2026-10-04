using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acrux.Core;

namespace Acrux.Rendering;

public static class RenderingSettingsConfig
{
    private static string GetConfigPath()
    {
        var dir = AppPaths.AppDataDir;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    internal class ConfigData
    {
        public PerformancePreset Preset { get; set; } = PerformancePreset.Balanced;
        public bool GpuAcceleration { get; set; } = true;
        public bool VSync { get; set; }
        public int TargetFps { get; set; } = 60;
        public AntiAliasMode AntiAliasing { get; set; } = AntiAliasMode.High;
        public bool DirtyRegions { get; set; } = true;
        public bool PictureCaching { get; set; } = true;
        public bool SmoothScrolling { get; set; } = true;
        public float ResolutionScale { get; set; } = 1.0f;
        public bool ShowFps { get; set; }
        public string JsEngine { get; set; } = "Jint";
        public bool UseCustomHtmlParser { get; set; } = true;
        public string TabMode { get; set; } = RenderingSettings.DefaultTabMode;
    }

    public static void Save(RenderingSettings settings)
    {
        var data = new ConfigData
        {
            Preset = settings.Preset,
            GpuAcceleration = settings.GpuAcceleration,
            VSync = settings.VSync,
            TargetFps = settings.TargetFps,
            AntiAliasing = settings.AntiAliasing,
            DirtyRegions = settings.DirtyRegions,
            PictureCaching = settings.PictureCaching,
            SmoothScrolling = settings.SmoothScrolling,
            ResolutionScale = settings.ResolutionScale,
            ShowFps = settings.ShowFps,
            JsEngine = settings.JsEngine,
            UseCustomHtmlParser = settings.UseCustomHtmlParser,
            TabMode = settings.TabMode
        };

        var ctx = new RenderingSettingsJsonContext(new JsonSerializerOptions { WriteIndented = true });
        var json = JsonSerializer.Serialize(data, ctx.ConfigData);
        File.WriteAllText(GetConfigPath(), json);
    }

    public static void Load(RenderingSettings settings)
    {
        var path = GetConfigPath();
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var ctx = new RenderingSettingsJsonContext();
            var data = JsonSerializer.Deserialize(json, ctx.ConfigData);
            if (data == null) return;

            settings.Preset = data.Preset;
            settings.GpuAcceleration = data.GpuAcceleration;
            settings.VSync = data.VSync;
            settings.TargetFps = data.TargetFps;
            settings.AntiAliasing = data.AntiAliasing;
            settings.DirtyRegions = data.DirtyRegions;
            settings.PictureCaching = data.PictureCaching;
            settings.SmoothScrolling = data.SmoothScrolling;
            settings.ResolutionScale = data.ResolutionScale;
            settings.ShowFps = data.ShowFps;
            if (!string.IsNullOrEmpty(data.JsEngine))
                settings.JsEngine = data.JsEngine;
            settings.UseCustomHtmlParser = data.UseCustomHtmlParser;
            if (!string.IsNullOrEmpty(data.TabMode))
                settings.TabMode = data.TabMode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to load settings: {ex.Message}");
        }
    }
}
