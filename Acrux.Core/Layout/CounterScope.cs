namespace Acrux.Core.Layout;

/// <summary>
/// The counter scopes of one document walk (CSS Generated Content for Paged Media §4.2,
/// folded into CSS Lists 4 §4.2). A 'counter-reset' opens a scope that lives for the
/// element's subtree, so the value an inner list built does not leak back to the list it
/// sits in; 'counter-increment' instead writes to the innermost scope that already carries
/// the name, which is what lets a later sibling see the earlier item's increment.
///
/// The walk pushes a frame when it enters an element and pops it when it leaves, and the
/// frames are also what 'counters()' reads: that function joins one value per open scope,
/// outermost first.
/// </summary>
public sealed class CounterScope
{
    private readonly List<Dictionary<string, int>> _frames = [new()];

    /// <summary>Opens the scope of one element. Every declaration the element and its
    /// pseudo-elements carry is written into this frame.</summary>
    public void Push() => _frames.Add(new Dictionary<string, int>());

    public void Pop()
    {
        if (_frames.Count > 1)
            _frames.RemoveAt(_frames.Count - 1);
    }

    private Dictionary<string, int> Current => _frames[^1];

    /// <summary>The frame a counter is created in when no open scope carries its name yet:
    /// the enclosing element's scope, not the incrementing element's own. Creating it in
    /// the element's own scope would hide it from the next sibling, and Edge shows the
    /// sibling reading the value (measured: 'A[5]' then 'A[5]', with a later unrelated list
    /// still reading zero).</summary>
    private Dictionary<string, int> CreationFrame => _frames.Count >= 2 ? _frames[^2] : _frames[^1];

    /// <summary>'counter-reset': the name gets a fresh scope here, at the given value or
    /// zero. A later increment of the same name inside the subtree writes to this scope.</summary>
    public void Reset(string name, int value) => Current[name] = value;

    public void Set(string name, int value)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (_frames[i].ContainsKey(name))
            {
                _frames[i][name] = value;
                return;
            }
        }
        CreationFrame[name] = value;
    }

    public void Increment(string name, int delta)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (_frames[i].TryGetValue(name, out int value))
            {
                _frames[i][name] = value + delta;
                return;
            }
        }
        // With no scope carrying the name, the counter starts at zero in the enclosing
        // scope and is then incremented (§4.2).
        CreationFrame[name] = delta;
    }

    /// <summary>The innermost value of a name, or 0 when no counter of that name is open.</summary>
    public int Value(string name)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
            if (_frames[i].TryGetValue(name, out int value))
                return value;
        return 0;
    }

    /// <summary>Every open value of a name, outermost scope first: what counters() joins.</summary>
    public List<int> Values(string name)
    {
        var values = new List<int>();
        foreach (var frame in _frames)
            if (frame.TryGetValue(name, out int value))
                values.Add(value);
        return values;
    }

    // --- declaration parsing -------------------------------------------------

    /// <summary>'counter-reset' / 'counter-increment' take a repeated
    /// '&lt;counter-name&gt; &lt;integer&gt;?' list, the value defaulting to 0 for a reset
    /// and 1 for an increment.</summary>
    public void ApplyReset(string? declaration) => ApplyList(declaration, reset: true);

    public void ApplyIncrement(string? declaration) => ApplyList(declaration, reset: false);

    private void ApplyList(string? declaration, bool reset)
    {
        if (string.IsNullOrWhiteSpace(declaration) ||
            declaration.Trim().Equals("none", System.StringComparison.OrdinalIgnoreCase))
            return;

        var parts = declaration.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < parts.Length)
        {
            string name = parts[i];
            int value = reset ? 0 : 1;
            if (i + 1 < parts.Length &&
                int.TryParse(parts[i + 1], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                value = parsed;
                i++;
            }
            if (reset) Reset(name, value);
            else Increment(name, value);
            i++;
        }
    }

    /// <summary>'counter-set' takes the same list but never defaults to an increment: a
    /// bare name sets the counter to zero.</summary>
    public void ApplySet(string? declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration) ||
            declaration.Trim().Equals("none", System.StringComparison.OrdinalIgnoreCase))
            return;

        var parts = declaration.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < parts.Length)
        {
            string name = parts[i];
            int value = 0;
            if (i + 1 < parts.Length &&
                int.TryParse(parts[i + 1], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                value = parsed;
                i++;
            }
            Set(name, value);
            i++;
        }
    }

    /// <summary>Applies the three counter properties of one computed style, in the order
    /// CSS defines them: reset opens the scopes, then increment and set write into them.</summary>
    public void ApplyStyle(Dom.ComputedStyle style)
    {
        ApplyReset(style.CounterReset);
        ApplyIncrement(style.CounterIncrement);
        ApplySet(style.CounterSet);
    }
}
