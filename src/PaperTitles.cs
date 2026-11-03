using System.Globalization;

namespace PaperTodo;

public static class PaperTitles
{
    // Hard storage / edit cap in Unicode text elements (≈ full-width characters for CJK).
    // Titles are never stored longer than this regardless of the user setting.
    public const int MaxTitleLength = 20;

    // User-configurable display/edit cap (Settings → 标题最大长度), within MaxTitleLength.
    // Interpreted in full-width equivalents: a half-width character counts as 0.5, so
    // mixed CJK/Latin titles share one consistent visual budget.
    public const int DefaultMaxTitleLength = 8;
    public const int MinConfigurableTitleLength = 2;
    public const int MaxConfigurableTitleLength = MaxTitleLength;

    public static int NormalizeMaxTitleLength(int value)
    {
        if (value <= 0)
        {
            return DefaultMaxTitleLength;
        }

        return Math.Clamp(value, MinConfigurableTitleLength, MaxConfigurableTitleLength);
    }

    public static string DefaultTitle(string paperType, int number)
    {
        var prefix = DefaultTitlePrefix(paperType);
        return prefix + Math.Max(1, number).ToString(CultureInfo.InvariantCulture);
    }

    public static string DefaultTitlePrefix(string paperType)
    {
        return paperType == PaperTypes.Note
            ? Strings.Get("PaperKindNote")
            : Strings.Get("PaperKindTodo");
    }

    public static string CleanCustomTitle(string? title)
    {
        return CleanCustomTitle(title, MaxTitleLength);
    }

    public static string CleanCustomTitle(string? title, int maxLength)
    {
        var cleaned = NormalizeCustomTitle(title);
        return TakeTextElements(cleaned, Math.Clamp(maxLength, 1, MaxTitleLength));
    }

    internal static bool ExceedsTextElementLimit(string? title, int maxLength)
    {
        var cleaned = NormalizeCustomTitle(title);
        var limit = Math.Clamp(maxLength, 1, MaxTitleLength);
        return FullWidthEquivalents(cleaned) > limit;
    }

    public static string EffectiveTitle(PaperData paper, int fallbackNumber)
    {
        var title = CleanCustomTitle(paper.Title);
        return string.IsNullOrWhiteSpace(title)
            ? DefaultTitle(paper.Type, fallbackNumber)
            : title;
    }

    public static string CapsuleText(PaperData paper, int fallbackNumber)
    {
        return EffectiveTitle(paper, fallbackNumber);
    }

    private static string NormalizeCustomTitle(string? title)
    {
        var cleaned = (title ?? "").Trim();
        return string.Join("", cleaned.Where(ch => !char.IsControl(ch)));
    }

    private static string TakeTextElements(string text, int maxLength)
    {
        var indexes = StringInfo.ParseCombiningCharacters(text);
        if (FullWidthEquivalentsByIndexes(text, indexes) <= maxLength)
        {
            return text;
        }

        // Walk text elements until the full-width budget is exhausted.
        var budget = (double)maxLength;
        for (var i = 0; i < indexes.Length; i++)
        {
            var element = text.Substring(
                indexes[i],
                (i + 1 < indexes.Length ? indexes[i + 1] : text.Length) - indexes[i]);
            budget -= ElementWidth(element);
            if (budget < 0)
            {
                return text[..indexes[i]];
            }
        }

        return text;
    }

    private static double FullWidthEquivalents(string text)
    {
        var indexes = StringInfo.ParseCombiningCharacters(text);
        return FullWidthEquivalentsByIndexes(text, indexes);
    }

    private static double FullWidthEquivalentsByIndexes(string text, int[] indexes)
    {
        double total = 0;
        for (var i = 0; i < indexes.Length; i++)
        {
            var element = text.Substring(
                indexes[i],
                (i + 1 < indexes.Length ? indexes[i + 1] : text.Length) - indexes[i]);
            total += ElementWidth(element);
        }

        return total;
    }

    // Full-width characters count 1; anything narrow (Latin letters, digits, half-width
    // punctuation and katakana) counts 0.5 so CJK and mixed titles share one visual budget.
    private static double ElementWidth(string textElement)
    {
        return EastAsianWidthCategory.IsFullWidth(textElement) ? 1.0 : 0.5;
    }
}

internal static class EastAsianWidthCategory
{
    public static bool IsFullWidth(string textElement)
    {
        foreach (var ch in textElement)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.Surrogate)
            {
                continue;
            }

            // Any base character that is CJK ideographic, full-width form, or wide
            // punctuation/symbol makes the element full width.
            if (IsWideCodePoint(ch))
            {
                return true;
            }

            if (category is not (UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.EnclosingMark or
                    UnicodeCategory.SpacingCombiningMark))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsWideCodePoint(char ch)
    {
        // Half-width katakana (FF61-FF9F) must be tested before the wide-forms block,
        // otherwise that block's upper edge would swallow it.
        if (ch >= '｡' && ch <= 'ﾟ') return false;
        // CJK Unified Ideographs and extensions.
        if (ch >= '一' && ch <= '鿿') return true;
        if (ch >= '㐀' && ch <= '䶿') return true;
        // CJK Compatibility Ideographs (F900-FAFF) render full width.
        if (ch >= '豈' && ch <= '﫿') return true;
        // Hiragana / Katakana (full-width Japanese).
        if (ch >= '぀' && ch <= 'ヿ') return true;
        if (ch >= 'ㇰ' && ch <= 'ㇿ') return true;
        // Hangul syllables, jamo and compatibility.
        if (ch >= '가' && ch <= '힯') return true;
        if (ch >= 'ᄀ' && ch <= 'ᇿ') return true;
        if (ch >= '㄰' && ch <= '㆏') return true;
        // Full-width forms: FF01-FF60 are the full-width ASCII/punctuation variants.
        if (ch >= '！' && ch <= '｠') return true;
        if (ch >= '￠' && ch <= '￦') return true;
        // CJK punctuation and symbols (full-width in practice).
        if (ch >= '　' && ch <= '〿') return true;
        return false;
    }
}
