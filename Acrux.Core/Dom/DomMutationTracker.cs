namespace Acrux.Core.Dom;

/// <summary>
/// Global DOM mutation counter. Hosts snapshot <see cref="Version"/> around a
/// JS callback and only mark the page dirty when something actually changed —
/// a timer that merely computes must not trigger a full style/layout/paint
/// rebuild and a tile-cache wipe.
/// Every mutating DOM surface (node tree ops, attributes, inline style, text
/// data, form control state) funnels through <see cref="Notify"/>.
/// </summary>
public static class DomMutationTracker
{
    private static long _version;
    private static volatile bool _trackCharacterData;
    private static readonly List<CharacterDataChange> _characterDataChanges = new();
    private static readonly object _characterDataLock = new();

    /// <summary>Queued text edits are only worth recording while a MutationObserver with
    /// characterData:true is watching; the JS side turns this on and off with its registrations.</summary>
    public static bool TrackCharacterData
    {
        get => _trackCharacterData;
        set
        {
            _trackCharacterData = value;
            if (!value) lock (_characterDataLock) _characterDataChanges.Clear();
        }
    }

    public static long Version => Interlocked.Read(ref _version);

    public static void Notify() => Interlocked.Increment(ref _version);

    /// <summary>DOM §4.5.7: a characterData mutation record carries the data the node held
    /// <em>before</em> that one mutation, so the value has to be captured as it happens — a later
    /// snapshot comparison cannot recover it (the node may have been created and retyped within
    /// the same turn, and several edits coalesce into one diff).</summary>
    public static void NoteCharacterData(CharacterData node, string oldValue, string newValue)
    {
        if (!_trackCharacterData || oldValue == newValue) return;
        lock (_characterDataLock)
        {
            // A page that types into a textarea keystroke by keystroke must not grow this
            // without bound if nothing drains it (a detached document, a stalled checkpoint).
            if (_characterDataChanges.Count >= 4096) return;
            _characterDataChanges.Add(new CharacterDataChange(node, oldValue));
        }
    }

    /// <summary>Hands over everything recorded since the last call and empties the log.</summary>
    public static List<CharacterDataChange> TakeCharacterDataChanges()
    {
        lock (_characterDataLock)
        {
            if (_characterDataChanges.Count == 0) return new List<CharacterDataChange>();
            var taken = new List<CharacterDataChange>(_characterDataChanges);
            _characterDataChanges.Clear();
            return taken;
        }
    }
}

public readonly record struct CharacterDataChange(CharacterData Node, string OldValue);
