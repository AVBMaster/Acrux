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
";
}
