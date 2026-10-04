#if WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Acrux.Core;

public static class WindowsFolderProvider
{
    private const int CSIDL_APPDATA = 0x001A;
    private const int CSIDL_LOCAL_APPDATA = 0x001C;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SHGetFolderPath(IntPtr hwndOwner, int nFolder, IntPtr hToken, uint dwFlags, StringBuilder lpszPath);

    public static string GetAppDataPath()
    {
        try
        {
            var sb = new StringBuilder(520);
            var hr = SHGetFolderPath(IntPtr.Zero, CSIDL_APPDATA, IntPtr.Zero, 0, sb);
            if (hr == 0)
                return sb.ToString();
        }
        catch { }

        var env = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(env))
            return env;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Application Data");
    }

    public static string GetLocalAppDataPath()
    {
        try
        {
            var sb = new StringBuilder(520);
            var hr = SHGetFolderPath(IntPtr.Zero, CSIDL_LOCAL_APPDATA, IntPtr.Zero, 0, sb);
            if (hr == 0)
                return sb.ToString();
        }
        catch { }

        var env = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(env))
            return env;

        return Path.Combine(GetAppDataPath(), "Local");
    }
}
#endif
