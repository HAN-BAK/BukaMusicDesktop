using System;
using System.Collections.Generic;
using System.Linq;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace BukaMusicDesktop.Views;

/// <summary>
/// Full-bleed lyric PV, mirroring the Android lyrics activity: the Sonnet
/// stage owns the whole content area and the header floats on top of it,
/// fading away after a few seconds without pointer movement.
/// </summary>
public sealed partial class LyricsPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hideHeaderTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private Session? _session;
    private string _loadedKey = "";
    private string _loadedCoverKey = "";

    public LyricsPage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        _hideHeaderTimer.Tick += (_, _) =>
        {
            _hideHeaderTimer.Stop();
            FloatHeader.Opacity = 0;
        };
        StageHost.PointerMoved += (_, _) => WakeHeader();
        StageHost.PointerPressed += (_, _) => WakeHeader();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _session = e.Parameter as Session;
        _timer.Start();
        WakeHeader();
        await LoadLyricsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _timer.Stop();
        _hideHeaderTimer.Stop();
        _session = null;
    }

    private void WakeHeader()
    {
        FloatHeader.Opacity = 1;
        _hideHeaderTimer.Stop();
        _hideHeaderTimer.Start();
    }

    private async System.Threading.Tasks.Task LoadLyricsAsync(bool force = false)
    {
        var session = _session;
        if (session == null) return;
        DeviceState state = session.State;
        string key = $"{state.Title}|{state.Artist}|{state.DurationMs}";
        TrackText.Text = string.IsNullOrWhiteSpace(state.Title) ? "—" : $"{state.Title} · {state.Artist}";
        // Song / artist names are protected words for the lyric segmenter, the
        // same hints the Android activity passes to its stage.
        Stage.SetHints(new[] { state.Title, state.Artist, state.Album });
        if (!force && key == _loadedKey) return;
        _loadedKey = key;
        InfoText.Text = Loc.Current.Text("正在读取歌词…");

        LyricSet set = await session.Client.GetLyricsAsync();
        var lines = set.Lines;
        DispatcherQueue.TryEnqueue(() =>
        {
            Stage.SetLyrics(set);
            EmptyHint.Text = lines.Count == 0 ? "" : "";
            InfoText.Text = lines.Count == 0
                ? Loc.Current.Text("没有取到歌词")
                : Loc.Current.Text("{0} 行", lines.Count)
                  + (lines.Any(l => l.HasTranslation) ? Loc.Current.Text(" · 含译文") : "");
        });
        LogBus.Info($"读取歌词：{_linesCount(set)} 行");
        _ = LoadCoverAsync();
    }

    private static int _linesCount(LyricSet set) => set.Lines.Count;

    /// <summary>Cover art for the PV backdrop (blurred by the stage itself).</summary>
    private async System.Threading.Tasks.Task LoadCoverAsync()
    {
        var session = _session;
        if (session == null) return;
        string key = $"{session.State.Title}|{session.State.Artist}";
        if (key == _loadedCoverKey) return;
        _loadedCoverKey = key;
        byte[]? bytes = await session.Client.GetCoverAsync();
        DispatcherQueue.TryEnqueue(() => Stage.SetCover(bytes));
    }

    private void Tick()
    {
        var session = _session;
        if (session == null) return;
        // Reload when the track changed underneath us.
        string key = $"{session.State.Title}|{session.State.Artist}|{session.State.DurationMs}";
        if (key != _loadedKey) _ = LoadLyricsAsync();
        Stage.SetPlayback(session.EstimatedPositionMs(), session.State.Playing);
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        WakeHeader();
        await LoadLyricsAsync(true);
    }
}
