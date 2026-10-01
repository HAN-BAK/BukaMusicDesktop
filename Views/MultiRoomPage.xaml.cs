using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BukaMusicDesktop.Views;

/// <summary>Selects which devices join the multi-room group of this device.</summary>
public sealed partial class MultiRoomPage : Page
{
    private readonly ObservableCollection<MultiRoomDevice> _devices = new();
    private readonly DispatcherTimer _autoRefresh = new() { Interval = TimeSpan.FromSeconds(5) };
    /// <summary>
    /// Names the user just applied. The master connects to a receiver
    /// asynchronously, so the very next status poll still reports it as not
    /// selected; without this the checkbox would visibly flicker off and back on.
    /// </summary>
    private readonly HashSet<string> _pendingTargets = new();
    private readonly Dictionary<string, DateTime> _pendingSince = new();
    /// <summary>When each peer was first missing from a poll (see SyncDevices).</summary>
    private readonly Dictionary<string, DateTime> _missingSince = new();
    /// <summary>Peers confirmed gone (the HTTP probe got no answer).</summary>
    private readonly HashSet<string> _confirmedOffline = new();
    /// <summary>Peers with a probe in flight, so only one runs at a time.</summary>
    private readonly HashSet<string> _probing = new();
    /// <summary>A peer is probed after being missing from one poll (~5 seconds).</summary>
    private const double MissingGraceSeconds = 5;
    /// <summary>Peers of the last poll, replayed after a probe result arrives.</summary>
    private List<MultiRoomDevice> _lastSnapshotDevices = new();
    private Session? _session;
    private bool _loading;

    public MultiRoomPage()
    {
        InitializeComponent();
        DeviceList.ItemsSource = _devices;
        // The device discovers its peers over mDNS; when its network came up late
        // the list can still be empty right after the page opens, so keep polling
        // while the page is visible instead of waiting for a manual refresh.
        _autoRefresh.Tick += (_, _) =>
        {
            if (!_loading) _ = RefreshAsync();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _session = e.Parameter as Session;
        if (_session != null) _session.StateUpdated += OnStateUpdated;
        LogBus.Info("打开多房间同步页");
        // The session is already polling the device, so its state is known right
        // now. Deciding here (instead of after the first status poll comes back)
        // keeps the page from painting the master layout for a moment before the
        // receiver layout replaces it.
        UpdateReceiverMode();
        await RefreshAsync();
        _autoRefresh.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _autoRefresh.Stop();
        if (_session != null) _session.StateUpdated -= OnStateUpdated;
    }

    private void OnStateUpdated()
    {
        DispatcherQueue.TryEnqueue(UpdateReceiverMode);
    }

    /// <summary>
    /// A device that is playing as a multi-room receiver cannot be a master:
    /// hide the master controls and offer only "断开多房间连接".
    /// </summary>
    private void UpdateReceiverMode()
    {
        bool receiver = _session?.State.Source == "REMOTE";
        if (receiver != _receiverMode)
        {
            _receiverMode = receiver;
            LogBus.Info(receiver
                ? "本机进入多房间接收模式：隐藏主控控件，只保留「断开多房间连接」"
                : "本机退出多房间接收模式：恢复主控控件");
        }
        ApplyButton.Visibility = receiver ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = receiver ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = receiver ? Visibility.Collapsed : Visibility.Visible;
        DisconnectReceiverButton.Visibility = receiver ? Visibility.Visible : Visibility.Collapsed;
        // A receiver has nothing to pick: the peer list only makes sense for the
        // master, so the whole card goes away and only the disconnect button stays.
        DeviceCard.Visibility = receiver ? Visibility.Collapsed : Visibility.Visible;
        DisconnectReceiverButton.IsEnabled = !_disconnecting;
        if (receiver)
        {
            string client = _session?.State.ClientName ?? "";
            RoomStatusText.Text = client.Length == 0
                ? Loc.Current.Text("本机正在接收多房间音频")
                : Loc.Current.Text("本机正在作为接收端播放（来自 {0}）", client);
            HintText.Text = "";
        }
    }

    /// <summary>
    /// Asks a peer that vanished from the mDNS list whether it is still there.
    /// Answering over HTTP means it was only a lost discovery reply, so the row
    /// stays; no answer means the device is really gone and the row is dropped.
    /// </summary>
    private async Task VerifyPeerAsync(MultiRoomDevice device)
    {
        bool answered = false;
        string host = device.Addresses.FirstOrDefault(a =>
            System.Net.IPAddress.TryParse(a, out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? "";
        if (host.Length > 0)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(
                    TimeSpan.FromMilliseconds(2500));
                var info = await new BukaClient(new DeviceInfo { Ip = host, Port = 8080 })
                    .GetInfoAsync(cts.Token);
                answered = info != null;
            }
            catch (Exception ex)
            {
                LogBus.Warn($"探测多房间设备 {device.Name} 失败：{ex.Message}");
            }
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            _probing.Remove(device.Name);
            if (answered)
            {
                // Still online: forget the miss and leave the row untouched.
                _missingSince.Remove(device.Name);
                return;
            }
            _missingSince.Remove(device.Name);
            _confirmedOffline.Add(device.Name);
            SyncDevices(_lastSnapshotDevices);
        });
    }

    /// <summary>Receiver side: fade out, tell the master to drop us, then refresh.</summary>
    private async void OnDisconnectReceiver(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null || _disconnecting) return;
        _disconnecting = true;
        HintText.Text = Loc.Current.Text("正在断开…");
        try
        {
            bool ok = await session.Client.ControlAsync("disconnectMultiRoomReceiver");
            if (ok)
            {
                LogBus.Info("已请求断开多房间连接");
            }
            else
            {
                LogBus.Warn("断开多房间连接失败");
            }
        }
        catch (Exception ex)
        {
            LogBus.Error("断开多房间连接失败：" + ex.Message);
        }
        finally
        {
            _disconnecting = false;
            HintText.Text = "";
            UpdateReceiverMode();
        }
    }

    private bool _disconnecting;
    private bool _receiverMode;

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        var session = _session;
        if (session == null) return;
        _loading = true;
        var snapshot = await session.Client.GetMultiRoomAsync();
        MergeKnownAddresses(snapshot);
        DispatcherQueue.TryEnqueue(() =>
        {
            // A device counts as confirmed once the master lists it as a target;
            // only then can the local "just applied" flag be dropped.
            foreach (string target in snapshot.Targets) _pendingTargets.Remove(target);
            // A receiver that never connects must not stay checked forever: after
            // ten seconds the row follows the master's real state again.
            foreach (string name in _pendingTargets.ToList())
            {
                if (_pendingSince.TryGetValue(name, out DateTime since)
                    && (DateTime.Now - since).TotalSeconds > 10)
                {
                    _pendingTargets.Remove(name);
                    _pendingSince.Remove(name);
                }
            }
            foreach (string name in snapshot.Targets) _pendingSince.Remove(name);
            _lastSnapshotDevices = snapshot.Devices;
            SyncDevices(snapshot.Devices);
            EmptyHint.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // As a receiver this device is not the master, so the master text and
            // hints would fight with the receiver text shown by UpdateReceiverMode.
            if (!_receiverMode)
            {
                RoomStatusText.Text = snapshot.ActiveText
                                  + (snapshot.Targets.Count > 0
                                      ? "：" + string.Join("、", snapshot.Targets)
                                      : "");
                var unusable = snapshot.Devices.Count(d => !d.HasIpv4);
                HintText.Text = unusable > 0
                    ? Loc.Current.Text("有 {0} 台设备只报告了 IPv6 地址，勾选后主控仍会尝试连接", unusable)
                    : "";
            }
            _loading = false;
        });
    }

    /// <summary>
    /// Applies a status poll to the list without rebuilding it: rows that are
    /// still there keep their instances (so the ListView does not replay its
    /// add/remove animation every five seconds), only real changes are pushed
    /// through the bindings.
    /// </summary>
    private void SyncDevices(List<MultiRoomDevice> incoming)
    {
        DateTime now = DateTime.Now;
        for (int i = _devices.Count - 1; i >= 0; i--)
        {
            string name = _devices[i].Name;
            if (incoming.Any(d => d.Name == name))
            {
                _missingSince.Remove(name);
                _confirmedOffline.Remove(name);
                continue;
            }
            // mDNS hands the peer list over in bursts: while a receiver is
            // connecting (or the device restarts discovery) one poll can come
            // back empty. Dropping the row on a single empty poll made the whole
            // list disappear and pop back a second later, so a missing peer is
            // first probed over HTTP and only then removed.
            if (_confirmedOffline.Contains(name))
            {
                LogBus.Info($"多房间列表：移除 {name}（无应答）");
                _devices.RemoveAt(i);
                continue;
            }
            if (!_missingSince.TryGetValue(name, out DateTime since))
            {
                _missingSince[name] = now;
                continue;
            }
            if ((now - since).TotalSeconds >= MissingGraceSeconds
                && _probing.Add(name))
            {
                if (_devices[i].HasIpv4)
                {
                    _ = VerifyPeerAsync(_devices[i]);
                }
                else
                {
                    // Nothing to probe over HTTP. Keep IPv6-only peers around
                    // much longer, then drop them.
                    _probing.Remove(name);
                    if ((now - since).TotalSeconds >= MissingGraceSeconds * 6)
                    {
                        _missingSince.Remove(name);
                        LogBus.Info($"多房间列表：移除 {name}（仅 IPv6 且长时间未出现）");
                        _devices.RemoveAt(i);
                    }
                }
            }
        }
        foreach (MultiRoomDevice device in incoming)
        {
            MultiRoomDevice? existing = _devices.FirstOrDefault(d => d.Name == device.Name);
            bool selected = _pendingTargets.Contains(device.Name) || device.Selected == true;
            if (existing == null)
            {
                device.Selected = selected;
                _devices.Add(device);
                LogBus.Info($"多房间列表：加入 {device.Name}（{device.AddressText}）");
                continue;
            }
            if (existing.Apply(device, selected))
            {
                LogBus.Info($"多房间列表：更新 {existing.Name}"
                            + $"（{existing.AddressText}，勾选={selected}）");
            }
        }
    }

    /// <summary>
    /// mDNS sometimes resolves only a device's IPv6 address. The desktop finds
    /// the same devices over UDP broadcast with their IPv4, so that address is
    /// merged in before the row is built.
    /// </summary>
    private static void MergeKnownAddresses(MultiRoomSnapshot snapshot)
    {
        var scanner = MainWindow.Instance?.Scanner;
        if (scanner == null) return;
        foreach (var device in snapshot.Devices)
        {
            if (device.HasIpv4) continue;
            var known = scanner.Devices.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.Ip)
                && string.Equals(d.Name, device.Name, StringComparison.OrdinalIgnoreCase));
            if (known == null) continue;
            var addresses = new System.Collections.Generic.List<string>(device.Addresses);
            if (!addresses.Contains(known.Ip)) addresses.Add(known.Ip);
            device.Addresses = addresses.ToArray();
        }
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var names = _devices.Where(d => d.Selected == true)
            .Select(d => d.Name).ToList();
        // Remember the intent and show it right away; the next poll would
        // otherwise uncheck everything until the receivers are connected.
        _pendingTargets.Clear();
        _pendingSince.Clear();
        foreach (string name in names)
        {
            _pendingTargets.Add(name);
            _pendingSince[name] = DateTime.Now;
        }
        var ok = await session.Client.SetMultiRoomAsync(names);
        if (!ok)
        {
            LogBus.Warn("多房间设置下发失败");
            _pendingTargets.Clear();
            _pendingSince.Clear();
        }
        else if (names.Count == 0)
        {
            LogBus.Info("已断开所有多房间设备");
        }
        else
        {
            LogBus.Success("多房间同步已应用：" + string.Join("、", names));
        }
        await RefreshAsync();
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        _pendingTargets.Clear();
        _pendingSince.Clear();
        await session.Client.SetMultiRoomAsync(Array.Empty<string>());
        LogBus.Info("已断开所有多房间设备");
        await RefreshAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

}
