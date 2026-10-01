using System;
using System.Collections.Generic;
using System.Text;

namespace BukaMusicDesktop.Controls;

/// <summary>One timed lyric line, as fed into the director.</summary>
public sealed class SonnetLine
{
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public string Text { get; init; } = "";
    public string Translation { get; init; } = "";
    /// <summary>Real per-word timings when the source provides them (YRC / enhanced LRC).</summary>
    public List<SonnetWord> Words { get; init; } = new();

    public bool HasWordTiming => Words.Count > 0;
    public string TranslationOrNull => string.IsNullOrWhiteSpace(Translation) ? "" : Translation.Trim();
}

/// <summary>One word with its own start / end time inside the line.</summary>
public sealed class SonnetWord
{
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>
/// Port of Folia's Sonnet timeline compiler, matching the Android
/// SonnetDirector. Lyrics are split into semantic segments, grouped into
/// paragraphs the way the original does (median gap threshold, line caps,
/// duration caps), then into shots of at most four lines / six seconds. Every
/// shot gets a deterministic kind, a camera position on the scene plane and a
/// transition kind, using the same mixing rules as the original director.
/// </summary>
public sealed class SonnetDirector
{
    public sealed class Grapheme
    {
        public string Text { get; }
        public long StartMs { get; }
        public long EndMs { get; }

        internal Grapheme(string text, long startMs, long endMs)
        {
            Text = text;
            StartMs = startMs;
            EndMs = Math.Max(endMs, startMs + 40);
        }
    }

    public sealed class Segment
    {
        public string Text { get; }
        public long StartMs { get; }
        public long EndMs { get; }
        public List<Grapheme> Graphemes { get; }
        public bool WordLike { get; }

        internal Segment(string text, long startMs, long endMs, List<Grapheme> graphemes)
        {
            Text = text ?? "";
            StartMs = startMs;
            EndMs = Math.Max(endMs, startMs + 80);
            Graphemes = graphemes;
            WordLike = !string.IsNullOrWhiteSpace(Text);
        }
    }

    public sealed class Shot
    {
        public int Index;
        public SonnetKind Kind { get; }
        public long StartMs { get; }
        public long EndMs { get; }
        public List<Segment> Segments { get; }
        public float CameraX { get; }
        public float CameraY { get; }
        public float CameraZoom { get; }
        public float CameraRotation { get; }
        public int ParagraphIndex { get; }
        /// <summary>True for the virtual three-dot shot generated inside a long break.</summary>
        public bool Interlude { get; }
        /// <summary>Break markers follow the orientation of the shot before the gap.</summary>
        public bool InterludeVertical { get; }

        internal Shot(int index, SonnetKind kind, long startMs, long endMs,
                      List<Segment> segments, float cameraX, float cameraY,
                      float cameraZoom, float cameraRotation, int paragraphIndex,
                      bool interlude, bool interludeVertical)
        {
            Index = index;
            Kind = kind;
            StartMs = startMs;
            EndMs = endMs;
            Segments = segments;
            CameraX = cameraX;
            CameraY = cameraY;
            CameraZoom = cameraZoom;
            CameraRotation = cameraRotation;
            ParagraphIndex = paragraphIndex;
            Interlude = interlude;
            InterludeVertical = interludeVertical;
        }

        public long DurationMs => Math.Max(1, EndMs - StartMs);
    }

    public sealed class Paragraph
    {
        public int Index { get; }
        public string Kind { get; }
        public long StartMs { get; }
        public long EndMs { get; }
        public List<Shot> Shots { get; }
        public SonnetTransitionKind TransitionOut { get; }
        public long TransitionStartMs { get; }
        public long TransitionEndMs { get; }

        internal Paragraph(int index, string kind, long startMs, long endMs,
                           List<Shot> shots, SonnetTransitionKind transitionOut,
                           long transitionStartMs, long transitionEndMs)
        {
            Index = index;
            Kind = kind;
            StartMs = startMs;
            EndMs = endMs;
            Shots = shots;
            TransitionOut = transitionOut;
            TransitionStartMs = transitionStartMs;
            TransitionEndMs = transitionEndMs;
        }
    }

    private sealed class LineDraft
    {
        public SonnetLine Line { get; }
        public List<Segment> Segments { get; }

        public LineDraft(SonnetLine line, List<Segment> segments)
        {
            Line = line;
            Segments = segments;
        }
    }

    public const long InterludeMinMs = 6_000L;
    /// <summary>Lyrics appear this much before the detected onset, to match the voice.</summary>
    private const long LyricLeadMs = 200L;
    /// <summary>Visual lead for word timings that come from a karaoke source.</summary>
    private const long RealWordLeadMs = 120L;

    public List<Paragraph> Paragraphs { get; }
    public List<Shot> Shots { get; }
    public float ParagraphGapThresholdMs { get; }

    private SonnetDirector(List<Paragraph> paragraphs, List<Shot> shots, float paragraphGapThresholdMs)
    {
        Paragraphs = paragraphs;
        Shots = shots;
        ParagraphGapThresholdMs = paragraphGapThresholdMs;
    }

    public bool IsEmpty => Shots.Count == 0;

    public static readonly SonnetDirector Empty =
        new(new List<Paragraph>(), new List<Shot>(), 0f);

    public int ShotIndexAt(long timeMs)
    {
        if (Shots.Count == 0 || timeMs < Shots[0].StartMs) return -1;
        int lo = 0;
        int hi = Shots.Count - 1;
        int result = 0;
        while (lo <= hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            if (Shots[mid].StartMs <= timeMs)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Compilation
    // ------------------------------------------------------------------

    public static SonnetDirector Compile(IReadOnlyList<SonnetLine>? lines, string seedText,
                                         List<string>? hints = null, List<long>? onsetsMs = null)
    {
        if (lines == null || lines.Count == 0)
        {
            return new SonnetDirector(new List<Paragraph>(), new List<Shot>(), 0f);
        }

        var compiled = new List<LineDraft>();
        float wordPaceMs = EstimatePace(lines);
        for (int i = 0; i < lines.Count; i++)
        {
            SonnetLine line = lines[i];
            long lineEnd = line.EndMs;
            if (i + 1 < lines.Count)
            {
                lineEnd = Math.Min(lineEnd, lines[i + 1].StartMs);
            }
            List<Segment> segments = SplitSegments(line.Text, line.StartMs,
                    Math.Max(lineEnd, line.StartMs + 250L), hints, wordPaceMs, onsetsMs);
            if (line.HasWordTiming)
            {
                // Karaoke sources already know when every word is sung - use that
                // instead of estimating.
                List<Segment> exact = SegmentsFromWords(line);
                if (exact.Count > 0) segments = exact;
            }
            compiled.Add(new LineDraft(line, segments));
        }

        float gapThreshold = ResolveParagraphGapThreshold(compiled);
        var drafts = new List<List<LineDraft>>();
        var current = new List<LineDraft>();
        for (int i = 0; i < compiled.Count; i++)
        {
            LineDraft draft = compiled[i];
            LineDraft? previous = i > 0 ? compiled[i - 1] : null;
            bool boundary = false;
            if (previous != null)
            {
                long gap = draft.Line.StartMs - previous.Line.EndMs;
                if (gap >= gapThreshold) boundary = true;
            }
            if (boundary && current.Count > 0)
            {
                drafts.AddRange(SplitOversized(current));
                current = new List<LineDraft>();
            }
            current.Add(draft);
        }
        if (current.Count > 0) drafts.AddRange(SplitOversized(current));

        string seed = string.IsNullOrEmpty(seedText) ? "sonnet" : seedText;
        var paragraphs = new List<Paragraph>();
        var allShots = new List<Shot>();
        SonnetKind? previousShot = null;
        SonnetTransitionKind? previousTransition = null;
        for (int p = 0; p < drafts.Count; p++)
        {
            List<LineDraft> draft = drafts[p];
            string paragraphKind = ClassifyParagraph(draft, p, drafts.Count);
            List<List<LineDraft>> groups = GroupShotLines(draft);
            var shots = new List<Shot>();
            for (int s = 0; s < groups.Count; s++)
            {
                List<LineDraft> group = groups[s];
                int wordCount = 0;
                foreach (LineDraft line in group)
                {
                    foreach (Segment segment in line.Segments)
                    {
                        if (segment.WordLike) wordCount++;
                    }
                }
                SonnetKind kind = ChooseShotKind(seed, p, s, group, paragraphKind, previousShot, wordCount);
                int hash = HashSeed(seed + ":" + p + ":" + s + ":camera");
                float zoomBase = kind == SonnetKind.PosterBlocks ? 1.02f
                        : kind == SonnetKind.QuietTableau ? 1.12f : 1.22f;
                float zoomSpan = kind == SonnetKind.PosterBlocks ? 0.16f
                        : kind == SonnetKind.QuietTableau ? 0.20f : 0.26f;
                long start = group[0].Line.StartMs;
                long end = group[group.Count - 1].Line.EndMs;
                var segments = new List<Segment>();
                foreach (LineDraft line in group) segments.AddRange(line.Segments);
                List<List<Segment>> chunks = ChunkSegments(segments);
                for (int c = 0; c < chunks.Count; c++)
                {
                    List<Segment> chunk = chunks[c];
                    long chunkStart = c == 0 ? start : chunk[0].StartMs;
                    long chunkEnd = c + 1 < chunks.Count ? chunks[c + 1][0].StartMs : end;
                    int chunkHash = HashSeed(seed + ":" + p + ":" + s + ":camera:" + c);
                    float[] chunkCamera =
                    {
                        (Byte(chunkHash, 0) / 255f - 0.5f) * 0.10f,
                        (Byte(chunkHash, 1) / 255f - 0.5f) * 0.08f,
                        zoomBase + Byte(chunkHash, 2) / 255f * zoomSpan,
                        (Byte(chunkHash, 3) / 255f - 0.5f) * 0.08f,
                    };
                    var shot = new Shot(allShots.Count, kind, chunkStart, chunkEnd, chunk,
                            chunkCamera[0], chunkCamera[1], chunkCamera[2], chunkCamera[3],
                            p, false, false);
                    shots.Add(shot);
                    allShots.Add(shot);
                    previousShot = kind;
                }
            }
            long paragraphEnd = draft[^1].Line.EndMs;
            SonnetTransitionKind transition = default;
            long transStart = 0;
            long transEnd = 0;
            if (p + 1 < drafts.Count)
            {
                transition = ChooseTransition(seed + ":" + p + ":transition", previousTransition);
                previousTransition = transition;
                long nextStart = drafts[p + 1][0].Line.StartMs;
                long gap = Math.Max(0, nextStart - paragraphEnd);
                long duration = (long)(Math.Min(0.3, Math.Max(0.16, gap > 0
                        ? gap / 1000.0 * 0.5 : 0.2)) * 1000.0);
                transStart = Math.Max(draft[0].Line.StartMs, nextStart - duration);
                transEnd = nextStart;
            }
            paragraphs.Add(new Paragraph(p, paragraphKind, draft[0].Line.StartMs,
                    paragraphEnd, shots, transition, transStart, transEnd));
        }

        var withInterludes = new List<Shot>(allShots.Count);
        for (int i = 0; i < allShots.Count; i++)
        {
            Shot shot = allShots[i];
            withInterludes.Add(shot);
            if (i + 1 >= allShots.Count) continue;
            Shot next = allShots[i + 1];
            long gap = next.StartMs - shot.EndMs;
            if (gap >= InterludeMinMs)
            {
                withInterludes.Add(BuildInterludeShot(shot, next, gap, seed));
            }
        }
        for (int i = 0; i < withInterludes.Count; i++)
        {
            withInterludes[i].Index = i;
        }
        return new SonnetDirector(paragraphs, withInterludes, gapThreshold);
    }

    private static int Byte(int hash, int index) => (int)(((uint)hash >> (index * 8)) & 255);

    /// <summary>
    /// Long lines are split into several shots of at most seven words, so the
    /// typography engine never has to shrink the whole composition to a size
    /// where the lyric becomes unreadable.
    /// </summary>
    private static List<List<Segment>> ChunkSegments(List<Segment> segments)
    {
        var chunks = new List<List<Segment>>();
        const int limit = 7;
        for (int i = 0; i < segments.Count; i += limit)
        {
            var chunk = new List<Segment>();
            for (int k = i; k < Math.Min(segments.Count, i + limit); k++) chunk.Add(segments[k]);
            chunks.Add(chunk);
        }
        if (chunks.Count == 0) chunks.Add(new List<Segment>());
        return chunks;
    }

    /// <summary>
    /// A long break is rendered by the same typography pipeline as the lyrics:
    /// a virtual shot whose segments are three dots, each timed to one third of
    /// the break.
    /// </summary>
    private static Shot BuildInterludeShot(Shot previous, Shot next, long gap, string seed)
    {
        int hash = HashSeed(seed + ":interlude:" + previous.Index);
        float zoom = 1.08f + Byte(hash, 2) / 255f * 0.12f;
        float cameraX = (Byte(hash, 0) / 255f - 0.5f) * 0.06f;
        float cameraY = (Byte(hash, 1) / 255f - 0.5f) * 0.05f;
        bool vertical = previous.Kind == SonnetKind.QuietTableau
                || previous.Kind == SonnetKind.MaskReveal
                || previous.Kind == SonnetKind.EditorialColumn;
        var segments = new List<Segment>(3);
        long gapStart = previous.EndMs;
        for (int i = 0; i < 3; i++)
        {
            long start = gapStart + gap * i / 3;
            long end = Math.Min(next.StartMs, gapStart + gap * (i + 1) / 3);
            segments.Add(BuildSegment("\u00B7", start, Math.Max(start + 120L, end)));
        }
        return new Shot(previous.Index + 1, SonnetKind.QuietTableau,
                gapStart, next.StartMs, segments,
                cameraX, cameraY, zoom, 0f, previous.ParagraphIndex, true, vertical);
    }

    // ------------------------------------------------------------------
    // Line / segment parsing
    // ------------------------------------------------------------------

    private static List<Segment> SplitSegments(string? text, long startMs, long endMs,
                                               List<string>? hints, float wordPaceMs,
                                               List<long>? onsetsMs)
    {
        var output = new List<Segment>();
        if (text == null) return output;
        string value = text.Trim();
        if (value.Length == 0) return output;

        // Same offline fallback the original uses: the platform word segmenter
        // splits CJK into real words.
        List<string> chunks = SonnetWordSegmenter.Segment(value, hints);
        if (chunks.Count == 0) chunks.Add(value);

        var words = new List<string>(chunks.Count);
        var weights = new List<int>(chunks.Count);
        foreach (string chunk in chunks)
        {
            string segmentText = (chunk ?? "").Trim();
            if (segmentText.Length == 0) continue;
            words.Add(segmentText);
            weights.Add(Math.Max(1, VisibleWeight(segmentText)));
        }
        if (words.Count == 0) return output;

        long duration = Math.Max(250L, endMs - startMs);
        float estimatedTotal = 0f;
        foreach (int weight in weights) estimatedTotal += weight * wordPaceMs;
        float fit = estimatedTotal > duration * 0.92f
                ? (duration * 0.92f) / Math.Max(1f, estimatedTotal) : 1f;

        var starts = new List<long>(words.Count);
        long cursor = startMs;
        for (int i = 0; i < words.Count; i++)
        {
            starts.Add(cursor);
            cursor += Math.Max(90L, (long)Math.Round(weights[i] * wordPaceMs * fit));
        }
        if (onsetsMs != null && onsetsMs.Count > 0)
        {
            starts = SnapToOnsets(starts, startMs, endMs, onsetsMs);
        }
        for (int i = 0; i < words.Count; i++)
        {
            long wordStart = Math.Max(0L, starts[i] - LyricLeadMs);
            long wordEnd = i + 1 < words.Count
                    ? Math.Max(wordStart + 60L, starts[i + 1] - LyricLeadMs)
                    : Math.Min(endMs, wordStart + Math.Max(120L,
                        (long)Math.Round(weights[i] * wordPaceMs * fit)));
            if (wordEnd <= wordStart) wordEnd = wordStart + 60L;
            output.Add(BuildSegment(words[i], wordStart, wordEnd));
        }
        return output;
    }

    /// <summary>
    /// Snaps estimated word starts to nearby vocal onsets while keeping the
    /// order strictly monotonic (a word can never start before the previous one).
    /// </summary>
    private static List<long> SnapToOnsets(List<long> estimated, long lineStart,
                                           long lineEnd, List<long> onsets)
    {
        var output = new List<long>(estimated.Count);
        long previous = lineStart - 250L;
        int searchFrom = 0;
        foreach (long guess in estimated)
        {
            long best = -1L;
            long bestDelta = long.MaxValue;
            for (int i = searchFrom; i < onsets.Count; i++)
            {
                long onset = onsets[i];
                if (onset < previous + 110L) continue;
                if (onset > lineEnd + 300L) break;
                long delta = Math.Abs(onset - guess);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = onset;
                }
                if (onset > guess + 650L) break;
            }
            if (best >= 0L && bestDelta <= 600L)
            {
                output.Add(best);
                previous = best;
                while (searchFrom < onsets.Count && onsets[searchFrom] <= best) searchFrom++;
            }
            else
            {
                long fallback = Math.Max(guess, previous + 120L);
                output.Add(fallback);
                previous = fallback;
            }
        }
        return output;
    }

    /// <summary>Median milliseconds per syllable across the whole song.</summary>
    private static float EstimatePace(IReadOnlyList<SonnetLine> lines)
    {
        var paces = new List<float>();
        foreach (SonnetLine line in lines)
        {
            long duration = line.EndMs - line.StartMs;
            int weight = VisibleWeight(line.Text);
            if (weight < 2 || duration < 500L || duration > 15_000L) continue;
            paces.Add(duration / (float)weight);
        }
        if (paces.Count == 0) return 220f;
        paces.Sort();
        return Math.Max(120f, Math.Min(420f, paces[paces.Count / 2]));
    }

    private static int VisibleWeight(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        int count = 0;
        bool previousVowel = false;
        foreach (char c in text)
        {
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF))
            {
                count++;
                previousVowel = false;
            }
            else if (c >= 0x3040 && c <= 0x30FF)
            {
                // Small kana fold into the preceding mora; everything else is one.
                if ("\u3041\u3043\u3045\u3047\u3049\u3083\u3085\u3087\u308E\u30A1\u30A3\u30A5\u30A7\u30A9\u30E3\u30E5\u30E7\u30EE\u30F5\u30F6\u30C3"
                        .IndexOf(c) < 0)
                {
                    count++;
                }
                previousVowel = false;
            }
            else if (char.IsLetter(c))
            {
                bool vowel = "aeiouyAEIOUY".IndexOf(c) >= 0;
                if (vowel && !previousVowel) count++;
                previousVowel = vowel;
            }
            else if (char.IsDigit(c))
            {
                count++;
                previousVowel = false;
            }
        }
        return Math.Max(1, count == 0 ? Math.Max(1, text.Length / 3) : count);
    }

    /// <summary>
    /// Builds the word segments straight from a karaoke source. The provider
    /// timings are already aligned with the singing, so only a small visual lead
    /// is applied instead of the estimation lead used elsewhere.
    /// </summary>
    private static List<Segment> SegmentsFromWords(SonnetLine line)
    {
        var output = new List<Segment>();
        if (line.Words.Count == 0) return output;
        for (int i = 0; i < line.Words.Count; i++)
        {
            SonnetWord word = line.Words[i];
            string text = (word.Text ?? "").Trim();
            if (text.Length == 0) continue;
            long start = Math.Max(0L, word.StartMs - RealWordLeadMs);
            long end = Math.Max(start + 60L, word.EndMs - RealWordLeadMs);
            if (i + 1 < line.Words.Count)
            {
                long next = Math.Max(start + 60L, line.Words[i + 1].StartMs - RealWordLeadMs);
                if (next > end) end = next;
            }
            output.Add(BuildSegment(text, start, end));
        }
        return output;
    }

    private static Segment BuildSegment(string text, long startMs, long endMs)
    {
        var codePoints = new List<string>();
        for (int i = 0; i < text.Length;)
        {
            int codePoint = char.ConvertToUtf32(text, i);
            int count = char.IsSurrogatePair(text, i) ? 2 : 1;
            codePoints.Add(char.ConvertFromUtf32(codePoint));
            i += count;
        }
        int total = Math.Max(1, codePoints.Count);
        long duration = Math.Max(60, endMs - startMs);
        var graphemes = new List<Grapheme>(total);
        for (int i = 0; i < total; i++)
        {
            long gStart = startMs + duration * i / total;
            long gEnd = startMs + duration * (i + 1) / total;
            graphemes.Add(new Grapheme(codePoints[i], gStart, gEnd));
        }
        return new Segment(text, startMs, endMs, graphemes);
    }

    // ------------------------------------------------------------------
    // Paragraph / shot grouping (mirrors sonnetProgram.ts)
    // ------------------------------------------------------------------

    private static float ResolveParagraphGapThreshold(List<LineDraft> lines)
    {
        var gaps = new List<long>();
        for (int i = 1; i < lines.Count; i++)
        {
            long gap = lines[i].Line.StartMs - lines[i - 1].Line.EndMs;
            if (gap > 0) gaps.Add(gap);
        }
        if (gaps.Count == 0) return 2500f;
        gaps.Sort();
        int mid = gaps.Count / 2;
        float median = gaps.Count % 2 == 0 ? (gaps[mid - 1] + gaps[mid]) / 2f : gaps[mid];
        return Math.Max(1250f, Math.Min(3500f, median * 2.5f));
    }

    private static List<List<LineDraft>> SplitOversized(List<LineDraft> draft)
    {
        var output = new List<List<LineDraft>>();
        var remaining = new List<LineDraft>(draft);
        int guard = 0;
        while (remaining.Count > 6
               || (remaining.Count > 1
                   && remaining[^1].Line.EndMs - remaining[0].Line.StartMs > 18_000L))
        {
            if (guard++ > 1000) break;
            int bestSplit = -1;
            long bestGap = -1;
            for (int i = 2; i <= remaining.Count - 2; i++)
            {
                long gap = remaining[i].Line.StartMs - remaining[i - 1].Line.EndMs;
                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestSplit = i;
                }
            }
            if (bestSplit < 1) bestSplit = Math.Min(4, remaining.Count - 1);
            var head = new List<LineDraft>();
            for (int i = 0; i < bestSplit; i++) head.Add(remaining[i]);
            output.Add(head);
            var tail = new List<LineDraft>();
            for (int i = bestSplit; i < remaining.Count; i++) tail.Add(remaining[i]);
            remaining = tail;
        }
        output.Add(remaining);
        return output;
    }

    private static List<List<LineDraft>> GroupShotLines(List<LineDraft> lines)
    {
        var groups = new List<List<LineDraft>>();
        var current = new List<LineDraft>();
        long groupStart = 0;
        foreach (LineDraft line in lines)
        {
            if (current.Count == 0)
            {
                current.Add(line);
                groupStart = line.Line.StartMs;
            }
            else if (current.Count < 4 && line.Line.EndMs - groupStart <= 6_000L)
            {
                current.Add(line);
            }
            else
            {
                groups.Add(current);
                current = new List<LineDraft> { line };
                groupStart = line.Line.StartMs;
            }
        }
        if (current.Count > 0) groups.Add(current);
        return groups;
    }

    private static string ClassifyParagraph(List<LineDraft> lines, int index, int total)
    {
        var full = new StringBuilder();
        foreach (LineDraft line in lines) full.Append(line.Line.Text).Append(' ');
        string text = full.ToString();
        string lower = text.ToLowerInvariant();
        if (lower.Contains("chorus") || text.Contains("副歌")) return "chorus";
        if (lower.Contains("interlude") || lower.Contains("bridge")
            || text.Contains("间奏") || text.Contains("間奏")) return "break";
        if (index == total - 1) return "outro";
        long duration = lines[^1].Line.EndMs - lines[0].Line.StartMs;
        int segmentCount = 0;
        int punctuation = 0;
        foreach (LineDraft line in lines)
        {
            foreach (Segment segment in line.Segments)
            {
                if (segment.WordLike) segmentCount++;
            }
            foreach (char c in line.Line.Text)
            {
                if (c == '!' || c == '?' || c == '\uFF01' || c == '\uFF1F' || c == '\u2026')
                {
                    punctuation++;
                }
            }
        }
        if (duration <= 3_500L || segmentCount <= 3) return "breath";
        if (punctuation >= 2 || segmentCount / Math.Max(1.0, duration / 1000.0) > 2.5) return "lift";
        return "verse";
    }

    private static SonnetKind ChooseShotKind(string seed, int paragraphIndex, int shotIndex,
                                             List<LineDraft> group, string paragraphKind,
                                             SonnetKind? previous, int wordCount)
    {
        var signature = new StringBuilder();
        foreach (LineDraft line in group) signature.Append(line.Line.Text).Append('|');
        SonnetKind[] kinds = SonnetMotion.AllKinds;
        int start = FloorMod(HashSeed(seed + ":" + paragraphIndex + ":" + shotIndex + ":" + signature),
                kinds.Length);
        SonnetKind chosen = kinds[start];
        for (int offset = 0; offset < kinds.Length; offset++)
        {
            SonnetKind candidate = kinds[(start + offset) % kinds.Length];
            if (previous == null || candidate != previous.Value)
            {
                chosen = candidate;
                break;
            }
        }
        if (paragraphKind == "breath" && shotIndex == 0 && wordCount <= 2)
        {
            chosen = SonnetKind.QuietTableau;
        }
        if (paragraphKind == "chorus" && chosen == SonnetKind.QuietTableau)
        {
            chosen = SonnetKind.TypeImpact;
        }
        return chosen;
    }

    private static SonnetTransitionKind ChooseTransition(string seed, SonnetTransitionKind? previous)
    {
        SonnetTransitionKind[] kinds = SonnetMotion.AllTransitions;
        int start = FloorMod(HashSeed(seed), kinds.Length);
        SonnetTransitionKind chosen = kinds[start];
        for (int offset = 0; offset < kinds.Length; offset++)
        {
            SonnetTransitionKind candidate = kinds[(start + offset) % kinds.Length];
            if (previous == null || candidate != previous.Value)
            {
                chosen = candidate;
                break;
            }
        }
        return chosen;
    }

    public static int FloorMod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    /// <summary>FNV-1a over UTF-16 units, matching the deterministic spirit of sonnetRandom.</summary>
    public static int HashSeed(string? value)
    {
        unchecked
        {
            int hash = (int)0x811C9DC5;
            if (value == null) return hash;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 0x01000193;
            }
            return hash;
        }
    }
}
