using SkiaSharp;
using Acrux.Core.Dom;

namespace Acrux.Core.Css.Resolver;

/// <summary>
/// Per-property metadata used to implement the CSS-wide keywords
/// (inherit / initial / unset / revert). The active cascade is a string switch,
/// so this table carries, for each recognized property:
///   - whether the property inherits by default
///   - a way to restore its initial value and to copy a parent's value
/// Property names are lowercase, matching the cascade map keys.
/// </summary>
public static class CssPropertyTraits
{
    private static readonly HashSet<string> Inherited = new(StringComparer.Ordinal)
    {
        "accent-color", "border-collapse", "border-spacing", "caption-side", "caret-color",
        "color", "cursor", "direction", "empty-cells", "font", "font-family", "font-feature-settings",
        "font-kerning", "font-optical-sizing", "font-size", "font-size-adjust", "font-stretch",
        "font-style", "font-synthesis", "font-variant", "font-variant-caps", "font-variation-settings", "font-weight",
        "hyphens", "hyphenate-character", "image-rendering", "letter-spacing", "line-break", "line-height", "list-style",
        "list-style-image", "list-style-position", "list-style-type", "orphans", "pointer-events",
        "quotes", "tab-size", "text-align", "text-align-last", "text-indent", "text-justify",
        "text-rendering", "text-shadow", "text-transform",
        "visibility", "white-space", "white-space-collapse", "text-wrap-mode", "text-wrap-style", "widows", "word-break",
        // CSS Text Security: the mask runs down the tree, so a child's text is masked with its
        // parent's character (measured: 'getComputedStyle(child)["-webkit-text-security"]' is the
        // parent's 'disc' and the child's run is bullets).
        "text-security",
        "word-spacing", "writing-mode", "overflow-wrap", "color-scheme", "ruby-position",
        "text-emphasis", "text-emphasis-color", "text-emphasis-style", "text-emphasis-position",
    };

    /// <summary>True when the property inherits its value by default. The echo-only properties
    /// carry their own answer in the table that prints them, so the list above is not the last
    /// word — 'font-language-override' and the SVG keyword family inherit from there.</summary>
    public static bool IsInherited(string property) =>
        Inherited.Contains(property)
        || Acrux.Core.Css.Resolver.EchoedProperties.Find(property)?.Inherited == true;

    /// <summary>
    /// Properties whose value is (or contains) a length, so that a unitless zero is really a
    /// length of nothing and a reference engine prints it with the unit: measured,
    /// <c>margin: 0 auto</c> reads back as <c>0px auto</c>, <c>border: 0 solid red</c> as
    /// <c>0px solid red</c>, <c>background-position: 0</c> as <c>0px</c>. The properties this
    /// table does not name keep the author's characters, which is what makes the list safe to
    /// maintain: <c>line-height: 0</c>, <c>opacity: 0</c>, <c>flex: 0 1 auto</c>,
    /// <c>order</c>, <c>z-index</c>, <c>tab-size</c>, <c>scale</c> and SVG's
    /// <c>stroke-width</c> all keep the bare number in the reference engine too, so leaving
    /// them out of a length table is the same decision as putting them in a 'never add a unit'
    /// table, but a length property added later cannot pick up a unit it does not take.
    /// </summary>
    private static readonly HashSet<string> LengthValued = new(StringComparer.Ordinal)
    {
        // Sizes and the box axes.
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "inline-size", "block-size", "min-inline-size", "min-block-size",
        "max-inline-size", "max-block-size", "flex-basis", "columns", "column-width",
        "contain-intrinsic-size", "contain-intrinsic-width", "contain-intrinsic-height",
        // Margins, paddings, insets and the logical spellings of both.
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "margin-inline", "margin-inline-start", "margin-inline-end",
        "margin-block", "margin-block-start", "margin-block-end",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "padding-inline", "padding-inline-start", "padding-inline-end",
        "padding-block", "padding-block-start", "padding-block-end",
        "inset", "inset-inline", "inset-inline-start", "inset-inline-end",
        "inset-block", "inset-block-start", "inset-block-end",
        "top", "right", "bottom", "left",
        "scroll-margin", "scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom",
        "scroll-margin-left", "scroll-margin-inline", "scroll-margin-inline-start",
        "scroll-margin-inline-end", "scroll-margin-block", "scroll-margin-block-start",
        "scroll-margin-block-end", "scroll-padding", "scroll-padding-top",
        "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left",
        "scroll-padding-inline", "scroll-padding-inline-start", "scroll-padding-inline-end",
        "scroll-padding-block", "scroll-padding-block-start", "scroll-padding-block-end",
        // Borders, outlines and corners.
        "border", "border-top", "border-right", "border-bottom", "border-left",
        "border-width", "border-top-width", "border-right-width", "border-bottom-width",
        "border-left-width", "border-inline", "border-inline-width",
        "border-inline-start", "border-inline-end", "border-inline-start-width",
        "border-inline-end-width", "border-block", "border-block-width", "border-block-start",
        "border-block-end", "border-block-start-width", "border-block-end-width",
        "outline", "outline-width", "outline-offset", "column-rule", "column-rule-width",
        "border-spacing",
        "border-radius", "border-top-left-radius", "border-top-right-radius",
        "border-bottom-right-radius", "border-bottom-left-radius",
        "border-start-start-radius", "border-start-end-radius",
        "border-end-start-radius", "border-end-end-radius",
        // Positioning within a box.
        "background-position", "background-position-x", "background-position-y",
        "object-position", "mask-position", "mask-position-x", "mask-position-y",
        "transform-origin", "translate", "perspective",
        // Text metrics.
        "font", "font-size", "letter-spacing", "word-spacing", "text-indent",
        "text-decoration-thickness", "text-underline-offset", "text-emphasis-offset",
        // Shadows and the lists that carry offsets.
        "box-shadow", "text-shadow", "filter", "backdrop-filter", "background", "mask",
        // Tracks, gaps and shapes.
        "gap", "row-gap", "column-gap", "grid-gap", "grid-row-gap", "grid-column-gap",
        "grid-template-columns", "grid-template-rows", "grid-auto-columns", "grid-auto-rows",
        "shape-margin", "overflow-clip-margin",
        // The legacy stroke width is a length and prints a unitless zero as '0px' (measured).
        "-webkit-text-stroke-width",
    };

    /// <summary>True when a unitless zero in this property's value is a length.</summary>
    public static bool TakesLength(string property) => LengthValued.Contains(property);

    /// <summary>
    /// The properties whose value is a colour — the longhands that hold one and the shorthands
    /// whose grammar carries it — and which therefore print it in the reference engine's form
    /// (a hex or an <c>hsl()</c> as <c>rgb()</c>, a named colour lower-cased). Measured per name
    /// against the reference engine's own CSSOM output.
    /// </summary>
    private static readonly HashSet<string> ColorValued = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "background-color", "caret-color", "accent-color", "outline-color",
        "column-rule-color", "text-decoration-color", "text-emphasis-color",
        "border-color", "border-top-color", "border-right-color", "border-bottom-color",
        "border-left-color", "border-block-color", "border-block-start-color",
        "border-block-end-color", "border-inline-color", "border-inline-start-color",
        "border-inline-end-color", "border-top", "border-right", "border-bottom", "border-left",
        "border-block", "border-block-start", "border-block-end", "border-inline",
        "border-inline-start", "border-inline-end", "border", "outline",
        "-webkit-text-fill-color", "-webkit-text-stroke-color", "-webkit-tap-highlight-color",
        "-webkit-text-stroke", "background", "box-shadow", "text-shadow",
        // The reference engine prints a shadow list with its colour first, which this engine does
        // not reorder yet; the colour itself is still folded to the same form.
    };

    public static bool TakesColorValue(string property) =>
        !string.IsNullOrEmpty(property) && ColorValued.Contains(property);

    /// <summary>Whether a property name is one the reference engine declares: an unprefixed name
    /// from the property table that is not one of this engine's private ids, a custom property, or
    /// a <c>-webkit-</c> spelling of the names in <see cref="WebkitAliases"/>. The cross-product
    /// alias registration in <c>CssPropertyIdExtensions</c> exists so a page written against a
    /// prefix keeps working when the declaration is applied, but it must not be what
    /// <c>@supports</c> or <c>CSS.supports</c> answers from — measured over the whole 447-name table
    /// (snapshots/out/_b256_edge_props.txt), the reference engine refuses every <c>-moz-</c>,
    /// <c>-ms-</c> and <c>-o-</c> spelling, refuses <c>-webkit-color</c>, <c>-webkit-hyphens</c>,
    /// <c>-webkit-scrollbar</c> and <c>-webkit-box-reflect</c>, and accepts names no rule about
    /// prefixes would predict (<c>-webkit-text-security</c>, <c>-webkit-line-clamp</c>,
    /// <c>-webkit-user-modify</c>). The <c>--</c> test is a prefix test, not a lookup: a custom
    /// property is any name that starts with two dashes, and the reference engine accepts one
    /// whatever follows them.</summary>
    public static bool IsDeclaredProperty(string property)
    {
        if (string.IsNullOrEmpty(property)) return false;
        if (property.StartsWith("--", StringComparison.Ordinal)) return true;
        string name = property.ToLowerInvariant();
        foreach (var prefix in OtherVendorPrefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (name.StartsWith("-webkit-", StringComparison.Ordinal))
            // Either an alias of an unprefixed property this table knows, or one of the legacy
            // spellings that are properties of their own — the set behind the legacy value grammars
            // and the declared-only table, both measured name by name.
            return WebkitAliases.Contains(name["-webkit-".Length..])
                || LegacyDeclaredOnly.ContainsKey(name)
                || Css.CssValueGrammar.IsLegacyPrefixedName(name);
        if (EngineOnlyNames.Contains(name)) return false;
        // A property the engine echoes from its own table is a declared property even where this
        // engine has no id for it ('font-language-override' is the one such name — the reference
        // engine takes it, and a gate that consulted only the id table would drop the declaration).
        if (Acrux.Core.Css.Resolver.EchoedProperties.Find(name) != null) return true;
        return IsKnown(name) || ReferenceDeclared.Contains(name);
    }

    private static readonly string[] OtherVendorPrefixes = { "-moz-", "-ms-", "-o-" };

    /// <summary>
    /// The legacy <c>-webkit-</c> properties the reference engine still declares, still validates
    /// and still answers a computed value for, and that this engine has no behaviour for at all.
    /// Each entry is (the value an element with no declaration reports, whether that value runs
    /// down the tree) — both measured per name over 22 properties and 100 values
    /// (snapshots/out/_b257_edge_legacy_grammar.txt). The inherited group is the text paint pair,
    /// the font smoothing, the tap highlight and the edit mode; the old flexbox and the mask border
    /// are per-box.
    /// </summary>
    private static readonly Dictionary<string, (string Initial, bool Inherited)> LegacyDeclaredOnly =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["-webkit-box-align"] = ("stretch", false),
        ["-webkit-box-direction"] = ("normal", false),
        ["-webkit-box-flex"] = ("0", false),
        ["-webkit-box-ordinal-group"] = ("1", false),
        ["-webkit-box-orient"] = ("horizontal", false),
        ["-webkit-box-pack"] = ("start", false),
        ["-webkit-box-reflect"] = ("none", false),
        ["-webkit-border-horizontal-spacing"] = ("0px", true),
        ["-webkit-border-vertical-spacing"] = ("0px", true),
        ["-webkit-font-smoothing"] = ("auto", true),
        ["-webkit-mask-box-image-outset"] = ("0", false),
        ["-webkit-mask-box-image-repeat"] = ("stretch", false),
        ["-webkit-mask-box-image-slice"] = ("0 fill", false),
        ["-webkit-mask-box-image-source"] = ("none", false),
        ["-webkit-mask-box-image-width"] = ("auto", false),
        ["-webkit-tap-highlight-color"] = ("rgba(0, 0, 0, 0.18)", true),
        ["-webkit-text-decorations-in-effect"] = ("none", false),
        ["-webkit-text-fill-color"] = ("rgb(0, 0, 0)", true),
        ["-webkit-text-stroke"] = ("0px rgb(0, 0, 0)", true),
        ["-webkit-text-stroke-color"] = ("rgb(0, 0, 0)", true),
        ["-webkit-text-stroke-width"] = ("0px", true),
        ["-webkit-user-drag"] = ("auto", false),
        ["-webkit-user-modify"] = ("read-only", true),
    };

    /// <summary>Whether |name| is one of the legacy properties whose only surface is the computed
    /// value, and what that value is when the page wrote nothing.</summary>
    public static bool IsLegacyDeclaredOnly(string name) =>
        !string.IsNullOrEmpty(name) && LegacyDeclaredOnly.ContainsKey(name);

    public static string? LegacyDeclaredOnlyInitial(string name) =>
        LegacyDeclaredOnly.TryGetValue(name, out var entry) ? entry.Initial : null;

    /// <summary>The legacy properties whose computed value is the initial however the page wrote
    /// them. Measured over one: <c>-webkit-text-decorations-in-effect</c> echoes 'underline' in the
    /// CSSOM, refuses 'glow', and still answers 'none' from a computed read for every value it
    /// takes. The declaration is real; only the computed surface ignores it.</summary>
    private static readonly HashSet<string> LegacyComputedStaysInitial =
        new(StringComparer.OrdinalIgnoreCase) { "-webkit-text-decorations-in-effect" };

    public static bool IsLegacyComputedInitial(string name) =>
        !string.IsNullOrEmpty(name) && LegacyComputedStaysInitial.Contains(name);

    /// <summary>The computed text of a legacy property whose computed form prints more than the
    /// author wrote. <c>-webkit-box-reflect</c> names its mask image even when the page left it out
    /// (measured: 'below' echoes as 'below 0px' and computes as 'below 0px none'); the full mask
    /// expansion — 'above 5px url(a.png)' with its slice, origin and repeat — is still open (#259).</summary>
    public static string LegacyComputedText(string name, string declared)
    {
        if (!name.Equals("-webkit-box-reflect", StringComparison.OrdinalIgnoreCase)) return declared;
        var words = declared.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        bool direction = words.Length > 0 && words[0] is "above" or "below" or "left" or "right";
        return direction && words.Length is >= 1 and <= 2 ? declared + " none" : declared;
    }

    /// <summary>The legacy properties that run down the tree (CSS Text Decorations and the old box
    /// model's paint pair are inherited; the flex-box geometry is not).</summary>
    public static IEnumerable<string> InheritedLegacyNames =>
        LegacyDeclaredOnly.Where(entry => entry.Value.Inherited).Select(entry => entry.Key);

    /// <summary>Whether |property| — as the page <b>wrote</b> it, before any prefix is folded away
    /// — is one of this engine's private ids rather than a property the reference engine declares.
    /// The cascade drops such a declaration where it reads it, which is what the reference engine
    /// does with it: measured, <c>style.cssText</c> after <c>text-security:disc</c>,
    /// <c>line-clamp:2</c>, <c>hanging-punctuation:first</c>, <c>ime-mode:active</c>,
    /// <c>max-lines:2</c> and <c>timing-function:linear</c> is the empty string in each case, while
    /// the <c>-webkit-</c> spelling of the first two is a real property and stays. The test is on
    /// the authored name and not the canonical one because the canonicaliser has already thrown the
    /// prefix away by the time a declaration reaches the cascade.</summary>
    public static bool IsEngineOnlyName(string property) =>
        !string.IsNullOrEmpty(property)
        && !property.StartsWith("--", StringComparison.Ordinal)
        && EngineOnlyNames.Contains(property.ToLowerInvariant());

    /// <summary>Properties the reference engine declares that this engine has no id for, so
    /// <see cref="IsKnown"/> cannot see them. Measured as above: each name answered true for at
    /// least one of eighteen values, and none of them is in this engine's property table. They are
    /// declared only as far as a <c>@supports</c> test is concerned — the cascade has no case for
    /// them either, which is what the reference engine does with a property no layout step reads —
    /// and the difference between this set and the property table is the reason keeping it separate
    /// is honest rather than a whitelist of wishes. A name that gains an id leaves this set: the id
    /// is what makes a <c>-webkit-</c> spelling of it fold, and a name in both places would say two
    /// things about the same property. The names come in groups: the scroll- and timeline-linked
    /// properties, the anchored-positioning ones, the shapes a float excludes text around, and the
    /// font and text properties of CSS Fonts 4 and CSS Text 4.</summary>
    private static readonly HashSet<string> ReferenceDeclared = new(StringComparer.Ordinal)
    {
        "animation-composition", "animation-range", "animation-range-end", "animation-range-start",
        "baseline-source", "caret-shape", "font-palette", "font-variant-alternates",
        "font-variant-emoji", "overflow-block", "overflow-inline", "overlay", "paint-order",
        "position-area", "position-try-fallbacks", "position-visibility", "scroll-timeline",
        "scroll-timeline-axis", "scroll-timeline-name", "shape-image-threshold", "shape-margin",
        "shape-outside", "text-autospace", "text-spacing-trim",
        "transition-behavior", "x", "y",
    };

    /// <summary>Ids this engine keeps in its property table that are not properties the reference
    /// engine declares. Four kinds: SVG presentation geometry the engine models as its own
    /// properties (<c>svgwidth</c> and siblings), names that stand for something else in the engine
    /// (<c>display-type</c> for the display keyword, <c>timing-function</c> for the shared
    /// <c>transition</c>/<c>animation-timing-function</c> grammar, <c>webkit-line-clamp</c> for the
    /// prefixed spelling, <c>max-lines</c>, <c>offset-position-normal</c>,
    /// <c>content-visibility-auto-state</c>), names it parses for a platform that has no counterpart
    /// here (<c>imemode</c>, <c>page-size</c>, <c>input-security</c>, <c>starting-style</c>,
    /// <c>position-try-options</c>), and names a browser never shipped (<c>text-security</c> without
    /// its prefix, <c>line-clamp</c> without its prefix, <c>highlight</c>, <c>anchor-default</c>,
    /// <c>ruby-merge</c>, <c>hanging-punctuation</c>, <c>initial-letter-align</c>). Each was measured
    /// with the value an author would write, so the list is not an artefact of a grammar the engine
    /// happens not to implement.</summary>
    private static readonly HashSet<string> EngineOnlyNames = new(StringComparer.Ordinal)
    {
        "display-type", "svgwidth", "svgheight", "svgx", "svgy", "timing-function", "imemode",
        "page-size", "ruby-merge", "hanging-punctuation", "initial-letter-align", "max-lines",
        "line-clamp", "webkit-line-clamp", "text-security", "highlight", "input-security",
        "content-visibility-auto-state", "anchor-default", "position-try-options", "starting-style",
        "offset-position-normal",
    };

    /// <summary>The names the reference engine accepts with a <c>-webkit-</c> prefix, that prefix
    /// removed. Several have no unprefixed counterpart at all (<c>text-fill-color</c>,
    /// <c>line-clamp</c>, <c>user-drag</c>, <c>box-orient</c>) and the set is not closed under
    /// anything useful — <c>text-security</c> and <c>line-clamp</c> are in it, <c>hyphens</c> and
    /// <c>color</c> are not — so it is a measured inventory rather than a rule about prefixes.
    /// The first group is every name from this engine's own table that was measured accepted
    /// (79 of 447); the second is the legacy spellings the table has no id for.</summary>
    private static readonly HashSet<string> WebkitAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Measured accepted with the prefix, prefixed to a name this engine already carries.
        "align-content", "align-items", "align-self", "animation", "animation-delay",
        "animation-direction", "animation-duration", "animation-fill-mode",
        "animation-iteration-count", "animation-name", "animation-play-state",
        "animation-timing-function", "appearance", "backface-visibility", "background-clip",
        "background-origin", "background-size", "border-bottom-left-radius",
        "border-bottom-right-radius", "border-image", "border-radius", "border-top-left-radius",
        "border-top-right-radius", "box-decoration-break", "box-shadow", "box-sizing", "clip-path",
        "column-count", "column-gap", "column-rule", "column-rule-color", "column-rule-style",
        "column-rule-width", "column-span", "column-width", "columns", "filter", "flex",
        "flex-basis", "flex-direction", "flex-flow", "flex-grow", "flex-shrink", "flex-wrap",
        "font-feature-settings", "hyphenate-character", "justify-content", "line-break",
        "line-clamp", "mask", "mask-clip", "mask-composite", "mask-image", "mask-origin",
        "mask-position", "mask-repeat", "mask-size", "opacity", "order", "perspective",
        "perspective-origin", "print-color-adjust", "text-emphasis", "text-emphasis-color",
        "text-emphasis-position", "text-emphasis-style", "text-orientation", "text-security",
        "transform", "transform-origin", "transform-style", "transition", "transition-delay",
        "transition-duration", "transition-property", "transition-timing-function", "user-select",
        "writing-mode",
        // Measured accepted with the prefix for a name this engine has no id for, so the ids above
        // could never produce them: the legacy box model, the text-paint pair, the logical edges
        // and the mask-image shorthand family.
        "box-align", "box-direction", "box-flex", "box-ordinal-group", "box-orient", "box-pack",
        "box-reflect", "font-smoothing", "margin-end", "margin-start", "mask-box-image",
        "mask-box-image-outset",
        "mask-box-image-repeat", "mask-box-image-slice", "mask-box-image-source",
        "mask-box-image-width", "padding-end", "padding-start", "tap-highlight-color",
        "text-decorations-in-effect", "text-fill-color", "text-size-adjust", "text-stroke",
        "text-stroke-color", "text-stroke-width", "user-drag", "user-modify",
        // Measured NOT accepted with the prefix, so they are absent on purpose: 'color', 'margin',
        // 'padding', 'hyphens', 'overflow-scrolling', 'scrollbar', 'box-lines', 'box-flex-group',
        // 'backup-display', 'dasharray', 'dashoffset', 'filter-function', 'highlight', 'locale',
        // 'hyphenate-limit-chars', 'mask-source-type', 'nested-composite', 'orientation',
        // 'vertical-position', 'color-adjust', 'border-image-slice', 'margin-collapse',
        // 'logical-width-minimum', 'text-fill' and the '-webkit-flex-*' family
        // (flex-negative, flex-order, flex-pack, flex-align, flex-line-pack, flex-item-pack) —
        // and no '-moz-', '-ms-' or '-o-' name is ever accepted.
    };

    /// <summary>Whether the cascade can resolve CSS-wide keywords for a property.
    /// Derived from the property-id table (which is generated from the enum) rather
    /// than from the hand-maintained 'Known' set below: that set had drifted, so
    /// 'border-top-left-radius: inherit' and every other missing name silently did
    /// nothing at all. 'Known' stays as the extra vocabulary the enum does not carry.</summary>
    public static bool IsKnown(string property) =>
        Known.Contains(property)
        || Acrux.Core.Css.Properties.CssPropertyIdExtensions.FromString(property)
           != Acrux.Core.Css.Properties.CssPropertyId.Invalid;

    /// <summary>Properties the cascade can resolve initial values for.</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "display", "position", "float", "clear", "margin", "margin-top", "margin-right",
        "margin-bottom", "margin-left", "padding", "padding-top", "padding-right",
        "padding-bottom", "padding-left", "color", "background", "background-color",
        "font-family", "font-size", "font-weight", "font-style", "line-height",
        "text-align", "text-decoration", "text-decoration-color", "text-decoration-line",
        "text-decoration-skip-ink", "text-decoration-style", "text-decoration-thickness",
        "text-underline-offset", "text-underline-position",
        "vertical-align", "white-space", "white-space-collapse", "text-wrap-mode", "text-wrap-style", "visibility",
        // Not inherited (CSS Overflow 3 §4.1) but must be known so 'inherit'/'initial'
        // and the 'all' shorthand can reach it.
        "overflow", "overflow-x", "overflow-y", "overflow-clip-margin", "overflow-wrap",
        "overflow", "z-index", "opacity", "border", "border-top", "border-right",
        "border-bottom", "border-left", "border-width", "border-style", "border-color",
        "border-radius", "box-sizing", "outline", "top", "right", "bottom", "left",
        "border-start-start-radius", "border-start-end-radius",
        "border-end-start-radius", "border-end-end-radius",
        "overscroll-behavior-block", "overscroll-behavior-inline",
        "grid-gap", "grid-row-gap", "grid-column-gap",
        "cursor", "flex", "flex-direction", "flex-wrap", "flex-grow", "flex-shrink",
        "flex-basis", "justify-content", "align-items", "align-self", "order", "gap",
        "transform", "transform-origin", "translate", "rotate", "scale",
        "perspective", "perspective-origin", "backface-visibility", "transform-box",
        "transform-style",
        "transition", "animation", "filter",
        "content", "word-break", "overflow-wrap", "letter-spacing", "word-spacing",
        "text-indent", "text-transform", "direction", "writing-mode", "list-style",
        "list-style-type", "list-style-position", "list-style-image",
        "box-decoration-break",
    };

    /// <summary>Handles `all: <keyword>` — every property the cascade knows about
    /// except direction/unicode-bidi (CSS Properties 4 §all).</summary>
    private static readonly string[] AllResetProperties =
        Known.Union(Inherited)
            .Where(p => p != "direction" && p != "unicode-bidi")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Copies a property value from a source style into a target style.</summary>
    public static void Copy(ComputedStyle to, ComputedStyle from, string property)
    {
        switch (property)
        {
            case "width": to.Width = from.Width; break;
            case "height": to.Height = from.Height; break;
            case "min-width": to.MinWidth = from.MinWidth; break;
            case "min-height": to.MinHeight = from.MinHeight; break;
            case "max-width": to.MaxWidth = from.MaxWidth; break;
            case "max-height": to.MaxHeight = from.MaxHeight; break;
            case "display": to.Display = from.Display; to.DisplayIsFlowRoot = from.DisplayIsFlowRoot; break;
            case "position": to.Position = from.Position; break;
            case "float": to.Float = from.Float; break;
            case "clear": to.Clear = from.Clear; break;
            case "margin": case "margin-top": to.MarginTop = from.MarginTop; break;
            case "margin-right": to.MarginRight = from.MarginRight; break;
            case "margin-bottom": to.MarginBottom = from.MarginBottom; break;
            case "margin-left": to.MarginLeft = from.MarginLeft; break;
            case "padding": case "padding-top": to.PaddingTop = from.PaddingTop; break;
            case "padding-right": to.PaddingRight = from.PaddingRight; break;
            case "padding-bottom": to.PaddingBottom = from.PaddingBottom; break;
            case "padding-left": to.PaddingLeft = from.PaddingLeft; break;
            case "color": to.Color = from.Color; break;
            case "background": case "background-color": to.BackgroundColor = from.BackgroundColor; break;
            case "background-image": to.BackgroundImage = from.BackgroundImage; break;
            case "font-family": to.FontFamily = from.FontFamily; break;
            case "font-size": to.FontSize = from.FontSize; break;
            case "font-weight": to.FontWeight = from.FontWeight; break;
            case "font-style": to.FontStyle = from.FontStyle; break;
            case "line-height":
                to.LineHeight = from.LineHeight;
                to.LineHeightIsNormal = from.LineHeightIsNormal;
                to.LineHeightPx = from.LineHeightPx;
                break;
            case "text-align": to.TextAlign = from.TextAlign; break;
            case "text-decoration":
                to.TextDecoration = from.TextDecoration;
                to.TextDecorationLine = from.TextDecorationLine;
                to.TextDecorationStyle = from.TextDecorationStyle;
                to.TextDecorationColor = from.TextDecorationColor;
                to.TextDecorationColorIsAuto = from.TextDecorationColorIsAuto;
                to.TextDecorationThickness = from.TextDecorationThickness;
                to.TextDecorationThicknessFromFont = from.TextDecorationThicknessFromFont;
                to.TextDecorationSkipInk = from.TextDecorationSkipInk;
                to.TextUnderlineOffset = from.TextUnderlineOffset;
                to.TextUnderlineOffsetIsAuto = from.TextUnderlineOffsetIsAuto;
                to.TextUnderlinePosition = from.TextUnderlinePosition;
                break;
            case "text-decoration-line": to.TextDecorationLine = from.TextDecorationLine;
                // Keep the legacy single-line enum in sync with the line list.
                to.TextDecoration = CssPropertyApplier.LegacyTextDecorationOf(to.TextDecorationLine);
                break;
            case "text-decoration-style": to.TextDecorationStyle = from.TextDecorationStyle; break;
            case "text-decoration-color": to.TextDecorationColor = from.TextDecorationColor;
                to.TextDecorationColorIsAuto = from.TextDecorationColorIsAuto; break;
            case "text-decoration-thickness": to.TextDecorationThickness = from.TextDecorationThickness;
                to.TextDecorationThicknessFromFont = from.TextDecorationThicknessFromFont; break;
            case "text-decoration-skip-ink": to.TextDecorationSkipInk = from.TextDecorationSkipInk; break;
            case "text-underline-offset": to.TextUnderlineOffset = from.TextUnderlineOffset;
                to.TextUnderlineOffsetIsAuto = from.TextUnderlineOffsetIsAuto; break;
            case "text-underline-position": to.TextUnderlinePosition = from.TextUnderlinePosition; break;
            case "box-decoration-break": to.BoxDecorationBreak = from.BoxDecorationBreak; break;
            case "vertical-align": to.VerticalAlign = from.VerticalAlign; break;
            case "white-space": to.WhiteSpace = from.WhiteSpace;
                to.WhiteSpaceCollapse = from.WhiteSpaceCollapse;
                to.TextWrapMode = from.TextWrapMode; to.TextWrapStyle = from.TextWrapStyle; break;
            case "white-space-collapse": to.WhiteSpaceCollapse = from.WhiteSpaceCollapse; break;
            case "text-wrap-mode": to.TextWrapMode = from.TextWrapMode; break;
            case "text-wrap-style": to.TextWrapStyle = from.TextWrapStyle; break;
            case "overflow-clip-margin": to.OverflowClipMargin = from.OverflowClipMargin;
                to.OverflowClipMarginBox = from.OverflowClipMarginBox; break;
            case "contain": to.Contain = from.Contain; break;
            case "content-visibility": to.ContentVisibility = from.ContentVisibility; break;
            case "field-sizing": to.FieldSizing = from.FieldSizing; break;
            case "contain-intrinsic-size":
                to.ContainIntrinsicWidth = from.ContainIntrinsicWidth;
                to.ContainIntrinsicWidthIsAuto = from.ContainIntrinsicWidthIsAuto;
                to.ContainIntrinsicHeight = from.ContainIntrinsicHeight;
                to.ContainIntrinsicHeightIsAuto = from.ContainIntrinsicHeightIsAuto; break;
            case "contain-intrinsic-width":
            case "contain-intrinsic-inline-size":
                to.ContainIntrinsicWidth = from.ContainIntrinsicWidth;
                to.ContainIntrinsicWidthIsAuto = from.ContainIntrinsicWidthIsAuto; break;
            case "contain-intrinsic-height": case "contain-intrinsic-block-size":
                to.ContainIntrinsicHeight = from.ContainIntrinsicHeight;
                to.ContainIntrinsicHeightIsAuto = from.ContainIntrinsicHeightIsAuto; break;

            case "tab-size": to.TabSize = from.TabSize; to.TabSizePx = from.TabSizePx; break;
            // The keyword family the engine declares but does not yet act on: 'inherit' and
            // 'initial' (and 'all', which enumerates them) have to reach the stored text like any
            // other value, or a page that inherits 'paint-order' would read back the initial.
            case "touch-action": to.TouchAction = from.TouchAction; break;
            case "paint-order": to.PaintOrder = from.PaintOrder; break;
            case "vector-effect": to.VectorEffect = from.VectorEffect; break;
            case "shape-rendering": to.ShapeRendering = from.ShapeRendering; break;
            case "color-rendering": to.ColorRendering = from.ColorRendering; break;
            case "color-interpolation": to.ColorInterpolation = from.ColorInterpolation; break;
            case "color-interpolation-filters":
                to.ColorInterpolationFilters = from.ColorInterpolationFilters; break;
            case "image-orientation": to.ImageOrientation = from.ImageOrientation; break;
            case "text-security": to.TextSecurity = from.TextSecurity; break;
            case "visibility": to.Visibility = from.Visibility; break;
            case "overflow": to.Overflow = to.OverflowX = to.OverflowY = from.Overflow; break;
            case "overflow-x": to.OverflowX = from.OverflowX; break;
            case "overflow-y": to.OverflowY = from.OverflowY; break;
            case "z-index": to.ZIndex = from.ZIndex; break;
            case "opacity": to.Opacity = from.Opacity; break;
            case "border-width": case "border-top-width": to.BorderTopWidth = from.BorderTopWidth; break;
            case "border-right-width": to.BorderRightWidth = from.BorderRightWidth; break;
            case "border-bottom-width": to.BorderBottomWidth = from.BorderBottomWidth; break;
            case "border-left-width": to.BorderLeftWidth = from.BorderLeftWidth; break;
            case "border-style": case "border-top-style": to.BorderTopStyle = from.BorderTopStyle; break;
            case "border-right-style": to.BorderRightStyle = from.BorderRightStyle; break;
            case "border-bottom-style": to.BorderBottomStyle = from.BorderBottomStyle; break;
            case "border-left-style": to.BorderLeftStyle = from.BorderLeftStyle; break;
            case "border-color": case "border-top-color": to.BorderTopColor = from.BorderTopColor; break;
            case "border-right-color": to.BorderRightColor = from.BorderRightColor; break;
            case "border-bottom-color": to.BorderBottomColor = from.BorderBottomColor; break;
            case "border-left-color": to.BorderLeftColor = from.BorderLeftColor; break;
            // Every corner carries an elliptical pair; copying only the horizontal one
            // made 'border-top-left-radius: 10px 20px' lose its 20px through
            // inherit/initial. 'border-radius' is a shorthand over all four corners.
            // The logical corner names map to their LTR corner here: the direction-aware
            // mapping happens in the applier's replay queue, and for 'initial' every
            // corner is zero anyway.
            case "border-radius":
                CopyRadius(to, from, 0); CopyRadius(to, from, 1);
                CopyRadius(to, from, 2); CopyRadius(to, from, 3); break;
            case "border-top-left-radius": case "border-start-start-radius":
                CopyRadius(to, from, 0); break;
            case "border-top-right-radius": case "border-start-end-radius":
                CopyRadius(to, from, 1); break;
            case "border-bottom-right-radius": case "border-end-end-radius":
                CopyRadius(to, from, 2); break;
            case "border-bottom-left-radius": case "border-end-start-radius":
                CopyRadius(to, from, 3); break;
            case "box-sizing": to.BoxSizing = from.BoxSizing; break;
            case "top": to.Top = from.Top; break;
            case "right": to.Right = from.Right; break;
            case "bottom": to.Bottom = from.Bottom; break;
            case "left": to.Left = from.Left; break;
            case "cursor": to.Cursor = from.Cursor; break;
            case "flex-direction": to.FlexDirection = from.FlexDirection; break;
            case "flex-wrap": to.FlexWrap = from.FlexWrap; break;
            case "flex-grow": to.FlexGrow = from.FlexGrow; break;
            case "flex-shrink": to.FlexShrink = from.FlexShrink; break;
            case "flex-basis": to.FlexBasis = from.FlexBasis; break;
            case "justify-content": to.JustifyContent = from.JustifyContent; break;
            case "align-items": to.AlignItems = from.AlignItems; break;
            case "align-self": to.AlignSelf = from.AlignSelf; break;
            case "order": to.Order = from.Order; break;
            case "gap": case "row-gap": to.RowGap = from.RowGap; break;
            case "column-gap": to.ColumnGap = from.ColumnGap; break;
            case "transform": to.Transform = from.Transform; break;
            case "transform-origin": to.TransformOrigin = from.TransformOrigin; break;
            // The individual transforms and the properties that surround them. 'inherit' and
            // 'initial' (and the 'all' shorthand, which enumerates this set) have to reach them
            // like any other property, and each one's initial value is the fresh ComputedStyle's
            // field default that SetInitial reads off of.
            case "translate": to.Translate = from.Translate; break;
            case "rotate": to.Rotate = from.Rotate; break;
            case "scale": to.Scale = from.Scale; break;
            case "perspective": to.Perspective = from.Perspective; break;
            case "perspective-origin": to.PerspectiveOrigin = from.PerspectiveOrigin; break;
            case "backface-visibility": to.BackfaceVisibility = from.BackfaceVisibility; break;
            case "transform-box": to.TransformBox = from.TransformBox; break;
            case "transform-style": to.TransformStyle = from.TransformStyle; break;
            case "transition": to.Transition = from.Transition; break;
            case "animation": to.Animation = from.Animation; break;
            case "filter": to.Filter = from.Filter; break;
            case "content": to.Content = from.Content; break;
            case "word-break": to.WordBreak = from.WordBreak; break;
            case "overflow-wrap": to.OverflowWrap = from.OverflowWrap; break;
            case "letter-spacing": to.LetterSpacing = from.LetterSpacing;
                to.LetterSpacingIsNormal = from.LetterSpacingIsNormal; break;
            // 'hyphens' had no Copy case at all, so 'hyphens: inherit' and
            // 'initial' silently kept whatever the element already had.
            case "hyphens": to.Hyphens = from.Hyphens; break;
            case "hyphenate-character": to.HyphenateCharacter = from.HyphenateCharacter; break;
            case "word-spacing": to.WordSpacing = from.WordSpacing;
                to.WordSpacingIsNormal = from.WordSpacingIsNormal; break;
            case "text-indent": to.TextIndent = from.TextIndent; to.TextIndentHanging = from.TextIndentHanging;
                to.TextIndentEachLine = from.TextIndentEachLine;
                to.TextIndentPercent = from.TextIndentPercent; break;
            case "text-transform": to.TextTransform = from.TextTransform; break;
            case "direction": to.Direction = from.Direction; break;
            case "writing-mode": to.WritingMode = from.WritingMode; break;
            case "list-style": case "list-style-type": to.ListStyleType = from.ListStyleType;
                to.ListStyleTypeString = from.ListStyleTypeString;
                to.ListStyleTypeName = from.ListStyleTypeName; break;
            case "list-style-position": to.ListStylePosition = from.ListStylePosition; break;
            case "list-style-image": to.ListStyleImage = from.ListStyleImage; break;
        }
        // The echo-only properties have no field to copy: their value lives in one map, and a
        // source that says nothing clears the target's entry the way copying a default field
        // would for any other property.
        if (Acrux.Core.Css.Resolver.EchoedProperties.Find(property) is { } echo)
        {
            var echoedValue = from.GetEchoed(echo.Name);
            if (echoedValue == null) to.Echoed?.Remove(echo.Name);
            else to.SetEchoed(echo.Name, echoedValue);
        }
    }

    /// <summary>Restores a property to its initial (default) value.</summary>
    /// <summary>Copy one corner's elliptical pair (horizontal + vertical radius).</summary>
    private static void CopyRadius(ComputedStyle to, ComputedStyle from, int corner)
    {
        switch (corner)
        {
            case 0:
                to.BorderTopLeftRadius = from.BorderTopLeftRadius;
                to.BorderTopLeftRadiusY = from.BorderTopLeftRadiusY; break;
            case 1:
                to.BorderTopRightRadius = from.BorderTopRightRadius;
                to.BorderTopRightRadiusY = from.BorderTopRightRadiusY; break;
            case 2:
                to.BorderBottomRightRadius = from.BorderBottomRightRadius;
                to.BorderBottomRightRadiusY = from.BorderBottomRightRadiusY; break;
            default:
                to.BorderBottomLeftRadius = from.BorderBottomLeftRadius;
                to.BorderBottomLeftRadiusY = from.BorderBottomLeftRadiusY; break;
        }
    }

    public static void SetInitial(ComputedStyle to, string property)
    {
        // 'display' has the initial value 'inline' (CSS 2.1 §9.7), while a fresh
        // ComputedStyle models the engine's default box as block.
        if (property == "display")
        {
            to.Display = DisplayType.Inline;
            to.DisplayIsFlowRoot = false;
            return;
        }
        Copy(to, new ComputedStyle(), property);
    }

    /// <summary>The CSS-wide keywords (CSS Values 3 §2.1). Every property accepts them, so a
    /// value check against one property's own grammar must not reject them. Keyword matching
    /// is ASCII case-insensitive (CSS 2.1 §3.1), which is why these comparisons are not
    /// ordinal: 'Display: Inherit' is the same declaration as 'display: inherit'.</summary>
    public static bool IsCssWideKeyword(string value) =>
        value.Equals("inherit", StringComparison.OrdinalIgnoreCase)
        || value.Equals("initial", StringComparison.OrdinalIgnoreCase)
        || value.Equals("unset", StringComparison.OrdinalIgnoreCase)
        || value.Equals("revert", StringComparison.OrdinalIgnoreCase)
        || value.Equals("revert-layer", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for 'revert' and 'revert-layer'; |revertLayer| says which. These two are
    /// the only CSS-wide keywords whose meaning depends on what the cascade holds *below* the
    /// declaration, so StyleCascade resolves them there and the value reached here is the
    /// fallback for when there is nothing lower left to revert to.</summary>
    public static bool IsRevertKeyword(string? value, out bool revertLayer)
    {
        revertLayer = false;
        if (value == null) return false;
        var text = value.Trim();
        if (text.Equals("revert", StringComparison.OrdinalIgnoreCase)) return true;
        if (!text.Equals("revert-layer", StringComparison.OrdinalIgnoreCase)) return false;
        revertLayer = true;
        return true;
    }

    /// <summary>
    /// Handles the CSS-wide keywords inherit/initial/unset/revert/revert-layer.
    /// Returns true when the value is a global keyword (already applied), false
    /// when the value is a normal declaration that the caller must apply.
    /// </summary>
    public static bool TryApplyCssWideKeyword(ComputedStyle style, string name, string value, ComputedStyle? parentStyle)
    {
        var keyword = value.Trim();
        if (name == "all")
            return TryApplyAll(style, keyword, parentStyle);
        if (!IsCssWideKeyword(keyword)) return false;

        switch (keyword.ToLowerInvariant())
        {
            case "inherit":
                if (IsKnown(name))
                    Copy(style, parentStyle ?? new ComputedStyle(), name);
                return true;
            case "initial":
                if (IsKnown(name))
                    SetInitial(style, name);
                return true;
            case "unset":
                if (IsInherited(name))
                    Copy(style, parentStyle ?? new ComputedStyle(), name);
                else if (IsKnown(name))
                    SetInitial(style, name);
                return true;
            case "revert":
            case "revert-layer":
                // Nothing below the declaration to revert to: 'revert' then behaves like
                // 'unset' (CSS Cascade 4 § revert), which is what StyleCascade leaves for
                // the case where no lower-origin declaration exists.
                if (IsInherited(name))
                    Copy(style, parentStyle ?? new ComputedStyle(), name);
                else if (IsKnown(name))
                    SetInitial(style, name);
                return true;
            default:
                return false;
        }
    }

    private static bool TryApplyAll(ComputedStyle style, string value, ComputedStyle? parentStyle)
    {
        var source = parentStyle ?? new ComputedStyle();
        // 'all' also addresses the logical properties, whose queued re-mapping pass
        // would otherwise re-assert a logical value that 'all' has just reset.
        style.PendingBoxEdgeProperties = null;
        switch (value.ToLowerInvariant())
        {
            case "inherit":
                foreach (var property in AllResetProperties)
                    Copy(style, source, property);
                return true;
            case "initial":
                foreach (var property in AllResetProperties)
                    SetInitial(style, property);
                return true;
            case "unset":
            case "revert":
            case "revert-layer":
                foreach (var property in AllResetProperties)
                {
                    if (IsInherited(property))
                        Copy(style, source, property);
                    else
                        SetInitial(style, property);
                }
                return true;
            default:
                return false;
        }
    }
}
