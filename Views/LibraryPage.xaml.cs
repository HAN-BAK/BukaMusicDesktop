using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
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
                    Tracks = bucket,
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
    }

    private string GroupKey(TrackItem item)
    {
        string key = _groupBy == "album" ? item.Album : item.Artist;
        return string.IsNullOrWhiteSpace(key) ? Loc.Current.Text("未知") : key.Trim();
    }

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
                return;
            }
            BitmapImage? image = await CoverCache.DecodeAsync(bytes);
            if (image == null)
            {
                CoverCache.Shared.MarkMissing(tile.CoverPath);
                LogBus.Warn($"专辑封面解码失败：{tile.Title}（{bytes.Length} 字节）");
                return;
            }
            CoverCache.Shared.Put(tile.CoverPath, image);
            tile.Cover = image;
        }
        catch (Exception)
        {
            // A missing cover just leaves the placeholder.
            LogBus.Warn($"专辑封面不可用：{tile.Title}");
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
        _openGroup = tile.Key;
        RefreshView();
    }

    private void OnBackToGroups(object sender, RoutedEventArgs e)
    {
        _openGroup = null;
        RefreshView();
    }

    private async void OnTrackClick(object sender, ItemClickEventArgs e)
    {
        var session = _session;
        if (session == null || e.ClickedItem is not TrackItem track) return;
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
        DeleteButton.IsEnabled = TrackList.SelectedItems.Count > 0;
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var selected = TrackList.SelectedItems.Cast<TrackItem>().ToList();
        if (selected.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = Loc.Current.Text("删除音乐文件"),
            Content = Loc.Current.Text("将从设备上删除 {0} 个文件，且无法恢复：\n", selected.Count)
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

        UploadProgress.Visibility = Visibility.Visible;
        int done = 0;
        int ok = 0;
        foreach (var file in files)
        {
            UploadStatus.Text = Loc.Current.Text("正在上传 ({0}/{1})：{2}",
                done + 1, files.Count, file.Name);
            UploadProgress.Value = done * 100d / files.Count;
            if (await session.Client.UploadAsync(file.Path))
            {
                ok++;
            }
            done++;
        }
        UploadProgress.Value = 100;
        UploadStatus.Text = Loc.Current.Text("上传完成：成功 {0} / {1}", ok, files.Count);
        LogBus.Success($"上传完成：成功 {ok} / {files.Count}");
        await ReloadAsync();
    }
}
