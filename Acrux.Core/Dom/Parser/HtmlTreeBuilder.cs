using Acrux.Core.Dom.Html;

namespace Acrux.Core.Dom.Parser;

internal class HtmlTreeBuilder
{
    private enum InsertionMode
    {
        Initial,
        BeforeHtml,
        BeforeHead,
        InHead,
        InHeadNoscript,
        AfterHead,
        TemplateContents,
        InBody,
        Text,
        InTable,
        InTableText,
        InCaption,
        InColumnGroup,
        InTableBody,
        InRow,
        InCell,
        InSelect,
        InSelectInTable,
        AfterBody,
        InFrameset,
        AfterFrameset,
        AfterAfterBody,
        AfterAfterFrameset,
    }

    private readonly HtmlDocumentParser _parser;
    private readonly HtmlConstructionSite _tree;
    private InsertionMode _insertionMode = InsertionMode.Initial;
    private InsertionMode _originalInsertionMode = InsertionMode.Initial;
    private readonly List<InsertionMode> _templateInsertionModes = new();
    private bool _framesetOk = true;
    private bool _shouldSkipLeadingNewline;
    private readonly HtmlParserOptions _options;

    public HtmlTreeBuilder(HtmlDocumentParser parser, Document document, HtmlParserOptions options)
    {
        _parser = parser;
        _tree = new HtmlConstructionSite(document);
        _options = options;
        _insertionMode = InsertionMode.Initial;
    }

    public HtmlConstructionSite Tree => _tree;
    public bool IsParsingFragment => false;
    public bool HasParserBlockingScript => false;
    public bool FramesetOk { get => _framesetOk; set => _framesetOk = value; }

    public void ConstructTree(AtomicHtmlToken token)
    {
        ProcessToken(token);
        _tree.Flush();
    }

    public void Flush() => _tree.Flush();

    public void Finished()
    {
        _tree.ProcessEndOfFile();
        _tree.FinishedParsing();
    }

    public void Detach() { }

    private void ProcessToken(AtomicHtmlToken token)
    {
        if (token.Type == HtmlToken.TokenType.Character)
        {
            ProcessCharacter(token);
            return;
        }

        _tree.Flush();
        _shouldSkipLeadingNewline = false;

        switch (token.Type)
        {
            case HtmlToken.TokenType.Doctype:
                ProcessDoctypeToken(token);
                break;
            case HtmlToken.TokenType.StartTag:
                ProcessStartTag(token);
                break;
            case HtmlToken.TokenType.EndTag:
                ProcessEndTag(token);
                break;
            case HtmlToken.TokenType.Comment:
                ProcessComment(token);
                break;
            case HtmlToken.TokenType.EndOfFile:
                ProcessEndOfFile(token);
                break;
        }
    }

    private void ProcessDoctypeToken(AtomicHtmlToken token)
    {
        if (_insertionMode == InsertionMode.Initial)
        {
            _tree.InsertDoctype(token);
            _insertionMode = InsertionMode.BeforeHtml;
            return;
        }
        if (_insertionMode == InsertionMode.InTableText)
        {
            DefaultForInTableText();
            ProcessDoctypeToken(token);
            return;
        }
    }

    private void ProcessStartTag(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (_insertionMode)
        {
            case InsertionMode.Initial:
                DefaultForInitial();
                goto case InsertionMode.BeforeHtml;

            case InsertionMode.BeforeHtml:
                if (tagName == "html")
                {
                    _tree.InsertHtmlHtmlStartTagBeforeHTML(token);
                    _insertionMode = InsertionMode.BeforeHead;
                    return;
                }
                DefaultForBeforeHtml();
                goto case InsertionMode.BeforeHead;

            case InsertionMode.BeforeHead:
                if (tagName == "html")
                {
                    ProcessHtmlStartTagForInBody(token);
                    return;
                }
                if (tagName == "head")
                {
                    _tree.InsertHtmlHeadElement(token);
                    _insertionMode = InsertionMode.InHead;
                    return;
                }
                DefaultForBeforeHead();
                goto case InsertionMode.InHead;

            case InsertionMode.InHead:
                if (ProcessStartTagForInHead(token))
                    return;
                DefaultForInHead();
                goto case InsertionMode.AfterHead;

            case InsertionMode.AfterHead:
                switch (tagName)
                {
                    case "html":
                        ProcessHtmlStartTagForInBody(token);
                        return;
                    case "body":
                        _framesetOk = false;
                        _tree.InsertHtmlBodyElement(token);
                        _insertionMode = InsertionMode.InBody;
                        return;
                    case "frameset":
                        _tree.InsertHtmlelement(token);
                        _insertionMode = InsertionMode.InFrameset;
                        return;
                    case "base": case "basefont": case "bgsound": case "link":
                    case "meta": case "noframes": case "script": case "style":
                    case "template": case "title":
                        _tree.InsertHtmlelement(token);
                        _tree.OpenElements.Pop();
                        return;
                    case "head":
                        return;
                    default:
                        break;
                }
                DefaultForAfterHead();
                goto case InsertionMode.InBody;

            case InsertionMode.InTable:
                ProcessStartTagForInTable(token);
                break;

            case InsertionMode.InTableBody:
                ProcessStartTagForInTableBody(token);
                break;

            case InsertionMode.InRow:
                ProcessStartTagForInRow(token);
                break;

            case InsertionMode.InCell:
                ProcessStartTagForInCell(token);
                break;

            case InsertionMode.InSelect:
                ProcessStartTagForInSelect(token);
                break;

            case InsertionMode.InCaption:
                ProcessStartTagForInCaption(token);
                break;

            case InsertionMode.InColumnGroup:
                ProcessStartTagForInColumnGroup(token);
                break;

            case InsertionMode.InTableText:
                DefaultForInTableText();
                ProcessStartTag(token);
                break;

            case InsertionMode.AfterBody:
            case InsertionMode.AfterAfterBody:
                if (tagName == "html")
                {
                    ProcessHtmlStartTagForInBody(token);
                    return;
                }
                _tree.Flush();
                _insertionMode = InsertionMode.InBody;
                goto case InsertionMode.InBody;

            case InsertionMode.InBody:
                ProcessStartTagForInBody(token);
                break;

            default:
                ProcessStartTagForInBody(token);
                break;
        }
    }

    private void ProcessEndTag(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (_insertionMode)
        {
            case InsertionMode.Initial:
                DefaultForInitial();
                goto case InsertionMode.BeforeHtml;

            case InsertionMode.BeforeHtml:
                if (tagName == "head" || tagName == "body" || tagName == "html" || tagName == "br")
                {
                    DefaultForBeforeHtml();
                }
                else
                    return;
                goto case InsertionMode.BeforeHead;

            case InsertionMode.BeforeHead:
                if (tagName == "head" || tagName == "body" || tagName == "html" || tagName == "br")
                {
                    DefaultForBeforeHead();
                }
                else
                    return;
                goto case InsertionMode.InHead;

            case InsertionMode.InHead:
                if (tagName == "template")
                {
                    ProcessTemplateEndTag(token);
                    return;
                }
                if (tagName == "head")
                {
                    _tree.OpenElements.PopHtmlHeadElement();
                    _insertionMode = InsertionMode.AfterHead;
                    return;
                }
                if (tagName == "body" || tagName == "html" || tagName == "br")
                {
                    DefaultForInHead();
                    goto case InsertionMode.AfterHead;
                }
                return;

            case InsertionMode.AfterHead:
                if (tagName == "body" || tagName == "html" || tagName == "br")
                {
                    DefaultForAfterHead();
                    goto case InsertionMode.InBody;
                }
                return;

            case InsertionMode.InBody:
                ProcessEndTagForInBody(token);
                break;

            case InsertionMode.InTable:
                ProcessEndTagForInTable(token);
                break;

            case InsertionMode.InTableBody:
                ProcessEndTagForInTableBody(token);
                break;

            case InsertionMode.InRow:
                ProcessEndTagForInRow(token);
                break;

            case InsertionMode.InCell:
                ProcessEndTagForInCell(token);
                break;

            case InsertionMode.InSelect:
                ProcessEndTagForInSelect(token);
                break;

            case InsertionMode.InCaption:
                ProcessEndTagForInCaption(token);
                break;

            case InsertionMode.InColumnGroup:
                ProcessEndTagForInColumnGroup(token);
                break;

            // Fix #9: an end tag in text mode just closes the current text element.
            case InsertionMode.Text:
                _tree.OpenElements.Pop();
                _insertionMode = _originalInsertionMode;
                break;

            case InsertionMode.InTableText:
                DefaultForInTableText();
                ProcessEndTag(token);
                break;

            case InsertionMode.AfterBody:
            case InsertionMode.AfterAfterBody:
                if (tagName == "html")
                {
                    _insertionMode = InsertionMode.AfterAfterBody;
                    return;
                }
                _tree.Flush();
                _insertionMode = InsertionMode.InBody;
                goto case InsertionMode.InBody;

            case InsertionMode.InSelectInTable:
                if (tagName == "caption" || tagName == "table" || tagName == "tbody" ||
                    tagName == "tfoot" || tagName == "thead" || tagName == "tr" ||
                    tagName == "td" || tagName == "th")
                {
                    if (_tree.OpenElements.InTableScope(tagName))
                    {
                        ProcessEndTag(new AtomicHtmlToken(HtmlToken.TokenType.EndTag, "select"));
                        ProcessEndTag(token);
                    }
                    return;
                }
                goto case InsertionMode.InSelect;

            case InsertionMode.InFrameset:
                if (tagName == "frameset")
                {
                    _tree.OpenElements.Pop();
                    if (!_tree.IsEmpty && _tree.CurrentStackItem?.Name.ToLowerInvariant() != "frameset")
                        _insertionMode = InsertionMode.AfterFrameset;
                    return;
                }
                break;

            case InsertionMode.AfterFrameset:
            case InsertionMode.AfterAfterFrameset:
                if (tagName == "html")
                    _insertionMode = InsertionMode.AfterAfterFrameset;
                break;

            case InsertionMode.InHeadNoscript:
                if (tagName == "noscript" && _tree.CurrentStackItem?.Name.ToLowerInvariant() == "noscript")
                {
                    _tree.OpenElements.Pop();
                    _insertionMode = InsertionMode.InHead;
                    return;
                }
                if (tagName != "br")
                    return;
                break;

            case InsertionMode.TemplateContents:
                if (tagName == "template")
                    ProcessTemplateEndTag(token);
                break;

            default:
                ProcessEndTagForInBody(token);
                break;
        }
    }

    private void ProcessComment(AtomicHtmlToken token)
    {
        _tree.InsertComment(token);
    }

    private void ProcessCharacter(AtomicHtmlToken token)
    {
        string data = token.Data;
        if (_shouldSkipLeadingNewline)
        {
            if (data.Length > 0 && data[0] == '\n')
                data = data.Substring(1);
            _shouldSkipLeadingNewline = false;
        }

        // Fix #8: in text mode just insert the text.
        if (_insertionMode == InsertionMode.Text)
        {
            _tree.InsertTextNode(data);
            return;
        }

        if (_insertionMode == InsertionMode.InTableText)
        {
            bool allWhitespace = true;
            foreach (char c in data)
            {
                if (!IsHtmlSpace(c))
                {
                    allWhitespace = false;
                    break;
                }
            }
            if (allWhitespace)
            {
                _tree.InsertTextNode(data);
                return;
            }
            DefaultForInTableText();
        }

        // Handle insertion modes that should ignore whitespace or re-process.
        bool isWhitespaceOnly = true;
        foreach (char c in data)
        {
            if (!IsHtmlSpace(c))
            {
                isWhitespaceOnly = false;
                break;
            }
        }

        switch (_insertionMode)
        {
            case InsertionMode.Initial:
                if (isWhitespaceOnly)
                    return;
                DefaultForInitial();
                ProcessCharacter(token);
                return;

            case InsertionMode.BeforeHtml:
                if (isWhitespaceOnly)
                    return;
                DefaultForBeforeHtml();
                ProcessCharacter(token);
                return;

            case InsertionMode.BeforeHead:
                if (isWhitespaceOnly)
                    return;
                DefaultForBeforeHead();
                ProcessCharacter(token);
                return;

            case InsertionMode.AfterHead:
                if (isWhitespaceOnly)
                {
                    _tree.InsertTextNode(data);
                    return;
                }
                DefaultForAfterHead();
                ProcessCharacter(token);
                return;

            case InsertionMode.AfterBody:
            case InsertionMode.AfterAfterBody:
                if (isWhitespaceOnly)
                {
                    _tree.InsertTextNode(data);
                    return;
                }
                // Anything else: run default steps (re-processed via fall-through)
                break;

            case InsertionMode.InFrameset:
            case InsertionMode.AfterFrameset:
                if (isWhitespaceOnly)
                {
                    _tree.InsertTextNode(data);
                    return;
                }
                break;
        }

        _tree.InsertTextNode(data);
    }

    private void ProcessEndOfFile(AtomicHtmlToken token)
    {
        _tree.ProcessEndOfFile();
    }

    private void ProcessHtmlStartTagForInBody(AtomicHtmlToken token)
    {
        _tree.InsertHtmlHtmlStartTagInBody(token);
    }

    private bool ProcessStartTagForInHead(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "html":
                ProcessHtmlStartTagForInBody(token);
                return true;
            case "base": case "basefont": case "bgsound": case "link":
            case "meta":
                _tree.InsertHtmlelement(token);
                _tree.OpenElements.Pop();
                return true;
            case "title":
                _tree.InsertHtmlelement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.RcdataState);
                _originalInsertionMode = _insertionMode;
                _insertionMode = InsertionMode.Text;
                return true;
            case "noscript":
                if (_options.ScriptingFlag)
                {
                    _tree.InsertHtmlelement(token);
                    _parser.Tokenizer.SetState(HtmlTokenizer.State.RawtextState);
                    _insertionMode = InsertionMode.InHeadNoscript;
                    return true;
                }
                _tree.InsertHtmlelement(token);
                return true;
            case "noframes": case "style":
                _tree.InsertHtmlelement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.RawtextState);
                _originalInsertionMode = _insertionMode;
                _insertionMode = InsertionMode.Text;
                return true;
            case "script":
                _tree.InsertScriptElement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.ScriptDataState);
                _originalInsertionMode = _insertionMode;
                _insertionMode = InsertionMode.Text;
                return true;
            case "template":
                ProcessTemplateStartTag(token);
                return true;
            case "head":
                return true;
            default:
                return false;
        }
    }

    private void ProcessStartTagForInBody(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "html":
                ProcessHtmlStartTagForInBody(token);
                break;
            case "base": case "basefont": case "bgsound": case "command":
            case "link": case "meta": case "noframes": case "script":
            case "style": case "title": case "template":
                ProcessStartTagForInHead(token);
                break;
            case "body":
                if (!_tree.OpenElements.SecondElementIsHtmlBodyElement ||
                    _tree.OpenElements.HasTemplateInHtmlScope)
                    break;
                _framesetOk = false;
                _tree.InsertHtmlBodyStartTagInBody(token);
                break;
            case "frameset":
                if (!_tree.OpenElements.SecondElementIsHtmlBodyElement ||
                    _tree.OpenElements.HasOnlyOneElement)
                    break;
                if (!_framesetOk) break;
                _tree.OpenElements.BodyElement?.Remove();
                _tree.OpenElements.PopUntilPopped(_tree.OpenElements.BodyElement!.LocalName);
                _tree.OpenElements.PopHtmlBodyElement();
                _tree.InsertHtmlelement(token);
                _insertionMode = InsertionMode.InFrameset;
                break;
            case "address": case "article": case "aside": case "blockquote":
            case "center": case "details": case "dialog": case "dir":
            case "div": case "dl": case "fieldset": case "figcaption":
            case "figure": case "footer": case "header": case "hgroup":
            case "main": case "menu": case "nav": case "ol": case "p":
            case "section": case "summary": case "ul":
                ProcessFakePEndTagIfPInButtonScope();
                _tree.InsertHtmlelement(token);
                break;
            case "li":
                ProcessCloseWhenNestedTag(token, item => item.Name.ToLowerInvariant() == "li");
                break;
            case "dd": case "dt":
                ProcessCloseWhenNestedTag(token, item => item.Name.ToLowerInvariant() == "dd" || item.Name.ToLowerInvariant() == "dt");
                break;
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                ProcessFakePEndTagIfPInButtonScope();
                if (_tree.CurrentStackItem?.IsNumberedHeaderElement == true)
                    _tree.OpenElements.Pop();
                _tree.InsertHtmlelement(token);
                break;
            case "pre": case "listing":
                ProcessFakePEndTagIfPInButtonScope();
                _tree.InsertHtmlelement(token);
                _shouldSkipLeadingNewline = true;
                _framesetOk = false;
                break;
            case "form":
                _tree.InsertHtmlelement(token);
                break;
            case "a":
                var activeA = _tree.ActiveFormattingElements.ClosestElementInScopeWithName("a");
                if (activeA != null)
                {
                    _tree.ActiveFormattingElements.Remove(activeA);
                    if (_tree.OpenElements.Contains(activeA))
                        _tree.OpenElements.Remove(activeA);
                }
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertFormattingElement(token);
                break;
            case "b": case "big": case "code": case "em": case "font":
            case "i": case "s": case "small": case "strike": case "strong":
            case "tt": case "u":
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertFormattingElement(token);
                break;
            case "nobr":
                _tree.ReconstructTheActiveFormattingElements();
                if (_tree.OpenElements.InScope("nobr"))
                {
                    _tree.ActiveFormattingElements.Remove(_tree.OpenElements.Top!);
                    _tree.OpenElements.Pop();
                    _tree.ReconstructTheActiveFormattingElements();
                }
                _tree.InsertFormattingElement(token);
                break;
            case "applet": case "marquee": case "object":
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertHtmlelement(token);
                _tree.ActiveFormattingElements.AppendMarker();
                _framesetOk = false;
                break;
            case "table":
                _tree.InsertHtmlelement(token);
                _framesetOk = false;
                _insertionMode = InsertionMode.InTable;
                break;
            case "br": case "area": case "embed": case "img": case "input":
            case "keygen": case "wbr":
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertSelfClosingHtmlElementDestroyingToken(token);
                _framesetOk = false;
                break;
            case "hr":
                ProcessFakePEndTagIfPInButtonScope();
                _tree.InsertSelfClosingHtmlElementDestroyingToken(token);
                _framesetOk = false;
                break;
            case "textarea":
                _tree.InsertHtmlelement(token);
                _shouldSkipLeadingNewline = true;
                _parser.Tokenizer.SetState(HtmlTokenizer.State.RcdataState);
                _originalInsertionMode = _insertionMode;
                _framesetOk = false;
                _insertionMode = InsertionMode.Text;
                break;
            case "xmp":
                ProcessFakePEndTagIfPInButtonScope();
                _tree.ReconstructTheActiveFormattingElements();
                _framesetOk = false;
                _tree.InsertHtmlelement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.RawtextState);
                _originalInsertionMode = _insertionMode;
                _insertionMode = InsertionMode.Text;
                break;
            case "iframe":
                _framesetOk = false;
                _tree.InsertHtmlelement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.RawtextState);
                _originalInsertionMode = _insertionMode;
                _insertionMode = InsertionMode.Text;
                break;
            case "noembed": case "noscript":
                _tree.InsertHtmlelement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.RawtextState);
                _originalInsertionMode = _insertionMode;
                _insertionMode = InsertionMode.Text;
                break;
            case "plaintext":
                ProcessFakePEndTagIfPInButtonScope();
                _tree.InsertHtmlelement(token);
                _parser.Tokenizer.SetState(HtmlTokenizer.State.PlaintextState);
                break;
            case "select":
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertHtmlelement(token);
                _framesetOk = false;
                _insertionMode = InsertionMode.InSelect;
                break;
            case "optgroup": case "option":
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "option")
                {
                    _tree.OpenElements.Pop();
                }
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertHtmlelement(token);
                break;
            case "caption": case "col": case "colgroup": case "frame":
            case "head": case "tbody": case "td": case "tfoot":
            case "th": case "thead": case "tr":
                break;
            case "image":
                _tree.InsertHtmlelement(token);
                _tree.InsertSelfClosingHtmlElementDestroyingToken(token);
                break;
            default:
                _tree.ReconstructTheActiveFormattingElements();
                _tree.InsertHtmlelement(token);
                break;
        }
    }

    private void ProcessEndTagForInBody(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "body":
                if (_tree.OpenElements.InScope("body"))
                    _insertionMode = InsertionMode.AfterBody;
                break;
            case "html":
                if (_tree.OpenElements.InScope("body"))
                {
                    ProcessEndTagForInBody(new AtomicHtmlToken(HtmlToken.TokenType.EndTag, "body"));
                    ProcessEndTag(token);
                }
                break;
            case "br":
                ProcessStartTag(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "br"));
                break;
            case "p":
                if (!_tree.OpenElements.InButtonScope("p"))
                    ProcessStartTag(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "p"));
                _tree.GenerateImpliedEndTagsWithExclusion("p");
                _tree.OpenElements.PopUntilPopped("p");
                break;
            case "li":
                _tree.OpenElements.PopUntilPopped("li");
                break;
            case "dd": case "dt":
                _tree.OpenElements.PopUntilPopped(tagName);
                break;
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                _tree.GenerateImpliedEndTags();
                if (_tree.CurrentStackItem?.IsNumberedHeaderElement == true)
                    _tree.OpenElements.Pop();
                break;
            case "a": case "b": case "big": case "code": case "em": case "font":
            case "i": case "nobr": case "s": case "small": case "strike":
            case "strong": case "tt": case "u":
                CallTheAdoptionAgency(token);
                break;
            case "applet": case "marquee": case "object":
                if (!_tree.OpenElements.InScope(tagName)) break;
                _tree.GenerateImpliedEndTags();
                _tree.OpenElements.PopUntilPopped(tagName);
                _tree.ActiveFormattingElements.ClearToLastMarker();
                break;
            case "template":
                _tree.OpenElements.PopUntilPopped("template");
                break;
            default:
                ProcessAnyOtherEndTagForInBody(token);
                break;
        }
    }

    private void ProcessStartTagForInTable(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "caption":
                _tree.OpenElements.PopUntilTableScopeMarker();
                _tree.ActiveFormattingElements.AppendMarker();
                _tree.InsertHtmlelement(token);
                _insertionMode = InsertionMode.InCaption;
                return;
            case "colgroup":
                _tree.OpenElements.PopUntilTableScopeMarker();
                _tree.InsertHtmlelement(token);
                _insertionMode = InsertionMode.InColumnGroup;
                return;
            case "col":
                _tree.OpenElements.PopUntilTableScopeMarker();
                _tree.InsertHtmlelement(token);
                _tree.OpenElements.Pop();
                _insertionMode = InsertionMode.InColumnGroup;
                return;
            case "tbody": case "tfoot": case "thead":
                _tree.OpenElements.PopUntilTableScopeMarker();
                _tree.InsertHtmlelement(token);
                _insertionMode = InsertionMode.InTableBody;
                return;
            case "td": case "th": case "tr":
                // Spec: act as if a start tag token with the tag name "tbody"
                // had been seen, then reprocess the current token. The previous
                // code inserted the row, popped it, and inserted a second
                // anonymous <tr>, which orphaned the row's attributes.
                _tree.OpenElements.PopUntilTableScopeMarker();
                _tree.InsertHtmlelement(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "tbody"));
                _insertionMode = InsertionMode.InTableBody;
                ProcessStartTag(token);
                return;
            case "table":
                _tree.OpenElements.PopUntilPopped("table");
                _insertionMode = InsertionMode.InBody;
                return;
            case "style": case "script":
                ProcessStartTagForInHead(token);
                return;
            case "input":
                _tree.InsertSelfClosingHtmlElementDestroyingToken(token);
                return;
            case "form":
                _tree.InsertHtmlelement(token);
                _tree.OpenElements.Pop();
                return;
            case "template":
                ProcessStartTagForInHead(token);
                return;
            default:
                ProcessStartTagForInBody(token);
                break;
        }
    }

    private void ProcessEndTagForInTable(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        if (tagName == "table")
        {
            if (!_tree.OpenElements.InTableScope("table")) return;
            _tree.OpenElements.PopUntilPopped("table");
            ResetInsertionModeAppropriately();
            return;
        }
        if (tagName == "template")
        {
            _tree.OpenElements.PopUntilPopped("template");
            return;
        }
    }

    private void ProcessStartTagForInTableBody(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "tr":
                _tree.OpenElements.PopUntilTableBodyScopeMarker();
                _tree.InsertHtmlelement(token);
                _insertionMode = InsertionMode.InRow;
                return;
            case "th": case "td":
                _tree.OpenElements.PopUntilTableBodyScopeMarker();
                _tree.InsertHtmlelement(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "tr"));
                _insertionMode = InsertionMode.InRow;
                ProcessStartTag(token);
                return;
            case "caption": case "col": case "colgroup": case "tbody":
            case "tfoot": case "thead":
                _tree.OpenElements.PopUntilPopped("tbody");
                _insertionMode = InsertionMode.InTable;
                ProcessStartTag(token);
                return;
            default:
                ProcessStartTagForInTable(token);
                break;
        }
    }

    private void ProcessEndTagForInTableBody(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "tbody": case "tfoot": case "thead":
                if (!_tree.OpenElements.InTableScope(tagName)) return;
                _tree.OpenElements.PopUntilPopped(tagName);
                ResetInsertionModeAppropriately();
                return;
            case "table":
                _tree.OpenElements.PopUntilPopped("tbody");
                _insertionMode = InsertionMode.InTable;
                ProcessEndTag(token);
                return;
            default:
                ProcessEndTagForInTable(token);
                break;
        }
    }

    private void ProcessStartTagForInRow(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "th": case "td":
                _tree.OpenElements.PopUntilTableRowScopeMarker();
                _tree.InsertHtmlelement(token);
                _insertionMode = InsertionMode.InCell;
                _tree.ActiveFormattingElements.AppendMarker();
                return;
            case "caption": case "col": case "colgroup": case "tbody":
            case "tfoot": case "thead": case "tr":
                _tree.OpenElements.PopUntilPopped("tr");
                _insertionMode = InsertionMode.InTableBody;
                ProcessStartTag(token);
                return;
            default:
                ProcessStartTagForInTable(token);
                break;
        }
    }

    private void ProcessEndTagForInRow(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        if (tagName == "tr")
        {
            if (!_tree.OpenElements.InTableScope("tr")) return;
            _tree.OpenElements.PopUntilPopped("tr");
            ResetInsertionModeAppropriately();
            return;
        }
        if (tagName == "table")
        {
            _tree.OpenElements.PopUntilPopped("tr");
            _insertionMode = InsertionMode.InTableBody;
            ProcessEndTag(token);
            return;
        }
        if (tagName == "tbody" || tagName == "tfoot" || tagName == "thead")
        {
            if (!_tree.OpenElements.InTableScope(tagName)) return;
            ProcessEndTagForInRow(new AtomicHtmlToken(HtmlToken.TokenType.EndTag, "tr"));
            ProcessEndTag(token);
            return;
        }
        ProcessEndTagForInTable(token);
    }

    private void ProcessStartTagForInCell(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "caption": case "col": case "colgroup": case "tbody":
            case "td": case "tfoot": case "th": case "thead": case "tr":
                if (!_tree.OpenElements.InTableScope("td") && !_tree.OpenElements.InTableScope("th"))
                    return;
                CloseTheCell();
                ProcessStartTag(token);
                return;
            default:
                ProcessStartTagForInBody(token);
                break;
        }
    }

    private void ProcessEndTagForInCell(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "td": case "th":
                if (!_tree.OpenElements.InTableScope(tagName)) return;
                _tree.GenerateImpliedEndTags();
                _tree.OpenElements.PopUntilPopped(tagName);
                _tree.ActiveFormattingElements.ClearToLastMarker();
                ResetInsertionModeAppropriately();
                return;
            case "body": case "caption": case "col": case "colgroup":
            case "html":
                return;
            case "table":
                if (!_tree.OpenElements.InTableScope("td") && !_tree.OpenElements.InTableScope("th"))
                    return;
                CloseTheCell();
                ProcessEndTag(token);
                return;
            case "tbody": case "tfoot": case "thead": case "tr":
                if (!_tree.OpenElements.InTableScope(tagName)) return;
                CloseTheCell();
                ProcessEndTag(token);
                return;
            default:
                ProcessEndTagForInBody(token);
                break;
        }
    }

    private void ProcessStartTagForInCaption(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "caption": case "col": case "colgroup": case "tbody":
            case "td": case "tfoot": case "th": case "thead": case "tr":
                if (!_tree.OpenElements.InTableScope("caption")) return;
                _tree.GenerateImpliedEndTags();
                _tree.OpenElements.PopUntilPopped("caption");
                _tree.ActiveFormattingElements.ClearToLastMarker();
                _insertionMode = InsertionMode.InTable;
                ProcessStartTag(token);
                return;
            default:
                ProcessStartTagForInBody(token);
                break;
        }
    }

    private void ProcessEndTagForInCaption(AtomicHtmlToken token)
    {
        if (token.GetName().ToLowerInvariant() == "caption")
        {
            if (!_tree.OpenElements.InTableScope("caption")) return;
            _tree.GenerateImpliedEndTags();
            _tree.OpenElements.PopUntilPopped("caption");
            _tree.ActiveFormattingElements.ClearToLastMarker();
            ResetInsertionModeAppropriately();
            return;
        }
    }

    private void ProcessStartTagForInColumnGroup(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        if (tagName == "html")
        {
            ProcessHtmlStartTagForInBody(token);
            return;
        }
        if (tagName == "col")
        {
            _tree.InsertSelfClosingHtmlElementDestroyingToken(token);
            return;
        }
        if (tagName == "template")
        {
            _tree.InsertHtmlelement(token);
            _templateInsertionModes.Add(InsertionMode.TemplateContents);
            _insertionMode = InsertionMode.TemplateContents;
            return;
        }
        _tree.OpenElements.Pop();
        _insertionMode = InsertionMode.InTable;
        ProcessStartTag(token);
    }

    private void ProcessEndTagForInColumnGroup(AtomicHtmlToken token)
    {
        if (token.GetName().ToLowerInvariant() == "colgroup")
        {
            if (_tree.OpenElements.Top == _tree.OpenElements.RootNode)
                return;
            _tree.OpenElements.Pop();
            _insertionMode = InsertionMode.InTable;
            return;
        }
        if (token.GetName().ToLowerInvariant() == "template")
        {
            _tree.OpenElements.PopUntilPopped("template");
            return;
        }
    }

    private void ProcessStartTagForInSelect(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "html":
                ProcessStartTagForInBody(token);
                break;
            case "option":
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "option")
                    _tree.OpenElements.Pop();
                _tree.InsertHtmlelement(token);
                break;
            case "optgroup":
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "option")
                    _tree.OpenElements.Pop();
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "optgroup")
                    _tree.OpenElements.Pop();
                _tree.InsertHtmlelement(token);
                break;
            case "select":
                // A nested <select> start tag acts as a </select>: it closes the
                // current select. Without this the parser stays in "in select"
                // mode and silently drops everything that follows.
                if (CloseSelect())
                    return;
                break;
            case "input":
            case "keygen":
            case "textarea":
                // These are not allowed in a select; they close it, then are
                // reprocessed in the reset insertion mode.
                if (CloseSelect())
                {
                    ProcessStartTag(token);
                    return;
                }
                break;
            case "script":
            case "template":
                ProcessStartTagForInHead(token);
                break;
            default:
                // Any other start tag is ignored inside a select.
                break;
        }
    }

    private void ProcessEndTagForInSelect(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        switch (tagName)
        {
            case "optgroup":
                // If the current node is an option whose parent is an optgroup,
                // pop the option first.
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "option" &&
                    _tree.CurrentStackItem?.NextItemInStack?.Name.ToLowerInvariant() == "optgroup")
                    _tree.OpenElements.Pop();
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "optgroup")
                    _tree.OpenElements.Pop();
                break;
            case "option":
                if (_tree.CurrentStackItem?.Name.ToLowerInvariant() == "option")
                    _tree.OpenElements.Pop();
                break;
            case "select":
                // Close the select and leave "in select" mode. This is the end of
                // the select's content; subsequent markup resumes normally.
                CloseSelect();
                break;
            case "template":
                _tree.OpenElements.PopUntilPopped("template");
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Pop the open-elements stack up to and including the nearest select, then
    /// reset the insertion mode. Returns false (and does nothing) when there is
    /// no select in scope, matching the spec's parse-error/ignore behaviour.
    /// </summary>
    private bool CloseSelect()
    {
        if (!_tree.OpenElements.InScope("select"))
            return false;
        _tree.OpenElements.PopUntilPopped("select");
        ResetInsertionModeAppropriately();
        return true;
    }

    private void ProcessAnyOtherEndTagForInBody(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        for (HtmlStackItem? item = _tree.OpenElements.TopStackItem; item != null; item = item.NextItemInStack)
        {
            if (string.Equals(item.Name, tagName, StringComparison.OrdinalIgnoreCase) && item.IsHtmlNamespace)
            {
                _tree.GenerateImpliedEndTagsWithExclusion(tagName);
                _tree.OpenElements.PopUntilPopped(tagName);
                return;
            }
        }
    }

    private void ProcessCloseWhenNestedTag(AtomicHtmlToken token, Func<HtmlStackItem, bool> shouldClose)
    {
        _framesetOk = false;
        HtmlStackItem? item = _tree.OpenElements.TopStackItem;
        while (item != null)
        {
            if (shouldClose(item))
            {
                _tree.OpenElements.PopUntilPopped(item.Name);
                break;
            }
            if (item.IsSpecialNode && item.Name.ToLowerInvariant() != "address" &&
                item.Name.ToLowerInvariant() != "div" && item.Name.ToLowerInvariant() != "p")
                break;
            item = item.NextItemInStack;
        }
        ProcessFakePEndTagIfPInButtonScope();
        _tree.InsertHtmlelement(token);
    }

    private void ProcessFakePEndTagIfPInButtonScope()
    {
        if (!_tree.OpenElements.InButtonScope("p")) return;
        ProcessEndTag(new AtomicHtmlToken(HtmlToken.TokenType.EndTag, "p"));
    }

    private void ProcessTemplateStartTag(AtomicHtmlToken token)
    {
        _tree.ActiveFormattingElements.AppendMarker();
        _tree.InsertHtmlTemplateElement(token);
        _framesetOk = false;
        _templateInsertionModes.Add(InsertionMode.TemplateContents);
        _insertionMode = InsertionMode.TemplateContents;
    }

    private bool ProcessTemplateEndTag(AtomicHtmlToken token)
    {
        if (!_tree.OpenElements.HasTemplateInHtmlScope)
            return false;
        _tree.GenerateImpliedEndTags();
        _tree.OpenElements.PopUntilPopped("template");
        _tree.ActiveFormattingElements.ClearToLastMarker();
        if (_templateInsertionModes.Count > 0)
            _templateInsertionModes.RemoveAt(_templateInsertionModes.Count - 1);
        ResetInsertionModeAppropriately();
        return true;
    }

    private void CallTheAdoptionAgency(AtomicHtmlToken token)
    {
        string tagName = token.GetName().ToLowerInvariant();
        var formattingElement = _tree.ActiveFormattingElements.ClosestElementInScopeWithName(tagName);
        if (formattingElement == null)
        {
            ProcessAnyOtherEndTagForInBody(token);
            return;
        }
        if (!_tree.OpenElements.Contains(formattingElement))
        {
            _tree.ActiveFormattingElements.Remove(formattingElement);
            return;
        }
        if (_tree.OpenElements.InScope(formattingElement.LocalName))
        {
            _tree.ActiveFormattingElements.Remove(formattingElement);
            _tree.OpenElements.Remove(formattingElement);
        }
    }

    private void CloseTheCell()
    {
        if (_tree.OpenElements.InTableScope("td"))
        {
            _tree.GenerateImpliedEndTags();
            _tree.OpenElements.PopUntilPopped("td");
            _tree.ActiveFormattingElements.ClearToLastMarker();
            ResetInsertionModeAppropriately();
            return;
        }
        _tree.GenerateImpliedEndTags();
        _tree.OpenElements.PopUntilPopped("th");
        _tree.ActiveFormattingElements.ClearToLastMarker();
        ResetInsertionModeAppropriately();
    }

    private void ResetInsertionModeAppropriately()
    {
        for (HtmlStackItem? item = _tree.OpenElements.TopStackItem; item != null; item = item.NextItemInStack)
        {
            if (item.IsDocumentFragmentNode) { _insertionMode = InsertionMode.InBody; return; }
            string tag = item.Name.ToLowerInvariant();
            switch (tag)
            {
                case "select":
                    _insertionMode = InsertionMode.InSelect;
                    return;
                case "td": case "th":
                    _insertionMode = InsertionMode.InCell;
                    return;
                case "tr":
                    _insertionMode = InsertionMode.InRow;
                    return;
                case "tbody": case "thead": case "tfoot":
                    _insertionMode = InsertionMode.InTableBody;
                    return;
                case "caption":
                    _insertionMode = InsertionMode.InCaption;
                    return;
                case "colgroup":
                    _insertionMode = InsertionMode.InColumnGroup;
                    return;
                case "table":
                    _insertionMode = InsertionMode.InTable;
                    return;
                case "template":
                    _insertionMode = _templateInsertionModes.Count > 0
                        ? _templateInsertionModes[_templateInsertionModes.Count - 1]
                        : InsertionMode.TemplateContents;
                    return;
                case "head":
                    if (tag == "head" && item == _tree.HeadStackItem) { _insertionMode = InsertionMode.InBody; return; }
                    _insertionMode = InsertionMode.InBody;
                    return;
                case "body":
                    _insertionMode = InsertionMode.InBody;
                    return;
                case "frameset":
                    _insertionMode = InsertionMode.InFrameset;
                    return;
                case "html":
                    _insertionMode = InsertionMode.BeforeHead;
                    return;
                default:
                    _insertionMode = InsertionMode.InBody;
                    return;
            }
        }
        _insertionMode = InsertionMode.InBody;
    }

    private void DefaultForInitial()
    {
        _tree.InsertDoctype(new AtomicHtmlToken(HtmlToken.TokenType.Doctype, "html"));
        _insertionMode = InsertionMode.BeforeHtml;
    }

    private void DefaultForBeforeHtml()
    {
        _tree.InsertHtmlHtmlStartTagBeforeHTML(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "html"));
        _insertionMode = InsertionMode.BeforeHead;
    }

    private void DefaultForBeforeHead()
    {
        _tree.InsertHtmlHeadElement(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "head"));
        _insertionMode = InsertionMode.InHead;
    }

    private void DefaultForInHead()
    {
        _tree.OpenElements.Pop();
        _insertionMode = InsertionMode.AfterHead;
    }

    private void DefaultForAfterHead()
    {
        _tree.InsertHtmlBodyElement(new AtomicHtmlToken(HtmlToken.TokenType.StartTag, "body"));
        _insertionMode = InsertionMode.InBody;
    }

    private void DefaultForInTableText()
    {
        _insertionMode = _originalInsertionMode;
    }

    private static bool IsHtmlSpace(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
    }
}