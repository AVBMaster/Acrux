using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// The single definition of which computed properties inherit.
///
/// There used to be two hand-maintained copies of this list — CascadeResolver.InheritProperties
/// and StyleResolver.InheritProperties — and the copy that actually ran had drifted behind by
/// twenty-four inherited properties, so declaring <c>caret-color</c>, <c>text-emphasis</c>,
/// <c>pointer-events</c>, <c>hyphens</c>, <c>orphans</c> and friends on an ancestor was silently
/// ignored by every descendant. Both call sites now delegate here; add a property once.
///
/// Everything not listed keeps its initial value on a child, which is correct for the
/// non-inherited majority (CSS 2.1 §6.1.2 plus each module's own 'Inherited:' line).
/// </summary>
public static class CssInheritance
{
    public static void Apply(ComputedStyle child, ComputedStyle parent)
    {
        // Color and the font chain (CSS Color 3 §2; CSS Fonts 4 §2–§7).
        child.Color = parent.Color;
        child.FontFamily = parent.FontFamily;
        child.FontSize = parent.FontSize;
        // The 'is this still the default size?' state travels with the inherited value: a
        // descendant of an element that authored a size must not substitute the monospace
        // generic's default (see ComputedStyle.FontSizeIsDefault).
        child.FontSizeIsDefault = parent.FontSizeIsDefault;
        child.FontWeight = parent.FontWeight;
        child.FontStyle = parent.FontStyle;
        // The authored angle is part of the inherited 'font-style' value: an element that
        // inherits 'oblique 20deg' must keep the 20deg, or it degrades into a request for the
        // family's own slanted face and paints a different slant than its parent's.
        child.FontStyleObliqueDegrees = parent.FontStyleObliqueDegrees;
        child.FontVariant = parent.FontVariant;
        child.FontVariantCaps = parent.FontVariantCaps;
        child.FontStretch = parent.FontStretch;
        child.FontKerning = parent.FontKerning;
        child.FontSynthesis = parent.FontSynthesis;
        child.FontOpticalSizing = parent.FontOpticalSizing;
        child.FontVariationSettings = parent.FontVariationSettings;
        child.FontFeatureSettings = parent.FontFeatureSettings;
        child.FontSizeAdjust = parent.FontSizeAdjust;
        // 'line-height' inherits its computed value, so the 'normal' flag and any absolute
        // length must travel with the multiplier.
        child.LineHeight = parent.LineHeight;
        child.LineHeightIsNormal = parent.LineHeightIsNormal;
        child.LineHeightPx = parent.LineHeightPx;

        // Text (CSS Text 3/4; the modular white-space longhands inherit exactly like the
        // shorthand they feed).
        child.TextAlign = parent.TextAlign;
        child.TextAlignLast = parent.TextAlignLast;
        child.WhiteSpace = parent.WhiteSpace;
        child.WhiteSpaceCollapse = parent.WhiteSpaceCollapse;
        child.TextWrapMode = parent.TextWrapMode;
        child.TextWrapStyle = parent.TextWrapStyle;
        child.TextWrap = parent.TextWrap;
        child.WordBreak = parent.WordBreak;
        child.OverflowWrap = parent.OverflowWrap;
        child.LineBreak = parent.LineBreak;
        child.Hyphens = parent.Hyphens;
        child.HyphenateCharacter = parent.HyphenateCharacter;
        child.TextJustify = parent.TextJustify;
        child.TextRendering = parent.TextRendering;
        child.TextTransform = parent.TextTransform;
        child.LetterSpacing = parent.LetterSpacing;
        child.WordSpacing = parent.WordSpacing;
        child.LetterSpacingIsNormal = parent.LetterSpacingIsNormal;
        child.WordSpacingIsNormal = parent.WordSpacingIsNormal;
        child.TextIndent = parent.TextIndent;
        child.TextIndentPercent = parent.TextIndentPercent;
        child.TextIndentHanging = parent.TextIndentHanging;
        child.TextIndentEachLine = parent.TextIndentEachLine;
        child.TabSize = parent.TabSize;
        child.TabSizePx = parent.TabSizePx;
        child.Quotes = parent.Quotes;
        child.TextShadow = new System.Collections.Generic.List<TextShadowValue>(parent.TextShadow);
        child.TextEmphasis = parent.TextEmphasis;
        child.TextEmphasisColor = parent.TextEmphasisColor;
        child.TextEmphasisStyle = parent.TextEmphasisStyle;
        child.TextEmphasisPosition = parent.TextEmphasisPosition;
        child.RubyPosition = parent.RubyPosition;

        // Lists (CSS Lists 3 §3).
        child.ListStyleType = parent.ListStyleType;
        child.ListStyleTypeName = parent.ListStyleTypeName;
        child.ListStyleTypeString = parent.ListStyleTypeString;
        child.ListStylePosition = parent.ListStylePosition;

        // CSS Text Security: the mask is a property of the text, and the text of a descendant is
        // its parent's text (measured: a span inside a masked element reads 'disc' and draws
        // bullets).
        child.TextSecurity = parent.TextSecurity;

        // Box, direction and interaction.
        child.Visibility = parent.Visibility;
        child.Cursor = parent.Cursor;
        child.Direction = parent.Direction;
        child.WritingMode = parent.WritingMode;
        child.CaptionSide = parent.CaptionSide;
        child.EmptyCells = parent.EmptyCells;
        child.BorderCollapse = parent.BorderCollapse;
        child.BorderSpacing = parent.BorderSpacing;
        child.BorderRowSpacing = parent.BorderRowSpacing;
        child.Orphans = parent.Orphans;
        child.Widows = parent.Widows;
        child.ImageRendering = parent.ImageRendering;
        child.AccentColor = parent.AccentColor;
        child.CaretColor = parent.CaretColor;
        child.ColorScheme = parent.ColorScheme;
        child.ForcedColorAdjust = parent.ForcedColorAdjust;
        child.PointerEvents = parent.PointerEvents;
        child.UserSelect = parent.UserSelect;
        child.Zoom = parent.Zoom;
    }
}
