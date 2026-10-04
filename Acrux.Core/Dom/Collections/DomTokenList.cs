using System.Collections;

namespace Acrux.Core.Dom;

public class DOMTokenList : IReadOnlyList<string>
{
    private List<string> _tokens = new();

    public DOMTokenList(string? initialValue = null)
    {
        if (!string.IsNullOrWhiteSpace(initialValue))
            _tokens.AddRange(initialValue.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public int Length => _tokens.Count;
    public int Count => _tokens.Count;
    public string this[int index] => _tokens[index];
    public string? Value { get => string.Join(" ", _tokens); set => _tokens = (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(); }

    public bool Contains(string token) => _tokens.Contains(token);
    public void Add(string token) { if (!_tokens.Contains(token)) _tokens.Add(token); }
    public void Remove(string token) => _tokens.Remove(token);
    public bool Toggle(string token, bool? force = null)
    {
        if (force == true || (force == null && !_tokens.Contains(token)))
        { Add(token); return true; }
        Remove(token);
        return false;
    }
    public void Replace(string oldToken, string newToken)
    {
        var idx = _tokens.IndexOf(oldToken);
        if (idx >= 0) _tokens[idx] = newToken;
    }
    public bool Supports(string token) => true;

    public IEnumerator<string> GetEnumerator() => _tokens.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _tokens.GetEnumerator();
}
