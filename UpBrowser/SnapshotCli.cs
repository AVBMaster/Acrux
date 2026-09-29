using UpBrowser.Rendering;

namespace UpBrowser;

/// <summary>
/// Off-screen rendering command line used by the visual-regression harness.
///
///   UpBrowser --snapshot &lt;input.html&gt; &lt;output.png&gt; [width] [height] [dpiScale] [timeMs]
///   UpBrowser --diff &lt;expected.png&gt; &lt;actual.png&gt; [diff.png] [tolerance]
///
/// The snapshot mode never opens a window, so it is safe to run repeatedly from a
/// script and compare the output against a reference rendering of the same markup.
/// </summary>
internal static class SnapshotCli
{
    public static int Run(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "--snapshot" => RunSnapshot(args),
                "--diff" => RunDiff(args),
                "--dumplayout" => RunDumpLayout(args),
                "--textops" => RunTextOps(args),
                "--pixels" => RunPixels(args),
                "--rows" => RunRows(args),
                "--anim" => RunAnimDump(args),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[snapshot] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static int RunSnapshot(string[] args)
    {
        if (args.Length < 3) return Usage();

        string inputPath = args[1];
        string outputPath = args[2];
        int width = args.Length > 3 ? int.Parse(args[3]) : 1024;
        int height = args.Length > 4 ? int.Parse(args[4]) : 768;
        float dpiScale = args.Length > 5 ? float.Parse(args[5]) : 1f;
        // Document-timeline instant to evaluate animations and transitions at.
        // The shared clock is frozen here, so the frame is reproducible.
        double timeMs = args.Length > 6 ? double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 0;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[snapshot] input not found: {inputPath}");
            return 1;
        }

        var started = DateTime.UtcNow;
        RenderSnapshot.CaptureFileToFile(inputPath, outputPath, width, height, dpiScale, timeMs);
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

        Console.WriteLine($"[snapshot] {inputPath} -> {outputPath} ({width}x{height} @{dpiScale}x t={timeMs}ms) in {elapsed:F0} ms");
        return 0;
    }

    /// <summary>
    /// Dump the animation/transition state of a page as numbers, so timing can be
    /// checked without reading pixels. Mirrors the role <c>--textops</c> plays for
    /// text placement.
    ///   UpBrowser --anim &lt;input.html&gt; [width] [height] [timeMs]
    /// </summary>
    private static int RunAnimDump(string[] args)
    {
        if (args.Length < 2) return Usage();

        string inputPath = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int height = args.Length > 3 ? int.Parse(args[3]) : 768;
        double timeMs = args.Length > 4
            ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[anim] input not found: {inputPath}");
            return 1;
        }

        var full = Path.GetFullPath(inputPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;

        var events = new List<string>();
        var page = RenderSnapshot.Prepare(html, width, height, baseUrl, 1f, true, timeMs,
            (el, evt) => events.Add(evt.Type + " " + Describe(evt)));

        var engine = page.Animations!;
        var keyframes = page.Load.StyleComputer?.CollectKeyframeRules();
        var result = page.UpdateResult;

        Console.WriteLine($"[anim] time={timeMs}ms active={result.HasActiveAnimations} " +
                          $"needsLayout={result.NeedsLayout} needsRepaint={result.NeedsRepaint} " +
                          $"effects={engine.ActiveEffectCount} " +
                          $"animatedElements={result.AnimatedElements} declared={CountAnimated(page.Root)}");

        int index = 0;
        DumpElements(page.Root, ref index);

        Console.WriteLine($"[anim] stylesheet keyframe rules: {keyframes?.Count ?? 0}");
        if (keyframes != null)
        {
            foreach (var rule in keyframes)
            {
                var frames = string.Join(" ", rule.Keyframes.Select(k =>
                    $"{k.Key} {{ {string.Join("; ", k.Properties.Properties.Select(p => p.Name.ToCssString() + ":" + p.Value.CssText()))} }}"));
                Console.WriteLine($"[kf] {rule.Name} -> {frames}");

                // The normalized set the engine actually samples: offsets merged,
                // implicit endpoints added, per-keyframe easing extracted.
                var byName = new List<UpBrowser.Core.Css.Rules.StyleRuleKeyframes> { rule };
                var built = UpBrowser.Core.Dom.Animations.CssKeyframes.Build(rule.Name, byName);
                if (built == null) { Console.WriteLine($"[kfn] {rule.Name} -> (not built)"); continue; }
                foreach (var f in built.Frames)
                {
                    string decls = string.Join("; ", f.Declarations.Select(d => d.Key + ":" + d.Value));
                    Console.WriteLine($"[kfn] {rule.Name} {f.Offset * 100:F1}% " +
                                      $"easing={(f.Easing?.CssText ?? "-")} explicit={f.IsExplicit} {{{decls}}}");
                }
            }
        }

        Console.WriteLine("[anim] events:");
        foreach (var e in events)
            Console.WriteLine($"[evt] {e}");

        return 0;
    }

    /// <summary>
    /// Shadows in canonical text, so an interpolated value can be read back as
    /// numbers rather than inferred from pixels. AOT-friendly: the two record
    /// shapes are read directly rather than through reflection.
    /// </summary>
    private static string SerializeShadows(List<UpBrowser.Core.Dom.BoxShadowValue>? shadows)
    {
        if (shadows == null || shadows.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var s in shadows)
            parts.Add((s.Inset ? "inset " : "") +
                      $"{s.OffsetX} {s.OffsetY} {s.BlurRadius} {s.Spread} " +
                      $"rgba({s.Color.Red},{s.Color.Green},{s.Color.Blue},{s.Color.Alpha})");
        return string.Join(", ", parts);
    }

    private static string SerializeShadows(List<UpBrowser.Core.Dom.TextShadowValue>? shadows)
    {
        if (shadows == null || shadows.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var s in shadows)
            parts.Add($"{s.OffsetX} {s.OffsetY} {s.BlurRadius} " +
                      $"rgba({s.Color.Red},{s.Color.Green},{s.Color.Blue},{s.Color.Alpha})");
        return string.Join(", ", parts);
    }

    private static int CountAnimated(UpBrowser.Core.Dom.Element element)
    {
        int n = 0;
        var style = element.ComputedStyle;
        if (style != null)
        {
            bool declares = !string.IsNullOrEmpty(style.AnimationName) && style.AnimationName != "none";
            bool transitions = !string.IsNullOrEmpty(style.TransitionDuration) &&
                               style.TransitionDuration != "0s" && style.TransitionDuration != "0ms";
            if (declares || transitions) n++;
        }
        foreach (var child in element.Children)
            if (child is UpBrowser.Core.Dom.Element ce)
                n += CountAnimated(ce);
        return n;
    }

    private static string Describe(UpBrowser.Core.Dom.Event evt) => evt switch
    {
        UpBrowser.Core.Dom.AnimationEvent a =>
            $"name={a.AnimationName} elapsed={a.ElapsedTime:F1} currentTime={a.CurrentTime:F1}",
        UpBrowser.Core.Dom.TransitionEvent t =>
            $"property={t.PropertyName} elapsed={t.ElapsedTime:F1} currentTime={t.CurrentTime:F1}",
        _ => "",
    };

    /// <summary>Print the computed values the animation engine can observe.</summary>
    private static void DumpElements(UpBrowser.Core.Dom.Element element, ref int index)
    {
        if (element == null) return;
        var style = element.ComputedStyle;
        if (style != null)
        {
            bool declares = !string.IsNullOrEmpty(style.AnimationName) && style.AnimationName != "none";
            bool transitions = !string.IsNullOrEmpty(style.TransitionDuration) &&
                               style.TransitionDuration != "0s" && style.TransitionDuration != "0ms";
            if (declares || transitions)
            {
                string id = element.Id is { Length: > 0 } v ? "#" + v : element.TagName + (declares || transitions ? "." + index.ToString() : "");
                string anim = declares
                    ? $"animation=[{style.AnimationName} {style.AnimationDuration} {style.AnimationDelay} {style.AnimationTimingFunction} {style.AnimationIterationCount} {style.AnimationDirection} {style.AnimationFillMode} {style.AnimationPlayState}]"
                    : "";
                string trans = transitions
                    ? $"transition=[{style.TransitionProperty} {style.TransitionDuration} {style.TransitionDelay} {style.TransitionTimingFunction}]"
                    : "";
                Console.WriteLine($"[el] {id} {anim} {trans}");
                Console.WriteLine($"[val] opacity={style.Opacity:F4} transform={style.Transform ?? "none"} " +
                                  $"width={style.Width} height={style.Height} " +
                                  $"color=({style.Color.Red},{style.Color.Green},{style.Color.Blue}) " +
                                  $"bg={(style.BackgroundColor.HasValue ? $"({style.BackgroundColor.Value.Red},{style.BackgroundColor.Value.Green},{style.BackgroundColor.Value.Blue},{style.BackgroundColor.Value.Alpha})" : "none")} " +
                                  $"margin=({style.MarginLeft},{style.MarginTop}) " +
                                  $"borderTopWidth={style.BorderTopWidth:F2} " +
                                  $"fontSize={style.FontSize:F2} lineHeight={style.LineHeight:F3}");
                // The properties the shadow/filter and discrete batches exercise
                // are not visible in the numbers above, so they are printed here.
                Console.WriteLine($"[css] display={style.Display} visibility={style.Visibility} " +
                                  $"listStyle={style.ListStyleTypeString ?? style.ListStyleType.ToString()} " +
                                  $"borderTopStyle={style.BorderTopStyle} " +
                                  $"boxShadow={SerializeShadows(style.BoxShadow)} " +
                                  $"textShadow={SerializeShadows(style.TextShadow)} " +
                                  $"filter={style.Filter ?? "none"} " +
                                  $"bgImage={(style.BackgroundImage is { Count: > 0 } ? style.BackgroundImage[0] : "none")} " +
                                  $"left={style.Left} paddingTop={style.PaddingTop}");
                index++;
            }
        }

        foreach (var child in element.Children)
            if (child is UpBrowser.Core.Dom.Element ce)
                DumpElements(ce, ref index);
    }

    private static int RunDiff(string[] args)
    {
        if (args.Length < 3) return Usage();

        string expectedPath = args[1];
        string actualPath = args[2];
        string? diffPath = args.Length > 3 ? args[3] : null;
        int tolerance = args.Length > 4 ? int.Parse(args[4]) : 0;

        foreach (var path in new[] { expectedPath, actualPath })
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[diff] not found: {path}");
                return 1;
            }
        }

        var result = RenderSnapshot.Compare(expectedPath, actualPath, diffPath, tolerance);
        Console.WriteLine($"[diff] {result}");
        if (diffPath != null && !result.SizeMismatch)
            Console.WriteLine($"[diff] visualization -> {diffPath}");

        return result.SizeMismatch || result.DifferingPixels > 0 ? 1 : 0;
    }

    private static int RunDumpLayout(string[] args)
    {
        if (args.Length < 2) return Usage();

        string inputPath = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int height = args.Length > 3 ? int.Parse(args[3]) : 768;
        float dpiScale = args.Length > 4
            ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
            : 1f;
        double timeMs = args.Length > 5
            ? double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[dumplayout] input not found: {inputPath}");
            return 1;
        }

        var full = Path.GetFullPath(inputPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;

        // Same preparation the PNG path uses, so the boxes printed here are the
        // ones the snapshot rasterized 鈥?animations and transitions included.
        var page = RenderSnapshot.Prepare(html, width, height, baseUrl, dpiScale, true, timeMs);

        DumpBox(page.Load.Document.DocumentElement, 0);
        return 0;
    }

    private static void DumpBox(UpBrowser.Core.Dom.Element? element, int depth)
    {
        if (element == null) return;
        var box = element.LayoutBox;
        var indent = new string(' ', depth * 2);
        if (box != null)
        {
            var b = box.BorderBox;
            Console.WriteLine($"{indent}<{element.TagName}> fontSize={element.ComputedStyle?.FontSize} display={element.ComputedStyle?.Display} lst={element.ComputedStyle?.ListStyleType} bgImg={(element.ComputedStyle?.BackgroundImage is { } bi && bi.Count > 0 ? bi[0] : "null")} border=({b.Left:F1},{b.Top:F1} {b.Width:F1}x{b.Height:F1}) lineH={box.LineHeight:F1} lines={box.Lines?.Count ?? 0} lineRuns={box.LineRuns?.Count ?? 0}");
            if (box.Lines != null)
            {
                foreach (var line in box.Lines)
                {
                    Console.WriteLine($"{indent}  line y={line.Y:F1} baseline={line.Baseline:F1} h={line.Height:F1} runs={line.Runs.Count}");
                    foreach (var run in line.Runs)
                        Console.WriteLine($"{indent}    run '{run.Text}' x={run.X:F1} w={run.Width:F1} b={run.Baseline:F1} fs={run.FontSize}");
                }
            }
        }
        else
        {
            Console.WriteLine($"{indent}<{element.TagName}> fontSize={element.ComputedStyle?.FontSize} (no box)");
        }
        foreach (var child in element.Children)
            if (child is UpBrowser.Core.Dom.Element ce)
                DumpBox(ce, depth + 1);
    }

    /// <summary>
    /// Debug helper: build the display list for a page and dump every text op
    /// (text, x, y) so paint-side misplacement can be traced without pixels.
    /// </summary>
    private static int RunTextOps(string[] args)
    {
        if (args.Length < 2) return Usage();

        string inputPath = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int height = args.Length > 3 ? int.Parse(args[3]) : 768;
        double timeMs = args.Length > 4
            ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
            : 0;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[textops] input not found: {inputPath}");
            return 1;
        }

        var full = Path.GetFullPath(inputPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;

        // Same preparation as the PNG path, so the ops listed here are the ops
        // the snapshot executed at the same animation instant.
        var page = RenderSnapshot.Prepare(html, width, height, baseUrl, 1f, true, timeMs);

        var visitor = new PaintVisitor(
            contentOffsetY: 0,
            sharedTypefaceCache: null,
            sharedImageCache: page.ImageCache,
            fontFamilies: SkiaSharp.SKFontManager.Default.FontFamilies.ToArray(),
            baseUrl: baseUrl,
            viewportWidth: width,
            viewportHeight: height);
        visitor.SetSkipInputTextOverlay(true);
        visitor.VisitDocumentStacking(page.Load.Document);

        var displayList = visitor.GetDisplayList();
        displayList.SortByZIndex();
        foreach (var op in displayList.EnumerateOps())
        {
            Console.WriteLine($"[op] {op.GetType().Name} bounds=[{op.Bounds.Left:F2},{op.Bounds.Top:F2},{op.Bounds.Right:F2},{op.Bounds.Bottom:F2}] z={op.ZIndex}");
            if (op is DrawTextOp t && t.Text.Length > 0 && t.Text.Length <= 12)
            {
                string decorations = "";
                if (t.Underline || t.Overline || t.LineThrough)
                {
                    var lines = new List<string>();
                    if (t.Underline) lines.Add("underline");
                    if (t.Overline) lines.Add("overline");
                    if (t.LineThrough) lines.Add("line-through");
                    decorations = $" deco=[{string.Join(' ', lines)} {t.DecorationStyle} " +
                        $"color=({t.UnderlineColor.Red},{t.UnderlineColor.Green},{t.UnderlineColor.Blue}) " +
                        $"text=({t.Color.Red},{t.Color.Green},{t.Color.Blue}) " +
                        $"thick={(t.DecorationThicknessFromFont ? "from-font" : float.IsNaN(t.DecorationThickness) ? "auto" : t.DecorationThickness.ToString("F1"))} " +
                        $"uoff={(float.IsNaN(t.DecorationUnderlineOffset) ? "auto" : t.DecorationUnderlineOffset.ToString("F1"))} " +
                        $"upos={t.DecorationUnderlinePosition} skipInk={(t.DecorationSkipInk ? "auto" : "none")}]";
                }
                if (t.AncestorDecorations is { Count: > 0 } ancestors)
                    decorations += $" propagated={ancestors.Count} {string.Join(",", ancestors)}";
                Console.WriteLine($"[text] '{t.Text}' x={t.X:F1} y={t.Y:F1} size={t.FontSize:F1}{decorations}");
            }
            else if (op is DrawRectOp r && r.FillColor.Alpha > 0)
                Console.WriteLine($"[rect] fill=({r.FillColor.Red},{r.FillColor.Green},{r.FillColor.Blue}) [{r.Rect.Left:F2},{r.Rect.Top:F2} - {r.Rect.Right:F2},{r.Rect.Bottom:F2}]");
            else if (op is PushLayerOp l)
                Console.WriteLine($"[layer] opacity={l.Opacity:F4} alpha={(byte)(l.Opacity * 255)} " +
                                  $"filter={(l.ImageFilter != null)} blend={l.BlendMode} " +
                                  $"bounds=[{l.Bounds.Left:F2},{l.Bounds.Top:F2},{l.Bounds.Right:F2},{l.Bounds.Bottom:F2}]");
        }
        return 0;
    }

    /// <summary>
    /// Debug helper: print the pixel colors along a horizontal scanline of a PNG.
    ///   UpBrowser --pixels &lt;image.png&gt; &lt;y&gt; &lt;x0&gt; &lt;x1&gt;
    /// </summary>
    /// <summary>
    /// Scan a band of a PNG and report the first and last non-white pixel in each
    /// row. This is how a probe reads geometry: a box that a transition moved or
    /// resized shows up as a different span, which a single fixed sample point
    /// cannot distinguish from a box that merely changed colour.
    ///   UpBrowser --rows &lt;image.png&gt; &lt;x0&gt; &lt;x1&gt; &lt;y0&gt; &lt;y1&gt;
    /// </summary>
    private static int RunRows(string[] args)
    {
        if (args.Length < 6) return Usage();
        string path = args[1];
        int x0 = int.Parse(args[2]);
        int x1 = int.Parse(args[3]);
        int y0 = int.Parse(args[4]);
        int y1 = int.Parse(args[5]);

        using var bmp = SkiaSharp.SKBitmap.Decode(path);
        if (bmp == null) { Console.Error.WriteLine($"[rows] cannot decode {path}"); return 1; }

        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(bmp.Width - 1, x1);
        y1 = Math.Min(bmp.Height - 1, y1);

        // "Content" is anything distinguishable from the white page background.
        // A black outline or a swatch of any colour both count; near-white does not.
        for (int y = y0; y <= y1; y++)
        {
            int first = -1, last = -1;
            for (int x = x0; x <= x1; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red > 246 && c.Green > 246 && c.Blue > 246) continue;
                if (first < 0) first = x;
                last = x;
            }
            Console.WriteLine($"[row] y={y} first={first} last={last}");
        }
        return 0;
    }

    private static int RunPixels(string[] args)
    {
        if (args.Length < 5) return Usage();
        string path = args[1];
        int y = int.Parse(args[2]);
        int x0 = int.Parse(args[3]);
        int x1 = int.Parse(args[4]);
        using var bmp = SkiaSharp.SKBitmap.Decode(path);
        if (bmp == null) { Console.Error.WriteLine($"[pixels] cannot decode {path}"); return 1; }
        if (y < 0 || y >= bmp.Height)
        {
            Console.Error.WriteLine($"[pixels] y={y} out of range [0, {bmp.Height})");
            return 1;
        }
        for (int x = x0; x <= x1; x++)
        {
            if (x < 0 || x >= bmp.Width)
            {
                Console.Error.WriteLine($"[pixels] x={x} out of range [0, {bmp.Width})");
                continue;
            }
            var c = bmp.GetPixel(x, y);
            Console.WriteLine($"[px] x={x} y={y} rgb=({c.Red},{c.Green},{c.Blue})");
        }
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage:");
        Console.Error.WriteLine("  UpBrowser --snapshot <input.html> <output.png> [width] [height] [dpiScale] [timeMs]");
        Console.Error.WriteLine("  UpBrowser --diff <expected.png> <actual.png> [diff.png] [tolerance]");
        Console.Error.WriteLine("  UpBrowser --dumplayout <input.html> [width] [height]");
        Console.Error.WriteLine("  UpBrowser --anim <input.html> [width] [height] [timeMs]");
        Console.Error.WriteLine("  UpBrowser --rows <image.png> <x0> <x1> <y0> <y1>");
        return 64;
    }
}
