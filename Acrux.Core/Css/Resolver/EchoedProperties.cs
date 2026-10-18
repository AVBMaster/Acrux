using System.Globalization;

namespace Acrux.Core.Css.Resolver;

/// <summary>
/// The properties this engine reads, validates, inherits and echoes, and does not yet consume:
/// scroll snapping, container queries, the SVG keyword family that CSS borrows, and the remaining
/// <c>font-variant-*</c> flags. Each entry carries what the reference engine prints for a value and
/// what it answers when the page says nothing, so the four surfaces that must agree — the value
/// grammar (does a declaration exist at all), the canonical text (what both a sheet and the CSSOM
/// echo), the cascade (what a child inherits) and <c>getComputedStyle</c> (what a script reads) —
/// are driven from one table instead of four hand-maintained lists.
///
/// A property leaves this table the day it gains a field with a consumer. The map is the honest
/// answer for "the value is known, correct, and nothing acts on it yet"; it is not somewhere to
/// park a half-implemented feature.
///
/// Every rule below is measured against the reference engine, value by value
/// (snapshots/_b262_truth.txt); where the specification and the engine disagree — <c>auto</c> for
/// <c>print-color-adjust</c>, <c>allows-keywords</c> for <c>interpolate-size</c>,
/// <c>digits</c> for <c>text-combine-upright</c> — the engine wins, because that is what a page
/// sees.
/// </summary>
public static class EchoedProperties
{
    /// <summary>One property: what it says when nothing is written, whether it inherits, and how a
    /// written value is read. <see cref="Normalize"/> returns the canonical text, or null when the
    /// text is no value at all and the declaration must not exist.</summary>
    public sealed class Entry
    {
        public required string Name { get; init; }
        public required string Initial { get; init; }
        public required Func<string, string?> Normalize { get; init; }
        public bool Inherited { get; init; }
        /// <summary>Set when the computed value is not the text the specified value keeps — the
        /// <c>math-depth</c> and <c>offset-rotate</c> shape, where the engine expands a keyword, and
        /// the <c>offset-distance</c> shape, where it resolves a length against the element's own
        /// font. The style is handed to the hook for that alone; an entry that only re-spells a
        /// keyword ignores it.</summary>
        public Func<string, Acrux.Core.Dom.ComputedStyle, string>? ComputedText { get; init; }
        /// <summary>Set when the computed value keeps the math expression the page wrote rather than
        /// the number it works out to — the <c>stroke-width: calc(2)</c> that reads back
        /// <c>calc(2px)</c> (measured). The cascade prices an expression on its way through, so an
        /// entry with this flag is handed the text before that happens; without it the printer would
        /// be given a pixel count and no expression to put the unit in.</summary>
        public bool KeepsMath { get; init; }
    }

    /// <summary>Whether the property's computed value is the expression the page wrote, so that the
    /// cascade must substitute what the text refers to but leave the arithmetic standing.</summary>
    public static bool KeepsMathExpression(string name) => Find(name)?.KeepsMath == true;

    /// <summary>The entry for a property name, or null when the property is not echo-only.</summary>
    public static Entry? Find(string name)
        => ByName.TryGetValue(name.Trim().ToLowerInvariant(), out var entry) ? entry : null;

    /// <summary>The canonical text of a value, or null when the value is refused.</summary>
    public static string? Normalize(string name, string value) => Find(name)?.Normalize(value);

    /// <summary>The entries that run down the tree. <see cref="CssInheritance"/> copies these and
    /// nothing else from this table, so an entry's <c>Inherited</c> flag is read in exactly one
    /// place rather than being decoration.</summary>
    public static IEnumerable<Entry> InheritedEntries { get; private set; } = Array.Empty<Entry>();

    private static readonly Dictionary<string, Entry> ByName = Build();

    private static Dictionary<string, Entry> Build()
    {
        var entries = new Entry[]
        {
            // ===== Scroll Snap 1 §5. The axis and the strictness are one value, and the strictness
            // is printed only when it says 'mandatory' — 'proximity' is what the pair means without
            // it (measured: 'both proximity' reads 'both' while 'x mandatory' reads as written).
            new() { Name = "scroll-snap-type", Initial = "none", Normalize = NormalizeScrollSnapType },
            new() { Name = "scroll-snap-align", Initial = "none", Normalize = NormalizeScrollSnapAlign },
            new() { Name = "scroll-snap-stop", Initial = "normal", Normalize = Single("normal", "always") },

            // ===== Writing Modes 4 §3.4 and Ruby 1 §3.3. 'sideways-left' is not a value here, and
            // 'vertical-rl' belongs to 'writing-mode' alone (both measured).
            new() { Name = "text-orientation", Initial = "mixed", Inherited = true,
                Normalize = Single("mixed", "upright", "sideways-right", "sideways"),
                // 'sideways-right' is the long spelling of the same orientation, and the computed
                // value only ever says the short one (measured).
                ComputedText = (canonical, _) => canonical.Equals("sideways-right",
                    StringComparison.OrdinalIgnoreCase) ? "sideways" : canonical },
            new() { Name = "ruby-align", Initial = "space-around",
                Normalize = Single("start", "center", "space-between", "space-around") },
            new() { Name = "text-combine-upright", Initial = "none",
                Normalize = Single("none", "all") },

            // ===== CSS Aural 2 §4.2, measured word by word: the reference engine keeps 'none',
            // 'normal' and the four common listening manuals, and refuses every other word it has
            // ever defined — 'auto', 'never', 'once', 'always', 'off', 'muted', 'highlight' and
            // 'point' all leave the property at its initial 'normal'.
            new() { Name = "speak", Initial = "normal",
                Normalize = Single("none", "normal", "spell-out", "digits",
                    "literal-punctuation", "no-punctuation") },

            // ===== CSS Shapes 1 §4/§11. 'shape-outside' is the clip-path shape grammar with the
            // MARGIN box as its default, so a box is printed only when it is not 'margin-box' and
            // always behind the shape (measured: 'border-box circle(50%)' reads
            // 'circle(50%) border-box'). 'shape-margin' is a length that resolves against the
            // element's own font at computed-value time ('2em' → '32px'), and 'shape-image-threshold'
            // is a number that a percentage spells as its fraction ('25%' → '0.25').
            new() { Name = "shape-outside", Initial = "none", Normalize = NormalizeShapeOutside },
            new() { Name = "shape-margin", Initial = "0px", Normalize = NormalizeShapeMargin,
                ComputedText = LengthPercentageComputed },
            new() { Name = "shape-image-threshold", Initial = "0",
                Normalize = NormalizeShapeImageThreshold,
                ComputedText = static (text, _) => ClampToUnitInterval(text) },

            // ===== CSS Text 4 §3.9 (the autospace pair) and CSS Text 3 §3.1.5, measured word by
            // word. 'text-autospace' takes 'normal' or its initial 'no-autospace' and nothing of
            // the sub-keywords the older draft spelled ('none', 'insert', 'replace',
            // 'ideograph-space', and any two-word list are all refused); 'text-spacing-trim' takes
            // 'normal', 'space-all', 'space-first' and 'trim-start' alone ('trim-both', 'trim-edge',
            // 'trim-atomic-start', 'allow-end' and every two-word list are refused).
            new() { Name = "text-autospace", Initial = "no-autospace",
                Normalize = Single("normal", "no-autospace") },
            new() { Name = "text-spacing-trim", Initial = "normal",
                Normalize = Single("normal", "space-all", "space-first", "trim-start") },

            // ===== CSS UI 4 §3.7 and the caret's own animation. The reference engine declares
            // 'caret-animation' with ONE value — 'auto' — and drops 'always' and 'never' with it
            // (measured), and 'interactivity' takes 'auto' and 'inert' only.
            new() { Name = "caret-animation", Initial = "auto", Normalize = Single("auto") },
            new() { Name = "interactivity", Initial = "auto", Normalize = Single("auto", "inert") },
            new() { Name = "scroll-initial-target", Initial = "none",
                Normalize = Single("none", "nearest") },

            // ===== CSS Animations 2 §3 and Scroll-linked Animations 1 §3/§5. A timeline name is a
            // <dashed-ident> or 'none', an axis is ONE keyword ('inline block' is refused,
            // measured), and 'animation-composition' is a comma list of the three composition
            // keywords — one per keyframe animation, so a space between two of them is no list.
            new() { Name = "animation-composition", Initial = "replace",
                Normalize = NormalizeCompositionList },
            // Scroll-driven animations 1 §3: the range pair, and the two edges it is made of, are
            // one range per comma-separated timeline animation. An offset may be an expression,
            // which the specified value keeps and the computed value prices
            // (measured: 'animation-range: calc(1px + 2px)' reads 'calc(3px)' and computes '3px';
            // 'animation-range: entry, exit' is two layers and reads back as written).
            new() { Name = "animation-range", Initial = "normal",
                Normalize = NormalizeAnimationRangeList,
                ComputedText = AnimationRangeComputed },
            new() { Name = "animation-range-start", Initial = "normal",
                Normalize = NormalizeAnimationRangeEdgeList,
                ComputedText = AnimationRangeComputed },
            new() { Name = "animation-range-end", Initial = "normal",
                Normalize = NormalizeAnimationRangeEdgeList,
                ComputedText = AnimationRangeComputed },
            new() { Name = "scroll-timeline-axis", Initial = "block",
                Normalize = Single("block", "inline", "x", "y", "initial") },
            new() { Name = "view-timeline-axis", Initial = "block",
                Normalize = Single("block", "inline", "x", "y", "initial") },
            new() { Name = "scroll-timeline-name", Initial = "none",
                Normalize = NormalizeTimelineName },
            new() { Name = "view-timeline-name", Initial = "none",
                Normalize = NormalizeTimelineName },
            new() { Name = "timeline-scope", Initial = "none", Normalize = NormalizeTimelineScope },
            new() { Name = "view-timeline-inset", Initial = "auto",
                Normalize = NormalizeViewTimelineInset },

            // ===== CSS Fonts 5 §11.4 and CSS Anchor Positioning §4.7. 'font-palette' is ONE of the
            // two preset keywords or a single <dashed-ident> ('light dark' is refused, measured);
            // 'position-try-fallbacks' is a COMMA list of the flip and insert keywords, so two of
            // them written with only a space between are no value at all.
            // CSS Overflow 5 §3: which way the marker group sits relative to its scroller, with the
            // word for what goes in it behind that. Measured: the side alone stands ('before'),
            // 'links' may follow it, and the two are the only content word — 'before first',
            // 'before auto', a bare 'links' and 'before after' are all dropped. The computed surface
            // always says what the group holds, so a lone side grows its 'links'.
            // CSS Display 3 §6B and CSS UI 4: 'overlay' says whether a dialog's top-layer box is
            // drawn over the page. Both words are taken, and the computed surface answers 'none'
            // for either — 'auto' only becomes 'auto' once a box is really in the top layer with a
            // transition running (measured: both spellings compute 'none').
            new() { Name = "overlay", Initial = "none", Normalize = Single("auto", "none"),
                ComputedText = static (_, _) => "none" },

            // CSS Anchor Positioning §5.2/§5.3/§4.2. 'position-area' names one or two sides of the
            // anchor's containing block; the measured set so far is 'none' and the single sides,
            // while the 'span-block start' pair is refused. 'position-visibility' says when the
            // fallback box is hidden, and 'anchored' — the word an older draft used — is refused.
            new() { Name = "position-area", Initial = "none",
                Normalize = NormalizePositionArea },
            new() { Name = "position-visibility", Initial = "anchors-visible",
                Normalize = Single("anchors-visible") },
            new() { Name = "position-try", Initial = "none",
                Normalize = NormalizePositionTry },

            // CSS Transitions 2 §4.2 and CSS View Transitions 1: a comma list, one keyword per
            // transition, and the view box the replaced element is cropped to.
            new() { Name = "transition-behavior", Initial = "normal",
                Normalize = NormalizeBehaviorList },
            new() { Name = "object-view-box", Initial = "none",
                Normalize = NormalizeViewBox },

            // Scroll-linked Animations 1 §3/§5: the two shorthands say the name and the axis (and
            // for a view timeline the inset) the longhands carry. Measured: a lone dashed name, a
            // name with an axis, and 'none' all stand; a plain name is not a timeline name.
            new() { Name = "scroll-timeline", Initial = "none",
                Normalize = NormalizeTimelineShorthand },
            new() { Name = "view-timeline", Initial = "none",
                Normalize = NormalizeTimelineShorthand },

            new() { Name = "scroll-marker-group", Initial = "none",
                Normalize = NormalizeScrollMarkerGroup,
                ComputedText = ScrollMarkerGroupComputed },
            // CSS Pointer Events 2 §3.4: the combinable words print in one order whichever way the
            // page wrote them (measured: 'pinch-zoom pan-x' reads 'pan-x pinch-zoom').
            new() { Name = "touch-action", Initial = "auto",
                Normalize = Acrux.Core.Css.Resolver.CssPropertyApplier.CanonicalTouchActionForEcho },
            new() { Name = "font-palette", Initial = "normal", Normalize = NormalizeFontPalette },
            // CSS Fonts 4 §8.7.4, read against the reference engine: of the five alternation
            // functions it takes 'styleset()' and the bare 'historical-forms' only — 'ornaments',
            // 'annotation(2)' and 'character-variant(1)' are each dropped. The two parts it does
            // take sit beside each other space-separated ('historical-forms styleset(a)'), and the
            // names inside the function are the comma list.
            new() { Name = "font-variant-alternates", Initial = "normal",
                Normalize = NormalizeFontVariantAlternates },
            new() { Name = "position-try-fallbacks", Initial = "none",
                Normalize = NormalizePositionTryFallbacks },
            // CSS Anchor Positioning §3.2/§4.5/§4.6: an anchor name is 'none' or ONE
            // <dashed-ident>, 'anchor-scope' adds 'all', and 'position-try-order' is one of the
            // three orderings (measured, both surfaces).
            new() { Name = "anchor-name", Initial = "none", Normalize = NormalizeTimelineName },
            new() { Name = "anchor-scope", Initial = "none", Normalize = NormalizeAnchorScope },
            new() { Name = "position-try-order", Initial = "normal",
                Normalize = Single("normal", "most-width", "most-height") },

            // ===== Fonts 4 §9. A tag is a quoted string, and the engine prints it with double
            // quotes whichever way the page wrote it (measured: "'SIN'" reads '"SIN"').
            new() { Name = "font-language-override", Initial = "normal", Inherited = true,
                Normalize = NormalizeFontLanguageOverride },
            new() { Name = "font-variant-position", Initial = "normal", Inherited = true,
                Normalize = Single("normal", "sub", "super") },
            new() { Name = "font-variant-east-asian", Initial = "normal", Inherited = true,
                Normalize = NormalizeEastAsian },
            new() { Name = "font-variant-numeric", Initial = "normal", Inherited = true,
                Normalize = NormalizeFontVariantNumeric,
                ComputedText = static (text, _) => NumericVariantOrder(text) },
            new() { Name = "font-variant-ligatures", Initial = "normal", Inherited = true,
                Normalize = NormalizeLigatures },

            // ===== Text 4 and Color Adjust 1. 'text-decoration-skip-ink' is NOT in this table even
            // though it looks like the rest: the line painter reads it, so it keeps its own field and
            // its own computed case.
            new() { Name = "print-color-adjust", Initial = "economy", Inherited = true,
                Normalize = Single("economy", "exact") },
            new() { Name = "-webkit-print-color-adjust", Initial = "economy", Inherited = true,
                Normalize = Single("economy", "exact") },
            new() { Name = "hyphenate-limit-chars", Initial = "auto", Inherited = true,
                Normalize = NormalizeHyphenateLimitChars,
                ComputedText = HyphenateLimitCharsComputed },

            // ===== Masks 1 §3.3 and MathML 4 §3.3.2.
            new() { Name = "mask-type", Initial = "luminance",
                Normalize = Single("luminance", "alpha") },
            // 'mask-mode' is NOT in this table though it looks like the rest: the mask painter reads
            // it, so it keeps its own field, its own applier case and its own computed case. An
            // entry here would answer the cascade before that applier runs — which is what happened,
            // and the four masked boxes of batch 28 lost their mask with it.
            // CSS View Transitions 1 §6/§7 and CSS Paged Media 3 §3.5.
            new() { Name = "view-transition-group", Initial = "normal",
                Normalize = NormalizeViewTransitionGroup },
            // CSS Fonts 4 §7.7: which emoji presentation the text asks for. One word, and the
            // reference engine answers it back from the computed surface as well (measured).
            new() { Name = "font-variant-emoji", Initial = "normal", Inherited = true,
                Normalize = Single("normal", "unicode", "emoji", "text") },
            // SVG 2 §11.7 / CSS Inline 3 §3.3: the baseline a box sits on, either named or moved.
            // 'baseline' is the zero position and prints as one (measured: it computes '0px' while
            // 'sub' and 'super' compute themselves), a length is priced against the element's own
            // font and a percentage is left alone. None of it inherits: the shift belongs to the box
            // that asks for it (measured: a child of a 'sub' parent computes '0px').
            new() { Name = "baseline-shift", Initial = "0px",
                Normalize = NormalizeBaselineShift, ComputedText = BaselineShiftComputed,
                KeepsMath = true },
            new() { Name = "view-transition-class", Initial = "none",
                Normalize = NormalizeViewTransitionClass },
            new() { Name = "page", Initial = "auto", Normalize = NormalizePageName },
            new() { Name = "math-depth", Initial = "0", Normalize = NormalizeMathDepth,
                ComputedText = MathDepthComputed },

            // ===== Motion Path 1 §3. Nothing here lays a box on a path yet, but every one of these
            // values is read, inherited and echoed correctly (snapshots/_b265_*): the path is echoed
            // the way the engine reads it — command letter, then its numbers, each separated
            // (measured: 'path("M0 0")' reads 'path("M 0 0")') — a geometry box stands beside the
            // shape but never beside another box, and 'reverse' is a rotation the engine spells out
            // in the computed value while the specified one keeps the word the page wrote.
            new() { Name = "offset-path", Initial = "none", Normalize = NormalizeOffsetPath,
                ComputedText = OffsetPathComputed },
            new() { Name = "offset-distance", Initial = "0px", Normalize = NormalizeOffsetDistance,
                ComputedText = LengthPercentageComputed },
            new() { Name = "offset-rotate", Initial = "auto", Normalize = NormalizeOffsetRotate,
                ComputedText = OffsetRotateComputed },
            new() { Name = "offset-anchor", Initial = "auto", Normalize = NormalizeOffsetAnchor,
                ComputedText = PositionComputed },
            new() { Name = "offset-position", Initial = "normal", Normalize = NormalizeOffsetPosition,
                ComputedText = PositionComputed },

            // ===== SVG 2 properties CSS inherits: each is one keyword of a closed set.
            new() { Name = "text-anchor", Initial = "start", Inherited = true,
                Normalize = Single("start", "middle", "end") },
            new() { Name = "dominant-baseline", Initial = "auto", Inherited = true,
                Normalize = Single("auto", "alphabetic", "hanging", "ideographic", "middle",
                    "mathematical", "central", "use-script") },
            new() { Name = "alignment-baseline", Initial = "auto", Inherited = true,
                Normalize = Single("auto", "baseline", "before-edge", "text-before-edge", "middle",
                    "central", "after-edge", "text-after-edge", "ideographic", "alphabetic",
                    "hanging", "mathematical") },
            new() { Name = "stroke-linecap", Initial = "butt", Inherited = true,
                Normalize = Single("butt", "round", "square") },
            new() { Name = "stroke-linejoin", Initial = "miter", Inherited = true,
                Normalize = Single("miter", "round", "bevel") },
            new() { Name = "fill-rule", Initial = "nonzero", Inherited = true,
                Normalize = Single("nonzero", "evenodd") },
            new() { Name = "clip-rule", Initial = "nonzero", Inherited = true,
                Normalize = Single("nonzero", "evenodd") },

            // ===== CSS Fill and Stroke 3 / SVG 2 §11. The paint family is inherited and, in this
            // engine, read by nothing but the CSSOM — every one of them computed the empty string
            // until here. A <paint> is a colour, 'none' or 'currentColor', and the computed value
            // resolves the keyword to the element's own colour (measured: 'fill: currentColor' reads
            // 'rgb(0, 0, 0)'); a bare number of 'stroke-width'/'stroke-dashoffset' is the pixel
            // length it stands for once computed but keeps its unitless spelling as specified, while
            // a percentage is never priced; and a dash list is printed comma-separated, each part
            // with its unit (measured: '5 3' specifies as '5, 3' and computes '5px, 3px').
            new() { Name = "fill", Initial = "rgb(0, 0, 0)", Inherited = true,
                Normalize = NormalizePaint, ComputedText = PaintComputed },
            new() { Name = "stroke", Initial = "none", Inherited = true,
                Normalize = NormalizePaint, ComputedText = PaintComputed },
            new() { Name = "stop-color", Initial = "rgb(0, 0, 0)",
                Normalize = NormalizeColorOnly, ComputedText = PaintComputed },
            // CSS Filter Effects 1 §14.1/§16.4: the filter primitives' own colour and coverage.
            // 'none' is no colour for them, and their initials differ — 'flood-color' starts black
            // where 'lighting-color' starts white (all measured). None of the five inherits: each
            // belongs to the primitive it paints, and a child of a red parent says its own initial
            // (measured: 'flood-color', 'flood-opacity', 'lighting-color', 'stop-color' and
            // 'stop-opacity' all leave a child at its initial).
            new() { Name = "flood-color", Initial = "rgb(0, 0, 0)",
                Normalize = NormalizeColorOnly, ComputedText = PaintComputed },
            new() { Name = "flood-opacity", Initial = "1",
                Normalize = NormalizeOpacityValue, ComputedText = OpacityComputed },
            new() { Name = "lighting-color", Initial = "rgb(255, 255, 255)",
                Normalize = NormalizeColorOnly, ComputedText = PaintComputed },
            new() { Name = "fill-opacity", Initial = "1", Inherited = true,
                Normalize = NormalizeOpacityValue, ComputedText = OpacityComputed },
            new() { Name = "stroke-opacity", Initial = "1", Inherited = true,
                Normalize = NormalizeOpacityValue, ComputedText = OpacityComputed },
            new() { Name = "stop-opacity", Initial = "1",
                Normalize = NormalizeOpacityValue, ComputedText = OpacityComputed },
            new() { Name = "stroke-width", Initial = "1px", Inherited = true,
                Normalize = NormalizeNonNegativeSvgLength, ComputedText = SvgLengthComputed,
                KeepsMath = true },
            new() { Name = "stroke-dashoffset", Initial = "0px", Inherited = true,
                Normalize = NormalizeSvgLength, ComputedText = SvgLengthComputed,
                KeepsMath = true },
            new() { Name = "stroke-miterlimit", Initial = "4", Inherited = true,
                Normalize = NormalizePositiveNumber },
            new() { Name = "stroke-dasharray", Initial = "none", Inherited = true,
                Normalize = NormalizeDashArray, ComputedText = DashArrayComputed,
                KeepsMath = true },

            // ===== SVG 2 §11.3. The three marker properties name one image or nothing at all. The
            // specified value keeps the address the page wrote in the url() spelling the CSSOM
            // prints; the computed value makes it absolute, except for a bare fragment, which is
            // already a reference into the document (all measured).
            new() { Name = "marker-start", Initial = "none", Inherited = true,
                Normalize = NormalizeMarker, ComputedText = MarkerComputed },
            new() { Name = "marker-mid", Initial = "none", Inherited = true,
                Normalize = NormalizeMarker, ComputedText = MarkerComputed },
            new() { Name = "marker-end", Initial = "none", Inherited = true,
                Normalize = NormalizeMarker, ComputedText = MarkerComputed },

            // ===== Scroll-driven Animations 1, View Transitions 1 and Anchor Position 1. A name is
            // a dashed ident here, so a bare 't' is refused while '--t' is kept; 'view-transition-name'
            // reserves 'auto' (measured: it is refused).
            new() { Name = "animation-timeline", Initial = "auto", Normalize = NormalizeTimeline },
            new() { Name = "view-transition-name", Initial = "none", Normalize = NormalizeViewTransitionName },
            new() { Name = "position-anchor", Initial = "none", Normalize = NormalizePositionAnchor },

            // ===== Containment 3 §3.4 and the interpolation control of Values 4 §1.2. A container
            // name list is space-separated — a comma is no value (measured).
            new() { Name = "container-type", Initial = "normal",
                Normalize = Single("normal", "size", "inline-size") },
            new() { Name = "container-name", Initial = "none", Normalize = NormalizeContainerName },
            // The 'container' shorthand is the same word list read the same way: the reference
            // engine does not sort its parts, does not lower them, and does not even decide which
            // one is the type — 'INLINE-SIZE Cards' comes back exactly as authored, while
            // 'normal none' and 'none cards' and '1cards' are refused (all measured). The one
            // difference is its initial value, which is the name's 'none'.
            new() { Name = "container", Initial = "none",
                Normalize = NormalizeContainerShorthand },
            new() { Name = "interpolate-size", Initial = "numeric-only", Inherited = true,
                Normalize = Single("numeric-only") },
        };

        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var e in entries) map[e.Name] = e;
        InheritedEntries = entries.Where(e => e.Inherited).ToArray();
        return map;
    }

    // ===== the readers =====

    /// <summary>One keyword of a closed set, in the engine's own lower case.</summary>
    // ===== The normalizers the block above names. Each one is the measured value set of its
    // property, and each returns the canonical text or null for a value the reference engine drops.

    private static string? NormalizeCompositionList(string value) =>
        KeywordList(value, "add", "accumulate", "replace");

    /// <summary>'transition-behavior' is a comma list of the two behaviours, one per transition
    /// (CSS Transitions 2 §4.2, measured: 'normal' and 'allow-discrete' both stand and a space
    /// between them is no list).</summary>
    private static string? NormalizeBehaviorList(string value) =>
        KeywordList(value, "normal", "allow-discrete");

    /// <summary>'object-view-box' is 'none' or one inset function (CSS View Transitions 1 §4.6,
    /// measured: 'auto' is refused and 'inset(10%)' stands as written).</summary>
    private static string? NormalizeViewBox(string value)
    {
        var text = value.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        return text.StartsWith("inset(", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    /// <summary>The timeline shorthands: 'none', a dashed name, an axis, or the name and the
    /// axis in either order (Scroll-linked Animations 1 §3.1/§5.1, measured: '--t inline' reads
    /// back as written and a second axis word is no value).</summary>
    private static string? NormalizeTimelineShorthand(string value)
    {
        var words = Words(value);
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        string? name = null, axis = null;
        foreach (var word in words)
        {
            var p = word.ToLowerInvariant();
            if (p is "block" or "inline" or "x" or "y")
            {
                if (axis != null) return null;
                axis = p;
            }
            else if (IsDashedIdent(word))
            {
                if (name != null) return null;
                name = word;
            }
            else return null;
        }
        if (name == null && axis == null) return null;
        return name == null ? axis : axis == null ? name : name + " " + axis;
    }

    private static string? NormalizeScrollMarkerGroup(string value)
    {
        var words = Words(value);
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (words.Length is < 1 or > 2) return null;
        var side = words[0].ToLowerInvariant();
        if (side is not ("before" or "after")) return null;
        if (words.Length == 2)
        {
            if (!words[1].Equals("links", StringComparison.OrdinalIgnoreCase)) return null;
            return side + " links";
        }
        return side;
    }

    private static string ScrollMarkerGroupComputed(string canonical, Acrux.Core.Dom.ComputedStyle _) =>
        // The group always says what it holds, so a side written on its own grows the word the page
        // left out — and one that already said 'links' says it once (measured: 'before' computes
        // 'before links' and 'before links' computes itself).
        canonical is "none" or "before links" or "after links" ? canonical
            : canonical + " links";

    private static string? NormalizeFontPalette(string value)
    {
        var words = Words(value);
        if (words.Length == 1)
        {
            var one = words[0];
            if (one.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
            if (one.Equals("light", StringComparison.OrdinalIgnoreCase)) return "light";
            if (one.Equals("dark", StringComparison.OrdinalIgnoreCase)) return "dark";
            return IsDashedIdent(one) ? one : null;
        }
        return null;
    }

    /// <summary>'font-variant-alternates' as far as the reference engine implements it: 'normal', or
    /// the space-separated pair of 'historical-forms' and 'styleset(<name>#)' (measured: 'ornaments',
    /// 'annotation(2)' and 'character-variant(1)' are each dropped, 'styleset(SS01)' and
    /// 'historical-forms styleset(a)' both stand, and an empty 'styleset()' is no value).</summary>
    private static string? NormalizeFontVariantAlternates(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return null;
        if (text.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        bool sawHistorical = false, sawStyleset = false;
        var kept = new List<string>();
        foreach (var raw in Acrux.Core.Css.MaskLayerParser.SplitTopLevel(text, ' '))
        {
            var item = raw.Trim();
            if (item.Equals("historical-forms", StringComparison.OrdinalIgnoreCase))
            {
                if (sawHistorical) return null;
                sawHistorical = true;
                kept.Add("historical-forms");
                continue;
            }
            int open = item.IndexOf('(');
            if (open <= 0 || !item.EndsWith(")", StringComparison.Ordinal)) return null;
            if (!item[..open].Equals("styleset", StringComparison.OrdinalIgnoreCase)) return null;
            if (sawStyleset) return null;
            var inner = item[(open + 1)..^1].Trim();
            var names = inner.Split(',');
            if (inner.Length == 0 || names.Any(name => name.Trim().Length == 0)) return null;
            sawStyleset = true;
            kept.Add("styleset(" + string.Join(", ", names.Select(name => name.Trim())) + ")");
        }
        return kept.Count == 0 ? null : string.Join(" ", kept);
    }

    /// <summary>'anchor-scope' is 'none', 'all', or a comma list of <dashed-ident>s (CSS Anchor
    /// Positioning §3.3); 'position-anchor' is 'auto', 'none' or one dashed name (§4.3), and the
    /// engine's initial for it is 'none' — a box that declares nothing answers 'none', not 'auto'
    /// (measured).</summary>
    private static string? NormalizeAnchorScope(string value)
    {
        var words = Words(value);
        if (words.Length == 1 && (words[0].Equals("none", StringComparison.OrdinalIgnoreCase)
            || words[0].Equals("all", StringComparison.OrdinalIgnoreCase)))
            return words[0].ToLowerInvariant();
        return DashedList(value);
    }

    /// <summary>CSS Anchor Positioning 1 §4.1: a fallback is a run of flips OR one inset area, never
    /// both — 'flip-block flip-inline', 'left top' and a lone 'block-start' stand, while
    /// 'flip-start block-start' does not (measured; 'insert-*' and 'scale size-down' are the draft's
    /// vocabulary this engine drops, and a flip written twice says nothing twice).</summary>
    private static string? NormalizePositionTryFallbacks(string value)
    {
        var words = Words(value);
        if (words.Length == 0) return null;
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        var flips = new List<string>();
        var area = new List<string>();
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            if (lower is "flip-start" or "flip-block" or "flip-inline") { flips.Add(lower); continue; }
            area.Add(lower);
        }
        if (flips.Count > 0 && area.Count > 0) return null;
        if (area.Count > 0 && !Acrux.Core.Css.CssValueGrammar.IsInsetArea(string.Join(" ", area)))
            return null;
        if (flips.Distinct().Count() != flips.Count) return null;
        var parts = new List<string>();
        if (flips.Count > 0) parts.Add(string.Join(" ", flips));
        if (area.Count > 0) parts.Add(string.Join(" ", area));
        return string.Join(" ", parts);
    }

    /// <summary>§5.2: 'none' or one &lt;inset-area&gt; (measured: 'x-start y-end' and
    /// 'left span-top' stand; 'top bottom' — two words of the same axis — does not).</summary>
    private static string? NormalizePositionArea(string value)
    {
        var words = Words(value);
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        return Acrux.Core.Css.CssValueGrammar.IsInsetArea(value)
            ? string.Join(" ", words.Select(w => w.ToLowerInvariant())) : null;
    }

    /// <summary>§4: the shorthand is the order and the fallbacks in either order, or a named try
    /// block, or 'none' (measured: 'most-height flip-start' and '--t' stand and enumerate the two
    /// longhands, while 'most-height' alone and 'flip-start block-start' do not).</summary>
    private static string? NormalizePositionTry(string value)
    {
        var words = Words(value);
        if (words.Length == 0) return null;
        if (words.Length == 1)
        {
            var only = words[0];
            if (only.Equals("none", StringComparison.OrdinalIgnoreCase)
                || only.Equals("normal", StringComparison.OrdinalIgnoreCase)) return only.ToLowerInvariant();
            if (only.Length > 2 && only.StartsWith("--", StringComparison.Ordinal)) return only;
            return NormalizePositionTryFallbacks(value);
        }
        var order = words.Where(w => w.ToLowerInvariant() is "most-width" or "most-height");
        var rest = string.Join(" ", words
            .Where(w => w.ToLowerInvariant() is not ("most-width" or "most-height")));
        var fallbacks = NormalizePositionTryFallbacks(rest);
        if (fallbacks == null) return null;
        return order.Count() > 0 ? string.Join(" ", order) + " " + fallbacks : fallbacks;
    }

    private static string? NormalizeTimelineName(string value)
    {
        var words = Words(value);
        if (words.Length != 1) return null;
        return words[0].Equals("none", StringComparison.OrdinalIgnoreCase) ? "none"
            : IsDashedIdent(words[0]) ? words[0] : null;
    }

    private static string? NormalizeTimelineScope(string value)
    {
        var words = Words(value);
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        return DashedList(value);
    }

    private static string? NormalizeViewTimelineInset(string value)
    {
        var words = Words(value);
        if (words.Length is < 1 or > 2) return null;
        foreach (var word in words)
        {
            if (word.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || word.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Acrux.Core.Css.CssValueGrammar.IsLengthOrPercentage(word)) return null;
        }
        return string.Join(" ", words.Select(w => w.ToLowerInvariant()));
    }

    /// <summary>Scroll-driven animations 1 §3: the range pair. Each edge is a timeline-range name
    /// with an optional offset, or a bare offset on the 'normal' range, or 'normal' alone — and the
    /// second edge may be left out, in which case it stays at its own default. Three words are the
    /// pair with one offset missing, so the split is decided by what reads rather than by a count
    /// of spaces; '1px 2px 3px' has no split that reads and falls, while 'entry 50% exit' splits as
    /// two and one (measured, both). 'auto' is nobody's range and a name the timeline does not have
    /// ('span-a') falls with it.</summary>
    /// <summary>One range per comma-separated animation, each read by the grammar of a single
    /// range (CSS Scroll-driven Animations 1 §3; measured: 'entry, exit' is two layers, while a
    /// space between the two words names one range's two edges).</summary>
    private static string? NormalizeAnimationRangeList(string value) =>
        KeywordListByLayer(value, NormalizeAnimationRange);

    /// <summary>The same list for one edge, where a layer is a name with an optional offset.</summary>
    private static string? NormalizeAnimationRangeEdgeList(string value) =>
        KeywordListByLayer(value, NormalizeAnimationRangeEdge);

    /// <summary>Apply one layer's reader to every comma-separated layer of a value and rejoin them
    /// the way the engine prints a list — a comma with a space after it.</summary>
    private static string? KeywordListByLayer(string value, Func<string, string?> readLayer)
    {
        var layers = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(value, ',');
        if (layers.Count == 0) return null;
        var printed = new System.Collections.Generic.List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var text = readLayer(layer);
            if (text == null) return null;
            printed.Add(text);
        }
        return string.Join(", ", printed);
    }

    /// <summary>The computed text of a range list: every offset priced against the element, every
    /// name said as the page said it (measured: 'calc(1px + 2px)' computes '3px').</summary>
    private static string AnimationRangeComputed(string canonical,
        Acrux.Core.Dom.ComputedStyle? style)
    {
        var layers = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(canonical, ',');
        var printed = new System.Collections.Generic.List<string>(layers.Count);
        foreach (var layer in layers)
        {
            var words = SplitTopLevel(layer).ToArray();
            var parts = new System.Collections.Generic.List<string>(words.Length);
            foreach (var word in words)
                parts.Add(Acrux.Core.Css.CssValueGrammar.IsLengthPercentageOrMath(word)
                    ? RangeOffsetComputed(word, style)
                    : word);
            printed.Add(string.Join(" ", parts));
        }
        return string.Join(", ", printed);
    }

    private static string RangeOffsetComputed(string token,
        Acrux.Core.Dom.ComputedStyle? style)
    {
        if (style == null) return token;
        if (!Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(token))
            return LengthPercentageComputed(token, style);
        var folded = Acrux.Core.Css.CssMathValue.Fold(token);
        var length = Acrux.Core.Dom.Length.Parse(folded.ToLowerInvariant());
        return length is null or Acrux.Core.Dom.AutoLength
            ? folded : style.ComputedLengthCss(length);
    }

    private static string? NormalizeAnimationRange(string value)
    {
        // Split outside every parenthesis: an offset may be a math expression, and its own spaces
        // are not the spaces between the two edges of the range (measured:
        // 'animation-range: calc(1px + 2px)' is one offset and reads back 'calc(3px)').
        var words = SplitTopLevel(value).ToArray();
        if (words.Length is < 1 or > 4) return null;
        (string text, bool named)? start = null, end = null;
        if (words.Length <= 2)
        {
            // One edge first — 'entry 50%' is one range, not two — and only when that reading
            // fails is the pair of words the two edges ('normal cover' and '1px 2px', both
            // measured).
            start = ReadRangeEdge(words, 0, words.Length, isEnd: false);
            if (start == null && words.Length == 2)
            {
                start = ReadRangeEdge(words, 0, 1, isEnd: false);
                end = start == null ? null : ReadRangeEdge(words, 1, 1, isEnd: true);
            }
        }
        else if (words.Length == 3)
        {
            start = ReadRangeEdge(words, 0, 2, isEnd: false);
            end = start == null ? null : ReadRangeEdge(words, 2, 1, isEnd: true);
            if (start == null || end == null)
            {
                start = ReadRangeEdge(words, 0, 1, isEnd: false);
                end = start == null ? null : ReadRangeEdge(words, 1, 2, isEnd: true);
            }
        }
        else
        {
            start = ReadRangeEdge(words, 0, 2, isEnd: false);
            end = start == null ? null : ReadRangeEdge(words, 2, 2, isEnd: true);
        }
        if (start == null) return null;
        if (words.Length > 2 && end == null) return null;
        if (end == null) return start.Value.text;
        // The end is left out when it says nothing the start did not already say: 'normal' is the
        // edge's own default, and a named edge repeated is the same range on both sides (measured:
        // 'normal normal' and 'cover cover' each read back one word while '1px 1px' reads back two).
        if (end.Value.text == "normal") return start.Value.text;
        if (end.Value.named && end.Value.text == start.Value.text) return start.Value.text;
        return start.Value.text + " " + end.Value.text;
    }

    /// <summary>One edge of a scroll-driven range, read from <paramref name="words"/> at
    /// <paramref name="from"/> for <paramref name="count"/> words and printed as the engine prints
    /// it: the name carries over, and the offset the edge would have taken with no word saying it
    /// does not (measured: 'entry 0%' reads 'entry', 'exit 100%' reads 'exit 100%',
    /// 'contain 100%' reads 'contain', and a bare '0%' keeps its number because a number with no
    /// name has no default to fall back on).</summary>
    private static (string text, bool named)? ReadRangeEdge(string[] words, int from, int count,
        bool isEnd)
    {
        if (count is < 1 or > 2 || from + count > words.Length) return null;
        var first = words[from].ToLowerInvariant();
        bool firstName = System.Array.IndexOf(RangeNames, first) >= 0 || first == "normal";
        string? offset = null;
        if (firstName)
        {
            if (count == 2)
            {
                if (!Acrux.Core.Css.CssValueGrammar.IsLengthPercentageOrMath(words[from + 1]))
                    return null;
                offset = PrintRangeOffset(words[from + 1]);
            }
        }
        else
        {
            if (count != 1 || !Acrux.Core.Css.CssValueGrammar.IsLengthPercentageOrMath(first))
                return null;
            offset = PrintRangeOffset(first);
        }
        if (offset != null && firstName)
        {
            // The offset this edge would have taken anyway: the top of the range at the start and
            // the bottom at the end.
            if (offset == (isEnd ? "100%" : "0%")) offset = null;
        }
        if (!firstName)
        {
            // 'normal' at the END of the range means its bottom edge, which is the 100% no word
            // would have said (measured: 'animation-range: 0% 100%' reads back the one '0%', while
            // a start of '0%' says itself).
            if (isEnd && offset == "100%") return ("normal", false);
            return offset == null ? null : (offset, false);
        }
        return offset == null ? (first, true) : (first + " " + offset, true);
    }

    /// <summary>The length text the engine prints for an offset: a number is a pixel length, and a
    /// percentage keeps its sign (measured: 'animation-range: 0' reads back '0px').</summary>
    private static string PrintRangeOffset(string token)
    {
        token = token.ToLowerInvariant();
        return token == "0" ? "0px" : token;
    }

    private static string? NormalizeAnimationRangeEdge(string value)
    {
        var words = SplitTopLevel(value).ToArray();
        if (words.Length is < 1 or > 2) return null;
        if (words[0].Equals("normal", StringComparison.OrdinalIgnoreCase))
            return words.Length == 1 ? "normal" : null;
        bool firstName = System.Array.IndexOf(RangeNames, words[0].ToLowerInvariant()) >= 0;
        if (firstName)
        {
            if (words.Length == 2
                && !Acrux.Core.Css.CssValueGrammar.IsLengthPercentageOrMath(words[1]))
                return null;
            return string.Join(" ", words.Select(w => PrintRangeOffset(w)));
        }
        if (words.Length != 1
            || !Acrux.Core.Css.CssValueGrammar.IsLengthPercentageOrMath(words[0]))
            return null;
        return PrintRangeOffset(words[0]);
    }

    private static readonly string[] RangeNames =
        { "contain", "cover", "entry", "exit", "entry-crossing", "exit-crossing" };

    /// <summary>A comma list of one keyword per animation, each from the given set — a space
    /// between two of them is not a list (measured: 'replace accumulate' is dropped where
    /// 'add, accumulate' stands).</summary>
    private static string? KeywordList(string value, params string[] allowed)
    {
        var parts = Acrux.Core.Css.MaskLayerParser.SplitTopLevel(value, ',');
        if (parts.Count == 0) return null;
        var kept = new string[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            var words = Words(parts[i]);
            if (words.Length != 1) return null;
            int index = System.Array.FindIndex(allowed,
                a => a.Equals(words[0], StringComparison.OrdinalIgnoreCase));
            if (index < 0) return null;
            kept[i] = allowed[index];
        }
        return string.Join(", ", kept);
    }

    private static string? DashedList(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        foreach (var part in parts)
            if (!IsDashedIdent(part)) return null;
        return string.Join(", ", parts);
    }

    private static Func<string, string?> Single(params string[] allowed) => text =>
    {
        var words = Words(text);
        if (words.Length != 1) return null;
        foreach (var a in allowed)
            if (words[0].Equals(a, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    };

    private static string[] Words(string text) => text.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>'shape-outside' takes the same shape as 'clip-path' (CSS Shapes 1 §11) with the
    /// margin box as its default, so one reader decides both rather than a second list of shape
    /// names drifting from the first.</summary>
    private static string? NormalizeShapeOutside(string value)
    {
        var text = value.Trim();
        return text.Length > 0 && CssValueGrammar.IsClipPathValue(text, bareBoxAllowed: true) ? text : null;
    }

    private static string? NormalizeShapeMargin(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.StartsWith("-", StringComparison.Ordinal)) return null;
        // A math expression is a distance the engine has not finished adding (measured:
        // 'shape-margin: calc(1px)' stands and computes '1px').
        if (text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase)) return text;
        if (!CssValueGrammar.IsLengthOrPercentage(text)) return null;
        // A margin is a distance: the unit or the percentage has to be there, and the bare number
        // is only the zero it may stand for (measured: 'shape-margin: 1' falls while '0', '1px' and
        // '50%' each stand, and '-1px' falls with the number).
        int i = 0;
        while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
        return i == text.Length && text != "0" ? null : text;
    }

    /// <summary>'shape-image-threshold' is a number, and a percentage is the fraction it stands for
    /// (measured: '25%' reads '0.25' on the specified surface as on the computed one). The specified
    /// surface says the number the page wrote — '2' reads '2' and computes '1' (measured, both), so
    /// the clamp belongs to the computed printer and not here: a printer that clamps repairs a value
    /// the property never said.</summary>
    /// <summary>The number as the page wrote it, clamped into 0..1 for the computed surface.</summary>
    private static string ClampToUnitInterval(string text)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Acrux.Core.Dom.Animations.CssValueTokenizer.Num(Math.Clamp(value, 0d, 1d)) : text;

    private static string? NormalizeShapeImageThreshold(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return null;
        double number;
        if (text.EndsWith("%", StringComparison.Ordinal))
        {
            if (!double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                return null;
            number = percent / 100d;
        }
        else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return null;
        if (double.IsNaN(number)) return null;
        return Acrux.Core.Dom.Animations.CssValueTokenizer.Num(number);
    }

    private static string? NormalizeScrollSnapType(string text)
    {
        var words = Words(text);
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (words.Length is not (1 or 2)) return null;
        var axis = words[0].ToLowerInvariant();
        if (axis is not ("x" or "y" or "block" or "inline" or "both")) return null;
        if (words.Length == 1) return axis;
        var strictness = words[1].ToLowerInvariant();
        // 'proximity' is what the axis means on its own, so it is said and then dropped; a value
        // that names both strictnesses is no value (measured).
        if (strictness == "proximity") return axis;
        return strictness == "mandatory" ? axis + " mandatory" : null;
    }

    private static string? NormalizeScrollSnapAlign(string text)
    {
        var words = Words(text);
        if (words.Length == 0 || words.Length > 2) return null;
        foreach (var w in words)
            if (w.ToLowerInvariant() is not ("start" or "end" or "center" or "none")) return null;
        var first = words[0].ToLowerInvariant();
        if (words.Length == 1) return first;
        var second = words[1].ToLowerInvariant();
        // 'none' is a value of its own and does not pair; two equal axes print once (measured:
        // 'start start' reads 'start', while 'end center' keeps both).
        if (first == "none" || second == "none") return null;
        return first == second ? first : first + " " + second;
    }

    private static string? NormalizeFontLanguageOverride(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        if (!IsQuotedString(trimmed)) return null;
        // The tag inside the quotes is a language subtag: the reference engine takes up to four
        // characters and no more (measured: '"ab"', '"abc"' and '"abcd"' stand while '"abcde"' and
        // '"abcdefghijkl"' are each dropped — the spec's three is older than the engine).
        var inner = Unquote(trimmed);
        return inner.Length is >= 1 and <= 4 ? "\"" + inner + "\"" : null;
    }

    /// <summary>CSS Fonts 4 §9.3: one value from each of three groups, and <c>normal</c> says it
    /// alone. The engine prints the groups in its own order — the width variant, then the spacing,
    /// then <c>ruby</c> — whichever order the page wrote them in (measured: 'full-width jis04'
    /// reads 'jis04 full-width' and 'ruby full-width' reads 'full-width ruby').</summary>
    private static string? NormalizeEastAsian(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        string? variant = null, width = null;
        bool ruby = false;
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (w == "normal") return words.Length == 1 ? "normal" : null;
            if (w is "jis04" or "jis78" or "jis83" or "simplified" or "traditional")
            {
                if (variant != null) return null;
                variant = w;
            }
            else if (w is "full-width" or "proportional-width")
            {
                if (width != null) return null;
                width = w;
            }
            else if (w == "ruby")
            {
                if (ruby) return null;
                ruby = true;
            }
            else return null;
        }
        var parts = new List<string>();
        if (variant != null) parts.Add(variant);
        if (width != null) parts.Add(width);
        if (ruby) parts.Add("ruby");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>CSS Fonts 4 §9.2: the numeric keywords come in three either/or groups, and the
    /// engine prints what the page wrote in the order it wrote it (measured: 'tabular-nums ordinal
    /// slashed-zero' reads back unchanged, while 'diagonal-fractions stacked-fractions' is refused
    /// and 'normal ordinal' never combines with the rest).</summary>
    private static string? NormalizeFontVariantNumeric(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outWords = new List<string>();
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (w == "normal") return words.Length == 1 ? "normal" : null;
            if (w is not ("ordinal" or "slashed-zero" or "oldstyle-nums" or "lining-nums"
                or "proportional-nums" or "tabular-nums" or "diagonal-fractions"
                or "stacked-fractions"))
            {
                // A numeric factor ('1/2', '3/4') is the one other thing the grammar takes; it is
                // echoed as written.
                if (!IsFactor(w)) return null;
                w = "factor";
            }
            if (!seen.Add(Group(w))) return null;
            outWords.Add(raw.ToLowerInvariant());
        }
        return string.Join(" ", outWords);
    }

    private static string Group(string w) => w switch
    {
        "oldstyle-nums" or "lining-nums" => "figuring",
        "proportional-nums" or "tabular-nums" => "spacing",
        "diagonal-fractions" or "stacked-fractions" => "fractions",
        "factor" => "factor",
        _ => w,
    };

    /// <summary>The computed spelling of <c>font-variant-numeric</c>: the same keywords, but said in
    /// the order the grammar lists its groups rather than the order the page wrote them (measured:
    /// 'slashed-zero ordinal' computes 'ordinal slashed-zero', and
    /// 'diagonal-fractions slashed-zero ordinal proportional-nums oldstyle-nums' computes
    /// 'oldstyle-nums proportional-nums diagonal-fractions ordinal slashed-zero'). The specified
    /// value keeps the page's own order, which is why this is a computed hook and not the
    /// normalizer.</summary>
    private static string NumericVariantOrder(string text)
    {
        var words = Words(text);
        if (words.Length < 2) return string.Join(" ", words);
        return string.Join(" ", words
            .Select((word, index) => (word, index))
            .OrderBy(item => NumericRank(item.word))
            .ThenBy(item => item.index)
            .Select(item => item.word));
    }

    private static int NumericRank(string word) => word switch
    {
        "oldstyle-nums" or "lining-nums" => 0,
        "proportional-nums" or "tabular-nums" => 1,
        "diagonal-fractions" or "stacked-fractions" => 2,
        "ordinal" => 3,
        "slashed-zero" => 4,
        _ => 5,
    };

    private static bool IsFactor(string w)
    {
        int slash = w.IndexOf('/');
        if (slash <= 0 || slash == w.Length - 1) return false;
        return int.TryParse(w[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && int.TryParse(w[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>CSS Fonts 4 §9.1: the ligature keywords are three either/or groups plus
    /// <c>contextual</c>, and <c>normal</c> stands alone.</summary>
    private static string? NormalizeLigatures(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        // 'none' says the whole value: no ligature of any kind (measured: it stands and reads back
        // 'none', while 'none common-ligatures' is dropped).
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outWords = new List<string>();
        foreach (var raw in words)
        {
            var w = raw.ToLowerInvariant();
            if (w == "normal") return words.Length == 1 ? "normal" : null;
            if (w is not ("common-ligatures" or "no-common-ligatures" or "discretionary-ligatures"
                or "no-discretionary-ligatures" or "historical-ligatures"
                or "no-historical-ligatures" or "contextual" or "no-contextual")) return null;
            string group = w.StartsWith("no-", StringComparison.Ordinal) ? w[3..] : w;
            if (!seen.Add(group)) return null;
            outWords.Add(w);
        }
        return string.Join(" ", outWords);
    }

    /// <summary>CSS Text 4 §5.6.3: <c>auto</c>, or one to three integers where the first is the
    /// whole-word minimum. Zero is not a minimum, so '0' is no value (measured).</summary>
    /// <summary>CSS Text 4 §6.5: one, two or three integers of at least one, each of which may be
    /// the word 'auto' instead — and the words may stand in any order, so 'auto 5' and '5 auto' are
    /// both values (measured). A math expression is a number written another way
    /// ('calc(5) calc(6)' stands), and '0', '1.5' and '-1' are not the integers the property asks
    /// for.</summary>
    private static string? NormalizeHyphenateLimitChars(string text)
    {
        var words = Words(text);
        if (words.Length == 1 && words[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
            return "auto";
        if (words.Length is < 1 or > 3) return null;
        var printed = new List<string>();
        foreach (var w in words)
        {
            if (w.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                printed.Add("auto");
                continue;
            }
            if (Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(w))
            {
                if (!MathNamesOneOf(w, "number")) return null;
                printed.Add(w);
                continue;
            }
            if (!int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1)
                return null;
            printed.Add(n.ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(" ", printed);
    }

    /// <summary>The computed limit list: each number priced or folded, and the trailing 'auto'
    /// words dropped because they say nothing the initial would not (measured: '5 auto' computes
    /// '5', '5 5 auto' computes '5 5', 'auto 5' computes 'auto 5' and 'auto' alone stays 'auto').</summary>
    private static string HyphenateLimitCharsComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        var words = canonical.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int last = words.Length;
        while (last > 0 && words[last - 1].Equals("auto", StringComparison.OrdinalIgnoreCase)) last--;
        if (last == 0) return "auto";
        var printed = new List<string>(last);
        for (int i = 0; i < last; i++)
            printed.Add(Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(words[i])
                ? MathNumber(words[i])
                : words[i]);
        return string.Join(" ", printed);
    }

    /// <summary>MathML 4 §3.3.2: the specified value is <c>auto-add</c>, <c>add(</c>integer<c>)</c>
    /// or a bare integer, and the computed value is the integer it adds — <c>auto-add</c> adds
    /// nothing, so it reads <c>0</c> (all three measured).</summary>
    private static string? NormalizeMathDepth(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("auto-add", StringComparison.OrdinalIgnoreCase)) return "auto-add";
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare))
            return bare.ToString(CultureInfo.InvariantCulture);
        if (!trimmed.StartsWith("add(", StringComparison.OrdinalIgnoreCase)) return null;
        int close = trimmed.IndexOf(')');
        if (close < 0) return null;
        var inner = trimmed[4..close].Trim();
        return int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? "add(" + n.ToString(CultureInfo.InvariantCulture) + ")" : null;
    }

    /// <summary>The computed text of <c>math-depth</c>: the integer the specified value adds.</summary>
    public static string MathDepthComputed(string canonical, Acrux.Core.Dom.ComputedStyle _)
    {
        if (canonical.Equals("auto-add", StringComparison.OrdinalIgnoreCase)) return "0";
        int open = canonical.IndexOf('('), close = canonical.IndexOf(')');
        return open >= 0 && close > open ? canonical[(open + 1)..close].Trim() : canonical;
    }

    /// <summary>The geometry boxes <c>offset-path</c> reads (CSS Motion Path 1 §3.2). The
    /// reference engine's set is the one a shape can be measured against: <c>margin-box</c> is not
    /// in it (measured: refused), while <c>view-box</c> is.</summary>
    private static readonly string[] OffsetBoxes =
    {
        "content-box", "border-box", "padding-box", "stroke-box", "fill-box", "view-box"
    };

    /// <summary>CSS Motion Path 1 §3.2: <c>none</c>, one of the geometry boxes, a box beside a
    /// shape, or a shape — which the engine prints FIRST, whichever order the page wrote them in
    /// (measured: <c>content-box path("M 0 0")</c> reads <c>path("M 0 0") content-box</c>, and two
    /// boxes are no value at all). The path data itself is echoed command letter, then its numbers,
    /// space separated and with their case kept, because an absolute <c>L</c> and a relative <c>l</c>
    /// are different commands (measured: <c>path("M0 0")</c> reads <c>path("M 0 0")</c> and
    /// <c>path("M 1 2 L 3 4")</c> keeps its <c>L</c>).</summary>
    private static string? NormalizeOffsetPath(string text)
    {
        var tokens = SplitTopLevel(text);
        if (tokens.Count == 0) return null;
        if (tokens.Count == 1 && tokens[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        string? box = null, shape = null;
        foreach (var token in tokens)
        {
            if (box == null && IsOneOf(token, OffsetBoxes)) box = token.ToLowerInvariant();
            else if (shape == null && IsOffsetShape(token, out var canonical)) shape = canonical;
            else return null;
        }
        if (shape == null) return box;
        return box == null ? shape : shape + " " + box;
    }

    /// <summary>Whether one token is a shape <c>offset-path</c> takes, and the text it is
    /// echoed as. <c>path()</c> has its own reprint; <c>url()</c> the quoted spelling the CSSOM
    /// prints; <c>ray()</c> is the page's own data; and the basic shapes have the grammar of
    /// CSS Shapes 1 §4 as the reference engine reads it: a radius that is the shape's own default
    /// is not said, an ellipse takes two radii or none, the offsets of an inset take one to four
    /// lengths and collapse the way a four-sided list does, the box of an xywh takes exactly four
    /// lengths and nothing beside them, and an <c>at</c> clause is a position, which the engine
    /// prints as its two axes (all measured, value by value — see snapshots/_probe_b265_h.html).</summary>
    private static bool IsOffsetShape(string token, out string? canonical)
    {
        canonical = null;
        int open = token.IndexOf('(');
        if (open <= 0 || !token.EndsWith(")", StringComparison.Ordinal)) return false;
        var name = token[..open].ToLowerInvariant();
        var inner = token[(open + 1)..^1].Trim();
        switch (name)
        {
            case "path":
            {
                if (!IsQuotedString(inner)) return false;
                var data = CanonicalPathData(Unquote(inner));
                if (data == null) return false;
                canonical = "path(\"" + data + "\")";
                return true;
            }
            case "ray":
            {
                // The shape position of a ray is not a value this engine reads (measured:
                // 'ray(center 45deg)' is refused while 'ray(45deg contain)' is kept whole).
                var words = inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0
                    || words[0].ToLowerInvariant() is "left" or "center" or "right"
                        or "top" or "bottom") return false;
                canonical = token.ToLowerInvariant();
                return true;
            }
            case "url":
            {
                var address = IsQuotedString(inner) ? Unquote(inner) : inner;
                if (address.Length == 0) return false;
                canonical = "url(\"" + address + "\")";
                return true;
            }
            case "circle":
            case "ellipse":
            {
                if (!TrySplitShapeClause(inner, "at", out var sizes, out var position)) return false;
                // An ellipse is two radii or no radii at all; a circle has one (measured:
                // 'ellipse(10px)' is refused while 'ellipse()' is kept).
                if (sizes.Count > (name == "circle" ? 1 : 2)) return false;
                if (name == "ellipse" && sizes.Count == 1) return false;
                var printed = new List<string>();
                foreach (var size in sizes)
                {
                    if (size.ToLowerInvariant() is "closest-side" or "farthest-side")
                        printed.Add(size.ToLowerInvariant());
                    else if (ShapeLength(size) is { } length) printed.Add(length);
                    else return false;
                }
                // A radius the shape would assume anyway is not said twice: the default of both
                // circle() and ellipse() is 'closest-side', and the engine prints the shape with
                // that radius as the shape without one (measured: 'circle(closest-side at left)'
                // reads 'circle(at left center)', but 'ellipse(closest-side 10px)' keeps both).
                if (printed.Count > 0 && printed.TrueForAll(r => r == "closest-side"))
                    printed.Clear();
                var head = string.Join(" ", printed);
                string? positionText = null;
                if (position != null)
                {
                    positionText = ShorthandExpander.PositionPairText(string.Join(" ", position));
                    if (positionText == null) return false;
                }
                canonical = name + "(" + head
                    + (positionText == null ? "" : (head.Length > 0 ? " at " : "at ") + positionText)
                    + ")";
                return true;
            }
            case "inset":
            {
                if (!TrySplitShapeClause(inner, "round", out var offsets, out var radii))
                    return false;
                if (offsets.Count == 0 || offsets.Count > 4) return false;
                var sides = new List<string>();
                foreach (var offset in offsets)
                {
                    if (ShapeLength(offset) is { } length) sides.Add(length);
                    else return false;
                }
                var text = "inset(" + string.Join(" ", CollapseBox(sides));
                if (radii != null)
                {
                    // The round corners are border radii: one to four of them, each possibly a
                    // horizontal and a vertical radius separated by '/'.
                    var corners = new List<string>();
                    foreach (var corner in radii)
                    {
                        if (corner == "/") { corners.Add("/"); continue; }
                        if (ShapeLength(corner) is { } radius) corners.Add(radius);
                        else return false;
                    }
                    int slash = corners.IndexOf("/");
                    if (slash < 0)
                    {
                        if (corners.Count == 0 || corners.Count > 4) return false;
                        text += " round " + string.Join(" ", CollapseBox(corners));
                    }
                    else
                    {
                        var horizontal = corners[..slash];
                        var vertical = corners[(slash + 1)..];
                        if (horizontal.Count == 0 || vertical.Count == 0
                            || horizontal.Count > 4 || vertical.Count > 4) return false;
                        var first = CollapseBox(horizontal);
                        var second = CollapseBox(vertical);
                        var pair = first.SequenceEqual(second)
                            ? string.Join(" ", first)
                            : string.Join(" ", first) + " / " + string.Join(" ", second);
                        text += " round " + pair;
                    }
                }
                canonical = text + ")";
                return true;
            }
            case "xywh":
            {
                // Four lengths and nothing else: the 'extend' and 'contain' flags of the shapes
                // grammar are no value here (measured: refused).
                var args = SplitTopLevel(inner);
                if (args.Count != 4) return false;
                var printed = new List<string>();
                foreach (var arg in args)
                {
                    if (ShapeLength(arg) is { } length) printed.Add(length);
                    else return false;
                }
                canonical = "xywh(" + string.Join(" ", printed) + ")";
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The tokens of a shape's arguments, split at its one top-level keyword: what comes
    /// before it, and what comes after it — null when the keyword is absent, an empty list when the
    /// page wrote the keyword and nothing after it, which is no shape at all.</summary>
    private static bool TrySplitShapeClause(string inner, string keyword, out List<string> head,
        out List<string>? tail)
    {
        head = new List<string>();
        tail = null;
        var tokens = SplitTopLevel(inner);
        int at = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Equals(keyword, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
        }
        if (at < 0)
        {
            head = tokens;
            return true;
        }
        head = tokens[..at].ToList();
        tail = tokens[(at + 1)..].ToList();
        return tail.Count > 0;
    }

    /// <summary>One &lt;length-percentage&gt; of a shape, printed the way the engine prints a
    /// length: a number the page wrote without a unit is the zero pixels it stands for (measured:
    /// <c>xywh(0 0 10px 10px)</c> reads <c>xywh(0px 0px 10px 10px)</c>), and a math function is
    /// kept whole because folding its constant parts is the same gap every other length has
    /// (#277). Anything else — a keyword, an angle, a name — is not a length.</summary>
    private static string? ShapeLength(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0) return null;
        if (IsCalc(trimmed)) return trimmed;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(trimmed, out var number,
                out var unit)) return null;
        var canonical = Number(number) + (unit.Length == 0 ? "px" : unit.ToLowerInvariant());
        return CssPropertyApplier.IsLengthOrPercentageToken(canonical) ? canonical : null;
    }

    /// <summary>The four-value collapse of a one-to-four-item list (CSS 2.1 §5.10.1, and the same
    /// rule the reference engine applies to an inset's offsets and its round corners): the list is
    /// read as the four sides it means, and said again as the shortest sequence that means those
    /// four (measured: <c>inset(10px 20px 10px)</c> reads <c>inset(10px 20px)</c> and
    /// <c>round 5px 10px 5px 10px</c> reads <c>round 5px 10px</c>).</summary>
    private static List<string> CollapseBox(List<string> values)
    {
        string top = values[0];
        string right = values.Count > 1 ? values[1] : values[0];
        string bottom = values.Count > 2 ? values[2] : values[0];
        string left = values.Count > 3 ? values[3] : right;
        if (bottom == top && left == right) return top == right ? new List<string> { top }
            : new List<string> { top, right };
        if (left == right) return new List<string> { top, right, bottom };
        return new List<string> { top, right, bottom, left };
    }

    /// <summary>The computed text of <c>offset-path</c>: the shape the page wrote, measured in the
    /// box it will be drawn in. A length is resolved against the element's own font, a position
    /// inside the shape becomes the pair of edges and offsets it stands for, and a box given as
    /// <c>xywh()</c> is said as the insets it means (all three measured: <c>circle(2em at left 50%)</c>
    /// computes <c>circle(32px at 0% 50%)</c> and <c>xywh(10px 10px 10px 10px)</c> computes
    /// <c>inset(10px calc(100% - 20px) calc(100% - 20px) 10px)</c>). A path, a ray, a url and a
    /// geometry box are the text they already are.</summary>
    public static string OffsetPathComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (style == null) return canonical;
        var parts = new List<string>();
        foreach (var token in SplitTopLevel(canonical))
        {
            parts.Add(TryShapeFunction(token, out var name, out var inner)
                ? ShapeComputed(name, inner, style) : token);
        }
        return string.Join(" ", parts);
    }

    /// <summary>Whether the token is one of the four basic shapes whose arguments the computed
    /// value has to resolve. A <c>path()</c>, a <c>ray()</c> and a <c>url()</c> are not: their data
    /// is measured by nothing at computed-value time.</summary>
    private static bool TryShapeFunction(string token, out string name, out string inner)
    {
        int open = token.IndexOf('(');
        name = open <= 0 ? string.Empty : token[..open].ToLowerInvariant();
        inner = open <= 0 ? string.Empty : token[(open + 1)..^1].Trim();
        return name is "circle" or "ellipse" or "inset" or "xywh"
            && token.EndsWith(")", StringComparison.Ordinal);
    }

    private static string ShapeComputed(string name, string inner,
        Acrux.Core.Dom.ComputedStyle style)
    {
        switch (name)
        {
            case "circle":
            case "ellipse":
            {
                if (!TrySplitShapeClause(inner, "at", out var sizes, out var position))
                    return name + "(" + inner + ")";
                var parts = new List<string>();
                foreach (var size in sizes) parts.Add(ResolveShapeLength(size, style));
                var text = string.Join(" ", parts);
                if (position != null)
                    text += (text.Length > 0 ? " at " : "at ")
                            + PositionComputed(string.Join(" ", position), style);
                return name + "(" + text + ")";
            }
            case "inset":
            {
                if (!TrySplitShapeClause(inner, "round", out var offsets, out var radii))
                    return "inset(" + inner + ")";
                var sides = new List<string>();
                foreach (var offset in offsets) sides.Add(ResolveShapeLength(offset, style));
                var text = "inset(" + string.Join(" ", CollapseBox(sides));
                if (radii != null)
                {
                    var corners = new List<string>();
                    foreach (var radius in radii)
                        corners.Add(radius == "/" ? "/" : ResolveShapeLength(radius, style));
                    text += " round " + string.Join(" ", corners);
                }
                return text + ")";
            }
            case "xywh":
            {
                var args = SplitTopLevel(inner);
                if (args.Count != 4) return "xywh(" + inner + ")";
                var resolved = new List<string>();
                foreach (var arg in args) resolved.Add(ResolveShapeLength(arg, style));
                if (resolved.Count(r => IsCalc(r)) > 0) return "xywh(" + string.Join(" ", resolved) + ")";
                if (resolved.Count(r => TryEdgeValue(r, out _, out _)) != 4)
                    return "xywh(" + string.Join(" ", resolved) + ")";
                TryEdgeValue(resolved[0], out var xPx, out var xPct);
                TryEdgeValue(resolved[1], out var yPx, out var yPct);
                TryEdgeValue(resolved[2], out var wPx, out var wPct);
                TryEdgeValue(resolved[3], out var hPx, out var hPct);
                // The box is the four insets it leaves: the top and the left are the origin the
                // page wrote, and the right and the bottom are what is left of the reference box
                // once the origin and the size are taken off it.
                return "inset(" + resolved[1] + " " + RemainingInset(xPx, xPct, wPx, wPct) + " "
                     + RemainingInset(yPx, yPct, hPx, hPct) + " " + resolved[0] + ")";
            }
            default:
                return name + "(" + inner + ")";
        }
    }

    /// <summary>One length of a shape, resolved the way the same length is resolved anywhere else
    /// in the computed value: a font unit against this element's font, a percentage kept as the
    /// percentage it is, a math function kept whole.</summary>
    private static string ResolveShapeLength(string token, Acrux.Core.Dom.ComputedStyle style) =>
        ShapeLength(token) is { } length ? LengthPercentageComputed(length, style) : token;

    /// <summary>The number and the unit of a resolved length: <c>32px</c> is 32 pixels and no
    /// percentage, <c>50%</c> is 50 percent and no pixels.</summary>
    private static bool TryEdgeValue(string text, out double px, out double percent)
    {
        px = percent = 0;
        if (text.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out percent);
        return Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(text, out px, out var unit)
            && (unit.Length == 0 || unit.Equals("px", StringComparison.Ordinal));
    }

    /// <summary>What is left of a 100%-wide reference box once one origin and one size are taken
    /// off it. A pure percentage folds to that percentage; a mix of percentages and pixels is the
    /// arithmetic the engine cannot do yet, so it is said as the calculation it is (measured:
    /// <c>xywh(10% 0px 30% 40px)</c> computes <c>inset(0px 60% calc(100% - 40px) 10%)</c>).</summary>
    private static string RemainingInset(double originPx, double originPercent, double sizePx,
        double sizePercent)
    {
        double percent = 100 - originPercent - sizePercent;
        double px = -(originPx + sizePx);
        if (px == 0) return Number(percent) + "%";
        if (percent == 0) return Number(px) + "px";
        return "calc(" + Number(percent) + "% " + (px < 0 ? "- " : "+ ") + Number(Math.Abs(px))
             + "px)";
    }

    /// <summary>The one number printer, shared with every other surface: six significant digits,
    /// the exponent form outside 1e-4 to 1e6 (measured: 'stroke-dashoffset: 0.0000123456789px'
    /// reads '1.23457e-05px', where a plain decimal format kept all the digits and no exponent).</summary>
    private static string Number(double value) =>
        Acrux.Core.Dom.Animations.CssValueTokenizer.Num(value);


    /// <summary>Motion Path 1 §3.3: one <c>&lt;length-percentage&gt;</c>. A bare number is no
    /// length — except zero, which the engine prints with the unit it means, and a number written
    /// in another spelling is echoed in the digits it stands for (measured: <c>offset-distance: 0</c>
    /// reads <c>0px</c> and <c>1e2px</c> reads <c>100px</c>), while <c>auto</c> and <c>normal</c> are
    /// not values of this property at all. A math function is kept whole: folding its constant
    /// parts is the same gap every other length has (#277).</summary>
    private static string? NormalizeOffsetDistance(string text)
    {
        var trimmed = text.Trim();
        // A math function is one value with spaces inside it, so it is read before the length
        // token test, which has no way to see past the first of them.
        if (IsCalc(trimmed)) return SplitTopLevel(trimmed).Count == 1 ? trimmed : null;
        return trimmed.Contains(' ') ? null : ShapeLength(trimmed);
    }

    /// <summary>The computed text of a length the page wrote: resolved against the element's own
    /// font and viewport, with a percentage kept as the percentage it is and a math function kept
    /// whole because part of it cannot be resolved yet (all three measured: <c>2em</c> on a 16px box
    /// is <c>32px</c>, <c>50%</c> stays <c>50%</c> and <c>calc(50% + 10px)</c> stays as written).</summary>
    public static string LengthPercentageComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (style == null || IsCalc(canonical)) return canonical;
        if (!CssPropertyApplier.IsLengthOrPercentageToken(canonical)) return canonical;
        var length = Acrux.Core.Dom.Length.Parse(canonical.ToLowerInvariant());
        return length is Acrux.Core.Dom.AutoLength ? canonical : style.ComputedLengthCss(length);
    }

    private static bool IsCalc(string text) =>
        text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);

    /// <summary>Motion Path 1 §3.5: where the box is placed on the path. <c>auto</c> stands alone —
    /// two of them are no value (measured) — and anything else is a position, which the engine
    /// prints as its two axes with the horizontal one first, the axis the page never named filled
    /// with the middle of the box (measured: <c>bottom right</c> reads <c>right bottom</c> and
    /// <c>top</c> reads <c>center top</c>).</summary>
    private static string? NormalizeOffsetAnchor(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        return ShorthandExpander.PositionPairText(trimmed);
    }

    /// <summary>Motion Path 1 §3.6: where an element with no path starts. <c>normal</c> and
    /// <c>auto</c> stand alone, and a position is printed as the pair of axes it names (measured:
    /// <c>offset-position: none</c> is refused, so the initial stays <c>normal</c>).</summary>
    private static string? NormalizeOffsetPosition(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "normal";
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        return ShorthandExpander.PositionPairText(trimmed);
    }

    /// <summary>The computed text of a position: each axis resolved to the place in the box it
    /// stands for — the keywords are the edges and the middle, a length is itself (measured:
    /// <c>right 50%</c> computes <c>100% 50%</c> and <c>top</c> computes <c>50% 0%</c>).</summary>
    public static string PositionComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical is "auto" or "normal" || style == null) return canonical;
        var words = SplitTopLevel(canonical);
        if (words.Count == 0) return canonical;
        if (!ShorthandExpander.TryMatchBackgroundPosition(words, out var x, out var y))
            return canonical;
        return AxisComputed(x, true, style) + " " + AxisComputed(y, false, style);
    }

    private static string AxisComputed(ShorthandExpander.PositionAxis axis, bool horizontal,
        Acrux.Core.Dom.ComputedStyle style)
    {
        // A math function on an axis is printed as written: its percentage half has no box to be
        // resolved against yet (measured: 'calc(50% + 10px) top' computes 'calc(50% + 10px) 0%').
        if (axis.Present && axis.Edge == null && IsCalc(axis.Offset ?? "")) return axis.Offset!;
        var length = CssPropertyApplier.ResolvePositionAxis(axis, horizontal);
        return length == null ? "center" : style.ComputedLengthCss(length);
    }

    /// <summary>The items of a 'content' value. Whitespace separates them, but two strings written
    /// side by side are still two items — the reference engine reads 'content: "a""b"' as the pair
    /// it prints, '"a" "b"' (measured).</summary>
    public static List<string> SplitContentItems(string text)
    {
        var items = new List<string>();
        foreach (var token in SplitTopLevel(text))
        {
            var strings = RunOfQuotedStrings(token);
            if (strings != null) items.AddRange(strings);
            else items.Add(token);
        }
        return items;
    }

    /// <summary>The quoted strings a token is made of, or null when it is anything else — so
    /// '"a""b"' is two items while '"a" attr(x)' and 'url("a")' stay exactly as they are.</summary>
    private static List<string>? RunOfQuotedStrings(string token)
    {
        var found = new List<string>();
        int i = 0;
        while (i < token.Length)
        {
            char quote = token[i];
            if (quote != '"' && quote != '\'') return null;
            int j = i + 1;
            while (j < token.Length)
            {
                if (token[j] == '\\') { j += 2; continue; }
                if (token[j] == quote) break;
                j++;
            }
            if (j >= token.Length) return null;   // the string never closes
            found.Add(token.Substring(i, j - i + 1));
            i = j + 1;
        }
        return found.Count > 0 ? found : null;
    }

    /// <summary>Whether one token is a shape <c>offset-path</c> takes. The <c>'offset'</c> shorthand
    /// reads its parts one token at a time, and this is the line between the shape and the distance
    /// written beside it.</summary>
    public static bool IsOffsetPathShape(string token) => IsOffsetShape(token, out _);

    /// <summary>Whether one token names a geometry box a path can be measured against.</summary>
    public static bool IsOffsetBoxKeyword(string token) => IsOneOf(token, OffsetBoxes);

    /// <summary>The whitespace-separated tokens of a value, with a function call and a quoted string
    /// each kept as the one token they are.</summary>
    public static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = -1;
        bool inString = false;
        char quote = '"';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                else if (c == quote) inString = false;
                continue;
            }
            if (c is '"' or '\'') { inString = true; quote = c; if (start < 0) start = i; continue; }
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (start >= 0 && i > start) parts.Add(text[start..i]);
                start = -1;
                continue;
            }
            if (start < 0) start = i;
        }
        if (start >= 0 && start < text.Length) parts.Add(text[start..]);
        return parts;
    }

    private static bool IsOneOf(string token, string[] allowed)
    {
        foreach (var a in allowed)
            if (token.Equals(a, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Re-print SVG path data: each command letter on its own, its numbers after it.
    /// A letter the grammar does not know, or a number it cannot read, is no path at all.</summary>
    internal static string? CanonicalPathData(string data)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < data.Length)
        {
            char c = data[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(c) < 0) return null;
            parts.Add(c.ToString());
            i++;
            while (i < data.Length)
            {
                int start = i;
                if (data[i] is '+' or '-' or '.' || char.IsAsciiDigit(data[i]))
                {
                    i++;
                    while (i < data.Length && (char.IsAsciiDigit(data[i]) || data[i] is '.' or 'e' or 'E'
                        || ((data[i] == '+' || data[i] == '-') && (data[i - 1] == 'e' || data[i - 1] == 'E'))))
                        i++;
                    if (!double.TryParse(data[start..i], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var value)) return null;
                    parts.Add(Number(value));
                }
                else if (char.IsWhiteSpace(data[i])) { i++; }
                else break;
            }
        }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>CSS Motion Path 1 §3.4: <c>auto</c>, optionally with an angle, <c>reverse</c>,
    /// optionally with an angle, or a bare angle. The engine prints <c>reverse</c> as the rotation
    /// it stands for (measured), and keeps an angle beside <c>auto</c> or <c>reverse</c> in the
    /// order it was written.</summary>
    private static string? NormalizeOffsetRotate(string text)
    {
        var words = Words(text);
        if (words.Length == 0) return null;
        if (words.Length == 1)
        {
            var one = words[0].ToLowerInvariant();
            if (one == "auto") return "auto";
            if (one == "reverse") return "reverse";
            return IsAngle(one) ? one : null;
        }
        if (words.Length != 2) return null;
        var first = words[0].ToLowerInvariant();
        if (first != "auto" && first != "reverse") return null;
        var angle = words[1].ToLowerInvariant();
        return IsAngle(angle) ? first + " " + angle : null;
    }

    /// <summary>The computed text of <c>offset-rotate</c>: the engine always spells out the rotation
    /// it stands for, so <c>auto</c> becomes <c>auto 0deg</c> and <c>reverse</c> the half turn it
    /// means — with an angle beside it, the turn it adds (all three measured: <c>reverse</c> is
    /// <c>auto 180deg</c> and <c>reverse -45deg</c> is <c>auto 135deg</c>), while an angle the page
    /// wrote beside <c>auto</c> is kept as it is.</summary>
    public static string OffsetRotateComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        var words = SplitTopLevel(canonical);
        if (words.Count == 1)
        {
            if (words[0].Equals("reverse", StringComparison.OrdinalIgnoreCase)) return "auto 180deg";
            if (words[0].Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto 0deg";
            return canonical;
        }
        if (words.Count == 2 && words[0].Equals("reverse", StringComparison.OrdinalIgnoreCase)
            && TryAngleInDegrees(words[1], out var degrees))
            return "auto " + Number(degrees + 180) + "deg";
        return canonical;
    }

    /// <summary>The degrees an angle stands for; a unit the engine does not know is no angle.</summary>
    private static bool TryAngleInDegrees(string token, out double degrees)
    {
        degrees = 0;
        foreach (var (suffix, factor) in new[]
                 {
                     ("deg", 1.0), ("grad", 0.9), ("rad", 180.0 / System.Math.PI), ("turn", 360.0)
                 })
        {
            if (token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(token[..^suffix.Length], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value))
            {
                degrees = value * factor;
                return true;
            }
        }
        return false;
    }

    private static bool IsAngle(string w)
    {
        foreach (var suffix in new[] { "deg", "grad", "rad", "turn" })
            if (w.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(w[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out _)) return true;
        return false;
    }

    /// <summary>Scroll-driven Animations 1 §3: <c>auto</c>, <c>none</c>, a dashed ident naming a
    /// scroll timeline, or one of the timeline functions with its arguments. A bare ident is not a
    /// name — the dashed form is (measured: 't' is refused, '--t' is kept, and 'scroll()' is kept).</summary>
    private static string? NormalizeTimeline(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        foreach (var layer in trimmed.Split(','))
        {
            var one = layer.Trim();
            if (one.Length == 0) return null;
            if (one.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || one.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsDashedIdent(one)) continue;
            int open = one.IndexOf('(');
            if (open <= 0 || !one.EndsWith(")", StringComparison.Ordinal)) return null;
            var function = one[..open].TrimEnd();
            if (!function.Equals("scroll", StringComparison.OrdinalIgnoreCase)
                && !function.Equals("view", StringComparison.OrdinalIgnoreCase)) return null;
        }
        return trimmed.ToLowerInvariant();
    }

    private static string? NormalizeViewTransitionName(string text)
    {
        var words = Words(text);
        if (words.Length != 1) return null;
        var w = words[0];
        if (w.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        // 'auto' is reserved for the engine, and a keyword of another property is no custom name
        // (measured: 'view-transition-name: auto' is refused). A dashed ident is a name an author
        // invented, which is exactly what this property takes (measured: '--a' reads '--a').
        return (IsCustomIdent(w) || IsDashedIdent(w)) && !w.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? w : null;
    }

    private static string? NormalizePositionAnchor(string text)
    {
        var words = Words(text);
        if (words.Length != 1) return null;
        var w = words[0];
        // 'none' and 'auto' are the two words the property says for "no anchor", and the name of
        // one is the dashed custom property 'anchor-name' declared — a plain ident is nobody's
        // anchor name (measured: '--x' and 'none' stand, 'x' leaves no declaration).
        if (w.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || w.Equals("none", StringComparison.OrdinalIgnoreCase)) return w.ToLowerInvariant();
        return IsDashedIdent(w) ? w : null;
    }

    private static string? NormalizeContainerName(string text)
    {
        var words = Words(text);
        if (words.Length == 0 || text.Contains(',')) return null;
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        var parts = new List<string>();
        foreach (var w in words)
        {
            // A container name is an <ident> here: the reference engine keeps a leading dash —
            // '-cards' reads back exactly as written (measured) — while a word that starts with a
            // digit is no ident at all ('1cards' and '1cards b' are refused, measured), and 'none'
            // belongs to the keyword rather than to a name.
            if (!IsContainerNameWord(w) || w.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
            parts.Add(w);
        }
        return string.Join(" ", parts);
    }

    /// <summary>The <c>container</c> shorthand (CSS Containment 3 §3.4): the same name list the
    /// longhand reads, and after one slash the type. Each half is read by the longhand's own
    /// reader, so the shorthand cannot accept a name or a type its longhand would refuse.
    /// Measured: 'x' names a container whose type stays 'normal', 'a b / inline-size' carries
    /// both, while '/ inline-size' (no name), 'x /' (no type), 'x / bogus' and a second slash
    /// are no value at all. The slash is said with a space on each side.</summary>
    private static string? NormalizeContainerShorthand(string text)
    {
        var trimmed = text.Trim();
        int slash = trimmed.IndexOf('/');
        if (slash < 0) return NormalizeContainerName(trimmed);
        if (trimmed.IndexOf('/', slash + 1) >= 0) return null;
        var names = NormalizeContainerName(trimmed[..slash]);
        var type = Find("container-type")?.Normalize(trimmed[(slash + 1)..]);
        if (names == null || type == null) return null;
        // A type of 'normal' says nothing the shorthand did not already say, so it is left out of
        // the echo (measured: 'container: x / normal' reads back 'container: x', and
        // 'container: none / normal' reads back 'container: none').
        return type == "normal" ? names : names + " / " + type;
    }

    /// <summary>One word of a container name list: ident characters, not beginning with a digit,
    /// not a lone dash (which is no name in any spelling — but a one-letter name like 'x' is) and
    /// not a dashed ident, which the reference engine refuses as a container name (measured:
    /// '-cards' is kept, '--cards' is not).</summary>
    private static bool IsContainerNameWord(string w) =>
        w.Length > 0 && w != "-" && !char.IsAsciiDigit(w[0])
        && !w.StartsWith("--", StringComparison.Ordinal) && IsIdentChars(w);

    /// <summary>CSS Fill and Stroke 3 §2: a <c>&lt;paint&gt;</c> is <c>none</c>, <c>currentColor</c>
    /// or a colour. A colour written as a name keeps it, and any other spelling becomes the rgb()
    /// form the engine prints (measured: 'stroke: blue' reads 'blue' while 'fill: #00ff00' reads
    /// 'rgb(0, 255, 0)') — the same split every other colour property in this engine already makes.</summary>
    private static string? NormalizePaint(string text) => NormalizePaintCore(text, noneAllowed: true);

    /// <summary>CSS Fill and Stroke 3 §4: a colour, 'none', 'currentcolor' or an image reference —
    /// and the reference may carry its own fallback, which is a colour or the word 'none' (measured:
    /// 'fill: url(#g)' specifies 'url("#g")' and computes the same, 'fill: url(#g) red' computes the
    /// pair, 'fill: url()' is a reference with nothing in it and still a value, and a second url is
    /// no fallback at all ('fill: url(#g) url(#h)' is dropped).</summary>
    private static string? NormalizePaintCore(string text, bool noneAllowed)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            return noneAllowed ? "none" : null;
        if (trimmed.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) return "currentcolor";
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return NormalizeColorOnly(trimmed);
        int open = trimmed.IndexOf('(');
        int close = MatchingParen(trimmed, open);
        if (close < 0) return null;
        // The address is the whole argument, quoted or not, and an empty one is an address the
        // engine still prints ('url( "" )' is 'url("")', measured).
        var inner = trimmed[(open + 1)..close].Trim();
        var address = IsQuotedString(inner) ? Unquote(inner) : inner;
        var painted = "url(\"" + address + "\")";
        var after = trimmed[(close + 1)..].Trim();
        if (after.Length == 0) return painted;
        if (after.Equals("none", StringComparison.OrdinalIgnoreCase) && noneAllowed)
            return painted + " none";
        // A fallback is one &lt;paint&gt; of its own and nothing else: a second image, a third
        // component or a word the colour parser cannot read all leave the declaration out.
        if (after.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return null;
        return NormalizeColorOnly(after) is { } fallback ? painted + " " + fallback : null;
    }

    /// <summary>One colour in the spelling the reference engine prints it in. 'stop-color',
    /// 'flood-color' and 'lighting-color' take a colour and nothing else — 'none' is no colour and
    /// each of them is dropped for it (measured).</summary>
    private static string? NormalizeColorOnly(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) return "currentcolor";
        if (!Acrux.Core.Css.ColorParser.IsColorToken(trimmed)) return null;
        bool isName = char.IsAsciiLetter(trimmed[0])
            && !trimmed.Contains('(') && !trimmed.Contains('#');
        if (isName) return trimmed.ToLowerInvariant();
        var color = Acrux.Core.Css.ColorParser.Parse(trimmed);
        return Acrux.Core.Dom.Animations.ComputedValueSerializer.ColorText(color);
    }

    /// <summary>The computed <c>&lt;paint&gt;</c>: 'currentColor' becomes the element's own colour,
    /// which is the one place the keyword never survives, and a named colour becomes the rgb() triple
    /// it stands for (measured: 'fill: red' specifies 'red' and computes 'rgb(255, 0, 0)'). A
    /// reference keeps its own address with its fallback printed beside it, resolved like the paint
    /// standing behind it (measured: 'stroke: url(#g) currentcolor' computes
    /// <c>url("#g") rgb(9, 9, 9)</c>), and an address with nothing in it is never made absolute
    /// ('fill: url()' computes <c>url("")</c>).</summary>
    public static string PaintComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            return style == null ? "rgb(0, 0, 0)"
                : Acrux.Core.Dom.Animations.ComputedValueSerializer.ColorText(style.Color);
        if (canonical.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (canonical.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int open = canonical.IndexOf('(');
            int close = MatchingParen(canonical, open);
            if (close < 0) return canonical;
            var painted = canonical[..(close + 1)];
            var address = painted[(open + 1)..close].Trim();
            var printed = address.Length == 0
                ? "url(\"\")"
                : Acrux.Core.Css.ImageValueCanonicalizer.Canonicalize(painted);
            if (string.IsNullOrWhiteSpace(printed)) printed = painted;
            var after = canonical[(close + 1)..].Trim();
            return after.Length == 0 ? printed : printed + " " + PaintComputed(after, style);
        }
        if (!canonical.Contains('('))
            return Acrux.Core.Dom.Animations.ComputedValueSerializer
                .ColorText(Acrux.Core.Css.ColorParser.Parse(canonical));
        return canonical;
    }

    /// <summary>A <c>&lt;number&gt;</c> or <c>&lt;percentage&gt;</c>, echoed in the digits it stands
    /// for. The opacity properties take one; a negative or a non-number is no value, and a math
    /// expression has to name one type: an &lt;alpha-value&gt; is a number OR a percentage and never
    /// a sum of the two (measured: 'fill-opacity: calc(50% + 0)' and 'calc(1px)' are each dropped
    /// while 'calc(100% * 0.5)' stands).</summary>
    private static string? NormalizeOpacityValue(string text)
    {
        var trimmed = text.Trim();
        // A percentage is the fraction it stands for and is printed as one (measured:
        // 'fill-opacity: 50%' specifies '0.5'); a negative number is still a value, and the
        // computed surface is what clamps it.
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var percent) ? Number(percent / 100d) : null;
        if (Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(trimmed))
            return MathNamesOneOf(trimmed, "number", "percentage") ? trimmed : null;
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Number(number) : null;
    }

    /// <summary>The computed opacity: whatever the page wrote, kept inside [0, 1] (measured:
    /// 'fill-opacity: -1' computes '0', '2' computes '1' and 'calc(0.5)' computes '0.5').</summary>
    public static string OpacityComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        var text = canonical;
        if (Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(text))
            text = Acrux.Core.Css.CssFunctionEvaluator.EvaluateConstantNumber(text);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return canonical;
        return Number(Math.Clamp(value, 0d, 1d));
    }

    /// <summary>A <c>&lt;number&gt;</c> that is not less than nothing, which is what
    /// 'stroke-miterlimit' measures — and a math expression of numbers is one of them, because the
    /// limit is a number whatever spelling carries it (measured: 'calc(2 + 2)' specifies
    /// 'calc(4)' and computes '4', while 'calc(4px)' is a length and is dropped).</summary>
    private static string? NormalizePositiveNumber(string text)
    {
        var trimmed = text.Trim();
        if (Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(trimmed))
            return MathNamesOneOf(trimmed, "number") ? trimmed : null;
        // 'stroke-miterlimit: 0' is a value the reference engine keeps (measured) — the limit is
        // non-negative rather than positive — while a negative one is dropped.
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number >= 0 ? Number(number) : null;
    }

    /// <summary>The number a math expression means, or null when it is not a constant one.</summary>
    private static string MathNumber(string text)
        => Acrux.Core.Css.CssFunctionEvaluator.EvaluateConstantNumber(
            Acrux.Core.Css.CssMathValue.Fold(text));

    /// <summary>Whether a math expression names a type of its own and that type is one of the words
    /// given. An expression carrying a variable names no type yet and is left to the property's own
    /// reading; an expression that mixes a percentage with a number, or a length with a number,
    /// names nothing at all and is refused everywhere.</summary>
    private static bool MathNamesOneOf(string text, params string[] types)
        => Acrux.Core.Css.CssMathValue.WellTyped(text, out var category)
            && (category == null || System.Array.IndexOf(types, category) >= 0);

    /// <summary>CSS Fill and Stroke 3 §4.5: a <c>&lt;length&gt;</c>, a <c>&lt;percentage&gt;</c> or a
    /// bare number, which is a number of pixels. The specified value keeps the spelling the page
    /// wrote (measured: 'stroke-width: 2' reads '2'), so only the digits are normalised.</summary>
    /// <summary>A &lt;length&gt;, &lt;percentage&gt; or bare number that is not less than nothing:
    /// 'stroke-width' measures a band and -1px is no band (measured: it is dropped), while
    /// 'stroke-dashoffset: -2px' stands because an offset may run backwards.</summary>
    private static string? NormalizeNonNegativeSvgLength(string text) =>
        NormalizeSvgLengthCore(text, negativeAllowed: false);

    private static string? NormalizeSvgLength(string text) => NormalizeSvgLengthCore(text, true);

    private static string? NormalizeSvgLengthCore(string text, bool negativeAllowed)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("-", StringComparison.Ordinal) && !negativeAllowed) return null;
        // A math expression is a value only when it names one type: an SVG length is a length, a
        // percentage or a number, and a sum that mixes them names none of those (measured:
        // 'stroke-width: calc(2px + 3)' and 'min(2, 3px)' are each dropped while 'calc(2)' stands).
        // The specified value keeps the expression as it folds to, and the computed surface prices
        // it with the unit it will get.
        if (Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(trimmed))
            return MathNamesOneOf(trimmed, "length", "percentage", "number") ? trimmed : null;
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
            return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out var percent) ? Number(percent) + "%" : null;
        if (Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(trimmed, out var number,
                out var unit))
        {
            return unit.Length == 0 ? Number(number)
                : Number(number) + unit.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>The computed length: a bare number is the pixel count it means, a length is priced
    /// against the element's own font, and a percentage is left alone (measured: '2' computes '2px'
    /// and '50%' computes '50%').</summary>
    public static string SvgLengthComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(canonical))
            return MathLengthComputed(canonical, style);
        if (canonical.EndsWith("%", StringComparison.Ordinal) || style == null) return canonical;
        if (!Acrux.Core.Dom.Animations.CssValueTokenizer.TrySplitUnit(canonical, out var number,
                out var unit)) return canonical;
        if (unit.Length == 0) return Number(number) + "px";
        var length = Acrux.Core.Dom.Length.Parse(canonical.ToLowerInvariant());
        return length is Acrux.Core.Dom.AutoLength ? canonical : style.ComputedLengthCss(length);
    }

    /// <summary>The computed value of a math expression that stands for an SVG length. The type the
    /// expression names decides the spelling: a percentage prints the share it means with no wrapper
    /// around it, a number keeps the expression and gains the unit it stands for, a length is priced
    /// the way any other length is — with a percentage it cannot price left inside the expression —
    /// and a zero, which names no type of its own, prints as the percentage it could be (all
    /// measured: 'calc(50%)' computes '50%', 'calc(2)' computes 'calc(2px)', 'calc(1rem)' computes
    /// '16px', 'calc(50% + 1em)' computes 'calc(50% + 14px)' and 'calc(0)' computes '0%').</summary>
    private static string MathLengthComputed(string text, Acrux.Core.Dom.ComputedStyle? style)
    {
        var folded = Acrux.Core.Css.CssMathValue.Fold(text);
        // A folded expression of one term can lose its wrapper, and the spelling the computed
        // surface wants is a calc() of that term, so it is put back before anything is decided.
        if (!Acrux.Core.Css.CssFunctionEvaluator.IsMathFunctionText(folded))
            folded = "calc(" + folded + ")";
        int open = folded.IndexOf('(');
        var inner = folded[(open + 1)..^1];
        var terms = SplitMathTerms(inner);
        bool anyPercent = terms.Exists(t => t.Text.EndsWith("%", StringComparison.Ordinal));
        bool anyUnit = terms.Exists(t => HasUnit(t.Text));
        // One share of a box the computed surface has not been given: the wrapper carried no
        // arithmetic, so it goes and the percentage answers on its own.
        if (terms.Count == 1 && IsPlainPercentage(terms[0].Text)) return terms[0].Text;
        // Nothing but bare numbers: the expression keeps its wrapper and every number in it gains
        // the pixel unit it stands for — except a zero, which names no type at all and is printed
        // as the percentage it could have been.
        if (!anyPercent && !anyUnit)
        {
            return double.TryParse(MathNumber(folded), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var value) && value == 0
                ? "0%"
                : Acrux.Core.Css.CssFunctionEvaluator.NumberlessMathToPixels(folded);
        }
        if (style == null || !anyPercent)
        {
            if (anyPercent) return folded;
            var length = Acrux.Core.Dom.Length.Parse(folded.ToLowerInvariant());
            return length is null or Acrux.Core.Dom.AutoLength ? folded : style.ComputedLengthCss(length);
        }
        // A percentage with no box to price it against stays in the expression, and every term the
        // element can price on its own is priced beside it.
        var parts = new List<string>(terms.Count);
        foreach (var term in terms)
        {
            var printed = term.Text.EndsWith("%", StringComparison.Ordinal)
                ? term.Text
                : PriceMathTerm(term.Text, style);
            parts.Add(term.Op.Length == 0 ? printed : term.Op + " " + printed);
        }
        return folded[..(open + 1)] + string.Join(" ", parts) + ")";
    }

    /// <summary>The term of an expression priced as the length it is, or left as written when it is
    /// not one the element can price.</summary>
    private static string PriceMathTerm(string term, Acrux.Core.Dom.ComputedStyle style)
    {
        var length = Acrux.Core.Dom.Length.Parse(term.ToLowerInvariant());
        return length is null or Acrux.Core.Dom.AutoLength ? term : style.ComputedLengthCss(length);
    }

    /// <summary>Whether a term is one percentage and nothing else — the only case where a wrapper
    /// around it says less than the term does ('min(50%, 100%)' is a list, not a share).</summary>
    private static bool IsPlainPercentage(string term)
    {
        if (!term.EndsWith("%", StringComparison.Ordinal)) return false;
        return double.TryParse(term[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>The terms of a folded expression's sum, each with the operator that joins it to the
    /// term before. Only a top-level '+' or '-' divides them: the arguments of a 'min()' are a
    /// choice rather than an addition, an exponent's sign belongs to its number, and a leading sign
    /// belongs to the term it stands in front of.</summary>
    private static List<(string Op, string Text)> SplitMathTerms(string inner)
    {
        var terms = new List<(string Op, string Text)>();
        int depth = 0;
        var current = new System.Text.StringBuilder();
        string op = "";
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            bool splits = depth == 0 && (c == '+' || c == '-') && current.Length > 0;
            if (splits)
            {
                // '1e-3' is one number, so a sign that follows the 'e' of an exponent is not an
                // operator; and a sign right after another operator is the term's own.
                char previous = LastNonSpace(current);
                if (previous == 'e' || previous == 'E' || previous is '+' or '-' or '*' or '/')
                    splits = false;
            }
            if (splits)
            {
                var text = current.ToString().Trim();
                if (text.Length > 0) terms.Add((op, text));
                op = c.ToString();
                current = new System.Text.StringBuilder();
                continue;
            }
            current.Append(c);
        }
        var last = current.ToString().Trim();
        if (last.Length > 0) terms.Add((op, last));
        return terms;
    }

    private static char LastNonSpace(System.Text.StringBuilder builder)
    {
        for (int i = builder.Length - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(builder[i])) return builder[i];
        return '\0';
    }

    /// <summary>Whether a term carries a unit of its own — a length written in 'px', an 'em', a
    /// 'vw' — as opposed to being a bare number or a percentage.</summary>
    private static bool HasUnit(string term)
    {
        foreach (var c in term)
            if (char.IsLetter(c)) return true;
        return false;
    }

    /// <summary>CSS Fill and Stroke 3 §5.5: 'none' or a list of lengths, numbers and percentages.
    /// The page may separate the parts with commas, with spaces or with both, and the reference
    /// engine always prints them comma-separated (measured: '5 3' and '5,3' both read '5, 3').</summary>
    private static string? NormalizeDashArray(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        var parts = trimmed.Split(new[] { ',', ' ', '\t' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var printed = new List<string>();
        foreach (var part in parts)
        {
            if (NormalizeNonNegativeSvgLength(part) is not { } one) return null;
            printed.Add(one);
        }
        return string.Join(", ", printed);
    }

    /// <summary>The computed dash list: every part priced the way 'stroke-width' is.</summary>
    public static string DashArrayComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        var parts = canonical.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.TrimEntries
            | StringSplitOptions.RemoveEmptyEntries);
        var printed = new List<string>();
        foreach (var part in parts) printed.Add(SvgLengthComputed(part, style));
        return string.Join(", ", printed);
    }

    /// <summary>SVG 2 §11.3: a marker is <c>none</c> or one <c>url()</c>. A bare ident names no
    /// image, so 'marker-start: bogus' is refused (measured).</summary>
    /// <summary>CSS View Transitions 1 §7: the class list a snapshot element belongs to. 'none'
    /// alone, or one or more custom idents, each kept in the author's case (measured: 'foo' reads
    /// back 'foo').</summary>
    private static string? NormalizeBaselineShift(string text)
    {
        var trimmed = text.Trim();
        if (IsOneOfWord(trimmed, "baseline", "sub", "super")) return trimmed.ToLowerInvariant();
        return NormalizeSvgLength(trimmed);
    }

    private static string BaselineShiftComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style) =>
        canonical.Equals("baseline", StringComparison.OrdinalIgnoreCase)
            ? "0px"
            // A length, a percentage or a number written as one of those: priced the way the
            // SVG lengths are, because it is the same kind of value (measured: '1em' of a 14px box
            // computes '14px', '0' computes '0px', '3%' stays '3%' and 'calc(2)' computes
            // 'calc(2px)').
            : SvgLengthComputed(canonical, style);

    /// <summary>CSS View Transitions 1 §6: how a snapshot group is sized. The three words the
    /// property names, the two the shorthand answers with, a dashed ident beside them — and any
    /// other ident, because the value is a name the page gives a group of its own
    /// (measured: 'unknown-ident' and '--a' each read back as written while '1px' and the pair
    /// 'normal contain' are dropped).</summary>
    private static string? NormalizeViewTransitionGroup(string text)
    {
        var trimmed = text.Trim();
        if (IsOneOfWord(trimmed, "normal", "contain", "allow-discrete", "none", "auto"))
            return trimmed.ToLowerInvariant();
        if (IsDashedIdent(trimmed)) return trimmed;
        return IsIdentifierText(trimmed) ? trimmed.ToLowerInvariant() : null;
    }

    /// <summary>A CSS &lt;ident&gt;: it does not begin with a digit, a sign or a dot, and every
    /// character of it is a name character. '1px' and '50%' are numbers, and 'unknown-ident' is a
    /// name the page gave a group of its own (both measured).</summary>
    private static bool IsIdentifierText(string token)
    {
        if (token.Length == 0) return false;
        if (char.IsAsciiDigit(token[0]) || token[0] is '-' or '+' or '.' or '%' or '(') return false;
        foreach (var c in token)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.' && c <= '\u007F')
                return false;
        return true;
    }

    private static bool IsOneOfWord(string text, params string[] words)
    {
        foreach (var word in words)
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? NormalizeViewTransitionClass(string text)
    {
        var words = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        if (words.Length == 1 && words[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";
        var printed = new List<string>(words.Length);
        foreach (var word in words)
        {
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
            // A class of a snapshot element is a custom ident, and the dashed spelling a page
            // uses for its own names is one of those (measured: '--a' reads '--a', '1' is dropped).
            if (!Acrux.Core.Css.CssValueGrammar.IsCustomIdent(word) && !IsDashedIdent(word))
                return null;
            printed.Add(word);
        }
        return string.Join(" ", printed);
    }

    /// <summary>CSS Paged Media 3 §3.5: the named page a box asks for, or 'auto'. A custom ident is
    /// the author's own case and a string is no name (measured: 'page: "a"' falls, 'page: ident'
    /// stands and reads back as it was written).</summary>
    private static string? NormalizePageName(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        // A page box is a name of its own, and a name the author invented with '--' is one of
        // those too (measured: 'page: --a' reads '--a' while 'page: "a"' and 'page: 1' are dropped).
        return Acrux.Core.Css.CssValueGrammar.IsCustomIdent(trimmed) || IsDashedIdent(trimmed)
            ? trimmed : null;
    }

    public static string? NormalizeMarker(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith(")", StringComparison.Ordinal)) return null;
        var inner = trimmed[4..^1].Trim();
        var address = IsQuotedString(inner) ? Unquote(inner) : inner;
        return address.Length == 0 ? null : "url(\"" + address + "\")";
    }

    /// <summary>The computed marker: a relative address becomes the absolute one the image is
    /// looked up by, and a fragment reference is already complete.</summary>
    public static string MarkerComputed(string canonical, Acrux.Core.Dom.ComputedStyle? style)
    {
        if (canonical.Equals("none", StringComparison.OrdinalIgnoreCase)) return canonical;
        var printed = Acrux.Core.Css.ImageValueCanonicalizer.Canonicalize(canonical);
        return string.IsNullOrWhiteSpace(printed) ? canonical : printed;
    }

    // ===== the small token tests every entry above leans on =====
    private static bool IsQuotedString(string text) => text.Length >= 2
        && (text[0] == '"' || text[0] == '\'') && text[text.Length - 1] == text[0];

    private static string Unquote(string text) => text[1..^1];

    /// <summary>The index of the ')' that closes the '(' at <paramref name="open"/>, quotes skipped
    /// so a parenthesis inside an address cannot end the function early, or -1 when the argument
    /// list never closes.</summary>
    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                for (i++; i < text.Length && text[i] != c; i++)
                    if (text[i] == '\\') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static bool IsDashedIdent(string w) => w.Length > 2 && w[0] == '-' && w[1] == '-'
        && IsIdentChars(w[2..]);

    private static bool IsCustomIdent(string w) => w.Length > 0 && w[0] != '-' && !char.IsAsciiDigit(w[0]) && IsIdentChars(w);

    private static bool IsIdentChars(string w)
    {
        foreach (var c in w)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '\\')) return false;
        return w.Length > 0;
    }
}
