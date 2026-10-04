namespace Acrux.Core.Dom;

public class MutationObserverInit
{
    public bool ChildList { get; set; }
    public bool Attributes { get; set; }
    public bool CharacterData { get; set; }
    public bool Subtree { get; set; }
    public bool AttributeOldValue { get; set; }
    public bool CharacterDataOldValue { get; set; }
    public List<string>? AttributeFilter { get; set; }
}

public class MutationObserver
{
    private readonly Action<MutationRecord[], MutationObserver> _callback;
    private readonly List<ObservationTarget> _targets = new();
    private readonly Queue<MutationRecord> _recordQueue = new();
    private bool _isObserving;

    public MutationObserver(Action<MutationRecord[], MutationObserver> callback)
    {
        _callback = callback;
    }

    public void Observe(Node target, MutationObserverInit options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var existing = _targets.FirstOrDefault(t => t.Target == target);
        if (existing != null)
        {
            existing.Options = options;
        }
        else
        {
            _targets.Add(new ObservationTarget { Target = target, Options = options });
        }

        _isObserving = true;
    }

    public void Disconnect()
    {
        _targets.Clear();
        _recordQueue.Clear();
        _isObserving = false;
    }

    public MutationRecord[] TakeRecords()
    {
        var records = _recordQueue.ToArray();
        _recordQueue.Clear();
        return records;
    }

    internal void QueueRecord(MutationRecord record)
    {
        _recordQueue.Enqueue(record);
    }

    internal bool IsObserving => _isObserving;

    internal bool HasTarget(Node node)
    {
        return _targets.Any(t =>
            t.Target == node ||
            (t.Options.Subtree && IsDescendant(node, t.Target)));
    }

    internal bool ShouldObserveAttribute(Node target, string attrName)
    {
        var obs = GetTargetObservation(target);
        if (obs == null) return false;
        if (!obs.Options.Attributes) return false;
        if (obs.Options.AttributeFilter != null && !obs.Options.AttributeFilter.Contains(attrName))
            return false;
        return true;
    }

    internal bool ShouldObserveCharacterData(Node target)
    {
        var obs = GetTargetObservation(target);
        if (obs == null) return false;
        return obs.Options.CharacterData;
    }

    internal bool ShouldObserveChildList(Node target)
    {
        var obs = GetTargetObservation(target);
        if (obs == null) return false;
        return obs.Options.ChildList;
    }

    internal void NotifyMutation(Node target, MutationRecord record)
    {
        if (!_isObserving) return;
        QueueRecord(record);
        ScheduleCallback();
    }

    private void ScheduleCallback()
    {
        Task.Run(() =>
        {
            if (_recordQueue.Count > 0)
            {
                var records = TakeRecords();
                _callback(records, this);
            }
        });
    }

    private ObservationTarget? GetTargetObservation(Node node)
    {
        foreach (var t in _targets)
        {
            if (t.Target == node) return t;
            if (t.Options.Subtree && IsDescendant(node, t.Target)) return t;
        }
        return null;
    }

    private static bool IsDescendant(Node node, Node ancestor)
    {
        var current = node.ParentNode;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.ParentNode;
        }
        return false;
    }

    private class ObservationTarget
    {
        public Node Target { get; set; } = null!;
        public MutationObserverInit Options { get; set; } = null!;
    }
}
