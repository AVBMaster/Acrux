namespace Acrux.Core.JavaScript;

public class JsDomConstructorInfo
{
    public string ConstructorName { get; set; } = "";
    public Func<object?[], object?> Factory { get; set; } = _ => null;
    public string[] PrototypeChain { get; set; } = Array.Empty<string>();
}

public class JsDomPrototypeSystem
{
    private readonly BrowserJsEngineFacade _facade;
    private readonly Dictionary<string, JsDomConstructorInfo> _constructors = new();
    private bool _prototypesInitialized;

    private static readonly string PrototypeSetupScript = @"
(function() {
    if (window.__domPrototypes) return;
    var proto = {};
    var protoChains = {};

    // Helper: convert CLR string[] to JS array (CLR arrays have numeric indexer but no .length)
    function toJsArray(clrArr) {
        var result = [];
        if (!clrArr) return result;
        try {
            for (var i = 0; ; i++) {
                var item = clrArr[i];
                if (item === undefined) break;
                result.push(item);
            }
        } catch(e) {}
        return result;
    }

    function createPrototypeChain(names) {
        var chain = [];
        for (var i = 0; i < names.length; i++) {
            var name = names[i];
            if (!proto[name]) {
                proto[name] = {};
                if (i > 0) {
                    proto[name].__proto__ = proto[names[i-1]];
                }
            }
            chain.push(proto[name]);
        }
        return chain;
    }

    function defineConstructor(name, baseNames, factory) {
        var chain = createPrototypeChain(baseNames);
        var ctor = function() {
            return factory ? factory.apply(this, arguments) : null;
        };
        ctor.prototype = chain[chain.length - 1] || {};
        ctor.prototype.constructor = ctor;

        // Store prototype chain for instanceof support
        var allNames = baseNames.concat([name]);
        protoChains[name] = allNames;

        // Symbol.hasInstance: check if instance has __domTypeChain containing this name
        // Note: Jint bypasses Symbol.hasInstance for CLR host objects (uses OrdinaryHasInstance),
        // so this is a fallback for non-host-object instances.
        if (typeof Symbol !== 'undefined' && Symbol.hasInstance) {
            ctor[Symbol.hasInstance] = function(instance) {
                if (instance == null) return false;
                try {
                    var chain = instance.__domTypeChain;
                    if (chain) {
                        var names = toJsArray(chain);
                        return names.indexOf(name) !== -1;
                    }
                } catch(e) {}
                return false;
            };
        }

        window[name] = ctor;
        window.__domPrototypes = proto;
        window.__domProtoChains = protoChains;
    }

    window.__defineDomCtor = defineConstructor;
    window.__domProto = proto;

    // Track fixed prototypes to avoid redundant setPrototypeOf calls
    if (typeof WeakSet !== 'undefined') {
        window.__fixedProtoSet = new WeakSet();
    }

    // Helper to fix prototype on any DOM host object
    window.__fixProto = function(obj) {
        if (!obj) return;
        try {
            // Skip if already fixed
            if (window.__fixedProtoSet && window.__fixedProtoSet.has(obj)) return;

            var rawChain = obj.__domTypeChain;
            if (!rawChain) return;
            var names = toJsArray(rawChain);
            if (names.length === 0) return;
            var typeName = names[names.length - 1];
            var ctor = window[typeName];
            if (ctor && ctor.prototype) {
                Object.setPrototypeOf(obj, ctor.prototype);
                if (window.__fixedProtoSet) {
                    window.__fixedProtoSet.add(obj);
                }
            }
        } catch(e) {}
    };

    // Also fix document right away
    if (typeof document !== 'undefined') {
        window.__fixProto(document);
    }
})();
";

    public JsDomPrototypeSystem(BrowserJsEngineFacade facade)
    {
        _facade = facade;
    }

    public void InitializePrototypes(bool force = false)
    {
        if (_prototypesInitialized && !force) return;

        try
        {
            _facade.Execute(PrototypeSetupScript);
            _prototypesInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DOM Prototype] Init error: {ex.Message}");
        }
    }

    public void RegisterConstructor(string name, string[] prototypeChain, Func<object?[], object?> factory)
    {
        _constructors[name] = new JsDomConstructorInfo
        {
            ConstructorName = name,
            Factory = factory,
            PrototypeChain = prototypeChain
        };

        if (!_prototypesInitialized) return;

        var chainArr = new System.Text.Json.Nodes.JsonArray();
        foreach (var pn in prototypeChain)
            chainArr.Add((System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(pn)!);
        var chainJson = chainArr.ToJsonString();
        var safeName = System.Web.HttpUtility.JavaScriptStringEncode(name);

        try
        {
            _facade.Execute($@"
                window.__defineDomCtor('{safeName}', {chainJson}, function() {{
                    return null;
                }});
            ");
        }
        catch { }
    }

    public void Reset()
    {
        _prototypesInitialized = false;
    }

    public void RegisterBuiltinConstructors()
    {
        InitializePrototypes();

        RegisterConstructor("EventTarget", new[] { "Object" }, _ => null);
        RegisterConstructor("Node", new[] { "Object", "EventTarget" }, _ => null);
        RegisterConstructor("Element", new[] { "Object", "EventTarget", "Node" }, _ => null);
        RegisterConstructor("HTMLElement", new[] { "Object", "EventTarget", "Node", "Element" }, _ => null);
        RegisterConstructor("HTMLDocument", new[] { "Object", "EventTarget", "Node" }, _ => null);

        RegisterConstructor("Text", new[] { "Object", "EventTarget", "Node" }, _ => null);
        RegisterConstructor("Comment", new[] { "Object", "EventTarget", "Node" }, _ => null);
        RegisterConstructor("DocumentFragment", new[] { "Object", "EventTarget", "Node" }, _ => null);

        RegisterConstructor("MouseEvent", new[] { "Object" }, _ => null);
        RegisterConstructor("KeyboardEvent", new[] { "Object" }, _ => null);
        RegisterConstructor("CustomEvent", new[] { "Object" }, _ => null);
        RegisterConstructor("Event", new[] { "Object" }, _ => null);

        RegisterConstructor("NodeList", new[] { "Object" }, _ => null);
        RegisterConstructor("HTMLCollection", new[] { "Object" }, _ => null);
        RegisterConstructor("DOMTokenList", new[] { "Object" }, _ => null);

        RegisterConstructor("NamedNodeMap", new[] { "Object" }, _ => null);
        RegisterConstructor("Attr", new[] { "Object" }, _ => null);

        RegisterConstructor("URL", new[] { "Object" }, _ => null);
        RegisterConstructor("URLSearchParams", new[] { "Object" }, _ => null);
    }
}
