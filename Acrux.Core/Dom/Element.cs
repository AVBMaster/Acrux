using SkiaSharp;
using Acrux.Core.Performance;

namespace Acrux.Core.Dom;

public abstract class Element : Node
{
    private string[]? _classListCache;

    protected Element(string tagName) : base()
    {
        InternalState.LocalName = tagName;
        InternalState.NamespaceUri = "http://www.w3.org/1999/xhtml";
        TagName = tagName.ToUpperInvariant();
    }

    public override NodeType NodeType => NodeType.Element;

    public string TagName { get; set; } = string.Empty;
    public string? NamespaceUri { get => InternalState.NamespaceUri; set => InternalState.NamespaceUri = value; }
    public string? Prefix { get => InternalState.Prefix; set => InternalState.Prefix = value; }
    public string LocalName => InternalState.LocalName;
    public override string NodeName => TagName;

    // ===== Attributes =====
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAttributes() => Attributes.Count > 0;
    public string[] GetAttributeNames() => Attributes.Keys.ToArray();

    public string? GetAttribute(string name) =>
        Attributes.TryGetValue(name, out var val) ? val : null;

    public void SetAttribute(string name, string value)
    {
        var oldValue = Attributes.GetValueOrDefault(name);
        Attributes[name] = value;
        _classListCache = null;

        OnAttributeChanged(name, oldValue, value);

        if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
            _classListCache = null;

        DirtyState.AddSelf(this, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
        DomMutationTracker.Notify();
    }

    public void RemoveAttribute(string name)
    {
        var oldValue = Attributes.GetValueOrDefault(name);
        Attributes.Remove(name);
        _classListCache = null;

        OnAttributeChanged(name, oldValue, null);

        if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
            _classListCache = null;

        DirtyState.AddSelf(this, DirtyFlags.Style | DirtyFlags.Layout | DirtyFlags.Paint);
        DomMutationTracker.Notify();
    }

    public bool HasAttribute(string name) => Attributes.ContainsKey(name);

    public string? GetAttributeNS(string? ns, string name) => GetAttribute(name);
    public void SetAttributeNS(string? ns, string name, string value) => SetAttribute(name, value);
    public void RemoveAttributeNS(string? ns, string name) => RemoveAttribute(name);
    public bool HasAttributeNS(string? ns, string name) => HasAttribute(name);

    // ===== ClassList =====
    public string[] ClassList
    {
        get
        {
            if (_classListCache == null)
            {
                var classAttr = GetAttribute("class");
                _classListCache = string.IsNullOrWhiteSpace(classAttr)
                    ? Array.Empty<string>()
                    : classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
            return _classListCache;
        }
    }

    public bool HasClass(string className) => ClassList.Contains(className);

    public string? ClassName
    {
        get => GetAttribute("class");
        set
        {
            if (value != null) SetAttribute("class", value);
            else RemoveAttribute("class");
            _classListCache = null;
        }
    }

    public string? Id
    {
        get => GetAttribute("id");
        set
        {
            if (value != null) SetAttribute("id", value);
            else RemoveAttribute("id");
        }
    }

    // ===== DOM Layout Properties =====
    private string? _slot;
    public string? Slot
    {
        get => _slot ?? GetAttribute("slot");
        set { _slot = value; if (value != null) SetAttribute("slot", value); else RemoveAttribute("slot"); }
    }

    public ComputedStyle? ComputedStyle { get; set; }
    public LayoutBox? LayoutBox { get; set; }

    /// <summary>The sheet a <style> or <link> element contributes to the cascade, and the
    /// object the cascade itself resolves from. Keeping the reference on the element is what
    /// lets <c>element.sheet</c> (CSSOM §5.2) hand out the live sheet: a rule inserted
    /// through it is in the cascade on the next recompute, not in a copy.</summary>
    public Acrux.Core.Css.Rules.StyleSheetContents? AssociatedStyleSheet { get; set; }

    /// <summary>'disabled' set through the DOM (CSSOM §5.2). The reference engine keeps it as
    /// element state that the content attribute initialises but the IDL setter never mirrors
    /// back, so <c>styleEl.disabled = true</c> hides the sheet while
    /// <c>hasAttribute('disabled')</c> stays false (measured). Null means the attribute alone
    /// decides, which is also what a sheet authored with the attribute looks like.</summary>
    public bool? SheetDisabledState { get; set; }

    /// <summary>Whether the element's sheet is currently out of the cascade.</summary>
    public bool SheetDisabled => SheetDisabledState ?? HasAttribute("disabled");

    public Dictionary<string, string> Style { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string>? BeforeStyles { get; set; }
    public Dictionary<string, string>? AfterStyles { get; set; }
    /// <summary>Collected ::marker declarations (color/font of the generated marker).</summary>
    public Dictionary<string, string>? MarkerStyles { get; set; }
    /// <summary>Collected ::first-line declarations, merged with the box style at layout time.</summary>
    public Dictionary<string, string>? FirstLineStyles { get; set; }
    /// <summary>Collected ::first-letter declarations (CSS Pseudo-Elements 4 §3).</summary>
    public Dictionary<string, string>? FirstLetterStyles { get; set; }

    /// <summary>Collected ::-webkit-scrollbar-* part styles (side-car).</summary>
    public ScrollbarStyles? ScrollbarCustom { get; set; }
    public bool HasGeneratedBefore { get; set; }
    public bool HasGeneratedAfter { get; set; }
    /// <summary>True for a box the engine materialized from a 'content' value
    /// (::before, ::after, a floated ::first-letter). It has no node in the author's DOM,
    /// so it can never be the target of a pointer event either — a hit inside it belongs
    /// to the element that declared it.</summary>
    public bool IsGeneratedPseudoElement { get; set; }
    /// <summary>Set when a floated ::first-letter box was materialized (CSS
    /// Pseudo-Elements 4 §3): the letter no longer belongs to the text run, so the
    /// inline ::first-letter slicing must not claim the character after it.</summary>
    public bool HasGeneratedFirstLetter { get; set; }
    public object? MarkerLayoutObject { get; set; }

    // ===== Common Attribute Shortcuts =====
    public string? Src => GetAttribute("src");
    public string? Href => GetAttribute("href");
    public string? StyleAttr => GetAttribute("style");
    public string? Alt => GetAttribute("alt");
    public string? Type => GetAttribute("type");

    public float IntrinsicWidth { get; set; }
    public float IntrinsicHeight { get; set; }

    private string? _value;
    public string? Value
    {
        get => _value ??= GetAttribute("value");
        set
        {
            _value = value;
            SetAttribute("value", value ?? "");
        }
    }

    public string? Name => GetAttribute("name");
    public string? InputType => GetAttribute("type");
    public bool IsFormElement => TagName is "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON";

    // True for controls that accept typed text: text-like <input> and <textarea>.
    public bool IsTextEditable =>
        TagName == "TEXTAREA" || (TagName == "INPUT" && InputType?.ToLowerInvariant() is
            null or "text" or "password" or "email" or "search" or "tel" or "url" or "number");

    public int SelectionStart { get; set; }
    public int SelectionEnd { get; set; }
    public bool IsFocused { get; set; }
    public bool IsHovered { get; set; }

    // ===== Shadow DOM =====
    public ShadowRoot? ShadowRoot => InternalState.ShadowRoot;
    public Element? AssignedSlot => null;

    public ShadowRoot AttachShadow(ShadowRootInit init)
    {
        if (InternalState.ShadowRoot != null)
            throw new DOMException("Shadow root already exists", "NotSupportedError");

        var shadowRoot = new ShadowRoot(this, init.Mode, init.DelegatesFocus, init.SlotAssignment)
        {
            Clonable = init.Clonable,
            Serializable = init.Serializable
        };
        InternalState.ShadowRoot = shadowRoot;
        return shadowRoot;
    }

    // ===== Geometry =====
    public SKRect GetBoundingClientRect()
    {
        if (LayoutBox != null && LayoutBox.BorderBox.Width > 0 && LayoutBox.BorderBox.Height > 0)
            return LayoutBox.BorderBox;

        var rect = new SKRect(0, 0, 0, 0);
        bool first = true;

        if (LayoutBox?.LineRuns != null)
        {
            foreach (var run in LayoutBox.LineRuns)
            {
                if (run.Width > 0 || run.Height > 0)
                {
                    var runRect = new SKRect(run.X, 0, run.X + run.Width, run.Height);
                    rect = first ? runRect : SKRect.Union(rect, runRect);
                    first = false;
                }
            }
        }

        foreach (var child in Children.OfType<Element>())
        {
            var childRect = child.GetBoundingClientRect();
            if (childRect.Width > 0 || childRect.Height > 0)
            {
                rect = first ? childRect : SKRect.Union(rect, childRect);
                first = false;
            }
        }

        return rect;
    }

    public Element? OffsetParent
    {
        get
        {
            var parent = ParentElement;
            while (parent != null)
            {
                var pos = parent.ComputedStyle?.Position;
                if (pos != PositionType.Static ||
                    parent.TagName is "TD" or "TH" or "TABLE" or "BODY")
                    return parent;
                parent = parent.ParentElement;
            }
            return null;
        }
    }

    public float OffsetTop
    {
        get
        {
            if (LayoutBox == null) return 0;
            var myTop = LayoutBox.BorderBox.Top;
            var parentTop = OffsetParent?.LayoutBox?.BorderBox.Top ?? 0;
            return myTop - parentTop;
        }
    }

    public float OffsetLeft
    {
        get
        {
            if (LayoutBox == null) return 0;
            var myLeft = LayoutBox.BorderBox.Left;
            var parentLeft = OffsetParent?.LayoutBox?.BorderBox.Left ?? 0;
            return myLeft - parentLeft;
        }
    }

    public float OffsetWidth => LayoutBox?.BorderBox.Width ?? 0;
    public float OffsetHeight => LayoutBox?.BorderBox.Height ?? 0;
    public float ClientWidth => LayoutBox?.PaddingBox.Width ?? 0;
    public float ClientHeight => LayoutBox?.PaddingBox.Height ?? 0;
    public float ScrollWidth => Math.Max(ClientWidth, LayoutBox?.ContentBox.Width ?? 0);
    public float ScrollHeight => Math.Max(ClientHeight, LayoutBox?.ContentBox.Height ?? 0);

    // ===== HTML Children API =====
    public Element? FirstElementChild => Children.OfType<Element>().FirstOrDefault();
    public Element? LastElementChild => Children.OfType<Element>().LastOrDefault();
    public int ChildElementCount => Children.OfType<Element>().Count();
    public HtmlCollection ChildElements => new(Children.OfType<Element>().Cast<Element>().ToList());

    // ===== innerText =====
    public string? InnerText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            CollectInnerText(this, sb);
            return sb.ToString();
        }
        set
        {
            Children.Clear();
            if (value != null)
                AppendChild(new TextNode(value));
        }
    }

    // ===== outerHTML =====
    public string OuterHTML
    {
        get => SerializeElement(this);
        set
        {
            if (ParentNode == null) return;
            var next = NextSibling;
            Remove();
        }
    }

    // ===== scrollIntoView / scrollTo / scrollBy =====

    /// <summary>
    /// Finds the nearest ancestor (or self) that is a scroll container.
    /// </summary>
    public Element? NearestScrollContainer()
    {
        var node = this;
        while (node != null)
        {
            var b = node.LayoutBox;
            if (b != null && b.IsScrollContainer) return node;
            node = node.ParentElement;
        }
        return null;
    }

    public void ScrollIntoView(bool alignToTop = true) =>
        ScrollIntoView(new ScrollIntoViewOptions { Block = alignToTop ? ScrollLogicalPosition.Start : ScrollLogicalPosition.End });

    public void ScrollIntoView(ScrollIntoViewOptions? options = null)
    {
        var box = LayoutBox;
        if (box == null) return;

        // Walk up to the nearest scroll container.
        Element? container = NearestScrollContainer();
        if (container == null) return;
        var cBox = container.LayoutBox;
        if (cBox == null) return;

        var opts = options ?? new ScrollIntoViewOptions();

        // Both boxes are already in absolute page coordinates (no scroll offset).
        float elemPageTop = box.BorderBox.Top;
        float containerContentTop = cBox.ContentBox.Top;
        float elemInContent = elemPageTop - containerContentTop;

        float target = opts.Block switch
        {
            ScrollLogicalPosition.End => elemInContent - cBox.ContentBox.Height + box.BorderBox.Height,
            ScrollLogicalPosition.Center => elemInContent - (cBox.ContentBox.Height - box.BorderBox.Height) / 2,
            _ => elemInContent, // Start / Nearest
        };

        float maxScroll = Math.Max(0, cBox.ScrollContentHeight - cBox.ContentBox.Height);
        target = Math.Clamp(target, 0, maxScroll);

        bool smooth = opts.Behavior == ScrollBehavior.Smooth
            || (opts.Behavior == ScrollBehavior.Auto
                && container.ComputedStyle?.ScrollBehavior == ScrollBehaviorType.Smooth);

        if (smooth)
        {
            cBox.TargetScrollY = target;
            cBox.IsSmoothScrollingY = true;
            cBox.ScrollVelY = 0;
            cBox.IsBouncingY = false;
        }
        else
        {
            cBox.ScrollY = target;
        }
    }

    public void ScrollTo(double x, double y) => ScrollTo(new ScrollToOptions { Left = x, Top = y });

    public void ScrollTo(ScrollToOptions? options = null)
    {
        if (options == null) return;
        var container = NearestScrollContainer();
        var cBox = container?.LayoutBox;
        if (cBox == null) return;

        float maxY = Math.Max(0, cBox.ScrollContentHeight - cBox.ContentBox.Height);
        float maxX = Math.Max(0, cBox.ScrollContentWidth - cBox.ContentBox.Width);
        float tx = Math.Clamp((float)options.Left, 0, maxX);
        float ty = Math.Clamp((float)options.Top, 0, maxY);

        if (options.Behavior == ScrollBehavior.Smooth)
        {
            cBox.TargetScrollX = tx;
            cBox.TargetScrollY = ty;
            cBox.IsSmoothScrollingX = true;
            cBox.IsSmoothScrollingY = true;
            cBox.ScrollVelX = 0;
            cBox.ScrollVelY = 0;
            cBox.IsBouncingX = false;
            cBox.IsBouncingY = false;
        }
        else
        {
            cBox.ScrollX = tx;
            cBox.ScrollY = ty;
        }
    }

    public void ScrollBy(double x, double y) => ScrollTo(new ScrollToOptions { Left = ScrollLeft + x, Top = ScrollTop + y });

    public void ScrollBy(ScrollToOptions? options = null)
    {
        if (options == null) return;
        ScrollTo(new ScrollToOptions { Left = ScrollLeft + options.Left, Top = ScrollTop + options.Top, Behavior = options.Behavior });
    }

    public float ScrollTop
    {
        get => LayoutBox?.ScrollY ?? 0;
        set { if (LayoutBox != null) LayoutBox.ScrollY = value; }
    }

    public float ScrollLeft
    {
        get => LayoutBox?.ScrollX ?? 0;
        set { if (LayoutBox != null) LayoutBox.ScrollX = value; }
    }

    // ===== checkVisibility =====
    public bool CheckVisibility(CheckVisibilityOptions? options = null) => true;

    // ===== part (CSS ::part()) =====
    public DOMTokenList Part => new(GetAttribute("part") ?? "");

    // ===== ARIA reflection =====
    public string? AriaHidden { get => GetAttribute("aria-hidden"); set => SetOrRemoveAttr("aria-hidden", value); }
    public string? AriaDisabled { get => GetAttribute("aria-disabled"); set => SetOrRemoveAttr("aria-disabled", value); }
    public string? AriaLabel { get => GetAttribute("aria-label"); set => SetOrRemoveAttr("aria-label", value); }
    public string? AriaPressed { get => GetAttribute("aria-pressed"); set => SetOrRemoveAttr("aria-pressed", value); }
    public string? AriaExpanded { get => GetAttribute("aria-expanded"); set => SetOrRemoveAttr("aria-expanded", value); }
    public string? AriaChecked { get => GetAttribute("aria-checked"); set => SetOrRemoveAttr("aria-checked", value); }
    public string? AriaCurrent { get => GetAttribute("aria-current"); set => SetOrRemoveAttr("aria-current", value); }
    public string? AriaDescribedBy { get => GetAttribute("aria-describedby"); set => SetOrRemoveAttr("aria-describedby", value); }
    public string? AriaLabelledBy { get => GetAttribute("aria-labelledby"); set => SetOrRemoveAttr("aria-labelledby", value); }
    public string? AriaOwns { get => GetAttribute("aria-owns"); set => SetOrRemoveAttr("aria-owns", value); }
    public string? AriaRelevant { get => GetAttribute("aria-relevant"); set => SetOrRemoveAttr("aria-relevant", value); }
    public string? AriaActiveDescendant { get => GetAttribute("aria-activedescendant"); set => SetOrRemoveAttr("aria-activedescendant", value); }
    public string? AriaAutoComplete { get => GetAttribute("aria-autocomplete"); set => SetOrRemoveAttr("aria-autocomplete", value); }
    public string? AriaBusy { get => GetAttribute("aria-busy"); set => SetOrRemoveAttr("aria-busy", value); }
    public string? AriaColCount { get => GetAttribute("aria-colcount"); set => SetOrRemoveAttr("aria-colcount", value); }
    public string? AriaColIndex { get => GetAttribute("aria-colindex"); set => SetOrRemoveAttr("aria-colindex", value); }
    public string? AriaColSpan { get => GetAttribute("aria-colspan"); set => SetOrRemoveAttr("aria-colspan", value); }
    public string? AriaControls { get => GetAttribute("aria-controls"); set => SetOrRemoveAttr("aria-controls", value); }
    public string? AriaDescription { get => GetAttribute("aria-description"); set => SetOrRemoveAttr("aria-description", value); }
    public string? AriaDetails { get => GetAttribute("aria-details"); set => SetOrRemoveAttr("aria-details", value); }
    public string? AriaDropEffect { get => GetAttribute("aria-dropeffect"); set => SetOrRemoveAttr("aria-dropeffect", value); }
    public string? AriaErrorMessage { get => GetAttribute("aria-errormessage"); set => SetOrRemoveAttr("aria-errormessage", value); }
    public string? AriaFlowTo { get => GetAttribute("aria-flowto"); set => SetOrRemoveAttr("aria-flowto", value); }
    public string? AriaGrabbed { get => GetAttribute("aria-grabbed"); set => SetOrRemoveAttr("aria-grabbed", value); }
    public string? AriaHasPopup { get => GetAttribute("aria-haspopup"); set => SetOrRemoveAttr("aria-haspopup", value); }
    public string? AriaInvalid { get => GetAttribute("aria-invalid"); set => SetOrRemoveAttr("aria-invalid", value); }
    public string? AriaKeyShortcuts { get => GetAttribute("aria-keyshortcuts"); set => SetOrRemoveAttr("aria-keyshortcuts", value); }
    public string? AriaLevel { get => GetAttribute("aria-level"); set => SetOrRemoveAttr("aria-level", value); }
    public string? AriaLive { get => GetAttribute("aria-live"); set => SetOrRemoveAttr("aria-live", value); }
    public string? AriaModal { get => GetAttribute("aria-modal"); set => SetOrRemoveAttr("aria-modal", value); }
    public string? AriaMultiLine { get => GetAttribute("aria-multiline"); set => SetOrRemoveAttr("aria-multiline", value); }
    public string? AriaMultiSelectable { get => GetAttribute("aria-multiselectable"); set => SetOrRemoveAttr("aria-multiselectable", value); }
    public string? AriaOrientation { get => GetAttribute("aria-orientation"); set => SetOrRemoveAttr("aria-orientation", value); }
    public string? AriaPlaceholder { get => GetAttribute("aria-placeholder"); set => SetOrRemoveAttr("aria-placeholder", value); }
    public string? AriaPosInSet { get => GetAttribute("aria-posinset"); set => SetOrRemoveAttr("aria-posinset", value); }
    public string? AriaReadOnly { get => GetAttribute("aria-readonly"); set => SetOrRemoveAttr("aria-readonly", value); }
    public string? AriaRequired { get => GetAttribute("aria-required"); set => SetOrRemoveAttr("aria-required", value); }
    public string? AriaRoleDescription { get => GetAttribute("aria-roledescription"); set => SetOrRemoveAttr("aria-roledescription", value); }
    public string? AriaRowCount { get => GetAttribute("aria-rowcount"); set => SetOrRemoveAttr("aria-rowcount", value); }
    public string? AriaRowIndex { get => GetAttribute("aria-rowindex"); set => SetOrRemoveAttr("aria-rowindex", value); }
    public string? AriaRowSpan { get => GetAttribute("aria-rowspan"); set => SetOrRemoveAttr("aria-rowspan", value); }
    public string? AriaSelected { get => GetAttribute("aria-selected"); set => SetOrRemoveAttr("aria-selected", value); }
    public string? AriaSetSize { get => GetAttribute("aria-setsize"); set => SetOrRemoveAttr("aria-setsize", value); }
    public string? AriaSort { get => GetAttribute("aria-sort"); set => SetOrRemoveAttr("aria-sort", value); }
    public string? AriaValueMax { get => GetAttribute("aria-valuemax"); set => SetOrRemoveAttr("aria-valuemax", value); }
    public string? AriaValueMin { get => GetAttribute("aria-valuemin"); set => SetOrRemoveAttr("aria-valuemin", value); }
    public string? AriaValueNow { get => GetAttribute("aria-valuenow"); set => SetOrRemoveAttr("aria-valuenow", value); }
    public string? AriaValueText { get => GetAttribute("aria-valuetext"); set => SetOrRemoveAttr("aria-valuetext", value); }

    private void SetOrRemoveAttr(string name, string? value)
    {
        if (value != null) SetAttribute(name, value);
        else RemoveAttribute(name);
    }

    // ===== Spec Methods =====
    public Element? Closest(string selector)
    {
        var el = this;
        while (el != null)
        {
            if (MatchesSelector(el, selector))
                return el;
            el = el.ParentElement;
        }
        return null;
    }

    public bool Matches(string selector)
    {
        return Css.CssSelectorMatcher.Matches(selector, this);
    }

    public void Prepend(params Node[] nodes)
    {
        for (int i = nodes.Length - 1; i >= 0; i--)
            InsertBefore(nodes[i], FirstChild);
    }

    public void Append(params Node[] nodes)
    {
        foreach (var node in nodes)
            AppendChild(node);
    }

    public void ReplaceChildren(params Node[] nodes)
    {
        Children.Clear();
        foreach (var node in nodes)
            AppendChild(node);
    }

    public void Before(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        foreach (var node in nodes)
            parent.InsertBefore(node, this);
    }

    public void After(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        var next = NextSibling;
        foreach (var node in nodes)
            parent.InsertBefore(node, next);
    }

    public void ReplaceWith(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        var next = NextSibling;
        Remove();
        foreach (var node in nodes)
            parent.InsertBefore(node, next);
    }

    public void InsertAdjacentElement(string position, Element element)
    {
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                ParentNode?.InsertBefore(element, this);
                break;
            case "afterbegin":
                InsertBefore(element, FirstChild);
                break;
            case "beforeend":
                AppendChild(element);
                break;
            case "afterend":
                ParentNode?.InsertBefore(element, NextSibling);
                break;
        }
    }

    public void InsertAdjacentText(string position, string text)
    {
        var textNode = new TextNode(text);
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                ParentNode?.InsertBefore(textNode, this);
                break;
            case "afterbegin":
                InsertBefore(textNode, FirstChild);
                break;
            case "beforeend":
                AppendChild(textNode);
                break;
            case "afterend":
                ParentNode?.InsertBefore(textNode, NextSibling);
                break;
        }
    }

    public void InsertAdjacentHTML(string position, string text)
    {
        var parsed = JavaScript.HtmlParser.ParseFragment(text, TagName);
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                var parent = ParentNode;
                if (parent != null)
                    for (int i = parsed.Count - 1; i >= 0; i--)
                        parent.InsertBefore(parsed[i], this);
                break;
            case "afterbegin":
                for (int i = parsed.Count - 1; i >= 0; i--)
                    InsertBefore(parsed[i], FirstChild);
                break;
            case "beforeend":
                foreach (var node in parsed)
                    AppendChild(node);
                break;
            case "afterend":
                var parent2 = ParentNode;
                var next = NextSibling;
                if (parent2 != null)
                    foreach (var node in parsed)
                        parent2.InsertBefore(node, next);
                break;
        }
    }

    // ===== Hooks =====
    protected override void OnAttributeChanged(string name, string? oldValue, string? newValue)
    {
        base.OnAttributeChanged(name, oldValue, newValue);
    }

    // ===== Static helpers =====
    private static void CollectInnerText(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode text)
        {
            sb.Append(text.Data);
        }
        else if (node is Element el)
        {
            var display = el.ComputedStyle?.Display;
            if (display is DisplayType.Block or DisplayType.Flex or DisplayType.Grid or DisplayType.Table)
            {
                if (sb.Length > 0 && sb[^1] != '\n')
                    sb.Append('\n');
            }
            if (el.TagName == "BR")
                sb.Append('\n');
            else
            {
                foreach (var child in el.Children)
                    CollectInnerText(child, sb);
            }
        }
    }

    private static string SerializeElement(Element el)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('<').Append(el.TagName.ToLowerInvariant());
        foreach (var attr in el.Attributes)
            sb.Append(' ').Append(attr.Key).Append("=\"").Append(attr.Value.Replace("\"", "&quot;")).Append('"');
        if (el.Children.Count == 0 && !voidElements.Contains(el.TagName.ToLowerInvariant()))
        {
            sb.Append("></").Append(el.TagName.ToLowerInvariant()).Append('>');
        }
        else if (voidElements.Contains(el.TagName.ToLowerInvariant()))
        {
            sb.Append(" />");
        }
        else
        {
            sb.Append('>');
            foreach (var child in el.Children)
            {
                if (child is Element childEl)
                    sb.Append(SerializeElement(childEl));
                else if (child is TextNode text)
                {
                    var data = text.Data;
                    data = data.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                    sb.Append(data);
                }
                else if (child is CommentNode comment)
                    sb.Append("<!--").Append(comment.Data).Append("-->");
            }
            sb.Append("</").Append(el.TagName.ToLowerInvariant()).Append('>');
        }
        return sb.ToString();
    }

    private static readonly HashSet<string> voidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    // ===== Private =====
    private static bool MatchesSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }
}
