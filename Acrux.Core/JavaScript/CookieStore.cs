namespace Acrux.Core.JavaScript;

/// <summary>
/// The cookie store a document's <c>document.cookie</c> reads and writes (HTML §8.4 with RFC 6265
/// §5.1–§5.4). A cookie is held by the name, the domain and the path it was written under — not by the
/// document that wrote it — so the same three written twice leaves one cookie, and a cookie written
/// under a path the document is not inside is not read back at all.
/// </summary>
/// <remarks>The store is the user agent's rather than the document's: one instance is shared by every
/// document the engine runs, which is the rule the storage areas follow too. The rules kept here are the
/// ones the pairing syntax fixes: a name that is empty or carries a delimiter is dropped without a word,
/// an attribute the parser does not know is left out of the cookie, a <c>max-age</c> that has run out
/// takes the cookie away instead of writing one, an <c>expires</c> the HTTP-date grammar cannot read is
/// not an expiry at all, and a read lists the longest path first and, where two paths are equally long,
/// the cookie that arrived first (§5.4).</remarks>
public sealed class CookieStore
{
    private sealed record Held(string Name, string Value, string Domain, string Path, bool HostOnly,
        bool Secure, DateTime? Expires, long Created);

    private readonly List<Held> _held = new();
    private long _clock;

    /// <summary>The string a page writes: one name-value pair, then the attributes that follow it.</summary>
    public void Write(string host, bool secureContext, string documentPath, string cookieString)
    {
        if (string.IsNullOrEmpty(cookieString)) return;

        var parts = cookieString.Split(';');
        var pair = parts[0];
        var equals = pair.IndexOf('=');
        // A string with no '=' in its first segment is not a cookie: the write does nothing rather than
        // keep the whole sentence as a name (§5.2 step 4).
        if (equals < 0) return;
        var name = Trim(pair[..equals]);
        var value = Trim(pair[(equals + 1)..]);
        if (name.Length == 0 || !IsPairable(name)) return;

        var domain = host;
        var hostOnly = true;
        var path = (string?)null;
        DateTime? expires = null;
        var secure = false;
        var expired = false;

        for (var i = 1; i < parts.Length; i++)
        {
            var attribute = parts[i];
            var cut = attribute.IndexOf('=');
            var key = Trim(cut < 0 ? attribute : attribute[..cut]);
            var text = cut < 0 ? "" : Trim(attribute[(cut + 1)..]);

            if (key.Equals("expires", StringComparison.OrdinalIgnoreCase))
            {
                if (TryHttpDate(text, out var when)) expires = when;
            }
            else if (key.Equals("max-age", StringComparison.OrdinalIgnoreCase))
            {
                // The value wins over an 'expires' the page also wrote, and a number that ran out is the
                // cookie being taken away rather than one that lives for no time at all (§5.2.2, §5.3).
                if (!TrySeconds(text, out var seconds)) continue;
                if (seconds <= 0)
                {
                    expired = true;
                    expires = DateTime.UnixEpoch;
                }
                else
                {
                    expires = DateTime.UtcNow.AddSeconds(seconds);
                }
            }
            else if (key.Equals("domain", StringComparison.OrdinalIgnoreCase))
            {
                var asked = (text.StartsWith(".") ? text[1..] : text).ToLowerInvariant();
                // The document's door may write for the host it is at or for a parent of it; a domain the
                // host does not sit inside is the whole cookie thrown away (§5.2.3).
                if (!HostMatchesDomain(host, asked)) return;
                domain = asked;
                hostOnly = false;
            }
            else if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                // A path that does not open with a separator is the attribute being ignored, so the
                // default path of the document's address takes its place (§5.2.4).
                if (text.StartsWith("/")) path = text;
            }
            else if (key.Equals("secure", StringComparison.OrdinalIgnoreCase))
            {
                if (!secureContext) return;
                secure = true;
            }
            // An 'httponly' written by a page is left out of the cookie: a script cannot write a cookie
            // its own document may not read. Anything else is not an attribute of this cookie.
        }

        var finalPath = path ?? DefaultPath(documentPath);
        Predicate<Held> sameThree = held => held.Name == name && held.Domain == domain && held.Path == finalPath;
        if (expired || (expires != null && expires.Value <= DateTime.UtcNow))
        {
            _held.RemoveAll(sameThree);
            return;
        }

        // The same name, domain and path is one cookie: what was there goes and this arrives afresh
        // (§5.3 steps 11–12).
        _held.RemoveAll(sameThree);
        _held.Add(new Held(name, value, domain, finalPath, hostOnly, secure, expires, ++_clock));
    }

    /// <summary>The string a page reads: the names and values of the cookies this document's address
    /// would be sent, in the order the reading rule gives them (§5.4).</summary>
    public string Read(string host, bool secureContext, string documentPath)
    {
        var living = new List<Held>();
        foreach (var held in _held)
        {
            if (held.Expires != null && held.Expires.Value <= DateTime.UtcNow) continue;
            if (!MatchesHost(held, host)) continue;
            if (held.Secure && !secureContext) continue;
            if (!PathMatches(held.Path, documentPath)) continue;
            living.Add(held);
        }
        living.Sort((a, b) =>
        {
            var byPath = b.Path.Length.CompareTo(a.Path.Length);
            return byPath != 0 ? byPath : a.Created.CompareTo(b.Created);
        });
        return string.Join("; ", living.Select(held => held.Name + "=" + held.Value));
    }

    /// <summary>§5.1.3: a host-only cookie is written for one host and read back for that host alone;
    /// one written with a domain is read for that domain and for the hosts beneath it.</summary>
    private static bool MatchesHost(Held held, string host) => held.HostOnly
        ? string.Equals(host, held.Domain, StringComparison.OrdinalIgnoreCase)
        : HostMatchesDomain(host, held.Domain);

    private static bool HostMatchesDomain(string host, string domain)
    {
        if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)) return true;
        if (domain.Length == 0 || host.Length == 0) return false;
        return host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The path a cookie lives under when the page did not name one: the directory of the
    /// document's own address, the file's name taken off (§5.1.4).</summary>
    private static string DefaultPath(string documentPath)
    {
        if (string.IsNullOrEmpty(documentPath) || documentPath[0] != '/') return "/";
        var last = documentPath.LastIndexOf('/');
        return last <= 0 ? "/" : documentPath[..(last + 1)];
    }

    /// <summary>§5.1.4: the cookie's path is the whole request path, or a leading run of it that ends on
    /// a separator — '/foo' is a prefix of '/foobar' and is not a match for it.</summary>
    private static bool PathMatches(string cookiePath, string requestPath)
    {
        if (cookiePath == requestPath) return true;
        if (!requestPath.StartsWith(cookiePath, StringComparison.Ordinal)) return false;
        if (cookiePath.EndsWith("/", StringComparison.Ordinal)) return true;
        return requestPath[cookiePath.Length] == '/';
    }

    /// <summary>The characters a name may not carry: the separators and delimiters of the pairing
    /// syntax, and the whitespace the pair was cut on (§4.1.1, §5.2).</summary>
    private static bool IsPairable(string name)
    {
        foreach (var c in name)
        {
            if (c is '=' or ';' or ',' or ' ' or '\t' or '\r' or '\n' or '\f' or '"') return false;
        }
        return true;
    }

    /// <summary>The trimmed string of the pairing algorithm: the control characters a cookie-string is cut
    /// on and the space and tab around a name or a value (§5.1.2).</summary>
    private static string Trim(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsTrimmed(text[start])) start++;
        while (end > start && IsTrimmed(text[end - 1])) end--;
        return text[start..end];

        static bool IsTrimmed(char c) => c is '\0' or '\t' or '\n' or '\r' or '\f' or ' ' or (char)0x0B;
    }

    /// <summary>§5.2.2: an optional sign and up to eighteen digits, and anything else is not a number of
    /// seconds.</summary>
    private static bool TrySeconds(string text, out long seconds)
    {
        seconds = 0;
        if (text.Length == 0 || text.Length > 18) return false;
        var start = 0;
        var negative = false;
        if (text[0] is '+' or '-')
        {
            negative = text[0] == '-';
            start = 1;
        }
        if (start >= text.Length) return false;
        var made = 0L;
        for (var i = start; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i])) return false;
            made = made * 10 + (text[i] - '0');
        }
        seconds = negative ? -made : made;
        return true;
    }

    private static bool TryHttpDate(string text, out DateTime when)
    {
        var formats = new[]
        {
            "R", "r", "ddd, dd MMM yyyy HH:mm:ss 'GMT'", "dd MMM yyyy HH:mm:ss 'GMT'",
            "ddd, dd-MMM-yyyy HH:mm:ss 'GMT'", "ddd MMM dd HH:mm:ss yyyy",
        };
        return DateTime.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out when);
    }
}
