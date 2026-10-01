using System;
using System.IO;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// Desktop twin of the Android LyricsGlView: the Canvas scene is rendered into
/// an offscreen target, blurred during shot transitions, then composited
/// through the same print stack shader (radial lens distortion, chromatic
/// dispersion, vignette, grain, halftone, contrast).
/// </summary>
public sealed class SonnetPostFx : IDisposable
{
    public const float Distortion = 0.10f;
    public const float Dispersion = 0.0022f;
    public const float Vignette = 0.35f;
    public const float Grain = 0.07f;
    public const float Contrast = 0.06f;
    public const float Halftone = 0.16f;
    public const float RgbShift = 0.0018f;

    private PixelShaderEffect? _final;
    private GaussianBlurEffect? _blur;
    private ColorMatrixEffect? _fallback;

    public SonnetPostFx()
    {
        try
        {
            var assembly = typeof(SonnetPostFx).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(
                    "BukaMusicDesktop.Shaders.sonnet_final.bin");
            if (stream == null) return;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            _final = new PixelShaderEffect(buffer.ToArray())
            {
                Source1Mapping = SamplerCoordinateMapping.Unknown,
            };
            _blur = new GaussianBlurEffect
            {
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Balanced,
            };
        }
        catch (Exception)
        {
            _final?.Dispose();
            _final = null;
            _blur?.Dispose();
            _blur = null;
        }
    }

    /// <summary>True when the custom print-stack shader is available.</summary>
    public bool Available => _final != null;

    /// <summary>
    /// Composites the rendered scene into <paramref name="target"/>.
    /// <paramref name="blurStrength"/> is the Android blur strength in virtual
    /// pixels (0 = none).
    /// </summary>
    public void Draw(CanvasDrawingSession session, CanvasRenderTarget scene,
                     float blurStrength, Rect target)
    {
        ICanvasImage source = scene;
        var sourceRect = new Rect(0, 0, scene.SizeInPixels.Width, scene.SizeInPixels.Height);
        if (_blur != null && blurStrength > 0.05f)
        {
            _blur.BlurAmount = Math.Min(8f, blurStrength * 0.55f);
            _blur.Source = scene;
            source = _blur;
        }

        if (_final != null)
        {
            _final.Source1 = source;
            _final.Properties["uParams0"] = new Vector4(Distortion, Dispersion, Vignette, Grain);
            _final.Properties["uParams1"] = new Vector4(Halftone, Contrast, RgbShift,
                    (float)(SonnetStage.Now() % 100000L) / 1000f);
            _final.Properties["uParams2"] = new Vector4(1f, 1f, 0f, 0f);
            _final.Properties["uParams3"] = new Vector4(
                    0.5f / Math.Max(1f, (float)scene.SizeInPixels.Width),
                    0.5f / Math.Max(1f, (float)scene.SizeInPixels.Height), 0f, 0f);
            session.DrawImage(_final, target, sourceRect, 1f, CanvasImageInterpolation.Linear);
            return;
        }

        // Shader unavailable (very old Direct2D): keep the frame usable with a
        // plain composite plus a mild contrast pass.
        _fallback ??= new ColorMatrixEffect();
        _fallback.Source = source;
        session.DrawImage(_fallback, target, sourceRect, 1f, CanvasImageInterpolation.Linear);
    }

    public void Dispose()
    {
        _final?.Dispose();
        _blur?.Dispose();
    }
}
