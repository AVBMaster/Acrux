using SkiaSharp;

namespace Acrux.Platform;

public class ImeCompositionState
{
    public string CompositionText { get; set; } = string.Empty;
    public string CommittedText { get; set; } = string.Empty;
    public string ReadingString { get; set; } = string.Empty;
    public int CursorPosition { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public bool IsComposing => !string.IsNullOrEmpty(CompositionText);
    public int CompositionStartIndex { get; set; }

    public void Reset()
    {
        CompositionText = string.Empty;
        CommittedText = string.Empty;
        ReadingString = string.Empty;
        CursorPosition = 0;
        SelectionStart = 0;
        SelectionLength = 0;
        CompositionStartIndex = 0;
    }
}

public class CandidateWindowState
{
    public List<string> Candidates { get; set; } = new();
    public int SelectedIndex { get; set; }
    public int PageSize { get; set; } = 9;
    public bool IsVisible => Candidates.Count > 0;
    public SKPoint Position { get; set; }

    public void Reset()
    {
        Candidates.Clear();
        SelectedIndex = 0;
    }
}

public interface IImeHandler : IDisposable
{
    ImeCompositionState GetCompositionState();
    CandidateWindowState GetCandidateState();
    void SetCaretPosition(SKPoint screenPosition, float lineHeight);
    void Reset();
    bool IsImeEnabled { get; set; }
    bool IsComposing { get; }
    (int conversionMode, int sentenceMode) GetConversionMode();
    void SetConversionMode(int conversionMode, int sentenceMode);
    void EnableNativeMode(bool enable);
    void EnableFullShape(bool enable);
    IntPtr GetInputContext();
    void AssociateInputContext(IntPtr hIMC);
}