namespace Acrux.Core.JavaScript;

internal static class JsCallbackStore
{
    internal static readonly string JsSetup = @"
var __g_cbid = 0;
var __g_cbs = {};
var __g_fnMap = new WeakMap();

function __g_store(fn) {
    var id = __g_fnMap.get(fn);
    if (id !== undefined) return id;
    id = ++__g_cbid;
    __g_cbs[id] = fn;
    __g_fnMap.set(fn, id);
    return id;
}

function __g_storeForce(fn) {
    var id = ++__g_cbid;
    __g_cbs[id] = fn;
    return id;
}

function __g_invoke(id, arg) {
    var fn = __g_cbs[id];
    if (fn) {
        if (arg !== undefined) return fn(arg);
        return fn();
    }
}

function __g_remove(id) {
    delete __g_cbs[id];
}

// A NodeFilter (DOM 4 §4.6.2) reaches the host either as the callback interface itself — a bare
// function, which the reference accepts (measured: createTreeWalker with a function filter walked
// on) — or as an object carrying acceptNode(). Both are stored under one id so the host can ask
// the page about a candidate node without holding a JavaScript value of its own.
var __g_filterMap = new WeakMap();
// The id is also kept beside the object it was made from, because 'walker.filter' is answered back
// by identity (measured: the reference hands the page the very object it passed in) and a
// JavaScript object converted into a host parameter cannot be given back as the same object —
// only the interface prototype, which reads this map, can.
var __g_filterById = new Map();

// The answer a filter gives is a long, not the value the page returned (measured: 1.5 accepts,
// '3' skips, and 0, NaN, null, undefined, false and an object all reject), so it is coerced here —
// where the coercion is the JavaScript one — and the host only has to map the number.
function __acruxFilterAnswer(v) {
    var x = Number(v);
    if (x !== x || x === Infinity || x === -Infinity) return 0;
    return x | 0;
}

// The id of a filter that is an object but has no callable acceptNode() is negative: the walk is
// not refused at the factory — the reference only complains when a step actually asks the filter
// whether it wants a node. Each one gets its own id, because two refused filters must still be
// told apart when the page reads 'walker.filter' back.
var __g_notCallable = 0;

function __acruxRememberFilter(id, f) {
    if (id) __g_filterById.set(id, f);
    return id;
}
function __acruxFilterOf(id) {
    if (!id) return null;
    var f = __g_filterById.get(id);
    return f === undefined ? null : f;
}
function __acruxStoreFilter(f) {
    if (f == null) return 0;
    if (typeof f === 'function') {
        return __acruxRememberFilter(__g_storeForce(function (n) { return __acruxFilterAnswer(f.call(f, n)); }), f);
    }
    if (typeof f !== 'object') return 0;
    if (typeof f.acceptNode !== 'function') return __acruxRememberFilter(--__g_notCallable, f);
    var seen = __g_filterMap.get(f);
    if (seen !== undefined) return seen;
    var id = __g_storeForce(function (n) { return __acruxFilterAnswer(f.acceptNode.call(f, n)); });
    __g_filterMap.set(f, id);
    return __acruxRememberFilter(id, f);
}
";
}
