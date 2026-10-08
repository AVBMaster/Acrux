using Acrux.Rendering;

namespace Acrux;

/// <summary>
/// Off-screen rendering command line used by the visual-regression harness.
///
///   Acrux --snapshot &lt;input.html&gt; &lt;output.png&gt; [width] [height] [dpiScale] [timeMs]
///   Acrux --diff &lt;expected.png&gt; &lt;actual.png&gt; [diff.png] [tolerance]
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
                "--computed" => RunComputed(args),
                "--textops" => RunTextOps(args),
                "--hittest" => RunHitTest(args),
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
    ///   Acrux --anim &lt;input.html&gt; [width] [height] [timeMs]
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
        var keyframes = page.Load.StyleComputer?.CollectKeyframeRules(page.Load.Document);
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
                var byName = new List<Acrux.Core.Css.Rules.StyleRuleKeyframes> { rule };
                var built = Acrux.Core.Dom.Animations.CssKeyframes.Build(rule.Name, byName);
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
    private static string SerializeShadows(List<Acrux.Core.Dom.BoxShadowValue>? shadows)
    {
        if (shadows == null || shadows.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var s in shadows)
            parts.Add((s.Inset ? "inset " : "") +
                      $"{s.OffsetX} {s.OffsetY} {s.BlurRadius} {s.Spread} " +
                      $"rgba({s.Color.Red},{s.Color.Green},{s.Color.Blue},{s.Color.Alpha})");
        return string.Join(", ", parts);
    }

    private static string SerializeShadows(List<Acrux.Core.Dom.TextShadowValue>? shadows)
    {
        if (shadows == null || shadows.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var s in shadows)
            parts.Add($"{s.OffsetX} {s.OffsetY} {s.BlurRadius} " +
                      $"rgba({s.Color.Red},{s.Color.Green},{s.Color.Blue},{s.Color.Alpha})");
        return string.Join(", ", parts);
    }

    private static int CountAnimated(Acrux.Core.Dom.Element element)
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
            if (child is Acrux.Core.Dom.Element ce)
                n += CountAnimated(ce);
        return n;
    }

    private static string Describe(Acrux.Core.Dom.Event evt) => evt switch
    {
        Acrux.Core.Dom.AnimationEvent a =>
            $"name={a.AnimationName} elapsed={a.ElapsedTime:F1} currentTime={a.CurrentTime:F1}",
        Acrux.Core.Dom.TransitionEvent t =>
            $"property={t.PropertyName} elapsed={t.ElapsedTime:F1} currentTime={t.CurrentTime:F1}",
        _ => "",
    };

    /// <summary>Print the computed values the animation engine can observe.</summary>
    private static void DumpElements(Acrux.Core.Dom.Element element, ref int index)
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
            if (child is Acrux.Core.Dom.Element ce)
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

    private static int RunComputed(string[] args)
    {
        if (args.Length < 2) return Usage();

        string inputPath = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int height = args.Length > 3 ? int.Parse(args[3]) : 768;
        float dpiScale = args.Length > 4
            ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
            : 1f;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[computed] input not found: {inputPath}");
            return 1;
        }

        var full = Path.GetFullPath(inputPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;
        var page = RenderSnapshot.Prepare(html, width, height, baseUrl, dpiScale, true, 0);

        // Quoted CSS values ('list-style: "«"') must survive the pipe into the
        // comparison script, so the console cannot stay on the OEM code page.
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        DumpComputed(page.Load.Document.DocumentElement);
        return 0;
    }

    // A computed-style channel exists so shorthand parsing (order, omission,
    // initial-value resets) can be diffed against a browser's getComputedStyle
    // without a screenshot - the same values, one line per element.
    private static void DumpComputed(Acrux.Core.Dom.Element? element)
    {
        if (element == null) return;

        var style = element.ComputedStyle;
        var id = element.GetAttribute("id");
        // A generated box has no id, but its style is exactly what a browser's
        // getComputedStyle(element, '::before') reports, so dump it under the originating
        // element's name — the only other way to see why a pseudo box is the size it is.
        if (string.IsNullOrEmpty(id) && element.IsGeneratedPseudoElement)
            id = $"{element.ParentElement?.GetAttribute("id") ?? "?"}:{element.TagName.ToLowerInvariant().Replace("pseudo-", "::")}";
        if (style != null && !string.IsNullOrEmpty(id))
        {
            Console.WriteLine($"{id} display={style.DisplayCssText} boxSizing={style.BoxSizing} position={style.Position}");
            Console.WriteLine($"{id} border={Fmt(style.BorderTopWidth)}/{style.BorderTopStyle}/{Color(style.BorderTopColor)} " +
                $"{Fmt(style.BorderRightWidth)}/{style.BorderRightStyle}/{Color(style.BorderRightColor)} " +
                $"{Fmt(style.BorderBottomWidth)}/{style.BorderBottomStyle}/{Color(style.BorderBottomColor)} " +
                $"{Fmt(style.BorderLeftWidth)}/{style.BorderLeftStyle}/{Color(style.BorderLeftColor)}");
            Console.WriteLine($"{id} radius={Fmt(style.BorderTopLeftRadius)},{Fmt(style.BorderTopRightRadius)},{Fmt(style.BorderBottomRightRadius)},{Fmt(style.BorderBottomLeftRadius)}" +
                $"/{Fmt(style.BorderTopLeftRadiusY)},{Fmt(style.BorderTopRightRadiusY)},{Fmt(style.BorderBottomRightRadiusY)},{Fmt(style.BorderBottomLeftRadiusY)}");
            Console.WriteLine($"{id} outline={Fmt(style.OutlineWidth)}/{style.OutlineStyle}/{Color(style.OutlineColor)} offset={Fmt(style.OutlineOffset)}");
            Console.WriteLine($"{id} textDecoration={style.TextDecorationLine}/{style.TextDecorationStyle}/{Color(style.ResolvedTextDecorationColor)}/{Fmt(style.TextDecorationThickness)} fromFont={style.TextDecorationThicknessFromFont} uOffset={(style.TextUnderlineOffsetIsAuto ? "auto" : Fmt(style.TextUnderlineOffset))} uPos={style.TextUnderlinePosition}");
            Console.WriteLine($"{id} margin={Fmt(style.MarginTop)} {Fmt(style.MarginRight)} {Fmt(style.MarginBottom)} {Fmt(style.MarginLeft)}");
            Console.WriteLine($"{id} padding={Fmt(style.PaddingTop)} {Fmt(style.PaddingRight)} {Fmt(style.PaddingBottom)} {Fmt(style.PaddingLeft)}");
            Console.WriteLine($"{id} inset={Fmt(style.Top)}/{Fmt(style.Right)}/{Fmt(style.Bottom)}/{Fmt(style.Left)} z={style.ZIndex?.ToString() ?? "auto"} opacity={Fmt(style.Opacity)}");
            Console.WriteLine($"{id} font={Fmt(style.FontSize)}/{style.FontWeight}/{style.FontStyle}/{style.FontFamily} lh={Fmt(style.LineHeight)}({style.LineHeightIsNormal}) variant={style.FontVariant} transform={style.TextTransform} synthesis={Acrux.Core.Css.Resolver.CssPropertyApplier.FormatFontSynthesis(style.FontSynthesis)}");
            Console.WriteLine($"{id} color={Color(style.Color)} bg={Color(style.BackgroundColor)} bgImg={style.BackgroundImage?.Count ?? 0} bgSize={style.BackgroundSize} bgRepeat={style.BackgroundRepeat} bgPos={style.BackgroundPositionX}|{style.BackgroundPositionY} bgClip={style.BackgroundClip} bgOrigin={style.BackgroundOrigin}");
            // The background layer model as the CSSOM reports it: the scalar fields above can only
            // hold the first layer, so a list that was clamped, cycled or short of a silent layer
            // is only diagnosable here — and this is the text a page's own script reads.
            Console.WriteLine($"{id} bgLayers[i={Bgl(style, "background-image")} rep={Bgl(style, "background-repeat")} sz={Bgl(style, "background-size")} pos={Bgl(style, "background-position")} px={Bgl(style, "background-position-x")} py={Bgl(style, "background-position-y")} ori={Bgl(style, "background-origin")} clp={Bgl(style, "background-clip")} att={Bgl(style, "background-attachment")}]");
            Console.WriteLine($"{id} boxShadow={ShadowList(style.BoxShadow)} textShadow={ShadowList(style.TextShadow)}");
            Console.WriteLine($"{id} flex={Fmt(style.FlexGrow)}/{Fmt(style.FlexShrink)}/{Fmt(style.FlexBasis)} dir={style.FlexDirection} wrap={style.FlexWrap} jc={style.JustifyContent} ai={style.AlignItems} ac={style.AlignContent} gap={Fmt(style.RowGap)}/{Fmt(style.ColumnGap)}");
            Console.WriteLine($"{id} listStyle={style.ListStyleType}/{style.ListStylePosition}/{style.ListStyleImage} custom='{style.ListStyleTypeString}'");
            Console.WriteLine($"{id} spacing={Fmt(style.LetterSpacing)}/{Fmt(style.WordSpacing)} indent={Fmt(style.TextIndent)}({style.TextIndentPercent}%) hang={style.TextIndentHanging} ws={style.WhiteSpace}[{style.WhiteSpaceCollapse}/{style.TextWrapMode}/{style.TextWrapStyle}] overflow={style.OverflowX}/{style.OverflowY} clipMargin={style.OverflowClipMargin?.ToString() ?? style.OverflowClipMarginBox.ToString()}");
            Console.WriteLine($"{id} textOverflow={style.TextOverflow}/'{style.TextOverflowString}' wrap={style.OverflowWrap}/{style.WordBreak} lineBreak={style.LineBreak} hyphens={style.Hyphens} hyphenateChar={style.HyphenateCharacter} textWrap={style.TextWrap}");
            Console.WriteLine($"{id} columns={style.ColumnCount}/{Fmt(style.ColumnWidth)}/{Fmt(style.ColumnGap)} rule={Fmt(style.ColumnRuleWidth)}/{style.ColumnRuleStyle} orphans={style.Orphans} widows={style.Widows}");
            Console.WriteLine($"{id} contain={Acrux.Core.Css.Resolver.CssPropertyApplier.FormatContain(style.Contain)} contentVisibility={style.ContentVisibility} containIntrinsic={Acrux.Core.Css.Resolver.CssPropertyApplier.FormatContainIntrinsic(style)} fieldSizing={style.FieldSizing}");
            Console.WriteLine($"{id} transition={Or(style.TransitionProperty, "all")}/{Or(style.TransitionDuration, "0s")}/{Or(style.TransitionDelay, "0s")}/{Or(style.TransitionTimingFunction, "ease")}");
            Console.WriteLine($"{id} animation={Or(style.AnimationName, "none")}/{Or(style.AnimationDuration, "0s")}/{Or(style.AnimationIterationCount, "1")}/{Or(style.AnimationFillMode, "none")}");
            Console.WriteLine($"{id} pe={Or(style.PointerEvents, "auto")} visibility={style.Visibility} contentVisibility={style.ContentVisibility} caret={Color(style.CaretColor)} accent={Color(style.AccentColor)} float={Acrux.Core.Dom.CssFloatKeywords.ToCssString(style.Float)} clear={Acrux.Core.Dom.CssFloatKeywords.ToCssString(style.Clear)} usedFloat={Acrux.Core.Dom.CssFloatKeywords.ToCssString(style.PhysicalFloat)} imgRendering:{style.ImageRendering} scroll:{style.ScrollBehavior} overscroll:{style.OverscrollBehaviorX}/{style.OverscrollBehaviorY} tab:{Fmt(style.TabSizePx)} filter:{style.Filter}");
            // The pseudo-element side-car is what the layout engine actually receives, so a
            // declaration that never reaches a generated box is only diagnosable by reading
            // it here (generated boxes have no ComputedStyle of their own in this dump).
            if (element.BeforeStyles is { Count: > 0 } before)
                Console.WriteLine($"{id} ::before {{{SideCar(before)}}}");
            if (element.AfterStyles is { Count: > 0 } after)
                Console.WriteLine($"{id} ::after {{{SideCar(after)}}}");
        }

        foreach (var child in element.Children)
            if (child is Acrux.Core.Dom.Element el)
                DumpComputed(el);
    }

    // A missing value is printed as "auto" so a numeric column never hides the
    // difference between "0" and "not specified".
    // A null time/keyword field means "never declared", so the initial value is
    // printed instead of an empty column - the whole point of the channel is that a
    // line can be read straight against a browser's getComputedStyle output.
    private static string Or(string? value, string initial) =>
        string.IsNullOrEmpty(value) ? initial : value;

    private static string Fmt(float? value) =>
        value == null || float.IsNaN(value.Value) ? "auto"
            : value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Fmt(Acrux.Core.Dom.Length? length) =>
        length == null ? "none" : length.ToString() ?? "none";

    private static string Color(SkiaSharp.SKColor? color) =>
        color == null ? "none"
            : color.Value.Alpha == 255
                ? $"rgb({color.Value.Red},{color.Value.Green},{color.Value.Blue})"
                : $"rgba({color.Value.Red},{color.Value.Green},{color.Value.Blue},{color.Value.Alpha / 255f:0.###})";

    // A background layer list exactly as the CSSOM reads it back, so the dump and a browser's
    // getComputedStyle line up without a second tool.
    private static string Bgl(Acrux.Core.Dom.ComputedStyle style, string property) =>
        Acrux.Core.Dom.Animations.ComputedValueSerializer.Get(style, property, includeShorthands: true) ?? "?";

    private static string ShadowList(System.Collections.Generic.List<Acrux.Core.Dom.BoxShadowValue>? shadows) =>
        shadows is not { Count: > 0 } ? "none"
            : string.Join(", ", shadows.Select(s =>
                $"{Color(s.Color)} {Fmt(s.OffsetX)} {Fmt(s.OffsetY)} {Fmt(s.BlurRadius)} {Fmt(s.Spread)}{(s.Inset ? " inset" : "")}"));

    // The raw pseudo-element side-car, in the order the cascade stored it.
    private static string SideCar(System.Collections.Generic.Dictionary<string, string> values) =>
        string.Join("; ", values.Select(kv => kv.Key + "=" + kv.Value));

    private static string ShadowList(System.Collections.Generic.List<Acrux.Core.Dom.TextShadowValue>? shadows) =>        shadows is not { Count: > 0 } ? "none"
            : string.Join(", ", shadows.Select(s =>
                $"{Color(s.Color)} {Fmt(s.OffsetX)} {Fmt(s.OffsetY)} {Fmt(s.BlurRadius)}"));

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

    private static void DumpBox(Acrux.Core.Dom.Element? element, int depth)
    {
        if (element == null) return;
        var box = element.LayoutBox;
        var indent = new string(' ', depth * 2);
        // The id is part of the line so a static probe page can be matched against
        // the same engine's getBoundingClientRect numbers case by case.
        var id = element.GetAttribute("id");
        var label = string.IsNullOrEmpty(id) ? $"<{element.TagName}>" : $"<{element.TagName}#{id}>";
        if (box != null)
        {
            var b = box.BorderBox;
            Console.WriteLine($"{indent}{label} fontSize={element.ComputedStyle?.FontSize} display={element.ComputedStyle?.Display} lst={element.ComputedStyle?.ListStyleType} bgImg={(element.ComputedStyle?.BackgroundImage is { } bi && bi.Count > 0 ? bi[0] : "null")} border=({b.Left:F1},{b.Top:F1} {b.Width:F1}x{b.Height:F1}) lineH={box.LineHeight:F1} lines={box.Lines?.Count ?? 0} lineRuns={box.LineRuns?.Count ?? 0}");
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
            Console.WriteLine($"{indent}{label} fontSize={element.ComputedStyle?.FontSize} (no box)");
        }
        foreach (var child in element.Children)
            if (child is Acrux.Core.Dom.Element ce)
                DumpBox(ce, depth + 1);
    }

    /// <summary>
    /// Hit-test channel: answers "which element takes a pointer here" for a page.
    /// Probe points come from the markup itself — every [data-probe] element contributes
    /// the centre of its own border box — so the same page can be probed in a reference
    /// browser through document.elementFromPoint without either side publishing pixel
    /// coordinates. Extra arguments are read as explicit "x,y" points.
    ///   Acrux --hittest &lt;input.html&gt; [width] [height] [dpiScale] [x,y ...]
    /// </summary>
    private static int RunHitTest(string[] args)
    {
        if (args.Length < 2) return Usage();

        string inputPath = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int height = args.Length > 3 ? int.Parse(args[3]) : 768;
        float dpiScale = args.Length > 4
            ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
            : 1f;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[hittest] input not found: {inputPath}");
            return 1;
        }

        var full = Path.GetFullPath(inputPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;
        var page = RenderSnapshot.Prepare(html, width, height, baseUrl, dpiScale, true, 0);
        var doc = page.Load.Document;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var points = new List<(string Label, float X, float Y)>();
        foreach (var probe in CollectProbes(doc.DocumentElement))
        {
            var box = probe.LayoutBox;
            if (box == null) { Console.WriteLine($"[hit] {probe.GetAttribute("data-probe")} (no box)"); continue; }
            float px = (box.BorderBox.Left + box.BorderBox.Right) / 2f;
            float py = (box.BorderBox.Top + box.BorderBox.Bottom) / 2f;
            points.Add((probe.GetAttribute("data-probe")!, px, py));
        }
        for (int i = 5; i < args.Length; i++)
        {
            var parts = args[i].Split(',');
            if (parts.Length != 2) continue;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var px)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var py)) continue;
            points.Add(($"{px.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{py.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}", px, py));
        }

        foreach (var (label, px, py) in points)
        {
            var hit = Acrux.PageHost.PageHitTest.HitTest(doc, px, py);
            var id = hit?.GetAttribute("id");
            var name = string.IsNullOrEmpty(id) ? (hit?.TagName.ToLowerInvariant() ?? "null") : id;
            var style = hit?.ComputedStyle;
            Console.WriteLine($"[hit] {label} ({Fmt(px)},{Fmt(py)}) -> {name}" +
                (style == null ? "" : $" pe={Or(style.PointerEvents, "auto")} vis={style.Visibility} z={Fmt(style.ZIndex)}"));
        }
        return 0;
    }

    private static IEnumerable<Acrux.Core.Dom.Element> CollectProbes(Acrux.Core.Dom.Element? element)
    {
        if (element == null) yield break;
        if (!string.IsNullOrEmpty(element.GetAttribute("data-probe"))) yield return element;
        foreach (var child in element.Children.OfType<Acrux.Core.Dom.Element>())
            foreach (var found in CollectProbes(child))
                yield return found;
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
        // The device scale belongs to this channel as much as to --dumplayout: a page whose rows
        // depend on 'resolution' has to be readable at any scale, not only at one.
        float dpiScale = args.Length > 4
            ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
            : 1f;
        double timeMs = args.Length > 5
            ? double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture)
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
        var page = RenderSnapshot.Prepare(html, width, height, baseUrl, dpiScale, true, timeMs);

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
            if (op is DrawTextOp t && t.Text.Length > 0)
            {
                // Long runs used to be dropped from the dump entirely, which made the
                // tool look like the text was missing from the display list. The limit has to
                // clear a whole verification row ('name="value" want="value" ok=true' is
                // routinely longer than a sentence), or the dump cannot be read back — and a
                // probe that prints one rule record of a multi-rule sheet per line says more
                // than a sentence does, so a truncated tail is the part that held the answer.
                string shown = t.Text.Length <= 2000 ? t.Text : t.Text[..2000] + $"…(+{t.Text.Length - 2000})";
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
                Console.WriteLine($"[text] '{shown}' x={t.X:F1} y={t.Y:F1} w={t.Bounds.Width:F1} size={t.FontSize:F1}{decorations}");
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
    ///   Acrux --pixels &lt;image.png&gt; &lt;y&gt; &lt;x0&gt; &lt;x1&gt;
    /// </summary>
    /// <summary>
    /// Scan a band of a PNG and report the first and last non-white pixel in each
    /// row. This is how a probe reads geometry: a box that a transition moved or
    /// resized shows up as a different span, which a single fixed sample point
    /// cannot distinguish from a box that merely changed colour.
    ///   Acrux --rows &lt;image.png&gt; &lt;x0&gt; &lt;x1&gt; &lt;y0&gt; &lt;y1&gt;
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
        Console.Error.WriteLine("  Acrux --snapshot <input.html> <output.png> [width] [height] [dpiScale] [timeMs]");
        Console.Error.WriteLine("  Acrux --diff <expected.png> <actual.png> [diff.png] [tolerance]");
        Console.Error.WriteLine("  Acrux --dumplayout <input.html> [width] [height] [dpiScale] [timeMs]");
        Console.Error.WriteLine("  Acrux --textops <input.html> [width] [height] [dpiScale] [timeMs]");
        Console.Error.WriteLine("  Acrux --computed <input.html> [width] [height] [dpiScale]");
        Console.Error.WriteLine("  Acrux --hittest <input.html> [width] [height] [dpiScale] [x,y ...]");
        Console.Error.WriteLine("  Acrux --anim <input.html> [width] [height] [timeMs]");
        Console.Error.WriteLine("  Acrux --rows <image.png> <x0> <x1> <y0> <y1>");
        return 64;
    }
}
