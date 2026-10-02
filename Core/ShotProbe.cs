using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BukaMusicDesktop.Core;

/// <summary>
/// Debug helper: renders a page (or one named element of it) into a PNG with
/// RenderTargetBitmap, so layout questions can be measured instead of guessed.
/// Invoked with <c--shot &lt;page&gt; &lt;host&gt; &lt;out.png&gt; [elementName]</c>.
/// </summary>
public static class ShotProbe
{
    public static async Task<bool> SaveAsync(UIElement element, string path)
    {
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            var pixels = await bitmap.GetPixelsAsync();
            byte[] bytes = pixels.ToArray();
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            using var stream = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)width, (uint)height, 96, 96, bytes);
            await encoder.FlushAsync();
            stream.Seek(0);

            using var file = File.Create(path);
            await stream.AsStreamForRead().CopyToAsync(file);
            LogBus.Info($"截图已保存：{path}（{width}x{height}）");
            return true;
        }
        catch (Exception ex)
        {
            LogBus.Warn("截图失败：" + ex.Message);
            return false;
        }
    }
}
