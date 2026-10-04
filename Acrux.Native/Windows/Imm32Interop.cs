using System.Runtime.InteropServices;

namespace Acrux.Native.Windows;

public static class Imm32Interop
{
    private const string ImmDll = "imm32.dll";
    private const string UserDll = "user32.dll";

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern int ImmGetCompositionString(IntPtr hIMC, int dwIndex, byte[]? lpBuf, int dwBufLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetCompositionString(IntPtr hIMC, int dwIndex, byte[]? lpComp, int dwCompLen, byte[]? lpRead, int dwReadLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmAssociateContextEx(IntPtr hWnd, IntPtr hIMC, int dwFlags);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmGetConversionStatus(IntPtr hIMC, out int lpdwConversion, out int lpdwSentence);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetConversionStatus(IntPtr hIMC, int dwConversion, int dwSentence);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmIsIME(IntPtr hKL);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmLockIMC(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmUnlockIMC(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern int ImmGetIMCLockCount(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetIMCWndList(IntPtr hIMC, IntPtr pWnd1, IntPtr pWnd2, uint uFlags);

    public const int IME_CMODE_NATIVE = 0x0001;
    public const int IME_CMODE_FULLSHAPE = 0x0008;
    public const int IME_CMODE_CHARCODE = 0x0020;
    public const int IME_CMODE_HANJACONVERT = 0x0040;
    public const int IME_CMODE_ROMAN = 0x0010;
    public const int IME_CMODE_EUDC = 0x0100;
    public const int IME_CMODE_SYMBOL = 0x0400;

    public const int IME_SMODE_NONE = 0x0000;
    public const int IME_SMODE_PHRASE = 0x0001;
    public const int IME_SMODE_SINGLECONVERT = 0x0002;
    public const int IME_SMODE_AUTOMATIC = 0x0004;
    public const int IME_SMODE_PLAURALCLAUSE = 0x0008;
    public const int IME_SMODE_CONVERSATION = 0x0010;

    public const int GCS_COMPSTR = 0x0008;
    public const int GCS_CURSORPOS = 0x0010;
    public const int GCS_RESULTSTR = 0x0800;
    public const int GCS_COMPATTR = 0x0010;
    public const int GCS_COMPCLAUSE = 0x0020;
    public const int GCS_READINGSTRING = 0x0400;

    public const int WM_IME_COMPOSITION = 0x010F;
    public const int WM_IME_STARTCOMPOSITION = 0x010D;
    public const int WM_IME_ENDCOMPOSITION = 0x010E;
    public const int WM_IME_NOTIFY = 0x0282;
    public const int WM_IME_CHAR = 0x0286;
    public const int WM_IME_CONTROL = 0x0283;
    public const int WM_IME_SELECT = 0x0285;
    public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const int WM_INPUTLANGCHANGE = 0x0051;

    public const int IMN_OPENCANDIDATE = 0x0001;
    public const int IMN_CHANGECANDIDATE = 0x0002;
    public const int IMN_CLOSECANDIDATE = 0x0003;
    public const int IMN_SETCONVERSIONMODE = 0x0004;
    public const int IMN_SETOPENSTATUS = 0x0005;
    public const int IMN_SETCANDIDATEPOS = 0x0006;
    public const int IMN_SETCOMPOSITIONFONT = 0x0007;
    public const int IMN_SETCOMPOSITIONWINDOW = 0x0008;

    [DllImport(ImmDll)]
    public static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM lpCompForm);

    [DllImport(ImmDll)]
    public static extern bool ImmSetCandidateWindow(IntPtr hIMC, ref CANDIDATEFORM lpCandidate);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern int ImmGetCandidateCount(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmNotifyIME(IntPtr hIMC, int dwAction, int dwIndex, int dwValue);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern int ImmGetCandidateList(IntPtr hIMC, int dwIndex, IntPtr lpCandidateList, int dwBufLen);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ImmGetCompositionFont(IntPtr hIMC);

    [DllImport(ImmDll, CharSet = CharSet.Unicode)]
    public static extern bool ImmSetCompositionFont(IntPtr hIMC, IntPtr lpLogFont);

    public const int NI_COMPOSITIONSTR = 0x0001;
    public const int NI_OPENCANDIDATE = 0x0002;
    public const int NI_CLOSECANDIDATE = 0x0003;
    public const int NI_SELECTCANDIDATE = 0x0004;
    public const int NI_CHANGECANDIDATE = 0x0005;
    public const int NI_FINDCANDIDATE = 0x0006;

    public const int CPS_COMPLETE = 0x0004;
    public const int CPS_CONVERT = 0x0002;
    public const int CPS_REVERT = 0x0001;
    public const int CPS_CANCEL = 0x0008;

    public const int CFS_DEFAULT = 0x0000;
    public const int CFS_FORCE_POSITION = 0x0020;
    public const int CFS_POINT = 0x0002;
    public const int CFS_RECT = 0x0001;
    public const int CFS_CANDIDATEPOS = 0x0040;
    public const int CFS_EXCLUDE = 0x0080;

    [DllImport(UserDll)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport(UserDll)]
    public static extern IntPtr GetFocus();

    [DllImport(UserDll)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport(UserDll)]
    public static extern IntPtr GetActiveWindow();

    [DllImport(UserDll)]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport(UserDll)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport(UserDll)]
    public static extern IntPtr GetKeyboardLayout(uint dwLayout);

    [DllImport(UserDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr ActivateKeyboardLayout(IntPtr hKL, uint Flags);

    [DllImport(UserDll, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport(UserDll, CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport(UserDll)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport(UserDll)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport(UserDll)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport(UserDll, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport(UserDll)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport(UserDll)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport(UserDll)]
    public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport(UserDll)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint KLF_ACTIVATE = 0x00000001;
    public const uint KLF_SUBSTITUTE_OK = 0x00000002;
    public const uint KLF_REORDER = 0x00000008;
    public const uint KLF_SETFORPROCESS = 0x00000100;

    public const uint INPUTLANGCHANGE_FORWARD = 0x00000001;
    public const uint INPUTLANGCHANGE_SYSCHARSET = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct COMPOSITIONFORM
    {
        public uint dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CANDIDATEFORM
    {
        public uint dwIndex;
        public uint dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CANDIDATELIST
    {
        public uint dwSize;
        public uint dwStyle;
        public uint dwCount;
        public uint dwSelection;
        public uint dwPageStart;
        public uint dwPageSize;
        public char[] wszCandidate;
    }

    public const int SC_MAXIMIZE = 0xF030;
    public const int SC_MINIMIZE = 0xF020;
    public const int SC_RESTORE = 0xF120;
}