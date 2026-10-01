using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace BukaMusicDesktop.Core;

/// <summary>Live playback state reported by /api/state.</summary>
public sealed class DeviceState : INotifyPropertyChanged
{
    private string _source = "IDLE";
    private bool _playing;
    private string _title = "";
    private string _artist = "";
    private string _album = "";
    private string _path = "";
    private int _positionMs;
    private int _durationMs;
    private string _mode = "SEQUENCE";
    private string _statusText = "";
    private string _clientName = "";
    private bool _hasCover;
    private int _volume;
    private int _volumeMax = 40;
    private string _screen = "main";

    public string Source { get => _source; set => Set(ref _source, value, nameof(Source), nameof(SourceText)); }
    public bool Playing { get => _playing; set => Set(ref _playing, value); }
    // The device sends Chinese placeholders when a tag is missing; they are
    // translated on the way out so the console never mixes languages.
    public string Title { get => Loc.Current.Text(_title); set => Set(ref _title, value); }
    public string Artist { get => Loc.Current.Tag(_artist, "未知歌手"); set => Set(ref _artist, value); }
    public string Album { get => Loc.Current.Tag(_album, "未知专辑"); set => Set(ref _album, value); }
    /// <summary>File path of the current local track (empty outside local mode).</summary>
    public string Path { get => _path; set => Set(ref _path, value); }
    public int PositionMs { get => _positionMs; set => Set(ref _positionMs, value, nameof(PositionMs), nameof(PositionText)); }
    public int DurationMs { get => _durationMs; set => Set(ref _durationMs, value, nameof(DurationMs), nameof(DurationText)); }
    public string Mode { get => _mode; set => Set(ref _mode, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string ClientName { get => _clientName; set => Set(ref _clientName, value); }
    public bool HasCover { get => _hasCover; set => Set(ref _hasCover, value); }
    public int Volume { get => _volume; set => Set(ref _volume, value); }
    public int VolumeMax { get => _volumeMax; set => Set(ref _volumeMax, value); }
    /// <summary>Screen the device shows: "lyrics" or "main".</summary>
    public string Screen { get => _screen; set => Set(ref _screen, value, nameof(IsLyricsScreen)); }

    public bool IsLyricsScreen => string.Equals(_screen, "lyrics", StringComparison.OrdinalIgnoreCase);

    public string SourceText => Source switch
    {
        "LOCAL" => Loc.Current.Text("本地播放"),
        "AIRPLAY" => "AirPlay",
        "REMOTE" => Loc.Current.Text("多房间接收"),
        _ => Loc.Current.Text("未在播放"),
    };

    public string PositionText => Format(PositionMs);
    public string DurationText => Format(DurationMs);

    private static string Format(int ms)
    {
        // No duration reported by the device: show the "none" placeholder.
        if (ms <= 0) return Loc.Current.Text("暂无");
        var span = TimeSpan.FromMilliseconds(ms);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, params string[] also)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        foreach (var name in also)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

/// <summary>Settings mirror of the Android app (/api/settings).</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private string _deviceName = "";
    private string _musicFolder = "";
    private string _playMode = "SEQUENCE";
    private bool _autoPlayOnStart;
    private double _balance;
    private bool _showAppsButton = true;
    private string _blurMode = "dark";
    private string _language = "zh";
    private bool _onlineLyrics = true;
    private double[] _eqGains = new double[10];

    public string DeviceName { get => _deviceName; set => Set(ref _deviceName, value); }
    public string MusicFolder { get => _musicFolder; set => Set(ref _musicFolder, value); }
    public string PlayMode { get => _playMode; set => Set(ref _playMode, value); }
    public bool AutoPlayOnStart { get => _autoPlayOnStart; set => Set(ref _autoPlayOnStart, value); }
    public double Balance { get => _balance; set => Set(ref _balance, value); }
    public bool ShowAppsButton { get => _showAppsButton; set => Set(ref _showAppsButton, value); }
    public string BlurMode { get => _blurMode; set => Set(ref _blurMode, value); }
    public string Language { get => _language; set => Set(ref _language, value); }
    public bool OnlineLyrics { get => _onlineLyrics; set => Set(ref _onlineLyrics, value); }
    public double[] EqGains { get => _eqGains; set => Set(ref _eqGains, value); }
    /// <summary>Equalizer presets saved on the device.</summary>
    public List<EqPreset> EqPresets { get; set; } = new();
    /// <summary>Band centre frequencies reported by the device (Hz).</summary>
    public double[] EqFrequencies { get; set; } = Array.Empty<double>();

    [JsonIgnore]
    public ObservableCollection<EqBand> Bands { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class EqBand : INotifyPropertyChanged
{
    private double _gain;

    public string Frequency { get; init; } = "";
    /// <summary>Slider value shown under the band, e.g. "+3.5".</summary>
    public string GainText => $"{(_gain >= 0 ? "+" : "")}{_gain:0.0}";

    public double Gain
    {
        get => _gain;
        set
        {
            if (Math.Abs(_gain - value) < 0.001) return;
            _gain = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gain)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GainText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One equalizer preset stored on the device.</summary>
public sealed class EqPreset
{
    public string Name { get; init; } = "";
    public double[] Gains { get; init; } = Array.Empty<double>();
}

/// <summary>One song in the device library.</summary>
public sealed class TrackItem : INotifyPropertyChanged
{
    private bool _isCurrent;

    public string Path { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Name { get; init; } = "";
    // Missing tags come from the device as Chinese placeholders - translated
    // here so the list matches the language of the console.
    private readonly string _title = "";
    private readonly string _artist = "";
    private readonly string _album = "";
    public string Title { get => Loc.Current.Text(_title); init => _title = value; }
    public string Artist { get => Loc.Current.Tag(_artist, "未知歌手"); init => _artist = value; }
    public string Album { get => Loc.Current.Tag(_album, "未知专辑"); init => _album = value; }
    public long DurationMs { get; init; }
    public long SizeBytes { get; init; }

    public string DurationText => DurationMs <= 0
        ? Loc.Current.Text("暂无")
        : TimeSpan.FromMilliseconds(DurationMs) is var span
            ? $"{(int)span.TotalMinutes}:{span.Seconds:D2}"
            : Loc.Current.Text("暂无");

    public string SizeText => SizeBytes switch
    {
        <= 0 => Loc.Current.Text("暂无"),
        >= 1024L * 1024L * 1024L => $"{SizeBytes / 1024d / 1024d / 1024d:0.00} GB",
        >= 1024L * 1024L => $"{SizeBytes / 1024d / 1024d:0.0} MB",
        >= 1024L => $"{SizeBytes / 1024d:0} KB",
        _ => $"{SizeBytes} B",
    };

    public string Display => string.IsNullOrWhiteSpace(Title) ? Name : Title;

    private bool _checked;
    private bool _showCheck;

    /// <summary>Ticked in the library's manage mode.</summary>
    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    /// <summary>Whether the tick box is shown at all (manage mode).</summary>
    public bool ShowCheck
    {
        get => _showCheck;
        set
        {
            if (_showCheck == value) return;
            _showCheck = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckVisibility)));
        }
    }

    public Visibility CheckVisibility => ShowCheck ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True for the song the device is playing right now.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentVisibility)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Microsoft.UI.Xaml.Media.Brush TitleBrush => IsCurrent
        ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current
            .Resources["AccentBrush"]
        : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current
            .Resources["TextPrimaryBrush"];

    /// <summary>Marker shown in front of the row that is playing.</summary>
    public string CurrentMark => IsCurrent ? "\uE768" : "";

    public Microsoft.UI.Xaml.Visibility CurrentVisibility => IsCurrent
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;
}

/// <summary>A group of library rows (by album / by artist).</summary>
public sealed class TrackGroup : List<TrackItem>
{
    public TrackGroup(string key, IEnumerable<TrackItem> items) : base(items)
    {
        Key = key;
    }

    public string Key { get; }

    public string Header => $"{Key}  ·  " + Loc.Current.Text("{0} 首", Count);
}

/// <summary>One device the selected device can sync with (from /api/multicast).</summary>
public sealed class MultiRoomDevice : INotifyPropertyChanged
{
    private string[] _addresses = Array.Empty<string>();
    private bool? _selected;

    public string Name { get; init; } = "";
    public int Port { get; init; }

    public string[] Addresses
    {
        get => _addresses;
        set
        {
            var next = value ?? Array.Empty<string>();
            // mDNS may hand the same addresses back in a different order on every
            // poll; notifying anyway would repaint the row (and its badge) even
            // though nothing changed on screen.
            if (SameAddresses(_addresses, next)) return;
            _addresses = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Addresses)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddressText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIpv4)));
        }
    }

    private static bool SameAddresses(string[] left, string[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Length != right.Length) return false;
        foreach (string address in left)
        {
            if (!right.Contains(address, StringComparer.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Merges a fresh poll into this row. Returns true when something the user
    /// can see actually changed, so the caller can keep the list untouched (and
    /// therefore animation free) when the poll repeated itself.
    /// </summary>
    public bool Apply(MultiRoomDevice fresh, bool? selected)
    {
        bool changed = false;
        if (!SameAddresses(_addresses, fresh.Addresses))
        {
            Addresses = fresh.Addresses;
            changed = true;
        }
        if (_selected != selected)
        {
            Selected = selected;
            changed = true;
        }
        return changed;
    }

    public bool? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AddressText => Addresses.Length == 0
        ? Loc.Current.Text("地址未知")
        : string.Join(" / ", Addresses.OrderBy(a => a.Contains(':') ? 1 : 0));

    /// <summary>True when an IPv4 address was advertised (the usual case).</summary>
    public bool HasIpv4 => Array.Exists(Addresses, a =>
        System.Net.IPAddress.TryParse(a, out var ip) &&
        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

    /// <summary>
    /// Devices that only advertised IPv6 stay selectable: the master tries every
    /// advertised address, so this is a hint rather than a blocker.
    /// </summary>
    public string StatusText => HasIpv4
        ? Loc.Current.Text("可同步")
        : Loc.Current.Text("仅 IPv6 地址");
}

/// <summary>One lyric line received from /api/lyrics.</summary>
public sealed class LyricLineItem : INotifyPropertyChanged
{
    private bool _isCurrent;

    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public string Text { get; init; } = "";
    public string Translation { get; init; } = "";
    /// <summary>Real per-word timings when the source provides them.</summary>
    public List<LyricWord> Words { get; init; } = new();

    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);

    public Visibility TranslationVisibility
        => HasTranslation ? Visibility.Visible : Visibility.Collapsed;

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontSizeValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontWeightValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowPadding)));
        }
    }

    public Microsoft.UI.Xaml.Media.Brush TextBrush => (Microsoft.UI.Xaml.Media.Brush)
        Microsoft.UI.Xaml.Application.Current.Resources[
            IsCurrent ? "AccentBrush" : "TextPrimaryBrush"];

    public double FontSizeValue => IsCurrent ? 20 : 15;

    public Windows.UI.Text.FontWeight FontWeightValue
        => IsCurrent
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;

    public Thickness RowPadding => IsCurrent
        ? new Thickness(10, 8, 10, 8)
        : new Thickness(10, 4, 10, 4);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One word with its own timing inside a lyric line.</summary>
public sealed class LyricWord
{
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>Lyrics of one song plus the layout seed the device used.</summary>
public sealed class LyricSet
{
    public List<LyricLineItem> Lines { get; } = new();
    public string Seed { get; set; } = "sonnet";

    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>Multi-room state of one device.</summary>
public sealed class GroupTile : INotifyPropertyChanged
{
    private Microsoft.UI.Xaml.Media.ImageSource? _cover;
    private bool _checked;
    private bool _showCheck;

    /// <summary>Ticked in the library's manage mode (whole album / artist).</summary>
    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    /// <summary>Whether the tick box is shown at all (manage mode).</summary>
    public bool ShowCheck
    {
        get => _showCheck;
        set
        {
            if (_showCheck == value) return;
            _showCheck = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckVisibility)));
        }
    }

    public Visibility CheckVisibility => ShowCheck ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Album or artist name (also the group key).</summary>
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    /// <summary>Path of the first track, used to fetch the group's cover.</summary>
    public string CoverPath { get; init; } = "";
    public List<TrackItem> Tracks { get; init; } = new();

    /// <summary>Cover art, filled in asynchronously by the page.</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? Cover
    {
        get => _cover;
        set
        {
            _cover = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Multi-room state of one device.</summary>
public sealed class MultiRoomSnapshot
{
    public bool Active { get; set; }
    public List<string> Targets { get; } = new();
    public List<MultiRoomDevice> Devices { get; } = new();

    public string ActiveText => Active
        ? Loc.Current.Text("同步中（{0} 台）", Targets.Count)
        : Loc.Current.Text("未开启多房间同步");
}
