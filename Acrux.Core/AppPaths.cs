using System.IO;
using System.Runtime.InteropServices;

namespace Acrux.Core;

/// <summary>
/// Resolves the per-user application data directory. The Windows path stays on
/// the WinXP-safe <see cref="WindowsFolderProvider"/>; Linux/macOS use the
/// platform-standard config locations, since
/// <see cref="Environment.SpecialFolder.ApplicationData"/> returns an empty
/// string on some Linux distributions (no XDG_CONFIG_HOME set).
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// The per-user profile directory. Reading it adopts the profile the product had
    /// under its previous name (see <see cref="AdoptLegacyProfile"/>), so every consumer —
    /// settings, downloaded engines, the engine registry — sees one directory without
    /// having to know about the rename.
    /// </summary>
    public static string AppDataDir
    {
        get
        {
            var dir = GetAppDataDirectory("Acrux");
            AdoptLegacyProfile(dir);
            return dir;
        }
    }

    private static bool _legacyAdopted;
    private static readonly object _adoptLock = new();

    /// <summary>
    /// One-time adoption of <c>%APPDATA%\UpBrowser</c> after the product was renamed.
    ///
    /// Without this the rename silently orphans the user's persisted state: the app reads
    /// an empty <c>%APPDATA%\Acrux</c>, falls back to every default — which is how a
    /// profile that had deliberately selected multi-process tabs came back to the
    /// single-process default — and re-downloads the JS engines it already had.
    ///
    /// Nothing is ever overwritten: a fresh profile that already has files wins over the
    /// legacy one, because those were written after the rename and may hold settings the
    /// user just changed. Entries the new profile does not have are moved, not copied, so
    /// the several-hundred-megabyte engine download is not duplicated on a disk that is
    /// already tight. The legacy directory itself is left in place (empty, or holding
    /// only the entries we refused to clobber) so a mistaken run stays recoverable.
    /// </summary>
    private static void AdoptLegacyProfile(string newDir)
    {
        lock (_adoptLock)
        {
            if (_legacyAdopted) return;
            _legacyAdopted = true;   // once per process, whatever happens

            try
            {
                var legacy = GetAppDataDirectory("UpBrowser");
                if (legacy == newDir || !Directory.Exists(legacy)) return;

                if (!Directory.Exists(newDir))
                {
                    Directory.Move(legacy, newDir);
                    Console.WriteLine($"[AppPaths] profile moved to the product's new name: {newDir}");
                    return;
                }

                Directory.CreateDirectory(newDir);
                int adopted = 0;
                foreach (var file in Directory.GetFiles(legacy))
                {
                    var target = Path.Combine(newDir, Path.GetFileName(file));
                    if (File.Exists(target)) continue;
                    File.Move(file, target);
                    adopted++;
                }
                foreach (var dir in Directory.GetDirectories(legacy))
                {
                    var target = Path.Combine(newDir, Path.GetFileName(dir));
                    if (Directory.Exists(target)) continue;
                    Directory.Move(dir, target);
                    adopted++;
                }
                if (adopted > 0)
                    Console.WriteLine($"[AppPaths] adopted {adopted} entr(y|ies) from the pre-rename profile at {legacy}");
            }
            catch (Exception ex)
            {
                // A profile that could not be adopted must not stop the browser from
                // starting: the defaults are still a working configuration.
                Console.WriteLine($"[AppPaths] legacy profile adoption skipped: {ex.Message}");
            }
        }
    }


    public static string GetAppDataDirectory(string appName)
    {
        string root;
#if WINDOWS
        root = WindowsFolderProvider.GetAppDataPath();
#else
        root = GetNonWindowsConfigRoot();
#endif
        return string.IsNullOrEmpty(root)
            ? Path.Combine(Path.GetTempPath(), appName)
            : Path.Combine(root, appName);
    }

    private static string GetNonWindowsConfigRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? FallbackRoot() : Path.Combine(home, "Library", "Application Support");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return xdg;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            return appData;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(userProfile) ? FallbackRoot() : Path.Combine(userProfile, ".config");
    }

    private static string FallbackRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local) ? Path.GetTempPath() : local;
    }
}
