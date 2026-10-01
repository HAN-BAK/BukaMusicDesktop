using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using BukaMusicDesktop.Controls;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace BukaMusicDesktop.Core;

/// <summary>
/// Development helper: renders the lyric PV for the track a device is playing
/// to PNG files so the renderer can be checked without a screen. Invoked with
/// <c>BukaMusicDesktop.exe --lyrics-probe &lt;outputDir&gt; [host]</c>.
/// </summary>
public static class LyricsProbe
{
    /// <summary>
    /// Renders a synthetic frame through the print stack. Used to check the
    /// shader's edge behaviour (distortion / dispersion must not darken the
    /// frame's borders) without needing a device.
    /// </summary>
    public static async System.Threading.Tasks.Task RunShaderTestAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var device = CanvasDevice.GetSharedDevice();
        using var postFx = new SonnetPostFx();
        const int width = 1600;
        const int height = 958;
        using var scene = new CanvasRenderTarget(device, width, height, 96f);
        using (CanvasDrawingSession session = scene.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 128, 128, 128));
            const float bar = 40f;
            session.FillRectangle(0, 0, width, bar, Colors.Red);
            session.FillRectangle(0, height - bar, width, bar, Colors.Lime);
            session.FillRectangle(0, 0, bar, height, Colors.Blue);
            session.FillRectangle(width - bar, 0, bar, height, Colors.Yellow);
            for (int i = 1; i < 8; i++)
            {
                float x = width * i / 8f;
                session.DrawLine(x, 0, x, height, Colors.White, 4f);
            }
        }
        using var output = new CanvasRenderTarget(device, width, height, 96f);
        using (CanvasDrawingSession session = output.CreateDrawingSession())
        {
            session.Clear(Colors.Black);
            postFx.Draw(session, scene, 0f, new Rect(0, 0, width, height));
        }
        await scene.SaveAsync(Path.Combine(outputDirectory, "shader_raw.png"),
                CanvasBitmapFileFormat.Png);
        await output.SaveAsync(Path.Combine(outputDirectory, "shader_out.png"),
                CanvasBitmapFileFormat.Png);
    }

    public static async System.Threading.Tasks.Task RunAsync(string outputDirectory, string host)
    {
        Directory.CreateDirectory(outputDirectory);
        var client = new BukaClient(new DeviceInfo { Ip = host, Port = 8080 });
        LyricSet lyrics = await client.GetLyricsAsync();
        byte[]? cover = await client.GetCoverAsync();

        var device = CanvasDevice.GetSharedDevice();
        using var stage = new SonnetStage();
        stage.EnsureResources(device);
        using var postFx = new SonnetPostFx();
        stage.SetCanvasPostEffectsEnabled(!postFx.Available);
        File.WriteAllText(Path.Combine(outputDirectory, "shader.txt"),
                postFx.Available ? "pixel shader active" : "pixel shader UNAVAILABLE (fallback)");

        var lines = new List<SonnetLine>();
        foreach (LyricLineItem item in lyrics.Lines)
        {
            var words = new List<SonnetWord>();
            foreach (LyricWord word in item.Words)
            {
                words.Add(new SonnetWord { StartMs = word.StartMs, EndMs = word.EndMs, Text = word.Text });
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
        stage.SetLyrics(lines, lyrics.Seed);
        File.WriteAllText(Path.Combine(outputDirectory, "program.txt"),
                $"lines={lines.Count}\n" + stage.Describe(40_000L));

        CanvasBitmap? coverBitmap = null;
        if (cover != null && cover.Length > 0)
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(cover);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            coverBitmap = await CanvasBitmap.LoadAsync(device, stream);
            stage.SetBackgroundArt(coverBitmap);
        }
        if (stage.BackdropForDebug != null)
        {
            await stage.BackdropForDebug.SaveAsync(
                    Path.Combine(outputDirectory, "backdrop.png"), CanvasBitmapFileFormat.Png);
        }

        const int sceneWidth = 1280;
        const int sceneHeight = 720;
        stage.SetViewport(SonnetStage.VW, SonnetStage.VH);
        stage.SetViewPixelWidth(sceneWidth);

        using var scene = new CanvasRenderTarget(device, sceneWidth, sceneHeight, 96f);
        using var output = new CanvasRenderTarget(device, sceneWidth, sceneHeight, 96f);
        // Sample eight moments spread over the song's own timeline.
        long first = long.MaxValue;
        long last = 0L;
        foreach (SonnetLine line in lines)
        {
            first = Math.Min(first, line.StartMs);
            last = Math.Max(last, line.EndMs);
        }
        if (lines.Count == 0)
        {
            first = 0L;
            last = 30_000L;
        }
        var stops = new List<long>();
        for (int i = 0; i < 8; i++)
        {
            stops.Add(first + (last - first) * i / 8 + 400L);
        }
        int index = 0;
        foreach (long position in stops)
        {
            long now = SonnetStage.Now();
            stage.AdvanceFrameCounter();
            using (CanvasDrawingSession session = scene.CreateDrawingSession())
            {
                session.Clear(Colors.Transparent);
                session.Transform = Matrix3x2.CreateScale(sceneWidth / SonnetStage.VW);
                stage.DrawSceneFrame(session, position, now);
            }
            using (CanvasDrawingSession session = output.CreateDrawingSession())
            {
                session.Clear(Colors.Black);
                postFx.Draw(session, scene, 0f, new Rect(0, 0, sceneWidth, sceneHeight));
            }
            string path = Path.Combine(outputDirectory, $"frame{index:D2}_{position}.png");
            await output.SaveAsync(path, CanvasBitmapFileFormat.Png);
            await scene.SaveAsync(Path.Combine(outputDirectory, $"raw{index:D2}_{position}.png"),
                    CanvasBitmapFileFormat.Png);
            index++;
        }
    }

    /// <summary>
    /// Continuous-playback probe. Renders the lyric PV frame by frame with the
    /// wall clock driving the audio clock, exactly like the app does, so shot
    /// transitions, the backdrop cross-fade and the print stack all run with
    /// their real timing. Every frame's border brightness is measured and the
    /// frames that come out with a dark edge are written to PNG.
    /// </summary>
    public static async System.Threading.Tasks.Task RunSequenceAsync(
            string outputDirectory, string host, long startMs, long endMs,
            int width, int height, int stride)
    {
        Directory.CreateDirectory(outputDirectory);
        var client = new BukaClient(new DeviceInfo { Ip = host, Port = 8080 });
        LyricSet lyrics = await client.GetLyricsAsync();
        byte[]? cover = await client.GetCoverAsync();
        DeviceState? state = await client.GetStateAsync();

        var device = CanvasDevice.GetSharedDevice();
        using var stage = new SonnetStage();
        stage.EnsureResources(device);
        using var postFx = new SonnetPostFx();
        stage.SetCanvasPostEffectsEnabled(!postFx.Available);

        var lines = new List<SonnetLine>();
        foreach (LyricLineItem item in lyrics.Lines)
        {
            var words = new List<SonnetWord>();
            foreach (LyricWord word in item.Words)
            {
                words.Add(new SonnetWord { StartMs = word.StartMs, EndMs = word.EndMs, Text = word.Text });
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
        if (state != null)
        {
            stage.SetHints(new List<string> { state.Title, state.Artist, state.Album });
        }
        stage.SetLyrics(lines, lyrics.Seed);

        CanvasBitmap? coverBitmap = null;
        if (cover != null && cover.Length > 0)
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(cover);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            coverBitmap = await CanvasBitmap.LoadAsync(device, stream);
            stage.SetBackgroundArt(coverBitmap);
        }

        stage.SetViewport(SonnetStage.VW, SonnetStage.VW * height / (float)width);
        stage.SetViewPixelWidth(width);

        using var scene = new CanvasRenderTarget(device, width, height, 96f);
        using var frame = new CanvasRenderTarget(device, width, height, 96f);

        var csv = new StringBuilder(
                "wallMs,posMs,blur,s_left,s_top,s_right,s_bottom,s_center,s_left0,s_left1," +
                "f_left,f_top,f_right,f_bottom,f_center,f_left0,f_left1\n");
        var watch = Stopwatch.StartNew();
        int index = 0;
        int saved = 0;
        long span = Math.Max(1L, endMs - startMs);
        while (true)
        {
            long elapsed = watch.ElapsedMilliseconds;
            if (elapsed > span) break;
            long pos = startMs + elapsed;

            stage.SetPlaybackState(pos, true);
            stage.AdvanceFrameCounter();
            long now = SonnetStage.Now();
            using (CanvasDrawingSession session = scene.CreateDrawingSession())
            {
                session.Clear(Colors.Transparent);
                session.Transform = Matrix3x2.CreateScale(width / SonnetStage.VW);
                stage.DrawSceneFrame(session, pos, now);
            }
            float blur = stage.CurrentBlurStrength();
            using (CanvasDrawingSession session = frame.CreateDrawingSession())
            {
                session.Clear(Colors.Black);
                postFx.Draw(session, scene, blur,
                        new Rect(0, 0, width, height));
            }
            index++;
            if (index % Math.Max(1, stride) != 0) continue;

            EdgeMetrics sceneMetrics = Measure(scene, width, height);
            EdgeMetrics frameMetrics = Measure(frame, width, height);
            csv.Append($"{elapsed},{pos},{blur:F2},")
               .Append(sceneMetrics.ToCsv()).Append(',')
               .Append(frameMetrics.ToCsv()).Append('\n');

            if (frameMetrics.Hard && saved < 40)
            {
                await frame.SaveAsync(
                        Path.Combine(outputDirectory, $"band_{saved:D2}_{pos}_post.png"),
                        CanvasBitmapFileFormat.Png);
                await scene.SaveAsync(
                        Path.Combine(outputDirectory, $"band_{saved:D2}_{pos}_scene.png"),
                        CanvasBitmapFileFormat.Png);
                saved++;
            }
            if (index % 600 == 0)
            {
                await frame.SaveAsync(
                        Path.Combine(outputDirectory, $"tick_{pos}.png"),
                        CanvasBitmapFileFormat.Png);
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory, "sequence.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(outputDirectory, "sequence.txt"),
                $"frames={index} saved={saved} viewport={SonnetStage.VW}x" +
                $"{SonnetStage.VW * height / (float)width:F2} scene={width}x{height}");
    }

    private readonly struct EdgeMetrics
    {
        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Center;
        public readonly float Left0;
        public readonly float Left1;
        public readonly bool Hard;

        public EdgeMetrics(float left, float top, float right, float bottom,
                           float center, float left0, float left1, bool hard)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            Center = center;
            Left0 = left0;
            Left1 = left1;
            Hard = hard;
        }

        public string ToCsv() =>
                $"{Left:F1},{Top:F1},{Right:F1},{Bottom:F1},{Center:F1},{Left0:F1},{Left1:F1}";
    }

    /// <summary>
    /// Measures a rendered frame's borders. "Hard" means a strip along the edge
    /// is far darker than the neighbouring columns, which is the signature of a
    /// geometric border rather than dark picture content.
    /// </summary>
    private static EdgeMetrics Measure(ICanvasImage image, int width, int height)
    {
        Color[] pixels = ((CanvasBitmap)image).GetPixelColors();
        float left = BandLumaAt(pixels, width, height, 0, height / 5, width / 60, height * 4 / 5);
        float top = BandLumaAt(pixels, width, height, width / 5, 0, width * 4 / 5, height / 60);
        float right = BandLumaAt(pixels, width, height,
                width - width / 60, height / 5, width, height * 4 / 5);
        float bottom = BandLumaAt(pixels, width, height,
                width / 5, height - height / 60, width * 4 / 5, height);
        float center = BandLumaAt(pixels, width, height,
                width * 2 / 5, height * 2 / 5, width * 3 / 5, height * 3 / 5);
        float left0 = ColumnLuma(pixels, width, height, 1);
        float left1 = ColumnLuma(pixels, width, height, Math.Max(2, width / 40));
        bool hard = left < center * 0.7f && left1 > left0 * 1.6f + 12f;
        return new EdgeMetrics(left, top, right, bottom, center, left0, left1, hard);
    }

    private static float BandLumaAt(Color[] pixels, int width, int height,
                                    int x0, int y0, int x1, int y1)
    {
        double sum = 0;
        int count = 0;
        int stepX = Math.Max(1, (x1 - x0) / 24);
        int stepY = Math.Max(1, (y1 - y0) / 24);
        for (int y = y0; y < y1 && y < height; y += stepY)
        {
            for (int x = x0; x < x1 && x < width; x += stepX)
            {
                Color c = pixels[y * width + x];
                sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                count++;
            }
        }
        return count == 0 ? 0f : (float)(sum / count);
    }

    private static float ColumnLuma(Color[] pixels, int width, int height, int x)
    {
        return BandLumaAt(pixels, width, height, x, height / 5, x + 1, height * 4 / 5);
    }

    private static float RowLuma(Color[] pixels, int width, int height, int y)
    {
        return BandLumaAt(pixels, width, height, width / 5, y, width * 4 / 5, y + 1);
    }
}
