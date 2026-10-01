using System;
using System.IO;
using System.Threading.Tasks;
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

    public ControlPage()
    {
        InitializeComponent();
        // Slider 内部会把 Pointer 事件标记为已处理，普通事件不会冒泡到页面，
        // 所以这里用 AddHandler(handledEventsToo) 捕获拖动结束（含捕获丢失）。
        DragAwareSlider(ProgressSlider, OnProgressDragStart, OnProgressDragEnd);
        DragAwareSlider(VolumeSlider, () => _draggingVolume = true, OnVolumeDragEnd);
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
        OnStateUpdated();
        _ = RefreshCoverAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
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
        DispatcherQueue.TryEnqueue(() =>
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
                VolumeText.Text = $"{state.Volume} / {state.VolumeMax}";
                _suppressVolume = false;
            }
        });

        _ = RefreshCoverAsync();
    }

    private async Task RefreshCoverAsync()
    {
        var session = _session;
        if (session == null) return;
        var state = session.State;
        var key = $"{state.Title}|{state.Artist}|{state.Album}";
        if (key == _coverKey) return;
        _coverKey = key;
        var bytes = await session.Client.GetCoverAsync();
        if (bytes == null || bytes.Length == 0)
        {
            DispatcherQueue.TryEnqueue(() => CoverImage.Source = null);
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
        VolumeText.Text = $"{value} / {_session.State.VolumeMax}";
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
