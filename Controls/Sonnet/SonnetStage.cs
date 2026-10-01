using System;
using System.Collections.Generic;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Windows.UI;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// Native renderer for the Sonnet-style lyric PV - the desktop port of the
/// Android SonnetStageView.
///
/// <p>Every shot is a scene on a shared 2D plane at its own camera position and
/// zoom. The camera tracks the glyph being sung with the original weighted
/// smoothing, so lines continuously slide up / down / left / right as if a
/// camera were flying across the plane. Shots are 4 lines / 6 seconds long and
/// switch with the original three short transitions.
/// </summary>
public sealed partial class SonnetStage : IDisposable
{
    public const float VW = 1280f;
    public const float VH = 720f;

    /// <summary>Scrim alpha at the start of a lyric line.</summary>
    /// <summary>
    /// Backdrop dimming at the start of a lyric line. Android uses 0.46, which is
    /// tuned for a TV across the room; on a monitor that step up from 0.10 makes
    /// the whole picture ~40% darker for a third of a second and reads as the
    /// screen suddenly going black, so the desktop keeps the breathing but much
    /// gentler.
    /// </summary>
    private const float BackdropMaskLineStart = 0.26f;
    /// <summary>Scrim alpha once the line has been sung out.</summary>
    private const float BackdropMaskLineEnd = 0.10f;
    /// <summary>Scrim alpha while no lyric line is on screen (intro / instrumental).</summary>
    private const float BackdropMaskIdle = 0.18f;
    /// <summary>Time constant of the smoothing that hides line-boundary steps.</summary>
    private const float BackdropMaskSmoothMs = 620f;
    /// <summary>
    /// How far the backdrop is scaled past the frame (3% per side). The blurred
    /// picture and the layers above it can shift by a few dozen pixels while the
    /// lyrics sway; without this the flat base colour showed as a hard bordered
    /// band along the edges.
    /// </summary>
    private const float BackdropOverscan = 0.03f;

    /// <summary>
    /// Diagnosis switch (BUKA_LYRICS_NODARK=1): skips the three full-frame
    /// darkening layers (per-line mask, scrim, vignette) so a stray dark border
    /// can be traced to the layer that draws it.
    /// </summary>
    public static bool SkipDarkLayers { get; set; } =
        Environment.GetEnvironmentVariable("BUKA_LYRICS_NODARK") == "1";

    /// <summary>Diagnosis switch (BUKA_LYRICS_NOBACKDROP=1): skips the blurred cover.</summary>
    public static bool SkipBackdrop { get; set; } =
        Environment.GetEnvironmentVariable("BUKA_LYRICS_NOBACKDROP") == "1";
    /// <summary>Crossfade length when the blurred cover changes.</summary>
    private const long BackdropFadeMs = 650L;

    private static readonly Color TextColor = Colors.White;
    private static readonly Color SubtleColor = Color.FromArgb(255, 0xA8, 0xC3, 0xE0);
    private static readonly Color StageBaseColor = Color.FromArgb(255, 0x0A, 0x14, 0x28);
    private static readonly Color ShapeSoftColor = Color.FromArgb(255, 0xEA, 0xF6, 0xFF);

    private sealed class Glyph
    {
        public string Text = "";
        public float BaseX;
        public float BaseY;
        public float EnterX;
        public float EnterY;
        public float EntryRotation;
        public float Rotation;
        public float FontSize;
        public long StartMs;
        public long EndMs;
        public long SettleMs;
        public SonnetRole Role;
        public bool Emphasized;
        public int SegmentIndex;
        public float ZDepth;
    }

    private sealed class Landmark
    {
        public long StartMs;
        public long EndMs;
        public float X;
        public float Y;
    }

    private sealed class Scene
    {
        public SonnetDirector.Shot Shot = null!;
        public List<Glyph> Glyphs = new();
        public List<Glyph> Tracking = new();
        public List<Landmark> Landmarks = new();
        public float BasePivotX;
        public float BasePivotY;
        public int MgVariant;
        public bool VerticalComposition;
        public float ContentScale = 1f;
        public float[][]? Particles;
        public float[][]? Sparkles;
        public float FocusX;
        public float FocusY;
        public bool FocusInitialized;
        public long LastFocusTimeMs = long.MinValue / 4;
    }

    private sealed class CameraFrame
    {
        public float BaseX;
        public float BaseY;
        public float PivotX;
        public float PivotY;
        public float Scale = 1f;
        public float Rotation;
        public float MotionX;
        public float MotionY;
    }

    private sealed class Interlude
    {
        public bool Active;
        public float Progress;
        public bool Vertical;
    }

    private SonnetDirector _director = SonnetDirector.Empty;
    private List<SonnetLine> _lines = new();
    private string _seed = "sonnet";
    private List<string> _hints = new();
    private List<long>? _onsets;
    private Color _accentColor = Color.FromArgb(255, 0x4F, 0xC3, 0xF7);
    private bool _lowPower;
    /// <summary>The GL/pixel-shader layer owns the print stack, so skip the Canvas overlays.</summary>
    private bool _canvasPostEffects;
    private bool _glMode = true;

    private Scene? _noLyricsScene;
    private long _noLyricsStartMs = Now();
    private readonly Dictionary<int, Scene> _sceneCache = new();

    private long _statePositionMs;
    private long _stateUptimeMs = Now();
    private bool _playing;
    /// <summary>Audio keeps playing through the 2.5 s fade-out; keep following it.</summary>
    private long _pauseGraceUntilMs;
    private bool _pauseAnchored = true;

    private int _activeShot = -1;
    private int _previousShot = -1;
    private long _shotChangedAtMs = Now();
    private SonnetTransitionKind _transition = SonnetTransitionKind.CameraPull;
    private SonnetTransitionKind _previousTransition = SonnetTransitionKind.CameraPull;
    private long _transitionDurationMs = 200L;

    private int _frameCounter;
    private long _lastRenderMs;
    private long _displayPositionMs;
    private bool _displayPositionInitialized;

    private float _backgroundMaskAlpha;
    private bool _backgroundMaskInitialized;
    private long _backgroundMaskAtMs;
    private float _contentAlpha = 1f;

    /// <summary>
    /// Visible area in virtual units. The scene is authored against VW x VH, but
    /// the renderer stretches the vertical field of view so the lyrics use the
    /// whole screen on 4:3 tablets or ultra-wide displays instead of being
    /// letterboxed.
    /// </summary>
    private float _viewW = VW;
    private float _viewH = VH;

    public static long Now() => Environment.TickCount64;

    public SonnetStage()
    {
        _canvasPostEffects = false;
    }

    // ------------------------------------------------------------------
    // Public inputs
    // ------------------------------------------------------------------

    public bool IsEmpty => _director.IsEmpty;

    public void SetViewport(float width, float height)
    {
        float w = width > 0f ? width : VW;
        float h = height > 0f ? height : VH;
        if (Math.Abs(w - _viewW) < 0.5f && Math.Abs(h - _viewH) < 0.5f) return;
        _viewW = w;
        _viewH = h;
        _scrimDirty = true;
        BackgroundArtDirty();
    }

    public void SetLyrics(IReadOnlyList<SonnetLine>? lyrics, string seedText)
    {
        _seed = string.IsNullOrEmpty(seedText) ? "sonnet" : seedText;
        _lines = new List<SonnetLine>();
        if (lyrics != null) _lines.AddRange(lyrics);
        _director = SonnetDirector.Compile(_lines, _seed, _hints, _onsets);
        _sceneCache.Clear();
        _noLyricsScene = null;
        _noLyricsStartMs = Now();
        _activeShot = -1;
        _previousShot = -1;
        // The backdrop dimming is deliberately *not* reset here: its smoothing
        // carries the value over from the previous track instead of snapping.
    }

    /// <summary>
    /// Song / artist names from the track, used as protected words by the local
    /// segmenter so names like 黄龄 or HOYO-MiX are not split into single chars.
    /// </summary>
    public void SetHints(List<string>? values)
    {
        _hints = values == null ? new List<string>() : new List<string>(values);
        if (_lines.Count == 0) return;
        _director = SonnetDirector.Compile(_lines, _seed, _hints, _onsets);
        _sceneCache.Clear();
        _noLyricsScene = null;
        _activeShot = -1;
        _previousShot = -1;
    }

    /// <summary>
    /// Vocal-onset times for the current track. When they arrive after the
    /// lyrics, the shot program is rebuilt so word starts snap to the singing.
    /// </summary>
    public void SetOnsets(List<long>? values)
    {
        _onsets = values == null || values.Count == 0 ? null : new List<long>(values);
        if (_lines.Count == 0) return;
        _director = SonnetDirector.Compile(_lines, _seed, _hints, _onsets);
        _sceneCache.Clear();
        _noLyricsScene = null;
        _activeShot = -1;
        _previousShot = -1;
    }

    public void SetAccentColor(Color color)
    {
        _accentColor = color;
    }

    public void SetLowPower(bool value)
    {
        _lowPower = value;
    }

    /// <summary>When the pixel-shader print stack runs, the Canvas overlays are skipped.</summary>
    public void SetCanvasPostEffectsEnabled(bool value) => _canvasPostEffects = value;

    /// <summary>Text shown by the typographic "no lyrics" scene.</summary>
    public string NoLyricsText
    {
        get => _noLyricsText ?? Core.Loc.Current.Text("暂未找到歌词");
        set => _noLyricsText = value;
    }

    private string? _noLyricsText;

    public void SetContentAlpha(float value)
    {
        float clamped = SonnetMotion.Clamp01(value);
        if (Math.Abs(clamped - _contentAlpha) < 0.004f) return;
        _contentAlpha = clamped;
    }

    public void SetPlaybackState(long positionMs, bool isPlaying)
    {
        long now = Now();
        if (_playing && !isPlaying)
        {
            // The player publishes "paused" immediately, then fades the audio out
            // before the decoder actually stops.
            _pauseGraceUntilMs = now + 2_800L;
            _pauseAnchored = false;
            _statePositionMs = Math.Max(0, positionMs);
            _stateUptimeMs = now;
        }
        else if (isPlaying)
        {
            _pauseGraceUntilMs = 0L;
            _pauseAnchored = false;
            long reported = Math.Max(0L, positionMs);
            if (_displayPositionInitialized && reported < _displayPositionMs - 3_500L)
            {
                // A large backwards jump is a real seek / track change.
                _statePositionMs = reported;
            }
            else if (_displayPositionInitialized && reported < _displayPositionMs)
            {
                // The player publishes the pre-pause position when resuming; the
                // fade already advanced the rendered clock, so keep it.
                _statePositionMs = _displayPositionMs;
            }
            else
            {
                _statePositionMs = reported;
            }
            _stateUptimeMs = now;
        }
        else
        {
            // Already paused: never pull the rendered position backwards.
            if (positionMs > _statePositionMs)
            {
                _statePositionMs = positionMs;
                _stateUptimeMs = now;
            }
        }
        _playing = isPlaying;
    }

    public void SetPaused()
    {
        _statePositionMs = CurrentPositionMs();
        _stateUptimeMs = Now();
        _playing = false;
    }

    // ------------------------------------------------------------------
    // Frame loop
    // ------------------------------------------------------------------

    public bool WantsAnimationFrames()
    {
        if (_playing || Now() < _pauseGraceUntilMs) return true;
        // Keep drawing while the backdrop is still crossfading.
        if (_backdropPrev != null) return true;
        return Now() - _shotChangedAtMs < _transitionDurationMs + 150L;
    }

    /// <summary>Real blur radius (in virtual pixels) for the current transition.</summary>
    public float CurrentBlurStrength()
    {
        if (_transition != SonnetTransitionKind.FastBlur) return 0f;
        if (_previousShot < 0 || _previousShot == _activeShot) return 0f;
        float progress = SonnetMotion.Clamp01((Now() - _shotChangedAtMs) / (float)_transitionDurationMs);
        return progress >= 1f ? 0f : OutAmount(progress) * 14f;
    }

    public long DisplayPositionMs()
    {
        long now = Now();
        AnchorPausedPositionIfNeeded(now);
        return SmoothPosition(CurrentPositionMs(), now);
    }

    private long CurrentPositionMs()
    {
        long now = Now();
        if (!_playing && now >= _pauseGraceUntilMs) return _statePositionMs;
        return _statePositionMs + Math.Max(0, now - _stateUptimeMs);
    }

    /// <summary>
    /// The player reports the audio clock every ~500 ms and its correction can
    /// be a few dozen milliseconds. Applying that directly to the camera makes
    /// the whole plane twitch, so ease the displayed clock towards the report.
    /// Large differences are real seeks and snap immediately.
    /// </summary>
    private long SmoothPosition(long target, long now)
    {
        // Clamp the step: after a long pause the wall-clock gap can be tens of
        // seconds, which would make the easing coefficient 1 and teleport the
        // lyrics on the first resumed frame.
        long deltaTime = _lastRenderMs == 0
                ? 16L : Math.Min(50L, Math.Max(1L, now - _lastRenderMs));
        _lastRenderMs = now;
        if (!_displayPositionInitialized || Math.Abs(target - _displayPositionMs) > 3_000L)
        {
            _displayPositionMs = target;
            _displayPositionInitialized = true;
            return _displayPositionMs;
        }
        float k = 1f - (float)Math.Exp(-deltaTime / 120.0);
        _displayPositionMs += (long)Math.Round((target - _displayPositionMs) * k);
        if (Math.Abs(target - _displayPositionMs) <= 4L)
        {
            _displayPositionMs = target;
        }
        return _displayPositionMs;
    }

    /// <summary>Freeze exactly where the fade-out actually ended, before reading the target.</summary>
    private void AnchorPausedPositionIfNeeded(long now)
    {
        if (!_playing && !_pauseAnchored && _pauseGraceUntilMs > 0
            && now >= _pauseGraceUntilMs && _displayPositionInitialized)
        {
            _statePositionMs = _displayPositionMs;
            _stateUptimeMs = now;
            _pauseAnchored = true;
        }
    }

    public void AdvanceFrameCounter() => _frameCounter++;

    // ------------------------------------------------------------------
    // Scene building
    // ------------------------------------------------------------------

    private Scene? EnsureScene(int index)
    {
        if (index < 0 || index >= _director.Shots.Count) return null;
        if (_sceneCache.TryGetValue(index, out Scene? cached)) return cached;
        Scene scene = BuildScene(_director.Shots[index]);
        _sceneCache[index] = scene;
        PruneScenes(index);
        return scene;
    }

    private void PruneScenes(int activeIndex)
    {
        var stale = new List<int>();
        foreach (int key in _sceneCache.Keys)
        {
            if (Math.Abs(key - activeIndex) > 1) stale.Add(key);
        }
        foreach (int key in stale) _sceneCache.Remove(key);
    }

    private Scene BuildScene(SonnetDirector.Shot shot)
    {
        var wordSegments = new List<SonnetDirector.Segment>();
        foreach (SonnetDirector.Segment segment in shot.Segments)
        {
            if (segment.WordLike) wordSegments.Add(segment);
        }
        var scene = new Scene
        {
            Shot = shot,
            MgVariant = SonnetDirector.FloorMod(shot.ParagraphIndex, 8),
        };
        var fxRandom = new JavaRandom(SonnetDirector.HashSeed(_seed + ":fx:" + shot.Index));
        int particleCount = _glMode ? 22 : (_lowPower ? 14 : 34);
        scene.Particles = new float[particleCount][];
        for (int i = 0; i < particleCount; i++)
        {
            scene.Particles[i] = new[]
            {
                (fxRandom.NextFloat() * 2f - 1f) * VW * 0.62f,
                (fxRandom.NextFloat() * 2f - 1f) * VH * 0.56f,
                1.4f + fxRandom.NextFloat() * 3.4f,
                fxRandom.NextFloat() * 2f - 1f,
                fxRandom.NextFloat() * (float)Math.PI * 2f,
            };
        }
        int sparkleCount = _glMode ? 7 : (_lowPower ? 5 : 11);
        scene.Sparkles = new float[sparkleCount][];
        for (int i = 0; i < sparkleCount; i++)
        {
            scene.Sparkles[i] = new[]
            {
                (fxRandom.NextFloat() * 2f - 1f) * VW * 0.52f,
                (fxRandom.NextFloat() * 2f - 1f) * VH * 0.46f,
                fxRandom.NextFloat() * (float)Math.PI * 2f,
                5f + fxRandom.NextFloat() * 7f,
            };
        }
        if (wordSegments.Count == 0)
        {
            return scene;
        }
        int wordCount = wordSegments.Count;
        float baseFontSize = BaseFontSize(wordCount);
        List<SonnetLayout.Placement> placements =
                SonnetLayout.Layout(shot, VW, VH, baseFontSize, _text);
        scene.ContentScale = SonnetLayout.LastContentScale;
        long motionDuration = Math.Min(
                (long)Math.Max(650f, Math.Min(1800f, shot.DurationMs * 0.42f)),
                (long)(shot.DurationMs * 0.72f));
        motionDuration = Math.Max(120L, motionDuration);

        var glyphs = new List<Glyph>();
        foreach (SonnetLayout.Placement placement in placements)
        {
            if (placement.SegmentIndex < 0 || placement.SegmentIndex >= wordSegments.Count) continue;
            SonnetDirector.Segment segment = wordSegments[placement.SegmentIndex];
            float totalAdvance = 0f;
            int count = segment.Graphemes.Count;
            var advances = new float[count];
            for (int i = 0; i < count; i++)
            {
                string ch = segment.Graphemes[i].Text;
                float advance;
                if (placement.Vertical)
                {
                    advance = Math.Max(placement.FontSize * 1.30f,
                            (_text.Descent(placement.FontSize) - _text.Ascent(placement.FontSize)) * 0.95f) + 2f;
                }
                else
                {
                    advance = Math.Max(placement.FontSize * 0.2f,
                            _text.MeasureText(ch, placement.FontSize));
                }
                advances[i] = advance;
                totalAdvance += advance;
            }
            float cursor = -totalAdvance * 0.5f;
            for (int i = 0; i < count; i++)
            {
                SonnetDirector.Grapheme grapheme = segment.Graphemes[i];
                float localX = placement.Vertical ? 0f : cursor + advances[i] * 0.5f;
                float localY = placement.Vertical ? cursor + advances[i] * 0.5f : 0f;
                float cosine = (float)Math.Cos(placement.Rotation);
                float sine = (float)Math.Sin(placement.Rotation);
                var glyph = new Glyph
                {
                    Text = grapheme.Text,
                    BaseX = placement.X + localX * cosine - localY * sine,
                    BaseY = placement.Y + localX * sine + localY * cosine,
                    Rotation = placement.Rotation,
                    FontSize = placement.FontSize,
                    StartMs = grapheme.StartMs,
                    EndMs = segment.EndMs,
                    SettleMs = Math.Max(grapheme.StartMs, grapheme.StartMs + motionDuration),
                    Role = placement.Role,
                    Emphasized = placement.Role == SonnetRole.Hero || placement.Role == SonnetRole.SemiHero,
                    SegmentIndex = placement.SegmentIndex,
                    ZDepth = 0f,
                };
                int stagger = i % 2 == 0 ? -1 : 1;
                glyph.EnterX = placement.EnterX + (placement.Vertical
                        ? stagger * placement.FontSize * 0.10f : 0f);
                glyph.EnterY = placement.EnterY + (placement.Vertical
                        ? 0f : stagger * Math.Min(42f, placement.FontSize * 0.18f));
                glyph.EntryRotation = stagger * (placement.Role == SonnetRole.Support ? 0.035f : 0.055f);
                cursor += advances[i];
                glyphs.Add(glyph);
            }
        }

        // Giant decorative echo behind the composition, like the original's
        // oversized background typography.
        SonnetDirector.Segment hero = wordSegments[0];
        float heroFont = 0f;
        foreach (SonnetLayout.Placement placement in placements)
        {
            if (placement.Role == SonnetRole.Hero)
            {
                heroFont = placement.FontSize;
                hero = wordSegments[Math.Min(placement.SegmentIndex, wordSegments.Count - 1)];
                break;
            }
        }
        bool roomForDecoration = placements.Count <= 4
                && (shot.Kind == SonnetKind.TypeImpact
                    || shot.Kind == SonnetKind.PosterBlocks
                    || shot.Kind == SonnetKind.QuietTableau);
        if (heroFont > 0f && roomForDecoration)
        {
            var deco = new Glyph
            {
                Text = hero.Text.Length > 8 ? hero.Text.Substring(0, 8) : hero.Text,
                BaseX = 0f,
                BaseY = -VH * 0.30f,
                FontSize = Math.Min(240f, heroFont * 1.15f),
                Rotation = 0f,
                EntryRotation = 0f,
                EnterX = 0f,
                EnterY = 0f,
                StartMs = shot.StartMs,
                SettleMs = shot.StartMs + motionDuration,
                Role = SonnetRole.Decoration,
                Emphasized = false,
                SegmentIndex = 0,
                ZDepth = -0.8f,
            };
            glyphs.Insert(0, deco);
        }
        scene.Glyphs = glyphs;
        foreach (SonnetLayout.Placement placement in placements)
        {
            if (placement.Vertical || Math.Abs(Math.Sin(placement.Rotation)) > 0.7f)
            {
                scene.VerticalComposition = true;
                break;
            }
        }
        scene.Tracking = new List<Glyph>();
        foreach (Glyph glyph in glyphs)
        {
            if (glyph.Role != SonnetRole.Decoration) scene.Tracking.Add(glyph);
        }
        scene.Tracking.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        scene.Landmarks = new List<Landmark>();
        for (int s = 0; s < wordSegments.Count; s++)
        {
            float sumX = 0f;
            float sumY = 0f;
            int count = 0;
            foreach (Glyph glyph in glyphs)
            {
                if (glyph.Role != SonnetRole.Decoration && glyph.SegmentIndex == s)
                {
                    sumX += glyph.BaseX;
                    sumY += glyph.BaseY;
                    count++;
                }
            }
            if (count == 0) continue;
            SonnetDirector.Segment segment = wordSegments[s];
            scene.Landmarks.Add(new Landmark
            {
                StartMs = segment.StartMs,
                EndMs = segment.EndMs,
                X = sumX / count,
                Y = sumY / count,
            });
        }
        scene.BasePivotX = 0f;
        scene.BasePivotY = 0f;
        foreach (SonnetLayout.Placement placement in placements)
        {
            scene.BasePivotX += placement.X;
            scene.BasePivotY += placement.Y;
        }
        scene.BasePivotX /= Math.Max(1, placements.Count);
        scene.BasePivotY /= Math.Max(1, placements.Count);
        return scene;
    }

    private static float BaseFontSize(int wordCount)
    {
        float baseFontSize = VW / Math.Max(9f, wordCount * 2.6f);
        return Math.Max(26f, Math.Min(88f, baseFontSize));
    }

    /// <summary>One-line state dump (used by the offline render probe and logs).</summary>
    public string DebugSummary(long positionMs)
    {
        Scene? scene = _sceneCache.TryGetValue(_activeShot, out Scene? cached) ? cached : null;
        int glyphs = scene?.Glyphs.Count ?? 0;
        return $"pos={positionMs} shot={_activeShot}/{_director.Shots.Count} " +
               $"glyphs={glyphs} alpha={_contentAlpha:F2} " +
               $"view={_viewW:F0}x{_viewH:F0} blur={CurrentBlurStrength():F1} " +
               $"playing={_playing}";
    }

    /// <summary>Development helper: textual dump of the compiled program and one shot's layout.</summary>
    public string Describe(long positionMs)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"lines={_lines.Count} shots={_director.Shots.Count} seed={_seed}");
        int index = _director.ShotIndexAt(positionMs);
        builder.AppendLine($"shotIndexAt({positionMs})={index}");
        for (int i = Math.Max(0, index - 1);
             i <= Math.Min(_director.Shots.Count - 1, index + 1); i++)
        {
            SonnetDirector.Shot shot = _director.Shots[i];
            builder.AppendLine($"shot {i}: kind={shot.Kind} {shot.StartMs}..{shot.EndMs} " +
                               $"segs={shot.Segments.Count} interlude={shot.Interlude} " +
                               $"cam=({shot.CameraX:F3},{shot.CameraY:F3},{shot.CameraZoom:F3})");
            foreach (SonnetDirector.Segment segment in shot.Segments)
            {
                builder.AppendLine($"   seg '{segment.Text}' {segment.StartMs}-{segment.EndMs} " +
                                   $"graphemes={segment.Graphemes.Count}");
            }
            var words = new List<SonnetDirector.Segment>();
            foreach (SonnetDirector.Segment segment in shot.Segments)
            {
                if (segment.WordLike) words.Add(segment);
            }
            List<SonnetLayout.Placement> placements =
                    SonnetLayout.Layout(shot, VW, VH, BaseFontSize(words.Count), _text);
            builder.AppendLine($"   base={BaseFontSize(words.Count):F1} contentScale={SonnetLayout.LastContentScale:F3}");
            foreach (SonnetLayout.Placement placement in placements)
            {
                builder.AppendLine($"   place '{placement.Text}' role={placement.Role} " +
                                   $"x={placement.X:F1} y={placement.Y:F1} font={placement.FontSize:F1} " +
                                   $"vert={placement.Vertical} mw={placement.MeasuredWidth:F1} " +
                                   $"mh={placement.MeasuredHeight:F1}");
            }
        }
        Scene? scene = EnsureScene(index);
        if (scene != null)
        {
            builder.AppendLine($"scene glyphs={scene.Glyphs.Count} " +
                               $"pivot=({scene.BasePivotX:F1},{scene.BasePivotY:F1}) " +
                               $"contentScale={scene.ContentScale:F3} vertical={scene.VerticalComposition}");
            foreach (Glyph glyph in scene.Glyphs)
            {
                builder.AppendLine($"   glyph '{glyph.Text}' base=({glyph.BaseX:F1},{glyph.BaseY:F1}) " +
                                   $"size={glyph.FontSize:F1} role={glyph.Role} start={glyph.StartMs}");
            }
        }
        return builder.ToString();
    }

    // ------------------------------------------------------------------
    // Camera
    // ------------------------------------------------------------------

    private CameraFrame ResolveCamera(Scene scene, long positionMs)
    {
        SonnetDirector.Shot shot = scene.Shot;
        float progress = SonnetMotion.Clamp01(
                (positionMs - shot.StartMs) / (float)Math.Max(1L, shot.EndMs - shot.StartMs));
        float[] motion = SonnetMotion.ShotMotionFrame(shot.Kind, progress);

        long gapTime = Math.Max(0L, positionMs - shot.EndMs);
        if (gapTime > 0)
        {
            float[] tail = SonnetMotion.ShotMotionFrame(shot.Kind, 0.8f);
            float drift = (float)((1.0 - Math.Exp(-gapTime / 1000.0 * 0.4)) * 0.45);
            motion[0] += (motion[0] - tail[0]) * drift;
            motion[1] += (motion[1] - tail[1]) * drift;
            motion[2] += (motion[2] - tail[2]) * drift;
            motion[3] += (motion[3] - tail[3]) * drift;
            // Long instrumental hold: a slow, organic camera sway so the frame
            // never freezes while the three interlude dots count down.
            AddSway(motion, gapTime);
        }
        if (shot.Interlude)
        {
            // The break is a normal typographic shot now; this is the slow
            // hand-held sway that keeps the three dots drifting through it.
            AddSway(motion, positionMs - shot.StartMs);
        }

        long revealDone = shot.EndMs;
        if (scene.Tracking.Count > 0)
        {
            revealDone = scene.Tracking[^1].SettleMs;
        }
        float breathWeight = SonnetMotion.BreathWeight(positionMs, revealDone, 1200L);
        if (breathWeight > 0f)
        {
            float phase = (SonnetDirector.HashSeed(_seed + ":" + shot.Index) & 1023) / 1023f
                    * (float)Math.PI * 2f;
            float[] breath = SonnetMotion.CameraBreath(positionMs, phase);
            motion[0] += breath[0] * breathWeight;
            motion[1] += breath[1] * breathWeight;
            motion[2] += breath[2] * breathWeight;
            motion[3] += breath[3] * breathWeight;
        }

        float focusX = scene.BasePivotX;
        float focusY = scene.BasePivotY;
        if (scene.Landmarks.Count > 0)
        {
            float[] focus = StableFocus(scene, positionMs);
            focusX = scene.BasePivotX + (focus[0] - scene.BasePivotX) * 0.32f;
            focusY = scene.BasePivotY + (focus[1] - scene.BasePivotY) * 0.32f;
        }

        var frame = new CameraFrame
        {
            BaseX = _viewW * (0.5f + shot.CameraX),
            BaseY = _viewH * (0.48f + shot.CameraY),
            PivotX = scene.BasePivotX + (focusX - scene.BasePivotX),
            PivotY = scene.BasePivotY + (focusY - scene.BasePivotY),
            Scale = shot.CameraZoom * motion[2],
            Rotation = shot.CameraRotation + motion[3],
            MotionX = motion[0] * 0.72f,
            MotionY = motion[1] * 0.72f,
        };
        return frame;
    }

    private static void AddSway(float[] motion, long elapsedMs)
    {
        double swaySeconds = elapsedMs / 1000.0;
        float swayRamp = SonnetMotion.Clamp01((float)swaySeconds / 1.5f);
        motion[0] += (float)Math.Sin(swaySeconds * 0.35) * 0.108f * swayRamp;
        motion[1] += (float)Math.Sin(swaySeconds * 0.27 + 1.2) * 0.084f * swayRamp;
        motion[3] += (float)Math.Sin(swaySeconds * 0.21 + 0.7) * 0.030f * swayRamp;
    }

    /// <summary>
    /// Continuous landmark interpolation with a critically damped follower.
    /// Landmarks are segment centres in timeline order; because the flow layout
    /// keeps consecutive segments close, the target never jumps and the camera
    /// simply glides from one word to the next.
    /// </summary>
    private float[] StableFocus(Scene scene, long positionMs)
    {
        List<Landmark> landmarks = scene.Landmarks;
        float targetX;
        float targetY;
        if (positionMs <= landmarks[0].StartMs)
        {
            targetX = landmarks[0].X;
            targetY = landmarks[0].Y;
        }
        else if (positionMs >= landmarks[^1].StartMs)
        {
            Landmark last = landmarks[^1];
            targetX = last.X;
            targetY = last.Y;
        }
        else
        {
            targetX = landmarks[0].X;
            targetY = landmarks[0].Y;
            for (int i = 0; i < landmarks.Count - 1; i++)
            {
                Landmark current = landmarks[i];
                Landmark next = landmarks[i + 1];
                if (positionMs < current.StartMs || positionMs > next.StartMs) continue;
                float progress = (positionMs - current.StartMs)
                        / (float)Math.Max(1L, next.StartMs - current.StartMs);
                float eased = progress * progress * (3f - 2f * progress);
                targetX = current.X + (next.X - current.X) * eased;
                targetY = current.Y + (next.Y - current.Y) * eased;
                break;
            }
        }
        long deltaTime = positionMs - scene.LastFocusTimeMs;
        if (!scene.FocusInitialized || Math.Abs(deltaTime) > 600L)
        {
            scene.FocusX = targetX;
            scene.FocusY = targetY;
            scene.FocusInitialized = true;
        }
        else
        {
            float dt = Math.Min(0.12f, Math.Max(0.001f, deltaTime / 1000f));
            float k = 1f - (float)Math.Exp(-dt / 0.42f);
            scene.FocusX += (targetX - scene.FocusX) * k;
            scene.FocusY += (targetY - scene.FocusY) * k;
        }
        scene.LastFocusTimeMs = positionMs;
        return new[] { scene.FocusX, scene.FocusY };
    }

    /// <summary>Weighted ±120 ms temporal smoothing, edge-preserving like the original.</summary>
    private float[] SmoothedFocus(Scene scene, long timeMs)
    {
        float[] weights = { 1f, 3f, 5f, 3f, 1f };
        long[] offsets = { -220L, -110L, 0L, 110L, 220L };
        float centerX = 0f;
        float centerY = 0f;
        var point = new float[2];
        for (int i = 0; i < 5; i++)
        {
            long sampleTime = Math.Max(scene.Shot.StartMs,
                    Math.Min(scene.Shot.EndMs, timeMs + offsets[i]));
            FocusRaw(scene, sampleTime, point);
            if (i == 2)
            {
                centerX = point[0];
                centerY = point[1];
            }
        }
        float x = 0f;
        float y = 0f;
        float total = 0f;
        for (int i = 0; i < 5; i++)
        {
            long sampleTime = Math.Max(scene.Shot.StartMs,
                    Math.Min(scene.Shot.EndMs, timeMs + offsets[i]));
            FocusRaw(scene, sampleTime, point);
            float dx = point[0] - centerX;
            float dy = point[1] - centerY;
            if (dx * dx + dy * dy > 140f * 140f) continue;
            x += point[0] * weights[i];
            y += point[1] * weights[i];
            total += weights[i];
        }
        if (total <= 0f) return new[] { centerX, centerY };
        return new[] { x / total, y / total };
    }

    private void FocusRaw(Scene scene, long timeMs, float[] output)
    {
        List<Glyph> glyphs = scene.Tracking;
        if (glyphs.Count == 0)
        {
            output[0] = scene.BasePivotX;
            output[1] = scene.BasePivotY;
            return;
        }
        Glyph first = glyphs[0];
        Glyph last = glyphs[^1];
        float x;
        float y;
        int segmentIndex;
        if (timeMs <= first.StartMs)
        {
            x = first.BaseX;
            y = first.BaseY;
            segmentIndex = first.SegmentIndex;
        }
        else if (timeMs >= last.StartMs)
        {
            x = last.BaseX;
            y = last.BaseY;
            segmentIndex = last.SegmentIndex;
        }
        else
        {
            x = first.BaseX;
            y = first.BaseY;
            segmentIndex = first.SegmentIndex;
            for (int i = 0; i < glyphs.Count - 1; i++)
            {
                Glyph current = glyphs[i];
                Glyph next = glyphs[i + 1];
                if (timeMs < current.StartMs || timeMs > next.StartMs) continue;
                float progress = (timeMs - current.StartMs)
                        / (float)Math.Max(1L, next.StartMs - current.StartMs);
                x = current.BaseX + (next.BaseX - current.BaseX) * progress;
                y = current.BaseY + (next.BaseY - current.BaseY) * progress;
                segmentIndex = current.SegmentIndex;
                break;
            }
        }
        // The original only follows half of the distance from the segment centre.
        float minX = float.MaxValue;
        float maxX = -float.MaxValue;
        float minY = float.MaxValue;
        float maxY = -float.MaxValue;
        bool found = false;
        foreach (Glyph glyph in glyphs)
        {
            if (glyph.SegmentIndex != segmentIndex) continue;
            minX = Math.Min(minX, glyph.BaseX);
            maxX = Math.Max(maxX, glyph.BaseX);
            minY = Math.Min(minY, glyph.BaseY);
            maxY = Math.Max(maxY, glyph.BaseY);
            found = true;
        }
        if (found)
        {
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            x = centerX + (x - centerX) * 0.32f;
            y = centerY + (y - centerY) * 0.32f;
        }
        output[0] = x;
        output[1] = y;
    }

    private static CameraFrame LerpCamera(CameraFrame from, CameraFrame to, float t)
    {
        float p = SonnetMotion.Clamp01(t);
        return new CameraFrame
        {
            BaseX = from.BaseX + (to.BaseX - from.BaseX) * p,
            BaseY = from.BaseY + (to.BaseY - from.BaseY) * p,
            PivotX = from.PivotX + (to.PivotX - from.PivotX) * p,
            PivotY = from.PivotY + (to.PivotY - from.PivotY) * p,
            Scale = from.Scale + (to.Scale - from.Scale) * p,
            Rotation = from.Rotation + (to.Rotation - from.Rotation) * p,
            MotionX = from.MotionX + (to.MotionX - from.MotionX) * p,
            MotionY = from.MotionY + (to.MotionY - from.MotionY) * p,
        };
    }

    private static float InAmount(float progress)
        => 1f - SonnetMotion.EaseInOut(SonnetMotion.Clamp01(progress));

    private static float OutAmount(float progress)
        => SonnetMotion.EaseInOut(SonnetMotion.Clamp01(progress));
}
