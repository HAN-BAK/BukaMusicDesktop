using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BukaMusicDesktop.Controls;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace BukaMusicDesktop.Views;

/// <summary>
/// Library browser. Flat order shows the song rows; "按专辑 / 按歌手" first shows
/// a grid of tiles with cover art - like a mainstream streaming client - and
/// clicking one opens that album / artist's songs, which then also become the
/// playback queue so every play mode stays inside the group.
/// </summary>
public sealed partial class LibraryPage : Page
{
    private readonly ObservableCollection<TrackItem> _items = new();
    private readonly List<TrackItem> _all = new();
    private readonly ObservableCollection<GroupTile> _tiles = new();
    private Session? _session;
    private string _groupBy = "none";
    /// <summary>True while the「管理」mode shows tick boxes and counts them.</summary>
    private bool _managing;
    /// <summary>Key of the album / artist currently open (null = tile grid).</summary>
    private string? _openGroup;
    private string _currentPath = "";

    public LibraryPage()
    {
        InitializeComponent();
        TrackList.ItemsSource = _items;
        GroupGrid.ItemsSource = _tiles;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _session = e.Parameter as Session;
        if (_session != null) _session.StateUpdated += OnStateUpdated;
        // Uploads run in the shared queue, not in this page: coming back to the
        // page picks the current state up again instead of showing nothing.
        UploadQueue.Instance.Changed += OnUploadChanged;
        UploadQueue.Instance.Drained += OnUploadDrained;
        ApplyUploadState();
        // Remember how the library was presented last time; the environment
        // variable stays as a debug override.
        string preset = Environment.GetEnvironmentVariable("BUKA_LIBRARY_GROUP")
                        ?? MainWindow.Instance?.Prefs.LibraryGroupBy ?? "none";
        if (preset.Length > 0)
        {
            for (int i = 0; i < GroupCombo.Items.Count; i++)
            {
                if (GroupCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == preset)
                {
                    GroupCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        await ReloadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_session != null) _session.StateUpdated -= OnStateUpdated;
        UploadQueue.Instance.Changed -= OnUploadChanged;
        UploadQueue.Instance.Drained -= OnUploadDrained;
    }

    private void OnUploadChanged() => DispatcherQueue.TryEnqueue(ApplyUploadState);

    /// <summary>The queue drained: pull the device's library in again.</summary>
    private void OnUploadDrained() => DispatcherQueue.TryEnqueue(async () => await ReloadAsync());

    /// <summary>Mirrors the shared upload state into this page's controls.</summary>
    private void ApplyUploadState()
    {
        UploadStatus.Text = UploadQueue.Instance.Status;
        UploadProgress.Value = UploadQueue.Instance.Progress;
        UploadProgress.Visibility = UploadQueue.Instance.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        string secondary = UploadQueue.Instance.SecondaryStatus;
        UploadQueueText.Text = secondary;
        UploadQueueText.Visibility = secondary.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>Keeps the "playing now" marker in sync with the device.</summary>
    private void OnStateUpdated()
    {
        string path = _session?.State.Path ?? "";
        if (path == _currentPath) return;
        _currentPath = path;
        DispatcherQueue.TryEnqueue(RefreshView);
    }

    private async Task ReloadAsync()
    {
        var session = _session;
        if (session == null) return;
        LoadingText.Visibility = Visibility.Visible;
        var tracks = await session.Client.GetLibraryAsync();
        _all.Clear();
        _all.AddRange(tracks.OrderBy(t => t.Display, StringComparer.CurrentCultureIgnoreCase));
        RefreshView();
        LoadingText.Visibility = Visibility.Collapsed;
        LogBus.Info($"读取曲库：{_all.Count} 首");
    }

    /// <summary>Tracks matching the search box.</summary>
    private List<TrackItem> Filtered()
    {
        string query = (SearchBox.Text ?? "").Trim();
        if (query.Length == 0) return new List<TrackItem>(_all);
        return _all.Where(track =>
                track.Display.Contains(query, StringComparison.OrdinalIgnoreCase)
                || track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
                || track.Album.Contains(query, StringComparison.OrdinalIgnoreCase)
                || track.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Rebuilds whichever of the two views is on screen.</summary>
    private void RefreshView()
    {
        foreach (TrackItem item in _all) item.IsCurrent = item.Path == _currentPath;
        List<TrackItem> filtered = Filtered();
        bool browsing = _groupBy != "none";
        if (!browsing) _openGroup = null;

        if (browsing && _openGroup == null)
        {
            // Tile grid: one entry per album / artist.
            var buckets = new Dictionary<string, List<TrackItem>>();
            var order = new List<string>();
            foreach (TrackItem item in filtered)
            {
                string key = GroupKey(item);
                if (!buckets.TryGetValue(key, out List<TrackItem>? bucket))
                {
                    bucket = new List<TrackItem>();
                    buckets[key] = bucket;
                    order.Add(key);
                }
                bucket.Add(item);
            }
            _tiles.Clear();
            foreach (string key in order.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
            {
                List<TrackItem> bucket = buckets[key];
                var tile = new GroupTile
                {
                    Key = key,
                    Title = key,
                    Subtitle = DescribeTile(bucket),
                    CoverPath = bucket[0].Path,
                    Tracks = _groupBy == "album"
                            ? InTrackOrder(bucket).ToList()
                            : bucket,
                };
                _tiles.Add(tile);
                _ = LoadTileCoverAsync(tile);
            }
            CountText.Text = _groupBy == "album"
                ? Loc.Current.Text("{0} 张专辑 · {1} 首", _tiles.Count, filtered.Count)
                : Loc.Current.Text("{0} 位歌手 · {1} 首", _tiles.Count, filtered.Count);
            LogBus.Info(_groupBy == "album"
                ? $"按专辑浏览：{_tiles.Count} 张专辑 / {filtered.Count} 首"
                : $"按歌手浏览：{_tiles.Count} 位歌手 / {filtered.Count} 首");
        }
        else
        {
            // Song rows: the whole library, or the open album / artist.
            IEnumerable<TrackItem> source = filtered;
            if (browsing && _openGroup != null)
            {
                source = filtered.Where(item => GroupKey(item) == _openGroup);
                // Album view only: a disc's songs belong in track order. The
                // artist view and the flat list keep the order they had.
                if (_groupBy == "album") source = InTrackOrder(source);
            }
            _items.Clear();
            foreach (TrackItem item in source) _items.Add(item);
            CountText.Text = Loc.Current.Text("共 {0} 首", _items.Count);
        }

        bool showTiles = browsing && _openGroup == null;
        GroupGrid.Visibility = showTiles ? Visibility.Visible : Visibility.Collapsed;
        TrackList.Visibility = showTiles ? Visibility.Collapsed : Visibility.Visible;
        ListHeaderRow.Visibility = showTiles ? Visibility.Collapsed : Visibility.Visible;
        GroupHeaderBar.Visibility = (!showTiles && browsing && _openGroup != null)
            ? Visibility.Visible : Visibility.Collapsed;
        if (!showTiles && browsing && _openGroup != null)
        {
            GroupTitleText.Text = _openGroup;
            GroupSubtitleText.Text = Loc.Current.Text("{0} 首", _items.Count);
        }
        // The two views have their own selection; only the visible one counts.
        ApplyManageState();
        UpdateDeleteButton();
    }

    /// <summary>Feeds the current manage state into every row / tile.</summary>
    private void ApplyManageState()
    {
        foreach (TrackItem item in _items) item.ShowCheck = _managing;
        foreach (GroupTile tile in _tiles)
        {
            tile.ShowCheck = _managing;
            foreach (TrackItem item in tile.Tracks) item.ShowCheck = _managing;
        }
    }

    private string GroupKey(TrackItem item)
    {
        string key = _groupBy == "album" ? item.Album : item.Artist;
        return string.IsNullOrWhiteSpace(key) ? Loc.Current.Text("未知") : key.Trim();
    }

    /// <summary>
    /// Order of one album's songs: by the file's track number when it has one.
    /// Songs without a number fall to the end, ordered by title.
    /// </summary>
    private static IEnumerable<TrackItem> InTrackOrder(IEnumerable<TrackItem> tracks)
        => tracks.OrderBy(t => t.TrackNo > 0 ? t.TrackNo : int.MaxValue)
                 .ThenBy(t => t.Display, StringComparer.CurrentCultureIgnoreCase);

    private string DescribeTile(List<TrackItem> bucket)
    {
        string count = Loc.Current.Text("{0} 首", bucket.Count);
        if (_groupBy != "album") return count;
        string artist = bucket[0].Artist;
        return string.IsNullOrWhiteSpace(artist) ? count : $"{artist} · {count}";
    }

    private async Task LoadTileCoverAsync(GroupTile tile)
    {
        var session = _session;
        if (session == null) return;
        try
        {
            BitmapImage? cached = CoverCache.Shared.TryGet(tile.CoverPath);
            if (cached != null)
            {
                tile.Cover = cached;
                return;
            }
            // Fetch off the UI thread, then decode back on it: BitmapImage may
            // only be created on the UI thread.
            byte[]? bytes = await CoverCache.Shared.FetchAsync(session.Client, tile.CoverPath);
            if (bytes == null || bytes.Length == 0)
            {
                CoverCache.Shared.MarkMissing(tile.CoverPath);
                LogBus.Warn($"专辑封面下载失败：{tile.Title}（{tile.CoverPath}）");
                // No embedded art anywhere in that album: fall back to the
                // dedicated placeholder instead of an empty tile.
                tile.Cover = PlaceholderCover.Image;
                return;
            }
            BitmapImage? image = await CoverCache.DecodeAsync(bytes);
            if (image == null)
            {
                CoverCache.Shared.MarkMissing(tile.CoverPath);
                LogBus.Warn($"专辑封面解码失败：{tile.Title}（{bytes.Length} 字节）");
                tile.Cover = PlaceholderCover.Image;
                return;
            }
            CoverCache.Shared.Put(tile.CoverPath, image);
            tile.Cover = image;
        }
        catch (Exception)
        {
            // A missing cover falls back to the dedicated placeholder.
            LogBus.Warn($"专辑封面不可用：{tile.Title}");
            tile.Cover = PlaceholderCover.Image;
        }
    }

    private void OnGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        _groupBy = (GroupCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
        if (MainWindow.Instance?.Prefs is { } prefs)
        {
            prefs.LibraryGroupBy = _groupBy;
            prefs.Save();
        }
        _openGroup = null;
        RefreshView();
    }

    private void OnGroupTileClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GroupTile tile) return;
        if (_managing)
        {
            // In manage mode a click ticks the album / artist instead of opening it.
            tile.IsChecked = !tile.IsChecked;
            UpdateDeleteButton();
            return;
        }
        _openGroup = tile.Key;
        RefreshView();
    }

    /// <summary>
    /// Album art inside a rounded tile: the border only rounds the card, so the
    /// picture itself has to be clipped or its square corners poke out.
    /// </summary>
    private void OnCoverImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            RoundedClip.Attach(element, () => 10);
        }
    }

    private void OnBackToGroups(object sender, RoutedEventArgs e)
    {
        _openGroup = null;
        RefreshView();
    }

    private void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        var session = _session;
        if (session == null || e.ClickedItem is not TrackItem track) return;
        if (_managing)
        {
            // In manage mode a click ticks the row instead of playing it.
            track.IsChecked = !track.IsChecked;
            UpdateDeleteButton();
            return;
        }
        // A plain click plays on this PC; the device is used from the right-click menu.
        LocalAudio.Instance.Play(session.Client, TrackList.Visibility == Visibility.Visible
            ? _items
            : _items, track);
        return;
    }

    /// <summary>Right-click target of the menu, remembered while it is open.</summary>
    private TrackItem? _menuTrack;

    private void OnTrackRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TrackItem track)
        {
            _menuTrack = track;
        }
        LogBus.Info($"[曲库] 右键：{_menuTrack?.Display ?? "未识别曲目"}");
    }

    /// <summary>
    /// Builds the context menu when it opens: a plain click already plays on this
    /// PC, so the menu offers the device (and the PC as a reminder).
    /// </summary>
    private void OnTrackMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        if (menu.Target is FrameworkElement target && target.DataContext is TrackItem fromTarget)
        {
            _menuTrack = fromTarget;
        }
        var session = _session;
        menu.Items.Clear();
        if (_menuTrack == null || session == null)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = Loc.Current.Text("暂无"), IsEnabled = false });
            return;
        }
        TrackItem track = _menuTrack;

        var playOnDevice = new MenuFlyoutItem
        {
            Text = Loc.Current.Text("在 {0} 上播放", session.Device.Name),
            Icon = new FontIcon { Glyph = "\uE768" },
        };
        playOnDevice.Click += async (_, _) => await PlayOnDeviceAsync(track);

        var playHere = new MenuFlyoutItem
        {
            Text = Loc.Current.Text("电脑播放"),
            Icon = new FontIcon { Glyph = "\uE7F4" },
        };
        playHere.Click += (_, _) => LocalAudio.Instance.Play(session.Client, _items, track);

        menu.Items.Add(playOnDevice);
        menu.Items.Add(playHere);
        LogBus.Info($"[曲库] 右键菜单：{track.Display}");
    }

    private async System.Threading.Tasks.Task PlayOnDeviceAsync(TrackItem track)
    {
        var session = _session;
        if (session == null) return;
        var payload = new Dictionary<string, object> { ["path"] = track.Path };
        if (_openGroup != null && _items.Count > 0)
        {
            // Playing inside an album / artist hands the whole group over as the
            // queue, so 上一首 / 下一首 and every play mode stay in the album.
            payload["queue"] = _items.Select(t => t.Path).ToArray();
        }
        bool ok = await session.Client.ControlAsync("playTrack", payload);
        LogBus.Info(ok ? $"切换到：{track.Display}" : $"切换失败：{track.Display}");
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshView();

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDeleteButton();
    }

    private void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDeleteButton();
    }

    /// <summary>Rows / tiles ticked in manage mode (only the visible view counts).</summary>
    private List<TrackItem> CheckedTracks() => _items.Where(t => t.IsChecked).ToList();

    private List<GroupTile> CheckedTiles() => _tiles.Where(t => t.IsChecked).ToList();

    /// <summary>「管理」/「完成」: shows the tick boxes, or leaves manage mode.</summary>
    private void OnManage(object sender, RoutedEventArgs e)
    {
        _managing = !_managing;
        if (!_managing)
        {
            foreach (TrackItem item in _items) item.IsChecked = false;
            foreach (GroupTile tile in _tiles) tile.IsChecked = false;
        }
        ManageButton.Content = Loc.Current.Text(_managing ? "完成" : "管理");
        DeleteButton.Visibility = _managing ? Visibility.Visible : Visibility.Collapsed;
        ApplyManageState();
        UpdateDeleteButton();
        LogBus.Info(_managing ? "进入曲库管理模式" : "退出曲库管理模式");
    }

    /// <summary>
    /// The delete button works in both views: individual songs in the list, or
    /// whole albums / artists when the tiles are shown.
    /// </summary>
    private void UpdateDeleteButton()
    {
        int chosen = TrackList.Visibility == Visibility.Visible
            ? CheckedTracks().Count
            : CheckedTiles().Count;
        DeleteButton.IsEnabled = chosen > 0;
        DeleteButton.Content = chosen > 0
            ? Loc.Current.Text("删除所选（{0}）", chosen)
            : Loc.Current.Text("删除所选");
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        bool tiles = TrackList.Visibility != Visibility.Visible;
        // Songs directly, or every song of the ticked albums / artists.
        var selected = tiles
            ? CheckedTiles()
                .SelectMany(tile => tile.Tracks).Distinct().ToList()
            : CheckedTracks();
        if (selected.Count == 0) return;
        string detail = tiles
            ? Loc.Current.Text("删除所选（{0}）", CheckedTiles().Count) + "："
              + string.Join("、", CheckedTiles().Take(3).Select(t => t.Title))
              + (CheckedTiles().Count > 3 ? "…" : "")
              + "\n\n"
            : "";

        var dialog = new ContentDialog
        {
            Title = Loc.Current.Text("删除音乐文件"),
            Content = detail
                      + Loc.Current.Text("将从设备上删除 {0} 个文件，且无法恢复：\n", selected.Count)
                      + string.Join("\n", selected.Take(5).Select(t => "· " + t.Name))
                      + (selected.Count > 5
                          ? Loc.Current.Text("\n… 以及另外 {0} 个文件", selected.Count - 5)
                          : ""),
            PrimaryButtonText = Loc.Current.Text("删除"),
            CloseButtonText = Loc.Current.Text("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        int deleted = await session.Client.DeleteAsync(selected.Select(t => t.Path));
        if (deleted < 0)
        {
            LogBus.Error("删除失败：设备未响应");
        }
        else
        {
            LogBus.Success($"已删除 {deleted} 个文件");
        }
        if (deleted > 0)
        {
            // The device rescans after a delete; waiting for the removed files to
            // disappear keeps the list from showing songs that are already gone.
            var removed = selected.Select(t => t.Path).ToList();
            for (int attempt = 0; attempt < 14; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(900);
                var tracks = await session.Client.GetLibraryAsync();
                bool stillThere = tracks.Any(t => removed.Any(path =>
                    string.Equals(path, t.Path, StringComparison.OrdinalIgnoreCase)));
                if (!stillThere)
                {
                    LogBus.Info("设备已完成曲库刷新");
                    break;
                }
            }
        }
        await ReloadAsync();

    }

    private async void OnUpload(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;

        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        foreach (var extension in new[]
                 {
                     ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".ape", ".wma", ".aif", ".aiff",
                 })
        {
            picker.FileTypeFilter.Add(extension);
        }

        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;

        var items = new List<UploadItem>();
        foreach (StorageFile file in files)
        {
            long size = 0;
            try
            {
                var properties = await file.GetBasicPropertiesAsync();
                size = (long)properties.Size;
            }
            catch (Exception)
            {
                // Size is only used to spot duplicates; a failure is not fatal.
            }
            items.Add(new UploadItem { Path = file.Path, Name = file.Name, Size = size });
        }

        // Picking while a batch is running only lines the files up: the shared
        // queue runs them one batch after another.
        UploadQueue.Instance.Enqueue(session.Client, items);
    }
}
