using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BukaMusicDesktop.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace BukaMusicDesktop.Controls;

/// <summary>
/// The lyric PV surface: the desktop port of the Android LyricsActivity's GL
/// layer. The Sonnet scene is rendered into an offscreen target, run through
/// the print-stack pixel shader and composited into the page.
/// </summary>
public sealed class LyricsStage : Grid
{
    /// <summary>The scene is rendered at the panel resolution, capped like the Android layer.</summary>
    /// <summary>
    /// Upper bound for the offscreen scene. It used to be 1600, which meant a
    /// normal 2K/4K window upscaled the picture (and its film grain with it),
    /// so the lyrics looked soft and noisy. Rendering close to the window's own
    /// pixel size keeps the grain fine and the text sharp.
    /// </summary>
    private const int MaxSceneWidth = 2560;
    private const float FadeOutMs = 160f;
    private const float FadeInMs = 220f;

    private readonly CanvasControl _canvas = new();
    private readonly SonnetStage _stage = new();
    private readonly SonnetPostFx _postFx = new();

    private CanvasRenderTarget? _scene;
    private CanvasRenderTarget? _final;
    private int _sceneWidth;
    private int _sceneHeight;
    private int _finalWidth;
    private int _finalHeight;
    private bool _ready;
    private static string? CaptureDirectory => Environment.GetEnvironmentVariable("BUKA_CAPTURE_DIR");

    private List<SonnetLine>? _pendingLines;
    private string _pendingSeed = "sonnet";
    private string _appliedKey = "";
    private bool _hasPending;
    private float _fade;
    private float _fadeTarget;

    private byte[]? _coverBytes;
    private bool _coverApplied;

    public LyricsStage()
    {
        Children.Add(_canvas);
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        _canvas.Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
        _canvas.Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Shown by the typographic "no lyrics" scene.</summary>
    public string EmptyHint
    {
        get => _emptyHint ?? Core.Loc.Current.Text("暂未找到歌词");
        set => _emptyHint = value;
    }

    private string? _emptyHint;

    // ------------------------------------------------------------------
    // Public inputs
    // ------------------------------------------------------------------

    public void SetLyrics(LyricSet lyrics)
    {
        var lines = new List<SonnetLine>();
        if (lyrics != null)
        {
            foreach (LyricLineItem item in lyrics.Lines)
            {
                var words = new List<SonnetWord>();
                foreach (LyricWord word in item.Words)
                {
                    words.Add(new SonnetWord
                    {
                        StartMs = word.StartMs,
                        EndMs = word.EndMs,
                        Text = word.Text,
                    });
                }
                lines.Add(new SonnetLine
                {
                    StartMs = item.StartMs,
                    EndMs = item.EndMs,
                    Text = item.Text,
                    Translation = item.Translation,
                    Words = words,
                });
            }
        }
        string seed = string.IsNullOrWhiteSpace(lyrics?.Seed) ? "sonnet" : lyrics!.Seed;
        string key = $"{seed}|{lines.Count}|{(lines.Count > 0 ? lines[0].Text : "")}|"
                     + (lines.Count > 0 ? lines[0].StartMs : 0);
        // Only override the stage's own (localized) default when the page set one.
        if (_emptyHint != null) _stage.NoLyricsText = _emptyHint;
        if (_appliedKey.Length == 0 || lines.Count == 0)
        {
            // First load (or an empty song): swap immediately.
            _appliedKey = key;
            _hasPending = false;
            _stage.SetLyrics(lines, seed);
            _fade = 1f;
            _fadeTarget = 1f;
        }
        else if (key != _appliedKey)
        {
            // Track change: fade the typography out, swap, fade back in, so the
            // scene never snaps between two songs' lyrics.
            _pendingLines = lines;
            _pendingSeed = seed;
            _hasPending = true;
            _fadeTarget = 0f;
        }
        _canvas.Invalidate();
    }

    public void SetHints(IEnumerable<string>? hints)
    {
        var list = hints == null ? new List<string>() : new List<string>(hints);
        _stage.SetHints(list);
        _canvas.Invalidate();
    }

    public void SetPlayback(int positionMs, bool playing)
    {
        _stage.SetPlaybackState(positionMs, playing);
    }

    public void SetContentAlpha(float alpha)
    {
        _stage.SetContentAlpha(alpha);
        _canvas.Invalidate();
    }

    public void SetCover(byte[]? bytes)
    {
        _coverBytes = bytes;
        _coverApplied = false;
        _ = ApplyCoverAsync();
    }

    private async System.Threading.Tasks.Task ApplyCoverAsync()
    {
        CanvasDevice? device = _canvas.Device;
        if (device == null) return;
        byte[]? bytes = _coverBytes;
        if (bytes == null || bytes.Length == 0)
        {
            _coverApplied = true;
            _stage.SetBackgroundArt(null);
            return;
        }
        try
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            CanvasBitmap bitmap = await CanvasBitmap.LoadAsync(device, stream);
            _coverApplied = true;
            _stage.SetBackgroundArt(bitmap);
            _canvas.Invalidate();
        }
        catch (Exception ex)
        {
            LogBus.Warn($"封面解码失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Frame loop
    // ------------------------------------------------------------------

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _stage.EnsureResources(sender.Device);
        _stage.SetCanvasPostEffectsEnabled(!_postFx.Available);
        _stage.SetLowPower(false);
        _ready = true;
    }

    private void OnRendering(object? sender, object e)
    {
        bool animating = AdvanceFade();
        if (animating || _stage.WantsAnimationFrames())
        {
            _canvas.Invalidate();
        }
    }

    private bool AdvanceFade()
    {
        if (Math.Abs(_fade - _fadeTarget) < 0.004f)
        {
            _fade = _fadeTarget;
            if (_hasPending && _fade <= 0.01f)
            {
                _stage.SetLyrics(_pendingLines, _pendingSeed);
                _appliedKey = _pendingSeed + "|" + (_pendingLines?.Count ?? 0);
                _hasPending = false;
                _fadeTarget = 1f;
                return true;
            }
            return false;
        }
        float rate = _fadeTarget > _fade ? 1f / FadeInMs : 1f / FadeOutMs;
        float step = rate * 16f;
        if (_fade < _fadeTarget) _fade = Math.Min(_fadeTarget, _fade + step);
        else _fade = Math.Max(_fadeTarget, _fade - step);
        _stage.SetContentAlpha(_fade);
        return true;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDrawingSession session = args.DrawingSession;
        session.Clear(Colors.Black);
        if (!_ready) return;
        double dipWidth = sender.ActualWidth;
        double dipHeight = sender.ActualHeight;
        if (dipWidth < 8 || dipHeight < 8) return;

        float dpi = session.Dpi;
        int pixelWidth = (int)Math.Round(dipWidth * dpi / 96.0);
        int pixelHeight = (int)Math.Round(dipHeight * dpi / 96.0);
        if (pixelWidth > MaxSceneWidth)
        {
            pixelHeight = Math.Max(2, pixelHeight * MaxSceneWidth / pixelWidth);
            pixelWidth = MaxSceneWidth;
        }
        EnsureScene(sender.Device, pixelWidth, pixelHeight);
        if (_scene == null || _final == null) return;

        // The scene is authored against a 1280 unit wide frame; the vertical
        // field of view follows the screen's shape so nothing is letterboxed.
        _stage.SetViewport(SonnetStage.VW, SonnetStage.VW * pixelHeight / (float)pixelWidth);
        _stage.SetViewPixelWidth(pixelWidth);

        long now = SonnetStage.Now();
        _stage.AdvanceFrameCounter();
        long position = _stage.DisplayPositionMs();
        using (CanvasDrawingSession sceneSession = _scene.CreateDrawingSession())
        {
            sceneSession.Clear(Colors.Transparent);
            sceneSession.Transform = Matrix3x2.CreateScale(pixelWidth / SonnetStage.VW);
            _stage.DrawSceneFrame(sceneSession, position, now);
        }
        // Composite into the final buffer first (exactly like the Android GL
        // layer does) so what is captured is what is on screen.
        using (CanvasDrawingSession finalSession = _final.CreateDrawingSession())
        {
            finalSession.Clear(Colors.Black);
            _postFx.Draw(finalSession, _scene, _stage.CurrentBlurStrength(),
                    new Rect(0, 0, _finalWidth, _finalHeight));
        }
        session.DrawImage(_final, new Rect(0, 0, dipWidth, dipHeight));

        string? captureDirectory = CaptureDirectory;
        if (!string.IsNullOrEmpty(captureDirectory)
            && SonnetStage.Now() - _lastCaptureMs > 2500)
        {
            _lastCaptureMs = SonnetStage.Now();
            try
            {
                Directory.CreateDirectory(captureDirectory);
                _ = CaptureAsync(captureDirectory, _captureIndex++);
            }
            catch (Exception ex)
            {
                LogBus.Warn("截图失败：" + ex.Message);
            }
        }
    }

    private void EnsureScene(CanvasDevice device, int pixelWidth, int pixelHeight)
    {
        if (_scene != null && _sceneWidth == pixelWidth && _sceneHeight == pixelHeight) return;
        _scene?.Dispose();
        _scene = new CanvasRenderTarget(device, pixelWidth, pixelHeight, 96f);
        _sceneWidth = pixelWidth;
        _sceneHeight = pixelHeight;
        _final?.Dispose();
        _final = new CanvasRenderTarget(device, pixelWidth, pixelHeight, 96f);
        _finalWidth = pixelWidth;
        _finalHeight = pixelHeight;
        // The backdrop is built for the scene size, so rebuild it.
        if (!_coverApplied && _coverBytes != null) _ = ApplyCoverAsync();
    }

    private long _lastCaptureMs;
    private int _captureIndex;

    /// <summary>Debug-only PNG dump; fire and forget so the UI thread never blocks.</summary>
    private async System.Threading.Tasks.Task CaptureAsync(string directory, int index)
    {
        try
        {
            if (_final != null)
            {
                await _final.SaveAsync(Path.Combine(directory, $"composite_{index:D3}.png"),
                        CanvasBitmapFileFormat.Png);
            }
            if (_scene != null)
            {
                await _scene.SaveAsync(Path.Combine(directory, $"raw_{index:D3}.png"),
                        CanvasBitmapFileFormat.Png);
            }
        }
        catch (Exception ex)
        {
            LogBus.Warn("截图失败：" + ex.Message);
        }
    }
}
