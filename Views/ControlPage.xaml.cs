using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BukaMusicDesktop.Controls;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace BukaMusicDesktop.Views;

/// <summary>Now playing + transport controls for the selected device.</summary>
public sealed partial class ControlPage : Page
{
    private Session? _session;
    private bool _draggingProgress;
    private bool _draggingVolume;
    private int _lastSeekMs = -1;
    private bool _suppressVolume;
    private string _coverKey = "";
    /// <summary>Every five seconds the page checks that what it shows matches the
    /// device (metadata and cover) and corrects itself when it does not.</summary>
    private readonly DispatcherTimer _verifyTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public ControlPage()
    {
        InitializeComponent();
        // Slider 内部会把 Pointer 事件标记为已处理，普通事件不会冒泡到页面，
        // 所以这里用 AddHandler(handledEventsToo) 捕获拖动结束（含捕获丢失）。
        DragAwareSlider(ProgressSlider, OnProgressDragStart, OnProgressDragEnd);
        DragAwareSlider(VolumeSlider, () => _draggingVolume = true, OnVolumeDragEnd);
        DragAwareSlider(LocalProgressSlider, () => LocalAudio.Instance.Seeking = true, OnLocalProgressDragEnd);
        LocalAudio.Instance.Updated += OnLocalAudioUpdated;
        Loaded += (_, _) => UpdateLocalAudioUi();
        _verifyTimer.Tick += (_, _) => VerifyShownState();
        // Border.CornerRadius rounds the card, not the picture inside it: the
        // cover kept square corners and leaked a dark sliver along the arcs.
        RoundedClip.Attach(CoverImage, () => 7);
    }

    // ------------------------------------------------------------------
    // 电脑端本地播放
    // ------------------------------------------------------------------

    private void OnLocalAudioUpdated() => DispatcherQueue.TryEnqueue(UpdateLocalAudioUi);

    /// <summary>Switches the device between its lyric screen and playback screen.</summary>
    private async void OnDeviceScreen(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        bool lyrics = session.State.IsLyricsScreen;
        bool ok = await session.Client.ControlAsync(lyrics ? "openMain" : "openLyrics");
        LogBus.Info(ok
            ? (lyrics ? "已请求设备切回播放界面" : "已请求设备打开歌词界面")
            : "切换设备界面失败");
    }

    /// <summary>Mirrors the local player's state into the transport card.</summary>
    private void UpdateLocalAudioUi()
    {
        var audio = LocalAudio.Instance;
        LocalTrackText.Text = audio.TrackText;
        LocalPlayIcon.Glyph = audio.IsPlaying ? "\uE769" : "\uE768";
        LocalPrevButton.IsEnabled = audio.HasTrack;
        LocalPlayButton.IsEnabled = audio.HasTrack;
        LocalNextButton.IsEnabled = audio.HasTrack;
        if (audio.Seeking) return;
        double total = audio.Duration.TotalMilliseconds;
        double position = audio.Position.TotalMilliseconds;
        LocalProgressSlider.Maximum = total > 0 ? total : 1000;
        LocalProgressSlider.Value = total > 0 ? Math.Min(position, total) : 0;
        LocalPositionText.Text = ShortTime(position);
        LocalDurationText.Text = ShortTime(total);
    }

    private static string ShortTime(double ms)
    {
        if (ms <= 0) return "0:00";
        var span = TimeSpan.FromMilliseconds(ms);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }

    private void OnLocalToggle(object sender, RoutedEventArgs e) => LocalAudio.Instance.Toggle();

    private void OnLocalPrevious(object sender, RoutedEventArgs e) => LocalAudio.Instance.Previous();

    private void OnLocalNext(object sender, RoutedEventArgs e) => LocalAudio.Instance.Next();

    private void OnLocalProgressChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!LocalAudio.Instance.Seeking) return;
        LocalPositionText.Text = ShortTime(e.NewValue);
    }

    private void OnLocalProgressDragEnd()
    {
        var audio = LocalAudio.Instance;
        double target = LocalProgressSlider.Value;
        audio.Seeking = false;
        audio.Seek(TimeSpan.FromMilliseconds(target));
    }

    private static void DragAwareSlider(Slider slider, Action? onStart, Action onEnd)
    {
        slider.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => onStart?.Invoke()), true);
        slider.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => onEnd()), true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => onEnd()), true);
        slider.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler((_, _) => onEnd()), true);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _session = e.Parameter as Session;
        if (_session == null) return;
        _session.StateUpdated += OnStateUpdated;
        _session.ConnectionChanged += OnConnectionChanged;
        _verifyTimer.Start();
        OnStateUpdated();
        _ = RefreshCoverAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _verifyTimer.Stop();
        if (_session != null)
        {
            _session.StateUpdated -= OnStateUpdated;
            _session.ConnectionChanged -= OnConnectionChanged;
        }
        _session = null;
    }

    private void OnConnectionChanged(bool connected)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
        SourceText.Text = connected
            ? _session?.State.SourceText
            : Loc.Current.Text("连接中断");
        });
    }

    private void OnStateUpdated()
    {
        var session = _session;
        if (session == null) return;
        DispatcherQueue.TryEnqueue(() => ApplyState(session));
    }

    /// <summary>
    /// Writes the session's current state into the page. Also used by the five
    /// second check below, which re-applies it when the screen and the device
    /// disagree (a dropped update used to leave stale text on screen).
    /// </summary>
    private void ApplyState(Session session)
    {
        {
            var state = session.State;
            SourceText.Text = state.SourceText;
        StatusText.Text = Loc.Current.DeviceStatus(state.StatusText);
            TitleText.Text = string.IsNullOrWhiteSpace(state.Title) ? "—" : state.Title;
            ArtistText.Text = state.Artist;
            AlbumText.Text = state.Album;
            PositionText.Text = state.PositionText;
            DurationText.Text = state.DurationText;
            PlayIcon.Glyph = state.Playing ? "\uE769" : "\uE768";

            // AirPlay 播放的进度由发送端控制，这里隐藏进度条（保留上一首/暂停/下一首遥控）。
            var airPlay = state.Source == "AIRPLAY";
            ProgressRow.Visibility = airPlay ? Visibility.Collapsed : Visibility.Visible;
            AirPlayHint.Visibility = airPlay ? Visibility.Visible : Visibility.Collapsed;

            if (!_draggingProgress && state.DurationMs > 0)
            {
                var value = state.PositionMs / (double)state.DurationMs * 1000d;
                ProgressSlider.Value = Math.Clamp(value, 0, 1000);
            }

            if (!_draggingVolume)
            {
                _suppressVolume = true;
                VolumeSlider.Maximum = Math.Max(1, state.VolumeMax);
                VolumeSlider.Value = state.Volume;
                VolumeText.Text = Percent(state.Volume, state.VolumeMax);
                _suppressVolume = false;
            }
            // 设备显示歌词页时按钮变成「播放界面」，点一下切回主播放界面。
            DeviceScreenButton.Content = Loc.Current.Text(
                state.IsLyricsScreen ? "播放界面" : "歌词界面");
        }

        _ = RefreshCoverAsync();
    }

    /// <summary>Five second self check: metadata text and cover must match the device.</summary>
    private void VerifyShownState()
    {
        var session = _session;
        if (session == null) return;
        var state = session.State;
        bool stale =
            !string.Equals(TitleText.Text, string.IsNullOrWhiteSpace(state.Title) ? "—" : state.Title, StringComparison.Ordinal)
            || !string.Equals(ArtistText.Text, state.Artist, StringComparison.Ordinal)
            || !string.Equals(AlbumText.Text, state.Album, StringComparison.Ordinal)
            || !string.Equals(SourceText.Text, state.SourceText, StringComparison.Ordinal)
            || CoverKey(state) != _coverKey;
        if (!stale) return;
        LogBus.Warn("播放信息与设备不一致，已重新同步");
        ApplyState(session);
    }

    /// <summary>Identifies the artwork currently expected from the device.</summary>
    private static string CoverKey(DeviceState state)
        => $"{state.Path}|{state.Title}|{state.Artist}|{state.Album}";

    private async Task RefreshCoverAsync()
    {
        var session = _session;
        if (session == null) return;
        var state = session.State;
        var key = CoverKey(state);
        if (key == _coverKey) return;
        _coverKey = key;
        var bytes = await session.Client.GetCoverAsync();
        if (bytes == null || bytes.Length == 0)
        {
            // No cover in the tags: show the dedicated placeholder, like the
            // Android playback screen does.
            DispatcherQueue.TryEnqueue(() => CoverImage.Source = PlaceholderCover.Image);
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                bitmap.SetSource(stream.AsRandomAccessStream());
                CoverImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                LogBus.Warn("封面加载失败：" + ex.Message);
            }
        });
    }

    private void OnProgressDragStart() => _draggingProgress = true;

    private void OnProgressDragEnd()
    {
        _draggingProgress = false;
        var session = _session;
        if (session == null || session.State.DurationMs <= 0) return;
        var position = (int)(ProgressSlider.Value / 1000d * session.State.DurationMs);
        // PointerReleased and PointerCaptureLost both fire; only act once.
        if (position == _lastSeekMs) return;
        _lastSeekMs = position;
        _ = session.Client.ControlAsync("seek",
            new System.Collections.Generic.Dictionary<string, object> { ["positionMs"] = position });
        LogBus.Info($"跳转到 {position / 1000} 秒");
    }

    private void OnProgressChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // While dragging, only preview the time; the seek is sent on release.
        var session = _session;
        if (!_draggingProgress || session == null || session.State.DurationMs <= 0) return;
        var position = (int)(e.NewValue / 1000d * session.State.DurationMs);
        PositionText.Text = FormatMs(position);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolume || _session == null) return;
        var value = (int)Math.Round(e.NewValue);
        VolumeText.Text = Percent(value, _session.State.VolumeMax);
        if (!_draggingVolume)
        {
            SendVolume(value);
        }
    }

    private void OnVolumeDragEnd()
    {
        if (!_draggingVolume) return;
        _draggingVolume = false;
        SendVolume((int)Math.Round(VolumeSlider.Value));
    }

    private void SendVolume(int value)
    {
        var session = _session;
        if (session == null) return;
        _ = session.Client.ControlAsync("volume",
            new System.Collections.Generic.Dictionary<string, object> { ["value"] = value });
    }

    /// <summary>Volume shown as a percentage of the device's own scale.</summary>
    private static string Percent(int value, int max)
    {
        int top = Math.Max(1, max);
        return $"{(int)Math.Round(value * 100d / top)}%";
    }
    private static string FormatMs(int ms)
    {
        if (ms <= 0) return "0:00";
        var span = TimeSpan.FromMilliseconds(ms);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }

    private void OnTogglePlay(object sender, RoutedEventArgs e)
    {
        _ = _session?.Client.ControlAsync("toggle");
    }

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        _ = _session?.Client.ControlAsync("previous");
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        _ = _session?.Client.ControlAsync("next");
    }

    private void OnRescan(object sender, RoutedEventArgs e)
    {
        _ = _session?.Client.ControlAsync("rescan");
        LogBus.Info("已请求设备重新扫描曲库");
    }
}
