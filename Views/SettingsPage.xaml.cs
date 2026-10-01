using System;
using System.Collections.Generic;
using System.Linq;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace BukaMusicDesktop.Views;

/// <summary>Device settings, mirroring the Android settings screen.</summary>
public sealed partial class SettingsPage : Page
{
    private static readonly string[] Frequencies =
    {
        "31.5", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k",
    };

    private Session? _session;
    private bool _loading;
    private List<EqPreset> _presets = new();
    private readonly List<EqBand> _bands;

    public SettingsPage()
    {
        InitializeComponent();
        _bands = Frequencies.Select(f => new EqBand { Frequency = f }).ToList();
        EqList.ItemsSource = _bands;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _session = e.Parameter as Session;
        await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var session = _session;
        if (session == null) return;
        var settings = await session.Client.GetSettingsAsync();
        if (settings == null)
        {
        SaveStatus.Text = Loc.Current.Text("读取设置失败，请检查设备连接");
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            _loading = true;
            DeviceNameBox.Text = settings.DeviceName;
            FolderBox.Text = settings.MusicFolder;
            AutoPlaySwitch.IsOn = settings.AutoPlayOnStart;
            BalanceSlider.Value = settings.Balance;
            BalanceText.Text = settings.Balance.ToString("0.00");
            AppsButtonSwitch.IsOn = settings.ShowAppsButton;
            OnlineLyricsSwitch.IsOn = settings.OnlineLyrics;
            SelectByTag(ModeCombo, settings.PlayMode);
            SelectByTag(BlurCombo, settings.BlurMode);
            SelectByTag(LanguageCombo, settings.Language);
            for (int i = 0; i < _bands.Count && i < settings.EqGains.Length; i++)
            {
                _bands[i].Gain = settings.EqGains[i];
            }
            _presets = settings.EqPresets ?? new List<EqPreset>();
            UpdatePresetHint();
            LogBus.Info($"均衡器预设：读取到 {_presets.Count} 个（来自设备）");
            _loading = false;
            SaveStatus.Text = "";
        });
        LogBus.Info($"已读取 {session.Device.Name} 的设置");
    }

    private static void SelectByTag(ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem entry && (entry.Tag as string) == tag)
            {
                combo.SelectedItem = entry;
                return;
            }
        }
    }

    private static string? SelectedTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private void OnBalanceChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        BalanceText.Text = e.NewValue.ToString("0.00");
    }

    /// <summary>
    /// Six preset buttons, exactly like the Android equalizer screen: save,
    /// load, delete, export, import and reset-to-default.
    /// </summary>
    private double[] CurrentGains() => _bands.Select(b => Math.Round(b.Gain, 1)).ToArray();

    private void UpdatePresetHint()
    {
        EqPresetHint.Text = _presets.Count == 0
            ? Loc.Current.Text("设备上还没有保存的预设")
            : Loc.Current.Text("设备上的 {0} 个预设：", _presets.Count)
              + string.Join("、", _presets.Select(p => p.Name));
    }

    private async System.Threading.Tasks.Task ReloadPresetsAsync()
    {
        var session = _session;
        if (session == null) return;
        var settings = await session.Client.GetSettingsAsync();
        if (settings == null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            _presets = settings.EqPresets ?? new List<EqPreset>();
            UpdatePresetHint();
        });
    }

    /// <summary>Writes the current curve to the device (same as "保存设置").</summary>
    private async System.Threading.Tasks.Task ApplyGainsAsync()
    {
        var session = _session;
        if (session == null) return;
        await session.Client.ApplySettingsAsync(new Dictionary<string, object>
        {
            ["eqGains"] = CurrentGains(),
        });
    }

    private string NextPresetName()
    {
        for (int i = 1; i < 1000; i++)
        {
            string candidate = Loc.Current.Text("预设{0}", i);
            if (_presets.All(p => p.Name != candidate)) return candidate;
        }
        return Loc.Current.Text("预设");
    }

    private async void OnSavePreset(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var input = new TextBox
        {
            Text = NextPresetName(),
            PlaceholderText = "",
        };
        Controls.MarqueeText.Enhance(input);
        var dialog = new ContentDialog
        {
            Title = Loc.Current.Text("保存预设"),
            Content = input,
            PrimaryButtonText = Loc.Current.Text("保存"),
            CloseButtonText = Loc.Current.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        string name = (input.Text ?? "").Trim();
        if (name.Length == 0)
        {
            SaveStatus.Text = Loc.Current.Text("预设名称不能为空");
            return;
        }
        var result = await session.Client.ControlRawAsync("saveEqPreset",
            new Dictionary<string, object> { ["name"] = name, ["gains"] = CurrentGains() });
        string outcome = result != null && result.Value.TryGetProperty("result", out var value)
            ? value.GetString() ?? "" : "";
        SaveStatus.Text = outcome switch
        {
            "ok" => Loc.Current.Text("预设「{0}」已保存", name),
            "duplicate" => Loc.Current.Text("预设名称已存在"),
            _ => Loc.Current.Text("保存预设失败，请检查连接"),
        };
        LogBus.Info(SaveStatus.Text);
        await ReloadPresetsAsync();
    }

    /// <summary>Shared picker for 加载预设 / 删除预设 (Android shows a list too).</summary>
    private async System.Threading.Tasks.Task<string?> PickPresetAsync(string title)
    {
        if (_presets.Count == 0)
        {
            // Say so in a dialog: the status line at the bottom of the page is
            // easy to miss, so clicking "加载预设" looked like nothing happened.
            SaveStatus.Text = Loc.Current.Text("暂无预设");
            var info = new ContentDialog
            {
                Title = title,
                Content = Loc.Current.Text("暂无预设"),
                CloseButtonText = Loc.Current.Text("确定"),
                XamlRoot = XamlRoot,
            };
            await info.ShowAsync();
            return null;
        }
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = _presets.Select(p => p.Name).ToList(),
            MaxHeight = 320,
            MinWidth = 280,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = list,
            PrimaryButtonText = Loc.Current.Text("确定"),
            CloseButtonText = Loc.Current.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (list.SelectedItem is string name) return name;
        SaveStatus.Text = Loc.Current.Text("没有选择预设");
        return null;
    }

    private async void OnLoadPreset(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        string? name = await PickPresetAsync(Loc.Current.Text("加载预设"));
        if (name == null) return;
        EqPreset? preset = _presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        for (int i = 0; i < _bands.Count && i < preset.Gains.Length; i++)
        {
            _bands[i].Gain = preset.Gains[i];
        }
        // The Android screen applies a loaded preset immediately; do the same.
        await ApplyGainsAsync();
        SaveStatus.Text = Loc.Current.Text("已载入预设「{0}」并写入设备", name);
        LogBus.Success(SaveStatus.Text);
    }

    private async void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        string? name = await PickPresetAsync(Loc.Current.Text("删除预设"));
        if (name == null) return;
        await session.Client.ControlRawAsync("deleteEqPreset",
            new Dictionary<string, object> { ["name"] = name });
        SaveStatus.Text = Loc.Current.Text("预设「{0}」已删除", name);
        LogBus.Info(SaveStatus.Text);
        await ReloadPresetsAsync();
    }

    private async void OnExportPresets(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var raw = await session.Client.ControlRawAsync("exportEqPresets");
        string json = raw != null && raw.Value.TryGetProperty("presets", out var value)
            ? value.GetString() ?? "[]" : "[]";
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = "buka_eq_presets",
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try
        {
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
        SaveStatus.Text = Loc.Current.Text("预设已导出到 {0}", file.Name);
            LogBus.Success(SaveStatus.Text);
        }
        catch (Exception ex)
        {
            SaveStatus.Text = Loc.Current.Text("导出失败：") + ex.Message;
            LogBus.Error(SaveStatus.Text);
        }
    }

    private async void OnImportPresets(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            string text = await Windows.Storage.FileIO.ReadTextAsync(file);
            var raw = await session.Client.ControlRawAsync("importEqPresets",
                new Dictionary<string, object> { ["json"] = text });
            int imported = raw != null && raw.Value.TryGetProperty("imported", out var value)
                ? value.GetInt32() : 0;
        SaveStatus.Text = Loc.Current.Text("预设已导入（{0} 个）", imported);
            LogBus.Success(SaveStatus.Text);
            await ReloadPresetsAsync();
        }
        catch (Exception ex)
        {
            SaveStatus.Text = Loc.Current.Text("导入失败：") + ex.Message;
            LogBus.Error(SaveStatus.Text);
        }
    }

    private async void OnResetEq(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var dialog = new ContentDialog
        {
            Title = Loc.Current.Text("恢复默认"),
            Content = Loc.Current.Text("确定要恢复默认均衡器设置吗？"),
            PrimaryButtonText = Loc.Current.Text("确定"),
            CloseButtonText = Loc.Current.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (EqBand band in _bands) band.Gain = 0;
        await ApplyGainsAsync();
        SaveStatus.Text = Loc.Current.Text("已恢复默认均衡器");
        LogBus.Info(SaveStatus.Text);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session == null) return;
        var payload = new Dictionary<string, object>
        {
            ["deviceName"] = DeviceNameBox.Text?.Trim() ?? "",
            ["musicFolder"] = FolderBox.Text?.Trim() ?? "",
            ["musicFolderDisplay"] = FolderBox.Text?.Trim() ?? "",
            ["playMode"] = SelectedTag(ModeCombo) ?? "SEQUENCE",
            ["autoPlayOnStart"] = AutoPlaySwitch.IsOn,
            ["balance"] = Math.Round(BalanceSlider.Value, 2),
            ["showAppsButton"] = AppsButtonSwitch.IsOn,
            ["blurMode"] = SelectedTag(BlurCombo) ?? "dark",
            ["language"] = SelectedTag(LanguageCombo) ?? "zh",
            ["onlineLyrics"] = OnlineLyricsSwitch.IsOn,
            ["eqGains"] = _bands.Select(b => Math.Round(b.Gain, 1)).ToArray(),
        };
        var ok = await session.Client.ApplySettingsAsync(payload);
        SaveStatus.Text = Loc.Current.Text(ok ? "已保存到设备" : "保存失败，请检查连接");
        if (ok)
        {
            LogBus.Success("设置已保存到设备");
            await LoadAsync();
        }
        else
        {
            LogBus.Error("设置保存失败");
        }
    }

    private async void OnReload(object sender, RoutedEventArgs e) => await LoadAsync();
}
