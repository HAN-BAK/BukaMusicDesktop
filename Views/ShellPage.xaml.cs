using System;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace BukaMusicDesktop.Views;

/// <summary>Console for one device: left navigation + content pages.</summary>
public sealed partial class ShellPage : Page
{
    public Session? Session { get; private set; }

    /// <summary>Navigation entry to select when the shell is opened (debug shortcut).</summary>
    public static string? StartupTag { get; set; }

    /// <summary>Navigation entry to click a moment after opening (debug shortcut).</summary>
    public static string? StartupNavTest { get; set; }

    public ShellPage()
    {
        InitializeComponent();
        // Pages reached from the navigation live in this inner frame; they are
        // translated when they load, exactly like the top level ones.
        ContentFrame.Navigated += (_, e) =>
        {
            if (e.Content is FrameworkElement element) MainWindow.Instance?.Localize(element);
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not BukaClient client) return;

        Session?.Dispose();
        Session = new Session(client);
        Session.ConnectionChanged += OnConnectionChanged;
        Session.StateUpdated += OnStateUpdated;
        Session.Start();

        DeviceNameText.Text = client.Device.Name;
        DeviceEndPointText.Text = client.Device.EndPoint;
        DeviceDetailText.Text = client.Device.Detail;
        MainWindow.Instance?.SetTitleDevice(client.Device.Name);
        MainWindow.Instance?.SetStatus(Loc.Current.Text("已连接 {0} ({1})",
            client.Device.Name, client.Device.EndPoint));

        SelectNav(StartupTag ?? "control");
        StartupTag = null;
        UpdateModeVisibility();
        if (string.IsNullOrWhiteSpace(client.Device.Name)) _ = FillDeviceNameAsync();
        if (StartupNavTest is { Length: > 0 } tag)
        {
            // Debug helper: exercises the same path as a click on the side bar,
            // which is the navigation that has to re-translate a page.
            StartupNavTest = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                LogBus.Info($"调试：模拟点击侧栏「{tag}」");
                SelectNav(tag);
            };
            timer.Start();
        }
    }

    /// <summary>Fills in the name/model when the device was opened by raw IP.</summary>
    private async System.Threading.Tasks.Task FillDeviceNameAsync()
    {
        var session = Session;
        if (session == null) return;
        var info = await session.Client.GetInfoAsync();
        if (info == null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            session.Device.Name = info.Name;
            session.Device.Model = info.Model;
            session.Device.Android = info.Android;
            session.Device.AppVersion = info.AppVersion;
            session.Device.MusicFolder = info.MusicFolder;
            session.Device.TrackCount = info.TrackCount;
            DeviceNameText.Text = info.Name;
            DeviceDetailText.Text = session.Device.Detail;
            MainWindow.Instance?.SetTitleDevice(info.Name);
        });
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Session?.Dispose();
        Session = null;
        MainWindow.Instance?.SetTitleDevice("");
    }

    private void OnConnectionChanged(bool connected)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            OnlineDot.Fill = (Brush)Application.Current.Resources[
                connected ? "SuccessBrush" : "ErrorBrush"];
            MainWindow.Instance?.SetStatus(connected
                ? Loc.Current.Text("已连接 {0} ({1})",
                    Session?.Device.Name, Session?.Device.EndPoint)
                : Loc.Current.Text("与 {0} 的连接中断，正在重试…", Session?.Device.EndPoint));
            if (!connected)
            {
                LogBus.Warn($"与 {Session?.Device.EndPoint} 的连接中断，正在重试");
            }
        });
    }

    private void OnStateUpdated()
    {
        // AirPlay owns the audio: the multi-room page makes no sense there, so
        // its navigation entry is hidden (the Android app does the same).
        // State updates arrive on the polling thread, so the UI work has to be
        // marshalled - touching XAML from there throws and would also abort the
        // remaining listeners (the pages).
        DispatcherQueue.TryEnqueue(UpdateModeVisibility);
    }

    private void UpdateModeVisibility()
    {
        string source = Session?.State.Source ?? "";
        bool airPlay = source == "AIRPLAY";
        bool receiver = source == "REMOTE";
        NavMultiRoom.Visibility = airPlay ? Visibility.Collapsed : Visibility.Visible;
        // A multi-room receiver plays whatever the master sends: its own library
        // is not in charge of the audio, so that entry goes away too.
        NavLibrary.Visibility = receiver ? Visibility.Collapsed : Visibility.Visible;
        if (airPlay && ContentFrame.CurrentSourcePageType == typeof(MultiRoomPage))
        {
            SelectNav("control");
        }
        if (receiver && ContentFrame.CurrentSourcePageType == typeof(LibraryPage))
        {
            SelectNav("control");
        }
    }

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string tag)
        {
            SelectNav(tag);
        }
    }

    private void SelectNav(string tag)
    {
        NavControl.IsChecked = tag == "control";
        NavLyrics.IsChecked = tag == "lyrics";
        NavMultiRoom.IsChecked = tag == "multicast";
        NavLibrary.IsChecked = tag == "library";
        NavSettings.IsChecked = tag == "settings";
        NavLogs.IsChecked = tag == "logs";
        NavAbout.IsChecked = tag == "about";

        var page = tag switch
        {
            "lyrics" => typeof(LyricsPage),
            "multicast" => typeof(MultiRoomPage),
            "library" => typeof(LibraryPage),
            "settings" => typeof(SettingsPage),
            "logs" => typeof(LogsPage),
            "about" => typeof(AboutPage),
            _ => typeof(ControlPage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, Session);
        }
    }

    private async void OnOpenWeb(object sender, RoutedEventArgs e)
    {
        if (Session == null) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(Session.Client.WebUrl));
        }
        catch (Exception ex)
        {
            LogBus.Warn("无法打开浏览器：" + ex.Message);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.ShowDevices();
    }

}
