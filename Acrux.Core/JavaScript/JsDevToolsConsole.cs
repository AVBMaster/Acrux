using System.Collections.Concurrent;

namespace Acrux.Core.JavaScript;

public class JsDevToolsConsole
{
    private readonly ConcurrentQueue<ConsoleEntry> _entries = new();
    private readonly int _maxEntries = 1000;

    public event Action<ConsoleEntry>? OnNewEntry;

    public IReadOnlyCollection<ConsoleEntry> Entries
    {
        get { lock (_entries) return new List<ConsoleEntry>(_entries); }
    }

    public void Log(string level, params object?[] args)
    {
        var message = string.Join(" ", args.Select(a => FormatArg(a)));
        var entry = new ConsoleEntry
        {
            Level = level,
            Message = message,
            Timestamp = DateTime.UtcNow,
            StackTrace = level == "error" || level == "trace" ? Environment.StackTrace : null
        };

        lock (_entries)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > _maxEntries)
                _entries.TryDequeue(out _);
        }

        OnNewEntry?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_entries)
        {
            while (_entries.TryDequeue(out _)) { }
        }
    }

    private static string FormatArg(object? arg)
    {
        if (arg == null) return "null";
        if (arg is string s) return s;
        if (arg is double d) return d.ToString();
        if (arg is int i) return i.ToString();
        if (arg is bool b) return b ? "true" : "false";
        if (arg is System.Collections.IEnumerable enumerable && arg is not string)
        {
            var items = enumerable.Cast<object>().Select(FormatArg);
            return "[" + string.Join(", ", items) + "]";
        }
        return arg.ToString() ?? "undefined";
    }
}

public class ConsoleEntry
{
    public string Level { get; set; } = "log";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? StackTrace { get; set; }
}
