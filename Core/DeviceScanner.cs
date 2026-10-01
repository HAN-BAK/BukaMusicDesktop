using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BukaMusicDesktop.Core;

namespace BukaMusicDesktop.Core;

/// <summary>
/// Finds devices by broadcasting a small UDP request; every BukaMusic device
/// answers with its /api/info payload plus the HTTP port. Manually added
/// addresses are probed over HTTP as well, so a device on another subnet works.
/// </summary>
public sealed class DeviceScanner : IDisposable
{
    public const int DiscoveryPort = 47101;
    private const string Request = "BUKAMUSIC_DISCOVER";

    private readonly AppPrefs _prefs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<DeviceInfo> Devices { get; } = new();
    public event Action? DevicesChanged;

    public DeviceScanner(AppPrefs prefs)
    {
        _prefs = prefs;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        foreach (var manual in _prefs.ManualDevices)
        {
            if (Devices.All(d => d.EndPoint != manual.EndPoint))
            {
                Devices.Add(manual);
            }
        }

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await ScanOnceAsync(token); }
                catch (Exception ex) { LogBus.Warn($"扫描失败：{ex.Message}"); }
                TryRaise();
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    /// <summary>Broadcasts on every IPv4 interface and collects the replies.</summary>
    private async Task ScanOnceAsync(CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        var payload = Encoding.UTF8.GetBytes(Request);

        var sent = 0;
        foreach (var address in BroadcastAddresses())
        {
            try
            {
                await udp.SendAsync(payload, payload.Length, new IPEndPoint(address, DiscoveryPort));
                sent++;
            }
            catch (Exception ex)
            {
                LogBus.Warn($"向 {address} 广播失败：{ex.Message}");
            }
        }
        if (Interlocked.Increment(ref _scanCount) % 10 == 1)
        {
            LogBus.Info($"发送发现广播（{sent} 个网段），等待设备应答…");
        }

        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        receiveCts.CancelAfter(TimeSpan.FromMilliseconds(1600));
        while (!receiveCts.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(receiveCts.Token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var info = ParseInfo(json);
                if (info != null)
                {
                    info.Ip = result.RemoteEndPoint.Address.ToString();
                    Merge(info);
                }
                else
                {
                    LogBus.Warn($"无法解析来自 {result.RemoteEndPoint} 的应答：{Truncate(json)}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogBus.Warn("解析设备应答失败：" + ex);
            }
        }

        await ProbeManualAsync(token);
    }

    private int _scanCount;

    private static string Truncate(string value, int length = 160)
        => value.Length <= length ? value : value[..length] + "…";

    private async Task ProbeManualAsync(CancellationToken token)
    {
        foreach (var manual in _prefs.ManualDevices)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                var client = new BukaClient(manual);
                var info = await client.GetInfoAsync(token);
                if (info != null)
                {
                    info.Ip = manual.Ip;
                    info.Port = manual.Port;
                    info.Manual = true;
                    Merge(info);
                }
            }
            catch
            {
                // Offline manual entry: keep showing it as offline.
            }
        }
    }

    private void Merge(DeviceInfo info)
        => OnUi(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.EndPoint == info.EndPoint);
            if (existing == null)
            {
                info.LastSeen = DateTime.Now;
                Devices.Add(info);
                LogBus.Success($"发现设备 {info.Name} ({info.EndPoint})");
            }
            else
            {
                existing.Touch(info);
            }
        });

    private void TryRaise() => OnUi(() => DevicesChanged?.Invoke());

    /// <summary>
    /// The device list is bound to the UI, so it may only be touched from the
    /// UI thread (WinUI throws RPC_E_WRONG_THREAD otherwise).
    /// </summary>
    private static void OnUi(Action action)
    {
        var dispatcher = LogBus.Dispatcher;
        if (dispatcher == null || dispatcher.HasThreadAccess) action();
        else dispatcher.TryEnqueue(() => action());
    }

    private static DeviceInfo? ParseInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Get(string name) => root.TryGetProperty(name, out var value)
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString())
            : "";
        int GetInt(string name) => int.TryParse(Get(name), out var value) ? value : 0;
        var port = GetInt("port");
        return new DeviceInfo
        {
            Name = Get("name"),
            Port = port > 0 ? port : 8080,
            Model = Get("model"),
            Android = Get("android"),
            AppVersion = Get("appVersion"),
            MusicFolder = Get("musicFolder"),
            TrackCount = GetInt("trackCount"),
            LastSeen = DateTime.Now,
        };
    }

    /// <summary>Broadcast address of every up IPv4 interface.</summary>
    private static System.Collections.Generic.IEnumerable<IPAddress> BroadcastAddresses()
    {
        var list = new System.Collections.Generic.List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var mask = info.IPv4Mask;
                    if (mask == null) continue;
                    var address = info.Address.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(address[i] | (maskBytes[i] ^ 255));
                    list.Add(new IPAddress(broadcast));
                }
            }
        }
        catch
        {
            // Fall back to the global broadcast address only.
        }
        return list.Distinct();
    }
}
