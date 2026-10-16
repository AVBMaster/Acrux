using System.Text;
using System.Text.RegularExpressions;
using Acrux.Core.JavaScript;
using Acrux.Core.Dom.Parser;

// Headless JS/DOM/Web-API harness.
//
// Every case is an .html file under DOMTest/cases (or the paths given on argv).
// Each case runs inside a real JavaScriptEngine (in-process Jint + the CLR DOM
// hosts), so it exercises the exact bindings a page hits in the browser. A case
// reports through the injected __T(name, cond, info) helper; the harness reads
// window.__RESULTS__ back out and prints PASS/FAIL per assertion.
//
//   dotnet run --project DOMTest --                 # all cases under cases/
//   dotnet run --project DOMTest -- cases/foo.html  # named cases only

string root = AppContext.BaseDirectory;
string caseDir = Path.Combine(root, "cases");
// When launched via `dotnet run --project DOMTest` the content root is the project dir.
string projDir = FindProjectDir();
if (projDir != null)
{
    root = projDir;
    caseDir = Path.Combine(projDir, "cases");
}

string[] args2 = Environment.GetCommandLineArgs().Skip(1).ToArray();

var cases = new List<string>();
if (args2.Length > 0)
{
    foreach (var a in args2)
    {
        string p = Path.IsPathRooted(a) ? a : Path.Combine(root, a);
        if (File.Exists(p)) cases.Add(p);
        else Console.Error.WriteLine($"[warn] case not found: {p}");
    }
}
else if (Directory.Exists(caseDir))
{
    cases.AddRange(Directory.EnumerateFiles(caseDir, "*.html").OrderBy(f => f));
}

if (cases.Count == 0)
{
    Console.WriteLine("No cases found. Put .html probes under DOMTest/cases/.");
    return 0;
}

string outDir = Path.Combine(root, "out");
Directory.CreateDirectory(outDir);

int totalFail = 0;
int totalPass = 0;
var summary = new StringBuilder();

foreach (var file in cases)
{
    string name = Path.GetFileNameWithoutExtension(file);
    Console.WriteLine($"\n=== {name} ===");
    summary.AppendLine($"\n=== {name} ({Path.GetFileName(file)}) ===");

    string html = File.ReadAllText(file);

    using var js = new JavaScriptEngine();
    js.ShowDialog = (msg, kind) => { Console.WriteLine($"    [{kind}] {msg}"); return "true"; };

    var doc = HtmlDocumentParserIntegration.ParseHtml(html);
    // The page is read from a file, so it is given that file's URL — the same thing the shell does
    // before it hands a document to the engine. Without it 'document.baseURI', 'location.href' and
    // every relative resolution a case measures would answer the empty string.
    doc.Url = new Uri(file).AbsoluteUri;
    js.LoadDocument(doc);

    // Give the page a laid-out canvas to measure against, the way the real shell does before it
    // runs script: style associations (a <style> element becomes a stylesheet), geometry reads
    // (getBoundingClientRect, offsetWidth) and the cascade all depend on this pass having run.
    try
    {
        new Acrux.Core.Css.StyleComputer().ComputeStyles(doc, 1024, 768);
        new Acrux.Core.Layout.LayoutEngine().LayoutAurora(doc, 1024, 768);
    }
    catch (Exception lex)
    {
        Console.WriteLine($"    [style/layout pass] {lex.Message}");
    }

    // Test-support bootstrap the cases rely on.
    js.Execute("""
        window.__RESULTS__ = [];
        window.__T = function (name, cond, info) {
            window.__RESULTS__.push({
                name: String(name),
                pass: !!cond,
                info: (info === undefined ? '' : String(info))
            });
        };
        """);

    // Execute inline scripts in document order (skip <script src>, like the parser does).
    foreach (var body in ExtractInlineScripts(html))
    {
        try { js.Execute(body); }
        catch (Exception ex)
        {
            js.Execute($"window.__T('script threw', false, {System.Text.Json.JsonSerializer.Serialize(ex.Message)});");
        }
        DrainTimers(js);
    }
    DrainTimers(js);

    object? raw = js.Evaluate("JSON.stringify(window.__RESULTS__ || [])");
    string json = raw as string ?? "[]";

    int caseFail = 0;
    foreach (var r in ParseResults(json))
    {
        string tag = r.Pass ? "PASS" : "FAIL";
        if (!r.Pass) caseFail++;
        string line = $"  [{tag}] {r.Name}" + (string.IsNullOrEmpty(r.Info) ? "" : $"  -> {r.Info}");
        Console.WriteLine(line);
        summary.AppendLine(line);
    }
    totalFail += caseFail;
    totalPass += ParseResults(json).Count - caseFail;
    summary.AppendLine($"  ({caseFail} failing in {name})");

    // Optional DOM dump for eyeballing.
    object? dump = js.Evaluate("window.__DUMP__ === undefined ? null : String(window.__DUMP__)");
    if (dump is string ds && ds.Length > 0)
    {
        summary.AppendLine("  --- __DUMP__ ---");
        summary.AppendLine(ds);
    }
}

Console.WriteLine();
Console.WriteLine(totalFail == 0
    ? $"ALL DOM/JS CHECKS PASSED ({totalPass} assertions)"
    : $"{totalFail} CHECK(S) FAILED  ({totalPass} passed)");
summary.AppendLine();
summary.AppendLine(totalFail == 0
    ? $"ALL DOM/JS CHECKS PASSED ({totalPass} assertions)"
    : $"{totalFail} CHECK(S) FAILED  ({totalPass} passed)");
File.WriteAllText(Path.Combine(outDir, "last_run.log"), summary.ToString());

return totalFail == 0 ? 0 : 1;

static void DrainTimers(JavaScriptEngine js)
{
    // Run every timer that is due, then wait out the one that is next, the way the shell's pump
    // does — a case that checks something a setTimeout answers has to let that timer actually
    // arrive. Bounded so a mistaken setInterval cannot hold the harness.
    for (int round = 0; round < 120; round++)
    {
        try { js.IntegrationService?.ProcessTimers(); } catch { }
        int delay = int.MaxValue;
        try { delay = js.IntegrationService?.NextTimerDelayMs() ?? int.MaxValue; } catch { }
        if (delay == int.MaxValue) break;
        Thread.Sleep(Math.Min(Math.Max(delay, 1), 20));
    }
}

static IEnumerable<string> ExtractInlineScripts(string html)
{
    // <script ...>body</script>, ignoring anything with a src attribute.
    var re = new Regex(@"<script\b([^>]*)>([\s\S]*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    foreach (Match m in re.Matches(html))
    {
        string attrs = m.Groups[1].Value;
        if (Regex.IsMatch(attrs, @"\bsrc\s*=", RegexOptions.IgnoreCase)) continue;
        Match typeMatch = Regex.Match(attrs, @"type\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (typeMatch.Success)
        {
            string t = typeMatch.Groups[1].Value.Trim().ToLowerInvariant();
            bool jsType = t.Length == 0
                || t == "text/javascript" || t == "application/javascript"
                || t == "module" || t == "text/ecmascript";
            if (!jsType) continue;
        }
        string body = m.Groups[2].Value;
        if (body.Trim().Length == 0) continue;
        yield return body;
    }
}

static string? FindProjectDir()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null)
    {
        if (File.Exists(Path.Combine(d.FullName, "DOMTest.csproj"))) return d.FullName;
        if (Directory.Exists(Path.Combine(d.FullName, "cases")) && d.Name == "DOMTest") return d.FullName;
        d = d.Parent;
    }
    return null;
}

static List<ResultRow> ParseResults(string json)
{
    var list = new List<ResultRow>();
    try
    {
        using var node = System.Text.Json.JsonDocument.Parse(json);
        foreach (var el in node.RootElement.EnumerateArray())
        {
            string nm = el.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
            bool ps = el.TryGetProperty("pass", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.True;
            string inf = el.TryGetProperty("info", out var i) ? i.GetString() ?? "" : "";
            list.Add(new ResultRow(nm, ps, inf));
        }
    }
    catch (Exception ex)
    {
        list.Add(new ResultRow("__harness_parse__", false, ex.Message + " | " + json));
    }
    return list;
}

record struct ResultRow(string Name, bool Pass, string Info);
