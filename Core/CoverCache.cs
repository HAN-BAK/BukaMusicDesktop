using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace BukaMusicDesktop.Core;

/// <summary>
/// Album / artist cover cache for the library grid.
///
/// <p>The bytes are fetched off the UI thread (the device serves one small JPEG
/// per album), but the <see cref="BitmapImage"/> itself must be created on the
/// UI thread - WinUI image objects are thread affine, and building them on a
/// worker thread silently yields no picture at all. So fetching and decoding
/// are split: <see cref="FetchAsync"/> can run anywhere, <see cref="DecodeAsync"/>
/// is called from the page.
/// </summary>
public sealed class CoverCache
{
    public static CoverCache Shared { get; } = new();

    private readonly Dictionary<string, BitmapImage> _images = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(3, 3);

    public BitmapImage? TryGet(string path)
    {
        lock (_images)
        {
            return _images.TryGetValue(path, out BitmapImage? image) ? image : null;
        }
    }

    public bool IsMissing(string path)
    {
        lock (_images)
        {
            return _missing.Contains(path);
        }
    }

    public void Put(string path, BitmapImage image)
    {
        lock (_images)
        {
            _images[path] = image;
        }
    }

    public void MarkMissing(string path)
    {
        lock (_images)
        {
            _missing.Add(path);
        }
    }

    /// <summary>Fetches the cover bytes; safe to await from any thread.</summary>
    public async Task<byte[]?> FetchAsync(BukaClient client, string path)
    {
        if (string.IsNullOrEmpty(path) || IsMissing(path)) return null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsMissing(path)) return null;
            return await client.GetCoverAsync(path).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Decodes a cover; call this on the UI thread.</summary>
    public static async Task<BitmapImage?> DecodeAsync(byte[] bytes)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = 256 };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
