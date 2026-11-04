using System.Text;

namespace Acrux.Core.JavaScript;

/// <summary>A URL record the way the URL Standard builds one (URL §4, §5, §6): the scheme, the
/// credentials, the host in the form its scheme allows, the port the scheme implies, the path as the
/// segments it folds to, the query and the fragment — and one serialisation for all of them.</summary>
/// <remarks>The platform's own address type is not this: it lowercases a host a non-special scheme
/// keeps verbatim, turns a backslash into a separator whatever the scheme is, hands the brackets of
/// an IPv6 host back as the hostname, gives a file, data or about address an origin of its own where
/// the standard answers <c>null</c>, and has no way to write a part back over the whole (§7). Every
/// percent-encode set, host form and default port here is the standard's own (§5.1, §6.1, §6.3,
/// §6.5, §6.6, §6.7).</remarks>
internal sealed class WhatwgUrl
{
    public string Scheme = "";
    public string Username = "";
    public string Password = "";
    public string Host = "";
    public int? Port;
    public List<string> Segments = new();
    public string OpaquePath = "";
    public bool HasOpaquePath;
    public bool HasAuthority;
    public string? Query;
    public string? Fragment;

    public bool IsSpecial => IsSpecialScheme(Scheme);

    /// <summary>URL §6.3: the schemes that carry a default port, an authority a page may leave out,
    /// and a path that can never be empty.</summary>
    public static bool IsSpecialScheme(string scheme) =>
        scheme is "ftp" or "file" or "http" or "https" or "ws" or "wss";

    public static int? DefaultPort(string scheme) => scheme switch
    {
        "ftp" => 21,
        "http" => 80,
        "https" => 443,
        "ws" => 80,
        "wss" => 443,
        _ => null,
    };

    /// <summary>§6.1: the schemes that can hold credentials and a port at all — a file url can name
    /// neither, which is why its setters are silent.</summary>
    public bool CanHoldCredentials => Scheme != "file" && IsSpecialNoFile(Scheme);

    private static bool IsSpecialNoFile(string scheme) =>
        scheme is "ftp" or "http" or "https" or "ws" or "wss";

    public WhatwgUrl Clone() => new()
    {
        Scheme = Scheme,
        Username = Username,
        Password = Password,
        Host = Host,
        Port = Port,
        Segments = new List<string>(Segments),
        OpaquePath = OpaquePath,
        HasOpaquePath = HasOpaquePath,
        HasAuthority = HasAuthority,
        Query = Query,
        Fragment = Fragment,
    };

    // ── §5.1: the percent-encode sets ───────────────────────────────────────

    internal enum Set { Fragment, Query, SpecialQuery, Path, Userinfo, Urlencoded }

    /// <summary>Encodes the code points the named set forbids (§5.1): the code point is cut into its
    /// UTF-8 bytes and every byte written as <c>%XX</c> with an uppercase tail, which is why one
    /// accented letter comes back as three escapes and not as one.</summary>
    public static string Encode(string text, Set set)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var scalar = (int)c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            if (!Needs(scalar, set))
            {
                sb.Append(char.ConvertFromUtf32(scalar));
                continue;
            }
            foreach (var b in Encoding.UTF8.GetBytes(char.ConvertFromUtf32(scalar)))
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>Encodes the characters the set forbids and leaves a written escape for the character
    /// it already names (§4.2, the rule inside a buffer): a path that carries <c>%2F</c> means a
    /// slash inside one segment, not a second segment, and an address that carries <c>%41</c> keeps
    /// the letter the page wrote escaped rather than folding it away.</summary>
    public static string EncodeKeeping(string text, Set set)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '%' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            {
                sb.Append('%').Append(text[i + 1]).Append(text[i + 2]);
                i += 2;
                continue;
            }
            var scalar = (int)c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            if (!Needs(scalar, set))
            {
                sb.Append(char.ConvertFromUtf32(scalar));
                continue;
            }
            foreach (var b in Encoding.UTF8.GetBytes(char.ConvertFromUtf32(scalar)))
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private static bool Needs(int scalar, Set set)
    {
        // Every set is the C0 control set underneath it: the controls and anything above the ASCII
        // range are always encoded (§5.1).
        if (scalar <= 0x1F || scalar > 0x7E) return true;
        var c = (char)scalar;
        return set switch
        {
            Set.Fragment => c is ' ' or '"' or '<' or '>' or '`',
            Set.Query => c is ' ' or '"' or '#' or '<' or '>',
            Set.SpecialQuery => c is ' ' or '"' or '#' or '<' or '>' or '\'',
            Set.Path => c is ' ' or '"' or '#' or '<' or '>' or '?' or '`' or '{' or '}',
            Set.Userinfo => c is ' ' or '"' or '#' or '<' or '>' or '?' or '`' or '{' or '}'
                or '/' or ':' or ';' or '=' or '@' or '[' or '\\' or ']' or '^' or '|',
            // The form-urlencoded serializer (§7.1): an ASCII letter, a digit or one of
            // "*-._" is written as itself, a space becomes a plus, everything else an escape.
            Set.Urlencoded => !(char.IsAsciiLetter((char)c) || char.IsAsciiDigit((char)c)
                || c is '*' or '-' or '.' or '_'),
            _ => false,
        };
    }

    /// <summary>Percent-decode (§5.4): the escapes are turned back into bytes and read as UTF-8, and
    /// a run that is not UTF-8 is the replacement character rather than an error. An escape whose byte
    /// is named by <paramref name="keep"/> is left written as an escape.</summary>
    public static string Decode(string text, string keep = "")
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new StringBuilder(text.Length);
        var pending = new List<byte>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '%' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
            {
                var value = Convert.ToInt32(text.Substring(i + 1, 2), 16);
                i += 2;
                if (keep.Length > 0 && keep.IndexOf((char)value) >= 0)
                {
                    sb.Append('%').Append(text[i - 1]).Append(text[i]);
                    continue;
                }
                pending.Add((byte)value);
                continue;
            }
            Flush(pending, sb);
            sb.Append(c);
        }
        Flush(pending, sb);
        return sb.ToString();
    }

    private static void Flush(List<byte> pending, StringBuilder sb)
    {
        if (pending.Count == 0) return;
        sb.Append(Utf8Decode(pending.ToArray()));
        pending.Clear();
    }

    private static string Utf8Decode(byte[] bytes)
    {
        var decoder = Encoding.GetEncoding("utf-8", EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
        return decoder.GetString(bytes);
    }

    // ── §6.1, §6.4, §6.5: the host forms ────────────────────────────────────

    private static bool IsForbiddenHostCodePoint(char c) =>
        c is '\0' or '\t' or '\n' or '\r' or ' ' or '#' or '/' or ':' or '<' or '>' or '?' or '@'
            or '[' or '\\' or ']' or '^' or '|' or '%';

    /// <summary>Parses a host (§6.1.4, §6.1.6, §6.1.7). An IPv6 host lives between brackets and comes
    /// back compressed and lowercase; a host of a special scheme is a domain — decoded, case-folded —
    /// that may still turn out to be four numbers in disguise; the host of any other scheme is opaque,
    /// which is what lets <c>foo://Example.COM</c> keep the case a page wrote.</summary>
    public static string? ParseHost(string input, bool special, out bool failure)
    {
        failure = false;
        if (input.Length == 0) return "";
        if (input[0] == '[')
        {
            if (input[^1] != ']') { failure = true; return null; }
            var made = ParseIpv6(input[1..^1]);
            if (made == null) { failure = true; return null; }
            return "[" + made + "]";
        }
        if (!special)
        {
            var opaque = Decode(input);
            foreach (var c in opaque)
            {
                if (IsForbiddenHostCodePoint(c)) { failure = true; return null; }
            }
            return opaque;
        }
        var domain = Decode(input);
        foreach (var c in domain)
        {
            if (IsForbiddenHostCodePoint(c)) { failure = true; return null; }
        }
        domain = domain.ToLowerInvariant();
        return ParseIpv4(domain) ?? domain;
    }

    /// <summary>IPv4 parse (§6.4): the one to four numbers a dotted host may be written as, in
    /// decimal, in hexadecimal behind <c>0x</c> or in octal behind a leading zero, with the last part
    /// carrying every octet the earlier ones did not name.</summary>
    public static string? ParseIpv4(string input)
    {
        var text = input;
        if (text.EndsWith('.')) text = text[..^1];
        if (text.Length == 0) return null;
        var parts = text.Split('.');
        if (parts.Length > 5) return null;
        var numbers = new List<long>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0) return null;
            if (!TryNumber(part, out var value)) return null;
            numbers.Add(value);
        }
        for (var i = 0; i < numbers.Count - 1; i++)
        {
            if (numbers[i] > 255) return null;
        }
        var last = numbers[^1];
        if (last >= (1L << (8 * (5 - numbers.Count)))) return null;
        var address = last;
        for (var i = 0; i < numbers.Count - 1; i++) address += numbers[i] << (8 * (3 - i));
        return string.Join('.',
            ((address >> 24) & 255).ToString(), ((address >> 16) & 255).ToString(),
            ((address >> 8) & 255).ToString(), (address & 255).ToString());
    }

    private static bool TryNumber(string text, out long value)
    {
        value = 0;
        var radix = 10;
        var digits = text;
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            radix = 16;
            digits = digits[2..];
            if (digits.Length == 0) return true;
        }
        else if (digits.Length > 1 && digits[0] == '0')
        {
            radix = 8;
            digits = digits[1..];
        }
        var made = 0L;
        foreach (var c in digits)
        {
            var d = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' when radix == 16 => c - 'a' + 10,
                >= 'A' and <= 'F' when radix == 16 => c - 'A' + 10,
                _ => -1,
            };
            if (d < 0 || d >= radix) return false;
            made = made * radix + d;
            if (made > 0xFFFFFFFFL) return false;
        }
        value = made;
        return true;
    }

    /// <summary>IPv6 parse and serialise (§6.5): eight pieces of sixteen bits, one run of them allowed
    /// to stand as <c>::</c>, and a trailing dotted quad read as two pieces. The answer is lowercase,
    /// without a piece's leading zeroes, and the leftmost longest run of empty pieces compresses.</summary>
    public static string? ParseIpv6(string input)
    {
        var pieces = new List<int>();
        var compressAt = -1;
        var at = 0;
        if (at + 1 < input.Length && input[at] == ':' && input[at + 1] == ':')
        {
            at += 2;
            compressAt = pieces.Count;
            while (at < input.Length && input[at] == ':') at++;
        }
        while (at < input.Length)
        {
            var value = 0;
            var seen = 0;
            while (seen < 4 && at < input.Length && Uri.IsHexDigit(input[at]))
            {
                value = value * 16 + HexValue(input[at]);
                at++;
                seen++;
            }
            if (at < input.Length && input[at] == '.')
            {
                if (seen == 0) return null;
                // The dotted quad begins where the piece being read began: the digits already taken
                // are its first octet, not a piece of their own (§6.5).
                var parts = input[(at - seen)..].Split('.');
                if (parts.Length != 4) return null;
                var numbers = new int[4];
                for (var i = 0; i < 4; i++)
                {
                    if (!TryNumber(parts[i], out var n) || n > 255) return null;
                    numbers[i] = (int)n;
                }
                pieces.Add((numbers[0] << 8) + numbers[1]);
                pieces.Add((numbers[2] << 8) + numbers[3]);
                at = input.Length;
                break;
            }
            if (seen == 0) return null;
            pieces.Add(value);
            if (at >= input.Length) break;
            if (input[at] != ':') return null;
            at++;
            if (at < input.Length && input[at] == ':')
            {
                if (compressAt >= 0) return null;
                at++;
                compressAt = pieces.Count;
                while (at < input.Length && input[at] == ':') at++;
            }
        }
        if (compressAt >= 0)
        {
            var missing = 8 - pieces.Count;
            if (missing < 1) return null;
            for (var i = 0; i < missing; i++) pieces.Insert(compressAt++, 0);
        }
        else if (pieces.Count != 8) return null;

        // §6.5: an address whose first six pieces name no network at all and the sixth is 0xFFFF is
        // written as the IPv4 address it maps, because that is the address it means.
        if (pieces[0] == 0 && pieces[1] == 0 && pieces[2] == 0 && pieces[3] == 0 && pieces[4] == 0
            && pieces[5] == 0xFFFF)
        {
            var quad = ((pieces[6] << 16) + pieces[7]);
            return string.Concat("::ffff:", ((quad >> 24) & 255).ToString(), ".",
                ((quad >> 16) & 255).ToString(), ".", ((quad >> 8) & 255).ToString(), ".",
                (quad & 255).ToString());
        }
        var bestStart = -1;
        var bestLength = 0;
        var runStart = -1;
        for (var i = 0; i <= pieces.Count; i++)
        {
            if (i < pieces.Count && pieces[i] == 0)
            {
                if (runStart < 0) runStart = i;
                continue;
            }
            if (runStart >= 0 && i - runStart > bestLength)
            {
                bestStart = runStart;
                bestLength = i - runStart;
            }
            runStart = -1;
        }
        if (bestLength >= 2)
        {
            var head = string.Join(":", pieces.GetRange(0, bestStart).ConvertAll(HexPiece));
            var tail = string.Join(":", pieces.GetRange(bestStart + bestLength,
                pieces.Count - bestStart - bestLength).ConvertAll(HexPiece));
            return head + "::" + tail;
        }
        return string.Join(':', pieces.ConvertAll(HexPiece));
    }

    private static string HexPiece(int piece) => piece.ToString("x");

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    /// <summary>§6.6: a port is a run of ASCII digits at most 65535; anything else is failure, and the
    /// port a scheme already implies is not kept at all.</summary>
    private static bool TryPort(string text, out int port)
    {
        port = 0;
        if (text.Length == 0) return false;
        var made = 0;
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c)) return false;
            made = made * 10 + (c - '0');
            if (made > 65535) return false;
        }
        port = made;
        return true;
    }

    // ── §4.1, §4.2: the parser ──────────────────────────────────────────────

    /// <summary>Runs the basic URL parser over the input with the base the page gave (§4.1). A failure
    /// is a null answer; <paramref name="baseBad"/> says it was the base that could not be read, which
    /// is the other of the two messages the constructor names.</summary>
    public static WhatwgUrl? Parse(string input, string? baseHref, out bool baseBad)
    {
        baseBad = false;
        var text = Clean(input ?? "");
        var schemeEnd = FindScheme(text);
        if (schemeEnd > 0)
        {
            var made = new WhatwgUrl { Scheme = text[..schemeEnd].ToLowerInvariant() };
            made.ParseRest(text[(schemeEnd + 1)..]);
            // §6.1: a scheme that cannot work without a host and was given none is not a url.
            return made.IsWellFormed() ? made : null;
        }
        if (baseHref == null) return null;
        var baseUri = Parse(baseHref, null, out _);
        if (baseUri == null) { baseBad = true; return null; }
        var url = baseUri.Clone();
        url.ResolveAgainst(baseUri, text);
        return url;
    }

    /// <summary>§4.1: the C0 controls and spaces around the input are dropped and every tab and
    /// newline inside it is taken out before a single state runs.</summary>
    private static string Clean(string text)
    {
        var trimmed = text.Trim('\0', '\t', '\n', '\r', ' ', (char)0x0B, (char)0x0C);
        if (trimmed.IndexOf('\t') < 0 && trimmed.IndexOf('\n') < 0 && trimmed.IndexOf('\r') < 0)
            return trimmed;
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (c is '\t' or '\n' or '\r') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The length of the scheme prefix, or 0 when the input opens with anything else (§6.2:
    /// a letter, then letters, digits and any of <c>+ - .</c>, then the colon).</summary>
    public static int FindScheme(string text)
    {
        if (text.Length == 0 || !char.IsAsciiLetter(text[0])) return 0;
        var i = 1;
        while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '+' or '-' or '.')) i++;
        return i < text.Length && text[i] == ':' ? i : 0;
    }

    private void ParseRest(string rest)
    {
        var special = IsSpecialScheme(Scheme);
        if (Scheme == "file")
        {
            // §6.3.2: a file address has an authority even when the page wrote nothing between the
            // slashes, and the two slashes come back in front of an absolute path.
            HasAuthority = true;
            HasOpaquePath = false;
            string body;
            if (rest.StartsWith("//"))
            {
                var end = AuthorityEnd(rest, false, 2);
                ParseAuthority(rest[2..end]);
                body = rest[end..];
            }
            else
            {
                body = rest.StartsWith("/") ? rest : "/" + rest;
            }
            ParsePathAndQuery(body, true);
            if (Segments.Count == 0) Segments.Add("");
            return;
        }
        if (special)
        {
            // §4.2, the special authority ignore-slashes and no-slashes states: however many slashes
            // the page wrote, the authority begins after them, and a scheme that needs a host and
            // finds none is the failure the constructor reports.
            HasAuthority = true;
            var body = rest.TrimStart('/', '\\');
            var end = AuthorityEnd(body, true, 0);
            ParseAuthority(body[..end]);
            ParsePathAndQuery(body[end..], true);
            FinishSpecialPath(true);
            return;
        }
        if (rest.StartsWith("//"))
        {
            HasAuthority = true;
            var end = AuthorityEnd(rest, false, 2);
            ParseAuthority(rest[2..end]);
            ParsePathAndQuery(rest[end..], true);
            return;
        }
        if (rest.Length > 0 && rest[0] != '/')
        {
            // §4.2, path or opaque state: a non-special scheme that was given neither slashes nor a
            // leading slash has an opaque path, and an opaque path is written the way it came in.
            HasOpaquePath = true;
            SplitOpaque(rest);
            return;
        }
        ParsePathAndQuery(rest, false);
    }

    private void FinishSpecialPath(bool special)
    {
        if (special && !HasOpaquePath && Segments.Count == 0) Segments.Add("");
    }

    private static int AuthorityEnd(string rest, bool special, int start)
    {
        for (var i = start; i < rest.Length; i++)
        {
            var c = rest[i];
            if (c is '/' or '?' or '#') return i;
            if (special && c == '\\') return i;
        }
        return rest.Length;
    }

    /// <summary>§4.2 authority state: the last at-sign divides the credentials from the host, and a
    /// colon after the host is the port — except when the host is an IPv6 address, whose colons are
    /// part of it. A file address keeps neither credential whatever the page wrote (§6.3.2).</summary>
    private void ParseAuthority(string authority)
    {
        var hostPart = authority;
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var userinfo = authority[..at];
            hostPart = authority[(at + 1)..];
            if (Scheme != "file")
            {
                var colon = userinfo.IndexOf(':');
                Username = EncodeKeeping(colon < 0 ? userinfo : userinfo[..colon], Set.Userinfo);
                Password = colon < 0 ? "" : EncodeKeeping(userinfo[(colon + 1)..], Set.Userinfo);
            }
        }
        var portText = "";
        var hasPort = false;
        if (hostPart.Length > 0 && hostPart[0] == '[')
        {
            var close = hostPart.IndexOf(']');
            if (close >= 0)
            {
                // The brackets belong to the host: the colon that follows one is the port, whatever
                // the colons inside the address are (§6.1).
                var after = hostPart[(close + 1)..];
                hostPart = hostPart[..(close + 1)];
                if (after.StartsWith(':')) { hasPort = true; portText = after[1..]; }
                else if (after.Length > 0) { hasPort = true; portText = after; }
            }
        }
        else
        {
            var colon = hostPart.IndexOf(':');
            if (colon >= 0) { portText = hostPart[(colon + 1)..]; hasPort = true; hostPart = hostPart[..colon]; }
        }
        var parsed = ParseHost(hostPart, IsSpecialScheme(Scheme), out var failed);
        if (failed) { Host = ""; Port = null; return; }
        Host = parsed ?? "";
        if (hasPort && portText.Length > 0)
        {
            Port = TryPort(portText, out var port) ? port : null;
        }
        if (Port != null && DefaultPort(Scheme) == Port) Port = null;
    }

    private void SplitOpaque(string rest)
    {
        var hash = rest.IndexOf('#');
        if (hash >= 0)
        {
            var fragment = rest[(hash + 1)..];
            Fragment = fragment.Length == 0 ? null : EncodeKeeping(fragment, Set.Fragment);
            rest = rest[..hash];
        }
        var question = rest.IndexOf('?');
        if (question >= 0)
        {
            Query = EncodeKeeping(rest[(question + 1)..], Set.Query);
            rest = rest[..question];
        }
        OpaquePath = EncodeKeeping(rest, Set.Path);
    }

    /// <summary>The states after the authority (§4.2): the fragment is what a hash leaves, the query
    /// what a question mark leaves, and the rest is the path. A query of a special scheme forbids one
    /// more character than the query of any other.</summary>
    private void ParsePathAndQuery(string rest, bool allowEmptyPath)
    {
        var querySet = IsSpecialScheme(Scheme) ? Set.SpecialQuery : Set.Query;
        var text = rest;
        var hash = text.IndexOf('#');
        if (hash >= 0)
        {
            var fragment = text[(hash + 1)..];
            Fragment = fragment.Length == 0 ? null : EncodeKeeping(fragment, Set.Fragment);
            text = text[..hash];
        }
        var question = text.IndexOf('?');
        if (question >= 0)
        {
            Query = EncodeKeeping(text[(question + 1)..], querySet);
            text = text[..question];
        }
        if (text.Length == 0 && !allowEmptyPath) return;
        ApplyPath(text);
    }

    /// <summary>§4.2 path state: segments divided by slashes — a backslash dividing them only for a
    /// scheme that has an authority to divide — with the single- and double-dot segments folded out of
    /// the list and every remaining one encoded with the path set.</summary>
    private void ApplyPath(string path)
    {
        Segments = new List<string>();
        if (path.Length == 0) return;
        var special = IsSpecialScheme(Scheme);
        var body = path;
        var leading = body[0] is '/' || (special && body[0] == '\\');
        if (leading) body = body[1..];
        var pieces = special ? body.Split('/', '\\') : body.Split('/');
        var trailing = false;
        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            var isLast = i == pieces.Length - 1;
            if (piece.Length == 0)
            {
                trailing = isLast && i > 0;
                continue;
            }
            if (IsSingleDot(piece))
            {
                trailing = isLast;
                continue;
            }
            if (IsDoubleDot(piece))
            {
                if (Segments.Count > 0) Segments.RemoveAt(Segments.Count - 1);
                trailing = isLast;
                continue;
            }
            trailing = false;
            Segments.Add(EncodeKeeping(piece, Set.Path));
        }
        if (trailing) Segments.Add("");
    }

    private static bool IsSingleDot(string text) =>
        text == "." || text.Equals("%2e", StringComparison.OrdinalIgnoreCase);

    private static bool IsDoubleDot(string text) =>
        text == ".." || text.Equals(".%2e", StringComparison.OrdinalIgnoreCase)
            || text.Equals("%2e.", StringComparison.OrdinalIgnoreCase)
            || text.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase);

    /// <summary>§4.2, the states a relative reference runs through: the double slash takes the base's
    /// scheme, a leading slash replaces the path, a query or a fragment replaces only what it names,
    /// and a bare path is resolved against the directory the base's path names.</summary>
    private void ResolveAgainst(WhatwgUrl baseUri, string text)
    {
        if (text.StartsWith("//"))
        {
            HasAuthority = true;
            HasOpaquePath = false;
            var end = AuthorityEnd(text, IsSpecialScheme(Scheme), 2);
            ParseAuthority(text[2..end]);
            ParsePathAndQuery(text[end..], true);
            FinishSpecialPath(IsSpecialScheme(Scheme));
            return;
        }
        var first = text.Length > 0 ? text[0] : '\0';
        if (first == '/' || (IsSpecialScheme(Scheme) && first == '\\'))
        {
            HasOpaquePath = false;
            Query = null;
            Fragment = null;
            ParsePathAndQuery(text, true);
            FinishSpecialPath(IsSpecialScheme(Scheme));
            return;
        }
        if (first == '?')
        {
            // §5.4: a query written over a url takes the place of the old query and of the old
            // fragment, which is what a page that follows '?x=1' from a '#anchor' address expects.
            HasOpaquePath = false;
            Fragment = null;
            ParsePathAndQuery("?" + text[1..], false);
            return;
        }
        if (first == '#')
        {
            var rest = text[1..];
            Fragment = rest.Length == 0 ? null : EncodeKeeping(rest, Set.Fragment);
            return;
        }
        if (text.Length == 0) return;
        HasOpaquePath = false;
        var basePath = baseUri.Pathname();
        var cut = basePath.LastIndexOf('/');
        var directory = cut >= 0 ? basePath[..(cut + 1)] : "";
        Query = null;
        Fragment = null;
        ParsePathAndQuery(directory + text, true);
        FinishSpecialPath(IsSpecialScheme(Scheme));
    }

    // ── §5.3: the serialisation ─────────────────────────────────────────────

    public string Href()
    {
        var sb = new StringBuilder();
        sb.Append(Scheme).Append(':');
        if (HasAuthority || IsSpecialScheme(Scheme))
        {
            sb.Append("//");
            if (Username.Length > 0 || Password.Length > 0)
            {
                sb.Append(Username);
                if (Password.Length > 0) sb.Append(':').Append(Password);
                sb.Append('@');
            }
            sb.Append(Host);
            if (Port != null) sb.Append(':').Append(Port.Value);
        }
        if (HasOpaquePath) sb.Append(OpaquePath);
        else sb.Append(Pathname());
        if (Query != null) sb.Append('?').Append(Query);
        if (Fragment != null) sb.Append('#').Append(Fragment);
        return sb.ToString();
    }

    /// <summary>§5.3: the path is the segments with the slash in front of each of them; a special
    /// scheme never answers the empty string here.</summary>
    public string Pathname()
    {
        if (HasOpaquePath) return OpaquePath;
        if (Segments.Count == 0) return IsSpecialScheme(Scheme) || HasAuthority ? "/" : "";
        return "/" + string.Join("/", Segments.ToArray());
    }

    /// <summary>URL §2.6 with §6.5: the schemes that can name an origin name it as scheme, host and
    /// port; every other one — file and data and about among them — has an opaque origin, and an
    /// opaque origin serialises as the string <c>null</c>.</summary>
    public string Origin()
    {
        if (Scheme == "blob")
        {
            var inner = OpaquePath.Length > 0 ? OpaquePath : (Href().Length > 5 ? Href()[5..] : "");
            var nested = Parse(inner, null, out _);
            return nested?.Origin() ?? "null";
        }
        if (Scheme is "ftp" or "http" or "https" or "ws" or "wss")
        {
            var made = Scheme + "://" + Host;
            return Port != null ? made + ":" + Port.Value : made;
        }
        return "null";
    }

    // ── §7: the setters, each run as the parser state it belongs to ──────────

    /// <summary>§7.2: the scheme may only be written over a url of the same kind — a special scheme
    /// does not become a non-special one and the other way round — and a name the syntax refuses is
    /// dropped without a word.</summary>
    public bool SetProtocol(string value)
    {
        var text = Clean(value);
        if (text.EndsWith(":")) text = text[..^1];
        if (text.Length == 0 || !char.IsAsciiLetter(text[0])) return false;
        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return false;
        }
        var made = text.ToLowerInvariant();
        if (IsSpecialScheme(made) != IsSpecialScheme(Scheme)) return false;
        if (made == Scheme) return true;
        if (made == "file" || Scheme == "file") return false;
        Scheme = made;
        if (Port != null && DefaultPort(Scheme) == Port) Port = null;
        return true;
    }

    public bool SetUsername(string value)
    {
        if (!CanHoldCredentials) return false;
        Username = EncodeKeeping(Clean(value), Set.Userinfo);
        return true;
    }

    public bool SetPassword(string value)
    {
        if (!CanHoldCredentials) return false;
        Password = EncodeKeeping(Clean(value), Set.Userinfo);
        return true;
    }

    /// <summary>§7.4, §7.5: the host setter takes a host and, behind the last colon it names, a port;
    /// the hostname setter takes the host alone. An empty host of a scheme that must have one is
    /// refused, and so is a host the syntax cannot read.</summary>
    public bool SetHost(string value)
    {
        if (Scheme == "file" && Clean(value).Length == 0) return true;
        if (!CanHaveHost()) return false;
        var text = Clean(value);
        var hostText = text;
        var portText = "";
        var hasPort = false;
        if (text.Length > 0 && text[0] == '[')
        {
            var close = text.IndexOf(']');
            if (close >= 0)
            {
                var after = text[(close + 1)..];
                hostText = text[..(close + 1)];
                if (after.StartsWith(':')) { hasPort = true; portText = after[1..]; }
            }
        }
        else
        {
            var colon = text.IndexOf(':');
            if (colon >= 0) { hostText = text[..colon]; hasPort = true; portText = text[(colon + 1)..]; }
        }
        var made = ParseHost(hostText, IsSpecialScheme(Scheme), out var failed);
        if (failed || made == null) return false;
        if (made.Length == 0 && IsSpecialNoFile(Scheme)) return false;
        Host = made;
        HasAuthority = true;
        if (hasPort)
        {
            if (portText.Length == 0) Port = null;
            else if (TryPort(portText, out var port)) Port = port;
            else return false;
            if (Port != null && DefaultPort(Scheme) == Port) Port = null;
        }
        return true;
    }

    public bool SetHostname(string value)
    {
        if (Scheme == "file")
        {
            Host = "";
            return true;
        }
        if (!CanHaveHost()) return false;
        var text = Clean(value);
        if (text.Contains('/') || text.Contains('\\') || text.Contains('?') || text.Contains('#'))
            return false;
        var made = ParseHost(text, IsSpecialScheme(Scheme), out var failed);
        if (failed || made == null || (made.Length == 0 && IsSpecialNoFile(Scheme))) return false;
        Host = made;
        HasAuthority = true;
        return true;
    }

    /// <summary>§7.6: an empty value takes the port off; a number the scheme already implies is not
    /// kept; anything the port syntax refuses leaves the url as it was.</summary>
    public bool SetPort(string value)
    {
        if (!CanHoldCredentials) return false;
        var text = Clean(value);
        if (text.Length == 0)
        {
            Port = null;
            return true;
        }
        if (!TryPort(text, out var port)) return false;
        Port = DefaultPort(Scheme) == port ? null : port;
        return true;
    }

    /// <summary>§7.7: the path is written by running the path states over the value, so a question
    /// mark in it starts a query and a hash starts a fragment; the parts the value does not name keep
    /// what they were. A url whose path is opaque has no path to write.</summary>
    public bool SetPathname(string value)
    {
        if (HasOpaquePath) return false;
        var text = Clean(value);
        if (IsSpecialScheme(Scheme) && !text.StartsWith("/") && !text.StartsWith("\\"))
            text = "/" + text;
        var savedQuery = Query;
        var savedFragment = Fragment;
        Query = null;
        Fragment = null;
        ParsePathAndQuery(text, true);
        if (Query == null) Query = savedQuery;
        if (Fragment == null) Fragment = savedFragment;
        if (IsSpecialScheme(Scheme) && Segments.Count == 0) Segments.Add("");
        return true;
    }

    /// <summary>§7.8: the empty string takes the query off the url entirely — the address goes back
    /// to having no question mark in it — and a value that opens with one keeps it.</summary>
    public bool SetSearch(string value)
    {
        var text = Clean(value);
        if (text.Length == 0)
        {
            Query = null;
            return true;
        }
        if (text[0] == '?') text = text[1..];
        Query = EncodeKeeping(text, IsSpecialScheme(Scheme) ? Set.SpecialQuery : Set.Query);
        return true;
    }

    public bool SetHash(string value)
    {
        var text = Clean(value);
        if (text.Length == 0)
        {
            Fragment = null;
            return true;
        }
        if (text[0] == '#') text = text[1..];
        Fragment = EncodeKeeping(text, Set.Fragment);
        return true;
    }

    private bool CanHaveHost() => HasAuthority || IsSpecialScheme(Scheme);

    /// <summary>§6.1: an address of a scheme that names a host, with no host in it, is a failure.</summary>
    private bool IsWellFormed() => !IsSpecialNoFile(Scheme) || Host.Length > 0;

    /// <summary>The query as the search-params interface reads it: without the question mark the
    /// serialisation writes in front of it.</summary>
    public string QueryForSearchParams() => Query ?? "";

    /// <summary>§7.8 through the params view: the pairs a page wrote become the query, encoded with
    /// the form-urlencoded set the serializer names.</summary>
    public void SetQueryFromSearchParams(string serialized)
    {
        Query = serialized.Length == 0 ? "" : serialized;
    }

    /// <summary>The encoding the form-urlencoded serializer applies to one name or value (§7.1).</summary>
    public static string FormUrlencode(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var scalar = (int)c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            if (scalar == ' ')
            {
                sb.Append('+');
                continue;
            }
            if (!Needs(scalar, Set.Urlencoded))
            {
                sb.Append(char.ConvertFromUtf32(scalar));
                continue;
            }
            foreach (var b in Encoding.UTF8.GetBytes(char.ConvertFromUtf32(scalar)))
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
