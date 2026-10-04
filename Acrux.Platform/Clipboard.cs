using System.Runtime.InteropServices;
using System.Text;

namespace Acrux.Platform;

public static class Clipboard
{
    public static void SetText(string text)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetTextWindows(text);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SetTextLinux(text);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            SetTextMac(text);
    }

    public static string? GetText()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetTextWindows();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetTextLinux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetTextMac();
        return null;
    }

    // Windows clipboard via Win32 API
    private static void SetTextWindows(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            IntPtr hMem = Marshal.StringToHGlobalUni(text);
            SetClipboardData(CF_UNICODETEXT, hMem);
        }
        finally { CloseClipboard(); }
    }

    private static string? GetTextWindows()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = GetClipboardData(CF_UNICODETEXT);
            return hData != IntPtr.Zero ? Marshal.PtrToStringUni(hData) : null;
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern bool SetClipboardData(uint uFormat, IntPtr data);
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    private const uint CF_UNICODETEXT = 13;

    // Linux clipboard via xclip or xsel (shell commands)
    private static void SetTextLinux(string text)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
                proc.WaitForExit(1000);
            }
        }
        catch
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xsel",
                    Arguments = "-ib",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    proc.StandardInput.Write(text);
                    proc.StandardInput.Close();
                    proc.WaitForExit(1000);
                }
            }
            catch { }
        }
    }

    private static string? GetTextLinux()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard -o",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var result = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);
                return result?.TrimEnd('\n', '\r');
            }
        }
        catch
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xsel",
                    Arguments = "-ob",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var result = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1000);
                    return result?.TrimEnd('\n', '\r');
                }
            }
            catch { }
        }
        return null;
    }

    // macOS clipboard via NSPasteboard (objc_msgSend) or pbpaste/pbcopy
    private static void SetTextMac(string text)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
                proc.WaitForExit(1000);
            }
        }
        catch { }
    }

    private static string? GetTextMac()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pbpaste",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var result = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);
                return result?.TrimEnd('\n', '\r');
            }
        }
        catch { }
        return null;
    }
}
