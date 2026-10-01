using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BukaMusicDesktop.Core;

/// <summary>One BukaMusic device on the LAN.</summary>
public sealed class DeviceInfo : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 8080;
    public string Model { get; set; } = "";
    public string Android { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string MusicFolder { get; set; } = "";
    public int TrackCount { get; set; }
    public bool Manual { get; set; }

    private DateTime _lastSeen = DateTime.MinValue;
    [JsonIgnore]
    public DateTime LastSeen
    {
        get => _lastSeen;
        set
        {
            _lastSeen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    /// <summary>Seen within the last 12 seconds -> considered online.</summary>
    [JsonIgnore]
    public bool IsOnline => (DateTime.Now - LastSeen).TotalSeconds < 12;

    [JsonIgnore]
    public string StatusText => Loc.Current.Text(IsOnline ? "在线" : "离线");

    [JsonIgnore]
    public string EndPoint => $"{Ip}:{Port}";

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(Model) ? EndPoint : $"{Model} · Android {Android}";

    [JsonIgnore]
    public string Detail => Loc.Current.Text("{0} · {1} 首 · v{2}",
        MusicFolder, TrackCount, AppVersion);

    [JsonIgnore]
    public Microsoft.UI.Xaml.Media.Brush StatusBrush => IsOnline
        ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SuccessBrush"]
        : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Touch(DeviceInfo other)
    {
        Name = other.Name;
        Port = other.Port;
        Model = other.Model;
        Android = other.Android;
        AppVersion = other.AppVersion;
        MusicFolder = other.MusicFolder;
        TrackCount = other.TrackCount;
        LastSeen = DateTime.Now;
    }
}
