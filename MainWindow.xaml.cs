using System;
using System.Collections.Generic;
using BukaMusicDesktop.Core;
using BukaMusicDesktop.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace BukaMusicDesktop;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public AppPrefs Prefs { get; }
    public DeviceScanner Scanner { get; }

    /// <summary>Language forced by the command line (--lang en / ja / ko).</summary>
    public static string? StartupLanguage { get; set; }

    /// <summary>Endpoint of the device currently opened, so the title survives a reload.</summary>
    private string _deviceName = "";
    private bool _languageReady;

    /// <summary>The shell that is currently shown, so the lyric page can ask it
    /// to step aside when the picture goes full screen.</summary>
    public ShellPage? ActiveShell { get; set; }

    /// <summary>True while the window is in the picture's full-screen mode.</summary>
    public bool IsFullscreen { get; private set; }

    private (PointInt32 Position, SizeInt32 Size)? _windowedBounds;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        LogBus.Dispatcher = DispatcherQueue;
        Prefs = AppPrefs.Load();
        Loc.Current.SetLanguage(Loc.ParseLanguage(StartupLanguage ?? Prefs.UiLanguage));
        Scanner = new DeviceScanner(Prefs);

        SetupTitleBar();
        SetupLanguageBox();
        // Escape leaves the picture's full-screen mode from anywhere in the
        // window, not only while the lyric page itself holds focus.
        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Escape,
        };
        escape.Invoked += (_, e) =>
        {
            if (!IsFullscreen) return;
            SetFullscreen(false);
            e.Handled = true;
        };
        // The accelerator must not paint an "Esc" badge into a hover tooltip:
        // the default Auto placement shows one on whatever the pointer is over.
        WindowRoot.KeyboardAcceleratorPlacementMode =
                Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        WindowRoot.KeyboardAccelerators.Add(escape);

        // Every page is built from Chinese literals in XAML; translating it when
        // the page is loaded keeps the XAML readable and the switch instant. The
        // visual tree only exists once the page is loaded, so Navigated is too
        // early to walk it.
        RootFrame.Navigated += (_, _) =>
        {
            if (RootFrame.Content is not FrameworkElement element) return;
            Localize(element);
        };

        RootFrame.Navigate(typeof(DevicesPage), this);
        // Size/centre once the window is actually shown: DisplayArea reports
        // the real work area only after activation.
        Activated += (_, _) =>
        {
            if (_sizingDone) return;
            _sizingDone = true;
            ApplyDefaultWindowSize();
        };
        Scanner.Start();

        AppWindow.Closing += (_, _) =>
        {
            Prefs.Save();
            Scanner.Stop();
        };

        SetStatus(Loc.Current.Text("正在搜索局域网内的设备…"));
        LogBus.Info("BukaMusic 控制台已启动，开始自动搜索设备");
    }

    /// <summary>Selects the stored language without triggering a reload.</summary>
    private void SetupLanguageBox()
    {
        string code = Loc.LanguageCode(Loc.Current.Language);
        for (int i = 0; i < LanguageBox.Items.Count; i++)
        {
            if (LanguageBox.Items[i] is ComboBoxItem item && (string?)item.Tag == code)
            {
                LanguageBox.SelectedIndex = i;
                break;
            }
        }
        _languageReady = true;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady) return;
        if (LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        UiLanguage language = Loc.ParseLanguage(code);
        if (language == Loc.Current.Language) return;
        Loc.Current.SetLanguage(language);
        Prefs.UiLanguage = code;
        Prefs.Save();
        LogBus.Info($"界面语言已切换为 {code}");
        // Pages hold one-time text, so they are re-created to pick the new
        // language up; the device list/console state is rebuilt from the device.
        ReloadForLanguage();
    }

    private void ReloadForLanguage()
    {
        ApplyLanguage(this);
        if (RootFrame.Content is ShellPage shell && shell.Session != null)
        {
            var client = shell.Session.Client;
            RootFrame.Navigate(typeof(ShellPage), client);
        }
        else
        {
            RootFrame.Navigate(typeof(DevicesPage), this);
        }
    }

    /// <summary>Translates a page (or the window chrome) and reports leftovers.</summary>
    private void ApplyLanguage(object? content)
    {
        var leftovers = new List<string>();
        int pageCount = 0;
        bool firstTime = !ReferenceEquals(content, _translatedPage);
        if (content is DependencyObject page)
        {
            leftovers.AddRange(Loc.Current.Apply(page));
            pageCount = Loc.Current.LastTranslatedCount;
            _translatedPage = content;
        }
        leftovers.AddRange(Loc.Current.Apply(TitleBarGrid));
        leftovers.AddRange(Loc.Current.Apply(StatusBarRoot));
        TitleAppText.Text = Loc.Current.Text("BukaMusic 控制台");
        SetTitleDevice(_deviceName);
        if (firstTime && leftovers.Count > 0)
        {
            LogBus.Warn("以下界面文案没有对应译文：" + string.Join(" | ", leftovers));
        }
        LogBus.Info($"界面语言 {Loc.LanguageCode(Loc.Current.Language)}："
                    + $"{content?.GetType().Name ?? "?"} 翻译 {pageCount} 处界面文案");
    }

    /// <summary>Last page object whose texts were translated (used once per page).</summary>
    private object? _translatedPage;

    /// <summary>
    /// Translates a page as soon as it is loaded. Every page of the console - the
    /// device list, the shell and the pages reached from its navigation - goes
    /// through here, otherwise only the first screen would be translated.
    /// </summary>
    public void Localize(FrameworkElement element)
    {
        if (element.IsLoaded)
        {
            LocalizeNow(element);
            return;
        }
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            element.Loaded -= OnLoaded;
            LocalizeNow(element);
        }
        element.Loaded += OnLoaded;
    }

    private void LocalizeNow(FrameworkElement element)
    {
        ApplyLanguage(element);
        // Buttons and text boxes get the same treatment: their text scrolls when
        // it does not fit. Runs after the translation so the labels are already
        // in the chosen language.
        Controls.MarqueeText.Enhance(element);
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);

        // The taskbar button uses Window.Title (Windows App SDK defaults it to
        // "WinUI Desktop") and the window icon, which has to be set explicitly
        // for an unpackaged app.
        Title = Loc.Current.Text("BukaMusic 控制台");
        ApplyWindowIcon();

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 237, 241, 245);
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 42, 45, 54);
        }

        try
        {
            ApplyDefaultWindowSize();
        }
        catch
        {
            // Resize is best effort (some shells refuse it).
        }
    }

    private bool _sizingDone;

    /// <summary>
    /// Opening size in device-independent pixels (the size the user settled on,
    /// derived from BetterGI's 900x600 layout), centred on the screen it opens on.
    /// </summary>
    private const int DefaultWidthDips = 1176;
    private const int DefaultHeightDips = 756;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Uses the app logo for the title bar and the taskbar button. An unpackaged
    /// WinUI app does not pick the executable icon up on its own, so it is
    /// loaded from the Assets folder next to the executable.
    /// </summary>
    private void ApplyWindowIcon()
    {
        try
        {
            string icon = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(icon))
            {
                AppWindow.SetIcon(icon);
            }
            else
            {
                LogBus.Warn("未找到应用图标文件：" + icon);
            }
        }
        catch (Exception ex)
        {
            LogBus.Warn("窗口图标设置失败：" + ex.Message);
        }
    }

    /// <summary>
    /// Opens the window at the default size (BetterGI's 900x600) centred on the
    /// work area. The size is in device-independent pixels, so it is scaled by
    /// the monitor's DPI before it is handed to the window manager.
    /// </summary>
    private void ApplyDefaultWindowSize()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            double scale = 1.0;
            try
            {
                uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
                if (dpi > 0) scale = dpi / 96.0;
            }
            catch
            {
                // Fall back to 100% scaling when the DPI cannot be read.
            }
            int width = Math.Min(area.Width, (int)Math.Round(DefaultWidthDips * scale));
            int height = Math.Min(area.Height, (int)Math.Round(DefaultHeightDips * scale));
            AppWindow.Resize(new SizeInt32(width, height));
            AppWindow.Move(new PointInt32(
                area.X + Math.Max(0, (area.Width - width) / 2),
                area.Y + Math.Max(0, (area.Height - height) / 2)));
            LogBus.Info($"窗口已排布：工作区 {area.Width}x{area.Height} @({area.X},{area.Y})，"
                        + $"缩放 {scale:0.00}，窗口 {width}x{height}");
        }
        catch (Exception ex)
        {
            LogBus.Warn("窗口排布失败：" + ex.Message);
        }
    }

    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Full-screen picture mode: the window goes borderless over the taskbar and
    /// the console's own chrome (title bar, status bar, side bar) steps aside so
    /// the lyric PV owns the whole screen. Leaving it restores the window size
    /// and position that were in use before.
    /// </summary>
    public void SetFullscreen(bool on)
    {
        if (IsFullscreen == on) return;
        try
        {
            if (on) _windowedBounds = (AppWindow.Position, AppWindow.Size);
            IsFullscreen = on;
            SetChromeVisible(!on);
            ActiveShell?.SetImmersive(on);
            AppWindow.SetPresenter(on
                    ? AppWindowPresenterKind.FullScreen
                    : AppWindowPresenterKind.Overlapped);
            if (!on && _windowedBounds is { } bounds)
            {
                // SetPresenter(Overlapped) hands back a fresh presenter, so put
                // the window where it was.
                AppWindow.Move(bounds.Position);
                AppWindow.Resize(bounds.Size);
            }
            LogBus.Info(on ? "歌词画面已全屏（双击或 Esc 退出）" : "已退出全屏");
        }
        catch (Exception ex)
        {
            IsFullscreen = false;
            SetChromeVisible(true);
            ActiveShell?.SetImmersive(false);
            LogBus.Warn("切换全屏失败：" + ex.Message);
        }
    }

    /// <summary>Shows or hides the custom title bar and the bottom status bar.</summary>
    private void SetChromeVisible(bool visible)
    {
        WindowRoot.RowDefinitions[0].Height = visible ? new GridLength(40) : new GridLength(0);
        WindowRoot.RowDefinitions[2].Height = visible ? new GridLength(30) : new GridLength(0);
    }

    public void SetTitleDevice(string text)
    {
        bool empty = string.IsNullOrWhiteSpace(text);
        _deviceName = empty ? "" : text;
        string app = Loc.Current.Text("BukaMusic 控制台");
        TitleDeviceText.Text = empty ? "" : $"· {text}";
        // Keep the taskbar caption in sync: "BukaMusic 控制台 · <设备名>".
        Title = empty ? app : $"{app} · {text}";
    }

    /// <summary>Opens the control shell for a device (clicked card).</summary>
    public void OpenDevice(BukaClient client)
    {
        RootFrame.Navigate(typeof(ShellPage), client);
    }

    /// <summary>Opens the control shell straight on the lyric page (debug shortcut).</summary>
    public void OpenLyrics(BukaClient client)
    {
        OpenPage("lyrics", client);
    }

    /// <summary>Opens the control shell on a given page (debug shortcut).</summary>
    public void OpenPage(string tag, BukaClient client)
    {
        ShellPage.StartupTag = tag;
        RootFrame.Navigate(typeof(ShellPage), client);
    }

    public void ShowDevices()
    {
        RootFrame.Navigate(typeof(DevicesPage), this);
    }
}
