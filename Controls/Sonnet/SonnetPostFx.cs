using System;
using System.IO;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
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
    /// <summary>
    /// Film grain. Kept subtle: on a monitor at arm's length the value that
    /// reads as texture on a TV reads as noise.
    /// </summary>
    public const float Grain = 0.045f;
    public const float Contrast = 0.06f;
    public const float Halftone = 0.16f;
    public const float RgbShift = 0.0018f;

    private PixelShaderEffect? _final;
    private GaussianBlurEffect? _blur;
    private ColorMatrixEffect? _fallback;
    private CanvasRenderTarget? _blurred;

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
                // Soft let the blur fade the edges into transparent black, which
                // showed up as a big black frame while the shots cross-fade.
                BorderMode = EffectBorderMode.Hard,
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
            // The blurred frame is flattened into a plain bitmap first. Handing
            // the blur effect straight to the pixel shader makes Direct2D give
            // it a padded intermediate surface, and D2DGetInputCoordinate(0)
            // then maps uv onto that padded rect: for as long as a FastBlur
            // transition runs, the left and top strips sample outside the
            // picture and come out as a hard dark border (measured 143 -> 20 at
            // 1351x972). A bitmap input keeps the mapping 1:1.
            CanvasRenderTarget? flat = EnsureBlurredTarget(scene);
            if (flat != null)
            {
                using (CanvasDrawingSession session2 = flat.CreateDrawingSession())
                {
                    session2.Clear(Colors.Black);
                    session2.DrawImage(_blur,
                            new Rect(0, 0, flat.SizeInPixels.Width, flat.SizeInPixels.Height),
                            sourceRect, 1f, CanvasImageInterpolation.Linear);
                }
                source = flat;
            }
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
        _blurred?.Dispose();
    }

    /// <summary>Reusable bitmap that the blur is flattened into.</summary>
    private CanvasRenderTarget? EnsureBlurredTarget(CanvasRenderTarget scene)
    {
        int width = (int)Math.Round((double)scene.SizeInPixels.Width);
        int height = (int)Math.Round((double)scene.SizeInPixels.Height);
        if (width < 2 || height < 2) return null;
        if (_blurred != null
            && (int)Math.Round((double)_blurred.SizeInPixels.Width) == width
            && (int)Math.Round((double)_blurred.SizeInPixels.Height) == height)
        {
            return _blurred;
        }
        _blurred?.Dispose();
        try
        {
            _blurred = new CanvasRenderTarget(scene.Device, width, height, 96f);
        }
        catch (Exception)
        {
            _blurred = null;
            return null;
        }
        return _blurred;
    }
}
