using Acrux.Core.Dom;

namespace Acrux.Core.Css;

/// <summary>
/// The inline declaration block of an element. The <c>style</c> attribute IS the block, and
/// <see cref="Element.Style"/> is the map both the CSSOM and the cascade read, so the map is a
/// mirror of the attribute and nothing else: a page that replaces the block with
/// <c>setAttribute('style', …)</c> takes the declarations a script had written with it, and a
/// declaration whose value the grammar refuses is never in the map at all.
///
/// The mirror has to be refreshed before anyone reads the map — including the cascade, which is
/// where <c>getComputedStyle</c> gets its answer. Reading a map that is behind the attribute it
/// replaced reports a property the page no longer declares as the value the removed block had,
/// which no reference engine does (measured: with the attribute emptied, <c>color</c> reads back
/// <c>rgb(0, 0, 0)</c> after having been red).
/// </summary>
public static class InlineStyleBlock
{
    /// <summary>Bring the map in step with the attribute, re-parsing the block only when its text
    /// has changed since the map was last mirrored from it.</summary>
    public static void SyncWithAttribute(Element element)
    {
        var text = element.GetAttribute("style") ?? "";
        if (string.Equals(text, element.StyleMirrorText, StringComparison.Ordinal)) return;
        element.Style.Clear();
        foreach (var kv in Parse(text))
        {
            // The same question the CSSOM's setter asks, because the attribute and setProperty are
            // two roads to one block: a declaration the property's grammar refuses is never in the
            // map at all, and so never reads back as if it were (measured: an authored
            // 'style="text-decoration: underline red overline"' answers getPropertyValue with the
            // empty string and paints no line).
            if (!CssValueGrammar.TextIsValidFor(kv.Key, kv.Value)) continue;
            Store(element, kv.Key, kv.Value);
        }
        element.StyleMirrorText = text;
    }

    /// <summary>Record one declaration in the block, under the name the engine gives it and with
    /// the value spelled the way the engine prints it (see <see cref="CanonicalSpecifiedValue"/>).</summary>
    public static void Store(Element element, string name, string value)
    {
        // The three CSS 2.1 break names are recorded under the property they alias, with the value
        // folded onto that name's set (LegacyBreakAlias; measured through both ends of the block —
        // setProperty and an authored 'style="page-break-before: always"' each leave the block
        // carrying 'break-before: page').
        if (LegacyBreakAlias.ModernName(name) is { } modern)
        {
            value = LegacyBreakAlias.StoredValue(name, value) ?? value;
            name = modern;
        }
        // One name is special: the reference engine keeps no serialisation for the legacy
        // '-webkit-mask-box-image' shorthand, so writing it replaces the shorthand with its five
        // longhands — the pieces the value left out printed as 'initial' — and the shorthand itself
        // then reads back as the empty string (all measured, snapshots/_b259_mask_box_image_truth.txt).
        // The split therefore belongs to the block, not to the printer.
        if (MaskBoxImageShorthand.IsShorthand(name)
            && MaskBoxImageShorthand.Parse(value) is { } parts)
        {
            element.Style.Remove(name);
            for (int i = 0; i < MaskBoxImageShorthand.Longhands.Length; i++)
            {
                var longhand = MaskBoxImageShorthand.Longhands[i];
                element.Style[longhand] = CanonicalSpecifiedValue(longhand, parts[i]);
            }
            return;
        }
        // A shorthand written into a block takes the longhands it covers with it, whichever end of
        // the block they were written on (CSSOM §5.1.4.2, measured: a block that had
        // 'font-size: 16px' and is then given 'font: bold 12px/1.5 Arial' is left with the nineteen
        // parts of that 'font' and nothing else — the earlier 'font-size' is gone, and 'cssText'
        // prints only the shorthand). Only the shorthands whose part list has been measured here
        // take anything away; the rest are stored beside what the block already carried.
        if (ShorthandExpander.CssomLonghands(name) is { } covered)
        {
            foreach (var longhand in covered) element.Style.Remove(longhand);
            element.Style[name] = CanonicalSpecifiedValue(name, value);
            return;
        }
        // The other end of the same rule: a longhand written beside a shorthand that already carries
        // it reaches through that shorthand, and a block whose parts are complete is printed as the
        // shorthand again (see ShorthandReparse). The legacy '-webkit-mask-box-image' is exempt — it
        // is stored only as its five longhands, so folding them back would put a declaration the
        // reference engine never keeps into the block.
        if (name.StartsWith("-webkit-mask-box-image", StringComparison.OrdinalIgnoreCase))
        {
            element.Style[name] = CanonicalSpecifiedValue(name, value);
            return;
        }
        ShorthandReparse.DemoteCoveringShorthands(element, name);
        element.Style[name] = CanonicalSpecifiedValue(name, value);
        ShorthandReparse.TryFold(element, name);
    }

    /// <summary>The declarations of a block of style text, in the order the page wrote them.</summary>
    public static Dictionary<string, string> Parse(string? styleText) =>
        string.IsNullOrWhiteSpace(styleText)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new CssParser().ParseInlineStyle(styleText!);

    /// <summary>The text a declaration is stored as: a CSS-wide keyword in its one spelling, the
    /// display values in the engine's own, and everything else through
    /// <see cref="CssValueText.Canonicalize"/> — the same choke point the sheet parser writes
    /// through, so a script that sets a declaration and a page that authors one in a
    /// <c>&lt;style&gt;</c> block read back the same text (measured: both print 'rgb(7, 7, 7)',
    /// '12px / 1.5' and 'margin: 0px auto' however the author typed them).</summary>
    public static string CanonicalSpecifiedValue(string name, string value)
    {
        if (name.StartsWith("--", StringComparison.Ordinal)) return value;
        var text = value.Trim();
        if (Resolver.CssPropertyTraits.IsCssWideKeyword(text)) return text.ToLowerInvariant();
        if (name.Equals("display", StringComparison.OrdinalIgnoreCase)
            && Resolver.CssPropertyApplier.TryCanonicalDisplayText(text, out var canonical))
            return canonical;
        return CssValueText.Canonicalize(text, name.ToLowerInvariant());
    }
}
