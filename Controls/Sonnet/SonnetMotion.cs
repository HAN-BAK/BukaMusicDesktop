using System;

namespace BukaMusicDesktop.Controls;

/// <summary>Shot families, in the same order as the Android enum (order matters: hashing indexes it).</summary>
public enum SonnetKind
{
    EditorialColumn,
    TypeImpact,
    FragmentCollage,
    TrackingRibbon,
    MaskReveal,
    PosterBlocks,
    QuietTableau,
}

/// <summary>Role of one word inside a shot.</summary>
public enum SonnetRole { Hero, SemiHero, Support, Decoration }

/// <summary>Shot-to-shot transition families.</summary>
public enum SonnetTransitionKind { FastBlur, MonoGlitch, CameraPull }

/// <summary>
/// Line-by-line port of the Android SonnetMotion (itself a port of Folia's
/// sonnetMotion.ts). The camera tables are kept exactly: the camera has to keep
/// drifting through the middle of a shot instead of easing to a stop, which is
/// what gives the original its PV feel.
/// </summary>
public static class SonnetMotion
{
    public const float CameraBreathMaxOffset = 0.006f;
    public const float CameraBreathMaxScale = 0.002f;
    public const float CameraBreathMaxRotation = 0.0015f;

    public static readonly SonnetKind[] AllKinds =
    {
        SonnetKind.EditorialColumn, SonnetKind.TypeImpact, SonnetKind.FragmentCollage,
        SonnetKind.TrackingRibbon, SonnetKind.MaskReveal, SonnetKind.PosterBlocks,
        SonnetKind.QuietTableau,
    };

    public static readonly SonnetTransitionKind[] AllTransitions =
    {
        SonnetTransitionKind.FastBlur, SonnetTransitionKind.MonoGlitch,
        SonnetTransitionKind.CameraPull,
    };

    public static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    private static float CubicCoordinate(float p1, float p2, float t)
    {
        float inverse = 1f - t;
        return 3f * inverse * inverse * t * p1
               + 3f * inverse * t * t * p2
               + t * t * t;
    }

    public static float CubicBezier(float x1, float y1, float x2, float y2, float value)
    {
        float target = Clamp01(value);
        if (target <= 0f || target >= 1f) return target;
        float low = 0f;
        float high = 1f;
        float parameter = target;
        for (int i = 0; i < 12; i++)
        {
            float x = CubicCoordinate(x1, x2, parameter);
            if (x < target) low = parameter;
            else high = parameter;
            parameter = (low + high) * 0.5f;
        }
        return CubicCoordinate(y1, y2, parameter);
    }

    public static float EaseInOut(float value) => CubicBezier(0.65f, 0f, 0.35f, 1f, value);
    public static float EaseEnter(float value) => CubicBezier(0.22f, 1f, 0.36f, 1f, value);

    /// <summary>High-tension PV entrance easing.</summary>
    public static float ExpoOut(float value)
        => value >= 1f ? 1f : (float)(1.0 - Math.Pow(2.0, -10.0 * value));

    /// <summary>PV shot path: fast entrance, constant-velocity middle, soft settle.</summary>
    public static float ShotPathProgress(SonnetKind kind, float progress)
    {
        float linear = Clamp01(progress);
        if (kind == SonnetKind.TrackingRibbon || kind == SonnetKind.FragmentCollage
            || kind == SonnetKind.QuietTableau || kind == SonnetKind.PosterBlocks)
        {
            return linear * 0.55f + EaseInOut(linear) * 0.45f;
        }
        if (linear < 0.18f) return ExpoOut(linear / 0.18f) * 0.22f;
        if (linear < 0.78f) return 0.22f + (linear - 0.18f) / 0.6f * 0.56f;
        float settle = (linear - 0.78f) / 0.22f;
        return 0.78f + (1f - (1f - settle) * (1f - settle)) * 0.22f;
    }

    /// <summary>Normalized camera frame {x, y, scale, rotation} for a shot kind.</summary>
    public static float[] ShotMotionFrame(SonnetKind kind, float progress)
    {
        float linear = Clamp01(progress);
        float e = ShotPathProgress(kind, linear);
        switch (kind)
        {
            case SonnetKind.EditorialColumn:
                return new[] { -0.055f + e * 0.095f, 0.025f - e * 0.04f,
                    0.98f + e * 0.07f, -0.006f + e * 0.01f };
            case SonnetKind.TypeImpact:
                return new[] { -0.035f + e * 0.07f, 0.018f - e * 0.028f,
                    1f + (1f - ExpoOut(Math.Min(linear / 0.18f, 1f))) * 0.22f + e * 0.08f,
                    -0.01f + e * 0.016f };
            case SonnetKind.FragmentCollage:
                return new[] { -0.045f + e * 0.085f,
                    0.028f - (float)Math.Sin(e * Math.PI) * 0.055f,
                    0.97f + e * 0.09f, -0.014f + e * 0.028f };
            case SonnetKind.TrackingRibbon:
                return new[] { -0.16f + e * 0.28f, 0.05f - e * 0.085f,
                    0.98f + e * 0.07f, 0.008f - e * 0.014f };
            case SonnetKind.MaskReveal:
                return new[] { 0.035f - e * 0.065f, 0.1f - e * 0.135f,
                    0.96f + e * 0.12f, -0.006f + e * 0.009f };
            case SonnetKind.PosterBlocks:
                return new[] { -0.012f + e * 0.024f, 0.008f - e * 0.016f,
                    0.99f + e * 0.025f, -0.0015f + e * 0.003f };
            default:
                return new[] { -0.022f + e * 0.04f, 0.014f - e * 0.025f,
                    1f + e * 0.028f, -0.002f + e * 0.003f };
        }
    }

    public static float[] CameraBreath(long timeMs, float phase)
    {
        double tau = timeMs / 1000.0 * Math.PI * 2.0;
        float x = (float)((Math.Sin(tau * 0.13 + phase) * 0.65
                + Math.Sin(tau * 0.31 + phase * 1.7) * 0.35) * CameraBreathMaxOffset);
        float y = (float)((Math.Cos(tau * 0.11 + phase * 2.3) * 0.65
                + Math.Sin(tau * 0.29 + phase * 0.9) * 0.35) * CameraBreathMaxOffset);
        float scale = (float)(Math.Sin(tau * 0.09 + phase * 1.3) * CameraBreathMaxScale);
        float rotation = (float)(Math.Sin(tau * 0.07 + phase * 2.9) * CameraBreathMaxRotation);
        return new[] { x, y, scale, rotation };
    }

    public static float BreathWeight(long timeMs, long revealDoneMs, long rampMs)
    {
        if (rampMs <= 0) return timeMs >= revealDoneMs ? 1f : 0f;
        return EaseInOut(Clamp01((timeMs - revealDoneMs) / (float)rampMs));
    }

    /// <summary>Segment-local grapheme progress with the original ExpoOut hit.</summary>
    public static float SegmentProgress(long startMs, long settleMs, long timeMs)
        => ExpoOut(Clamp01((timeMs - startMs) / (float)Math.Max(80L, settleMs - startMs)));
}
