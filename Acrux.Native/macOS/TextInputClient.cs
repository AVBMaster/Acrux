namespace Acrux.Native.MacOS;

public interface ITextInputClient
{
    string GetMarkedText();
    void SetMarkedText(string text, int cursorPosition);
    void InsertText(string text);
    void DeleteBackward();
    void DeleteForward();
    int GetCursorPosition();
    void SetCursorPosition(int position);
}

public class TextInputClient : ITextInputClient
{
    private string _markedText = string.Empty;
    private int _cursorPosition;

    public string GetMarkedText() => _markedText;

    public void SetMarkedText(string text, int cursorPosition)
    {
        _markedText = text;
        _cursorPosition = cursorPosition;
    }

    public void InsertText(string text)
    {
        _markedText = string.Empty;
        _cursorPosition = 0;
    }

    public void DeleteBackward()
    {
        if (_cursorPosition > 0)
        {
            _markedText = _markedText[..(_cursorPosition - 1)] + _markedText[_cursorPosition..];
            _cursorPosition--;
        }
    }

    public void DeleteForward()
    {
        if (_cursorPosition < _markedText.Length)
        {
            _markedText = _markedText[.._cursorPosition] + _markedText[(_cursorPosition + 1)..];
        }
    }

    public int GetCursorPosition() => _cursorPosition;

    public void SetCursorPosition(int position)
    {
        _cursorPosition = Math.Clamp(position, 0, _markedText.Length);
    }
}