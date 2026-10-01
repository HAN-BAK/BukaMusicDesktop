using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BukaMusicDesktop.Core;

/// <summary>Thin HTTP client for the device control API.</summary>
public sealed class BukaClient : IDisposable
{
    private static readonly HttpClient Http = CreateClient();

    private readonly DeviceInfo _device;

    public DeviceInfo Device => _device;
    public string BaseUrl => $"http://{_device.Ip}:{_device.Port}";
    public string CoverUrl => $"{BaseUrl}/api/cover";
    public string WebUrl => $"{BaseUrl}/";

    public BukaClient(DeviceInfo device)
    {
        _device = device;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BukaMusicDesktop/1.0");
        return client;
    }

    public async Task<DeviceInfo?> GetInfoAsync(CancellationToken token = default)
    {
        var json = await GetAsync("/api/info", token).ConfigureAwait(false);
        if (json == null) return null;
        return ParseDevice(json);
    }

    public async Task<DeviceState?> GetStateAsync(CancellationToken token = default)
    {
        var json = await GetAsync("/api/state", token).ConfigureAwait(false);
        if (json == null) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new DeviceState
        {
            Source = Str(root, "source"),
            Playing = Bool(root, "playing"),
            Title = Str(root, "title"),
            Artist = Str(root, "artist"),
            Album = Str(root, "album"),
            Path = Str(root, "path"),
            PositionMs = Int(root, "positionMs"),
            DurationMs = Int(root, "durationMs"),
            Mode = Str(root, "mode"),
            StatusText = Str(root, "statusText"),
            ClientName = Str(root, "clientName"),
            HasCover = Bool(root, "hasCover"),
            Volume = Int(root, "volume"),
            VolumeMax = Math.Max(1, Int(root, "volumeMax")),
            Screen = Str(root, "screen").Length == 0 ? "main" : Str(root, "screen"),
        };
    }

    public async Task<AppSettings?> GetSettingsAsync(CancellationToken token = default)
    {
        var json = await GetAsync("/api/settings", token).ConfigureAwait(false);
        if (json == null) return null;
        return ParseSettings(json);
    }

    public async Task<bool> ApplySettingsAsync(object payload, CancellationToken token = default)
    {
        var body = JsonSerializer.Serialize(payload);
        var response = await PostAsync("/api/settings", body, token).ConfigureAwait(false);
        return response != null;
    }

    public async Task<List<TrackItem>> GetLibraryAsync(CancellationToken token = default)
    {
        var result = new List<TrackItem>();
        var json = await GetAsync("/api/library", token, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (json == null) return result;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tracks", out var tracks)) return result;
        foreach (var track in tracks.EnumerateArray())
        {
            result.Add(new TrackItem
            {
                Path = Str(track, "path"),
                RelativePath = Str(track, "relativePath"),
                Name = Str(track, "name"),
                Title = Str(track, "title"),
                Artist = Str(track, "artist"),
                Album = Str(track, "album"),
                DurationMs = Long(track, "durationMs"),
                SizeBytes = Long(track, "sizeBytes"),
            });
        }
        return result;
    }

    public async Task<bool> ControlAsync(string action, object? extra = null,
                                         CancellationToken token = default)
    {
        var payload = new Dictionary<string, object> { ["action"] = action };
        if (extra is IDictionary<string, object> map)
        {
            foreach (var pair in map) payload[pair.Key] = pair.Value;
        }
        var body = JsonSerializer.Serialize(payload);
        var response = await PostAsync("/api/control", body, token).ConfigureAwait(false);
        return response != null && !response.Contains("\"error\"");
    }

    /// <summary>
    /// Same as <see cref="ControlAsync"/> but returns the raw JSON answer, for
    /// actions whose result matters (equalizer presets: saved / duplicate /
    /// imported count).
    /// </summary>
    public async Task<JsonElement?> ControlRawAsync(string action, object? extra = null,
                                                    CancellationToken token = default)
    {
        var payload = new Dictionary<string, object> { ["action"] = action };
        if (extra is IDictionary<string, object> map)
        {
            foreach (var pair in map) payload[pair.Key] = pair.Value;
        }
        var body = JsonSerializer.Serialize(payload);
        var response = await PostAsync("/api/control", body, token).ConfigureAwait(false);
        if (response == null || response.Contains("\"error\"")) return null;
        try
        {
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<int> DeleteAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        var body = JsonSerializer.Serialize(new { paths });
        var response = await PostAsync("/api/delete", body, token).ConfigureAwait(false);
        if (response == null) return -1;
        using var doc = JsonDocument.Parse(response);
        return Int(doc.RootElement, "deleted");
    }

    // ------------------------------------------------------------------
    // Multi-room + lyrics
    // ------------------------------------------------------------------

    public async Task<MultiRoomSnapshot> GetMultiRoomAsync(CancellationToken token = default)
    {
        var snapshot = new MultiRoomSnapshot();
        var json = await GetAsync("/api/multicast", token).ConfigureAwait(false);
        if (json == null) return snapshot;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        snapshot.Active = Bool(root, "active");
        if (root.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
        {
            foreach (var target in targets.EnumerateArray())
            {
                var name = target.GetString();
                if (!string.IsNullOrWhiteSpace(name)) snapshot.Targets.Add(name!);
            }
        }
        if (root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
        {
            foreach (var device in devices.EnumerateArray())
            {
                var addresses = new List<string>();
                if (device.TryGetProperty("addresses", out var list)
                    && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var address in list.EnumerateArray())
                    {
                        var value = address.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) addresses.Add(value!);
                    }
                }
                snapshot.Devices.Add(new MultiRoomDevice
                {
                    Name = Str(device, "name"),
                    Port = Int(device, "port"),
                    Addresses = addresses.ToArray(),
                    Selected = snapshot.Targets.Contains(Str(device, "name")),
                });
            }
        }
        return snapshot;
    }

    public async Task<bool> SetMultiRoomAsync(IEnumerable<string> names,
                                              CancellationToken token = default)
    {
        var body = JsonSerializer.Serialize(new { devices = names });
        var response = await PostAsync("/api/multicast", body, token).ConfigureAwait(false);
        return response != null && !response.Contains("\"error\"");
    }

    public async Task<LyricSet> GetLyricsAsync(CancellationToken token = default)
    {
        var set = new LyricSet();
        var json = await GetAsync("/api/lyrics", token, TimeSpan.FromSeconds(25)).ConfigureAwait(false);
        if (json == null) return set;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("lyrics", out var payload)) return set;
        var seed = Str(payload, "seed");
        if (!string.IsNullOrWhiteSpace(seed)) set.Seed = seed;
        if (!payload.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return set;
        }
        foreach (var item in array.EnumerateArray())
        {
            var words = new List<LyricWord>();
            if (item.TryGetProperty("w", out var wordArray) && wordArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var word in wordArray.EnumerateArray())
                {
                    words.Add(new LyricWord
                    {
                        StartMs = Long(word, "s"),
                        EndMs = Long(word, "e"),
                        Text = Str(word, "t"),
                    });
                }
            }
            set.Lines.Add(new LyricLineItem
            {
                StartMs = Long(item, "s"),
                EndMs = Long(item, "e"),
                Text = Str(item, "t"),
                Translation = Str(item, "tr"),
                Words = words,
            });
        }
        return set;
    }

    /// <summary>
    /// Uploads one file and reports progress through the callback. The result
    /// carries the device's own message, exactly like the web upload page shows
    /// it - that is where "同名覆盖 / 剩余空间不足 80MB" is decided, and the
    /// console has to display the same wording instead of a bare failure.
    /// </summary>
    public async Task<UploadOutcome> UploadAsync(string filePath, IProgress<double>? progress = null,
                                                 CancellationToken token = default)
    {
        string name = Path.GetFileName(filePath);
        try
        {
            var stream = new ProgressStream(File.OpenRead(filePath), progress);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            HttpRequestMessage request = new(HttpMethod.Post, $"{BaseUrl}/upload")
            {
                Content = content,
            };
            request.Headers.Add("X-File-Name", Uri.EscapeDataString(name));

            using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            progress?.Report(1.0);
            // The endpoint answers with "OK:<message>" or "ERR:<message>"; the
            // web page strips the same two prefixes before showing a toast.
            var trimmed = text.Trim();
            bool ok = trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            string message = trimmed.Length > 3
                    ? trimmed[3..].TrimStart(':', ' ')
                    : trimmed;
            if (!ok)
            {
                LogBus.Warn($"上传 {name} 失败：{message}");
            }
            return new UploadOutcome(ok, message);
        }
        catch (Exception ex)
        {
            LogBus.Error($"上传 {name} 失败：{ex.Message}");
            return new UploadOutcome(false, ex.Message);
        }
    }

    /// <summary>Forwards the bytes sent so the console's bar can move.</summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly IProgress<double>? _progress;
        private readonly long _length;
        private long _sent;

        public ProgressStream(Stream inner, IProgress<double>? progress)
        {
            _inner = inner;
            _progress = progress;
            _length = inner.CanSeek ? inner.Length : 0L;
        }

        public override bool CanRead => _inner.CanRead;
        // Seek and length have to be forwarded: StreamContent derives the
        // request's Content-Length from them, and the device's upload handler
        // reads the body by Content-Length. Reporting "not seekable" made
        // HttpClient send the body chunked, so the device saw an empty file and
        // answered "文件名或内容为空".
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.CanSeek ? _inner.Length : _length;
        public override long Position
        {
            get => _inner.CanSeek ? _inner.Position : _sent;
            set
            {
                if (!_inner.CanSeek) throw new NotSupportedException();
                _inner.Position = value;
                _sent = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                _sent += read;
                if (_length > 0) _progress?.Report(Math.Min(1.0, _sent / (double)_length));
            }
            return read;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!_inner.CanSeek) throw new NotSupportedException();
            long position = _inner.Seek(offset, origin);
            _sent = position;
            return position;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Cover art of the current track, or - when a path is given - of that
    /// library file (album / artist tiles).
    /// </summary>
    public Task<byte[]?> GetCoverAsync(string? path = null, CancellationToken token = default)
        => GetBytesAsync(string.IsNullOrEmpty(path)
            ? "/api/cover"
            : "/api/cover?path=" + Uri.EscapeDataString(path), token);

    // ------------------------------------------------------------------

    private async Task<string?> GetAsync(string path, CancellationToken token,
                                         TimeSpan? timeout = null)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
            using var response = await Http.GetAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogBus.Warn($"{_device.EndPoint} {path} 请求失败：{ex.Message}");
            return null;
        }
    }

    private async Task<byte[]?> GetBytesAsync(string path, CancellationToken token)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await Http.GetAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> PostAsync(string path, string body, CancellationToken token)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(BaseUrl + path, content, cts.Token)
                .ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogBus.Warn($"{path} 返回 {(int)response.StatusCode}：{text.Trim()}");
                return null;
            }
            return text;
        }
        catch (Exception ex)
        {
            LogBus.Error($"{_device.EndPoint} {path} 失败：{ex.Message}");
            return null;
        }
    }

    private static DeviceInfo ParseDevice(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new DeviceInfo
        {
            Name = Str(root, "name"),
            Port = Int(root, "port") is var port and > 0 ? port : 8080,
            Model = Str(root, "model"),
            Android = Str(root, "android"),
            AppVersion = Str(root, "appVersion"),
            MusicFolder = Str(root, "musicFolder"),
            TrackCount = Int(root, "trackCount"),
            LastSeen = DateTime.Now,
        };
    }

    private static AppSettings ParseSettings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var settings = new AppSettings
        {
            DeviceName = Str(root, "deviceName"),
            MusicFolder = Str(root, "musicFolder"),
            PlayMode = Str(root, "playMode"),
            AutoPlayOnStart = Bool(root, "autoPlayOnStart"),
            Balance = Double(root, "balance"),
            ShowAppsButton = Bool(root, "showAppsButton"),
            BlurMode = Str(root, "blurMode"),
            Language = Str(root, "language"),
            OnlineLyrics = Bool(root, "onlineLyrics"),
        };
        if (root.TryGetProperty("eqFrequencies", out var frequencies)
            && frequencies.ValueKind == JsonValueKind.Array)
        {
            var values = new List<double>();
            foreach (var item in frequencies.EnumerateArray()) values.Add(item.GetDouble());
            settings.EqFrequencies = values.ToArray();
        }
        if (root.TryGetProperty("eqPresets", out var presets)
            && presets.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in presets.EnumerateArray())
            {
                var presetGains = new List<double>();
                if (item.TryGetProperty("gains", out var gainsArray)
                    && gainsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var gain in gainsArray.EnumerateArray()) presetGains.Add(gain.GetDouble());
                }
                settings.EqPresets.Add(new EqPreset
                {
                    Name = Str(item, "name"),
                    Gains = presetGains.ToArray(),
                });
            }
        }
        if (root.TryGetProperty("eqGains", out var gains) && gains.ValueKind == JsonValueKind.Array)
        {
            var values = new List<double>();
            foreach (var gain in gains.EnumerateArray()) values.Add(gain.GetDouble());
            settings.EqGains = values.ToArray();
        }
        return settings;
    }

    private static string Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => value.ToString(),
            }
            : "";

    private static int Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number : 0;

    private static long Long(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number : 0;

    private static bool Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           (value.ValueKind == JsonValueKind.True ||
            (value.ValueKind == JsonValueKind.String &&
             bool.TryParse(value.GetString(), out var parsed) && parsed));

    private static double Double(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number : 0d;

    public void Dispose()
    {
        // HttpClient is shared; nothing per-client to release.
    }
}
