using System;
using System.Collections.Generic;
using System.Text;
using Windows.Data.Text;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// Port of the Android WordSegmenter (the offline fallback Folia's
/// Intl.Segmenter provides). Windows ships the same kind of dictionary based
/// word breaker as ICU, exposed as Windows.Data.Text.WordsSegmenter, so the
/// desktop splits CJK lines into real words instead of fixed chunks; the
/// post-processing rules are identical to the Android side.
/// </summary>
public static class SonnetWordSegmenter
{
    public static List<string> Segment(string? text, List<string>? hints = null)
    {
        var fallback = new List<string>();
        if (text == null) return fallback;
        string value = text.Trim();
        if (value.Length == 0) return fallback;

        List<string>? native = SegmentNative(value);
        if (native != null && native.Count > 0)
        {
            return MergeHints(MergeFunctionWords(AttachTrailingPunctuation(native), value), hints);
        }
        return MergeHints(MergeFunctionWords(FallbackChunks(value), value), hints);
    }

    private static List<string>? SegmentNative(string text)
    {
        try
        {
            var segmenter = new WordsSegmenter(LocaleFor(text));
            var pieces = new List<string>();
            foreach (WordSegment segment in segmenter.GetTokens(text))
            {
                pieces.Add(segment.Text);
            }
            return pieces;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Picks the word breaker language from the script, the way the Android
    /// fallback picks its ICU locale.
    /// </summary>
    private static string LocaleFor(string text)
    {
        bool kana = false;
        bool hangul = false;
        bool han = false;
        foreach (char c in text)
        {
            if (c >= 0x3040 && c <= 0x30FF) kana = true;
            else if (c >= 0xAC00 && c <= 0xD7AF) hangul = true;
            else if (c >= 0x4E00 && c <= 0x9FFF) han = true;
        }
        if (kana) return "ja-JP";
        if (hangul) return "ko-KR";
        if (han) return "zh-Hans-CN";
        return "en-US";
    }

    /// <summary>
    /// Mirrors the original rules: a grammatical tail / particle is glued to the
    /// content word it belongs to instead of being shown as its own floating
    /// segment (孤独的, 不知道, 見えない, ように...).
    /// </summary>
    private static List<string> MergeFunctionWords(List<string> pieces, string source)
    {
        var output = new List<string>();
        for (int index = 0; index < pieces.Count; index++)
        {
            string piece = pieces[index];
            if (string.IsNullOrEmpty(piece)) continue;
            string trimmed = piece.Trim();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0) output[^1] += piece;
                continue;
            }
            if (IsPrefixParticle(trimmed) && index + 1 < pieces.Count)
            {
                output.Add(piece + pieces[index + 1]);
                index++;
            }
            else if (IsSuffixParticle(trimmed) && output.Count > 0)
            {
                output[^1] += piece;
            }
            else
            {
                output.Add(piece);
            }
        }
        if (output.Count == 0) output.Add(source);
        return output;
    }

    /// <summary>Particles that attach to the following verb / adjective (不知道, 没见过).</summary>
    private static bool IsPrefixParticle(string text)
        => text.Length == 1 && "不没无别很太最更再又".Contains(text);

    /// <summary>Particles / auxiliaries that attach to the preceding content word.</summary>
    private static bool IsSuffixParticle(string text)
    {
        if (text.Length == 1)
        {
            return "的了着过地得吗呢吧".Contains(text)
                   || "啊呀哦嗯之于而其".Contains(text)
                   || "はがをにとでも".Contains(text)
                   || "のかよねさだたて".Contains(text);
        }
        return text == "です" || text == "ます" || text == "たい" || text == "ない";
    }

    /// <summary>
    /// Song / artist names are protected words: the breaker does not know names
    /// like 黄龄 or HOYO-MiX and would split them, so adjacent pieces are
    /// re-joined when they match a hint.
    /// </summary>
    private static List<string> MergeHints(List<string> pieces, List<string>? hints)
    {
        if (hints == null || hints.Count == 0 || pieces.Count == 0) return pieces;
        var output = new List<string>(pieces);
        foreach (string hintRaw in hints)
        {
            string hint = NormalizeHint(hintRaw);
            if (hint.Length < 2) continue;
            for (int i = 0; i < output.Count; i++)
            {
                var concat = new StringBuilder();
                int end = i;
                while (end < output.Count && concat.Length < hint.Length)
                {
                    concat.Append(NormalizeHint(output[end]));
                    end++;
                }
                if (concat.ToString() == hint && end > i + 1)
                {
                    var merged = new StringBuilder();
                    for (int k = i; k < end; k++) merged.Append(output[k]);
                    for (int k = end - 1; k >= i; k--) output.RemoveAt(k);
                    output.Insert(i, merged.ToString());
                    break;
                }
            }
        }
        return output;
    }

    private static string NormalizeHint(string? text)
    {
        if (text == null) return "";
        var output = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) output.Append(c);
        }
        return output.ToString();
    }

    /// <summary>
    /// The breaker returns words, spaces and punctuation as separate pieces.
    /// Trailing punctuation and the space after it stay glued to the word before
    /// them, so the typography engine never shows a lone comma as a segment.
    /// </summary>
    private static List<string> AttachTrailingPunctuation(List<string> pieces)
    {
        var output = new List<string>();
        var tail = new StringBuilder();
        foreach (string piece in pieces)
        {
            if (HasLetterOrDigit(piece))
            {
                if (tail.Length > 0)
                {
                    if (output.Count > 0) output[^1] += tail.ToString();
                    else output.Add(tail.ToString());
                    tail.Clear();
                }
                output.Add(piece);
            }
            else
            {
                tail.Append(piece);
            }
        }
        if (tail.Length > 0)
        {
            if (output.Count > 0) output[^1] += tail.ToString();
            else output.Add(tail.ToString());
        }
        return output;
    }

    private static bool HasLetterOrDigit(string text)
    {
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) return true;
        }
        return false;
    }

    private static List<string> FallbackChunks(string value)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        int latinRun = 0;
        for (int i = 0; i < value.Length;)
        {
            int codePoint = char.ConvertToUtf32(value, i);
            int charCount = char.IsSurrogatePair(value, i) ? 2 : 1;
            string ch = value.Substring(i, charCount);
            bool punctuation = IsPunctuation(codePoint);
            bool space = char.IsWhiteSpace(ch, 0);
            current.Append(ch);
            i += charCount;
            if (punctuation)
            {
                chunks.Add(current.ToString());
                current.Clear();
                latinRun = 0;
                continue;
            }
            if (space)
            {
                if (latinRun > 0) chunks.Add(current.ToString().Trim());
                current.Clear();
                latinRun = 0;
                continue;
            }
            latinRun += IsCjk(codePoint) ? 0 : 1;
            int visible = CountCodePoints(current);
            int limit = latinRun > 0 ? 11 : 5;
            if (visible >= limit)
            {
                chunks.Add(current.ToString());
                current.Clear();
                latinRun = 0;
            }
        }
        if (current.Length > 0) chunks.Add(current.ToString().Trim());
        return chunks;
    }

    private static int CountCodePoints(StringBuilder builder)
    {
        int count = 0;
        for (int i = 0; i < builder.Length; i++)
        {
            if (!char.IsLowSurrogate(builder[i])) count++;
        }
        return count;
    }

    private static bool IsCjk(int codePoint)
        => (codePoint >= 0x4E00 && codePoint <= 0x9FFF)
           || (codePoint >= 0x3040 && codePoint <= 0x30FF)
           || (codePoint >= 0xAC00 && codePoint <= 0xD7AF);

    private static bool IsPunctuation(int codePoint)
    {
        if (codePoint == ',' || codePoint == '.' || codePoint == '!' || codePoint == '?'
            || codePoint == ';' || codePoint == ':' || codePoint == '-')
        {
            return true;
        }
        return codePoint == 0x3001 || codePoint == 0x3002 || codePoint == 0xFF0C
               || codePoint == 0xFF01 || codePoint == 0xFF1F || codePoint == 0xFF1B
               || codePoint == 0xFF1A || codePoint == 0x2026 || codePoint == 0x2014;
    }
}
