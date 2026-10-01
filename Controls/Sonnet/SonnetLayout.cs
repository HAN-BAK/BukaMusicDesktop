using System;
using System.Collections.Generic;

namespace BukaMusicDesktop.Controls;

/// <summary>Text measurement the layout engine needs (Android Paint equivalent).</summary>
public interface ISonnetTextMetrics
{
    float MeasureText(string text, float fontSize);
    /// <summary>Font ascent at this size (negative, like Android's FontMetrics).</summary>
    float Ascent(float fontSize);
    /// <summary>Font descent at this size (positive).</summary>
    float Descent(float fontSize);
}

/// <summary>
/// Kinematic typography layout for one Sonnet shot (port of the Android
/// SonnetLayout).
///
/// <p>Segments are assigned hero / semi-hero / support roles, sized with the
/// original 3.0-5.5x base-font multipliers, then placed by one of the seven
/// composition families. All coordinates are relative to the shot origin; the
/// renderer moves the whole shot around the scene plane with the camera.
/// </summary>
public static class SonnetLayout
{
    private const float SafeHalfW = 0.46f;
    private const float SafeHalfH = 0.44f;

    /// <summary>Scale the last layout had to apply to make everything fit.</summary>
    public static float LastContentScale = 1f;

    public sealed class Placement
    {
        public int SegmentIndex;
        public string Text = "";
        public SonnetRole Role;
        public float X;
        public float Y;
        public float FontSize;
        public float Rotation;
        public float EnterX;
        public float EnterY;
        public bool Vertical;

        public float MeasuredWidth;
        public float MeasuredHeight;
    }

    public static List<Placement> Layout(SonnetDirector.Shot shot, float width, float height,
                                         float baseFontSize, ISonnetTextMetrics measure)
    {
        LastContentScale = 1f;
        var segments = new List<SonnetDirector.Segment>();
        foreach (SonnetDirector.Segment segment in shot.Segments)
        {
            if (segment.WordLike) segments.Add(segment);
        }
        if (segments.Count == 0) return new List<Placement>();
        if (segments.Count > 10) segments = segments.GetRange(0, 10);
        if (shot.Interlude)
        {
            return LayoutInterlude(shot, baseFontSize, measure, segments);
        }

        var placements = new List<Placement>(segments.Count);
        int visibleChars = 0;
        foreach (SonnetDirector.Segment segment in segments)
        {
            visibleChars += Math.Max(1, segment.Text.Trim().Length);
        }
        int variantSeed = visibleChars + segments.Count;

        int heroIndex = FindHeroIndex(segments);
        int secondaryHero = -1;
        int editorialVariant = variantSeed % 5;
        if (editorialVariant == 3 && segments.Count > 2)
        {
            secondaryHero = FindSecondaryHero(segments, heroIndex);
            if (secondaryHero < 0) editorialVariant = 0;
        }
        else if (editorialVariant == 3)
        {
            editorialVariant = 0;
        }
        else if (editorialVariant == 4 && segments.Count < 2)
        {
            editorialVariant = 2;
        }
        int ribbonVariant = variantSeed % 3;
        int tableauVariant = variantSeed % 4;
        int collageVariant = variantSeed % 3;

        for (int i = 0; i < segments.Count; i++)
        {
            SonnetDirector.Segment segment = segments[i];
            bool isHero = i == heroIndex
                    || (i == secondaryHero && shot.Kind == SonnetKind.EditorialColumn
                        && editorialVariant == 3);
            bool isSemiHero = (i == heroIndex - 1 || i == heroIndex + 1) && !isHero;
            SonnetRole role = isHero ? SonnetRole.Hero
                    : isSemiHero ? SonnetRole.SemiHero : SonnetRole.Support;

            float heroScale = 3.0f;
            float supportScale = 1.15f;
            bool vertical = false;
            float rotation = 0f;
            switch (shot.Kind)
            {
                case SonnetKind.EditorialColumn:
                    if (editorialVariant == 3)
                    {
                        heroScale = 3.8f;
                        supportScale = 1.3f;
                    }
                    else if (editorialVariant == 4)
                    {
                        heroScale = 4.2f;
                        supportScale = 1.25f;
                        vertical = isHero || isSemiHero;
                    }
                    else
                    {
                        heroScale = editorialVariant == 2 ? 3.2f : 4.0f;
                        supportScale = 1.2f;
                        vertical = (isHero || isSemiHero) && editorialVariant != 2;
                    }
                    break;
                case SonnetKind.TypeImpact:
                    heroScale = 5.5f;
                    supportScale = 1.5f;
                    break;
                case SonnetKind.FragmentCollage:
                    heroScale = 3.2f;
                    supportScale = 1.35f;
                    vertical = isSemiHero || i % 4 == 0;
                    break;
                case SonnetKind.TrackingRibbon:
                    heroScale = 3.5f;
                    supportScale = 1.5f;
                    break;
                case SonnetKind.MaskReveal:
                    heroScale = 4.5f;
                    supportScale = 1.6f;
                    vertical = isHero || isSemiHero;
                    break;
                case SonnetKind.PosterBlocks:
                    heroScale = 4.4f;
                    supportScale = 1.15f;
                    break;
                default:
                    heroScale = 3.0f;
                    supportScale = 1.15f;
                    vertical = (isHero || isSemiHero) && (tableauVariant == 0 || tableauVariant == 1);
                    break;
            }

            float fontScale = isHero ? heroScale
                    : isSemiHero ? Math.Max(supportScale * 1.35f, heroScale * 0.72f)
                    : supportScale;
            bool cjk = ContainsCjk(segment.Text);
            if (vertical && !cjk && segment.Text.Length > 1)
            {
                vertical = false;
                rotation += (float)(Math.PI / 2);
            }

            var placement = new Placement
            {
                SegmentIndex = i,
                Text = segment.Text,
                Role = role,
            };
            float roleLimit = isHero ? 260f : isSemiHero ? 170f : 110f;
            placement.FontSize = Math.Min(roleLimit,
                    Math.Max(baseFontSize * 0.85f, baseFontSize * fontScale));
            // Vertical pillars and long horizontal words must not outgrow the
            // safe area; capping the font here is what stops vertical lyrics
            // from overlapping their neighbours.
            int placementChars = Math.Max(1, VisibleCharCount(placement.Text));
            if (vertical)
            {
                float verticalLimit = height * 0.42f / (placementChars * 1.45f);
                placement.FontSize = Math.Min(placement.FontSize, Math.Max(20f, verticalLimit));
            }
            else
            {
                float horizontalLimit = width * 0.50f / (placementChars * 0.58f);
                placement.FontSize = Math.Min(placement.FontSize, Math.Max(20f, horizontalLimit));
            }
            placement.Rotation = rotation;
            placement.Vertical = vertical;
            Measure(placement, measure);
            placements.Add(placement);
        }

        Place(shot, placements, heroIndex, width, height, baseFontSize,
                editorialVariant, ribbonVariant, tableauVariant, collageVariant);
        ResolveOverlaps(placements, width, height);
        Fit(placements, width, height, measure);
        ResolveOverlaps(placements, width, height);
        // A final proportional fit: scaling positions and fonts together cannot
        // introduce new overlaps, unlike clamping boxes one by one.
        Fit(placements, width, height, measure);
        AssignEntrances(placements, heroIndex, width, height);
        return placements;
    }

    // ------------------------------------------------------------------
    // Placement families
    // ------------------------------------------------------------------

    /// <summary>
    /// Interlude markers are real lyric typography: three equal dots in the same
    /// bold face, laid out along the break's flow direction and entering one
    /// after another through the normal glyph timing.
    /// </summary>
    private static List<Placement> LayoutInterlude(SonnetDirector.Shot shot,
                                                   float baseFontSize,
                                                   ISonnetTextMetrics measure,
                                                   List<SonnetDirector.Segment> segments)
    {
        var placements = new List<Placement>(segments.Count);
        float fontSize = Math.Max(74f, Math.Min(175f, baseFontSize * 1.95f));
        float step = fontSize * (shot.InterludeVertical ? 0.2325f : 0.285f);
        for (int i = 0; i < segments.Count; i++)
        {
            var placement = new Placement
            {
                SegmentIndex = i,
                Text = segments[i].Text,
                Role = SonnetRole.Support,
                FontSize = fontSize,
                Rotation = 0f,
                Vertical = false,
            };
            if (shot.InterludeVertical)
            {
                placement.X = 0f;
                placement.Y = (i - (segments.Count - 1) * 0.5f) * step;
                placement.EnterX = 40f;
                placement.EnterY = 0f;
            }
            else
            {
                placement.X = (i - (segments.Count - 1) * 0.5f) * step;
                placement.Y = 0f;
                placement.EnterX = 0f;
                placement.EnterY = 40f;
            }
            Measure(placement, measure);
            placements.Add(placement);
        }
        return placements;
    }

    private static void Place(SonnetDirector.Shot shot, List<Placement> boxes, int heroIndex,
                              float width, float height, float baseFontSize,
                              int editorialVariant, int ribbonVariant,
                              int tableauVariant, int collageVariant)
    {
        PlaceFlow(shot, boxes, heroIndex, width, height, baseFontSize,
                ribbonVariant, tableauVariant, collageVariant);
    }

    /// <summary>
    /// The original layouts are flow based: consecutive segments sit one
    /// flowGap apart and the reading order never jumps across the stage. That is
    /// what keeps the camera glide short and stable, so this director uses the
    /// same principle for every shot kind and only varies the flow axis / jitter
    /// per family.
    /// </summary>
    private static void PlaceFlow(SonnetDirector.Shot shot, List<Placement> boxes, int heroIndex,
                                  float width, float height, float baseFontSize,
                                  int ribbonVariant, int tableauVariant, int collageVariant)
    {
        float flowGap = Clamp(baseFontSize * 0.7f, 28f, 84f);
        float stackGap = Math.Max(40f, flowGap * 1.5f);
        bool vertical = shot.Kind == SonnetKind.QuietTableau
                || shot.Kind == SonnetKind.MaskReveal;
        bool jitter = shot.Kind == SonnetKind.FragmentCollage
                || shot.Kind == SonnetKind.PosterBlocks
                || shot.Kind == SonnetKind.TypeImpact;
        Placement hero = boxes[heroIndex];
        hero.X = 0f;
        hero.Y = 0f;

        if (vertical)
        {
            float down = hero.Y + hero.MeasuredHeight * 0.5f + stackGap;
            float up = hero.Y - hero.MeasuredHeight * 0.5f - stackGap;
            int slot = 0;
            for (int i = heroIndex + 1; i < boxes.Count; i++)
            {
                Placement box = boxes[i];
                box.X = hero.X + JitterOffset(slot, tableauVariant, box);
                box.Y = down + box.MeasuredHeight * 0.5f;
                down += box.MeasuredHeight + stackGap;
                slot++;
            }
            slot = 0;
            for (int i = heroIndex - 1; i >= 0; i--)
            {
                Placement box = boxes[i];
                box.X = hero.X + JitterOffset(slot, tableauVariant, box);
                box.Y = up - box.MeasuredHeight * 0.5f;
                up -= box.MeasuredHeight + stackGap;
                slot++;
            }
        }
        else
        {
            float right = hero.X + hero.MeasuredWidth * 0.5f + flowGap;
            float left = hero.X - hero.MeasuredWidth * 0.5f - flowGap;
            int slot = 0;
            for (int i = heroIndex + 1; i < boxes.Count; i++)
            {
                Placement box = boxes[i];
                box.X = right + box.MeasuredWidth * 0.5f;
                box.Y = hero.Y + JitterOffset(slot, collageVariant, box);
                right += box.MeasuredWidth + flowGap;
                slot++;
            }
            slot = 0;
            for (int i = heroIndex - 1; i >= 0; i--)
            {
                Placement box = boxes[i];
                box.X = left - box.MeasuredWidth * 0.5f;
                box.Y = hero.Y + JitterOffset(slot, collageVariant, box);
                left -= box.MeasuredWidth + flowGap;
                slot++;
            }
        }
        if (jitter)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                boxes[i].Rotation += i % 2 == 0 ? -0.018f : 0.022f;
            }
        }
        // Give every word its own typographic voice: staggered baselines and a
        // slight rotation make the segmentation visible while the compact flow
        // keeps consecutive words close enough for a calm camera.
        for (int i = 0; i < boxes.Count; i++)
        {
            if (i == heroIndex) continue;
            Placement box = boxes[i];
            float stagger = Math.Min(52f, box.FontSize * 0.42f);
            if (vertical)
            {
                box.X += (i % 3 - 1) * stagger * 0.7f;
            }
            else
            {
                box.Y += (i % 3 - 1) * stagger;
            }
            box.Rotation += i % 2 == 0 ? -0.045f : 0.055f;
        }
    }

    private static float JitterOffset(int slot, int variant, Placement box)
    {
        int pattern = (variant + slot) % 3;
        if (pattern == 0) return 0f;
        return (pattern == 1 ? 1f : -1f) * Math.Min(26f, box.MeasuredHeight * 0.18f);
    }

    // ------------------------------------------------------------------
    // Fit + entrance vectors
    // ------------------------------------------------------------------

    private static void Fit(List<Placement> boxes, float width, float height,
                            ISonnetTextMetrics measure)
    {
        float safeW = width * SafeHalfW;
        float safeH = height * SafeHalfH;
        float contentScale = 1f;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            bool fits = true;
            foreach (Placement box in boxes)
            {
                if (Math.Abs(box.X) + box.MeasuredWidth * 0.5f > safeW
                    || Math.Abs(box.Y) + box.MeasuredHeight * 0.5f > safeH)
                {
                    fits = false;
                    break;
                }
            }
            if (fits)
            {
                LastContentScale = contentScale;
                return;
            }
            if (contentScale * 0.9f < 0.35f)
            {
                // Never shrink past readability.
                break;
            }
            contentScale *= 0.9f;
            foreach (Placement box in boxes)
            {
                box.X *= 0.9f;
                box.Y *= 0.9f;
                box.FontSize *= 0.9f;
                Measure(box, measure);
            }
        }
        LastContentScale = contentScale;
    }

    /// <summary>
    /// Pushes measured boxes apart so two segments can never sit on top of each
    /// other on the scene plane.
    /// </summary>
    private static void ResolveOverlaps(List<Placement> boxes, float width, float height)
    {
        float minGap = Math.Max(22f, Math.Min(width, height) * 0.03f);
        for (int iteration = 0; iteration < 30; iteration++)
        {
            bool moved = false;
            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    Placement a = boxes[i];
                    Placement b = boxes[j];
                    float dx = b.X - a.X;
                    float dy = b.Y - a.Y;
                    float ox = (a.MeasuredWidth + b.MeasuredWidth) * 0.5f + minGap - Math.Abs(dx);
                    float oy = (a.MeasuredHeight + b.MeasuredHeight) * 0.5f + minGap - Math.Abs(dy);
                    if (ox <= 0f || oy <= 0f) continue;
                    if (ox < oy)
                    {
                        float push = ox * 0.5f + 0.5f;
                        float sign = dx >= 0f ? 1f : -1f;
                        a.X -= push * sign;
                        b.X += push * sign;
                    }
                    else
                    {
                        float push = oy * 0.5f + 0.5f;
                        float sign = dy >= 0f ? 1f : -1f;
                        a.Y -= push * sign;
                        b.Y += push * sign;
                    }
                    moved = true;
                }
            }
            if (!moved) return;
        }
    }

    private static void AssignEntrances(List<Placement> boxes, int heroIndex,
                                        float width, float height)
    {
        Placement hero = boxes[heroIndex];
        for (int i = 0; i < boxes.Count; i++)
        {
            Placement box = boxes[i];
            if (i == heroIndex)
            {
                box.EnterX = 0f;
                box.EnterY = 0f;
                continue;
            }
            // Keep the glyph entrance subtle: a large hero font used to push
            // whole words down by 50-70 px on every shot change, which read as
            // the picture jumping downwards.
            float distance = Math.Max(10f, Math.Min(26f, box.FontSize * 0.09f));
            float dx = box.X - hero.X;
            float dy = box.Y - hero.Y;
            if (Math.Abs(dx) < 1f && Math.Abs(dy) < 1f)
            {
                dx = (i % 2 == 0 ? -1f : 1f) * width * 0.2f;
            }
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                box.EnterX = dx >= 0 ? -distance : distance;
                box.EnterY = 0f;
            }
            else
            {
                box.EnterX = 0f;
                // Items below the hero rise up into place; items above settle
                // downwards. Both come from their own side of the composition.
                box.EnterY = dy >= 0 ? distance : -distance;
            }
            float normal = (i * 37 % 100 / 100f * 2f - 1f) * box.FontSize * 0.12f;
            if (box.Vertical)
            {
                box.EnterX += (i % 2 == 0 ? -1f : 1f) * box.FontSize * 0.10f;
            }
            else
            {
                box.EnterY += normal * 0.35f;
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static int FindHeroIndex(List<SonnetDirector.Segment> segments)
    {
        int best = 0;
        float bestScore = float.MinValue;
        for (int i = 0; i < segments.Count; i++)
        {
            SonnetDirector.Segment segment = segments[i];
            int visible = segment.Text.Trim().Length;
            float score = visible * 1.6f + (segment.EndMs - segment.StartMs) / 1000f * 2.2f;
            if (visible >= 2) score += 6f;
            if (visible >= 4) score += 4f;
            float middle = Math.Abs(i - (segments.Count - 1) * 0.5f)
                    / Math.Max(1f, segments.Count * 0.5f);
            score += (1f - middle) * 6f;
            if (ContainsPunctuation(segment.Text)) score += 5f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    private static bool ContainsPunctuation(string text)
    {
        foreach (char c in text)
        {
            if (c == '!' || c == '?' || c == '\uFF01' || c == '\uFF1F' || c == '\u2026')
            {
                return true;
            }
        }
        return false;
    }

    private static int FindSecondaryHero(List<SonnetDirector.Segment> segments, int heroIndex)
    {
        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < segments.Count; i++)
        {
            if (i == heroIndex) continue;
            SonnetDirector.Segment segment = segments[i];
            int visible = segment.Text.Trim().Length;
            if (visible == 0) continue;
            float score = visible * 1.4f + Math.Abs(i - heroIndex) * 2f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    private static void Measure(Placement box, ISonnetTextMetrics measure)
    {
        float baseWidth;
        float baseHeight;
        if (box.Vertical)
        {
            // CJK fonts are much taller than the web font metrics the original
            // 0.9em advance assumed, so use the font's own ascent / descent to
            // stop vertical characters from overlapping.
            float verticalAdvance = Math.Max(box.FontSize * 1.30f,
                    (measure.Descent(box.FontSize) - measure.Ascent(box.FontSize)) * 0.95f) + 2f;
            baseWidth = box.FontSize * 1.02f;
            baseHeight = verticalAdvance * Math.Max(1, box.Text.Length);
        }
        else
        {
            baseWidth = measure.MeasureText(box.Text, box.FontSize);
            baseHeight = box.FontSize * 1.18f;
        }
        // Latin words in a vertical composition are rotated 90 degrees, so their
        // footprint on the stage is the rotated bounding box, not the unrotated
        // text metrics.
        float cosine = Math.Abs((float)Math.Cos(box.Rotation));
        float sine = Math.Abs((float)Math.Sin(box.Rotation));
        box.MeasuredWidth = baseWidth * cosine + baseHeight * sine;
        box.MeasuredHeight = baseWidth * sine + baseHeight * cosine;
    }

    private static int VisibleCharCount(string? text)
    {
        if (text == null) return 1;
        int count = 0;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) count++;
        }
        return Math.Max(1, count == 0 ? text.Length : count);
    }

    private static bool ContainsCjk(string? text)
    {
        if (text == null) return false;
        foreach (char c in text)
        {
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3040 && c <= 0x30FF)
                || (c >= 0xAC00 && c <= 0xD7AF))
            {
                return true;
            }
        }
        return false;
    }

    private static float Clamp(float value, float min, float max)
        => Math.Max(min, Math.Min(max, value));
}
