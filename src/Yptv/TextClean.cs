using System.Text;

namespace Yptv;

// Display text as the provider wrote it, minus emoji/pictographs, with whitespace collapsed.
static class TextClean
{
    public static string Clean(string text)
    {
        if (IsClean(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsPictograph(rune.Value)) continue;
            if (Rune.IsWhiteSpace(rune))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }
            else sb.Append(rune.ToString());
        }
        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }

    // Fast path: nothing to strip, single spaces only, no edge spaces.
    static bool IsClean(string text)
    {
        if (text.Length == 0) return true;
        if (text[0] == ' ' || text[^1] == ' ') return false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c >= 0x2000 || char.IsSurrogate(c)) return false;
            if (char.IsWhiteSpace(c) && (c != ' ' || text[i + 1] == ' ')) return false;
        }
        return true;
    }

    static bool IsPictograph(int c) =>
        c is 0x200D or 0xFE0F or 0x20E3                  // joiner, variation selector, keycap
        || c is >= 0x2190 and <= 0x21FF                  // arrows
        || c is >= 0x2300 and <= 0x23FF                  // technical (watch, hourglass)
        || c is >= 0x25A0 and <= 0x27BF                  // shapes, misc symbols, dingbats (sparkle U+2728)
        || c is >= 0x2900 and <= 0x297F || c is >= 0x2B00 and <= 0x2BFF
        || c is >= 0x1F000 and <= 0x1FAFF;               // emoji planes
}
