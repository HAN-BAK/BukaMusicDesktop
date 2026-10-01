using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BukaMusicDesktop.Core;

/// <summary>Local settings of the desktop app itself (remembered devices).</summary>
public sealed class AppPrefs
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BukaMusicDesktop");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public List<DeviceInfo> ManualDevices { get; set; } = new();
    public string? LastDeviceEndPoint { get; set; }
    public bool AutoConnectLast { get; set; } = true;
    /// <summary>Library presentation remembered from last time (none/album/artist).</summary>
    public string LibraryGroupBy { get; set; } = "none";
    /// <summary>UI language of the console (zh/en/ja/ko).</summary>
    public string UiLanguage { get; set; } = "zh";

    public static AppPrefs Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var prefs = JsonSerializer.Deserialize<AppPrefs>(json);
                if (prefs != null)
                {
                    prefs.ManualDevices ??= new List<DeviceInfo>();
                    return prefs;
                }
            }
        }
        catch
        {
            // Corrupt file: start fresh.
        }
        return new AppPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Ignore: preferences are best effort.
        }
    }

    public void AddManual(string host, int port)
    {
        var ip = host.Trim();
        if (ip.Length == 0) return;
        foreach (var device in ManualDevices)
        {
            if (device.Ip == ip && device.Port == port) return;
        }
        ManualDevices.Add(new DeviceInfo
        {
            Name = ip,
            Ip = ip,
            Port = port,
            Manual = true,
        });
        Save();
    }

    public void RemoveManual(DeviceInfo device)
    {
        ManualDevices.RemoveAll(d => d.EndPoint == device.EndPoint);
        Save();
    }
}
