using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace BukaMusicDesktop.Controls;

/// <summary>Scene motion graphics and print stack (port of the Android draw* helpers).</summary>
public sealed partial class SonnetStage
{
    private void DrawSceneMg(CanvasDrawingSession session, Scene scene, long positionMs, float alpha)
    {
        long shotTime = positionMs - scene.Shot.StartMs;
        switch (scene.MgVariant)
        {
            case 0: DrawBotanical(session, shotTime, alpha); break;
            case 1: DrawCelestial(session, shotTime, alpha); break;
            case 2: DrawCraft(session, alpha); break;
            case 3: DrawFlora(session, shotTime, alpha); break;
            case 4: DrawKinetic(session, shotTime, alpha); break;
            case 5: DrawLandscape(session, alpha); break;
            case 6: DrawMarine(session, shotTime, alpha); break;
            default: DrawMusicStaff(session, shotTime, alpha); break;
        }
    }

    private void DrawBotanical(CanvasDrawingSession session, long shotTime, float alpha)
    {
        float sway = (float)Math.Sin(shotTime / 2600.0) * 14f;
        uint soft = (uint)(52 * alpha);
        using (var builder = new CanvasPathBuilder(session))
        {
            builder.BeginFigure(-VW * 0.36f + 96f, VH * 0.16f + VH * 0.42f);
            builder.AddQuadraticBezier(
                    new Vector2(-VW * 0.36f + 86f + sway, VH * 0.16f + 20f),
                    new Vector2(-VW * 0.36f + 150f + sway, VH * 0.16f - VH * 0.34f));
            builder.EndFigure(CanvasFigureLoop.Open);
            using CanvasGeometry geometry = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(geometry, WithAlpha(_accentColor, soft / 255f), 2.4f);
        }
        using (var builder = new CanvasPathBuilder(session))
        {
            builder.BeginFigure(VW * 0.36f - 96f, VH * 0.16f + VH * 0.42f);
            builder.AddQuadraticBezier(
                    new Vector2(VW * 0.36f - 86f - sway, VH * 0.16f + 20f),
                    new Vector2(VW * 0.36f - 150f - sway, VH * 0.16f - VH * 0.34f));
            builder.EndFigure(CanvasFigureLoop.Open);
            using CanvasGeometry geometry = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(geometry, WithAlpha(_accentColor, (uint)(38 * alpha) / 255f), 2.4f);
        }
    }

    private void DrawCelestial(CanvasDrawingSession session, long shotTime, float alpha)
    {
        float angle = shotTime / 5200f;
        Color stroke = WithAlpha(_accentColor, (uint)(40 * alpha) / 255f);
        session.DrawCircle(0f, 0f, 300f, stroke, 1.6f);
        session.DrawCircle(0f, 0f, 220f, stroke, 1.6f);
        Color dot = WithAlpha(_accentColor, (uint)(70 * alpha) / 255f);
        for (int i = 0; i < 5; i++)
        {
            double a = angle + i * Math.PI * 2 / 5;
            session.FillCircle((float)Math.Cos(a) * 300f, (float)Math.Sin(a) * 300f, 5f, dot);
        }
    }

    private void DrawCraft(CanvasDrawingSession session, float alpha)
    {
        Color stroke = WithAlpha(_accentColor, (uint)(30 * alpha) / 255f);
        for (int x = -640; x <= 640; x += 80)
        {
            session.DrawLine(x, -360f, x, 360f, stroke, 1.2f);
        }
        for (int y = -360; y <= 360; y += 80)
        {
            session.DrawLine(-640f, y, 640f, y, stroke, 1.2f);
        }
        Color fill = WithAlpha(_accentColor, (uint)(40 * alpha) / 255f);
        session.FillRectangle(-420f, -210f, 140f, 140f, fill);
        session.FillRectangle(300f, 120f, 130f, 130f, fill);
    }

    private void DrawFlora(CanvasDrawingSession session, long shotTime, float alpha)
    {
        Color fill = WithAlpha(_accentColor, (uint)(42 * alpha) / 255f);
        float spin = shotTime / 6400f;
        for (int i = 0; i < 10; i++)
        {
            float x = -560f + i * 137 % 1120;
            float y = -280f + i * 89 % 560;
            Matrix3x2 saved = session.Transform;
            session.Transform = Matrix3x2.Multiply(
                    RotateAt((spin * 40f + i * 21f) * (float)Math.PI / 180f, x, y), saved);
            session.FillEllipse(x, y, 34f, 8f, fill);
            session.Transform = saved;
        }
    }

    private void DrawKinetic(CanvasDrawingSession session, long shotTime, float alpha)
    {
        Color fill = WithAlpha(_accentColor, (uint)(36 * alpha) / 255f);
        float shift = shotTime / 6f % 260f;
        for (int i = 0; i < 10; i++)
        {
            float y = -300f + i * 68f;
            float width = 180f + i * 53 % 420;
            float x = -600f + i * 97 % 1100 + shift;
            session.FillRectangle(x, y, width, 3f, fill);
        }
    }

    private void DrawLandscape(CanvasDrawingSession session, float alpha)
    {
        using var builder = new CanvasPathBuilder(session);
        builder.BeginFigure(-640f, 300f);
        builder.AddQuadraticBezier(new Vector2(-260f, 40f), new Vector2(80f, 290f));
        builder.AddQuadraticBezier(new Vector2(420f, 60f), new Vector2(640f, 300f));
        builder.EndFigure(CanvasFigureLoop.Open);
        using CanvasGeometry geometry = CanvasGeometry.CreatePath(builder);
        session.DrawGeometry(geometry, WithAlpha(_accentColor, (uint)(44 * alpha) / 255f), 2.4f);
    }

    private void DrawMarine(CanvasDrawingSession session, long shotTime, float alpha)
    {
        for (int row = 0; row < 4; row++)
        {
            using var builder = new CanvasPathBuilder(session);
            float baseY = 120f + row * 36f;
            float phase = shotTime / 900f + row * 1.4f;
            builder.BeginFigure(-640f, baseY);
            for (int x = -640; x <= 640; x += 40)
            {
                builder.AddLine(x, baseY + (float)Math.Sin(x / 130.0 + phase) * (10f + row * 2f));
            }
            builder.EndFigure(CanvasFigureLoop.Open);
            using CanvasGeometry geometry = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(geometry,
                    WithAlpha(_accentColor, (uint)((40 - row * 7) * alpha) / 255f), 2f);
        }
    }

    private void DrawMusicStaff(CanvasDrawingSession session, long shotTime, float alpha)
    {
        Color stroke = WithAlpha(_accentColor, (uint)(34 * alpha) / 255f);
        float top = 150f;
        for (int i = 0; i < 5; i++)
        {
            session.DrawLine(-460f, top + i * 16f, 460f, top + i * 16f, stroke, 1.4f);
        }
        Color note = WithAlpha(_accentColor, (uint)(80 * alpha) / 255f);
        for (int i = 0; i < 7; i++)
        {
            float phase = (shotTime / 1400f + i * 0.7f) % 1f;
            float x = -440f + phase * 880f;
            float y = top + i * 2 % 5 * 16f;
            session.FillEllipse(x, y, 8f, 6f, note);
            session.FillRectangle(x + 7f, y - 36f, 2.6f, 36f, note);
        }
    }

    /// <summary>
    /// Shot-specific motion graphics, following the original's per-kind MG
    /// families: bursts for type-impact, rules for editorial, torn paper for
    /// collage, ribbons / ticks, reveal scans, poster blocks and quiet orbits.
    /// </summary>
    private void DrawShotKindFx(CanvasDrawingSession session, Scene scene, long positionMs, float alpha)
    {
        double t = positionMs / 1000.0;
        var random = new JavaRandom(SonnetDirector.HashSeed("fx" + scene.Shot.Index));
        switch (scene.Shot.Kind)
        {
            case SonnetKind.TypeImpact:
            {
                for (int i = 0; i < 12; i++)
                {
                    double angle = i * Math.PI / 6.0 + t * 0.18;
                    float inner = 120f + (float)Math.Sin(t * 0.7 + i) * 12f;
                    session.DrawLine(
                            (float)Math.Cos(angle) * inner,
                            (float)Math.Sin(angle) * inner,
                            (float)Math.Cos(angle) * (inner + 150f),
                            (float)Math.Sin(angle) * (inner + 150f),
                            WithAlpha(ShapeSoftColor, alpha * 26f / 255f), 1.8f);
                }
                for (int ring = 0; ring < 3; ring++)
                {
                    float progress = (float)((t * 0.5 + ring / 3.0) % 1.0);
                    session.DrawCircle(0f, 0f, 60f + progress * 360f,
                            WithAlpha(_accentColor, alpha * (1f - progress) * 28f / 255f), 1.4f);
                }
                break;
            }
            case SonnetKind.EditorialColumn:
            {
                for (int i = 0; i < 9; i++)
                {
                    float y = -240f + i * 60f;
                    session.DrawLine(-420f, y, 420f, y,
                            WithAlpha(ShapeSoftColor, alpha * 20f / 255f), 1f);
                }
                session.FillRectangle(300f, -300f, 170f, 150f,
                        WithAlpha(_accentColor, alpha * 30f / 255f));
                session.FillRectangle(-470f, 180f, 150f, 120f,
                        WithAlpha(_accentColor, alpha * 18f / 255f));
                break;
            }
            case SonnetKind.FragmentCollage:
            {
                for (int i = 0; i < 5; i++)
                {
                    float x = -520f + random.NextFloat() * 1040f;
                    float y = -300f + random.NextFloat() * 600f;
                    float width = 90f + random.NextFloat() * 180f;
                    float height = 50f + random.NextFloat() * 120f;
                    float rotation = (random.NextFloat() - 0.5f) * 24f;
                    Matrix3x2 saved = session.Transform;
                    session.Transform = Matrix3x2.Multiply(
                            RotateAt(rotation * (float)Math.PI / 180f, x, y), saved);
                    session.DrawRectangle(x - width * 0.5f, y - height * 0.5f, width, height,
                            WithAlpha(ShapeSoftColor, alpha * 26f / 255f), 1.2f);
                    session.FillRectangle(x - width * 0.5f - 18f, y - 8f, width + 36f, 16f,
                            WithAlpha(_accentColor, alpha * 22f / 255f));
                    session.Transform = saved;
                }
                break;
            }
            case SonnetKind.TrackingRibbon:
            {
                for (int band = 0; band < 3; band++)
                {
                    float y = -180f + band * 180f;
                    session.FillRectangle(-560f, y - 2.5f, 1120f, 5f,
                            WithAlpha(band == 1 ? _accentColor : ShapeSoftColor,
                                    alpha * (band == 1 ? 34f : 18f) / 255f));
                    for (int tick = -8; tick <= 8; tick++)
                    {
                        float x = tick * 70f;
                        session.DrawLine(x, y - 14f, x, y - 4f,
                                WithAlpha(ShapeSoftColor, alpha * 22f / 255f), 1.2f);
                    }
                }
                break;
            }
            case SonnetKind.MaskReveal:
            {
                for (int i = 0; i < 6; i++)
                {
                    float y = -300f + i * 120f + (float)(t * 40.0 % 120.0);
                    session.FillRectangle(-520f, y - 9f, 1040f, 18f,
                            WithAlpha(ShapeSoftColor, alpha * 12f / 255f));
                }
                float edge = -260f + (float)(t * 120.0 % 520.0);
                session.DrawLine(edge, -320f, edge, 320f,
                        WithAlpha(_accentColor, alpha * 55f / 255f), 2.4f);
                break;
            }
            case SonnetKind.PosterBlocks:
            {
                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0 ? -1f : 1f) * (180f + i / 2 * 210f);
                    float y = (i / 2 == 0 ? -1f : 1f) * 170f;
                    var rect = new Rect(x - 110f, y - 80f, 220f, 160f);
                    using (CanvasGeometry geometry = CanvasGeometry.CreateRoundedRectangle(
                               session, rect, 10f, 10f))
                    {
                        session.FillGeometry(geometry, WithAlpha(_accentColor, alpha * 20f / 255f));
                        session.DrawGeometry(geometry, WithAlpha(ShapeSoftColor, alpha * 40f / 255f), 1.4f);
                    }
                }
                session.FillCircle(-330f, 230f, 42f, WithAlpha(_accentColor, alpha * 34f / 255f));
                session.FillCircle(360f, -240f, 28f, WithAlpha(_accentColor, alpha * 34f / 255f));
                break;
            }
            default:
            {
                for (int i = 0; i < 3; i++)
                {
                    session.DrawCircle(0f, 0f, 180f + i * 70f,
                            WithAlpha(_accentColor, alpha * (32 - i * 6) / 255f), 1.4f);
                }
                for (int i = 0; i < 6; i++)
                {
                    double angle = t * 0.25 + i * Math.PI / 3.0;
                    session.FillCircle((float)Math.Cos(angle) * 250f,
                            (float)Math.Sin(angle) * 180f, 3.5f,
                            WithAlpha(Colors.White, alpha * 45f / 255f));
                }
                break;
            }
        }
    }

    private void DrawSceneBackFx(CanvasDrawingSession session, Scene scene, long positionMs,
                                 float alpha, CameraFrame camera)
    {
        if (scene.Particles == null) return;
        double t = positionMs / 1000.0;
        for (int i = 0; i < scene.Particles.Length; i++)
        {
            float[] particle = scene.Particles[i];
            float depth = particle[3];
            float x = particle[0] + (float)Math.Sin(t * 0.3 + particle[4]) * 24f
                    + camera.MotionX * VW * depth * 1.6f;
            float y = particle[1] + (float)Math.Cos(t * 0.24 + particle[4]) * 18f
                    + camera.MotionY * VH * depth * 1.6f;
            float twinkle = 0.35f + 0.65f
                    * (0.5f + 0.5f * (float)Math.Sin(t * 0.8 + particle[4] * 1.7));
            Color color = i % 4 == 0 ? _accentColor : ShapeSoftColor;
            session.FillCircle(x, y, particle[2], WithAlpha(color, alpha * 26f * twinkle / 255f));
        }

        // Concentric pulse rings.
        for (int i = 0; i < 3; i++)
        {
            float progress = (float)((t * 0.22 + i / 3.0) % 1.0);
            float radius = 70f + progress * 430f;
            session.DrawCircle(0f, 0f, radius,
                    WithAlpha(_accentColor, alpha * (1f - progress) * 30f / 255f), 2f);
        }

        // Slowly rotating wireframe polygon.
        float polygonRadius = 336f + (float)Math.Sin(t * 0.4) * 12f;
        float polygonRotation = (float)t * 0.07f;
        using (var builder = new CanvasPathBuilder(session))
        {
            for (int i = 0; i < 6; i++)
            {
                double angle = polygonRotation + i * Math.PI / 3.0;
                float x = (float)Math.Cos(angle) * polygonRadius;
                float y = (float)Math.Sin(angle) * polygonRadius * 0.72f;
                if (i == 0) builder.BeginFigure(x, y);
                else builder.AddLine(x, y);
            }
            builder.EndFigure(CanvasFigureLoop.Closed);
            using CanvasGeometry geometry = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(geometry, WithAlpha(_accentColor, alpha * 24f / 255f), 1.6f);
        }

        SonnetKind kind = scene.Shot.Kind;
        if (kind == SonnetKind.TypeImpact || kind == SonnetKind.TrackingRibbon)
        {
            for (int i = 0; i < 9; i++)
            {
                float offset = (float)((i * 137 + t * 110) % 1400.0) - 700f;
                session.DrawLine(offset - 90f, -VH * 0.44f, offset + 90f, VH * 0.44f,
                        WithAlpha(ShapeSoftColor, alpha * 22f / 255f), 1.4f);
            }
        }
        if (kind == SonnetKind.EditorialColumn || kind == SonnetKind.MaskReveal)
        {
            float spacing = 54f;
            float drift = (float)(t * 18.0 % spacing);
            for (int i = 0; i < 16; i++)
            {
                float y = -VH * 0.5f + i * spacing + drift;
                session.DrawLine(-VW * 0.46f, y, VW * 0.46f, y,
                        WithAlpha(_accentColor, alpha * 15f / 255f), 1f);
            }
        }
    }

    private void DrawSceneFrontFx(CanvasDrawingSession session, Scene scene, long positionMs,
                                  float alpha, CameraFrame camera)
    {
        double t = positionMs / 1000.0;
        SonnetKind kind = scene.Shot.Kind;

        // Impact shockwave during the first 700 ms of the shot.
        long sinceStart = positionMs - scene.Shot.StartMs;
        if (kind == SonnetKind.TypeImpact && sinceStart >= 0 && sinceStart < 700L)
        {
            float progress = sinceStart / 700f;
            session.DrawCircle(0f, 0f, 46f + progress * 540f,
                    WithAlpha(_accentColor, alpha * (1f - progress) * 85f / 255f), 3f);
        }

        // Twinkling plus-shaped glints.
        if (scene.Sparkles != null)
        {
            foreach (float[] sparkle in scene.Sparkles)
            {
                float twinkle = 0.5f + 0.5f * (float)Math.Sin(t * 1.1 + sparkle[2]);
                float size = sparkle[3] * twinkle;
                Color color = WithAlpha(Colors.White, alpha * 90f * twinkle / 255f);
                session.DrawLine(sparkle[0] - size, sparkle[1], sparkle[0] + size, sparkle[1], color, 1.6f);
                session.DrawLine(sparkle[0], sparkle[1] - size, sparkle[0], sparkle[1] + size, color, 1.6f);
            }
        }

        // Accent sweep behind the current composition.
        float sweep = (float)(t * 0.32 % 1.0);
        float sweepX = -VW * 0.62f + sweep * VW * 1.24f;
        session.FillRectangle(sweepX, scene.BasePivotY - 150f, 160f, 300f,
                WithAlpha(_accentColor, alpha * 20f / 255f));

        // Technical HUD corner ticks.
        float cx = VW * 0.44f;
        float cy = VH * 0.40f;
        Color tick = WithAlpha(_accentColor, alpha * 34f / 255f);
        session.DrawLine(-cx, -cy, -cx + 26f, -cy, tick, 1.4f);
        session.DrawLine(-cx, -cy, -cx, -cy + 26f, tick, 1.4f);
        session.DrawLine(cx, cy, cx - 26f, cy, tick, 1.4f);
        session.DrawLine(cx, cy, cx, cy - 26f, tick, 1.4f);
    }

    // ------------------------------------------------------------------
    // Instrumental / empty state
    // ------------------------------------------------------------------

    /// <summary>
    /// "暂未找到歌词" is rendered by the same typography pipeline as real lyrics:
    /// a virtual shot of one segment per character, entering one after another
    /// and drifting with a gentle camera sway.
    /// </summary>
    private Scene EnsureNoLyricsScene()
    {
        if (_noLyricsScene != null) return _noLyricsScene;
        string text = NoLyricsText;
        var scene = new Scene
        {
            Shot = new SonnetDirector.Shot(-1, SonnetKind.QuietTableau, 0L, 600_000L,
                    new List<SonnetDirector.Segment>(), 0f, 0f, 1f, 0f, 0, false, false),
            MgVariant = 1,
        };
        var glyphs = new List<Glyph>();
        const float fontSize = 86f;
        var widths = new float[text.Length];
        float totalWidth = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            widths[i] = _text.MeasureText(text.Substring(i, 1), fontSize);
            totalWidth += widths[i];
        }
        float cursor = -totalWidth * 0.5f;
        for (int i = 0; i < text.Length; i++)
        {
            glyphs.Add(new Glyph
            {
                Text = text.Substring(i, 1),
                FontSize = fontSize,
                BaseX = cursor + widths[i] * 0.5f,
                BaseY = 0f,
                Rotation = 0f,
                EntryRotation = 0f,
                EnterX = 0f,
                EnterY = 28f,
                StartMs = i * 220L,
                SettleMs = i * 220L + 900L,
                Role = SonnetRole.Support,
                Emphasized = false,
                SegmentIndex = i,
                ZDepth = 0f,
            });
            cursor += widths[i];
        }
        scene.Glyphs = glyphs;
        scene.Tracking = new List<Glyph>(glyphs);
        _noLyricsScene = scene;
        return scene;
    }

    private void DrawNoLyricsScene(CanvasDrawingSession session, long now)
    {
        Scene scene = EnsureNoLyricsScene();
        long time = _playing ? Math.Max(0L, now - _noLyricsStartMs) : 600_000L;
        var camera = new CameraFrame
        {
            BaseX = _viewW * 0.5f,
            BaseY = _viewH * 0.5f,
            PivotX = 0f,
            PivotY = 0f,
            Scale = 1f,
        };
        double sway = time / 1000.0;
        float ramp = SonnetMotion.Clamp01((float)sway / 1.5f);
        camera.MotionX = (float)Math.Sin(sway * 0.35) * 0.054f * ramp;
        camera.MotionY = (float)Math.Sin(sway * 0.27 + 1.2) * 0.042f * ramp;
        camera.Rotation = (float)Math.Sin(sway * 0.21 + 0.7) * 0.015f * ramp;
        DrawScene(session, scene, time, _contentAlpha, 0f, 0f, 0f, camera);
    }

    private void DrawInstrumental(CanvasDrawingSession session, long now)
    {
        if (_playing)
        {
            Matrix3x2 saved = session.Transform;
            session.Transform = Matrix3x2.Multiply(
                    Matrix3x2.CreateTranslation(_viewW * 0.5f, _viewH * 0.22f), saved);
            DrawMusicStaff(session, now, _contentAlpha);
            session.Transform = saved;
            DrawFrameMarks(session);
            return;
        }
        float pulse = 0.5f + 0.5f * (float)Math.Sin(now / 800.0);
        session.DrawCircle(_viewW * 0.5f, _viewH * 0.5f, 120f + 18f * pulse,
                WithAlpha(_accentColor, (26 + 24 * pulse) * _contentAlpha / 255f), 1.4f);
        session.DrawCircle(_viewW * 0.5f, _viewH * 0.5f, 170f + 26f * pulse,
                WithAlpha(_accentColor, (18 + 14 * pulse) * _contentAlpha / 255f), 1.4f);
        DrawFrameMarks(session);
    }

    // ------------------------------------------------------------------
    // Post effects
    // ------------------------------------------------------------------

    private void DrawPostEffects(CanvasDrawingSession session)
    {
        if (!_canvasPostEffects) return;
        if (!_lowPower && _activeShot >= 0)
        {
            if (_sceneCache.TryGetValue(_activeShot, out Scene? scene)
                && (scene.Shot.Kind == SonnetKind.PosterBlocks
                    || scene.Shot.Kind == SonnetKind.FragmentCollage))
            {
                DrawHalftone(session);
            }
            DrawGrain(session);
        }
        // The vignette is drawn with the radial gradient brush.
        if (_vignette != null)
        {
            session.FillRectangle(0f, 0f, _viewW, _viewH, _vignette);
        }
    }

    private void DrawGrain(CanvasDrawingSession session)
    {
        _noiseBitmap ??= BuildNoiseBitmap(session.Device);
        if (_noiseBitmap == null) return;
        // The noise is tiled in *device pixels*, not scene units: the scene is
        // scaled up on high resolution windows, which used to blow the grain up
        // with it and made the lyric screen look noisy.
        float scale = _viewW > 1f ? _viewPixelWidth / _viewW : 1f;
        if (scale < 0.1f) scale = 1f;
        float width = _noiseBitmap.SizeInPixels.Width / scale;
        float height = _noiseBitmap.SizeInPixels.Height / scale;
        int offsetX = (int)(_frameCounter * 7 % (int)width);
        int offsetY = (int)(_frameCounter * 11 % (int)height);
        var source = new Rect(0, 0, width, height);
        for (float y = -offsetY; y < _viewH; y += height)
        {
            for (float x = -offsetX; x < _viewW; x += width)
            {
                session.DrawImage(_noiseBitmap, new Rect(x, y, width, height), source,
                        8f / 255f, CanvasImageInterpolation.NearestNeighbor);
            }
        }
    }

    private void DrawHalftone(CanvasDrawingSession session)
    {
        _halftoneBitmap ??= BuildHalftoneBitmap(session.Device);
        if (_halftoneBitmap == null) return;
        float width = _halftoneBitmap.SizeInPixels.Width;
        float height = _halftoneBitmap.SizeInPixels.Height;
        var source = new Rect(0, 0, width, height);
        for (float y = 0; y < _viewH; y += height)
        {
            for (float x = 0; x < _viewW; x += width)
            {
                session.DrawImage(_halftoneBitmap, new Rect(x, y, width, height), source,
                        9f / 255f, CanvasImageInterpolation.NearestNeighbor);
            }
        }
    }

    private void DrawFrameMarks(CanvasDrawingSession session)
    {
        Color mark = WithAlpha(_accentColor, 30f / 255f);
        const float inset = 22f;
        const float len = 34f;
        session.DrawLine(inset, inset, inset + len, inset, mark, 1.2f);
        session.DrawLine(inset, inset, inset, inset + len, mark, 1.2f);
        session.DrawLine(_viewW - inset, inset, _viewW - inset - len, inset, mark, 1.2f);
        session.DrawLine(_viewW - inset, inset, _viewW - inset, inset + len, mark, 1.2f);
        session.DrawLine(inset, _viewH - inset, inset + len, _viewH - inset, mark, 1.2f);
        session.DrawLine(inset, _viewH - inset, inset, _viewH - inset - len, mark, 1.2f);
        session.DrawLine(_viewW - inset, _viewH - inset, _viewW - inset - len, _viewH - inset, mark, 1.2f);
        session.DrawLine(_viewW - inset, _viewH - inset, _viewW - inset, _viewH - inset - len, mark, 1.2f);
    }

    private static CanvasBitmap? BuildNoiseBitmap(ICanvasResourceCreator creator)
    {
        try
        {
            const int w = 160;
            const int h = 90;
            var pixels = new byte[w * h * 4];
            var random = new JavaRandom(0xC0FFEE);
            for (int i = 0; i < w * h; i++)
            {
                byte value = (byte)(random.NextInt(256) & 0xFF);
                int offset = i * 4;
                // Android builds an ALPHA_8 bitmap: black, alpha = noise.
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = value;
            }
            return CanvasBitmap.CreateFromBytes(creator, pixels, w, h,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    96f, CanvasAlphaMode.Premultiplied);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CanvasBitmap? BuildHalftoneBitmap(ICanvasResourceCreator creator)
    {
        try
        {
            const int w = 8;
            const int h = 8;
            var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int offset = (y * w + x) * 4;
                    byte value = (byte)(x == 3 && y == 3 ? 255 : 0);
                    pixels[offset] = 0;
                    pixels[offset + 1] = 0;
                    pixels[offset + 2] = 0;
                    pixels[offset + 3] = value;
                }
            }
            return CanvasBitmap.CreateFromBytes(creator, pixels, w, h,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    96f, CanvasAlphaMode.Premultiplied);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
