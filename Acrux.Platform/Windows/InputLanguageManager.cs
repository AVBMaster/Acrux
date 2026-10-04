using System.Runtime.InteropServices;
using Acrux.Native.Windows;

namespace Acrux.Platform.Windows;

public class InputLanguageManager
{
    public static IntPtr GetCurrentKeyboardLayout()
    {
        return Imm32Interop.GetKeyboardLayout(0);
    }

    public static IntPtr GetForegroundWindowKeyboardLayout()
    {
        IntPtr hwnd = Imm32Interop.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        uint threadId = Imm32Interop.GetWindowThreadProcessId(hwnd, out _);
        return Imm32Interop.GetKeyboardLayout(threadId);
    }

    public static bool SwitchToChineseSimplified()
    {
        return ActivateKeyboardLayout("00000804");
    }

    public static bool SwitchToChineseTraditional()
    {
        return ActivateKeyboardLayout("00000404");
    }

    public static bool SwitchToEnglish()
    {
        return ActivateKeyboardLayout("00000409");
    }

    public static bool SwitchToJapanese()
    {
        return ActivateKeyboardLayout("00000411");
    }

    public static bool SwitchToKorean()
    {
        return ActivateKeyboardLayout("00000412");
    }

    public static bool ActivateKeyboardLayout(string klid)
    {
        IntPtr hkl = Imm32Interop.LoadKeyboardLayout(klid, Imm32Interop.KLF_ACTIVATE | Imm32Interop.KLF_REORDER);
        return hkl != IntPtr.Zero;
    }

    public static bool ActivateKeyboardLayoutForProcess(string klid)
    {
        IntPtr hkl = Imm32Interop.LoadKeyboardLayout(klid, Imm32Interop.KLF_ACTIVATE | Imm32Interop.KLF_SETFORPROCESS);
        return hkl != IntPtr.Zero;
    }

    public static bool IsImeEnabled(IntPtr hKL)
    {
        return Imm32Interop.ImmIsIME(hKL);
    }

    public static string GetLanguageName(IntPtr hKL)
    {
        uint langId = (uint)hKL.ToInt64() & 0xFFFF;
        return langId switch
        {
            0x0409 => "English",
            0x0804 => "Chinese (Simplified)",
            0x0404 => "Chinese (Traditional)",
            0x0411 => "Japanese",
            0x0412 => "Korean",
            0x0410 => "Italian",
            0x0407 => "German",
            0x040C => "French",
            0x0416 => "Portuguese (Brazil)",
            0x0419 => "Russian",
            _ => $"Language 0x{langId:X4}"
        };
    }

    public static void RequestLanguageChange(IntPtr hwnd, IntPtr hKL)
    {
        if (hwnd != IntPtr.Zero)
        {
            Imm32Interop.SendMessage(hwnd, Imm32Interop.WM_INPUTLANGCHANGEREQUEST, (IntPtr)1, hKL);
        }
    }
}

public class ImeStateMonitor
{
    private readonly IntPtr _hwnd;
    private bool _wasComposing;
    private bool _isComposing;

    public event Action<string>? OnCompositionStarted;
    public event Action<string, string>? OnCompositionChanged;
    public event Action<string>? OnCompositionEnded;
    public event Action<string>? OnCommittedText;
    public event Action? OnImeStateChanged;

    public ImeStateMonitor(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public void ProcessMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handler = new WindowsImeHandler(_hwnd);

        switch (msg)
        {
            case NativeWindow.WM_IME_STARTCOMPOSITION:
                _wasComposing = false;
                _isComposing = true;
                var startState = handler.GetCompositionState();
                OnCompositionStarted?.Invoke(startState.CompositionText);
                break;

            case NativeWindow.WM_IME_COMPOSITION:
                var compState = handler.GetCompositionState();
                if (_wasComposing && _isComposing)
                {
                    OnCompositionChanged?.Invoke(compState.CompositionText, compState.ReadingString);
                }
                else if (!_wasComposing && _isComposing)
                {
                    _wasComposing = true;
                    OnCompositionStarted?.Invoke(compState.CompositionText);
                }

                if (!string.IsNullOrEmpty(compState.CommittedText))
                {
                    OnCommittedText?.Invoke(compState.CommittedText);
                }
                break;

            case NativeWindow.WM_IME_ENDCOMPOSITION:
                var endState = handler.GetCompositionState();
                if (!string.IsNullOrEmpty(endState.CommittedText))
                {
                    OnCommittedText?.Invoke(endState.CommittedText);
                }
                OnCompositionEnded?.Invoke(endState.CompositionText);
                _wasComposing = false;
                _isComposing = false;
                break;

            case NativeWindow.WM_IME_NOTIFY:
                if (wParam.ToInt32() == NativeWindow.IMN_SETOPENSTATUS ||
                    wParam.ToInt32() == NativeWindow.IMN_SETCONVERSIONMODE)
                {
                    OnImeStateChanged?.Invoke();
                }
                break;
        }
    }

    public static class NativeWindow
    {
        public const uint WM_IME_STARTCOMPOSITION = 0x010D;
        public const uint WM_IME_ENDCOMPOSITION = 0x010E;
        public const uint WM_IME_COMPOSITION = 0x010F;
        public const uint WM_IME_NOTIFY = 0x0282;
        public const int IMN_SETOPENSTATUS = 0x0005;
        public const int IMN_SETCONVERSIONMODE = 0x0004;
    }
}