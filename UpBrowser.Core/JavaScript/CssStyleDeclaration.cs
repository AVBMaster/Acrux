using UpBrowser.Core.Dom;
using UpBrowser.Core.Performance;

namespace UpBrowser.Core.JavaScript;

public class CssStyleDeclaration
{
    private readonly Element _element;

    public CssStyleDeclaration(Element element)
    {
        _element = element;
    }

    public string cssText
    {
        get => string.Join("; ", _element.Style.Select(kv => $"{kv.Key}: {kv.Value}"));
        set
        {
            _element.Style.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colon = part.IndexOf(':');
                    if (colon > 0)
                    {
                        var prop = part[..colon].Trim();
                        var val = part[(colon + 1)..].Trim();
                        if (!string.IsNullOrEmpty(prop))
                            _element.Style[prop] = val;
                    }
                }
            }
            DirtyState.AddSelf(_element, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
            DomMutationTracker.Notify();
        }
    }

    public int length => _element.Style.Count;

    public string? item(int index)
    {
        if (index < 0 || index >= _element.Style.Count) return null;
        return _element.Style.ElementAt(index).Key;
    }

    public string getPropertyValue(string propertyName) =>
        _element.Style.TryGetValue(propertyName, out var val) ? val : "";

    public void setProperty(string propertyName, string value)
    {
        _element.Style[propertyName] = value;
        DirtyState.AddSelf(_element, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
        DomMutationTracker.Notify();
    }

    public string removeProperty(string propertyName)
    {
        _element.Style.Remove(propertyName, out var old);
        DirtyState.AddSelf(_element, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
        return old ?? "";
    }

    // ===== Common CSS properties as direct getter/setter (JS camelCase names) =====

    public string? accentColor { get => GetStyle("accent-color"); set => SetStyle("accent-color", value); }
    public string? alignContent { get => GetStyle("align-content"); set => SetStyle("align-content", value); }
    public string? alignItems { get => GetStyle("align-items"); set => SetStyle("align-items", value); }
    public string? alignSelf { get => GetStyle("align-self"); set => SetStyle("align-self", value); }
    public string? animation { get => GetStyle("animation"); set => SetStyle("animation", value); }
    public string? appearance { get => GetStyle("appearance"); set => SetStyle("appearance", value); }
    public string? aspectRatio { get => GetStyle("aspect-ratio"); set => SetStyle("aspect-ratio", value); }
    public string? backdropFilter { get => GetStyle("backdrop-filter"); set => SetStyle("backdrop-filter", value); }
    public string? background { get => GetStyle("background"); set => SetStyle("background", value); }
    public string? backgroundColor { get => GetStyle("background-color"); set => SetStyle("background-color", value); }
    public string? backgroundImage { get => GetStyle("background-image"); set => SetStyle("background-image", value); }
    public string? backgroundPosition { get => GetStyle("background-position"); set => SetStyle("background-position", value); }
    public string? backgroundRepeat { get => GetStyle("background-repeat"); set => SetStyle("background-repeat", value); }
    public string? backgroundSize { get => GetStyle("background-size"); set => SetStyle("background-size", value); }
    public string? border { get => GetStyle("border"); set => SetStyle("border", value); }
    public string? borderBottom { get => GetStyle("border-bottom"); set => SetStyle("border-bottom", value); }
    public string? borderBottomColor { get => GetStyle("border-bottom-color"); set => SetStyle("border-bottom-color", value); }
    public string? borderBottomLeftRadius { get => GetStyle("border-bottom-left-radius"); set => SetStyle("border-bottom-left-radius", value); }
    public string? borderBottomRightRadius { get => GetStyle("border-bottom-right-radius"); set => SetStyle("border-bottom-right-radius", value); }
    public string? borderBottomWidth { get => GetStyle("border-bottom-width"); set => SetStyle("border-bottom-width", value); }
    public string? borderCollapse { get => GetStyle("border-collapse"); set => SetStyle("border-collapse", value); }
    public string? borderColor { get => GetStyle("border-color"); set => SetStyle("border-color", value); }
    public string? borderLeft { get => GetStyle("border-left"); set => SetStyle("border-left", value); }
    public string? borderLeftWidth { get => GetStyle("border-left-width"); set => SetStyle("border-left-width", value); }
    public string? borderRadius { get => GetStyle("border-radius"); set => SetStyle("border-radius", value); }
    public string? borderRight { get => GetStyle("border-right"); set => SetStyle("border-right", value); }
    public string? borderRightWidth { get => GetStyle("border-right-width"); set => SetStyle("border-right-width", value); }
    public string? borderSpacing { get => GetStyle("border-spacing"); set => SetStyle("border-spacing", value); }
    public string? borderStyle { get => GetStyle("border-style"); set => SetStyle("border-style", value); }
    public string? borderTop { get => GetStyle("border-top"); set => SetStyle("border-top", value); }
    public string? borderTopColor { get => GetStyle("border-top-color"); set => SetStyle("border-top-color", value); }
    public string? borderTopLeftRadius { get => GetStyle("border-top-left-radius"); set => SetStyle("border-top-left-radius", value); }
    public string? borderTopRightRadius { get => GetStyle("border-top-right-radius"); set => SetStyle("border-top-right-radius", value); }
    public string? borderTopWidth { get => GetStyle("border-top-width"); set => SetStyle("border-top-width", value); }
    public string? borderWidth { get => GetStyle("border-width"); set => SetStyle("border-width", value); }
    public string? bottom { get => GetStyle("bottom"); set => SetStyle("bottom", value); }
    public string? boxShadow { get => GetStyle("box-shadow"); set => SetStyle("box-shadow", value); }
    public string? boxSizing { get => GetStyle("box-sizing"); set => SetStyle("box-sizing", value); }
    public string? caretColor { get => GetStyle("caret-color"); set => SetStyle("caret-color", value); }
    public string? clear { get => GetStyle("clear"); set => SetStyle("clear", value); }
    public string? clip { get => GetStyle("clip"); set => SetStyle("clip", value); }
    public string? clipPath { get => GetStyle("clip-path"); set => SetStyle("clip-path", value); }
    public string? color { get => GetStyle("color"); set => SetStyle("color", value); }
    public string? columnCount { get => GetStyle("column-count"); set => SetStyle("column-count", value); }
    public string? columnGap { get => GetStyle("column-gap"); set => SetStyle("column-gap", value); }
    public string? columnWidth { get => GetStyle("column-width"); set => SetStyle("column-width", value); }
    public string? content { get => GetStyle("content"); set => SetStyle("content", value); }
    public string? counterIncrement { get => GetStyle("counter-increment"); set => SetStyle("counter-increment", value); }
    public string? counterReset { get => GetStyle("counter-reset"); set => SetStyle("counter-reset", value); }
    public string? cursor { get => GetStyle("cursor"); set => SetStyle("cursor", value); }
    public string? direction { get => GetStyle("direction"); set => SetStyle("direction", value); }
    public string? display { get => GetStyle("display"); set => SetStyle("display", value); }
    public string? emptyCells { get => GetStyle("empty-cells"); set => SetStyle("empty-cells", value); }
    public string? filter { get => GetStyle("filter"); set => SetStyle("filter", value); }
    public string? flex { get => GetStyle("flex"); set => SetStyle("flex", value); }
    public string? flexBasis { get => GetStyle("flex-basis"); set => SetStyle("flex-basis", value); }
    public string? flexDirection { get => GetStyle("flex-direction"); set => SetStyle("flex-direction", value); }
    public string? flexFlow { get => GetStyle("flex-flow"); set => SetStyle("flex-flow", value); }
    public string? flexGrow { get => GetStyle("flex-grow"); set => SetStyle("flex-grow", value); }
    public string? flexShrink { get => GetStyle("flex-shrink"); set => SetStyle("flex-shrink", value); }
    public string? flexWrap { get => GetStyle("flex-wrap"); set => SetStyle("flex-wrap", value); }
    public string? floatProp { get => GetStyle("float"); set => SetStyle("float", value); }
    public string? font { get => GetStyle("font"); set => SetStyle("font", value); }
    public string? fontFamily { get => GetStyle("font-family"); set => SetStyle("font-family", value); }
    public string? fontFeatureSettings { get => GetStyle("font-feature-settings"); set => SetStyle("font-feature-settings", value); }
    public string? fontSize { get => GetStyle("font-size"); set => SetStyle("font-size", value); }
    public string? fontStretch { get => GetStyle("font-stretch"); set => SetStyle("font-stretch", value); }
    public string? fontStyle { get => GetStyle("font-style"); set => SetStyle("font-style", value); }
    public string? fontVariant { get => GetStyle("font-variant"); set => SetStyle("font-variant", value); }
    public string? fontWeight { get => GetStyle("font-weight"); set => SetStyle("font-weight", value); }
    public string? gap { get => GetStyle("gap"); set => SetStyle("gap", value); }
    public string? grid { get => GetStyle("grid"); set => SetStyle("grid", value); }
    public string? gridArea { get => GetStyle("grid-area"); set => SetStyle("grid-area", value); }
    public string? gridAutoColumns { get => GetStyle("grid-auto-columns"); set => SetStyle("grid-auto-columns", value); }
    public string? gridAutoFlow { get => GetStyle("grid-auto-flow"); set => SetStyle("grid-auto-flow", value); }
    public string? gridAutoRows { get => GetStyle("grid-auto-rows"); set => SetStyle("grid-auto-rows", value); }
    public string? gridColumn { get => GetStyle("grid-column"); set => SetStyle("grid-column", value); }
    public string? gridColumnEnd { get => GetStyle("grid-column-end"); set => SetStyle("grid-column-end", value); }
    public string? gridColumnGap { get => GetStyle("grid-column-gap"); set => SetStyle("grid-column-gap", value); }
    public string? gridColumnStart { get => GetStyle("grid-column-start"); set => SetStyle("grid-column-start", value); }
    public string? gridGap { get => GetStyle("grid-gap"); set => SetStyle("grid-gap", value); }
    public string? gridRow { get => GetStyle("grid-row"); set => SetStyle("grid-row", value); }
    public string? gridRowEnd { get => GetStyle("grid-row-end"); set => SetStyle("grid-row-end", value); }
    public string? gridRowGap { get => GetStyle("grid-row-gap"); set => SetStyle("grid-row-gap", value); }
    public string? gridRowStart { get => GetStyle("grid-row-start"); set => SetStyle("grid-row-start", value); }
    public string? gridTemplate { get => GetStyle("grid-template"); set => SetStyle("grid-template", value); }
    public string? gridTemplateAreas { get => GetStyle("grid-template-areas"); set => SetStyle("grid-template-areas", value); }
    public string? gridTemplateColumns { get => GetStyle("grid-template-columns"); set => SetStyle("grid-template-columns", value); }
    public string? gridTemplateRows { get => GetStyle("grid-template-rows"); set => SetStyle("grid-template-rows", value); }
    public string? height { get => GetStyle("height"); set => SetStyle("height", value); }
    public string? hyphens { get => GetStyle("hyphens"); set => SetStyle("hyphens", value); }
    public string? justifyContent { get => GetStyle("justify-content"); set => SetStyle("justify-content", value); }
    public string? justifyItems { get => GetStyle("justify-items"); set => SetStyle("justify-items", value); }
    public string? justifySelf { get => GetStyle("justify-self"); set => SetStyle("justify-self", value); }
    public string? left { get => GetStyle("left"); set => SetStyle("left", value); }
    public string? letterSpacing { get => GetStyle("letter-spacing"); set => SetStyle("letter-spacing", value); }
    public string? lineHeight { get => GetStyle("line-height"); set => SetStyle("line-height", value); }
    public string? listStyle { get => GetStyle("list-style"); set => SetStyle("list-style", value); }
    public string? listStyleImage { get => GetStyle("list-style-image"); set => SetStyle("list-style-image", value); }
    public string? listStylePosition { get => GetStyle("list-style-position"); set => SetStyle("list-style-position", value); }
    public string? listStyleType { get => GetStyle("list-style-type"); set => SetStyle("list-style-type", value); }
    public string? margin { get => GetStyle("margin"); set => SetStyle("margin", value); }
    public string? marginBottom { get => GetStyle("margin-bottom"); set => SetStyle("margin-bottom", value); }
    public string? marginLeft { get => GetStyle("margin-left"); set => SetStyle("margin-left", value); }
    public string? marginRight { get => GetStyle("margin-right"); set => SetStyle("margin-right", value); }
    public string? marginTop { get => GetStyle("margin-top"); set => SetStyle("margin-top", value); }
    public string? mask { get => GetStyle("mask"); set => SetStyle("mask", value); }
    public string? maxHeight { get => GetStyle("max-height"); set => SetStyle("max-height", value); }
    public string? maxWidth { get => GetStyle("max-width"); set => SetStyle("max-width", value); }
    public string? minHeight { get => GetStyle("min-height"); set => SetStyle("min-height", value); }
    public string? minWidth { get => GetStyle("min-width"); set => SetStyle("min-width", value); }
    public string? mixBlendMode { get => GetStyle("mix-blend-mode"); set => SetStyle("mix-blend-mode", value); }
    public string? objectFit { get => GetStyle("object-fit"); set => SetStyle("object-fit", value); }
    public string? objectPosition { get => GetStyle("object-position"); set => SetStyle("object-position", value); }
    public string? opacity { get => GetStyle("opacity"); set => SetStyle("opacity", value); }
    public string? order { get => GetStyle("order"); set => SetStyle("order", value); }
    public string? orphans { get => GetStyle("orphans"); set => SetStyle("orphans", value); }
    public string? outline { get => GetStyle("outline"); set => SetStyle("outline", value); }
    public string? outlineColor { get => GetStyle("outline-color"); set => SetStyle("outline-color", value); }
    public string? outlineOffset { get => GetStyle("outline-offset"); set => SetStyle("outline-offset", value); }
    public string? outlineStyle { get => GetStyle("outline-style"); set => SetStyle("outline-style", value); }
    public string? outlineWidth { get => GetStyle("outline-width"); set => SetStyle("outline-width", value); }
    public string? overflow { get => GetStyle("overflow"); set => SetStyle("overflow", value); }
    public string? overflowX { get => GetStyle("overflow-x"); set => SetStyle("overflow-x", value); }
    public string? overflowY { get => GetStyle("overflow-y"); set => SetStyle("overflow-y", value); }
    public string? overflowWrap { get => GetStyle("overflow-wrap"); set => SetStyle("overflow-wrap", value); }
    public string? padding { get => GetStyle("padding"); set => SetStyle("padding", value); }
    public string? paddingBottom { get => GetStyle("padding-bottom"); set => SetStyle("padding-bottom", value); }
    public string? paddingLeft { get => GetStyle("padding-left"); set => SetStyle("padding-left", value); }
    public string? paddingRight { get => GetStyle("padding-right"); set => SetStyle("padding-right", value); }
    public string? paddingTop { get => GetStyle("padding-top"); set => SetStyle("padding-top", value); }
    public string? pageBreakAfter { get => GetStyle("page-break-after"); set => SetStyle("page-break-after", value); }
    public string? pageBreakBefore { get => GetStyle("page-break-before"); set => SetStyle("page-break-before", value); }
    public string? perspective { get => GetStyle("perspective"); set => SetStyle("perspective", value); }
    public string? placeContent { get => GetStyle("place-content"); set => SetStyle("place-content", value); }
    public string? placeItems { get => GetStyle("place-items"); set => SetStyle("place-items", value); }
    public string? placeSelf { get => GetStyle("place-self"); set => SetStyle("place-self", value); }
    public string? pointerEvents { get => GetStyle("pointer-events"); set => SetStyle("pointer-events", value); }
    public string? position { get => GetStyle("position"); set => SetStyle("position", value); }
    public string? quotes { get => GetStyle("quotes"); set => SetStyle("quotes", value); }
    public string? resize { get => GetStyle("resize"); set => SetStyle("resize", value); }
    public string? right { get => GetStyle("right"); set => SetStyle("right", value); }
    public string? rowGap { get => GetStyle("row-gap"); set => SetStyle("row-gap", value); }
    public string? scrollBehavior { get => GetStyle("scroll-behavior"); set => SetStyle("scroll-behavior", value); }
    public string? tabSize { get => GetStyle("tab-size"); set => SetStyle("tab-size", value); }
    public string? tableLayout { get => GetStyle("table-layout"); set => SetStyle("table-layout", value); }
    public string? textAlign { get => GetStyle("text-align"); set => SetStyle("text-align", value); }
    public string? textAlignLast { get => GetStyle("text-align-last"); set => SetStyle("text-align-last", value); }
    public string? textDecoration { get => GetStyle("text-decoration"); set => SetStyle("text-decoration", value); }
    public string? textDecorationColor { get => GetStyle("text-decoration-color"); set => SetStyle("text-decoration-color", value); }
    public string? textDecorationLine { get => GetStyle("text-decoration-line"); set => SetStyle("text-decoration-line", value); }
    public string? textDecorationStyle { get => GetStyle("text-decoration-style"); set => SetStyle("text-decoration-style", value); }
    public string? textIndent { get => GetStyle("text-indent"); set => SetStyle("text-indent", value); }
    public string? textJustify { get => GetStyle("text-justify"); set => SetStyle("text-justify", value); }
    public string? textOverflow { get => GetStyle("text-overflow"); set => SetStyle("text-overflow", value); }
    public string? textShadow { get => GetStyle("text-shadow"); set => SetStyle("text-shadow", value); }
    public string? textTransform { get => GetStyle("text-transform"); set => SetStyle("text-transform", value); }
    public string? top { get => GetStyle("top"); set => SetStyle("top", value); }
    public string? touchAction { get => GetStyle("touch-action"); set => SetStyle("touch-action", value); }
    public string? transform { get => GetStyle("transform"); set => SetStyle("transform", value); }
    public string? transformOrigin { get => GetStyle("transform-origin"); set => SetStyle("transform-origin", value); }
    public string? transition { get => GetStyle("transition"); set => SetStyle("transition", value); }
    public string? transitionDelay { get => GetStyle("transition-delay"); set => SetStyle("transition-delay", value); }
    public string? transitionDuration { get => GetStyle("transition-duration"); set => SetStyle("transition-duration", value); }
    public string? transitionProperty { get => GetStyle("transition-property"); set => SetStyle("transition-property", value); }
    public string? transitionTimingFunction { get => GetStyle("transition-timing-function"); set => SetStyle("transition-timing-function", value); }
    public string? unicodeBidi { get => GetStyle("unicode-bidi"); set => SetStyle("unicode-bidi", value); }
    public string? userSelect { get => GetStyle("user-select"); set => SetStyle("user-select", value); }
    public string? verticalAlign { get => GetStyle("vertical-align"); set => SetStyle("vertical-align", value); }
    public string? visibility { get => GetStyle("visibility"); set => SetStyle("visibility", value); }
    public string? whiteSpace { get => GetStyle("white-space"); set => SetStyle("white-space", value); }
    public string? widows { get => GetStyle("widows"); set => SetStyle("widows", value); }
    public string? width { get => GetStyle("width"); set => SetStyle("width", value); }
    public string? willChange { get => GetStyle("will-change"); set => SetStyle("will-change", value); }
    public string? wordBreak { get => GetStyle("word-break"); set => SetStyle("word-break", value); }
    public string? wordSpacing { get => GetStyle("word-spacing"); set => SetStyle("word-spacing", value); }
    public string? wordWrap { get => GetStyle("word-wrap"); set => SetStyle("word-wrap", value); }
    public string? writingMode { get => GetStyle("writing-mode"); set => SetStyle("writing-mode", value); }
    public string? zIndex { get => GetStyle("z-index"); set => SetStyle("z-index", value); }

    private string? GetStyle(string name)
    {
        return _element.Style.TryGetValue(name, out var val) ? val : null;
    }

    private void SetStyle(string name, string? value)
    {
        if (value == null)
            _element.Style.Remove(name);
        else
            _element.Style[name] = value;
        DirtyState.AddSelf(_element, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
        DomMutationTracker.Notify();
    }
}