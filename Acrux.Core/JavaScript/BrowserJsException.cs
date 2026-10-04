namespace Acrux.Core.JavaScript;

public class BrowserJsException : Exception
{
    public string? ErrorType { get; set; }
    public string? SourceUrl { get; set; }
    public int LineNumber { get; set; }
    public int ColumnNumber { get; set; }
    public string? JsStackTrace { get; set; }

    public BrowserJsException(string message) : base(message) { }
    public BrowserJsException(string message, Exception inner) : base(message, inner) { }
    public BrowserJsException(string message, string sourceUrl, int lineNumber, int columnNumber, string stackTrace)
        : base(message)
    {
        SourceUrl = sourceUrl;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        JsStackTrace = stackTrace;
    }
}