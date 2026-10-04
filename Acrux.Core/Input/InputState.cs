using Acrux.Core.Dom;

namespace Acrux.Core.Input;

public class InputState
{
    public Element? FocusedElement { get; private set; }
    public string CurrentValue { get; private set; } = "";
    public int CaretPosition { get; private set; }

    public Action? OnCaretChanged;

    public void SetFocus(Element? element)
    {
        if (FocusedElement != element)
        {
            if (FocusedElement != null)
                FocusedElement.IsFocused = false;

            FocusedElement = element;

            if (FocusedElement != null)
            {
                FocusedElement.IsFocused = true;
                CurrentValue = FocusedElement.Value ?? "";
                CaretPosition = Math.Min(FocusedElement.SelectionStart >= 0 ? FocusedElement.SelectionStart : CurrentValue.Length, CurrentValue.Length);
            }
            else
            {
                CurrentValue = "";
                CaretPosition = 0;
            }
            OnCaretChanged?.Invoke();
        }
    }

    public void ClearFocus()
    {
        if (FocusedElement != null)
        {
            FocusedElement.IsFocused = false;
            FocusedElement = null;
        }
        CurrentValue = "";
        CaretPosition = 0;
        OnCaretChanged?.Invoke();
    }

    public bool HasFocus => FocusedElement != null;

    public void InsertText(string text)
    {
        if (FocusedElement == null) return;
        if (CaretPosition < 0) CaretPosition = 0;
        if (CaretPosition > CurrentValue.Length) CaretPosition = CurrentValue.Length;

        CurrentValue = CurrentValue[..CaretPosition] + text + CurrentValue[CaretPosition..];
        CaretPosition += text.Length;
        CommitToElement();
        OnCaretChanged?.Invoke();
    }

    public void DeleteBackward()
    {
        if (FocusedElement == null) return;
        if (CaretPosition <= 0) return;

        CurrentValue = CurrentValue[..(CaretPosition - 1)] + CurrentValue[CaretPosition..];
        CaretPosition--;
        CommitToElement();
        OnCaretChanged?.Invoke();
    }

    public void DeleteForward()
    {
        if (FocusedElement == null) return;
        if (CaretPosition >= CurrentValue.Length) return;

        CurrentValue = CurrentValue[..CaretPosition] + CurrentValue[(CaretPosition + 1)..];
        CommitToElement();
        OnCaretChanged?.Invoke();
    }

    public void MoveCaret(int newPosition)
    {
        if (FocusedElement == null) return;
        int oldPos = CaretPosition;
        CaretPosition = Math.Clamp(newPosition, 0, CurrentValue.Length);
        if (CaretPosition != oldPos)
        {
            CommitToElement();
            OnCaretChanged?.Invoke();
        }
    }

    public void MoveCaretLeft()
    {
        if (CaretPosition > 0) MoveCaret(CaretPosition - 1);
    }

    public void MoveCaretRight()
    {
        if (CaretPosition < CurrentValue.Length) MoveCaret(CaretPosition + 1);
    }

    public void SelectAll()
    {
        if (FocusedElement == null) return;
        CaretPosition = 0;
        FocusedElement.SelectionStart = 0;
        FocusedElement.SelectionEnd = CurrentValue.Length;
        OnCaretChanged?.Invoke();
    }

    private void CommitToElement()
    {
        if (FocusedElement == null) return;
        FocusedElement.Value = CurrentValue;
        FocusedElement.SelectionStart = CaretPosition;
        FocusedElement.SelectionEnd = CaretPosition;
    }
}