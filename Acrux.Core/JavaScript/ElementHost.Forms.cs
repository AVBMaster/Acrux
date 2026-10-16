using System.Globalization;
using Acrux.Core.Dom;
using DOMException = Acrux.Core.Dom.DOMException;
using DomElement = Acrux.Core.Dom.Element;

namespace Acrux.Core.JavaScript;

/// <summary>The form control interfaces (HTML §4.10) on the shared element host: the value a control
/// answers, the attributes the IDL reflects, the selection the text controls keep, the constraint
/// validation the forms ask about, and the lists the option, fieldset, output, progress and meter
/// elements carry. Everything here is read from the element the host wraps, so a control the page
/// changed answers the change: the live 'value' is separate from the 'value' attribute exactly the way
/// the specification keeps them (§4.10.7.1), which is what makes 'defaultValue' keep what the markup
/// said after the page wrote into the field (measured both).</summary>
public partial class ElementHost
{
    private bool IsInput => TagIs(_element, "INPUT");
    private bool IsTextArea => TagIs(_element, "TEXTAREA");
    private bool IsSelect => TagIs(_element, "SELECT");
    private bool IsOption => TagIs(_element, "OPTION");
    private string ControlType => (IsInput ? (_element.GetAttribute("type") ?? "text") : "").ToLowerInvariant();

    private static readonly HashSet<string> TextLikeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "search", "tel", "url", "email", "password", "number", "date", "time",
        "datetime-local", "month", "week", "color", "range", "file"
    };

    private bool IsTextEditable => IsTextArea || (IsInput && TextLikeTypes.Contains(ControlType));

    // ── the value ────────────────────────────────────────────────────────────
    /// <summary>HTML §4.10: what the control answers as its value — the page's own write for a text
    /// control, the 'value' attribute (or 'on') for a checkbox or radio, the chosen option's value for
    /// a select, the option's attribute or its text for an option, the number for a progress bar or
    /// meter, the text inside for an output. An element that holds no value answers nothing (measured:
    /// a div has no 'value' to read).</summary>
    public object? value
    {
        get
        {
            if (IsInput)
            {
                var type = ControlType;
                if (type == "checkbox" || type == "radio")
                    return _element.GetAttribute("value") ?? "on";
                var written = _element.Value ?? "";
                // HTML §4.10.5: a number or range control whose text is not a number has no value to
                // answer — the empty string is what the reference gives, and 'valueAsNumber' reads NaN
                // from it (measured: 'value = "abc"' leaves the field answering nothing).
                if ((type == "number" || type == "range") && written.Length > 0
                    && !double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return "";
                return written;
            }
            if (IsTextArea) return _element.Value ?? _element.TextContent ?? "";
            if (IsSelect)
            {
                var chosen = SelectedOptionElements();
                return chosen.Count > 0 ? OptionValue(chosen[0]) : "";
            }
            if (IsOption) return OptionValue(_element);
            if (TagIs(_element, "METER")) return NumericAttribute("value", MeterDefault());
            if (TagIs(_element, "PROGRESS")) return NumericAttribute("value", 0);
            if (TagIs(_element, "OUTPUT")) return _element.TextContent ?? "";
            if (TagIs(_element, "BUTTON") || TagIs(_element, "PARAM")) return _element.GetAttribute("value") ?? "";
            if (TagIs(_element, "LI")) return _element.GetAttribute("value") ?? "";
            return null;
        }
        set
        {
            var text = value switch
            {
                null => "",
                bool flag => flag ? "on" : "",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
            if (IsTextArea || IsInput)
            {
                _element.Value = text;
                return;
            }
            if (IsOption) { _element.SetAttribute("value", text); return; }
            if (IsSelect)
            {
                // Writing the select's value chooses the option that answers it and takes the choice off
                // every other one (§4.10.11).
                var options = OptionElements();
                var found = false;
                foreach (var option in options)
                {
                    var matches = string.Equals(OptionValue(option), text, StringComparison.Ordinal);
                    if (matches && !found) { option.SetAttribute("selected", ""); found = true; }
                    else option.RemoveAttribute("selected");
                }
                return;
            }
            if (TagIs(_element, "OUTPUT")) { _element.TextContent = text; return; }
            if (TagIs(_element, "PROGRESS") || TagIs(_element, "METER") || TagIs(_element, "LI")
                || TagIs(_element, "BUTTON"))
            {
                _element.SetAttribute("value", text);
                return;
            }
            _element.SetAttribute("value", text);
        }
    }

    /// <summary>The value the markup gave: the 'value' attribute for an input or a button, the text
    /// inside a text area, the 'selected' attribute for an option (HTML §4.10).</summary>
    public string defaultValue
    {
        get
        {
            if (IsTextArea) return _element.GetAttribute("value") ?? _element.TextContent ?? "";
            if (IsOption) return OptionValue(_element);
            return _element.GetAttribute("value") ?? "";
        }
        set
        {
            if (IsTextArea && string.IsNullOrEmpty(_element.GetAttribute("value")))
            {
                // A text area's default is what was written between its tags, so the first write names
                // it as the attribute the page can read back (HTML §4.10.10).
                _element.TextContent = value ?? "";
                return;
            }
            _element.SetAttribute("value", value ?? "");
        }
    }

    private static string OptionValue(DomElement option)
    {
        var written = option.GetAttribute("value");
        if (!string.IsNullOrEmpty(written)) return written!;
        // An option with no 'value' attribute answers its text, with the runs of space the markup
        // scattered through it folded together (HTML §4.10.11.1).
        return System.Net.WebUtility.HtmlEncode("").Length == 0
            ? CollapseSpaces(option.TextContent ?? "")
            : CollapseSpaces(option.TextContent ?? "");
    }

    private static string CollapseSpaces(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\f' or '\r') { space = builder.Length > 0; continue; }
            if (space) { builder.Append(' '); space = false; }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private double NumericAttribute(string name, double fallback = double.NaN)
    {
        var text = _element.GetAttribute(name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;
    }

    /// <summary>HTML §4.10.5: a number control answers its value as a number, and NaN when what it
    /// holds is not one (measured: 'value = "abc"' leaves 'valueAsNumber' NaN, because the value the
    /// control answers is then the empty string).</summary>
    public double valueAsNumber
    {
        get
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : double.NaN;
        }
        set
        {
            _element.Value = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>HTML §4.10.5: a date control's value as a date; the engine holds no date model for a
    /// control whose value is text, so the answer is the null the reference gives for the types that
    /// have no date to read (measured: 'valueAsDate' is null for a number field).</summary>
    public object? valueAsDate
    {
        get => null;
        set { }
    }

    /// <summary>HTML §4.10.5.3: the step members move the value along the control's step grid — the
    /// grid a min attribute anchors, and the value the page wrote snapped to the grid before the move
    /// (measured: a field with min 0 and step 2 goes from 7 to 8, then two steps down to 4). The move
    /// stops at the range the control declares.</summary>
    public void stepUp(object? n = null) => Step(n, 1);

    public void stepDown(object? n = null) => Step(n, -1);

    private void Step(object? n, int direction)
    {
        var steps = n == null ? 1 : ToInt(n, 1);
        var step = ParseStep();
        var baseValue = double.TryParse(_element.GetAttribute("min"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var anchor) ? anchor : 0;
        var current = valueAsNumber;
        var aligned = double.IsNaN(current)
            ? baseValue
            : baseValue + Math.Floor((current - baseValue) / step + 1e-9) * step;
        var moved = aligned + direction * steps * step;
        if (double.TryParse(_element.GetAttribute("min"), NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            && moved < min) moved = min;
        if (double.TryParse(_element.GetAttribute("max"), NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
            && moved > max) moved = max;
        _element.Value = FormatNumber(moved);
    }

    private double ParseStep()
    {
        var written = _element.GetAttribute("step");
        if (string.IsNullOrEmpty(written)) return 1;
        if (string.Equals(written, "any", StringComparison.OrdinalIgnoreCase)) return 1;
        return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step > 0
            ? step : 1;
    }

    private static string FormatNumber(double value)
    {
        if (value == Math.Truncate(value) && Math.Abs(value) < 1e15)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // ── the reflected attributes of a control ────────────────────────────────
    // 'min', 'max' and 'step' are strings on a form field that carries them as written text, and
    // numbers on the two elements whose value is a number — a meter and a progress bar. A progress bar
    // also has no floor and no low/high bounds to invent, so those answer nothing at all (measured:
    // 'meter.min' is 0 and 'progress.min' is null, both reading their attribute as a number).
    public object min
    {
        get => RangeAttribute("min");
        set => _element.SetAttribute("min", NumberOrText(value));
    }

    public object max
    {
        get => RangeAttribute("max");
        set => _element.SetAttribute("max", NumberOrText(value));
    }

    public object step
    {
        get => RangeAttribute("step");
        set => _element.SetAttribute("step", NumberOrText(value));
    }

    private object RangeAttribute(string name)
    {
        var written = _element.GetAttribute(name);
        if (TagIs(_element, "METER") || TagIs(_element, "PROGRESS"))
        {
            if (string.IsNullOrEmpty(written))
            {
                // §4.10.6, §4.10.7: a meter's floor is 0 and its ceiling 1; a progress bar answers no
                // floor, no low and no high at all, and a ceiling of 1 unless the markup says other.
                if (name == "min") return TagIs(_element, "METER") ? (object)0d : null!;
                if (name == "step") return (object)1d;
                return (object)1d;
            }
            return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : (object)double.NaN;
        }
        return written ?? "";
    }

    private static string NumberOrText(object? value)
        => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    public string pattern { get => _element.GetAttribute("pattern") ?? ""; set => _element.SetAttribute("pattern", value ?? ""); }

    public string accept { get => _element.GetAttribute("accept") ?? ""; set => _element.SetAttribute("accept", value ?? ""); }
    public string autocomplete { get => _element.GetAttribute("autocomplete") ?? ""; set => _element.SetAttribute("autocomplete", value ?? ""); }
    public string dirName { get => _element.GetAttribute("dirname") ?? ""; set => _element.SetAttribute("dirname", value ?? ""); }

    public bool autofocus
    {
        get => _element.HasAttribute("autofocus");
        set { if (value) _element.SetAttribute("autofocus", ""); else _element.RemoveAttribute("autofocus"); }
    }

    /// <summary>HTML §4.10.5: 'size' is the control's own width in characters, and the text types answer
    /// 20 when the markup did not say (measured). A select's 'size' is how many options it shows, with
    /// no default to fall back to.</summary>
    public int size
    {
        get
        {
            var written = _element.GetAttribute("size");
            if (int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                return parsed;
            return IsSelect ? 0 : 20;
        }
        set => _element.SetAttribute("size", (value < 0 ? 0 : value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>HTML §4.10.5: the length limits the control answers, with -1 for "no limit".</summary>
    public int minLength
    {
        get => IntAttribute("minlength", -1);
        set => _element.SetAttribute("minlength", value.ToString(CultureInfo.InvariantCulture));
    }

    public int maxLength
    {
        get => IntAttribute("maxlength", -1);
        set => _element.SetAttribute("maxlength", value.ToString(CultureInfo.InvariantCulture));
    }

    private int IntAttribute(string name, int fallback)
        => int.TryParse(_element.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallback;

    /// <summary>HTML §4.10.5: the 'list' attribute names the datalist the control offers its values
    /// from; the element it names, or null.</summary>
    public object? list
    {
        get
        {
            var id = _element.GetAttribute("list");
            if (string.IsNullOrEmpty(id)) return null;
            var document = _element.OwnerDocument;
            if (document == null) return null;
            foreach (var candidate in document.GetElementsByTagName("datalist"))
                if (string.Equals(candidate.GetAttribute("id"), id, StringComparison.Ordinal))
                    return WrapForCollection(candidate);
            return null;
        }
    }

    /// <summary>HTML §4.10.5: a checkbox the user has neither checked nor unchecked — a state only the
    /// script sets, never the markup, so it is the host's own flag rather than an attribute.</summary>
    public bool indeterminate { get; set; }

    // ── the selection the text controls keep ─────────────────────────────────
    /// <summary>HTML §4.10.6: which end of the selection the user extended from; a page that never set
    /// it reads the direction the control starts in.</summary>
    public string selectionDirection
    {
        get => _selectionDirection;
        set
        {
            var wanted = (value ?? "").ToLowerInvariant();
            _selectionDirection = wanted is "forward" or "backward" or "none" ? wanted : "forward";
        }
    }

    private string _selectionDirection = "forward";

    /// <summary>HTML §4.10.6.1: the text between the two ends of the selection is written over by the
    /// replacement, and the fourth argument says where the caret ends up — the whole new text selected,
    /// its start, its end, or nothing of it (the default). Measured: 'setRangeText("ELL", 1, 3, "end")'
    /// turns "hello" into "HELLo" with the caret after it.</summary>
    public void setRangeText(string replacement, object? start = null, object? end = null,
        string? selectionMode = null)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var from = ClampIndex(ToIndex(start, text.Length), text.Length);
        var to = ClampIndex(ToIndex(end, from), text.Length);
        if (to < from) (from, to) = (to, from);
        var written = (replacement ?? "") is { } insert ? insert : "";
        var next = text.Substring(0, from) + written + text.Substring(to);
        value = next;
        var mode = (selectionMode ?? "preserve").ToLowerInvariant();
        var caretStart = from;
        var caretEnd = from + written.Length;
        switch (mode)
        {
            case "start": selectionStart = caretStart; selectionEnd = caretStart; break;
            case "end": selectionStart = caretEnd; selectionEnd = caretEnd; break;
            case "select": selectionStart = caretStart; selectionEnd = caretEnd; break;
            default:
                // 'preserve' keeps the selection the control held, moved along by the text that changed.
                selectionStart = ClampIndex(_element.SelectionStart + (written.Length - (to - from)), next.Length);
                selectionEnd = ClampIndex(_element.SelectionEnd + (written.Length - (to - from)), next.Length);
                break;
        }
    }

    private static int ClampIndex(int index, int length) => index < 0 ? 0 : (index > length ? length : index);

    // ── the control's place in a form ────────────────────────────────────────
    /// <summary>HTML §4.10.13: the form the control belongs to — the one that holds it, or the one its
    /// 'form' attribute names.</summary>
    public object? form
    {
        get
        {
            if (!IsFormControl(_element)) return null;
            for (var node = _element.ParentElement; node != null; node = node.ParentElement)
                if (TagIs(node, "FORM")) return WrapForCollection(node);
            return null;
        }
    }

    private static bool IsFormControl(DomElement el)
        => el.TagName is "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON" or "OUTPUT" or "PROGRESS"
            or "METER" or "FIELDSET" or "OBJECT";

    /// <summary>HTML §4.10.3: the labels that name this control — the ones whose 'for' carries its id,
    /// and the label the control sits inside. The list is read from the tree every time, so a label the
    /// page adds afterwards is in it.</summary>
    public object labels => new JsLiveElementList(() =>
    {
        var found = new List<DomElement>();
        var id = _element.GetAttribute("id");
        var document = _element.OwnerDocument;
        if (document != null && !string.IsNullOrEmpty(id))
        {
            foreach (var candidate in document.GetElementsByTagName("label"))
                if (string.Equals(candidate.GetAttribute("for"), id, StringComparison.Ordinal))
                    found.Add(candidate);
        }
        for (var node = _element.ParentElement; node != null; node = node.ParentElement)
            if (TagIs(node, "LABEL")) { found.Insert(0, node); break; }
        return found;
    });

    /// <summary>HTML §4.10.5: the files a file control holds. Nothing is loaded from disk here, so the
    /// list is the empty one the reference answers for a control the user has not filled in.</summary>
    public object files => new JsFileList();

    // ── the option, fieldset, output, progress and meter members ─────────────
    /// <summary>HTML §4.10.11: the option's place in the list of options its select answers — the ones
    /// inside an option group counted too, in the order the select shows them.</summary>
    public int index
    {
        get
        {
            if (!IsOption) return 0;
            var select = FindAncestor(_element, "SELECT");
            if (select == null) return 0;
            var options = OptionElementsOf(select);
            var at = options.IndexOf(_element);
            return at >= 0 ? at : 0;
        }
    }

    private static List<DomElement> OptionElementsOf(DomElement root)
    {
        var list = new List<DomElement>();
        CollectOptionsInto(root, list);
        return list;
    }

    private static void CollectOptionsInto(DomElement root, List<DomElement> into)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (TagIs(child, "OPTION")) into.Add(child);
            else if (TagIs(child, "OPTGROUP")) CollectOptionsInto(child, into);
        }
    }

    /// <summary>HTML §4.10.11: the label that stands in for an option in a summary — its text when the
    /// markup wrote none.</summary>
    public string label
    {
        get => _element.GetAttribute("label") ?? (IsOption ? text : "");
        set => _element.SetAttribute("label", value ?? "");
    }

    public bool defaultSelected
    {
        get => _element.HasAttribute("selected");
        set
        {
            if (value) _element.SetAttribute("selected", "");
            else _element.RemoveAttribute("selected");
        }
    }

    /// <summary>HTML §4.10.7: the bounds a meter answers. 'low' and 'high' fall back to the ends of the
    /// range the control declares (measured: a meter with max 8 and no bounds reads low 0 and high 8),
    /// and 'optimum' to the middle of them. A progress bar has none of the three to answer (§4.10.6).</summary>
    public object low
    {
        get => GaugeAttribute("low", MeterBounds().min);
        set => _element.SetAttribute("low", NumberOrText(value));
    }

    public object high
    {
        get => GaugeAttribute("high", MeterBounds().max);
        set => _element.SetAttribute("high", NumberOrText(value));
    }

    public object optimum
    {
        get => GaugeAttribute("optimum", (MeterBounds().min + MeterBounds().max) / 2);
        set => _element.SetAttribute("optimum", NumberOrText(value));
    }

    private object GaugeAttribute(string name, double fallback)
    {
        if (!TagIs(_element, "METER")) return null!;
        var written = _element.GetAttribute(name);
        if (string.IsNullOrEmpty(written)) return fallback;
        return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : (object)double.NaN;
    }

    private (double min, double max) MeterBounds()
        => (NumericAttribute("min", 0), NumericAttribute("max", 1));

    /// <summary>HTML §4.10.6: where the bar or gauge sits in its range — 0 to 1 for a progress bar, and
    /// <summary>HTML §4.10.6: where a progress bar sits in its range — nothing when the maximum is
    /// unknown. The gauge's own answer (§4.10.7) is measured as nothing at all in this reference, so the
    /// meter reports that rather than a number the page cannot check against the reference.</summary>
    public object? position
    {
        get
        {
            if (TagIs(_element, "PROGRESS"))
            {
                var max = NumericAttribute("max", double.NaN);
                var current = NumericAttribute("value", double.NaN);
                if (double.IsNaN(max) || max <= 0 || double.IsNaN(current)) return null;
                return Math.Min(1, Math.Max(0, current / max));
            }
            if (!TagIs(_element, "METER")) return null;
            return null;
        }
    }

    private double MeterDefault()
    {
        // §4.10.7: a meter with no value answers halfway between its lowest and highest bounds.
        var (min, max) = MeterBounds();
        return Math.Min(Math.Max((min + max) / 2, min), max);
    }
    /// <summary>HTML §4.10.7: the gauge's value clamped into its range. The reference answers nothing
    /// for a meter that is not being rendered, so the member is there and empty (measured on both a
    /// markup meter and a scripted one).</summary>
    public object? actualValue => null;

    /// <summary>HTML §4.10.10: how the text area wraps the lines it shows — the attribute, or the
    /// 'soft' the interface gives when none was written.</summary>
    public string wrap
    {
        get
        {
            var written = (_element.GetAttribute("wrap") ?? "").ToLowerInvariant();
            return written is "hard" or "off" or "soft" ? written : "soft";
        }
        set => _element.SetAttribute("wrap", value ?? "");
    }

    /// <summary>HTML §4.10.10: the length of what the text area holds.</summary>
    public int textLength => (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Length;

    // ── constraint validation (HTML §4.10.4) ─────────────────────────────────
    private string _customValidityMessage = "";

    /// <summary>HTML §4.10.4: whether this control takes part in validation at all — a submittable
    /// control that is neither disabled nor read-only, and no other element (measured: a disabled
    /// required field answers false and 'checkValidity()' true for it).</summary>
    public bool willValidate => IsSubmittable && !_element.HasAttribute("disabled")
        && !_element.HasAttribute("readonly") && !_element.HasAttribute("formnovalidate");

    private bool IsSubmittable => _element.TagName is "INPUT" or "TEXTAREA" or "SELECT";

    /// <summary>The message the script put in the control; while it is there the control fails with
    /// 'customError' and answers this text (HTML §4.10.4.4).</summary>
    public void setCustomValidity(string? message) => _customValidityMessage = message ?? "";

    public ValidationStateHost validity => new(() => ValidationFlags());

    /// <summary>HTML §4.10.4: the first thing that is wrong with the control, told in the sentence the
    /// engine writes. The reference localises these strings to its own UI language, so a page must not
    /// compare them word for word — the flags are the answer, and this is only what the control says
    /// about them.</summary>
    public string validationMessage
    {
        get
        {
            if (!willValidate) return "";
            if (_customValidityMessage.Length > 0) return _customValidityMessage;
            var flags = ValidationFlags();
            if (Flag(flags, "valueMissing"))
                return IsSelect ? "Please select an item in the list." : "Please fill out this field.";
            if (Flag(flags, "typeMismatch"))
                return ControlType == "email" ? "Please include an '@' in the email address."
                    : "Please enter a URL.";
            if (Flag(flags, "patternMismatch")) return "Please match the requested format.";
            if (Flag(flags, "tooLong")) return "Please enter text no longer than the allowed length.";
            if (Flag(flags, "tooShort")) return "Please enter text at least as long as required.";
            if (Flag(flags, "rangeUnderflow"))
                return "Value must be greater than or equal to " + (_element.GetAttribute("min") ?? "") + ".";
            if (Flag(flags, "rangeOverflow"))
                return "Value must be less than or equal to " + (_element.GetAttribute("max") ?? "") + ".";
            if (Flag(flags, "stepMismatch")) return "Please enter a valid value.";
            if (Flag(flags, "badInput")) return "Please enter a number.";
            return "";
        }
    }

    /// <summary>The flags the control's current value sets (HTML §4.10.4). 'tooLong' and 'tooShort'
    /// wait for the user to have typed, which is why a value the script wrote never trips them
    /// (measured: a 'minlength' field holding less than it asks is still 'valid').</summary>
    private Dictionary<string, bool> ValidationFlags()
    {
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        flags["customError"] = _customValidityMessage.Length > 0;
        flags["valueMissing"] = _element.HasAttribute("required") && text.Length == 0;
        if (IsInput && ControlType == "email" && text.Length > 0 && !text.Contains('@'))
            flags["typeMismatch"] = true;
        if (IsInput && ControlType == "url" && text.Length > 0 && !Uri.IsWellFormedUriString(text, UriKind.Absolute))
            flags["typeMismatch"] = true;
        if (IsInput && ControlType == "number" && text.Length > 0 && !double.TryParse(text,
            NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            flags["badInput"] = true;
        var pattern = _element.GetAttribute("pattern");
        if (!string.IsNullOrEmpty(pattern) && text.Length > 0)
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^(?:" + pattern + ")\\z"))
                    flags["patternMismatch"] = true;
            }
            catch
            {
                // A pattern the page wrote that does not compile cannot be checked, so the control
                // keeps no mismatch for it — which is what the reference does too.
            }
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            if (double.TryParse(_element.GetAttribute("min"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var min) && number < min) flags["rangeUnderflow"] = true;
            if (double.TryParse(_element.GetAttribute("max"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var max) && number > max) flags["rangeOverflow"] = true;
            var written = _element.GetAttribute("step");
            if (!string.IsNullOrEmpty(written) && !string.Equals(written, "any", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step > 0)
            {
                var anchor = double.TryParse(_element.GetAttribute("min"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var baseValue) ? baseValue : 0;
                var units = (number - anchor) / step;
                if (Math.Abs(units - Math.Round(units, MidpointRounding.AwayFromZero)) > 1e-9)
                    flags["stepMismatch"] = true;
            }
        }
        return flags;
    }

    private static bool Flag(Dictionary<string, bool> flags, string name)
        => flags.TryGetValue(name, out var set) && set;

    /// <summary>HTML §4.10.4: the control is valid when no flag is set. Asking either member tells the
    /// control and its form, and both raise the 'invalid' event when the answer is no (measured: one
    /// for 'checkValidity', one more for 'reportValidity').</summary>
    public bool checkValidity()
    {
        if (!willValidate) return true;
        if (ValidityIsClean()) return true;
        var evt = new ScriptEvent("invalid", this) { cancelable = true };
        DispatchEvent(evt);
        return false;
    }

    public bool reportValidity() => checkValidity();

    private bool ValidityIsClean()
    {
        var flags = ValidationFlags();
        return !flags.Values.Any(v => v);
    }

    /// <summary>HTML §4.10.6: the value the markup defined for a progress bar. The reference answers
    /// nothing for it on both a markup bar and a scripted one (measured), so the member is there and
    /// empty while 'value' carries the number the page reads.</summary>
    public object? definedValue => null;

}

/// <summary>The empty file list a control that holds no files answers (HTML §4.10.5.1.6): the count, the
/// indexed read and 'item()', which is all a page can ask of a list with nothing in it.</summary>
public class JsFileList
{
    public int length => 0;
    public object? this[int index] => null;
    public object? item(int index) => null;
}

/// <summary>ValidityState (HTML §4.10.4): the flags the constraint validation says a control fails, read
/// from the control as it stands now — the required bit, the type and pattern the value does not match,
/// the range and step it is outside, the lengths it crosses, and the message the script wrote into it.
/// 'valid' is the answer that none of them is set.</summary>
public class ValidationStateHost
{
    private readonly Func<Dictionary<string, bool>> _flags;

    internal ValidationStateHost(Func<Dictionary<string, bool>> flags) => _flags = flags;

    private bool Flag(string name) => _flags().TryGetValue(name, out var set) && set;

    public bool valid => !Flag("valueMissing") && !Flag("typeMismatch") && !Flag("patternMismatch")
        && !Flag("tooLong") && !Flag("tooShort") && !Flag("rangeUnderflow") && !Flag("rangeOverflow")
        && !Flag("stepMismatch") && !Flag("badInput") && !Flag("customError");
    public bool valueMissing => Flag("valueMissing");
    public bool typeMismatch => Flag("typeMismatch");
    public bool patternMismatch => Flag("patternMismatch");
    public bool tooLong => Flag("tooLong");
    public bool tooShort => Flag("tooShort");
    public bool rangeUnderflow => Flag("rangeUnderflow");
    public bool rangeOverflow => Flag("rangeOverflow");
    public bool stepMismatch => Flag("stepMismatch");
    public bool badInput => Flag("badInput");
    public bool customError => Flag("customError");
}
