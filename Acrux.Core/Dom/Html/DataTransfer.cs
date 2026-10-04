namespace Acrux.Core.Dom.Html;

public class DataTransfer
{
    public string DropEffect { get; set; } = "move";
    public string EffectAllowed { get; set; } = "all";
    public DataTransferItemList Items { get; } = new();
    public string[] Types => Items.Select(i => i.Type).Distinct().ToArray();
    public FileList? Files { get; set; }

    public string GetData(string format) => "";
    public void SetData(string format, string data) { }
    public void ClearData(string? format = null) { }
    public void SetDragImage(Element image, long xOffset, long yOffset) { }
}

public class DataTransferItemList : IReadOnlyList<DataTransferItem>
{
    private readonly List<DataTransferItem> _items = new();

    public int Length => _items.Count;
    public int Count => _items.Count;

    public DataTransferItem? this[int index] => index >= 0 && index < _items.Count ? _items[index] : null;

    public DataTransferItem? Add(string data, string type)
    {
        var item = new DataTransferItem { Kind = "string", Type = type };
        _items.Add(item);
        return item;
    }

    public DataTransferItem? Add(DomFile file)
    {
        var item = new DataTransferItem { Kind = "file", Type = file.Type, GetAsFileResult = file };
        _items.Add(item);
        return item;
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _items.Count) _items.RemoveAt(index);
    }

    public void Clear() => _items.Clear();

    public IEnumerator<DataTransferItem> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

public class DataTransferItem
{
    public string Kind { get; set; } = "";
    public string Type { get; set; } = "";
    public DomFile? GetAsFileResult { get; set; }

    public string? GetAsString()
    {
        if (Kind == "string") return "";
        return null;
    }

    public DomFile? GetAsFile() => Kind == "file" ? GetAsFileResult : null;
}


