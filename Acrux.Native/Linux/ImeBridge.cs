namespace Acrux.Native.Linux;

public interface IImeBridge
{
    string GetCommittedText();
    string GetPreeditText();
    int GetCursorPosition();
    void Reset();
    void ProcessKeyEvent(uint keyVal, uint state);
}

public class ImeBridge : IImeBridge
{
    private string _committedText = string.Empty;
    private string _preeditText = string.Empty;
    private int _cursorPosition;

    public string GetCommittedText() => _committedText;

    public string GetPreeditText() => _preeditText;

    public int GetCursorPosition() => _cursorPosition;

    public void Reset()
    {
        _committedText = string.Empty;
        _preeditText = string.Empty;
        _cursorPosition = 0;
    }

    public void ProcessKeyEvent(uint keyVal, uint state)
    {
    }

    public void SetCommittedText(string text)
    {
        _committedText = text;
        _preeditText = string.Empty;
        _cursorPosition = 0;
    }

    public void SetPreeditText(string text, int cursorPos)
    {
        _preeditText = text;
        _cursorPosition = cursorPos;
    }
}