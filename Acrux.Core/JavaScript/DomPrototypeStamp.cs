#if !USE_MULTIPLE_JS_ENGINE
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace Acrux.Core.JavaScript;

/// <summary>
/// Gives a freshly wrapped host object the prototype of its DOM interface. Doing it while the
/// wrapper is created — instead of round-tripping 'Object.setPrototypeOf' through the engine on
/// every node read — is what makes 'el instanceof Element' and the accessors the prototypes carry
/// (document.defaultView, el.dataset, the on* handlers) answer on the first read of any element,
/// whichever host method handed the element out.
/// </summary>
internal static class DomPrototypeStamp
{
    private static readonly ConditionalWeakTable<Engine, Cache> Caches = new();

    private sealed class Cache
    {
        public ObjectInstance? Map;
        public readonly Dictionary<string, ObjectInstance?> Prototypes = new(StringComparer.Ordinal);
    }

    public static void Apply(Engine engine, ObjectInstance wrapper, IDomPrototypeHost host)
    {
        try
        {
            var chain = host.__domTypeChain;
            if (chain == null || chain.Length == 0) return;
            var typeName = chain[chain.Length - 1];
            if (string.IsNullOrEmpty(typeName)) return;

            // The prototype objects belong to a page: a reload rebuilds them, so the map is read
            // again and the resolved prototypes dropped when the map object is not the cached one.
            if (engine.GetValue("__domProto") is not ObjectInstance map) return;
            var cache = Caches.GetValue(engine, _ => new Cache());
            if (!ReferenceEquals(map, cache.Map))
            {
                cache.Map = map;
                cache.Prototypes.Clear();
            }
            if (!cache.Prototypes.TryGetValue(typeName, out var proto))
            {
                proto = map.Get(typeName) as ObjectInstance;
                cache.Prototypes[typeName] = proto;
            }
            if (proto != null && !ReferenceEquals(proto, wrapper.Prototype))
                wrapper.Prototype = proto;
        }
        catch
        {
            // An unstamped wrapper still answers every host member; the prototype is what the page
            // reads through, and the fixup the host runs on a wrapper covers this one later.
        }
    }
}
#endif
