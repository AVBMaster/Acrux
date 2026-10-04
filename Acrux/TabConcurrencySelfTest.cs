using System.Diagnostics;
using Acrux.Core.Dom;
using Acrux.Core.EventLoop;
using Acrux.Core.JavaScript;
using Acrux.Process;
using Acrux.Rendering;

namespace Acrux;

/// <summary>
/// Headless self-test for threaded-tab ownership: hands a live JS engine +
/// document to a TabProcess worker, verifies the background pump keeps running
/// timers and rebuilding the display list, then returns ownership to the UI.
/// Invoked via <c>Acrux --tabtest</c>. Returns 0 on pass, 1 on fail.
/// </summary>
internal static class TabConcurrencySelfTest
{
    public static int Run()
    {
        var el = new EventLoop();
        var proc = new TabProcess(0, "test", Array.Empty<string>(), el, 1f, 0f);
        proc.Start();

        var dm = new DocumentManager();
        string html = "<html><head></head><body><div id='x'>start</div></body></html>";
        var load = dm.LoadHtmlAsync(html, null, 800, 600, 1f).GetAwaiter().GetResult();

        var engine = new JavaScriptEngine(0);
        engine.LoadDocument(load.Document);
        engine.Execute("var n=0; setInterval(function(){ n++; var e=document.getElementById('x'); if(e) e.textContent='count'+n; }, 40);");
        engine.MarkDirty();

        var div = load.Document.GetElementById("x");
        string initial = div?.TextContent ?? "<null>";

        var own = new TabOwnership
        {
            LoadResult = load,
            Engine = engine,
            Html = html,
            BaseUrl = null,
            DisplayList = new DisplayList(),
            ScrollX = 0,
            ScrollY = 0
        };
        proc.HandOffToWorker(own);

        // Let the worker thread pump JS timers + rebuild display lists.
        Thread.Sleep(500);
        string afterBg = div?.TextContent ?? "<null>";
        var midDl = proc.GetDisplayList();

        // Return ownership to the UI thread.
        var done = new ManualResetEventSlim();
        TabOwnership? back = null;
        proc.RequestReturnToUi((o, dl) => { back = o; done.Set(); });
        var sw = Stopwatch.StartNew();
        while (!done.IsSet && sw.ElapsedMilliseconds < 2000)
        {
            el.ProcessTasks();
            Thread.Sleep(2);
        }

        // After return, the worker must NOT keep mutating the DOM.
        string atReturn = div?.TextContent ?? "<null>";
        Thread.Sleep(250);
        string afterReturn = div?.TextContent ?? "<null>";

        proc.Dispose();

        bool bgAdvanced = afterBg != initial && afterBg.StartsWith("count");
        bool returned = back != null;
        bool frozenAfterReturn = atReturn == afterReturn;
        bool dlBuilt = midDl.Count > 0;

        Console.WriteLine($"[tabtest] initial='{initial}' afterBg='{afterBg}' atReturn='{atReturn}' afterReturn='{afterReturn}' dlOps={midDl.Count}");
        Console.WriteLine($"[tabtest] bgAdvanced={bgAdvanced} returned={returned} frozenAfterReturn={frozenAfterReturn} dlBuilt={dlBuilt}");

        bool pass = bgAdvanced && returned && frozenAfterReturn && dlBuilt;
        Console.WriteLine(pass ? "[tabtest] PASS" : "[tabtest] FAIL");
        return pass ? 0 : 1;
    }
}
