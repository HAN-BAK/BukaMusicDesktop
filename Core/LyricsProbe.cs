using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
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
}
