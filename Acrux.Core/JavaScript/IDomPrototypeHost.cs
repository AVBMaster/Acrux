namespace Acrux.Core.JavaScript;

/// <summary>
/// A host object whose JavaScript side answers to a chain of DOM interface names. The last name is
/// the interface the object is an instance of; the ones before it are the interfaces it inherits
/// from, and they are what 'el instanceof Node' walks.
/// </summary>
/// <remarks>
/// The declaration is not tied to one engine: the hosts carry it whatever the JavaScript engine
/// configuration is, and only the stamping that reads it (DomPrototypeStamp) is Jint-specific.
/// </remarks>
internal interface IDomPrototypeHost
{
    string[] __domTypeChain { get; }
}

/// <summary>
/// A host whose members the page enumerates from the object itself rather than from its prototype.
/// One interface in the platform is written that way: a Location carries its fourteen members as own
/// properties, and 'Object.keys(window.location)' answers them (measured), while every other host
/// answers an empty list.
/// </summary>
internal interface IDomEnumerableHost
{
    string[] __domOwnKeys { get; }
}
