namespace Acrux.Core.Dom.Html;

public class ElementInternals
{
    private readonly HtmlElement _element;
    private readonly Dictionary<string, string> _formValues = new();
    private readonly List<string> _states = new();
    private string? _validationMessage;

    public ElementInternals(HtmlElement element)
    {
        _element = element;
    }

    public ShadowRoot? ShadowRoot => _element.ShadowRoot;
    public HTMLFormElement? Form { get; internal set; }
    public NodeList Labels { get; internal set; } = new();
    public ValidityState Validity { get; } = new();
    public string ValidationMessage => _validationMessage ?? "";
    public bool WillValidate { get; set; } = true;

    public void SetFormValue(FormData? value, FormData? state = null)
    {
        _formValues.Clear();
        if (value != null)
        {
            foreach (var entry in value)
                _formValues[entry.Key] = entry.Value;
        }
    }

    public string States => string.Join(" ", _states);

    public void SetValidity(ValidityState? flags, string? message = null, Element? anchor = null)
    {
        if (flags != null)
        {
            Validity.ValueMissing = flags.ValueMissing;
            Validity.TypeMismatch = flags.TypeMismatch;
            Validity.PatternMismatch = flags.PatternMismatch;
            Validity.TooLong = flags.TooLong;
            Validity.TooShort = flags.TooShort;
            Validity.RangeUnderflow = flags.RangeUnderflow;
            Validity.RangeOverflow = flags.RangeOverflow;
            Validity.StepMismatch = flags.StepMismatch;
            Validity.BadInput = flags.BadInput;
            Validity.CustomError = flags.CustomError;
            _validationMessage = message;
        }
        else
        {
            Validity.ValueMissing = false;
            Validity.TypeMismatch = false;
            Validity.PatternMismatch = false;
            Validity.TooLong = false;
            Validity.TooShort = false;
            Validity.RangeUnderflow = false;
            Validity.RangeOverflow = false;
            Validity.StepMismatch = false;
            Validity.BadInput = false;
            Validity.CustomError = false;
            _validationMessage = null;
        }
    }

    public bool CheckValidity() => Validity.Valid;
    public bool ReportValidity() => Validity.Valid;
}

public class CustomStateSet
{
    private readonly HashSet<string> _states = new();

    public int Size => _states.Count;

    public void Add(string state) => _states.Add(state);
    public void Delete(string state) => _states.Remove(state);
    public bool Has(string state) => _states.Contains(state);
    public void Clear() => _states.Clear();
    public IEnumerator<string> GetEnumerator() => _states.GetEnumerator();
}


