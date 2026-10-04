using Acrux.Core.Css.Properties;

namespace Acrux.Core.Css.Cascade;

/// <summary>
/// Maps CSS property names to their winning CascadePriority.
/// Standard properties are stored in a fixed-size bitfield + array; custom properties in a dictionary.
/// Mirrors Blink's CascadeMap.
/// </summary>
public class CascadeMap
{
    private readonly Dictionary<CssPropertyId, PriorityList> _native = new();
    private readonly Dictionary<string, PriorityList> _custom = new();
    private bool _hasImportant;
    private bool _inlineStyleLost;

    public bool HasImportant => _hasImportant;
    public bool InlineStyleLost => _inlineStyleLost;

    public void Reset()
    {
        _native.Clear();
        _custom.Clear();
        _hasImportant = false;
        _inlineStyleLost = false;
    }

    public void Add(CssPropertyId id, CascadePriority priority)
    {
        if (priority.IsImportant) _hasImportant = true;
        AddTo(GetOrCreateNative(id), priority);
    }

    public void Add(string customName, CascadePriority priority)
    {
        if (priority.IsImportant) _hasImportant = true;
        AddTo(GetOrCreateCustom(customName), priority);
    }

    private void AddTo(PriorityList list, CascadePriority priority)
    {
        list.Versions.Add(priority);
        if (!list.Winner.IsRelevant || CascadePriority.Compare(priority, list.Winner) > 0)
            list.Winner = priority;
    }

    public bool Has(CssPropertyId id) => _native.ContainsKey(id);
    public bool Has(string customName) => _custom.ContainsKey(customName);

    public CascadePriority At(CssPropertyId id) =>
        _native.TryGetValue(id, out var list) ? list.Winner : default;

    public CascadePriority At(string customName) =>
        _custom.TryGetValue(customName, out var list) ? list.Winner : default;

    public bool TryGetWinner(CssPropertyId id, out CascadePriority priority)
    {
        if (_native.TryGetValue(id, out var list) && list.Winner.IsRelevant)
        {
            priority = list.Winner;
            return true;
        }
        priority = default;
        return false;
    }

    public bool TryGetWinner(string customName, out CascadePriority priority)
    {
        if (_custom.TryGetValue(customName, out var list) && list.Winner.IsRelevant)
        {
            priority = list.Winner;
            return true;
        }
        priority = default;
        return false;
    }

    public IEnumerable<CssPropertyId> NativeIds => _native.Keys;
    public IEnumerable<string> CustomNames => _custom.Keys;

    private PriorityList GetOrCreateNative(CssPropertyId id)
    {
        if (!_native.TryGetValue(id, out var list))
        {
            list = new PriorityList();
            _native[id] = list;
        }
        return list;
    }

    private PriorityList GetOrCreateCustom(string name)
    {
        if (!_custom.TryGetValue(name, out var list))
        {
            list = new PriorityList();
            _custom[name] = list;
        }
        return list;
    }

    private class PriorityList
    {
        public readonly List<CascadePriority> Versions = new();
        public CascadePriority Winner;
    }
}