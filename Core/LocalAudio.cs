using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace BukaMusicDesktop.Core;

/// <summary>
/// Plays a song of the connected device on this PC. The audio is streamed from
/// the device's /api/file endpoint (which supports range requests, so seeking
/// works), leaving the device's own playback untouched.
/// </summary>
public sealed class LocalAudio
{
    public static LocalAudio Instance { get; } = new();

    private readonly MediaPlayer _player = new() { AutoPlay = false };
    private readonly DispatcherQueueTimer? _timer;
    private BukaClient? _client;
    private List<TrackItem> _queue = new();
    private int _index = -1;
    private bool _seeking;

    /// <summary>Raised on the UI thread whenever the local player changes state.</summary>
    public event Action? Updated;

    public TrackItem? Current { get; private set; }

    public bool HasTrack => Current != null;

    public bool IsPlaying =>
        _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public TimeSpan Position => _player.PlaybackSession.Position;

    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

    /// <summary>Line shown in the transport card: title and artist.</summary>
    public string TrackText => Current == null
        ? Loc.Current.Text("未在播放")
        : Current.Display + " · " + Current.Artist;

    private LocalAudio()
    {
        _player.MediaEnded += (_, _) => Next();
        _player.MediaFailed += (_, args) =>
            LogBus.Warn("电脑播放失败：" + args.ErrorMessage);
        var queue = LogBus.Dispatcher;
        if (queue != null)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => Updated?.Invoke();
            _timer.Start();
        }
    }

    /// <summary>Starts playing one song; the list it came from becomes the queue.</summary>
    public void Play(BukaClient client, IEnumerable<TrackItem> queue, TrackItem track)
    {
        _client = client;
        _queue = queue.ToList();
        _index = _queue.FindIndex(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase));
        if (_index < 0)
        {
            _queue.Insert(0, track);
            _index = 0;
        }
        StartCurrent();
    }

    public void Toggle()
    {
        if (Current == null) return;
        if (IsPlaying)
        {
            _player.Pause();
            LogBus.Info("电脑播放：暂停");
        }
        else
        {
            // Nothing loaded yet (or the stream finished): start it again.
            if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.None)
            {
                StartCurrent();
            }
            else
            {
                _player.Play();
                LogBus.Info("电脑播放：继续");
            }
        }
        Updated?.Invoke();
    }

    public void Next()
    {
        if (_queue.Count == 0) return;
        _index = (_index + 1) % _queue.Count;
        StartCurrent();
    }

    public void Previous()
    {
        if (_queue.Count == 0) return;
        // Restart the song first, like the phone app does.
        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }
        _index = (_index - 1 + _queue.Count) % _queue.Count;
        StartCurrent();
    }

    public void Seek(TimeSpan position)
    {
        if (Current == null) return;
        _seeking = true;
        try
        {
            _player.PlaybackSession.Position = position;
        }
        finally
        {
            _seeking = false;
        }
        Updated?.Invoke();
    }

    public void Stop()
    {
        _player.Pause();
        _player.Source = null;
        Current = null;
        _queue.Clear();
        _index = -1;
        Updated?.Invoke();
    }

    /// <summary>True while the UI is dragging the progress slider.</summary>
    public bool Seeking
    {
        get => _seeking;
        set => _seeking = value;
    }

    private void StartCurrent()
    {
        if (_client == null || _index < 0 || _index >= _queue.Count) return;
        TrackItem track = _queue[_index];
        Current = track;
        string url = $"{_client.BaseUrl}/api/file?path={Uri.EscapeDataString(track.Path)}";
        try
        {
            _player.Source = MediaSource.CreateFromUri(new Uri(url));
            _player.Play();
            LogBus.Success($"电脑播放：{track.Display}");
        }
        catch (Exception ex)
        {
            LogBus.Error("电脑播放失败：" + ex.Message);
        }
        Updated?.Invoke();
    }
}
