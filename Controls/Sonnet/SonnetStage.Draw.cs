using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Windows.Foundation;
using Windows.UI;

namespace BukaMusicDesktop.Controls;

/// <summary>Drawing half of the Sonnet stage (port of SonnetStageView.onDraw).</summary>
public sealed partial class SonnetStage
{
    private CanvasDevice? _device;
    private CanvasLinearGradientBrush? _scrim;
    private CanvasRadialGradientBrush? _vignette;
    private CanvasBitmap? _noiseBitmap;
    private CanvasBitmap? _halftoneBitmap;
    private bool _scrimDirty = true;

    private CanvasBitmap? _artSource;
    private CanvasRenderTarget? _backdrop;
    private CanvasRenderTarget? _backdropPrev;
    private long _backdropFadeStartMs;
    private int _viewPixelWidth = 1920;

    public void EnsureResources(CanvasDevice device)
    {
        if (ReferenceEquals(_device, device) && _scrim != null) return;
        _device = device;
        _text?.Dispose();
        _text = new SonnetText(device);
        _scrimDirty = true;
        BackgroundArtDirty();
    }

    private void EnsureBrushes(CanvasDevice device)
    {
        if (!_scrimDirty && _scrim != null && _vignette != null) return;
        _scrim?.Dispose();
        _vignette?.Dispose();
        _scrim = new CanvasLinearGradientBrush(device,
                Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0x55, 0, 0, 0))
        {
            StartPoint = new Vector2(0f, _viewH * 0.3f),
            EndPoint = new Vector2(0f, _viewH),
        };
        _vignette = new CanvasRadialGradientBrush(device,
                new[]
                {
                    new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0x00, 0, 0, 0) },
                    new CanvasGradientStop { Position = 0.62f, Color = Color.FromArgb(0x00, 0, 0, 0) },
                    new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0x6E, 0, 0, 0) },
                })
        {
            Center = new Vector2(_viewW * 0.5f, _viewH * 0.5f),
            RadiusX = Math.Max(840f, _viewH * 1.17f),
            RadiusY = Math.Max(840f, _viewH * 1.17f),
        };
        _scrimDirty = false;
    }

    public void SetViewPixelWidth(int pixelWidth) => _viewPixelWidth = Math.Max(2, pixelWidth);

    /// <summary>Development helper: the blurred backdrop currently in use.</summary>
    public CanvasRenderTarget? BackdropForDebug => _backdrop;

    private SonnetText _textImpl = null!;

    private SonnetText _text
    {
        get
        {
            if (_textImpl == null)
            {
                _device ??= CanvasDevice.GetSharedDevice();
                _textImpl = new SonnetText(_device);
            }
            return _textImpl;
        }
        set => _textImpl = value;
    }

    public void Dispose()
    {
        _scrim?.Dispose();
        _vignette?.Dispose();
        _noiseBitmap?.Dispose();
        _halftoneBitmap?.Dispose();
        _backdrop?.Dispose();
        _backdropPrev?.Dispose();
        _textImpl?.Dispose();
    }

    // ------------------------------------------------------------------
    // Backdrop (blurred album art)
    // ------------------------------------------------------------------

    /// <summary>Blurred album-art backdrop, drawn inside the scene.</summary>
    public void SetBackgroundArt(CanvasBitmap? art)
    {
        if (ReferenceEquals(art, _artSource)) return;
        _artSource = art;
        // The new backdrop is fully built *before* it replaces the old one:
        // clearing first left one frame without any backdrop, which showed up as
        // a black flash on every track change.
        CanvasRenderTarget? built = BuildBackdrop(art);
        CanvasRenderTarget? previous = _backdrop;
        _backdrop = built;
        if (previous != null && !ReferenceEquals(previous, _backdrop))
        {
            _backdropPrev?.Dispose();
            _backdropPrev = previous;
            _backdropFadeStartMs = Now();
        }
    }

    private void BackgroundArtDirty()
    {
        CanvasBitmap? source = _artSource;
        _artSource = null;
        SetBackgroundArt(source);
    }

    /// <summary>
    /// Exactly the pipeline used by the Android playback screen's blurred
    /// background: the source is crushed to screenWidth / 384 pixels wide and
    /// then stretched back up, which is what produces the very soft look.
    /// </summary>
    private CanvasRenderTarget? BuildBackdrop(CanvasBitmap? art)
    {
        if (art == null) return null;
        try
        {
            var size = art.SizeInPixels;
            int sourceWidth = (int)size.Width;
            int sourceHeight = (int)size.Height;
            if (sourceWidth <= 0 || sourceHeight <= 0) return null;
            var creator = art.Device;
            int tinyWidth = Math.Max(2, (int)Math.Round(_viewPixelWidth / 384f));
            float aspect = sourceWidth / (float)Math.Max(1, sourceHeight);
            int tinyHeight = Math.Max(2, (int)Math.Round(tinyWidth / Math.Max(0.1f, aspect)));
            int midWidth = Math.Max(6, tinyWidth * 4);
            int midHeight = Math.Max(6, tinyHeight * 4);

            using var tiny = new CanvasRenderTarget(creator, tinyWidth, tinyHeight, 96f);
            using (CanvasDrawingSession ds = tiny.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);
                ds.DrawImage(art, new Rect(0, 0, tinyWidth, tinyHeight),
                        new Rect(0, 0, sourceWidth, sourceHeight), 1f,
                        CanvasImageInterpolation.Linear);
            }
            using var mid = new CanvasRenderTarget(creator, midWidth, midHeight, 96f);
            using (CanvasDrawingSession ds = mid.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);
                // Explicit rectangles only: drawing an image at its natural size
                // collapses to a nearly empty quad here.
                ds.DrawImage(tiny, new Rect(0, 0, midWidth, midHeight),
                        new Rect(0, 0, tinyWidth, tinyHeight), 1f,
                        CanvasImageInterpolation.Linear);
            }

            // Built in *virtual* units: the backdrop is drawn inside the scaled
            // scene canvas.
            int targetW = Math.Max(2, (int)Math.Round(_viewW));
            int targetH = Math.Max(2, (int)Math.Round(_viewH));
            float cover = Math.Max(targetW / (float)midWidth, targetH / (float)midHeight);
            float drawWidth = midWidth * cover;
            float drawHeight = midHeight * cover;
            float left = (targetW - drawWidth) * 0.5f;
            float top = (targetH - drawHeight) * 0.5f;
            // Not disposed here: the caller owns the returned backdrop.
            var result = new CanvasRenderTarget(creator, targetW, targetH, 96f);
            using (CanvasDrawingSession ds = result.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);
                ds.DrawImage(mid, new Rect(left, top, drawWidth, drawHeight),
                        new Rect(0, 0, midWidth, midHeight), 1f,
                        CanvasImageInterpolation.Linear);
            }
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Frame
    // ------------------------------------------------------------------

    /// <summary>Draws one full frame in virtual units; the caller sets the pixel scale.</summary>
    public void DrawSceneFrame(CanvasDrawingSession session, long positionMs, long now)
    {
        session.FillRectangle(0f, 0f, _viewW, _viewH, StageBaseColor);
        if (_backdrop != null)
        {
            float fade = 1f;
            if (_backdropPrev != null)
            {
                long elapsed = now - _backdropFadeStartMs;
                fade = SonnetMotion.EaseInOut(
                        SonnetMotion.Clamp01(elapsed / (float)BackdropFadeMs));
                if (fade >= 1f)
                {
                    _backdropPrev.Dispose();
                    _backdropPrev = null;
                }
            }
            if (_backdropPrev != null)
            {
                session.DrawImage(_backdropPrev, new Rect(0, 0, _viewW, _viewH),
                        new Rect(0, 0, _backdropPrev.SizeInPixels.Width, _backdropPrev.SizeInPixels.Height),
                        1f - fade, CanvasImageInterpolation.Linear);
            }
            session.DrawImage(_backdrop, new Rect(0, 0, _viewW, _viewH),
                    new Rect(0, 0, _backdrop.SizeInPixels.Width, _backdrop.SizeInPixels.Height),
                    fade, CanvasImageInterpolation.Linear);
            float mask = BackgroundMaskAlpha(positionMs, now);
            session.FillRectangle(0f, 0f, _viewW, _viewH,
                    Color.FromArgb((byte)Math.Round(mask * 255f), 0, 0, 0));
        }
        EnsureBrushes(session.Device);
        session.FillRectangle(0f, 0f, _viewW, _viewH, _scrim!);
        DrawStage(session, positionMs, now);
        DrawTranslationSubtitle(session, positionMs);
        DrawPostEffects(session);
    }

    /// <summary>
    /// The lyric backdrop keeps brightening while a line is being sung: it
    /// starts out dimmed and ends up nearly clear, then the next line starts
    /// from the dim value again. The value is smoothed so the reset at a line
    /// boundary reads as a slow darkening instead of a flicker.
    /// </summary>
    private float BackgroundMaskAlpha(long positionMs, long now)
    {
        float target = BackdropMaskIdle;
        if (_lines.Count > 0)
        {
            int index = LineIndexAt(positionMs);
            if (index >= 0)
            {
                SonnetLine line = _lines[index];
                long duration = Math.Max(1L, line.EndMs - line.StartMs);
                float progress = SonnetMotion.Clamp01(
                        (positionMs - line.StartMs) / (float)duration);
                target = BackdropMaskLineStart
                        + (BackdropMaskLineEnd - BackdropMaskLineStart)
                        * SonnetMotion.EaseInOut(progress);
            }
        }
        long deltaMs = _backgroundMaskAtMs == 0L
                ? 16L : Math.Min(120L, Math.Max(1L, now - _backgroundMaskAtMs));
        _backgroundMaskAtMs = now;
        if (!_backgroundMaskInitialized)
        {
            _backgroundMaskInitialized = true;
            _backgroundMaskAlpha = target;
        }
        else
        {
            float k = Math.Min(1f, deltaMs / BackdropMaskSmoothMs);
            _backgroundMaskAlpha += (target - _backgroundMaskAlpha) * k;
        }
        return SonnetMotion.Clamp01(_backgroundMaskAlpha);
    }

    private int LineIndexAt(long positionMs)
    {
        if (_lines.Count == 0 || positionMs < _lines[0].StartMs) return -1;
        int lo = 0;
        int hi = _lines.Count - 1;
        int result = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_lines[mid].StartMs <= positionMs)
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

    private void DrawStage(CanvasDrawingSession session, long positionMs, long now)
    {
        if (_director.IsEmpty)
        {
            DrawNoLyricsScene(session, now);
            DrawFrameMarks(session);
            return;
        }
        int shotIndex = _director.ShotIndexAt(positionMs);
        bool allowSwitch = _activeShot < 0
                || shotIndex > _activeShot
                || (shotIndex < _activeShot
                    && positionMs < _director.Shots[Math.Max(0, _activeShot)].StartMs - 400L);
        if (shotIndex != _activeShot && allowSwitch)
        {
            _previousShot = _activeShot;
            _activeShot = shotIndex;
            _shotChangedAtMs = now;
            _transition = _previousShot < 0 ? SonnetTransitionKind.CameraPull
                    : ResolveBoundaryTransition(_previousShot);
            if (_transition == _previousTransition && _previousShot >= 0)
            {
                _transition = NextTransition(_transition);
            }
            _previousTransition = _transition;
            long duration = 200L;
            if (_previousShot >= 0 && _activeShot >= 0)
            {
                long delta = _director.Shots[_activeShot].StartMs
                        - _director.Shots[_previousShot].StartMs;
                duration = (long)(Math.Min(0.34f, Math.Max(0.22f, delta / 1000f * 0.22f)) * 1000f);
            }
            _transitionDurationMs = duration;
        }

        Scene? current = EnsureScene(_activeShot);
        if (current == null)
        {
            DrawInstrumental(session, now);
            return;
        }
        float progress = SonnetMotion.Clamp01((now - _shotChangedAtMs) / (float)_transitionDurationMs);
        bool transitioning = _previousShot >= 0 && _previousShot != _activeShot && progress < 1f;
        if (transitioning)
        {
            Scene? outgoing = EnsureScene(_previousShot);
            CameraFrame incomingCamera = ResolveCamera(current, positionMs);
            CameraFrame outgoingCamera = outgoing == null
                    ? incomingCamera : ResolveCamera(outgoing, positionMs);
            float eased = SonnetMotion.EaseInOut(progress);
            CameraFrame blended = LerpCamera(outgoingCamera, incomingCamera, eased);
            if (outgoing != null)
            {
                CameraFrame outgoingBlended = LerpCamera(outgoingCamera, incomingCamera, eased);
                DrawTransitionOutgoing(session, outgoing, positionMs, progress, outgoingBlended);
            }
            DrawScene(session, current, positionMs,
                    (1f - InAmount(progress) * 0.72f) * _contentAlpha,
                    _transition == SonnetTransitionKind.FastBlur ? OutAmount(progress) : 0f,
                    _transition == SonnetTransitionKind.MonoGlitch ? OutAmount(progress) : 0f,
                    0f, blended);
        }
        else
        {
            DrawScene(session, current, positionMs, _contentAlpha, 0f, 0f, 0f, null);
        }
        DrawFrameMarks(session);
    }

    private SonnetTransitionKind ResolveBoundaryTransition(int boundaryIndex)
    {
        SonnetTransitionKind[] kinds = SonnetMotion.AllTransitions;
        int mixed = SonnetDirector.HashSeed(_seed + ":" + (boundaryIndex + 1));
        return kinds[SonnetDirector.FloorMod(mixed, kinds.Length)];
    }

    private static SonnetTransitionKind NextTransition(SonnetTransitionKind current)
    {
        SonnetTransitionKind[] kinds = SonnetMotion.AllTransitions;
        return kinds[((int)current + 1) % kinds.Length];
    }

    private void DrawTransitionOutgoing(CanvasDrawingSession session, Scene scene, long positionMs,
                                        float progress, CameraFrame cameraOverride)
    {
        float amount = SonnetMotion.EaseInOut(SonnetMotion.Clamp01(progress));
        if (_transition == SonnetTransitionKind.FastBlur)
        {
            int copies = _lowPower ? 2 : 3;
            for (int i = 0; i < copies; i++)
            {
                float offset = (i - (copies - 1) * 0.5f) * (6f + 12f * amount);
                DrawScene(session, scene, positionMs, (1f - amount) / copies,
                        amount * 14f, 0f, offset, cameraOverride);
            }
        }
        else if (_transition == SonnetTransitionKind.MonoGlitch)
        {
            DrawGlitchScene(session, scene, positionMs, 1f - (progress > 0.86f
                    ? (progress - 0.86f) / 0.14f : 0f), amount, cameraOverride);
        }
        else
        {
            DrawScene(session, scene, positionMs, 1f - amount, 0f, 0f, 0f, cameraOverride);
        }
    }

    private void DrawGlitchScene(CanvasDrawingSession session, Scene scene, long positionMs,
                                 float alpha, float amount, CameraFrame cameraOverride)
    {
        int bands = _lowPower ? 5 : 8;
        var random = new JavaRandom(_frameCounter / 2 * 31L + _activeShot * 7L);
        for (int band = 0; band < bands; band++)
        {
            float top = band * _viewH / bands;
            float bottom = (band + 1) * _viewH / bands;
            float offset = (random.NextFloat() - 0.5f) * 54f * (0.35f + amount);
            using (session.CreateLayer(1f, new Rect(0, top, _viewW, bottom - top)))
            {
                DrawScene(session, scene, positionMs, alpha * 0.6f, 0f, 0f, offset, cameraOverride);
            }
        }
        for (int i = 0; i < 3; i++)
        {
            float y = random.NextFloat() * _viewH;
            session.FillRectangle(0f, y, _viewW, 1.6f,
                    WithAlpha(_accentColor, Math.Min(200, 46 + 120 * amount) / 255f));
        }
    }

    private void DrawScene(CanvasDrawingSession session, Scene scene, long positionMs, float alpha,
                           float blur, float glitch, float offsetX, CameraFrame? cameraOverride)
    {
        if (alpha <= 0.01f) return;
        CameraFrame camera = cameraOverride ?? ResolveCamera(scene, positionMs);
        Matrix3x2 saved = session.Transform;
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateTranslation(
                        camera.BaseX + camera.MotionX * _viewW + offsetX,
                        camera.BaseY + camera.MotionY * _viewH), saved);
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateScale(camera.Scale, camera.Scale), session.Transform);
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateRotation(camera.Rotation), session.Transform);
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateTranslation(-camera.PivotX, -camera.PivotY), session.Transform);

        DrawSceneMg(session, scene, positionMs, alpha);
        DrawShotKindFx(session, scene, positionMs, alpha);
        DrawSceneBackFx(session, scene, positionMs, alpha, camera);
        foreach (Glyph glyph in scene.Glyphs)
        {
            DrawGlyph(session, scene, glyph, positionMs, alpha, camera);
        }
        DrawSceneFrontFx(session, scene, positionMs, alpha, camera);
        session.Transform = saved;
    }

    private void DrawGlyph(CanvasDrawingSession session, Scene scene, Glyph glyph, long positionMs,
                           float alpha, CameraFrame camera)
    {
        if (positionMs < glyph.StartMs) return;
        float p = SonnetMotion.SegmentProgress(glyph.StartMs, glyph.SettleMs, positionMs);
        float offset = 1f - p;
        float coreAlpha = 0.16f + 0.84f * p;
        if (glyph.Role == SonnetRole.Decoration) coreAlpha *= 0.07f;
        float scale = glyph.Emphasized && scene.Shot.Kind == SonnetKind.TypeImpact
                ? 0.52f + 0.48f * p
                : glyph.Emphasized ? 0.78f + 0.22f * p : 0.68f + 0.32f * p;
        float depthScale = 1f + glyph.ZDepth * 0.45f;
        float parallaxX = camera.MotionX * VW * glyph.ZDepth * 2.5f;
        float parallaxY = camera.MotionY * VH * glyph.ZDepth * 2.5f;
        float x = glyph.BaseX + glyph.EnterX * offset + parallaxX;
        float y = glyph.BaseY + glyph.EnterY * offset + parallaxY;
        float rotation = glyph.Rotation + glyph.EntryRotation * offset;

        Matrix3x2 saved = session.Transform;
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateTranslation(x, y), session.Transform);
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateRotation(rotation), session.Transform);
        session.Transform = Matrix3x2.Multiply(
                Matrix3x2.CreateScale(scale * depthScale, scale * depthScale), session.Transform);

        float baseline = glyph.FontSize * 0.35f;
        float a = alpha * coreAlpha;
        if (!_glMode && !_lowPower && glyph.Emphasized && p < 0.92f)
        {
            // Halo + chromatic split during the hit, like the original's glyph layers.
            Matrix3x2 inner = session.Transform;
            session.Transform = Matrix3x2.Multiply(
                    Matrix3x2.CreateScale(1.12f, 1.12f), session.Transform);
            _text.DrawCentered(session, glyph.Text, 0f, baseline, glyph.FontSize,
                    WithAlpha(TextColor, a * 70f / 255f));
            session.Transform = inner;
            float ca = glyph.FontSize * 0.06f * (1f - p);
            _text.DrawCentered(session, glyph.Text, -ca, baseline, glyph.FontSize,
                    WithAlpha(Color.FromArgb(255, 0xFF, 0x3B, 0x5C), a * 150f / 255f));
            _text.DrawCentered(session, glyph.Text, ca, baseline + ca * 0.5f, glyph.FontSize,
                    WithAlpha(Color.FromArgb(255, 0x35, 0xF2, 0xE5), a * 150f / 255f));
        }
        if (!_glMode && !_lowPower && glyph.Emphasized)
        {
            // Offset echo behind the hero glyph, a light print-style duplicate.
            _text.DrawCentered(session, glyph.Text, 8f, baseline + 6f, glyph.FontSize,
                    WithAlpha(TextColor, a * 42f / 255f));
        }
        _text.DrawCentered(session, glyph.Text, 0f, baseline, glyph.FontSize,
                WithAlpha(TextColor, a));
        session.Transform = saved;
    }

    // ------------------------------------------------------------------
    // Instrumental interlude
    // ------------------------------------------------------------------

    private Interlude InterludeAt(long positionMs)
    {
        var info = new Interlude();
        if (_lines.Count < 2) return info;
        int index = LineIndexAt(positionMs);
        if (index < 0 || index + 1 >= _lines.Count) return info;
        SonnetLine current = _lines[index];
        SonnetLine next = _lines[index + 1];
        long gap = next.StartMs - current.EndMs;
        if (gap < SonnetDirector.InterludeMinMs || positionMs <= current.EndMs
            || positionMs >= next.StartMs)
        {
            return info;
        }
        info.Active = true;
        info.Progress = SonnetMotion.Clamp01(
                (positionMs - current.EndMs) / (float)Math.Max(1L, gap));
        int sceneIndex = _director.ShotIndexAt(positionMs);
        if (_sceneCache.TryGetValue(sceneIndex, out Scene? scene))
        {
            info.Vertical = scene.VerticalComposition;
        }
        return info;
    }

    /// <summary>1 at both ends of the gap, dimmed through the middle so the dots read.</summary>
    private static float InterludeSceneAlpha(Interlude interlude)
    {
        float elapsed = interlude.Progress;
        float remaining = 1f - interlude.Progress;
        float fadeOut = SonnetMotion.Clamp01(elapsed / 0.16f);
        float fadeIn = SonnetMotion.Clamp01(remaining / 0.16f);
        float dim = Math.Min(fadeOut, fadeIn);
        return 1f - 0.82f * dim;
    }

    /// <summary>
    /// The break marker is lyric typography, not a widget: the same bold white
    /// face as the PV, with the three dots appearing one after another and the
    /// last one breathing near the end of the interlude.
    /// </summary>
    private void DrawInterludeDots(CanvasDrawingSession session, Interlude interlude, long now)
    {
        float progress = interlude.Progress;
        float pulse = 0.5f + 0.5f * (float)Math.Sin(now / 420.0);
        float size = interlude.Vertical ? 66f : 76f;
        float step = interlude.Vertical ? size * 1.25f : size * 0.62f;
        for (int i = 0; i < 3; i++)
        {
            float appear = SonnetMotion.Clamp01((progress * 3f - i) / 0.45f);
            if (appear <= 0.01f) continue;
            float alpha = 235f * appear;
            if (i == 2 && progress > 0.66f) alpha = (150f + 105f * pulse) * appear;
            float x = _viewW * 0.5f + (interlude.Vertical ? 0f : (i - 1) * step);
            float y = interlude.Vertical ? _viewH * 0.42f + i * step : _viewH * 0.55f;
            float scale = 0.6f + 0.4f * appear;
            Matrix3x2 saved = session.Transform;
            session.Transform = Matrix3x2.Multiply(
                    RotateScaleAt(scale, 0f, x, y), session.Transform);
            _text.DrawCentered(session, "\u00B7", x, y, size, WithAlpha(TextColor, alpha / 255f));
            session.Transform = saved;
        }
    }

    /// <summary>
    /// Translation stays out of the PV plane, exactly like the original's bottom
    /// subtitle overlay: original lyrics drive the typography, the translation
    /// fades in below.
    /// </summary>
    private void DrawTranslationSubtitle(CanvasDrawingSession session, long positionMs)
    {
        if (_lines.Count == 0) return;
        int index = LineIndexAt(positionMs);
        if (index < 0 || index >= _lines.Count) return;
        SonnetLine line = _lines[index];
        string translation = line.TranslationOrNull;
        if (translation.Length == 0) return;
        float appear = SonnetMotion.Clamp01((positionMs - line.StartMs) / 300f);
        float exit = SonnetMotion.Clamp01((line.EndMs - positionMs) / 300f);
        float alpha = Math.Min(appear, exit) * 0.85f * _contentAlpha;
        if (alpha <= 0.02f) return;

        float fontSize = 40f;
        float maxWidth = _viewW * 0.78f;
        float width = _text.MeasureText(translation, fontSize);
        if (width > maxWidth && width > 0f)
        {
            fontSize = Math.Max(28f, 40f * maxWidth / width);
        }
        float centerX = _viewW * 0.5f;
        float baselineY = _viewH - 58f;
        // No box or outline: the original subtitle is plain text over the scene.
        _text.DrawCentered(session, translation, centerX + 1.5f, baselineY + 2f, fontSize,
                WithAlpha(Colors.Black, alpha * 0.55f));
        _text.DrawCentered(session, translation, centerX, baselineY, fontSize,
                WithAlpha(TextColor, alpha));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Color WithAlpha(Color color, float alpha)
    {
        float clamped = Math.Max(0f, Math.Min(1f, alpha));
        return Color.FromArgb((byte)Math.Round(clamped * 255f), color.R, color.G, color.B);
    }

    private static Matrix3x2 ScaleAt(float sx, float sy, float px, float py)
        => Matrix3x2.Multiply(
            Matrix3x2.CreateTranslation(px, py),
            Matrix3x2.Multiply(Matrix3x2.CreateScale(sx, sy),
                Matrix3x2.CreateTranslation(-px, -py)));

    private static Matrix3x2 RotateAt(float radians, float px, float py)
        => Matrix3x2.Multiply(
            Matrix3x2.CreateTranslation(px, py),
            Matrix3x2.Multiply(Matrix3x2.CreateRotation(radians),
                Matrix3x2.CreateTranslation(-px, -py)));

    private static Matrix3x2 RotateScaleAt(float scale, float radians, float px, float py)
        => Matrix3x2.Multiply(
            Matrix3x2.CreateTranslation(px, py),
            Matrix3x2.Multiply(Matrix3x2.CreateRotation(radians),
                Matrix3x2.Multiply(Matrix3x2.CreateScale(scale, scale),
                    Matrix3x2.CreateTranslation(-px, -py))));
}
