namespace Acrux.Core.Fonts;

/// <summary>
/// Which code points a browser renders with the colour-emoji face by default.
///
/// Unicode splits the emoji set in two: a character can be Emoji=Yes but still carry
/// <c>Emoji_Presentation=No</c>, which means "text style unless the author asks for the
/// emoji style with U+FE0F". A reference browser measured on this machine agrees with the
/// property exactly (probe <c>snapshots/out/_probe_emojipres.html</c>, 16px): the
/// default-presentation group is 40.42px wide for "&#x26A1;tail" while the text group is
/// 33.86px ("&#x2708;tail"), and U+2B05 — an emoji, but not default-presentation — sits
/// with the text group at 36.47px.
///
/// Getting this wrong in either direction is visible: answering with an outline face
/// shrinks ⚡ to a fraction of its reference width, and answering with the colour face for
/// a text-presentation character (or for an astral CJK ideograph such as U+20000, which no
/// emoji font contains) paints a blank.
/// </summary>
public static class EmojiRanges
{
    /// <summary>BMP code points with Emoji_Presentation=Yes (Unicode 15.1).</summary>
    private static readonly (int Start, int End)[] DefaultPresentationBmp =
    {
        (0x231A, 0x231B), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3),
        (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F),
        (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE),
        (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA),
        (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD),
        (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C),
        (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B06, 0x2B07), (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50), (0x2B55, 0x2B55),
    };

    /// <summary>Supplementary-plane pictographs. Every emoji above the BMP lives in one of
    /// these bands, and no other script does — CJK extension B starts at U+20000, so the
    /// bands stop before it rather than treating "astral" as a synonym for "emoji".</summary>
    private static readonly (int Start, int End)[] DefaultPresentationAstral =
    {
        (0x1F1E6, 0x1F1FF), // regional indicators (flag letters)
        (0x1F300, 0x1F320), (0x1F32D, 0x1F335), (0x1F337, 0x1F37C), (0x1F37E, 0x1F393),
        (0x1F3A0, 0x1F3CA), (0x1F3CF, 0x1F3D3), (0x1F3E0, 0x1F3F0), (0x1F3F4, 0x1F3F4),
        (0x1F3F8, 0x1F43E), (0x1F440, 0x1F440), (0x1F442, 0x1F4FC), (0x1F4FF, 0x1F53D),
        (0x1F54B, 0x1F54E), (0x1F550, 0x1F567), (0x1F57A, 0x1F57A), (0x1F595, 0x1F596),
        (0x1F5A4, 0x1F5A4), (0x1F5FB, 0x1F64F), (0x1F680, 0x1F6C5), (0x1F6CC, 0x1F6CC),
        (0x1F6D0, 0x1F6D2), (0x1F6D5, 0x1F6D7), (0x1F6DC, 0x1F6DF), (0x1F6EB, 0x1F6EC),
        (0x1F6F4, 0x1F6FC), (0x1F7E0, 0x1F7EB), (0x1F7F0, 0x1F7F0), (0x1F90C, 0x1F93A),
        (0x1F93C, 0x1F945), (0x1F947, 0x1F9FF), (0x1FA70, 0x1FA7C), (0x1FA80, 0x1FA88),
        (0x1FA90, 0x1FABD), (0x1FABF, 0x1FAC5), (0x1FACE, 0x1FADB), (0x1FAE0, 0x1FAE8),
        (0x1FAF0, 0x1FAF8),
    };

    /// <summary>True when the character should be drawn from the colour-emoji face without
    /// the author having to ask for it with U+FE0F.</summary>
    public static bool IsDefaultEmojiPresentation(int codePoint)
    {
        var table = codePoint >= 0x10000 ? DefaultPresentationAstral : DefaultPresentationBmp;
        foreach (var band in table)
            if (codePoint >= band.Start && codePoint <= band.End)
                return true;
        return false;
    }
}
