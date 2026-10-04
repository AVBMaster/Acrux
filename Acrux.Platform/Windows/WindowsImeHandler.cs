using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Acrux.Native.Windows;

namespace Acrux.Platform.Windows;

public class WindowsImeHandler : IImeHandler
{
    private readonly IntPtr _hwnd;
    private readonly ImeCompositionState _compositionState = new();
    private readonly CandidateWindowState _candidateState = new();
    private SKPoint _caretPosition;
    private float _lineHeight;
    private bool _compositionActive;

    public WindowsImeHandler(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public ImeCompositionState GetCompositionState()
    {
        ReadCurrentComposition();
        return _compositionState;
    }

    public CandidateWindowState GetCandidateState()
    {
        ReadCandidateWindow();
        return _candidateState;
    }

    public void SetCaretPosition(SKPoint screenPosition, float lineHeight)
    {
        _caretPosition = screenPosition;
        _lineHeight = lineHeight;

        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return;

        try
        {
            int caretX = (int)Math.Round(screenPosition.X);
            int caretY = (int)Math.Round(screenPosition.Y + lineHeight);

            var compForm = new Imm32Interop.COMPOSITIONFORM
            {
                dwStyle = Imm32Interop.CFS_POINT,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = caretX,
                    Y = caretY
                },
                rcArea = new Imm32Interop.RECT
                {
                    Left = caretX,
                    Top = caretY,
                    Right = caretX + 200,
                    Bottom = caretY + (int)(lineHeight * 2)
                }
            };

            Imm32Interop.ImmSetCompositionWindow(hIMC, ref compForm);

            var candForm = new Imm32Interop.CANDIDATEFORM
            {
                dwIndex = 0,
                dwStyle = Imm32Interop.CFS_FORCE_POSITION,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = caretX,
                    Y = caretY
                }
            };

            Imm32Interop.ImmSetCandidateWindow(hIMC, ref candForm);
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }

    public void Reset()
    {
        _compositionState.Reset();
        _candidateState.Reset();
        _compositionActive = false;

        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC != IntPtr.Zero)
        {
            try
            {
                Imm32Interop.ImmNotifyIME(hIMC, Imm32Interop.NI_COMPOSITIONSTR, 0, Imm32Interop.CPS_CANCEL);
            }
            finally
            {
                Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
            }
        }
    }

    public void Dispose()
    {
        Reset();
    }

    public bool IsComposing => _compositionActive;

    public bool IsImeEnabled
    {
        get
        {
            IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
            if (hIMC == IntPtr.Zero) return false;
            try
            {
                return Imm32Interop.ImmGetOpenStatus(hIMC);
            }
            finally
            {
                Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
            }
        }
        set
        {
            IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
            if (hIMC == IntPtr.Zero) return;
            try
            {
                Imm32Interop.ImmSetOpenStatus(hIMC, value);
            }
            finally
            {
                Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
            }
        }
    }

    public (int conversionMode, int sentenceMode) GetConversionMode()
    {
        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return (0, 0);
        try
        {
            Imm32Interop.ImmGetConversionStatus(hIMC, out int conversion, out int sentence);
            return (conversion, sentence);
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }

    public void SetConversionMode(int conversionMode, int sentenceMode)
    {
        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return;
        try
        {
            Imm32Interop.ImmSetConversionStatus(hIMC, conversionMode, sentenceMode);
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }

    public void EnableNativeMode(bool enable)
    {
        var (conversion, sentence) = GetConversionMode();
        if (enable)
            conversion |= Imm32Interop.IME_CMODE_NATIVE;
        else
            conversion &= ~Imm32Interop.IME_CMODE_NATIVE;
        SetConversionMode(conversion, sentence);
    }

    public void EnableFullShape(bool enable)
    {
        var (conversion, sentence) = GetConversionMode();
        if (enable)
            conversion |= Imm32Interop.IME_CMODE_FULLSHAPE;
        else
            conversion &= ~Imm32Interop.IME_CMODE_FULLSHAPE;
        SetConversionMode(conversion, sentence);
    }

    public IntPtr GetInputContext()
    {
        return Imm32Interop.ImmGetContext(_hwnd);
    }

    public void AssociateInputContext(IntPtr hIMC)
    {
        Imm32Interop.ImmAssociateContext(_hwnd, hIMC);
    }

    private void ReadCurrentComposition()
    {
        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return;

        try
        {
            int compLen = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_COMPSTR, null, 0);
            if (compLen > 0)
            {
                byte[] buffer = new byte[compLen];
                int read = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_COMPSTR, buffer, compLen);
                if (read > 0)
                {
                    _compositionState.CompositionText = Encoding.Unicode.GetString(buffer, 0, read);
                    _compositionActive = true;
                }
            }
            else
            {
                _compositionState.CompositionText = string.Empty;
                _compositionActive = false;
            }

            int cursorPosLen = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_CURSORPOS, null, 0);
            if (cursorPosLen >= 4)
            {
                byte[] buf = new byte[4];
                Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_CURSORPOS, buf, 4);
                _compositionState.CursorPosition = BitConverter.ToInt32(buf, 0);
            }

            int resultLen = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_RESULTSTR, null, 0);
            if (resultLen > 0)
            {
                byte[] resultBuf = new byte[resultLen];
                int resultRead = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_RESULTSTR, resultBuf, resultLen);
                if (resultRead > 0)
                {
                    _compositionState.CommittedText = Encoding.Unicode.GetString(resultBuf, 0, resultRead);
                }
            }
            else
            {
                _compositionState.CommittedText = string.Empty;
            }

            int readingLen = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_READINGSTRING, null, 0);
            if (readingLen > 0)
            {
                byte[] readingBuf = new byte[readingLen];
                int readingRead = Imm32Interop.ImmGetCompositionString(hIMC, Imm32Interop.GCS_READINGSTRING, readingBuf, readingLen);
                if (readingRead > 0)
                {
                    _compositionState.ReadingString = Encoding.Unicode.GetString(readingBuf, 0, readingRead);
                }
            }
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }

    private void ReadCandidateWindow()
    {
        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero) return;

        try
        {
            int candidateCount = Imm32Interop.ImmGetCandidateCount(hIMC);
            if (candidateCount > 0)
            {
                int listSize = candidateCount * 32 + 20;
                IntPtr listPtr = Marshal.AllocHGlobal(listSize);
                try
                {
                    int read = Imm32Interop.ImmGetCandidateList(hIMC, 0, listPtr, listSize);
                    if (read > 0)
                    {
                        var list = Marshal.PtrToStructure<Imm32Interop.CANDIDATELIST>(listPtr);
                        _candidateState.Candidates.Clear();
                        _candidateState.SelectedIndex = (int)list.dwSelection;
                        _candidateState.PageSize = (int)list.dwPageSize;

                        for (int i = 0; i < list.dwCount && i < 9; i++)
                        {
                            int offset = 20 + i * 32;
                            if (offset + 32 <= read)
                            {
                                string cand = Marshal.PtrToStringUni(listPtr + offset);
                                if (!string.IsNullOrEmpty(cand))
                                {
                                    _candidateState.Candidates.Add(cand);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(listPtr);
                }
            }
            else
            {
                _candidateState.Reset();
            }
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
    }
}