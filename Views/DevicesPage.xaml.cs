using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace BukaMusicDesktop.Views;

/// <summary>Entry screen: every discovered device is a card; click one to control it.</summary>
public sealed partial class DevicesPage : Page
{
    /// <summary>A device that has not answered for this long disappears from the list.</summary>
    private const double OfflineHideSeconds = 5;
    /// <summary>Staleness that counts as "definitely offline" when a card is clicked.</summary>
    private const double OfflineDialogSeconds = 12;

    private MainWindow? _window;
    private readonly ObservableCollection<DeviceInfo> _visible = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>Cards whose device stopped answering, confirmed over HTTP.</summary>
    private readonly HashSet<string> _confirmedOffline = new();
    /// <summary>Endpoints with an HTTP probe in flight (one probe at a time).</summary>
    private readonly HashSet<string> _probing = new();
    private ContentDialog? _dialog;

    /// <summary>
    /// Hover feedback: the card itself brightens. The GridView item's own
    /// background/border stay transparent - they used to paint a rectangle that
    /// stuck out past the card's margins (and the focus border was black).
    /// </summary>
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Application.Current.Resources["CardHoverBrush"];
        }
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Application.Current.Resources["CardBrush"];
        }
    }

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _refreshTimer.Tick += (_, _) => Rebuild();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = MainWindow.Instance;
        if (_window == null) return;
        DeviceGrid.ItemsSource = _visible;
        _window.Scanner.DevicesChanged += OnDevicesChanged;
        Rebuild();
        _refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        if (_window != null) _window.Scanner.DevicesChanged -= OnDevicesChanged;
    }

    private void OnDevicesChanged()
    {
        DispatcherQueue.TryEnqueue(Rebuild);
    }

    /// <summary>
    /// Keeps the visible cards in sync with the scanner: devices that stopped
    /// answering five seconds ago are dropped, newly seen ones are appended, and
    /// the order of the cards that stay put is preserved.
    /// </summary>
    private void Rebuild()
    {
        if (_window == null) return;
        DateTime now = DateTime.Now;
        foreach (DeviceInfo device in _window.Scanner.Devices)
        {
            // Answering discovery again is proof enough: the card comes back.
            if ((now - device.LastSeen).TotalSeconds <= OfflineHideSeconds)
            {
                _confirmedOffline.Remove(device.EndPoint);
            }
        }
        List<DeviceInfo> alive = _window.Scanner.Devices
            .Where(d => !_confirmedOffline.Contains(d.EndPoint))
            .ToList();
        for (int i = _visible.Count - 1; i >= 0; i--)
        {
            if (!alive.Contains(_visible[i])) _visible.RemoveAt(i);
        }
        foreach (DeviceInfo device in alive)
        {
            if (!_visible.Contains(device)) _visible.Add(device);
        }
        // A card that stopped answering discovery is probed over HTTP before it
        // is hidden: UDP replies get lost now and then, and hiding the card on a
        // single lost reply made the whole grid flash every few seconds.
        foreach (DeviceInfo device in alive)
        {
            if ((now - device.LastSeen).TotalSeconds <= OfflineHideSeconds) continue;
            if (!_probing.Add(device.EndPoint)) continue;
            _ = VerifyAsync(device);
        }
        UpdateEmptyHint(_visible.Count);
    }

    /// <summary>Confirms over HTTP whether a card that went quiet is really gone.</summary>
    private async Task VerifyAsync(DeviceInfo device)
    {
        bool answered = false;
        try
        {
            // Short timeout: this runs while the card is on screen, so it must
            // not keep a dead device listed for the full HTTP timeout.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
            var info = await new BukaClient(device).GetInfoAsync(cts.Token);
            answered = info != null;
            if (answered)
            {
                DispatcherQueue.TryEnqueue(() => device.Touch(info!));
            }
        }
        catch (Exception ex)
        {
            LogBus.Warn($"探测 {device.EndPoint} 失败：{ex.Message}");
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            _probing.Remove(device.EndPoint);
            if (answered) return;
            if (_confirmedOffline.Add(device.EndPoint))
            {
                LogBus.Info($"设备 {device.Name}（{device.EndPoint}）已离线，从列表移除");
            }
            Rebuild();
        });
    }

    private void UpdateEmptyHint(int count)
    {
        // Give the first scan a moment before claiming that nothing was found.
        bool settled = (DateTime.Now - _loadedAt).TotalSeconds > 4;
        EmptyHint.Visibility = count == 0 && settled ? Visibility.Visible : Visibility.Collapsed;
        if (count != _lastVisibleCount)
        {
            _lastVisibleCount = count;
            LogBus.Info(count == 0
                ? "设备列表已更新：当前没有在线设备"
                : $"设备列表已更新：在线 {count} 台{string.Join("、", _visible.Select(d => d.Name))}");
        }
        _window?.SetStatus(count == 0
            ? Loc.Current.Text("未发现设备，正在持续搜索…")
            : Loc.Current.Text("发现 {0} 台设备，点击卡片开始控制", count));
    }

    private int _lastVisibleCount = -1;
    private readonly DateTime _loadedAt = DateTime.Now;

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        LogBus.Info("手动刷新设备列表");
        _window?.Scanner.Start();
    }

    private void OnAddManual(object sender, RoutedEventArgs e)
    {
        var text = ManualBox.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        var port = 8080;
        var host = text;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var parsed))
        {
            host = text[..colon];
            port = parsed;
        }
        _window?.Prefs.AddManual(host, port);
        LogBus.Info($"手动添加设备 {host}:{port}");
        _window?.Scanner.Start();
        ManualBox.Text = "";
    }

    private async void OnDeviceClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            await ConnectAsync(e);
        }
        catch (Exception ex)
        {
            // An async void handler must never let an exception escape: that is
            // what used to close the whole app after the "无法连接" dialog.
            LogBus.Error("打开设备失败：" + ex.Message);
            await ShowDialogAsync(Loc.Current.Text("打开设备失败"), ex.Message);
        }
    }

    private async Task ConnectAsync(ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DeviceInfo device || _window == null) return;
        // A card only stays on screen for five seconds after the device stopped
        // answering; if it is already stale, tell the user instead of trying to
        // connect (that used to end in a dialog plus a crash).
        // A card can be a few seconds stale while its liveness probe is running,
        // so only a clearly dead entry is rejected here (the connection attempt
        // below has its own timeout and error dialog).
        if ((DateTime.Now - device.LastSeen).TotalSeconds > OfflineDialogSeconds)
        {
            LogBus.Warn($"设备 {device.Name}（{device.EndPoint}）已离线");
            await ShowDialogAsync(Loc.Current.Text("设备已离线"),
                Loc.Current.Text("设备 {0} ({1}) 已经离线，暂时无法控制。\n请确认手机/盒子上的 BukaMusic 正在运行，且与本机在同一网络。",
                    device.Name, device.EndPoint));
            Rebuild();
            return;
        }
        var client = new BukaClient(device);
        _window.SetStatus(Loc.Current.Text("正在连接 {0} ({1})…", device.Name, device.EndPoint));
        var info = await client.GetInfoAsync();
        if (info == null)
        {
            LogBus.Error($"连接 {device.EndPoint} 失败：设备无响应");
            _window.SetStatus(Loc.Current.Text("连接 {0} 失败，请确认设备在线", device.EndPoint));
            device.LastSeen = DateTime.MinValue;
            await ShowDialogAsync(Loc.Current.Text("无法连接"),
                Loc.Current.Text("设备 {0} ({1}) 没有响应。\n请确认手机/盒子上的 BukaMusic 正在运行，且与本机在同一网络。",
                    device.Name, device.EndPoint));
            Rebuild();
            return;
        }

        device.Touch(info);
        _window.Prefs.LastDeviceEndPoint = device.EndPoint;
        _window.Prefs.Save();
        LogBus.Success($"已连接 {device.Name} ({device.EndPoint})");
        _window.OpenDevice(client);
    }

    /// <summary>
    /// Shows a modal hint. The dialog is kept in a field and every failure is
    /// caught: a locally created ContentDialog can be collected while it is open,
    /// and an exception inside an async void handler would kill the app.
    /// </summary>
    private async Task ShowDialogAsync(string title, string content)
    {
        try
        {
            _dialog?.Hide();
            _dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = Loc.Current.Text("知道了"),
                XamlRoot = XamlRoot,
            };
            await _dialog.ShowAsync();
            _dialog = null;
        }
        catch (Exception ex)
        {
            LogBus.Warn("提示框显示失败：" + ex.Message);
        }
    }
}
